using System.Diagnostics;
using System.Text.Json;
using Jint;
using Jint.Native;
using Jint.Native.Object;
using Jint.Runtime;
using Xapper.McpServer.Infrastructure;
using Xapper.Protocol;

namespace Xapper.McpServer.Scripting;

/// <summary>
/// Jint 엔진을 만들고 제한을 걸어 스크립트를 실행한다. 전역 xapper·log·fail·sleep 을 등록하고,
/// 예외를 결과 종류로 바꾼다. 강제 중단(시간·문장·메모리)은 Jint 제약으로 건다.
/// </summary>
internal sealed class ScriptRunner
{
    #region Fields

    private const int MaxStatements = 1_000_000;
    private const int MemoryLimitBytes = 64 * 1024 * 1024;
    private const int RecursionLimit = 64;

    /// <summary>
    /// 세션의 PascalCase CLR 메서드를 camelCase JS API 로 감싸는 부트스트랩. Jint 4.16.3 에서 내부
    /// DelegateWrapper·PropertyDescriptor 는 접근이 막혀 있어, 가장 버전에 안전한 방식으로 JS 쉼(shim)을 쓴다.
    /// </summary>
    private const string XapperShim =
        "var xapper = {" +
        "find:(s)=>__xapper.Find(s), one:(s)=>__xapper.One(s)," +
        "click:(t,o)=>__xapper.Click(t,o), doubleClick:(t,o)=>__xapper.DoubleClick(t,o)," +
        "rightClick:(t,o)=>__xapper.RightClick(t,o), wheel:(t,n,o)=>__xapper.Wheel(t,n,o)," +
        "drag:(a,b,o)=>__xapper.Drag(a,b,o), type:(t,x,o)=>__xapper.Type(t,x,o)," +
        "key:(k,o)=>__xapper.Key(k,o), select:(t,o)=>__xapper.Select(t,o)," +
        "toggle:(t)=>__xapper.Toggle(t), expand:(t,e)=>__xapper.Expand(t,e)," +
        "scroll:(t,o)=>__xapper.Scroll(t,o), get:(t,p)=>__xapper.Get(t,p)," +
        "assert:(t,p,e,o)=>__xapper.Assert(t,p,e,o), waitUntil:(s,o)=>__xapper.WaitUntil(s,o)," +
        "snapshot:(o)=>__xapper.Snapshot(o), screenshot:(o)=>__xapper.Screenshot(o)," +
        "notice:{ show:(m)=>__xapper.NoticeShow(m), hide:()=>__xapper.NoticeHide() } };";

    private readonly Func<IpcMessage, CancellationToken, Task<IpcMessage>> _send;
    private readonly int _processId;
    private readonly IOperatorNotice? _notice;

    #endregion

    #region Constructor

    /// <summary><see cref="ScriptRunner"/> 를 만듭니다.</summary>
    /// <param name="send">IPC 요청 전송 함수(활성 세션의 클라이언트를 감싼다).</param>
    /// <param name="processId">알림을 놓을 대상 프로세스.</param>
    /// <param name="notice">실제 마우스 폴백 시 배너를 올리는 안전망. 없으면 null.</param>
    public ScriptRunner(Func<IpcMessage, CancellationToken, Task<IpcMessage>> send, int processId, IOperatorNotice? notice)
    {
        _send = send;
        _processId = processId;
        _notice = notice;
    }

    #endregion

    #region Public Methods

    /// <summary>스크립트를 실행하고 결과를 돌려줍니다. 절대 예외를 밖으로 던지지 않는다.</summary>
    public async Task<ScriptResult> RunAsync(string script, int timeoutMs, CancellationToken ct)
    {
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeoutMs);
        var runCt = timeoutCts.Token;

        var session = new ScriptSession(_send, _processId, _notice, runCt);
        var stopwatch = Stopwatch.StartNew();

        // Jint 는 CPU 바운드라 스레드풀에서 돌린다. 시간 제한은 취소 토큰으로 엔진에 전달한다.
        return await Task.Run(() => Execute(script, session, runCt, ct, stopwatch), CancellationToken.None);
    }

    #endregion

    #region Private Methods

    /// <summary>엔진을 만들어 스크립트를 실행하고, 성공·실패·타임아웃·문법오류를 결과로 만든다.</summary>
    private ScriptResult Execute(string script, ScriptSession session, CancellationToken runCt, CancellationToken outerCt, Stopwatch stopwatch)
    {
        try
        {
            // 엔진 준비도 try 안에 둔다 — RunAsync 는 어떤 경우에도 예외를 밖으로 내지 않는다는 계약을 지키기 위함.
            var engine = new Engine(options =>
            {
                options.CancellationToken(runCt);
                options.MaxStatements(MaxStatements);
                options.LimitMemory(MemoryLimitBytes);
                options.LimitRecursion(RecursionLimit);
            });

            engine.SetValue("__xapper", session);
            engine.Execute(XapperShim);
            engine.SetValue("log", new Action<JsValue>(v => session.Log(Stringify(v))));
            engine.SetValue("fail", new Action<string>(session.Fail));
            engine.SetValue("sleep", new Action<int>(session.Sleep));

            var completion = engine.Evaluate(script);
            stopwatch.Stop();
            return new ScriptResult
            {
                Outcome = ScriptOutcome.Ok,
                Elapsed = stopwatch.Elapsed,
                ActionCount = session.Trace.Count,
                Logs = session.Logs,
                ReturnValueJson = completion.IsUndefined() ? null : Serialize(completion)
            };
        }
        catch (ScriptFailure failure)
        {
            stopwatch.Stop();
            return Failed(ScriptOutcome.Fail, session, stopwatch, 0, failure.Api, failure.Message);
        }
        catch (Exception cancel) when (cancel is OperationCanceledException or ExecutionCanceledException or StatementsCountOverflowException)
        {
            stopwatch.Stop();
            // 취소·문장 상한 모두 폭주 방지 장치다. 바깥 토큰이 취소된 게 아니면 시간 초과로 본다.
            var outcome = outerCt.IsCancellationRequested ? ScriptOutcome.Fail : ScriptOutcome.Timeout;
            return Failed(outcome, session, stopwatch, 0, "timeout", $"stopped after {stopwatch.ElapsedMilliseconds} ms");
        }
        catch (JavaScriptException jsError)
        {
            stopwatch.Stop();
            var line = jsError.Location.Start.Line;
            if (IsSyntaxError(jsError))
            {
                return new ScriptResult
                {
                    Outcome = ScriptOutcome.SyntaxError,
                    Elapsed = stopwatch.Elapsed,
                    FailureLine = line > 0 ? line : 1,
                    FailureMessage = jsError.Message
                };
            }

            return Failed(ScriptOutcome.Fail, session, stopwatch, line, "script", jsError.Message);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            return Failed(ScriptOutcome.Fail, session, stopwatch, 0, "script", ex.Message);
        }
    }

    /// <summary>실패 계열 결과를 만든다(추적·로그·소요 시간 포함).</summary>
    private static ScriptResult Failed(ScriptOutcome outcome, ScriptSession session, Stopwatch stopwatch, int line, string api, string message)
        => new()
        {
            Outcome = outcome,
            Elapsed = stopwatch.Elapsed,
            ActionCount = session.Trace.Count,
            Logs = session.Logs,
            FailureLine = line,
            FailureApi = api,
            FailureMessage = message,
            Trace = session.Trace
        };

    /// <summary>Jint 는 파싱 오류도 SyntaxError 를 담은 JavaScriptException 으로 던진다. 그 이름으로 구분한다.</summary>
    private static bool IsSyntaxError(JavaScriptException error)
    {
        if (error.Error is ObjectInstance instance)
            return instance.Get("name").ToString() == "SyntaxError";
        return false;
    }

    private static string Stringify(JsValue value) => value.IsString() ? value.AsString() : value.ToString();

    /// <summary>반환값을 JSON 으로 직렬화한다. 실패하면 값의 문자열 형태로 대체한다.</summary>
    private static string Serialize(JsValue value)
    {
        var obj = value.ToObject();
        try
        {
            return JsonSerializer.Serialize(obj);
        }
        catch (Exception)
        {
            return Stringify(value);
        }
    }

    #endregion
}

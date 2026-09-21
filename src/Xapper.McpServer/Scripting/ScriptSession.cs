using System.Globalization;
using System.Text.Json;
using Jint.Native;
using Xapper.McpServer.Infrastructure;
using Xapper.McpServer.Tools;
using Xapper.Protocol;
using Xapper.Protocol.Messages.Responses;

namespace Xapper.McpServer.Scripting;

/// <summary>
/// 스크립트 전역 <c>xapper</c> 의 구현. 각 메서드는 기존 도구와 같은 IPC 요청을 만들어 보내고, 응답을 문자열로
/// 돌려주거나(오류·Pending 이면) <see cref="ScriptFailure"/> 를 던진다. 스크립트는 동기 API 이므로 전송을 동기 대기한다.
/// 성공한 호출은 최근 5개까지 추적에 남겨 실패 시 맥락을 보여준다.
/// </summary>
internal sealed class ScriptSession
{
    #region Fields

    private const int TraceCapacity = 5;
    private const int SnapshotLimit = 8 * 1024;

    private static int _screenshotCounter;

    private readonly Func<IpcMessage, CancellationToken, Task<IpcMessage>> _send;
    private readonly int _processId;
    private readonly IOperatorNotice? _notice;
    private readonly CancellationToken _ct;
    private readonly LinkedList<string> _trace = new();
    private readonly List<string> _logs = new();

    #endregion

    #region Constructor

    /// <summary><see cref="ScriptSession"/> 를 만듭니다.</summary>
    /// <param name="send">IPC 요청 전송 함수(활성 세션의 클라이언트를 감싼다).</param>
    /// <param name="processId">알림을 놓을 대상 프로세스. 알림이 없으면 무시된다.</param>
    /// <param name="notice">실제 마우스 폴백 시 배너를 올리는 안전망. 없으면 null.</param>
    /// <param name="ct">전체 실행 취소 토큰(러너의 시간 제한).</param>
    public ScriptSession(Func<IpcMessage, CancellationToken, Task<IpcMessage>> send, int processId, IOperatorNotice? notice, CancellationToken ct)
    {
        _send = send;
        _processId = processId;
        _notice = notice;
        _ct = ct;
    }

    #endregion

    #region Public Properties

    /// <summary>스크립트가 log() 로 남긴 줄들.</summary>
    public IReadOnlyList<string> Logs => _logs;

    /// <summary>실패 시 보여줄 최근 액션 추적(오래된 것부터).</summary>
    public IReadOnlyList<string> Trace => _trace.ToList();

    #endregion

    #region Element Tools

    /// <summary>셀렉터로 요소를 찾습니다. 0개면 빈 배열.</summary>
    public object[] Find(string selector)
    {
        if (!SelectorParser.TryParse(selector, out var fields, out var error))
            throw new ScriptFailure("find", error);

        var response = Send("find", new { name = fields.Name, automationId = fields.AutomationId, type = fields.Type, text = fields.Text });
        var result = IpcSerializer.DeserializePayload<FindElementResponse>(PayloadOf(response));
        Record("find", $"{selector} -> {result.Matches.Count}");
        return result.Matches
            .Select(m => (object)new ScriptElement(this, m.Ref, m.Type, m.Name, m.AutomationId, m.Text))
            .ToArray();
    }

    /// <summary>정확히 하나 맞는 요소를 돌려줍니다. 0개나 여러 개면 예외.</summary>
    public ScriptElement One(string selector)
    {
        var matches = Find(selector);
        if (matches.Length != 1)
            throw new ScriptFailure("one", $"selector \"{selector}\" matched {matches.Length} elements; expected exactly one.");
        return (ScriptElement)matches[0];
    }

    #endregion

    #region Action Tools

    /// <summary>요소를 클릭합니다. options: {x, y, modifiers, timeout, allowFail}.</summary>
    public string Click(object target, JsValue? options)
    {
        var (@ref, sel) = TargetArgument.Resolve(target);
        var o = Options.From(options);
        var response = Send("click", new { @ref, target = sel, x = o.X, y = o.Y, modifiers = o.Modifiers, doubleClick = false, timeout = o.Timeout });
        return Finish("click", response, o.AllowFail, $"click {Describe(target)}");
    }

    /// <summary>요소를 더블클릭합니다(기본 x/y = 0.5).</summary>
    public string DoubleClick(object target, JsValue? options)
    {
        var (@ref, sel) = TargetArgument.Resolve(target);
        var o = Options.From(options);
        var response = Send("click", new { @ref, target = sel, x = o.X ?? 0.5, y = o.Y ?? 0.5, modifiers = o.Modifiers, doubleClick = true, timeout = o.Timeout });
        return Finish("doubleClick", response, o.AllowFail, $"doubleClick {Describe(target)}");
    }

    /// <summary>요소를 우클릭합니다.</summary>
    public string RightClick(object target, JsValue? options)
    {
        var (@ref, sel) = TargetArgument.Resolve(target);
        var o = Options.From(options);
        var response = Send("rightclick", new { @ref, target = sel, x = o.X, y = o.Y, modifiers = o.Modifiers, timeout = o.Timeout });
        return Finish("rightClick", response, o.AllowFail, $"rightClick {Describe(target)}");
    }

    /// <summary>요소 위 한 지점에서 휠을 굴립니다. options: {x, y, modifiers, timeout, allowFail}.</summary>
    public string Wheel(object target, int notches, JsValue? options)
    {
        var (@ref, sel) = TargetArgument.Resolve(target);
        var o = Options.From(options);
        var response = Send("wheel", new { @ref, target = sel, notches, x = o.X, y = o.Y, modifiers = o.Modifiers, timeout = o.Timeout });
        return Finish("wheel", response, o.AllowFail, $"wheel {Describe(target)}");
    }

    /// <summary>두 요소(또는 ref) 사이를 드래그합니다. 셀렉터 문자열은 드래그 IPC 가 지원하지 않아 예외.</summary>
    public string Drag(object from, object to, JsValue? options)
    {
        var (fromRef, fromSel) = TargetArgument.Resolve(from);
        var (toRef, toSel) = TargetArgument.Resolve(to);
        if (fromSel is not null || toSel is not null)
            throw new ScriptFailure("drag", "drag needs element refs or find() results, not selector strings");

        var o = Options.From(options);
        var response = Send("drag", new { sourceRef = fromRef, targetRef = toRef, modifiers = o.Modifiers, timeout = o.Timeout });
        return Finish("drag", response, o.AllowFail, $"drag {Describe(from)} -> {Describe(to)}");
    }

    /// <summary>Selector 컨트롤에서 항목을 선택합니다. options: {text, index, timeout, allowFail}.</summary>
    public string Select(object target, JsValue? options)
    {
        var (@ref, sel) = TargetArgument.Resolve(target);
        var o = Options.From(options);
        var response = Send("select", new { @ref, target = sel, itemText = o.Text, itemIndex = o.Index, timeout = o.Timeout });
        return Finish("select", response, o.AllowFail, $"select {Describe(target)}");
    }

    /// <summary>토글 상태를 전환합니다.</summary>
    public string Toggle(object target)
    {
        var (@ref, sel) = TargetArgument.Resolve(target);
        var response = Send("toggle", new { @ref, target = sel, timeout = (int?)null });
        return Finish("toggle", response, false, $"toggle {Describe(target)}");
    }

    /// <summary>TreeViewItem/Expander 를 확장 또는 축소합니다.</summary>
    public string Expand(object target, bool expand)
    {
        var (@ref, sel) = TargetArgument.Resolve(target);
        var response = Send("expand", new { @ref, target = sel, expand, timeout = (int?)null });
        return Finish("expand", response, false, $"expand {Describe(target)}");
    }

    /// <summary>ScrollViewer 의 스크롤 위치를 바꿉니다. options: {h, v, timeout, allowFail}(0~1, 없으면 유지).</summary>
    public string Scroll(object target, JsValue? options)
    {
        var (@ref, sel) = TargetArgument.Resolve(target);
        var o = Options.From(options);
        var response = Send("scroll", new { @ref, target = sel, horizontalPercent = o.H ?? -1, verticalPercent = o.V ?? -1, timeout = o.Timeout });
        return Finish("scroll", response, o.AllowFail, $"scroll {Describe(target)}");
    }

    /// <summary>요소에 텍스트를 입력합니다. options: {clear, timeout, allowFail}.</summary>
    public string Type(object target, string text, JsValue? options)
    {
        var (@ref, sel) = TargetArgument.Resolve(target);
        var o = Options.From(options);
        var response = Send("type", new { @ref, target = sel, text, clear = o.Clear ?? true, timeout = o.Timeout });
        return Finish("type", response, o.AllowFail, $"type {Describe(target)}");
    }

    /// <summary>키를 보냅니다. options: {modifiers, timeout, allowFail}. 대상 없이 포커스 요소로.</summary>
    public string Key(string keys, JsValue? options)
    {
        var o = Options.From(options);
        var response = Send("key", new { key = keys, modifiers = o.Modifiers, timeout = o.Timeout });
        return Finish("key", response, o.AllowFail, $"key \"{keys}\"");
    }

    /// <summary>특정 요소에 키를 보냅니다(요소 축약 메서드용).</summary>
    public string KeyOn(int @ref, string keys, JsValue? options)
    {
        var o = Options.From(options);
        var response = Send("key", new { key = keys, modifiers = o.Modifiers, @ref, timeout = o.Timeout });
        return Finish("key", response, o.AllowFail, $"key \"{keys}\" on ref={@ref}");
    }

    #endregion

    #region Reference Overloads

    /// <summary>요소 ref 를 클릭합니다(요소 축약 메서드용).</summary>
    public string Click(int @ref, JsValue? options) => Click((object)@ref, options);

    /// <summary>요소 ref 를 더블클릭합니다(요소 축약 메서드용).</summary>
    public string DoubleClick(int @ref, JsValue? options) => DoubleClick((object)@ref, options);

    /// <summary>요소 ref 를 우클릭합니다(요소 축약 메서드용).</summary>
    public string RightClick(int @ref, JsValue? options) => RightClick((object)@ref, options);

    /// <summary>요소 ref 에 텍스트를 입력합니다(요소 축약 메서드용).</summary>
    public string Type(int @ref, string text, JsValue? options) => Type((object)@ref, text, options);

    /// <summary>요소 ref 의 프로퍼티를 읽습니다(요소 축약 메서드용).</summary>
    public object? Get(int @ref, string prop) => Get((object)@ref, prop);

    /// <summary>요소 ref 의 프로퍼티를 단언합니다(요소 축약 메서드용).</summary>
    public bool Assert(int @ref, string prop, string expected, JsValue? options) => Assert((object)@ref, prop, expected, options);

    #endregion

    #region Query Tools

    /// <summary>프로퍼티 값을 읽어 숫자/불리언/문자열/null 로 돌려줍니다.</summary>
    public object? Get(object target, string prop)
    {
        var (@ref, sel) = TargetArgument.Resolve(target);
        var response = Send("getProperty", new { @ref, target = sel, propertyName = prop });
        var result = IpcSerializer.DeserializePayload<PropertyResponse>(PayloadOf(response));
        Record("get", $"{Describe(target)} {prop}");
        return Convert(result.Value);
    }

    /// <summary>프로퍼티를 단언합니다. options.timeout 이 있으면 100ms 간격 재시도.</summary>
    public bool Assert(object target, string prop, string expected, JsValue? options)
    {
        var o = Options.From(options);
        var deadline = o.Timeout is int t ? Environment.TickCount64 + t : 0L;
        while (true)
        {
            var (@ref, sel) = TargetArgument.Resolve(target);
            var response = Send("assert", new { @ref, target = sel, property = prop, expected });
            var result = IpcSerializer.DeserializePayload<ActionResponse>(PayloadOf(response));
            if (result.Success)
            {
                Record("assert", $"{Describe(target)} {prop}=={expected}");
                return true;
            }
            if (deadline == 0 || Environment.TickCount64 >= deadline)
                throw new ScriptFailure("assert", result.Message ?? result.Error ?? $"{prop} != {expected}");
            Sleep(100);
        }
    }

    /// <summary>요소가 나타날 때까지 기다립니다(폴링 100ms).</summary>
    public ScriptElement WaitUntil(string selector, JsValue? options)
    {
        var o = Options.From(options);
        var deadline = Environment.TickCount64 + (o.Timeout ?? 5000);
        while (true)
        {
            var matches = Find(selector);
            if (matches.Length > 0)
            {
                Record("waitUntil", $"{selector} appeared");
                return (ScriptElement)matches[0];
            }
            if (Environment.TickCount64 >= deadline)
                throw new ScriptFailure("waitUntil", $"selector \"{selector}\" did not appear within {o.Timeout ?? 5000} ms.");
            Sleep(100);
        }
    }

    /// <summary>비주얼 트리 스냅샷 텍스트를 돌려줍니다. options: {maxDepth}. 8KB 를 넘으면 잘라 붙인다.</summary>
    public string Snapshot(JsValue? options)
    {
        var o = Options.From(options);
        var response = Send("snapshot", new { rootRef = (int?)null, maxDepth = o.MaxDepth ?? 5 });
        var payload = PayloadOf(response);
        Record("snapshot", $"depth {o.MaxDepth ?? 5}");
        var text = payload.GetRawText();
        return text.Length > SnapshotLimit ? text[..SnapshotLimit] + "… (truncated)" : text;
    }

    /// <summary>스크린샷을 캡처해 PNG 로 저장하고 그 경로를 돌려줍니다. options: {ref, mode, maxWidth, path}.</summary>
    public string Screenshot(JsValue? options)
    {
        var o = Options.From(options);
        var response = Send("screenshot", new { @ref = o.Ref, maxWidth = o.MaxWidth, mode = o.Mode });
        var result = IpcSerializer.DeserializePayload<ScreenshotResponse>(PayloadOf(response));

        var path = o.Path ?? DefaultScreenshotPath();
        var directory = System.IO.Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
            System.IO.Directory.CreateDirectory(directory);
        System.IO.File.WriteAllBytes(path, System.Convert.FromBase64String(result.Base64Png));

        Record("screenshot", path);
        return path;
    }

    #endregion

    #region Notice / Logging

    /// <summary>조작 알림을 올립니다(알림이 없으면 아무것도 하지 않는다).</summary>
    public void NoticeShow(string message)
    {
        if (_notice is null)
            return;
        _notice.ShowAsync(message, _processId, _ct).GetAwaiter().GetResult();
    }

    /// <summary>조작 알림을 내립니다(알림이 없으면 아무것도 하지 않는다).</summary>
    public void NoticeHide()
    {
        if (_notice is null)
            return;
        _notice.HideAsync(_ct).GetAwaiter().GetResult();
    }

    /// <summary>스크립트 로그를 한 줄 남깁니다(상한 200줄).</summary>
    public void Log(string line)
    {
        if (_logs.Count < 200)
            _logs.Add(line);
    }

    /// <summary>스크립트가 명시적으로 실패를 낼 때.</summary>
    public void Fail(string message) => throw new ScriptFailure("fail", message);

    /// <summary>지정한 밀리초만큼 쉽니다(취소 가능).</summary>
    public void Sleep(int ms) => _ct.WaitHandle.WaitOne(ms);

    #endregion

    #region Private Methods

    private IpcMessage Send(string method, object payload)
    {
        _ct.ThrowIfCancellationRequested();
        return _send(IpcSerializer.CreateRequest(method, payload), _ct).GetAwaiter().GetResult();
    }

    private string Finish(string api, IpcMessage response, bool allowFail, string traceText)
    {
        var text = ResponseFormat.Action(response, "OK");
        text = RaiseNoticeIfNeeded(text);

        if (!allowFail && IsFailure(response, text))
            throw new ScriptFailure(api, text);

        Record(api, traceText);
        return text;
    }

    private string RaiseNoticeIfNeeded(string text)
    {
        if (_notice is null)
            return text;
        return _notice.AfterActionAsync(text, _processId, _ct).GetAwaiter().GetResult();
    }

    private static bool IsFailure(IpcMessage response, string text)
    {
        if (response.Type == "error"
            || text.StartsWith("Error:", StringComparison.Ordinal)
            || text.StartsWith("Failed:", StringComparison.Ordinal)
            || text.StartsWith("FAIL", StringComparison.Ordinal))
            return true;

        if (response.Payload is { } payload)
        {
            try
            {
                var result = IpcSerializer.DeserializePayload<ActionResponse>(payload);
                if (result.Pending)
                    return true;
            }
            catch (JsonException)
            {
            }
        }
        return false;
    }

    private void Record(string api, string detail)
    {
        _trace.AddLast($"{api} {detail}");
        while (_trace.Count > TraceCapacity)
            _trace.RemoveFirst();
    }

    private static JsonElement PayloadOf(IpcMessage response)
    {
        if (response.Type == "error")
            throw new ScriptFailure("ipc", response.Payload?.ToString() ?? "IPC error");
        if (response.Payload is not { } payload)
            throw new ScriptFailure("ipc", "empty response payload");
        return payload;
    }

    private static object? Convert(string? value)
    {
        if (value is null)
            return null;
        if (long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
            return (double)i;
        if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            return d;
        if (bool.TryParse(value, out var b))
            return b;
        return value;
    }

    private static string DefaultScreenshotPath()
    {
        var dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "xapper");
        var counter = Interlocked.Increment(ref _screenshotCounter);
        var name = $"run-{DateTime.Now:yyyyMMdd-HHmmss}-{counter}.png";
        return System.IO.Path.Combine(dir, name);
    }

    private static string Describe(object target) => target switch
    {
        ScriptElement e => $"ref={e.Ref}",
        string s => s,
        double n => $"ref={(int)n}",
        int n => $"ref={n}",
        _ => "?"
    };

    #endregion
}

using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;
using Xapper.McpServer.Infrastructure;
using Xapper.McpServer.Ipc;
using Xapper.McpServer.Scripting;
using Xapper.Protocol;

namespace Xapper.McpServer.Tools;

/// <summary>
/// 여러 조작을 담은 JavaScript 스크립트를 한 번에 실행하는 MCP 도구. 반복·조건·대기·단언을 서버 안에서 처리해
/// 도구를 하나씩 부를 때의 모델 왕복을 없앤다. 결과는 요약(성공/실패/타임아웃)만 돌려준다.
/// </summary>
[McpServerToolType]
public sealed class RunTools
{
    #region Fields

    private readonly SessionManager _sessionManager;
    private readonly IOperatorNotice _notice;

    #endregion

    #region Constructor

    /// <summary><see cref="RunTools"/> 를 만듭니다.</summary>
    public RunTools(SessionManager sessionManager, IOperatorNotice notice)
    {
        _sessionManager = sessionManager;
        _notice = notice;
    }

    #endregion

    #region MCP Tools

    /// <summary>스크립트를 실행합니다.</summary>
    [McpServerTool(Name = "xapper_run"), Description(
        "Run a JavaScript program that drives the attached app through a global 'xapper', instead of one tool " +
        "call per action. Use it for any stretch of 3+ actions or anything with a loop/condition/wait. Globals: " +
        "xapper.find(sel)->elements, one(sel), click/doubleClick/rightClick(target,{x,y,modifiers,timeout}), " +
        "type(target,text), key(keys,{modifiers}), get(target,prop)->value, assert(target,prop,expected,{timeout}), " +
        "waitUntil(sel,{timeout}), screenshot({path}), notice.show/hide; and log(x), fail(msg), sleep(ms). target " +
        "is a selector string ('id=Save','name=x','text=로그인','type=Button', comma=AND), a ref number, or an " +
        "element from find(). A failed action throws and stops the script. Returns a summary and the return value; " +
        "on failure the line, a short trace and a screenshot path. Prefer id/name selectors and waitUntil over sleep.")]
    public async Task<string> Run(
        [Description("The JavaScript to run. Provide this or scriptPath")] string? script = null,
        [Description("Absolute path to a .js file on the server machine to run instead of script")] string? scriptPath = null,
        [Description("Overall time limit in ms (default 60000, max 300000)")] int timeoutMs = 60000,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(script) == string.IsNullOrWhiteSpace(scriptPath))
            return "Error: provide exactly one of script or scriptPath.";

        if (script is null)
        {
            try
            {
                script = await File.ReadAllTextAsync(scriptPath ?? "", ct);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
            {
                return $"Error: could not read scriptPath ({ex.Message}).";
            }
        }

        var limit = Math.Clamp(timeoutMs, 1000, 300000);

        InspectorClient client;
        try
        {
            client = _sessionManager.GetActive();
        }
        catch (InvalidOperationException ex)
        {
            return $"Error: {ex.Message}";
        }

        var pid = _sessionManager.ActiveProcessId ?? 0;
        var runner = new ScriptRunner((msg, token) => client.SendAsync(msg, token), pid, _notice);
        var result = await runner.RunAsync(script, limit, ct);

        if (result.Outcome is ScriptOutcome.Fail or ScriptOutcome.Timeout)
            result = await AttachScreenshot(result, client, ct);

        return Render(result);
    }

    #endregion

    #region Private Methods

    /// <summary>실패·타임아웃 시 화면 스크린샷을 파일로 저장하고 경로를 결과에 붙인다. 저장 실패는 조용히 넘긴다.</summary>
    private static async Task<ScriptResult> AttachScreenshot(ScriptResult result, InspectorClient client, CancellationToken ct)
    {
        try
        {
            var response = await client.ScreenshotAsync(mode: "screen", ct: ct);
            if (response.Type == "error" || response.Payload is not { } payload)
                return result;

            var shot = IpcSerializer.DeserializePayload<Xapper.Protocol.Messages.Responses.ScreenshotResponse>(payload);
            var dir = Path.Combine(Path.GetTempPath(), "xapper");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"run-{DateTime.Now:yyyyMMdd-HHmmss}.png");
            await File.WriteAllBytesAsync(path, Convert.FromBase64String(shot.Base64Png), ct);
            return CloneWithScreenshot(result, path);
        }
        catch (Exception)
        {
            return result;
        }
    }

    private static ScriptResult CloneWithScreenshot(ScriptResult r, string path) => new()
    {
        Outcome = r.Outcome, Elapsed = r.Elapsed, ActionCount = r.ActionCount, Logs = r.Logs,
        ReturnValueJson = r.ReturnValueJson, FailureLine = r.FailureLine, FailureApi = r.FailureApi,
        FailureMessage = r.FailureMessage, Trace = r.Trace, ScreenshotPath = path
    };

    private static string Render(ScriptResult r)
    {
        var sb = new StringBuilder();
        switch (r.Outcome)
        {
            case ScriptOutcome.Ok:
                sb.AppendLine($"OK in {r.Elapsed.TotalSeconds:F1} s ({r.ActionCount} actions)");
                break;
            case ScriptOutcome.SyntaxError:
                sb.AppendLine($"SYNTAX ERROR line {r.FailureLine}: {r.FailureMessage}");
                return sb.ToString().TrimEnd();
            case ScriptOutcome.Timeout:
                sb.AppendLine($"TIMEOUT at line {r.FailureLine} — {r.ActionCount} actions completed");
                break;
            case ScriptOutcome.Fail:
                sb.AppendLine($"FAIL at line {r.FailureLine} ({r.FailureApi}): {r.FailureMessage}");
                break;
        }

        if (r.Trace.Count > 0)
            sb.AppendLine("trace (last " + r.Trace.Count + "): " + string.Join(" → ", r.Trace));
        foreach (var line in r.Logs)
            sb.AppendLine("log: " + line);
        if (r.ReturnValueJson is not null)
            sb.AppendLine("result: " + r.ReturnValueJson);
        if (r.ScreenshotPath is not null)
            sb.AppendLine("screenshot: " + r.ScreenshotPath);

        return sb.ToString().TrimEnd();
    }

    #endregion
}

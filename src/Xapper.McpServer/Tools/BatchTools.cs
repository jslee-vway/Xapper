using System.ComponentModel;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Server;
using Xapper.McpServer.Infrastructure;
using Xapper.Protocol;

namespace Xapper.McpServer.Tools;

/// <summary>
/// 여러 조작을 한 호출로 순서대로 실행하는 xapper_batch 도구.
/// 각 단계는 단일 도구와 같은 IPC 메서드와 같은 포매터를 거치므로 결과 문장은 단일 도구를 부른 것과 똑같다.
/// </summary>
[McpServerToolType]
public sealed class BatchTools
{
    #region Fields

    private readonly SessionManager _sessionManager;
    private readonly IOperatorNotice _notice;

    #endregion

    #region Constructor

    public BatchTools(SessionManager sessionManager, IOperatorNotice notice)
    {
        _sessionManager = sessionManager;
        _notice = notice;
    }

    #endregion

    #region Public Methods

    [McpServerTool(Name = "xapper_batch"), Description(
        "Run several steps in ONE call: they execute in order, the batch stops at the first failure, and you get " +
        "one numbered result per step in exactly the format the single tools return. Use it for any sequence you " +
        "already know - fill a form, open an editor and type into it, then verify with assert - instead of " +
        "spending one turn per action. Target selectors (\"id=...\", \"text=...\") make refs unnecessary, so a whole " +
        "flow can run without a snapshot in between. A step that opens a modal dialog ends the batch with the " +
        "'still handling' note and the remaining steps are not run.")]
    public async Task<string> Batch(
        [Description("JSON array of steps, each {\"tool\": name, ...args} using that tool's own argument names " +
                     "(ref or target, text, key, x/y, modifiers, propertyName, expected...). Tools: click, doubleclick, " +
                     "rightclick, wheel, type, key, select, toggle, expand, scroll, find, get_property, assert.")]
        JsonElement steps,
        CancellationToken ct = default)
    {
        if (!BatchPlanner.TryPlan(steps, out var plan, out var planError))
            return planError ?? "Error: invalid steps.";
        if (plan.Count == 0)
            return "Error: steps is empty - give at least one step.";

        var client = _sessionManager.GetActive();
        var sb = new StringBuilder();

        for (var i = 0; i < plan.Count; i++)
        {
            var step = plan[i];
            var response = await client.SendAsync(IpcSerializer.CreateRequest(step.Method, step.Payload), ct);
            var text = await _notice.AfterActionAsync(Format(step.Tool, response).TrimEnd(), _sessionManager.ActiveProcessId, ct);

            sb.Append($"{i + 1}. {text}");

            if (IsFailure(response, text) || IsPending(response))
            {
                var remaining = plan.Count - i - 1;
                sb.Append($" (batch stopped; {remaining} remaining step(s) not run)");
                break;
            }

            if (i < plan.Count - 1)
                sb.AppendLine();
        }

        return sb.ToString();
    }

    #endregion

    #region Private Methods

    /// <summary>단계의 도구 종류에 맞는 포매터로 응답을 문장으로 만든다. 단일 도구와 같은 성공 문구를 쓴다.</summary>
    private static string Format(string tool, IpcMessage response) => tool switch
    {
        "find" => ResponseFormat.Find(response),
        "get_property" => ResponseFormat.Property(response),
        "assert" => ResponseFormat.Assert(response),
        "click" => ResponseFormat.Action(response, "Click succeeded"),
        "doubleclick" => ResponseFormat.Action(response, "Double-click succeeded"),
        "rightclick" => ResponseFormat.Action(response, "Right-click succeeded"),
        "wheel" => ResponseFormat.Action(response, "Wheel succeeded"),
        "type" => ResponseFormat.Action(response, "Type succeeded"),
        "key" => ResponseFormat.Action(response, "Key sent"),
        _ => ResponseFormat.Action(response, "Success"),
    };

    /// <summary>
    /// 이 단계에서 배치를 멈춰야 하는지 판단한다. IPC 오류, 실패한 액션, 실패한 단언이 해당한다 —
    /// 검증이 틀린 뒤의 조작은 이미 어긋난 상태 위에서 도는 것이므로 계속하지 않는다.
    /// </summary>
    private static bool IsFailure(IpcMessage response, string text) =>
        response.Type == "error"
        || text.StartsWith("Error:", StringComparison.Ordinal)
        || text.StartsWith("Failed:", StringComparison.Ordinal)
        || text.StartsWith("FAIL", StringComparison.Ordinal);

    /// <summary>
    /// 조작이 전달만 되고 앱이 아직 처리 중이면(대개 모달 대화상자) 그 뒤 단계는 어긋난 상태 위에서 돌게 되므로 멈춘다.
    /// </summary>
    private static bool IsPending(IpcMessage response)
    {
        if (response.Type == "error" || response.Payload is not { } payload)
            return false;

        try
        {
            return payload.TryGetProperty("pending", out var pending) && pending.ValueKind == JsonValueKind.True;
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }

    #endregion
}

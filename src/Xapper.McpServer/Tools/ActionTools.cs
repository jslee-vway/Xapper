using System.ComponentModel;
using ModelContextProtocol.Server;
using Xapper.Protocol;
using Xapper.Protocol.Messages.Responses;

namespace Xapper.McpServer.Tools;

/// <summary>
/// 클릭, 텍스트 입력 등 기본 UI 액션 MCP 도구를 제공하는 클래스.
/// </summary>
[McpServerToolType]
public sealed class ActionTools
{
    private readonly SessionManager _sessionManager;

    public ActionTools(SessionManager sessionManager)
    {
        _sessionManager = sessionManager;
    }

    [McpServerTool(Name = "xapper_click"), Description(
        "Click a UI element by ref. Decide the mode before calling; the two modes differ in fidelity. " +
        "WITHOUT x/y: uses the accessibility Invoke pattern and raised events. Fast, does not move the physical " +
        "cursor, and works even when the window is not in front - but it SKIPS hit-testing, so it will report " +
        "success on a button that is covered by an overlay or has IsHitTestVisible=false, which a real user " +
        "could never click. WITH x/y: sends real mouse input at that point, reproducing exactly what a user can " +
        "do - but it requires the target window to be in the foreground and it takes over the physical cursor, " +
        "interrupting the human operator. Default to the event mode for routine steps, and switch to x/y when " +
        "the point of the test IS that a user can physically reach the control, or when the response warns that " +
        "the element is not reachable. The response carries a WARNING when the element is unreachable by a real " +
        "mouse, or when the window could not be activated.")]
    public async Task<string> Click(
        [Description("Element ref from last snapshot")] int @ref,
        [Description("Relative X position within element (0.0=left, 1.0=right). Omit for event-based click.")] double? x = null,
        [Description("Relative Y position within element (0.0=top, 1.0=bottom). Omit for event-based click.")] double? y = null,
        [Description("Timeout in ms to wait for element readiness (default 5000)")] int timeout = 5000,
        CancellationToken ct = default)
    {
        var client = _sessionManager.GetActive();
        var response = await client.ClickAsync(@ref, timeout, x, y, ct);

        if (response.Type == "error")
            return $"Error: {response.Payload}";

        var result = IpcSerializer.DeserializePayload<ActionResponse>(response.Payload!.Value);
        return result.Success ? result.Message ?? "Click succeeded" : $"Failed: {result.Error}";
    }

    [McpServerTool(Name = "xapper_type"), Description("Type text into a TextBox or editable element by ref")]
    public async Task<string> Type(
        [Description("Element ref from last snapshot")] int @ref,
        [Description("Text to type into the element")] string text,
        [Description("If true, clears existing text first (default true)")] bool clear = true,
        [Description("Timeout in ms (default 5000)")] int timeout = 5000,
        CancellationToken ct = default)
    {
        var client = _sessionManager.GetActive();
        var response = await client.TypeAsync(@ref, text, clear, timeout, ct);

        if (response.Type == "error")
            return $"Error: {response.Payload}";

        var result = IpcSerializer.DeserializePayload<ActionResponse>(response.Payload!.Value);
        return result.Success ? result.Message ?? "Type succeeded" : $"Failed: {result.Error}";
    }
}

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
        "could never click. WITH x/y: clicks at that exact point, going through real hit-testing like a user " +
        "would (so it hits whatever is on top). It normally does this by driving the mouse from INSIDE the " +
        "target process, which moves neither the physical cursor nor the keyboard focus - you can keep working " +
        "while it clicks. Only if that in-process path cannot be set up does it fall back to real mouse input, " +
        "which does move the cursor and take focus (the response names which path ran). Default to the event " +
        "mode for routine steps, and switch to x/y when the point of the test IS that a user can physically " +
        "reach the control, or when the response warns that the element is not reachable. " +
        "What the event mode does depends on the element, and the response names " +
        "the path it took - check it when a click appears to do nothing. A control with an accessibility " +
        "pattern is invoked or toggled through it, and a ButtonBase-derived control whose peer offers neither " +
        "has its click event raised instead; neither produces any mouse event. Everything else falls back to " +
        "four simulated routed events, which do reach " +
        "MouseLeftButtonDown/Up handlers, but only the Left-specific ones - a handler on MouseDown, MouseUp or " +
        "PreviewMouseDown still never runs, and there is no hit-test, no mouse capture and no real device " +
        "state behind them. Use x/y when the element depends on any of that. The Invoke and Toggle paths queue " +
        "their work on the UI thread rather than running it inline, so this call waits for that queue to drain " +
        "before " +
        "answering; when it returns the application has processed the click, unless the response says it was " +
        "still busy. There is no double-click: two coordinate clicks in a row may not register as one, " +
        "because the interval between calls exceeds the system double-click time. The response also warns " +
        "when the element is unreachable by a real mouse, or when the window could not be activated.")]
    public async Task<string> Click(
        [Description("Element ref from last snapshot")] int @ref,
        [Description("Relative X position within element (0.0=left, 1.0=right). Omit for event-based click.")] double? x = null,
        [Description("Relative Y position within element (0.0=top, 1.0=bottom). Omit for event-based click.")] double? y = null,
        [Description("Timeout in ms (default 5000). Spent twice - first waiting for the element to be ready, then bounding the wait for the click to be processed - so the worst case is about double this value")] int timeout = 5000,
        CancellationToken ct = default)
    {
        if (x.HasValue != y.HasValue)
            return "Error: x and y go together. Supply both to click at a point inside the element, or neither " +
                   "to let the element be driven through its own events.";

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

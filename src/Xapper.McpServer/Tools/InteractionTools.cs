using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Xapper.McpServer.Tools;

/// <summary>
/// 선택, 토글, 확장, 스크롤, 드래그 등 상호작용 MCP 도구를 제공하는 클래스.
/// </summary>
[McpServerToolType]
public sealed class InteractionTools
{
    private readonly SessionManager _sessionManager;

    public InteractionTools(SessionManager sessionManager)
    {
        _sessionManager = sessionManager;
    }

    [McpServerTool(Name = "xapper_select"), Description("Select an item in a ComboBox, ListBox, or TabControl")]
    public async Task<string> Select(
        [Description(ToolDescriptions.Ref)] int? @ref = null,
        [Description(ToolDescriptions.Target)] string? target = null,
        [Description("Item text to select (case-insensitive match)")] string? itemText = null,
        [Description("Item index to select (0-based)")] int? itemIndex = null,
        [Description("Timeout in ms (default 5000)")] int timeout = 5000,
        CancellationToken ct = default)
    {
        if (@ref is null && target is null)
            return "Error: pass ref or target.";

        var client = _sessionManager.GetActive();
        var response = await client.SelectAsync(@ref, target: target, itemText: itemText, itemIndex: itemIndex, timeout: timeout, ct: ct);
        return ResponseFormat.Action(response, "Success");
    }

    [McpServerTool(Name = "xapper_toggle"), Description("Toggle a CheckBox or ToggleButton")]
    public async Task<string> Toggle(
        [Description(ToolDescriptions.Ref)] int? @ref = null,
        [Description(ToolDescriptions.Target)] string? target = null,
        [Description("Timeout in ms (default 5000)")] int timeout = 5000,
        CancellationToken ct = default)
    {
        if (@ref is null && target is null)
            return "Error: pass ref or target.";

        var client = _sessionManager.GetActive();
        var response = await client.ToggleAsync(@ref, target: target, timeout: timeout, ct: ct);
        return ResponseFormat.Action(response, "Success");
    }

    [McpServerTool(Name = "xapper_expand"), Description("Expand or collapse a TreeViewItem or Expander")]
    public async Task<string> Expand(
        [Description(ToolDescriptions.Ref)] int? @ref = null,
        [Description(ToolDescriptions.Target)] string? target = null,
        [Description("True to expand, false to collapse (default true)")] bool expand = true,
        [Description("Timeout in ms (default 5000)")] int timeout = 5000,
        CancellationToken ct = default)
    {
        if (@ref is null && target is null)
            return "Error: pass ref or target.";

        var client = _sessionManager.GetActive();
        var response = await client.ExpandAsync(@ref, target: target, expand: expand, timeout: timeout, ct: ct);
        return ResponseFormat.Action(response, "Success");
    }

    [McpServerTool(Name = "xapper_scroll"), Description("Scroll within a ScrollViewer")]
    public async Task<string> Scroll(
        [Description(ToolDescriptions.Ref)] int? @ref = null,
        [Description(ToolDescriptions.Target)] string? target = null,
        [Description("Horizontal scroll percent (0-100, -1 to leave unchanged)")] double horizontalPercent = -1,
        [Description("Vertical scroll percent (0-100, -1 to leave unchanged)")] double verticalPercent = -1,
        [Description("Timeout in ms (default 5000)")] int timeout = 5000,
        CancellationToken ct = default)
    {
        if (@ref is null && target is null)
            return "Error: pass ref or target.";

        var client = _sessionManager.GetActive();
        var response = await client.ScrollAsync(@ref, target: target, horizontalPercent: horizontalPercent, verticalPercent: verticalPercent, timeout: timeout, ct: ct);
        return ResponseFormat.Action(response, "Success");
    }

    [McpServerTool(Name = "xapper_drag"), Description(
        "Drag with the left button: element to element (sourceRef/targetRef), by pixel offset " +
        "(offsetX/offsetY), or absolute screen points. Starting on a splitter, slider or scrollbar thumb " +
        "drives that control's own drag events (precise, no cursor) - the start point decides this, not the element " +
        "named; prefer xapper_scroll for sliders and scrollbars. Otherwise it runs in-process and " +
        "cursor-free; real-input fallback only if unavailable or the app's own window covers the start point " +
        "(the response says which, with a WARNING if the window could not be activated). modifiers for a " +
        "Ctrl- or Shift-drag.")]
    public async Task<string> Drag(
        [Description("Source element ref from last snapshot. Omit to treat sourceX/sourceY as absolute screen pixels")] int? sourceRef = null,
        [Description("Start X: 0-1 within the source element (default 0.5), or screen X without sourceRef")] double? sourceX = null,
        [Description("Start Y: 0-1 within the source element (default 0.5), or screen Y without sourceRef")] double? sourceY = null,
        [Description("Drop target element ref from last snapshot")] int? targetRef = null,
        [Description("End X: 0-1 within the target element (default 0.5), or screen X without targetRef/offsets")] double? targetX = null,
        [Description("End Y: 0-1 within the target element (default 0.5), or screen Y without targetRef/offsets")] double? targetY = null,
        [Description("Horizontal pixels to drag from the start point. Used when targetRef is omitted")] double? offsetX = null,
        [Description("Vertical pixels to drag from the start point. Used when targetRef is omitted")] double? offsetY = null,
        [Description("Modifiers to hold: \"Ctrl\", \"Shift\", \"Alt\" or \"Ctrl+Shift\" (ignored for thumb drags)")] string? modifiers = null,
        [Description("Timeout in ms (default 5000)")] int timeout = 5000,
        CancellationToken ct = default)
    {
        var client = _sessionManager.GetActive();
        var response = await client.DragAsync(
            sourceRef, sourceX, sourceY, targetRef, targetX, targetY, offsetX, offsetY, modifiers, timeout, ct);

        return ResponseFormat.Action(response, "Success");
    }
}

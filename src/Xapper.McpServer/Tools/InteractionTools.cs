using System.ComponentModel;
using ModelContextProtocol.Server;
using Xapper.Protocol;
using Xapper.Protocol.Messages.Responses;

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
        [Description("Element ref from last snapshot")] int @ref,
        [Description("Item text to select (case-insensitive match)")] string? itemText = null,
        [Description("Item index to select (0-based)")] int? itemIndex = null,
        [Description("Timeout in ms (default 5000)")] int timeout = 5000,
        CancellationToken ct = default)
    {
        var client = _sessionManager.GetActive();
        var response = await client.SelectAsync(@ref, itemText, itemIndex, timeout, ct);
        return FormatResponse(response);
    }

    [McpServerTool(Name = "xapper_toggle"), Description("Toggle a CheckBox or ToggleButton")]
    public async Task<string> Toggle(
        [Description("Element ref from last snapshot")] int @ref,
        [Description("Timeout in ms (default 5000)")] int timeout = 5000,
        CancellationToken ct = default)
    {
        var client = _sessionManager.GetActive();
        var response = await client.ToggleAsync(@ref, timeout, ct);
        return FormatResponse(response);
    }

    [McpServerTool(Name = "xapper_expand"), Description("Expand or collapse a TreeViewItem or Expander")]
    public async Task<string> Expand(
        [Description("Element ref from last snapshot")] int @ref,
        [Description("True to expand, false to collapse (default true)")] bool expand = true,
        [Description("Timeout in ms (default 5000)")] int timeout = 5000,
        CancellationToken ct = default)
    {
        var client = _sessionManager.GetActive();
        var response = await client.ExpandAsync(@ref, expand, timeout, ct);
        return FormatResponse(response);
    }

    [McpServerTool(Name = "xapper_scroll"), Description("Scroll within a ScrollViewer")]
    public async Task<string> Scroll(
        [Description("Element ref from last snapshot")] int @ref,
        [Description("Horizontal scroll percent (0-100, -1 to leave unchanged)")] double horizontalPercent = -1,
        [Description("Vertical scroll percent (0-100, -1 to leave unchanged)")] double verticalPercent = -1,
        [Description("Timeout in ms (default 5000)")] int timeout = 5000,
        CancellationToken ct = default)
    {
        var client = _sessionManager.GetActive();
        var response = await client.ScrollAsync(@ref, horizontalPercent, verticalPercent, timeout, ct);
        return FormatResponse(response);
    }

    [McpServerTool(Name = "xapper_drag"), Description("Drag with the left mouse button held down. Three modes: element to element (sourceRef + targetRef), element by pixel offset (sourceRef + offsetX/offsetY), or absolute screen points (sourceX/sourceY + targetX/targetY). Two very different things can happen and the response says which did. If the start point lands on a splitter, slider or scrollbar thumb, that control's own drag events are raised: the movement is given as a number, so it is more precise than a real drag and it touches neither the cursor nor focus. Note that the start point decides this, not the element you name - starting on a list item does not become a scrollbar drag just because the list has a scrollbar. For a slider or a scrollbar prefer xapper_scroll or setting the value outright; drag is for splitters and for the cases below. Everything else - dropping an item on a target, moving a shape on a canvas - is done with real mouse input, which moves the physical cursor and takes focus. There is no event-based fallback here as there is for clicking, and a drop cannot be aimed at all without the cursor, because Windows reads the drop location from it. Because that takes over the physical cursor and the keyboard focus, it interrupts whatever the person is doing at that moment. Tell them you are about to drive the mouse BEFORE you call it, and give them a moment to stop typing - an unannounced cursor that moves on its own reads as a malfunction. It also needs the target window in front, and the response carries a WARNING when it could not be brought there, which means the drag may not have reached the control.")]
    public async Task<string> Drag(
        [Description("Source element ref from last snapshot. Omit to treat sourceX/sourceY as absolute screen pixels")] int? sourceRef = null,
        [Description("Start X: relative 0.0-1.0 within the source element (default 0.5), or absolute screen X when sourceRef is omitted")] double? sourceX = null,
        [Description("Start Y: relative 0.0-1.0 within the source element (default 0.5), or absolute screen Y when sourceRef is omitted")] double? sourceY = null,
        [Description("Drop target element ref from last snapshot")] int? targetRef = null,
        [Description("End X: relative 0.0-1.0 within the target element (default 0.5), or absolute screen X when targetRef and offsets are omitted")] double? targetX = null,
        [Description("End Y: relative 0.0-1.0 within the target element (default 0.5), or absolute screen Y when targetRef and offsets are omitted")] double? targetY = null,
        [Description("Horizontal pixels to drag from the start point. Used when targetRef is omitted")] double? offsetX = null,
        [Description("Vertical pixels to drag from the start point. Used when targetRef is omitted")] double? offsetY = null,
        [Description("Timeout in ms (default 5000)")] int timeout = 5000,
        CancellationToken ct = default)
    {
        var client = _sessionManager.GetActive();
        var response = await client.DragAsync(
            sourceRef, sourceX, sourceY, targetRef, targetX, targetY, offsetX, offsetY, timeout, ct);

        return FormatResponse(response);
    }

    private static string FormatResponse(IpcMessage response)
    {
        if (response.Type == "error")
            return $"Error: {response.Payload}";
        var result = IpcSerializer.DeserializePayload<ActionResponse>(response.Payload!.Value);
        return result.Success ? result.Message ?? "Success" : $"Failed: {result.Error}";
    }
}

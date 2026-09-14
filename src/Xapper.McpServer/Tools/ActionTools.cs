using System.ComponentModel;
using ModelContextProtocol.Server;

namespace Xapper.McpServer.Tools;

/// <summary>
/// 클릭, 텍스트 입력 등 기본 UI 액션 MCP 도구를 제공하는 클래스.
/// </summary>
[McpServerToolType]
public sealed class ActionTools
{
    /// <summary>수식키 파라미터 설명. 에이전트가 정확한 문자열을 넘기도록 허용 어휘를 그대로 적는다.</summary>
    private const string ModifiersDescription =
        "Modifiers to hold: \"Ctrl\", \"Shift\", \"Alt\" or a combination like \"Ctrl+Shift\"";

    private readonly SessionManager _sessionManager;

    public ActionTools(SessionManager sessionManager)
    {
        _sessionManager = sessionManager;
    }

    [McpServerTool(Name = "xapper_click"), Description(
        "Click an element. Without x/y: accessibility Invoke/Toggle or the ButtonBase click event - fast, no " +
        "cursor, works behind other windows, but skips hit-testing (a covered control still 'succeeds'; the " +
        "response warns). With x/y (0-1 within the element): a real hit-tested click driven inside the target " +
        "process - no cursor or focus change, even under your own windows; falls back to real mouse input " +
        "only if that path is unavailable or the app's own popup/dialog covers the point (the response says " +
        "which). Use x/y for controls that need genuine mouse input (DevExpress grids). modifiers need x/y. " +
        "If the click opens a modal dialog the call returns after timeout with a note. The response names the " +
        "path taken - check it when a click seems to do nothing. For a double-click use xapper_doubleclick.")]
    public async Task<string> Click(
        [Description(ToolDescriptions.Ref)] int? @ref = null,
        [Description(ToolDescriptions.Target)] string? target = null,
        [Description("X 0-1 within the element; omit for the event-based click")] double? x = null,
        [Description("Y 0-1 within the element; omit for the event-based click")] double? y = null,
        [Description("Timeout in ms (default 5000), spent twice: waiting for the element, then for the click to be processed")] int timeout = 5000,
        [Description(ModifiersDescription)] string? modifiers = null,
        CancellationToken ct = default)
    {
        if (x.HasValue != y.HasValue)
            return "Error: x and y go together. Supply both to click at a point inside the element, or neither " +
                   "to let the element be driven through its own events.";

        if (!string.IsNullOrWhiteSpace(modifiers) && !x.HasValue)
            return "Error: modifiers need x/y - a modified click happens at a point. Supply x and y.";

        if (@ref is null && target is null)
            return "Error: pass ref or target.";

        var client = _sessionManager.GetActive();
        var response = await client.ClickAsync(@ref, target: target, timeout: timeout, x: x, y: y, doubleClick: false, modifiers: modifiers, ct: ct);

        return ResponseFormat.Action(response, "Click succeeded");
    }

    [McpServerTool(Name = "xapper_doubleclick"), Description(
        "Double-click at a point (x/y required, 0-1 within the element), e.g. a grid cell to open its editor. " +
        "Same in-process, cursor-free path as xapper_click; the two presses are close enough to register as " +
        "one double-click, which two xapper_click calls cannot guarantee. When the editor opens on a key, " +
        "prefer xapper_key F2.")]
    public async Task<string> DoubleClick(
        [Description("X 0-1 within the element")] double x,
        [Description("Y 0-1 within the element")] double y,
        [Description(ToolDescriptions.Ref)] int? @ref = null,
        [Description(ToolDescriptions.Target)] string? target = null,
        [Description("Timeout in ms (default 5000), spent twice: waiting for the element, then for the double-click to be processed")] int timeout = 5000,
        [Description(ModifiersDescription)] string? modifiers = null,
        CancellationToken ct = default)
    {
        if (@ref is null && target is null)
            return "Error: pass ref or target.";

        var client = _sessionManager.GetActive();
        var response = await client.ClickAsync(@ref, target: target, timeout: timeout, x: x, y: y, doubleClick: true, modifiers: modifiers, ct: ct);

        return ResponseFormat.Action(response, "Double-click succeeded");
    }

    [McpServerTool(Name = "xapper_rightclick"), Description(
        "Right-click a point in the element (x/y default centre) to open its context menu; in-process and " +
        "cursor-free, real-input fallback only if unavailable. Snapshot afterwards to see the menu (menus are " +
        "separate windows).")]
    public async Task<string> RightClick(
        [Description(ToolDescriptions.Ref)] int? @ref = null,
        [Description(ToolDescriptions.Target)] string? target = null,
        [Description("X 0-1 within the element (default 0.5)")] double? x = null,
        [Description("Y 0-1 within the element (default 0.5)")] double? y = null,
        [Description(ModifiersDescription)] string? modifiers = null,
        [Description("Timeout in ms (default 5000)")] int timeout = 5000,
        CancellationToken ct = default)
    {
        if (@ref is null && target is null)
            return "Error: pass ref or target.";

        var client = _sessionManager.GetActive();
        var response = await client.RightClickAsync(@ref, target: target, x: x, y: y, modifiers: modifiers, timeout: timeout, ct: ct);

        return ResponseFormat.Action(response, "Right-click succeeded");
    }

    [McpServerTool(Name = "xapper_wheel"), Description(
        "Roll the mouse wheel over a point in the element: a genuine wheel gesture, unlike xapper_scroll " +
        "which sets a ScrollViewer offset. Use it for Ctrl+wheel zoom (modifiers=\"Ctrl\") or handlers that " +
        "react only to real wheel input. notches: positive = up, negative = down, non-zero, within -100..100. " +
        "In-process and cursor-free; real-input fallback only if unavailable.")]
    public async Task<string> Wheel(
        [Description("Notches: +up / -down, non-zero, within -100..100")] int notches,
        [Description(ToolDescriptions.Ref)] int? @ref = null,
        [Description(ToolDescriptions.Target)] string? target = null,
        [Description("X 0-1 within the element (default 0.5)")] double? x = null,
        [Description("Y 0-1 within the element (default 0.5)")] double? y = null,
        [Description(ModifiersDescription)] string? modifiers = null,
        [Description("Timeout in ms (default 5000)")] int timeout = 5000,
        CancellationToken ct = default)
    {
        if (@ref is null && target is null)
            return "Error: pass ref or target.";

        var client = _sessionManager.GetActive();
        var response = await client.WheelAsync(@ref, target: target, notches: notches, x: x, y: y, modifiers: modifiers, timeout: timeout, ct: ct);

        return ResponseFormat.Action(response, "Wheel succeeded");
    }

    [McpServerTool(Name = "xapper_type"), Description(
        "Enter text into an editable element - use this for any text, never one xapper_key call per letter. " +
        "Sets the value through the accessibility Value pattern or TextBox.Text; otherwise focuses the " +
        "element and types real keystrokes, so it also works on RichTextBox, grid cell editors and DevExpress " +
        "editors. Omit ref/target to type into the currently focused editor (e.g. right after F2). Text is " +
        "entered literally. The keystroke path cannot detect rejection - read the value back when it matters.")]
    public async Task<string> Type(
        [Description("Text to type into the element")] string text,
        [Description("Element ref; omit ref and target to use the focused element")] int? @ref = null,
        [Description(ToolDescriptions.Target)] string? target = null,
        [Description("If true, clears existing text first (default true)")] bool clear = true,
        [Description("Timeout in ms (default 5000)")] int timeout = 5000,
        CancellationToken ct = default)
    {
        var client = _sessionManager.GetActive();
        var response = await client.TypeAsync(@ref, target: target, text: text, clear: clear, timeout: timeout, ct: ct);

        return ResponseFormat.Action(response, "Type succeeded");
    }

    [McpServerTool(Name = "xapper_key"), Description(
        "Send a key or chord to the app's keyboard focus from inside the process: works whatever window is in " +
        "front and never leaks into other apps. key is a WPF Key name (F2, Enter, Escape, Tab, Down), one " +
        "character, or a longer string typed character by character in one call. A string that is itself a " +
        "Key name (Enter, Space, Home, End, Delete, Help, Print, Select, Clear, Insert, Up, Down, Left, " +
        "Right, D1, F5 ...) is sent as that key - for literal text use xapper_type. modifiers (\"Ctrl\", " +
        "\"Shift\", \"Alt\", \"Ctrl+Shift\") are held in-process without real key presses; Ctrl/Alt + a character " +
        "is a chord, not typed. ref/target focuses that element first, otherwise the focused one. If the key " +
        "opens a modal dialog the call returns after timeout with a note; the dialog is a Win32 window - see " +
        "it with xapper_screenshot mode=\"screen\", not in a snapshot.")]
    public async Task<string> Key(
        [Description("WPF Key name (F2, Enter, Tab, Down), one character, or a string to type")] string key,
        [Description(ModifiersDescription)] string? modifiers = null,
        [Description("Element ref to focus first (omit, along with target, to send to the currently focused element)")] int? @ref = null,
        [Description(ToolDescriptions.Target)] string? target = null,
        [Description("Timeout in ms (default 5000)")] int timeout = 5000,
        CancellationToken ct = default)
    {
        var client = _sessionManager.GetActive();
        var response = await client.KeyAsync(key, modifiers: modifiers, @ref: @ref, target: target, timeout: timeout, ct: ct);

        return ResponseFormat.Action(response, "Key sent");
    }
}

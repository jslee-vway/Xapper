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
    /// <summary>수식키 파라미터 설명. 에이전트가 정확한 문자열을 넘기도록 허용 어휘를 그대로 적는다.</summary>
    private const string ModifiersDescription =
        "Modifier keys to hold during the action: exactly \"Ctrl\", \"Shift\" or \"Alt\", or a combination " +
        "joined with '+', e.g. \"Ctrl+Shift\". Case-insensitive. Omit for none.";

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
        "while it clicks, even with your own windows on top of the target. Only if that in-process path cannot " +
        "be set up, or a window of the target app itself (a popup or dialog) covers the point, does it fall back " +
        "to real mouse input, " +
        "which does move the cursor and take focus (the response names which path ran). Default to the event " +
        "mode for routine steps, and switch to x/y when the point of the test IS that a user can physically " +
        "reach the control, or when the response warns that the element is not reachable. Some controls - " +
        "notably DevExpress grids (GridControl/TableView, TreeListControl) - only change focus or selection " +
        "for genuine mouse input, so the event mode leaves FocusedRowHandle unchanged; use x/y on those. " +
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
        "when the element is unreachable by a real mouse, or when the window could not be activated. " +
        "To double-click, use xapper_doubleclick instead. Pass modifiers (e.g. \"Ctrl\") for a Ctrl-click or " +
        "Shift-click; modifiers require x/y because they only apply to a real point click.")]
    public async Task<string> Click(
        [Description("Element ref from last snapshot")] int @ref,
        [Description("Relative X position within element (0.0=left, 1.0=right). Omit for event-based click.")] double? x = null,
        [Description("Relative Y position within element (0.0=top, 1.0=bottom). Omit for event-based click.")] double? y = null,
        [Description("Timeout in ms (default 5000). Spent twice - first waiting for the element to be ready, then bounding the wait for the click to be processed - so the worst case is about double this value")] int timeout = 5000,
        [Description(ModifiersDescription)] string? modifiers = null,
        CancellationToken ct = default)
    {
        if (x.HasValue != y.HasValue)
            return "Error: x and y go together. Supply both to click at a point inside the element, or neither " +
                   "to let the element be driven through its own events.";

        if (!string.IsNullOrWhiteSpace(modifiers) && !x.HasValue)
            return "Error: modifiers need x/y - a modified click happens at a point. Supply x and y.";

        var client = _sessionManager.GetActive();
        var response = await client.ClickAsync(@ref, timeout, x, y, doubleClick: false, modifiers: modifiers, ct: ct);

        if (response.Type == "error")
            return $"Error: {response.Payload}";

        var result = IpcSerializer.DeserializePayload<ActionResponse>(response.Payload!.Value);
        return result.Success ? result.Message ?? "Click succeeded" : $"Failed: {result.Error}";
    }

    [McpServerTool(Name = "xapper_doubleclick"), Description(
        "Double-click a point inside a UI element by ref. A double-click is a gesture AT a location - for " +
        "example double-clicking a grid row or cell to open its editor, or a list item to activate it - so x " +
        "and y are required (unlike xapper_click, there is no event-based double-click). The point goes through " +
        "real hit-testing like a user would, hitting whatever is on top. It normally drives the mouse from " +
        "INSIDE the target process, moving neither the physical cursor nor the keyboard focus, so you can keep " +
        "working while it clicks; only if that in-process path cannot be set up, or a window of the target app " +
        "itself covers the point, does it fall back to real mouse input, which moves the cursor and takes focus " +
        "(the response names which path ran). The two presses are " +
        "sent close enough together that the application registers them as one double-click - two separate " +
        "xapper_click calls cannot guarantee this, because the gap between calls can exceed the system " +
        "double-click time. For keyboard-driven ways to open an editor (such as F2), use xapper_key instead. " +
        "Pass modifiers for a Ctrl- or Shift-double-click.")]
    public async Task<string> DoubleClick(
        [Description("Element ref from last snapshot")] int @ref,
        [Description("Relative X position within element (0.0=left, 1.0=right)")] double x,
        [Description("Relative Y position within element (0.0=top, 1.0=bottom)")] double y,
        [Description("Timeout in ms (default 5000). Spent twice - first waiting for the element to be ready, then bounding the wait for the double-click to be processed - so the worst case is about double this value")] int timeout = 5000,
        [Description(ModifiersDescription)] string? modifiers = null,
        CancellationToken ct = default)
    {
        var client = _sessionManager.GetActive();
        var response = await client.ClickAsync(@ref, timeout, x, y, doubleClick: true, modifiers: modifiers, ct: ct);

        if (response.Type == "error")
            return $"Error: {response.Payload}";

        var result = IpcSerializer.DeserializePayload<ActionResponse>(response.Payload!.Value);
        return result.Success ? result.Message ?? "Double-click succeeded" : $"Failed: {result.Error}";
    }

    [McpServerTool(Name = "xapper_rightclick"), Description(
        "Right-click a point inside a UI element by ref - typically to open its context menu. Always a coordinate " +
        "gesture (there is no accessibility right-click), so it goes through real hit-testing; x/y default to the " +
        "element's centre. Driven from INSIDE the target process, moving neither the physical cursor nor the " +
        "keyboard focus; only if that path cannot be set up does it fall back to real mouse input (the response " +
        "names which path ran). After it, snapshot again to see the opened menu. Pass modifiers for a Shift- or " +
        "Ctrl-right-click.")]
    public async Task<string> RightClick(
        [Description("Element ref from last snapshot")] int @ref,
        [Description("Relative X within element (0.0=left, 1.0=right). Default 0.5")] double? x = null,
        [Description("Relative Y within element (0.0=top, 1.0=bottom). Default 0.5")] double? y = null,
        [Description(ModifiersDescription)] string? modifiers = null,
        [Description("Timeout in ms (default 5000)")] int timeout = 5000,
        CancellationToken ct = default)
    {
        var client = _sessionManager.GetActive();
        var response = await client.RightClickAsync(@ref, x, y, modifiers, timeout, ct);

        if (response.Type == "error")
            return $"Error: {response.Payload}";

        var result = IpcSerializer.DeserializePayload<ActionResponse>(response.Payload!.Value);
        return result.Success ? result.Message ?? "Right-click succeeded" : $"Failed: {result.Error}";
    }

    [McpServerTool(Name = "xapper_wheel"), Description(
        "Roll the mouse wheel over a point inside a UI element by ref. This is a REAL wheel gesture, unlike " +
        "xapper_scroll which sets a ScrollViewer's offset directly - use xapper_wheel when the point of the test is " +
        "wheel behaviour: Ctrl+wheel zoom (pass modifiers=\"Ctrl\"), custom MouseWheel handlers, or controls that " +
        "only scroll on a genuine wheel. notches: positive rolls up/away from you, negative rolls down/toward you; " +
        "one notch is one standard wheel click; allowed range is -100..100 (call again for more). x/y default to " +
        "the element's centre. Driven from INSIDE the target process (no cursor movement); falls back to real " +
        "input only if that path is unavailable (the response names which path ran).")]
    public async Task<string> Wheel(
        [Description("Element ref from last snapshot")] int @ref,
        [Description("Notches to roll: positive = up/away, negative = down/toward you. Non-zero, between -100 and 100")] int notches,
        [Description("Relative X within element (0.0=left, 1.0=right). Default 0.5")] double? x = null,
        [Description("Relative Y within element (0.0=top, 1.0=bottom). Default 0.5")] double? y = null,
        [Description(ModifiersDescription)] string? modifiers = null,
        [Description("Timeout in ms (default 5000)")] int timeout = 5000,
        CancellationToken ct = default)
    {
        var client = _sessionManager.GetActive();
        var response = await client.WheelAsync(@ref, notches, x, y, modifiers, timeout, ct);

        if (response.Type == "error")
            return $"Error: {response.Payload}";

        var result = IpcSerializer.DeserializePayload<ActionResponse>(response.Payload!.Value);
        return result.Success ? result.Message ?? "Wheel succeeded" : $"Failed: {result.Error}";
    }

    [McpServerTool(Name = "xapper_type"), Description(
        "Enter text into an editable element by ref - the tool to use for any text longer than one key. Sets the " +
        "value through the element's accessibility Value pattern or TextBox.Text when it has one; otherwise it " +
        "focuses the element and types the text as real keystrokes inside the target process, so it also works " +
        "on editors without a value pattern (RichTextBox, grid cell editors, DevExpress text editors). Omit ref to " +
        "type into whatever currently has keyboard focus - the way to fill an inline editor you just opened with F2 " +
        "or a double-click, which has no ref until the next snapshot. The text is always entered literally. Do NOT " +
        "spell a word out with one xapper_key call per letter - one xapper_type call enters the whole string. Use " +
        "xapper_key only for named keys (Enter, Tab, F2, arrows) and key chords. The keystroke path cannot tell " +
        "whether the element accepted the characters (a focused button just ignores them), so read the value back " +
        "with xapper_get_property when it matters.")]
    public async Task<string> Type(
        [Description("Text to type into the element")] string text,
        [Description("Element ref from last snapshot. Omit to type into the element that currently has keyboard focus")] int? @ref = null,
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

    [McpServerTool(Name = "xapper_key"), Description(
        "Send a keystroke to the target's keyboard focus, driven from INSIDE the target process. Unlike " +
        "sending keys to the OS - which go to whatever window is in the foreground and leak into whatever the " +
        "person is doing - this routes the key to the application's own Keyboard.FocusedElement, so it works " +
        "no matter which window is in front and the person can keep working. Use it for keys the mouse cannot " +
        "express: F2 to open a grid cell editor, Enter to commit, Escape to cancel, Tab to move focus, arrow " +
        "keys to navigate. key is a WPF Key name (\"F2\", \"Enter\", \"Escape\", \"Tab\", \"Down\"), a single " +
        "printable character (\"a\", \"7\"), or a longer string such as \"qwerty\" which is typed as one " +
        "keystroke per character in this single call - never issue one call per letter. A string that happens to " +
        "be a Key name is sent as that key, not typed: this includes common words (Enter, Tab, Space, Home, End, " +
        "Help, Print, Select, Cancel, Clear, Insert, Delete, Pause, Play, Zoom, Up, Down, Left, Right, Add, Divide, " +
        "Scroll, Sleep) and short codes like \"D1\" or \"F5\". To enter any text literally - including into an " +
        "editor you just opened - use xapper_type (omit its ref to target the focused editor). Pass ref to " +
        "focus that element first; omit it to send to whatever currently has focus (for example the cell you " +
        "just clicked). The response names the key and the element that holds focus afterwards. " +
        "Modifiers ARE supported: pass modifiers=\"Ctrl\" for Ctrl+Z, \"Ctrl+Shift\" for combos. They are held from " +
        "inside the target process (no real key is pressed and nothing leaks to other windows). If that in-process " +
        "path cannot be set up in this process the call returns an error rather than pressing real keys. A " +
        "character with Ctrl or Alt is sent as a key chord only, not typed (Ctrl+a selects all, it does not insert " +
        "'a'). xapper_key is for keys and chords; xapper_type is for text.")]
    public async Task<string> Key(
        [Description("Key to send: a WPF Key name (F2, Enter, Escape, Tab, Down), a single printable character, or a string to type character by character in this one call")] string key,
        [Description(ModifiersDescription)] string? modifiers = null,
        [Description("Element ref to focus first (omit to send to the currently focused element)")] int? @ref = null,
        [Description("Timeout in ms (default 5000)")] int timeout = 5000,
        CancellationToken ct = default)
    {
        var client = _sessionManager.GetActive();
        var response = await client.KeyAsync(key, modifiers, @ref, timeout, ct);

        if (response.Type == "error")
            return $"Error: {response.Payload}";

        var result = IpcSerializer.DeserializePayload<ActionResponse>(response.Payload!.Value);
        return result.Success ? result.Message ?? "Key sent" : $"Failed: {result.Error}";
    }
}

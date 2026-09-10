using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;
using Xapper.Protocol;
using Xapper.Protocol.Messages.Responses;

namespace Xapper.McpServer.Tools;

/// <summary>
/// 비주얼 트리 요소 검색 MCP 도구를 제공하는 클래스.
/// </summary>
[McpServerToolType]
public sealed class FindTools
{
    private readonly SessionManager _sessionManager;

    public FindTools(SessionManager sessionManager)
    {
        _sessionManager = sessionManager;
    }

    [McpServerTool(Name = "xapper_find"), Description(
        "Find elements by name, automationId, type, or text content, returning a ref for each match. " +
        "type must equal the control's exact class name, ignoring case: a TreeView-derived control named " +
        "TreeViewControl is not matched by 'TreeView'. Take the name from a snapshot rather than guessing it. " +
        "name, automationId and text match on substrings. Supplying several criteria narrows the result - an " +
        "element must satisfy all of them - and supplying none is an error. Searching walks every open window " +
        "to full depth, and reports any node it could not traverse instead of abandoning the search. Unlike " +
        "xapper_snapshot this does not invalidate refs you already hold; it only hands out new numbers.")]
    public async Task<string> Find(
        [Description("Element x:Name to search for (partial match)")] string? name = null,
        [Description("AutomationProperties.AutomationId to search for (partial match)")] string? automationId = null,
        [Description("Control type name to filter by (e.g., 'Button', 'TextBox')")] string? type = null,
        [Description("Text content to search for (partial match)")] string? text = null,
        CancellationToken ct = default)
    {
        if (name == null && automationId == null && type == null && text == null)
            return "Error: Provide at least one search criteria (name, automationId, type, or text)";

        var client = _sessionManager.GetActive();
        var response = await client.FindAsync(name, automationId, type, text, ct);

        if (response.Type == "error")
            return $"Error: {response.Payload}";

        var result = IpcSerializer.DeserializePayload<FindElementResponse>(response.Payload!.Value);

        var sb = new StringBuilder();

        if (result.Matches.Count == 0)
        {
            sb.AppendLine("No matching elements found.");
        }
        else
        {
            sb.AppendLine($"Found {result.Matches.Count} match(es):");
            foreach (var match in result.Matches)
            {
                var parts = new List<string> { $"[ref={match.Ref}] {match.Type}" };
                if (!string.IsNullOrEmpty(match.Name)) parts.Add($"name=\"{match.Name}\"");
                if (!string.IsNullOrEmpty(match.AutomationId)) parts.Add($"id=\"{match.AutomationId}\"");
                if (!string.IsNullOrEmpty(match.Text)) parts.Add($"text=\"{match.Text}\"");
                sb.AppendLine($"  {string.Join(" ", parts)}");
            }
        }

        AppendSkippedNodes(sb, result.SkippedNodes);
        return sb.ToString();
    }

    [McpServerTool(Name = "xapper_element_at"), Description(
        "Ask what is drawn at a screen point and get a ref for it, without touching the mouse or the keyboard. " +
        "This is the way in when a control has no name, no automationId and no text to search for - the case " +
        "third-party grids and diagram surfaces usually present. x and y are SCREEN coordinates. To get them " +
        "from a screenshot you must convert, because a capture covers the window rather than the whole desktop " +
        "and may be scaled: the screenshot reports the screen position of its (0,0) pixel and its scale, and " +
        "screen = origin + image / scale. The lookup is a hit test, so it answers with what a real click at " +
        "that point would reach: a control covering the point wins. An empty answer means nothing there takes " +
        "hit-testing - an element with no brush behind it, one with IsHitTestVisible off, or a point in the " +
        "window border rather than its content; note that a Transparent brush does take hit-testing and will " +
        "be reported. Results run from the deepest element outwards, because the deepest one is usually a " +
        "piece of the control rather than the control itself - a Button's inner TextBlock, say, or a raw " +
        "drawing visual that xapper_click will refuse because it is not a UIElement. Read the chain, pick the " +
        "level you actually want, and act on that ref with xapper_click, which needs no coordinates and so " +
        "leaves your cursor and focus alone.")]
    public async Task<string> ElementAt(
        [Description("Screen X coordinate in pixels, as read from a screenshot")] double x,
        [Description("Screen Y coordinate in pixels, as read from a screenshot")] double y,
        [Description("How many levels above the deepest element to include (default 4)")] int maxAncestors = 4,
        CancellationToken ct = default)
    {
        if (maxAncestors < 0)
            return "Error: maxAncestors cannot be negative. Use 0 for the deepest element alone.";

        var client = _sessionManager.GetActive();
        var response = await client.ElementAtAsync(x, y, maxAncestors, ct);

        if (response.Type == "error")
            return $"Error: {response.Payload}";

        var result = IpcSerializer.DeserializePayload<FindElementResponse>(response.Payload!.Value);

        var sb = new StringBuilder();
        sb.AppendLine($"At ({x:F0},{y:F0}), deepest first:");
        for (var depth = 0; depth < result.Matches.Count; depth++)
        {
            var match = result.Matches[depth];
            var parts = new List<string> { $"[ref={match.Ref}] {match.Type}" };
            if (!string.IsNullOrEmpty(match.Name)) parts.Add($"name=\"{match.Name}\"");
            if (!string.IsNullOrEmpty(match.AutomationId)) parts.Add($"id=\"{match.AutomationId}\"");
            if (!string.IsNullOrEmpty(match.Text)) parts.Add($"text=\"{match.Text}\"");
            sb.AppendLine($"  {new string(' ', depth * 2)}{string.Join(" ", parts)}");
        }
        return sb.ToString();
    }

    /// <summary>
    /// 순회하지 못해 건너뛴 노드를 결과 끝에 덧붙입니다.
    /// 검색이 트리 전체를 보지 못했다는 사실과 어디서 막혔는지를 호출자가 알 수 있게 한다.
    /// </summary>
    private static void AppendSkippedNodes(StringBuilder sb, List<string> skippedNodes)
    {
        if (skippedNodes.Count == 0)
            return;

        sb.AppendLine();
        sb.AppendLine($"WARNING: skipped {skippedNodes.Count} unreadable node(s). The subtree under each was not searched:");
        foreach (var node in skippedNodes.Take(MaxReportedSkippedNodes))
            sb.AppendLine($"  {node}");

        var remaining = skippedNodes.Count - MaxReportedSkippedNodes;
        if (remaining > 0)
            sb.AppendLine($"  ... and {remaining} more");
    }

    /// <summary>결과에 나열할 건너뛴 노드의 최대 개수.</summary>
    private const int MaxReportedSkippedNodes = 10;
}

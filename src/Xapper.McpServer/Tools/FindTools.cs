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
        "Find elements by name, automationId, type or text (AND when combined; at least one required) and get " +
        "a ref for each. type is the exact class name (case-insensitive) - take it from a snapshot. Searches " +
        "every open window including popups and menus, reports nodes it could not traverse, and does NOT " +
        "invalidate existing refs (snapshot does). For one known element, pass target=\"id=...\" to the action " +
        "tool directly instead of finding first.")]
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

        return ResponseFormat.Find(response);
    }

    [McpServerTool(Name = "xapper_element_at"), Description(
        "Get the element drawn at a SCREEN point, with its ancestors deepest-first and a ref for each - the " +
        "way in for controls with no name, id or text, such as grid cells. It is a hit test: a covering " +
        "control wins, and an empty answer means nothing hit-testable is there. screen = screenshot origin + " +
        "pixel / scale. Pick the ancestor level you mean (the deepest is often an inner TextBlock) and act on " +
        "that ref.")]
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
}

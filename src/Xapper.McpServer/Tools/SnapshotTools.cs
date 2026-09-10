using System.ComponentModel;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Server;
using Xapper.Protocol;
using Xapper.Protocol.Messages.Responses;

namespace Xapper.McpServer.Tools;

/// <summary>
/// 비주얼 트리 스냅샷 MCP 도구를 제공하는 클래스. text/json 포맷 출력 지원.
/// </summary>
[McpServerToolType]
public sealed class SnapshotTools
{
    private readonly SessionManager _sessionManager;

    public SnapshotTools(SessionManager sessionManager)
    {
        _sessionManager = sessionManager;
    }

    [McpServerTool(Name = "xapper_snapshot"), Description(
        "Get a snapshot of the UI visual tree, assigning a ref to every element it lists except the " +
        "synthetic Application root that appears when several windows are open. Calling this discards all " +
        "refs handed out earlier, including those from xapper_find, and restarts numbering, so an old ref " +
        "may now point at a different element. Depth is the thing to " +
        "manage: real applications nest deeply and the default of 5 usually stops well above the content, " +
        "while raising it grows the response exponentially and can exceed what the transport carries. " +
        "Rather than raising maxDepth across the whole window, locate a container with xapper_find or a " +
        "shallow snapshot, then pass its ref as rootRef to expand that subtree alone.")]
    public async Task<string> Snapshot(
        [Description("Ref to start from, taken from the previous snapshot or from xapper_find (omit for the whole window)")] int? rootRef = null,
        [Description("Max depth to traverse (default 5)")] int maxDepth = 5,
        [Description("Output format: 'text' (compact) or 'json' (structured)")] string format = "text",
        CancellationToken ct = default)
    {
        var client = _sessionManager.GetActive();
        var response = await client.SnapshotAsync(rootRef, maxDepth, ct);

        if (response.Type == "error")
            return $"Error: {response.Payload}";

        var snapshotResponse = IpcSerializer.DeserializePayload<SnapshotResponse>(response.Payload!.Value);

        return format == "json"
            ? JsonSerializer.Serialize(snapshotResponse, new JsonSerializerOptions { WriteIndented = true })
            : FormatAsText(snapshotResponse);
    }

    private static string FormatAsText(SnapshotResponse response)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Generation: {response.Generation}");
        if (response.SkippedNodes.Count > 0)
            sb.AppendLine($"Skipped: {response.SkippedNodes.Count} unreadable node(s) (use format=\"json\" to see where)");
        sb.AppendLine();
        FormatElement(sb, response.Root, 0);
        return sb.ToString();
    }

    private static void FormatElement(StringBuilder sb, ElementSnapshot element, int indent)
    {
        var prefix = new string(' ', indent * 2);
        var parts = new List<string> { $"[ref={element.Ref}] {element.Type}" };

        if (!string.IsNullOrEmpty(element.Name))
            parts.Add($"name=\"{element.Name}\"");
        if (!string.IsNullOrEmpty(element.AutomationId))
            parts.Add($"id=\"{element.AutomationId}\"");
        if (!string.IsNullOrEmpty(element.Text))
            parts.Add($"text=\"{Truncate(element.Text, 50)}\"");
        if (!element.IsEnabled)
            parts.Add("disabled");
        if (!element.IsVisible)
            parts.Add("hidden");

        sb.AppendLine($"{prefix}{string.Join(" ", parts)}");

        foreach (var child in element.Children)
        {
            FormatElement(sb, child, indent + 1);
        }
    }

    private static string Truncate(string text, int maxLength)
    {
        return text.Length <= maxLength ? text : text[..maxLength] + "...";
    }
}

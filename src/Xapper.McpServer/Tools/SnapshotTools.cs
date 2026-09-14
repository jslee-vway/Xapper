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
        "Visual-tree snapshot with a ref per element. It invalidates ALL earlier refs (from find too) and " +
        "renumbers. The root is the window, or a synthetic Application node whenever several top-level " +
        "windows (popups, menus, tooltips) are open - do not key on it. Keep maxDepth small (default 5): " +
        "responses grow exponentially; to go deeper, pass a container's ref as rootRef.")]
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

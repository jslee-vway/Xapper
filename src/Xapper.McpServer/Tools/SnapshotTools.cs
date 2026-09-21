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
        "responses grow exponentially; to go deeper, pass a container's ref as rootRef. With " +
        "addressableOnly the anonymous layout containers are folded away, which is much shorter - raise " +
        "maxDepth when you use it, because the depth limit applies before the filter.")]
    public async Task<string> Snapshot(
        [Description("Ref to start from, taken from the previous snapshot or from xapper_find (omit for the whole window)")] int? rootRef = null,
        [Description("Max depth to traverse (default 5)")] int maxDepth = 5,
        [Description("Output format: 'text' (compact) or 'json' (structured)")] string format = "text",
        [Description("Keep only elements a selector can target - those with an id, a name or text. Folds anonymous layout containers away and promotes what is under them")] bool addressableOnly = false,
        CancellationToken ct = default)
    {
        var client = _sessionManager.GetActive();
        var response = await client.SnapshotAsync(rootRef, maxDepth, ct);

        if (response.Type == "error")
            return $"Error: {response.Payload}";

        var snapshotResponse = IpcSerializer.DeserializePayload<SnapshotResponse>(response.Payload!.Value);

        var folded = 0;
        if (addressableOnly)
        {
            var (filtered, foldedCount) = SnapshotFilter.KeepAddressable(snapshotResponse.Root);
            snapshotResponse.Root = filtered;
            folded = foldedCount;
        }

        return format == "json"
            ? JsonSerializer.Serialize(snapshotResponse, new JsonSerializerOptions { WriteIndented = true })
            : FormatAsText(snapshotResponse, folded);
    }

    /// <summary>
    /// 스냅샷을 들여쓴 텍스트로 만듭니다. 머리에 세대 번호와, 트리를 온전히 보여주지 못한 사정(건너뜀·접힘)을 적는다.
    /// </summary>
    /// <param name="response">Inspector 가 준 스냅샷.</param>
    /// <param name="folded">필터가 접어 버린 노드 수. 0이면 적지 않는다.</param>
    private static string FormatAsText(SnapshotResponse response, int folded)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Generation: {response.Generation}");
        if (response.SkippedNodes.Count > 0)
            sb.AppendLine($"Skipped: {response.SkippedNodes.Count} node(s) - " +
                          $"{SkippedNodeSummary.Summarize(response.SkippedNodes)}. Use format=\"json\" for each one.");
        if (folded > 0)
            sb.AppendLine($"Folded: {folded} container(s) with no id, name or text.");
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

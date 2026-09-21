using System.Globalization;

namespace Xapper.McpServer.Tools;

/// <summary>
/// 순회하지 못해 건너뛴 노드 목록을 한 줄로 요약한다.
/// 실측에서 수십 개가 전부 같은 사유(빈 자식 슬롯)였다 — 그것을 그대로 나열하면 호출자가 읽을 것이 없는 줄만 늘어난다.
/// 사유별로 세어 부모 타입과 깊이를 함께 알려 주고, 상세는 스냅샷의 json 출력에 남겨 둔다.
/// </summary>
internal static class SkippedNodeSummary
{
    #region Fields

    /// <summary>한 줄에 적을 사유 종류의 최대 개수. 나머지는 개수로만 알린다.</summary>
    private const int MaxReasons = 3;

    #endregion

    #region Public Methods

    /// <summary>건너뛴 노드 목록을 사유별로 묶어 한 줄로 만듭니다. 목록이 비면 빈 문자열.</summary>
    /// <param name="skippedNodes"><c>VisualChildren.Describe</c> 가 만든 설명 줄들.</param>
    public static string Summarize(IReadOnlyList<string> skippedNodes)
    {
        if (skippedNodes.Count == 0)
            return "";

        var groups = skippedNodes
            .Select(Parse)
            .GroupBy(entry => entry.Reason, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => group.Key, StringComparer.Ordinal)
            .ToList();

        var parts = groups.Take(MaxReasons).Select(Describe).ToList();

        var remaining = groups.Count - MaxReasons;
        if (remaining > 0)
            parts.Add($"+{remaining} other reason(s)");

        return string.Join(", ", parts);
    }

    #endregion

    #region Private Methods

    /// <summary>사유가 같은 항목들을 "3x \"사유\" (under Grid, depth 3-12)" 한 도막으로 만듭니다.</summary>
    private static string Describe(IGrouping<string, SkippedNode> group)
    {
        var count = group.Count();
        var detail = new List<string>();

        var types = group
            .Where(entry => entry.ParentType.Length > 0)
            .GroupBy(entry => entry.ParentType, StringComparer.Ordinal)
            .OrderByDescending(byType => byType.Count())
            .ThenBy(byType => byType.Key, StringComparer.Ordinal)
            .ToList();
        if (types.Count > 0)
        {
            // 가장 흔한 부모만 이름을 적되, 다른 부모가 더 있으면 그 사실을 숨기지 않는다 —
            // 하나만 적어 두면 호출자가 전부 그 부모 아래라고 읽고 엉뚱한 컨테이너를 뒤진다.
            var others = types.Count - 1;
            detail.Add(others > 0 ? $"under {types[0].Key} +{others} more type(s)" : $"under {types[0].Key}");
        }

        var depths = group.Where(entry => entry.Depth.HasValue).Select(entry => entry.Depth).ToList();
        if (depths.Count > 0)
        {
            var min = depths.Min();
            var max = depths.Max();
            detail.Add(min == max ? $"depth {min}" : $"depth {min}-{max}");
        }

        var suffix = detail.Count > 0 ? $" ({string.Join(", ", detail)})" : "";
        return $"{count}x \"{group.Key}\"{suffix}";
    }

    /// <summary>
    /// 설명 줄을 사유·부모 타입·깊이로 나눕니다. 사유는 오른쪽 끝 ": " 뒤에 오고 그 안에 ": " 가 없다는 것이
    /// <c>VisualChildren.Describe</c> 의 형식이다. 형식이 어긋난 줄도 예외 없이 "unknown" 으로 받는다.
    /// </summary>
    private static SkippedNode Parse(string line)
    {
        var separator = line.LastIndexOf(": ", StringComparison.Ordinal);
        if (separator < 0)
            return new SkippedNode("unknown", "", null);

        var reason = line[(separator + 2)..].Trim();
        var head = line[..separator];

        var space = head.IndexOf(' ');
        var parentType = space < 0 ? head : head[..space];

        return new SkippedNode(reason.Length == 0 ? "unknown" : reason, parentType, DepthIn(head));
    }

    /// <summary>설명 줄 앞부분에서 depth=N 의 N 을 읽습니다. 없으면 null.</summary>
    private static int? DepthIn(string head)
    {
        const string marker = "depth=";
        var start = head.IndexOf(marker, StringComparison.Ordinal);
        if (start < 0)
            return null;

        var digits = head[(start + marker.Length)..];
        var end = 0;
        while (end < digits.Length && char.IsDigit(digits[end]))
            end++;

        return end > 0 && int.TryParse(digits[..end], NumberStyles.Integer, CultureInfo.InvariantCulture, out var depth)
            ? depth
            : null;
    }

    #endregion

    #region Nested Types

    /// <summary>건너뛴 노드 한 줄에서 읽어낸 값들.</summary>
    /// <param name="Reason">건너뛴 사유.</param>
    /// <param name="ParentType">자식을 못 읽은 부모의 타입 이름. 못 읽으면 빈 문자열.</param>
    /// <param name="Depth">루트로부터의 깊이. 못 읽으면 null.</param>
    private sealed record SkippedNode(string Reason, string ParentType, int? Depth);

    #endregion
}

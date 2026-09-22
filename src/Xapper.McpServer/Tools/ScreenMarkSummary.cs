using System.Text;
using Xapper.Protocol.Messages.Responses;

namespace Xapper.McpServer.Tools;

/// <summary>
/// annotate 스크린샷의 번호 상자 목록을 요약 텍스트로 옮긴다.
/// 에이전트는 그림에서 번호를 읽고 이 목록에서 ref 를 찾으므로, 둘이 같은 줄에 있어야 한다.
/// </summary>
internal static class ScreenMarkSummary
{
    #region Public Methods

    /// <summary>번호 상자 목록을 여러 줄의 텍스트로 만듭니다. 목록이 비면 빈 문자열.</summary>
    /// <param name="marks">그림에 그린 상자 목록.</param>
    /// <param name="omitted">상한 때문에 그리지 않은 요소 수.</param>
    public static string Describe(IReadOnlyList<ScreenMark> marks, int omitted)
    {
        if (marks.Count == 0)
            return "";

        var sb = new StringBuilder();
        foreach (var mark in marks)
        {
            var parts = new List<string> { $"{mark.Number}: {mark.Type}" };
            if (!string.IsNullOrEmpty(mark.Name)) parts.Add($"name=\"{mark.Name}\"");
            if (!string.IsNullOrEmpty(mark.AutomationId)) parts.Add($"id=\"{mark.AutomationId}\"");
            if (!string.IsNullOrEmpty(mark.Text)) parts.Add($"text=\"{Shorten(mark.Text)}\"");

            // 기준점은 ref 와 성격이 다르다. ref 는 이번 세션에서만 유효하고 기준점은 세션을 넘어 유효하므로,
            // 둘을 함께 보여 주어야 호출자가 어느 쪽을 적어 둘지 판단할 수 있다.
            if (!string.IsNullOrEmpty(mark.Anchor) && mark.AnchorX is { } x && mark.AnchorY is { } y)
                parts.Add($"in {mark.Anchor} at {x:0.###},{y:0.###}");

            parts.Add($"ref={mark.Ref}");

            sb.AppendLine(" " + string.Join(" ", parts));
        }

        if (omitted > 0)
            sb.AppendLine($" ... and {omitted} more element(s) were not marked (too many to label). " +
                          "Capture a narrower area with ref to see them.");

        return sb.ToString().TrimEnd();
    }

    #endregion

    #region Private Methods

    /// <summary>목록이 길어지지 않게 텍스트를 줄입니다.</summary>
    private static string Shorten(string text)
        => text.Length <= 40 ? text : text[..40] + "...";

    #endregion
}

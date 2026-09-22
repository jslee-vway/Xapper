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

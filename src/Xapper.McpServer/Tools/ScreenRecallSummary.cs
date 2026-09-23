using System.Globalization;
using System.Text;
using Xapper.McpServer.Infrastructure;

namespace Xapper.McpServer.Tools;

/// <summary>
/// 저장된 화면 기록을 모델이 읽을 응답 문장으로 옮긴다.
/// 이 문장만 보고 스크린샷 없이 조작할 수 있어야 하므로, 한 영역에 대해 "어떻게 지목하는가"와
/// "무엇인가"가 같은 줄에 함께 놓인다. 기록이 없을 때는 대신 무엇을 하면 되는지를 일러 준다.
/// </summary>
internal static class ScreenRecallSummary
{
    #region Public Methods

    /// <summary>
    /// 아는 화면의 기록을 응답 문장으로 만듭니다.
    /// 언제 배웠고 몇 번 쓰였는지를 함께 적어, 기록이 오래되어 미심쩍을 때 모델이 다시 살펴볼지 판단할 수 있게 한다.
    /// </summary>
    /// <param name="record">조회에 걸린 화면 기록.</param>
    /// <returns>여러 줄의 응답 문장.</returns>
    public static string Known(ScreenRecord record)
    {
        if (record is null)
            throw new ArgumentNullException(nameof(record));

        var sb = new StringBuilder();
        sb.AppendLine($"Known screen: {record.Name}  ({Provenance(record)})");

        // 셀렉터가 하나도 없는 기록은 "스크린샷 없이 조작하라" 고 말할 자격이 없다. 좌표 몇 줄로는 그렇게
        // 할 수 없는데도 그 문장이 잘못된 확신을 준다(실측: 기준점 여섯 줄짜리 기록을 받은 직후 스크린샷을
        // 찍었다). 그럴 때는 기록이 얇다고 밝히고 다시 배우게 한다. 비고는 그래도 값이 있으므로 남긴다.
        if (record.Regions.Any(region => !string.IsNullOrWhiteSpace(region.Selector)))
        {
            sb.AppendLine("Act on these without a screenshot:");
            foreach (var region in record.Regions)
                sb.AppendLine("  " + Describe(region));

            // 셀렉터 줄은 그대로 target 에 넣으면 되지만, 기준점 줄은 쓰는 법이 눈에 보이지 않는다.
            // 좌표가 요소 안의 비율이라는 사실을 모르면 픽셀로 착각해 엉뚱한 자리를 누른다.
            if (record.Regions.Any(region => string.IsNullOrWhiteSpace(region.Selector)))
                sb.AppendLine(
                    "A line reading \"in X at a,b\" has no selector of its own: act on it with target=X and " +
                    "x=a, y=b, which are fractions of X, not pixels.");
        }
        else
        {
            sb.AppendLine(
                "The record holds nothing a selector can reach, so it cannot stand in for looking. Look once " +
                "with xapper_screenshot(annotate: true) and call xapper_screen_learn again to replace it.");
        }

        if (!string.IsNullOrWhiteSpace(record.Notes))
        {
            // 비고는 지난 방문이 알아낸 것을 그대로 믿고 쓰라고 적어 둔 것이다. 다시 확인하면 아낀 것이 없다.
            sb.AppendLine($"Notes: {record.Notes}");
            sb.AppendLine(
                "Those notes are what earlier visits worked out; act on them rather than finding out again. " +
                "If one turns out to be wrong, call xapper_screen_note with replace and write the corrected set.");
        }

        return sb.ToString().TrimEnd();
    }

    /// <summary>
    /// 모르는 화면에 대한 안내 문장을 만듭니다.
    /// 지문을 함께 적는 이유는, 같은 화면을 오가며 여러 번 조회할 때 값이 같은지 다른지를 모델이 스스로 견줄 수 있어서다.
    /// </summary>
    /// <param name="signature">현재 화면의 지문.</param>
    /// <param name="changedSinceLastRecall">직전 조회 때와 화면이 달라졌으면 true.</param>
    /// <returns>여러 줄의 안내 문장.</returns>
    public static string Unknown(string signature, bool changedSinceLastRecall)
    {
        var sb = new StringBuilder();
        sb.Append($"Unknown screen (signature {signature}).");

        if (changedSinceLastRecall)
            sb.Append(" The screen also changed since your last recall.");

        sb.AppendLine();
        sb.AppendLine("Explore it with xapper_screenshot(annotate: true), then call xapper_screen_learn");
        sb.Append("with a one-line name and any gotcha worth remembering.");

        return sb.ToString();
    }

    #endregion

    #region Private Methods

    /// <summary>이 기록이 언제 생겼고 얼마나 쓰였는지를 괄호 안에 넣을 한 조각으로 만듭니다.</summary>
    private static string Provenance(ScreenRecord record)
    {
        var times = $"seen {record.SeenCount} time{(record.SeenCount == 1 ? "" : "s")}";
        var learned = LearnedOn(record.LearnedAt);
        return learned is null ? times : $"learned {learned}, {times}";
    }

    /// <summary>
    /// 저장된 시각에서 날짜만 뽑습니다. 시·분·초까지 보여 줄 이유가 없고 줄만 길어진다.
    /// 읽을 수 없는 값이면 저장된 그대로 내보내, 잘못 적힌 값을 감추지 않는다.
    /// </summary>
    private static string? LearnedOn(string? learnedAt)
    {
        if (string.IsNullOrWhiteSpace(learnedAt))
            return null;

        return DateTimeOffset.TryParse(learnedAt, CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind, out var moment)
            ? moment.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : learnedAt;
    }

    /// <summary>
    /// 영역 하나를 한 줄로 만듭니다. 셀렉터가 있으면 그것을 앞에 놓아 그대로 복사해 쓰게 하고,
    /// 셀렉터가 없는 요소는 기준점과 상대 좌표로 자리를 일러 준다 — 클릭의 x/y 를 그것으로 정할 수 있다.
    /// </summary>
    private static string Describe(ScreenRegion region)
    {
        var parts = new List<string> { Address(region), region.Type };
        if (!string.IsNullOrWhiteSpace(region.Text))
            parts.Add(region.Text);

        return string.Join("  ", parts);
    }

    /// <summary>영역을 지목하는 방법. 셀렉터가 없으면 기준점과 상대 좌표, 그것도 없으면 지목할 길이 없다고 적는다.</summary>
    private static string Address(ScreenRegion region)
    {
        if (!string.IsNullOrWhiteSpace(region.Selector))
            return region.Selector;

        if (!string.IsNullOrWhiteSpace(region.Anchor) && region.AnchorX is { } x && region.AnchorY is { } y)
            return $"in {region.Anchor} at {x.ToString("0.###", CultureInfo.InvariantCulture)}," +
                   $"{y.ToString("0.###", CultureInfo.InvariantCulture)}";

        return "(no selector)";
    }

    #endregion
}

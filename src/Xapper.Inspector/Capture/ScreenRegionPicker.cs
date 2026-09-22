using System.Windows;

namespace Xapper.Inspector.Capture;

/// <summary>
/// 화면 기록에 남길 요소를 고른다.
///
/// <see cref="MarkPicker"/> 와 목적이 다르다는 점이 이 클래스가 따로 있는 이유다. annotate 스크린샷은
/// "이름이 없어 셀렉터로 잡을 수 없는 작은 셀" 을 찾기 때문에 면적이 작은 것부터 남긴다. 반대로 화면 기록이
/// 필요로 하는 것은 "다음 방문에 셀렉터로 그대로 지목할 수 있는 요소" 이고, 그런 요소는 대개 앱 작성자가 놓은
/// 크고 이름 있는 컨트롤이다. 둘이 한 목록을 나눠 쓰면 상한에서 정확히 서로를 밀어낸다(실측: 이름 있는 컨트롤
/// 12개와 템플릿 부품 200개가 있는 화면에서 마크 40개가 전부 템플릿 부품이었고 기록에 남을 것은 0개였다.
/// 실제 앱에서도 요소 435개짜리 화면에서 5개만 기록되었다).
///
/// 그래서 여기서는 히트테스트도 겹침 판정도 하지 않는다. 기록은 그림이 아니라 글이므로 상자가 겹치는지는
/// 상관이 없고, 오직 "셀렉터로 유일하게 지목되는가" 만 따진다.
///
/// 예외를 던지지 않는다. 대상 앱 안에서 도는 코드이므로(결함 2 원칙) 읽을 수 없는 요소는 건너뛴다.
/// </summary>
public static class ScreenRegionPicker
{
    #region Public Methods

    /// <summary>
    /// 화면 기록에 남길 요소를 고릅니다.
    /// </summary>
    /// <param name="root">훑어 내려갈 뿌리. 보통 주 창이다.</param>
    /// <param name="maxRegions">돌려줄 요소의 최대 개수. 넘으면 트리 순서대로 앞쪽만 남긴다.</param>
    /// <returns>트리 순서대로 놓인 요소 목록.</returns>
    public static List<UIElement> Pick(DependencyObject root, int maxRegions)
    {
        if (root is null)
            return [];

        var seen = new List<(UIElement Element, string Id, string Name)>();
        var idCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var nameCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        Collect(root, seen, idCounts, nameCounts);

        var picked = new List<UIElement>();
        foreach (var (element, id, name) in seen)
        {
            if (picked.Count >= maxRegions)
                break;

            if (IsAddressable(id, name, idCounts, nameCounts))
                picked.Add(element);
        }

        return picked;
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// 트리를 훑으며 후보와 함께 id·name 이 각각 몇 번 나왔는지 셉니다.
    /// 유일성은 트리를 다 본 뒤에야 알 수 있으므로 고르는 일은 여기서 하지 않는다.
    /// 숨어 있는 가지에는 들어가지 않는다. 화면에 붙지 않은 트리에서는 IsVisible 이 거짓이 되므로
    /// <see cref="UIElement.Visibility"/> 로 판정한다.
    /// </summary>
    private static void Collect(
        DependencyObject node,
        List<(UIElement Element, string Id, string Name)> seen,
        Dictionary<string, int> idCounts,
        Dictionary<string, int> nameCounts)
    {
        if (node is UIElement element)
        {
            var id = IdOf(element);
            var name = NameOf(element);
            Count(id, idCounts);
            Count(name, nameCounts);

            if (element.Visibility != Visibility.Visible)
                return;

            // 컨트롤 템플릿이 만든 부품은 앱 작성자가 놓은 것이 아니라 스크롤바나 편집기의 내부 구조다.
            // 기록에 담아도 다음 방문에 쓸 일이 없고, 자리만 차지해 정작 필요한 컨트롤을 밀어낸다.
            //
            // 다만 부품 자신만 빼고 그 아래로는 계속 내려가야 한다. 창의 내용물은 창 템플릿 안쪽
            // ContentPresenter 아래에 놓이므로, 여기서 가지를 끊으면 앱 화면 전체를 놓친다
            // (실측: 요소 576개짜리 VisualPro 화면에서 셀렉터를 가진 영역이 0개였다).
            if (element is not FrameworkElement { TemplatedParent: not null })
                seen.Add((element, id, name));
        }

        foreach (var child in VisualTree.VisualChildren.Of(node, depth: 0, []))
            Collect(child, seen, idCounts, nameCounts);
    }

    /// <summary>
    /// 이 요소를 셀렉터로 유일하게 지목할 수 있는지.
    /// 저장할 때 id 를 먼저 쓰고 없으면 name 을 쓰므로, 판정 순서도 그대로 맞춘다. 순서가 어긋나면
    /// 겹치는 id 를 가진 요소가 name 덕분에 기록에 들어간 뒤, 정작 저장된 셀렉터는 그 겹치는 id 가 된다.
    /// 텍스트는 데이터가 바뀌면 가리키는 것이 없어지므로 기록의 근거로 삼지 않는다.
    /// </summary>
    private static bool IsAddressable(
        string id, string name,
        Dictionary<string, int> idCounts, Dictionary<string, int> nameCounts)
    {
        if (!string.IsNullOrEmpty(id))
            return idCounts.TryGetValue(id, out var ids) && ids == 1;

        return !string.IsNullOrEmpty(name)
            && nameCounts.TryGetValue(name, out var names) && names == 1;
    }

    /// <summary>비어 있지 않은 값의 등장 횟수를 셉니다.</summary>
    private static void Count(string value, Dictionary<string, int> counts)
    {
        if (string.IsNullOrEmpty(value))
            return;

        counts[value] = counts.TryGetValue(value, out var current) ? current + 1 : 1;
    }

    /// <summary>AutomationId. 읽을 수 없으면 빈 문자열.</summary>
    private static string IdOf(UIElement element)
    {
        try
        {
            return System.Windows.Automation.AutomationProperties.GetAutomationId(element) ?? "";
        }
        catch (Exception)
        {
            return "";
        }
    }

    /// <summary>x:Name. 읽을 수 없으면 빈 문자열.</summary>
    private static string NameOf(UIElement element)
    {
        try
        {
            return (element as FrameworkElement)?.Name ?? "";
        }
        catch (Exception)
        {
            return "";
        }
    }

    #endregion
}

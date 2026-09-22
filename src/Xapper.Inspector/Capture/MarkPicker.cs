using System.Windows;
using System.Windows.Media;

namespace Xapper.Inspector.Capture;

/// <summary>
/// 번호 상자를 받을 요소 하나. 사각형은 캡처 대상의 DIP 좌표계 기준이므로 DPI 와 축소 배율에 무관하다.
/// </summary>
/// <param name="Number">그림에 그릴 번호. 1부터 센다.</param>
/// <param name="Element">상자를 받을 요소.</param>
/// <param name="Rect">캡처 대상 좌표계에서의 사각형.</param>
/// <param name="Anchor">
/// 세션을 넘겨 이 요소에 다시 닿기 위한 기준점 셀렉터("id=…" 또는 "name=…").
/// 요소 스스로 셀렉터로 지목될 수 있으면 null 이다. 그때는 좌표가 필요 없다.
/// </param>
/// <param name="AnchorX">기준점 안에서 상자 중심의 가로 비율(0.0~1.0). 기준점이 없으면 null.</param>
/// <param name="AnchorY">기준점 안에서 상자 중심의 세로 비율(0.0~1.0). 기준점이 없으면 null.</param>
public sealed record MarkCandidate(
    int Number, UIElement Element, Rect Rect, string? Anchor = null, double? AnchorX = null, double? AnchorY = null);

/// <summary>
/// annotate 스크린샷에서 번호 상자를 받을 요소를 고른다.
/// 기준은 "그 자리를 클릭하면 실제로 닿는가" 하나다: 자기 중심에서 히트테스트를 했을 때 자기 자신이나
/// 자기 후손이 잡히는 요소만 남긴다. 이 한 조건이 가려진 요소를 걸러내고, 부모와 자식에 상자가 겹쳐
/// 그려지는 것도 막는다. 히트테스트는 언제나 가장 깊은 요소를 돌려주기 때문이다.
///
/// 예외를 던지지 않는다. 대상 앱 안에서 도는 코드이므로(결함 2 원칙) 읽을 수 없는 요소는 건너뛴다.
/// </summary>
public static class MarkPicker
{
    #region Fields

    /// <summary>
    /// 컨테이너 판정에 넘길 후보 수를 상한의 몇 배까지 둘지. 걸러진 뒤에도 상한을 채울 만큼은 남겨야 하므로
    /// 여유를 두되, 이차 비용이 커지지 않게 묶는다.
    /// </summary>
    private const int ComparisonHeadroom = 4;

    #endregion

    #region Public Methods

    /// <summary>
    /// 번호 상자를 받을 요소를 고릅니다.
    /// </summary>
    /// <param name="captured">캡처 대상. 사각형은 이 요소의 좌표계로 계산된다.</param>
    /// <param name="bounds">캡처된 영역. 이 밖에 놓인 요소는 제외한다.</param>
    /// <param name="maxMarks">상자의 최대 개수. 넘으면 면적이 작은 것을 남긴다.</param>
    /// <param name="minSize">상자를 그릴 최소 너비·높이 (DIP). 더 작으면 번호를 얹을 자리가 없다.</param>
    /// <param name="omitted">상한 때문에 제외한 요소 수.</param>
    /// <returns>번호가 1부터 매겨진 후보 목록.</returns>
    public static List<MarkCandidate> Pick(
        UIElement captured, Rect bounds, int maxMarks, double minSize, out int omitted)
    {
        var reachable = new List<(UIElement Element, Rect Rect)>();

        // 기준점은 target 셀렉터로 쓰이는데, 그 셀렉터는 정확히 하나 맞을 때만 실행된다. 그래서 유일한
        // id·name 만 기준점이 될 수 있다. 트리를 훑는 김에 세어 두면 기준점마다 다시 훑지 않아도 된다.
        var idCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        var nameCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        Collect(captured, captured, bounds, minSize, reachable, idCounts, nameCounts);

        // 컨테이너 판정은 후보끼리 서로 견주므로 개수의 제곱에 비례한다. 셀이 수천 개인 그리드 화면에서는
        // 그것만으로 대상 앱의 UI 스레드가 1초 가까이 멎는다(측정: 후보 5000개에 763ms). 어차피 상한만큼만
        // 남길 것이고 남기는 기준도 "작은 것 우선" 이므로, 견주기 전에 작은 쪽부터 넉넉히 추려 비용을 묶는다.
        var trimmed = reachable
            .OrderBy(entry => AreaOf(entry.Rect))
            .Take(maxMarks * ComparisonHeadroom)
            .ToList();

        var useful = DropContainers(Deduplicate(trimmed));
        omitted = Math.Max(0, useful.Count - maxMarks);

        // 상한을 넘으면 면적이 작은 것을 남긴다. 큰 것은 대개 배경이나 컨테이너이고,
        // 찾고 있는 것은 작고 이름 없는 셀이다.
        var kept = useful
            .OrderBy(entry => entry.Rect.Width * entry.Rect.Height)
            .Take(maxMarks)
            .ToList();

        var marks = new List<MarkCandidate>(kept.Count);
        for (var index = 0; index < kept.Count; index++)
        {
            var entry = kept[index];
            var (anchor, anchorX, anchorY) = AnchorFor(entry.Element, entry.Rect, captured, idCounts, nameCounts);
            marks.Add(new MarkCandidate(index + 1, entry.Element, entry.Rect, anchor, anchorX, anchorY));
        }

        return marks;
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// 세션을 넘겨 이 요소에 다시 닿을 기준점을 정합니다.
    /// 요소 스스로 id·name·text 중 하나라도 있으면 셀렉터로 충분하므로 기준점을 붙이지 않는다.
    /// 그렇지 않으면 위로 올라가며 유일한 id 를, 없으면 유일한 name 을 가진 가장 가까운 조상을 찾는다.
    /// 텍스트는 데이터에 따라 바뀌어 기준으로 삼기 약하므로 쓰지 않는다.
    /// </summary>
    private static (string? Anchor, double? X, double? Y) AnchorFor(
        UIElement element, Rect rect, UIElement captured,
        Dictionary<string, int> idCounts, Dictionary<string, int> nameCounts)
    {
        if (IsAddressableItself(element))
            return (null, null, null);

        for (DependencyObject? node = VisualParentOf(element); node is not null; node = VisualParentOf(node))
        {
            if (node is not UIElement ancestor)
                continue;

            var selector = SelectorFor(ancestor, idCounts, nameCounts);
            if (selector is null)
            {
                if (ReferenceEquals(node, captured))
                    break;
                continue;
            }

            var anchorRect = ReferenceEquals(ancestor, captured)
                ? new Rect(captured.RenderSize)
                : RectOf(ancestor, captured);
            if (anchorRect is not { Width: > 0, Height: > 0 } box)
                return (null, null, null);

            var x = Ratio(rect.X + rect.Width / 2 - box.X, box.Width);
            var y = Ratio(rect.Y + rect.Height / 2 - box.Y, box.Height);
            return (selector, x, y);
        }

        return (null, null, null);
    }

    /// <summary>요소 스스로 target 셀렉터로 지목될 수 있는지.</summary>
    private static bool IsAddressableItself(UIElement element)
    {
        return !string.IsNullOrEmpty(IdOf(element))
            || !string.IsNullOrEmpty(NameOf(element))
            || !string.IsNullOrEmpty(VisualTree.ElementText.Of(element));
    }

    /// <summary>이 요소를 유일하게 가리키는 셀렉터. 유일하지 않거나 없으면 null.</summary>
    private static string? SelectorFor(
        UIElement element, Dictionary<string, int> idCounts, Dictionary<string, int> nameCounts)
    {
        var id = IdOf(element);
        if (!string.IsNullOrEmpty(id) && idCounts.TryGetValue(id, out var ids) && ids == 1)
            return $"id={id}";

        var name = NameOf(element);
        if (!string.IsNullOrEmpty(name) && nameCounts.TryGetValue(name, out var names) && names == 1)
            return $"name={name}";

        return null;
    }

    /// <summary>비율을 0.0~1.0 으로 자릅니다. 기준 사각형 밖으로 나간 중심점을 그대로 쓰면 조작이 빗나간다.</summary>
    private static double Ratio(double offset, double length)
        => Math.Round(Math.Clamp(offset / length, 0, 1), 3);

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

    /// <summary>FrameworkElement.Name. 읽을 수 없으면 빈 문자열.</summary>
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

    /// <summary>값이 있으면 출현 횟수를 하나 올립니다.</summary>
    private static void Count(string value, Dictionary<string, int> counts)
    {
        if (value.Length == 0)
            return;

        counts[value] = counts.TryGetValue(value, out var seen) ? seen + 1 : 1;
    }

    /// <summary>
    /// 같은 자리를 차지하는 후보를 하나만 남깁니다.
    /// 컨트롤 템플릿은 항목과 똑같은 크기의 Border 를 두는 일이 흔해서, 둘 다 상자를 받으면 테두리가 겹쳐 보인다.
    /// 시각 트리를 위에서 아래로 훑었으므로 먼저 담긴 것이 바깥쪽이고, 그쪽이 의미 있는 요소다.
    /// </summary>
    private static List<(UIElement Element, Rect Rect)> Deduplicate(List<(UIElement Element, Rect Rect)> reachable)
    {
        var kept = new List<(UIElement Element, Rect Rect)>();
        var seen = new HashSet<(int X, int Y, int Width, int Height)>();

        foreach (var entry in reachable)
        {
            var key = ((int)Math.Round(entry.Rect.X), (int)Math.Round(entry.Rect.Y),
                (int)Math.Round(entry.Rect.Width), (int)Math.Round(entry.Rect.Height));
            if (seen.Add(key))
                kept.Add(entry);
        }

        return kept;
    }

    /// <summary>
    /// 다른 후보를 품고 있는 후보를 제외합니다.
    /// 클릭해서 무언가가 일어나는 것은 가장 안쪽 요소이고, 컨테이너까지 상자를 받으면 그림이 큰 테두리로
    /// 뒤덮여 오히려 읽을 수 없게 된다. 품은 것이 없는 큰 요소(빈 캔버스 등)는 실제 대상일 수 있으므로 남긴다.
    /// </summary>
    private static List<(UIElement Element, Rect Rect)> DropContainers(List<(UIElement Element, Rect Rect)> candidates)
    {
        return candidates
            .Where(entry => !candidates.Any(other =>
                AreaOf(other.Rect) < AreaOf(entry.Rect) && entry.Rect.Contains(other.Rect)))
            .ToList();
    }

    /// <summary>사각형의 면적.</summary>
    private static double AreaOf(Rect rect) => rect.Width * rect.Height;

    /// <summary>
    /// 시각 트리를 위에서 아래로 훑어 닿을 수 있는 요소를 모읍니다.
    /// 보이지 않는 요소는 그 가지 전체를 건너뛴다: 그 아래의 것도 화면에 없다.
    /// </summary>
    private static void Collect(
        DependencyObject node, UIElement captured, Rect bounds, double minSize,
        List<(UIElement Element, Rect Rect)> reachable,
        Dictionary<string, int> idCounts, Dictionary<string, int> nameCounts)
    {
        if (node is UIElement element)
        {
            Count(IdOf(element), idCounts);
            Count(NameOf(element), nameCounts);

            // 화면에 붙지 않은 트리에서는 IsVisible 이 거짓이므로 Visibility 로 판정한다.
            // 위에서 아래로 훑기 때문에 조상이 숨어 있으면 그 가지에 아예 들어오지 않는다.
            if (element.Visibility != Visibility.Visible)
                return;

            if (!ReferenceEquals(element, captured) && RectOf(element, captured) is { } rect)
            {
                if (rect.Width >= minSize && rect.Height >= minSize
                    && bounds.Contains(rect.TopLeft) && bounds.Contains(rect.BottomRight)
                    && IsTopmostAtItsCentre(element, captured, rect))
                {
                    reachable.Add((element, rect));
                }
            }
        }

        foreach (var child in VisualChildrenOf(node))
            Collect(child, captured, bounds, minSize, reachable, idCounts, nameCounts);
    }

    /// <summary>캡처 대상 좌표계에서의 사각형. 변환할 수 없으면 null.</summary>
    private static Rect? RectOf(UIElement element, UIElement captured)
    {
        try
        {
            var transform = element.TransformToAncestor(captured);
            var rect = transform.TransformBounds(new Rect(element.RenderSize));
            return rect.IsEmpty ? null : rect;
        }
        catch (Exception)
        {
            // 캡처 대상의 후손이 아니거나 변환이 불가능한 요소는 상자를 받지 않는다.
            return null;
        }
    }

    /// <summary>사각형 중심에서 히트테스트를 했을 때 자기 자신이나 자기 후손이 잡히는지.</summary>
    private static bool IsTopmostAtItsCentre(UIElement element, UIElement captured, Rect rect)
    {
        try
        {
            var centre = new Point(rect.X + rect.Width / 2, rect.Y + rect.Height / 2);
            if (VisualTreeHelper.HitTest(captured, centre) is not { } hit)
                return false;

            for (DependencyObject? node = hit.VisualHit; node is not null; node = VisualParentOf(node))
            {
                if (ReferenceEquals(node, element))
                    return true;
            }

            return false;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>비주얼 부모. 읽을 수 없으면 null.</summary>
    private static DependencyObject? VisualParentOf(DependencyObject node)
    {
        try
        {
            return node is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(node)
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>순회할 수 있는 비주얼 자식. 읽을 수 없는 자식은 건너뛴다.</summary>
    private static List<DependencyObject> VisualChildrenOf(DependencyObject node)
    {
        var children = new List<DependencyObject>();
        if (node is not Visual and not System.Windows.Media.Media3D.Visual3D)
            return children;

        int count;
        try
        {
            count = VisualTreeHelper.GetChildrenCount(node);
        }
        catch (Exception)
        {
            return children;
        }

        for (var index = 0; index < count; index++)
        {
            try
            {
                if (VisualTreeHelper.GetChild(node, index) is { } child)
                    children.Add(child);
            }
            catch (Exception)
            {
            }
        }

        return children;
    }

    #endregion
}

using System.Windows;
using System.Windows.Media;

namespace Xapper.Inspector.Capture;

/// <summary>
/// 번호 상자를 받을 요소 하나. 사각형은 캡처 대상의 DIP 좌표계 기준이므로 DPI 와 축소 배율에 무관하다.
/// </summary>
/// <param name="Number">그림에 그릴 번호. 1부터 센다.</param>
/// <param name="Element">상자를 받을 요소.</param>
/// <param name="Rect">캡처 대상 좌표계에서의 사각형.</param>
public sealed record MarkCandidate(int Number, UIElement Element, Rect Rect);

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
        Collect(captured, captured, bounds, minSize, reachable);

        omitted = Math.Max(0, reachable.Count - maxMarks);

        // 상한을 넘으면 면적이 작은 것을 남긴다. 큰 것은 대개 배경이나 컨테이너이고,
        // 찾고 있는 것은 작고 이름 없는 셀이다.
        var kept = reachable
            .OrderBy(entry => entry.Rect.Width * entry.Rect.Height)
            .Take(maxMarks)
            .ToList();

        var marks = new List<MarkCandidate>(kept.Count);
        for (var index = 0; index < kept.Count; index++)
            marks.Add(new MarkCandidate(index + 1, kept[index].Element, kept[index].Rect));

        return marks;
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// 시각 트리를 위에서 아래로 훑어 닿을 수 있는 요소를 모읍니다.
    /// 보이지 않는 요소는 그 가지 전체를 건너뛴다: 그 아래의 것도 화면에 없다.
    /// </summary>
    private static void Collect(
        DependencyObject node, UIElement captured, Rect bounds, double minSize,
        List<(UIElement Element, Rect Rect)> reachable)
    {
        if (node is UIElement element)
        {
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
            Collect(child, captured, bounds, minSize, reachable);
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

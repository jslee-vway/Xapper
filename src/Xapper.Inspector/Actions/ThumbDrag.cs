using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace Xapper.Inspector.Actions;

/// <summary>
/// Thumb 이 처리하는 드래그를 실제 마우스 입력 없이 수행하는 헬퍼.
/// 스플리터·슬라이더·스크롤바는 모두 Thumb 의 드래그 이벤트로 움직이며, 그 이벤트는 이동량을 숫자로
/// 받는다. 그래서 커서를 옮기지 않고도, 오히려 실제 마우스보다 정확하게 조작할 수 있다.
/// </summary>
internal static class ThumbDrag
{
    #region Public Methods

    /// <summary>
    /// 드래그를 시작하는 지점에 실제로 놓인 Thumb 을 찾습니다.
    /// 요소 아래 아무 Thumb 이나 집으면 안 된다 — 스크롤 가능한 목록은 어디에나 스크롤바를 품고 있어서,
    /// 항목을 끌어내려는 요청이 조용히 스크롤로 바뀌고 성공으로 보고된다.
    /// 시작 지점에서 히트테스트한 뒤 그 자리에서 위로 올라가며 찾고, 출발 요소를 벗어나면 멈춘다.
    /// </summary>
    /// <param name="source">드래그의 출발 요소.</param>
    /// <param name="startScreenPoint">드래그를 시작하는 화면 좌표.</param>
    /// <returns>그 지점이 속한 Thumb. 없으면 null.</returns>
    /// <remarks>UI 스레드에서 호출해야 합니다.</remarks>
    public static Thumb? FindAt(UIElement source, Point startScreenPoint)
    {
        var window = Window.GetWindow(source);
        if (window is null)
            return null;

        if (VisualTreeHelper.HitTest(window, window.PointFromScreen(startScreenPoint)) is not { } hit)
            return null;

        for (DependencyObject? node = hit.VisualHit; node is not null; node = VisualParentOf(node))
        {
            if (node is Thumb thumb)
                return thumb;

            // 출발 요소보다 위로 올라가면 그 지점의 것이 아니다.
            if (ReferenceEquals(node, source))
                break;
        }

        return null;
    }

    /// <summary>
    /// 비주얼 부모를 반환합니다. 비주얼이 아닌 노드에서는 탐색을 멈춥니다.
    /// </summary>
    private static DependencyObject? VisualParentOf(DependencyObject node)
    {
        if (node is not Visual && node is not System.Windows.Media.Media3D.Visual3D)
            return null;

        return VisualTreeHelper.GetParent(node);
    }

    /// <summary>
    /// 지정된 이동량만큼 드래그가 일어난 것처럼 Thumb 의 이벤트를 발생시킵니다.
    /// </summary>
    /// <param name="thumb">대상 Thumb.</param>
    /// <param name="delta">Thumb 좌표계에서의 이동량.</param>
    /// <param name="grabPoint">Thumb 안에서 붙잡은 지점. 실제 드래그는 이 값을 함께 넘긴다.</param>
    /// <remarks>UI 스레드에서 호출해야 합니다.</remarks>
    public static void Perform(Thumb thumb, Vector delta, Point grabPoint)
    {
        thumb.RaiseEvent(new DragStartedEventArgs(grabPoint.X, grabPoint.Y)
        {
            RoutedEvent = Thumb.DragStartedEvent,
            Source = thumb
        });

        thumb.RaiseEvent(new DragDeltaEventArgs(delta.X, delta.Y)
        {
            RoutedEvent = Thumb.DragDeltaEvent,
            Source = thumb
        });

        thumb.RaiseEvent(new DragCompletedEventArgs(delta.X, delta.Y, canceled: false)
        {
            RoutedEvent = Thumb.DragCompletedEvent,
            Source = thumb
        });
    }

    #endregion
}

using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace Xapper.Inspector.VisualTree;

/// <summary>
/// 이 UI 스레드가 소유한 입력 소스를 열거하는 헬퍼.
/// WPF는 팝업·컨텍스트 메뉴·드롭다운도 각자 HwndSource로 띄우는데, 그것들은 Window가 아니라서
/// Application.Current.Windows 에는 나타나지 않고 부모 창의 시각 트리로도 도달할 수 없다.
/// 열린 메뉴 안의 항목을 찾으려면 창 목록이 아니라 여기서 출발해야 한다.
/// 다만 목록 자체는 프로세스 전체를 담으므로 반드시 호출 스레드의 것만 남긴다 — 다른 UI 스레드의
/// 요소를 건드리면 교차 스레드 예외가 나고, 그 예외는 순회 전체를 무너뜨린다.
/// </summary>
internal static class VisualRoots
{
    #region Win32

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(POINT point);

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr handle, uint flags);

    /// <summary>창이 속한 최상위 창을 구하는 GetAncestor 플래그.</summary>
    private const uint GA_ROOT = 2;

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// 호출 스레드가 소유한, 살아 있는 입력 소스를 열거합니다. 보이는지 여부는 호출자가 판단합니다.
    /// </summary>
    /// <returns>루트 비주얼을 가진 HwndSource 목록.</returns>
    /// <remarks>UI 스레드에서 호출해야 합니다.</remarks>
    public static List<HwndSource> Sources()
    {
        var sources = new List<HwndSource>();

        foreach (PresentationSource source in PresentationSource.CurrentSources)
        {
            if (source is not HwndSource hwndSource
                || hwndSource.IsDisposed
                || hwndSource.Handle == IntPtr.Zero
                || hwndSource.RootVisual is null)
            {
                continue;
            }

            // 목록은 프로세스 전체를 담는다. 다른 UI 스레드가 띄운 창의 요소는 읽는 것만으로도 예외가 나므로
            // 여기서 걸러야 한다. 그런 창은 이 스레드에서 다룰 수 있는 대상이 아니다.
            if (!hwndSource.CheckAccess())
                continue;

            sources.Add(hwndSource);
        }

        return sources;
    }

    /// <summary>
    /// 화면 좌표에서 가장 위에 있는 이 앱의 창을 찾습니다.
    /// 겹친 창의 우열은 운영체제가 알고 있으므로 그 판정을 그대로 쓴다.
    /// </summary>
    /// <param name="screenPoint">화면 좌표.</param>
    /// <returns>그 지점의 창. 이 앱의 창이 아니면 null.</returns>
    /// <remarks>UI 스레드에서 호출해야 합니다.</remarks>
    public static HwndSource? SourceAt(Point screenPoint)
    {
        var handle = WindowFromPoint(new POINT { X = (int)screenPoint.X, Y = (int)screenPoint.Y });
        if (handle == IntPtr.Zero)
            return null;

        var sources = Sources();
        var match = Match(sources, handle);
        if (match is not null)
            return match;

        // WindowFromPoint 은 자식 창까지 내려간다. WebView2 나 WindowsFormsHost 처럼 자식 HWND 를 품은
        // 지점에서는 우리 소스가 아닌 핸들이 나오므로, 그 창이 속한 최상위 창으로 올라가 다시 찾는다.
        var root = GetAncestor(handle, GA_ROOT);
        return root == handle ? null : Match(sources, root);
    }

    /// <summary>
    /// 지정된 화면 좌표에 실제로 그려져 있는 요소를 찾고, 그 위 조상들을 함께 돌려줍니다.
    /// 히트테스트를 거치므로 다른 요소에 가려져 있으면 가린 쪽이 잡힌다 — 실제 클릭이 닿는 곳과 같다.
    /// </summary>
    /// <param name="source">대상 창.</param>
    /// <param name="screenPoint">화면 좌표.</param>
    /// <param name="maxAncestors">함께 돌려줄 조상의 최대 수.</param>
    /// <returns>가장 깊은 요소부터 위로 올라가는 순서의 목록. 그 지점에 아무것도 없으면 빈 목록.</returns>
    /// <remarks>UI 스레드에서 호출해야 합니다.</remarks>
    public static List<DependencyObject> ElementsAt(HwndSource source, Point screenPoint, int maxAncestors)
    {
        var elements = new List<DependencyObject>();

        if (source.RootVisual is not Visual root)
            return elements;

        var localPoint = root.PointFromScreen(screenPoint);
        if (VisualTreeHelper.HitTest(root, localPoint) is not { } hit)
            return elements;

        for (DependencyObject? node = hit.VisualHit;
             node is not null && elements.Count <= maxAncestors;
             node = VisualParentOf(node))
        {
            elements.Add(node);
        }

        return elements;
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// 창 핸들에 해당하는 입력 소스를 찾습니다.
    /// </summary>
    private static HwndSource? Match(List<HwndSource> sources, IntPtr handle)
    {
        foreach (var source in sources)
        {
            if (source.Handle == handle)
                return source;
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

    #endregion
}

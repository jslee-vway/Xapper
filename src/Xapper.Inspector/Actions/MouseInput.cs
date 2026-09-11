using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace Xapper.Inspector.Actions;

/// <summary>
/// Win32 SendInput 기반 마우스 입력 주입과 좌표 변환을 담당하는 내부 헬퍼.
/// WPF 요소 로컬 좌표 → 스크린 디바이스 좌표 → 가상 데스크톱 절대 좌표(0~65535) 변환을 한곳에 모아
/// 클릭과 드래그가 동일한 좌표 계산을 공유하도록 한다.
/// 듀얼 모니터, Per-Monitor DPI 환경에서 동작.
/// </summary>
internal static class MouseInput
{
    #region Win32

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int nIndex);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public MOUSEINPUT mi;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    private const uint INPUT_MOUSE = 0;
    private const uint MOUSEEVENTF_ABSOLUTE = 0x8000;
    private const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
    private const uint MOUSEEVENTF_LEFTUP = 0x0004;
    private const uint MOUSEEVENTF_VIRTUALDESK = 0x4000;
    private const uint MOUSEEVENTF_MOVE = 0x0001;

    private const int SM_XVIRTUALSCREEN = 76;
    private const int SM_YVIRTUALSCREEN = 77;
    private const int SM_CXVIRTUALSCREEN = 78;
    private const int SM_CYVIRTUALSCREEN = 79;

    #endregion

    #region Public Methods

    /// <summary>
    /// 요소 내 상대 좌표(0.0~1.0)를 스크린 디바이스 좌표로 변환합니다.
    /// </summary>
    /// <param name="element">기준이 되는 요소.</param>
    /// <param name="relativeX">요소 내 상대 X 좌표 (0.0=좌, 1.0=우).</param>
    /// <param name="relativeY">요소 내 상대 Y 좌표 (0.0=상, 1.0=하).</param>
    /// <remarks>UI 스레드에서 호출해야 합니다.</remarks>
    public static Point ToScreenPoint(UIElement element, double relativeX, double relativeY)
    {
        var size = element.RenderSize;
        return element.PointToScreen(new Point(size.Width * relativeX, size.Height * relativeY));
    }

    /// <summary>
    /// 요소가 속한 윈도우를 포그라운드로 올리고, 실제로 활성화됐는지 확인합니다.
    /// </summary>
    /// <param name="element">대상 요소.</param>
    /// <returns>호출 후 대상 윈도우가 실제 포그라운드이면 true.</returns>
    /// <remarks>
    /// UI 스레드에서 호출해야 합니다.
    /// 운영체제는 호출 프로세스가 포그라운드를 소유하지 않으면 활성화 요청을 거부할 수 있으므로,
    /// 요청 성공 여부가 아니라 실제 포그라운드 창을 다시 조회해 판정한다.
    /// 활성화되지 않은 창에 보낸 첫 입력은 창 활성화에 소비되어 컨트롤까지 도달하지 않는다.
    /// </remarks>
    public static bool BringToForeground(UIElement element)
    {
        var window = Window.GetWindow(element);
        if (window is null)
            return false;

        if (PresentationSource.FromVisual(window) is not HwndSource hwndSource)
            return false;

        SetForegroundWindow(hwndSource.Handle);
        return GetForegroundWindow() == hwndSource.Handle;
    }

    /// <summary>
    /// 지정된 지점에서 실제 마우스 클릭이 해당 요소에 닿는지 히트테스트로 확인합니다.
    /// </summary>
    /// <param name="element">확인할 대상 요소.</param>
    /// <param name="relativeX">요소 내 상대 X 좌표 (0.0~1.0).</param>
    /// <param name="relativeY">요소 내 상대 Y 좌표 (0.0~1.0).</param>
    /// <returns>그 지점의 히트테스트 결과가 요소 자신이거나 자손이면 true.</returns>
    /// <remarks>
    /// UI 스레드에서 호출해야 합니다.
    /// 다른 요소가 위를 덮고 있거나 IsHitTestVisible이 꺼져 있으면 false가 되며,
    /// 이는 실제 사용자가 그 요소를 누를 수 없다는 뜻이다.
    /// </remarks>
    public static bool IsReachableByMouse(UIElement element, double relativeX, double relativeY)
    {
        var size = element.RenderSize;
        if (size.Width <= 0 || size.Height <= 0)
            return false;

        var window = Window.GetWindow(element);
        if (window is null)
            return false;

        var pointInWindow = element.TranslatePoint(
            new Point(size.Width * relativeX, size.Height * relativeY), window);

        if (VisualTreeHelper.HitTest(window, pointInWindow) is not { } hit)
            return false;

        for (DependencyObject? node = hit.VisualHit; node is not null; node = GetVisualParent(node))
        {
            if (ReferenceEquals(node, element))
                return true;
        }

        return false;
    }

    /// <summary>
    /// 이동, 버튼 누름, 버튼 뗌을 하나의 SendInput 호출로 원자적으로 전송합니다.
    /// 좌표가 각 입력 이벤트에 포함되므로 이동과 클릭 사이의 타이밍 이슈가 없습니다.
    /// </summary>
    /// <param name="screenPoint">클릭할 스크린 좌표.</param>
    public static void ClickAt(Point screenPoint)
    {
        var (x, y) = ToVirtualDesktop(screenPoint);
        var flags = MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK;

        var inputs = new[]
        {
            CreateInput(x, y, flags | MOUSEEVENTF_MOVE),
            CreateInput(x, y, flags | MOUSEEVENTF_LEFTDOWN),
            CreateInput(x, y, flags | MOUSEEVENTF_LEFTUP)
        };

        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    /// <summary>
    /// 같은 지점에서 누름-뗌을 두 번 반복해 더블클릭을 전송합니다. 한 번의 SendInput 호출로 보내므로
    /// 두 클릭 사이 간격이 시스템 더블클릭 시간 안에 들어가, OS 가 더블클릭으로 인식합니다.
    /// </summary>
    /// <param name="screenPoint">더블클릭할 스크린 좌표.</param>
    public static void DoubleClickAt(Point screenPoint)
    {
        var (x, y) = ToVirtualDesktop(screenPoint);
        var flags = MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK;

        var inputs = new[]
        {
            CreateInput(x, y, flags | MOUSEEVENTF_MOVE),
            CreateInput(x, y, flags | MOUSEEVENTF_LEFTDOWN),
            CreateInput(x, y, flags | MOUSEEVENTF_LEFTUP),
            CreateInput(x, y, flags | MOUSEEVENTF_LEFTDOWN),
            CreateInput(x, y, flags | MOUSEEVENTF_LEFTUP)
        };

        SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>());
    }

    /// <summary>지정된 스크린 좌표로 마우스 포인터를 이동시킵니다.</summary>
    /// <param name="screenPoint">이동할 스크린 좌표.</param>
    public static void MoveTo(Point screenPoint) => Send(screenPoint, MOUSEEVENTF_MOVE);

    /// <summary>지정된 스크린 좌표에서 왼쪽 버튼을 누릅니다.</summary>
    /// <param name="screenPoint">버튼을 누를 스크린 좌표.</param>
    public static void LeftDown(Point screenPoint) => Send(screenPoint, MOUSEEVENTF_LEFTDOWN);

    /// <summary>지정된 스크린 좌표에서 왼쪽 버튼을 뗍니다.</summary>
    /// <param name="screenPoint">버튼을 뗄 스크린 좌표.</param>
    public static void LeftUp(Point screenPoint) => Send(screenPoint, MOUSEEVENTF_LEFTUP);

    #endregion

    #region Private Methods

    /// <summary>
    /// 비주얼 트리 부모를 반환합니다. 비주얼이 아닌 노드에서는 탐색을 멈춥니다.
    /// </summary>
    private static DependencyObject? GetVisualParent(DependencyObject node)
    {
        if (node is not Visual && node is not Visual3D)
            return null;

        return VisualTreeHelper.GetParent(node);
    }

    /// <summary>
    /// 지정된 동작 플래그로 단일 마우스 입력을 전송합니다.
    /// </summary>
    private static void Send(Point screenPoint, uint actionFlag)
    {
        var (x, y) = ToVirtualDesktop(screenPoint);
        var input = CreateInput(x, y, MOUSEEVENTF_ABSOLUTE | MOUSEEVENTF_VIRTUALDESK | actionFlag);
        SendInput(1, new[] { input }, Marshal.SizeOf<INPUT>());
    }

    /// <summary>
    /// 스크린 좌표를 가상 데스크톱 절대 좌표(0~65535)로 정규화합니다. 멀티 모니터 배치를 반영합니다.
    /// </summary>
    private static (int X, int Y) ToVirtualDesktop(Point screenPoint)
    {
        var vx = GetSystemMetrics(SM_XVIRTUALSCREEN);
        var vy = GetSystemMetrics(SM_YVIRTUALSCREEN);
        var vw = GetSystemMetrics(SM_CXVIRTUALSCREEN);
        var vh = GetSystemMetrics(SM_CYVIRTUALSCREEN);

        return ((int)(((screenPoint.X - vx) * 65535.0) / vw),
                (int)(((screenPoint.Y - vy) * 65535.0) / vh));
    }

    /// <summary>
    /// 지정된 절대 좌표와 플래그를 담은 마우스 INPUT 구조체를 만듭니다.
    /// </summary>
    private static INPUT CreateInput(int absoluteX, int absoluteY, uint flags)
    {
        var input = new INPUT { type = INPUT_MOUSE };
        input.mi.dx = absoluteX;
        input.mi.dy = absoluteY;
        input.mi.dwFlags = flags;
        return input;
    }

    #endregion
}

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;

namespace Xapper.Inspector.Actions;

/// <summary>
/// 실제 커서를 옮기지 않고 대상 창에 마우스 제스처(클릭·더블클릭·우클릭·휠·드래그)를 전달하는 정적 클래스.
/// 입력 상태(<c>GetKeyState</c>/<c>GetCursorPos</c> 등)는 <see cref="InputSpoof"/> 가 스푸프하고, 여기서는
/// 그 스푸프를 켠 채 창에 WM 마우스 메시지를 보낸다. 커서는 한 픽셀도 움직이지 않고, 다른 프로세스의
/// 포커스도 뺏지 않는다. 후크를 설치할 수 없으면 false 를 돌려 호출자가 실제 입력으로 폴백한다.
/// </summary>
internal static class SyntheticMouse
{
    #region Fields

    private const uint WM_MOUSEMOVE = 0x0200;
    private const uint WM_LBUTTONDOWN = 0x0201;
    private const uint WM_LBUTTONUP = 0x0202;
    private const int MK_LBUTTON = 0x0001;
    private const uint WM_RBUTTONDOWN = 0x0204;
    private const uint WM_RBUTTONUP = 0x0205;
    private const uint WM_MOUSEWHEEL = 0x020A;
    private const int MK_RBUTTON = 0x0002;
    private const int MK_SHIFT = 0x0004;
    private const int MK_CONTROL = 0x0008;

    /// <summary>휠 한 눈금의 delta(Win32 WHEEL_DELTA).</summary>
    private const int WheelDelta = 120;

    /// <summary>WM 메시지 사이 간격 (밀리초). 대상 앱이 눌림을 처리한 뒤 떼기를 받도록 여유를 준다.</summary>
    private const int StepDelayMs = 16;

    /// <summary>드래그를 나누어 보낼 이동 횟수.</summary>
    private const int DragSteps = 8;

    /// <summary>WM 메시지 하나가 대상 UI 스레드에서 처리되기를 기다리는 한도 (밀리초).</summary>
    private const uint SendTimeoutMs = 3000;

    private static readonly object Gate = new();

    #endregion

    #region Public Methods

    /// <summary>지정된 창의 한 지점을 커서 이동 없이 클릭합니다.</summary>
    /// <param name="hwnd">대상 요소가 속한 최상위 창(HwndSource) 핸들.</param>
    /// <param name="screen">클릭할 스크린 디바이스 좌표.</param>
    /// <param name="modifiers">함께 눌린 것으로 볼 수식키.</param>
    /// <returns>후킹 경로로 수행했으면 true, 후크를 설치할 수 없어 쓸 수 없으면 false.</returns>
    public static bool TryClick(IntPtr hwnd, Point screen, ModifierKeys modifiers = ModifierKeys.None)
    {
        if (hwnd == IntPtr.Zero || !InputSpoof.EnsureInstalled())
            return false;

        lock (Gate)
        {
            var client = ToClient(hwnd, screen);
            var mk = MkFlags(modifiers);
            InputSpoof.BeginMouse(screen, modifiers);
            try
            {
                Send(hwnd, WM_MOUSEMOVE, (IntPtr)mk, client);
                Sleep();
                InputSpoof.SetLeftDown(true);
                Send(hwnd, WM_LBUTTONDOWN, (IntPtr)(mk | MK_LBUTTON), client);
                Sleep();
                InputSpoof.SetLeftDown(false);
                Send(hwnd, WM_LBUTTONUP, (IntPtr)mk, client);
                Sleep();
            }
            finally
            {
                InputSpoof.EndMouse();
            }
        }

        return true;
    }

    /// <summary>지정된 창의 한 지점을 커서 이동 없이 더블클릭합니다(누름-뗌 두 번, WPF 가 ClickCount=2 로 인식).</summary>
    /// <param name="hwnd">대상 요소가 속한 최상위 창(HwndSource) 핸들.</param>
    /// <param name="screen">더블클릭할 스크린 디바이스 좌표.</param>
    /// <param name="modifiers">함께 눌린 것으로 볼 수식키.</param>
    /// <returns>후킹 경로로 수행했으면 true, 후크를 설치할 수 없어 쓸 수 없으면 false.</returns>
    public static bool TryDoubleClick(IntPtr hwnd, Point screen, ModifierKeys modifiers = ModifierKeys.None)
    {
        if (hwnd == IntPtr.Zero || !InputSpoof.EnsureInstalled())
            return false;

        lock (Gate)
        {
            var client = ToClient(hwnd, screen);
            var mk = MkFlags(modifiers);
            InputSpoof.BeginMouse(screen, modifiers);
            try
            {
                Send(hwnd, WM_MOUSEMOVE, (IntPtr)mk, client);
                Sleep();
                for (var i = 0; i < 2; i++)
                {
                    InputSpoof.SetLeftDown(true);
                    Send(hwnd, WM_LBUTTONDOWN, (IntPtr)(mk | MK_LBUTTON), client);
                    Sleep();
                    InputSpoof.SetLeftDown(false);
                    Send(hwnd, WM_LBUTTONUP, (IntPtr)mk, client);
                    Sleep();
                }
            }
            finally
            {
                InputSpoof.EndMouse();
            }
        }

        return true;
    }

    /// <summary>
    /// 지정된 창의 한 지점을 커서 이동 없이 우클릭합니다. WPF 는 오른쪽 버튼 뗌에서 ContextMenu 를 연다.
    /// 접근성에 우클릭 패턴은 없으므로 항상 이 좌표 제스처로 수행한다.
    /// </summary>
    /// <param name="hwnd">대상 요소가 속한 최상위 창(HwndSource) 핸들.</param>
    /// <param name="screen">우클릭할 스크린 디바이스 좌표.</param>
    /// <param name="modifiers">함께 눌린 것으로 볼 수식키.</param>
    /// <returns>후킹 경로로 수행했으면 true, 후크를 설치할 수 없으면 false.</returns>
    public static bool TryRightClick(IntPtr hwnd, Point screen, ModifierKeys modifiers = ModifierKeys.None)
    {
        if (hwnd == IntPtr.Zero || !InputSpoof.EnsureInstalled())
            return false;

        lock (Gate)
        {
            var client = ToClient(hwnd, screen);
            var mk = MkFlags(modifiers);
            InputSpoof.BeginMouse(screen, modifiers);
            try
            {
                Send(hwnd, WM_MOUSEMOVE, (IntPtr)mk, client);
                Sleep();
                InputSpoof.SetRightDown(true);
                Send(hwnd, WM_RBUTTONDOWN, (IntPtr)(mk | MK_RBUTTON), client);
                Sleep();
                InputSpoof.SetRightDown(false);
                Send(hwnd, WM_RBUTTONUP, (IntPtr)mk, client);
                Sleep();
            }
            finally
            {
                InputSpoof.EndMouse();
            }
        }

        return true;
    }

    /// <summary>
    /// 지정된 창의 한 지점에서 커서 이동 없이 휠을 굴립니다. WPF 는 스푸프된 커서 위치 아래 요소로 MouseWheel 을 라우팅한다.
    /// </summary>
    /// <param name="hwnd">대상 요소가 속한 최상위 창(HwndSource) 핸들.</param>
    /// <param name="screen">휠을 굴릴 스크린 디바이스 좌표.</param>
    /// <param name="notches">굴릴 눈금 수. 양수는 위(앞), 음수는 아래(뒤).</param>
    /// <param name="modifiers">함께 눌린 것으로 볼 수식키(Ctrl+휠 줌 등).</param>
    /// <returns>후킹 경로로 수행했으면 true, 후크를 설치할 수 없으면 false.</returns>
    public static bool TryWheel(IntPtr hwnd, Point screen, int notches, ModifierKeys modifiers = ModifierKeys.None)
    {
        if (hwnd == IntPtr.Zero || !InputSpoof.EnsureInstalled())
            return false;

        lock (Gate)
        {
            InputSpoof.BeginMouse(screen, modifiers);
            try
            {
                Send(hwnd, WM_MOUSEMOVE, (IntPtr)MkFlags(modifiers), ToClient(hwnd, screen));
                Sleep();
                var (wParam, lParam) = PackWheel(notches, modifiers, screen);
                Send(hwnd, WM_MOUSEWHEEL, wParam, lParam);
                Sleep();
            }
            finally
            {
                InputSpoof.EndMouse();
            }
        }

        return true;
    }

    /// <summary>지정된 창에서 한 지점을 누른 채 다른 지점까지 끌고 뗍니다. 커서는 움직이지 않습니다.</summary>
    /// <param name="hwnd">대상 요소가 속한 최상위 창(HwndSource) 핸들.</param>
    /// <param name="start">드래그 시작 스크린 디바이스 좌표.</param>
    /// <param name="end">드래그 끝 스크린 디바이스 좌표.</param>
    /// <param name="modifiers">함께 눌린 것으로 볼 수식키(Ctrl+드래그 복사 등).</param>
    /// <returns>후킹 경로로 수행했으면 true, 후크를 설치할 수 없어 쓸 수 없으면 false.</returns>
    public static bool TryDrag(IntPtr hwnd, Point start, Point end, ModifierKeys modifiers = ModifierKeys.None)
    {
        if (hwnd == IntPtr.Zero || !InputSpoof.EnsureInstalled())
            return false;

        lock (Gate)
        {
            var mk = MkFlags(modifiers);
            InputSpoof.BeginMouse(start, modifiers);
            try
            {
                Send(hwnd, WM_MOUSEMOVE, (IntPtr)mk, ToClient(hwnd, start));
                Sleep();
                InputSpoof.SetLeftDown(true);
                Send(hwnd, WM_LBUTTONDOWN, (IntPtr)(mk | MK_LBUTTON), ToClient(hwnd, start));
                Sleep();

                for (var step = 1; step <= DragSteps; step++)
                {
                    var progress = (double)step / DragSteps;
                    var point = new Point(
                        start.X + (end.X - start.X) * progress,
                        start.Y + (end.Y - start.Y) * progress);
                    InputSpoof.SetPosition(point);
                    Send(hwnd, WM_MOUSEMOVE, (IntPtr)(mk | MK_LBUTTON), ToClient(hwnd, point));
                    Sleep();
                }

                InputSpoof.SetLeftDown(false);
                Send(hwnd, WM_LBUTTONUP, (IntPtr)mk, ToClient(hwnd, end));
                Sleep();
            }
            finally
            {
                InputSpoof.EndMouse();
            }
        }

        return true;
    }

    /// <summary>
    /// WM_MOUSEWHEEL 의 wParam(상위 워드 delta, 하위 워드 MK 플래그)과 lParam(스크린 좌표 — 버튼 메시지와 달리
    /// 클라이언트 좌표가 아니다)을 만듭니다. 테스트에서 검증하기 위해 internal 로 둔다.
    /// </summary>
    /// <param name="notches">굴릴 눈금 수. 양수는 위(앞), 음수는 아래(뒤).</param>
    /// <param name="modifiers">함께 눌린 것으로 볼 수식키.</param>
    /// <param name="screen">휠을 굴릴 스크린 디바이스 좌표.</param>
    internal static (IntPtr WParam, IntPtr LParam) PackWheel(int notches, ModifierKeys modifiers, Point screen)
    {
        var delta = unchecked((short)(notches * WheelDelta));
        var packed = ((uint)(ushort)delta << 16) | (uint)MkFlags(modifiers);
        // 32비트 프로세스에서 IntPtr(long) 은 checked 라 음수 delta(0xFF88…)에서 넘친다. int 로 잘라 넣는다.
        var wParam = (IntPtr)unchecked((int)packed);
        var lParam = PackPoint((int)Math.Round(screen.X), (int)Math.Round(screen.Y));
        return (wParam, lParam);
    }

    #endregion

    #region Private Methods

    private static void Sleep() => Thread.Sleep(StepDelayMs);

    /// <summary>수식키를 WM 마우스 메시지의 wParam MK 플래그로 바꿉니다(Alt 는 MK 플래그가 없다).</summary>
    private static int MkFlags(ModifierKeys modifiers)
    {
        var flags = 0;
        if (modifiers.HasFlag(ModifierKeys.Control)) flags |= MK_CONTROL;
        if (modifiers.HasFlag(ModifierKeys.Shift)) flags |= MK_SHIFT;
        return flags;
    }

    /// <summary>스크린 좌표를 대상 창의 클라이언트 좌표로 바꿔 lParam 으로 만듭니다.</summary>
    private static IntPtr ToClient(IntPtr hwnd, Point screen)
    {
        var point = new POINT { X = (int)Math.Round(screen.X), Y = (int)Math.Round(screen.Y) };
        ScreenToClient(hwnd, ref point);
        return PackPoint(point.X, point.Y);
    }

    /// <summary>x 를 하위 워드, y 를 상위 워드에 담은 lParam 을 만듭니다(음수 좌표도 16비트로 잘라 담는다).</summary>
    private static IntPtr PackPoint(int x, int y)
    {
        return (IntPtr)(((y & 0xFFFF) << 16) | (x & 0xFFFF));
    }

    private static void Send(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        // 타임아웃이 있는 동기 전송. 대상 UI 스레드가 멎으면(ABORTIFHUNG) 무한정 기다리지 않고 넘어간다.
        SendMessageTimeoutW(hwnd, message, wParam, lParam, SMTO_ABORTIFHUNG, SendTimeoutMs, out _);
    }

    #endregion

    #region Interop

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    private const uint SMTO_ABORTIFHUNG = 0x0002;

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessageTimeoutW(
        IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeoutMs, out IntPtr result);

    [DllImport("user32.dll")]
    private static extern bool ScreenToClient(IntPtr hWnd, ref POINT point);

    #endregion
}

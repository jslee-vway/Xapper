using System.Runtime.InteropServices;
using System.Windows;
using MinHook;

namespace Xapper.Inspector.Actions;

/// <summary>
/// 실제 커서를 옮기지 않고 대상 창에 마우스 입력을 전달하는 정적 클래스.
///
/// WPF 는 클릭이 진짜인지 판단할 때 OS 에 직접 묻는다(<c>MouseDevice.GetButtonStateFromSystem</c> →
/// <c>GetKeyState</c>, 입력 위치 → <c>GetCursorPos</c>/<c>GetMessagePos</c>). 그래서 대상 프로세스 안에서
/// 그 네 함수를 짧게 후킹해 "버튼이 눌렸고 커서가 이 지점에 있다"고 답하게 하고, 창에 WM 마우스 메시지를
/// 보낸다. 커서는 한 픽셀도 움직이지 않고, 다른 프로세스의 포커스도 뺏지 않는다(폐기 프로브로 두
/// 프로세스에서 확인). 후크는 프로세스 수명 동안 한 번만 설치하고 <see cref="_spoof"/> 플래그로만 켠다 —
/// 꺼져 있으면 트램폴린(원함수)을 그대로 부르므로 대상 앱에 투명하다.
///
/// 후크를 설치할 수 없으면(라이브러리 실패) 이 경로는 쓸 수 없으므로 호출자가 실제 입력으로 폴백한다.
/// </summary>
internal static class SyntheticMouse
{
    #region Fields

    private const int VK_LBUTTON = 0x01;
    private const uint WM_MOUSEMOVE = 0x0200;
    private const uint WM_LBUTTONDOWN = 0x0201;
    private const uint WM_LBUTTONUP = 0x0202;
    private const int MK_LBUTTON = 0x0001;

    /// <summary>WM 메시지 사이 간격 (밀리초). 대상 앱이 눌림을 처리한 뒤 떼기를 받도록 여유를 준다.</summary>
    private const int StepDelayMs = 16;

    /// <summary>드래그를 나누어 보낼 이동 횟수.</summary>
    private const int DragSteps = 8;

    /// <summary>WM 메시지 하나가 대상 UI 스레드에서 처리되기를 기다리는 한도 (밀리초). 멎은 창에 무한정 매달리지 않는다.</summary>
    private const uint SendTimeoutMs = 3000;

    private static readonly object Gate = new();

    // 후크는 대상 앱의 여러 스레드에서 불리고, 이 값들은 IPC 스레드가 Gate 안에서 쓴다. 교차 스레드에서
    // 최신 값을 읽도록 volatile 로 둔다(그래도 스푸프가 켜진 짧은 동안 대상 전체에 영향을 주는 것은 설계상 감수).
    private static volatile bool _spoof;
    private static volatile int _sx;
    private static volatile int _sy;
    private static volatile bool _lDown;

    private static bool _installAttempted;
    private static bool _installed;

    // 후크가 설치되어 있는 동안 트램폴린(원함수)을 붙들고 있어야 스푸프가 아닐 때 원래 동작을 부를 수 있다.
    // EnableHooks 는 이 넷이 모두 대입된 뒤에만 후크를 켜므로, 켜진 뒤 디투어가 부를 때는 항상 null 이 아니다.
    private static GetKeyStateDelegate? _getKeyStateOrig;
    private static GetAsyncKeyStateDelegate? _getAsyncKeyStateOrig;
    private static GetCursorPosDelegate? _getCursorPosOrig;
    private static GetMessagePosDelegate? _getMessagePosOrig;

    #endregion

    #region Public Methods

    /// <summary>
    /// 지정된 창의 한 지점을 커서 이동 없이 클릭합니다.
    /// </summary>
    /// <param name="hwnd">대상 요소가 속한 최상위 창(HwndSource) 핸들.</param>
    /// <param name="screen">클릭할 스크린 디바이스 좌표.</param>
    /// <returns>후킹 경로로 수행했으면 true, 후크를 설치할 수 없어 쓸 수 없으면 false.</returns>
    public static bool TryClick(IntPtr hwnd, Point screen)
    {
        if (hwnd == IntPtr.Zero || !EnsureInstalled())
            return false;

        lock (Gate)
        {
            var client = ToClient(hwnd, screen);
            Begin(screen);
            try
            {
                Send(hwnd, WM_MOUSEMOVE, IntPtr.Zero, client);
                Sleep();
                _lDown = true;
                Send(hwnd, WM_LBUTTONDOWN, (IntPtr)MK_LBUTTON, client);
                Sleep();
                _lDown = false;
                Send(hwnd, WM_LBUTTONUP, IntPtr.Zero, client);
                Sleep();
            }
            finally
            {
                End();
            }
        }

        return true;
    }

    /// <summary>
    /// 지정된 창에서 한 지점을 누른 채 다른 지점까지 끌고 뗍니다. 커서는 움직이지 않습니다.
    /// </summary>
    /// <param name="hwnd">대상 요소가 속한 최상위 창(HwndSource) 핸들.</param>
    /// <param name="start">드래그 시작 스크린 디바이스 좌표.</param>
    /// <param name="end">드래그 끝 스크린 디바이스 좌표.</param>
    /// <returns>후킹 경로로 수행했으면 true, 후크를 설치할 수 없어 쓸 수 없으면 false.</returns>
    public static bool TryDrag(IntPtr hwnd, Point start, Point end)
    {
        if (hwnd == IntPtr.Zero || !EnsureInstalled())
            return false;

        lock (Gate)
        {
            Begin(start);
            try
            {
                Send(hwnd, WM_MOUSEMOVE, IntPtr.Zero, ToClient(hwnd, start));
                Sleep();
                _lDown = true;
                Send(hwnd, WM_LBUTTONDOWN, (IntPtr)MK_LBUTTON, ToClient(hwnd, start));
                Sleep();

                for (var step = 1; step <= DragSteps; step++)
                {
                    var progress = (double)step / DragSteps;
                    var point = new Point(
                        start.X + (end.X - start.X) * progress,
                        start.Y + (end.Y - start.Y) * progress);
                    _sx = (int)Math.Round(point.X);
                    _sy = (int)Math.Round(point.Y);
                    Send(hwnd, WM_MOUSEMOVE, (IntPtr)MK_LBUTTON, ToClient(hwnd, point));
                    Sleep();
                }

                _lDown = false;
                Send(hwnd, WM_LBUTTONUP, IntPtr.Zero, ToClient(hwnd, end));
                Sleep();
            }
            finally
            {
                End();
            }
        }

        return true;
    }

    #endregion

    #region Private Methods

    /// <summary>스푸프를 켜고 커서 위치를 지정합니다. 반드시 <see cref="End"/>와 짝지어 호출한다.</summary>
    private static void Begin(Point screen)
    {
        _sx = (int)Math.Round(screen.X);
        _sy = (int)Math.Round(screen.Y);
        _lDown = false;
        _spoof = true;
    }

    /// <summary>스푸프를 끕니다. 이후 후크는 다시 원함수 값을 돌려준다.</summary>
    private static void End()
    {
        _spoof = false;
        _lDown = false;
    }

    private static void Sleep() => Thread.Sleep(StepDelayMs);

    /// <summary>스크린 좌표를 대상 창의 클라이언트 좌표로 바꿔 lParam 으로 만듭니다.</summary>
    private static IntPtr ToClient(IntPtr hwnd, Point screen)
    {
        var point = new POINT { X = (int)Math.Round(screen.X), Y = (int)Math.Round(screen.Y) };
        ScreenToClient(hwnd, ref point);
        return (IntPtr)(((point.Y & 0xFFFF) << 16) | (point.X & 0xFFFF));
    }

    private static void Send(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        // 타임아웃이 있는 동기 전송. 대상 UI 스레드가 멎으면(ABORTIFHUNG) 무한정 기다리지 않고 넘어간다.
        SendMessageTimeoutW(hwnd, message, wParam, lParam, SMTO_ABORTIFHUNG, SendTimeoutMs, out _);
    }

    /// <summary>
    /// 후크를 처음 한 번 설치합니다. 설치에 실패하면 다시 시도하지 않고 계속 false 를 돌려준다 —
    /// 실패한 후킹을 매번 재시도하면 대상 앱을 흔들 뿐이다.
    /// </summary>
    private static bool EnsureInstalled()
    {
        lock (Gate)
        {
            if (_installAttempted)
                return _installed;

            _installAttempted = true;
            try
            {
                var engine = new HookEngine();
                _getKeyStateOrig = engine.CreateHook("user32.dll", "GetKeyState", new GetKeyStateDelegate(GetKeyStateHook));
                _getAsyncKeyStateOrig = engine.CreateHook("user32.dll", "GetAsyncKeyState", new GetAsyncKeyStateDelegate(GetAsyncKeyStateHook));
                _getCursorPosOrig = engine.CreateHook("user32.dll", "GetCursorPos", new GetCursorPosDelegate(GetCursorPosHook));
                _getMessagePosOrig = engine.CreateHook("user32.dll", "GetMessagePos", new GetMessagePosDelegate(GetMessagePosHook));
                engine.EnableHooks();
                _installed = true;
            }
            catch
            {
                // 후킹을 못 걸면 이 경로는 포기하고 호출자가 실제 입력으로 폴백한다.
                _installed = false;
            }

            return _installed;
        }
    }

    #endregion

    #region Hooks

    private static short GetKeyStateHook(int nVirtKey)
    {
        if (_spoof && nVirtKey == VK_LBUTTON)
            return _lDown ? unchecked((short)0x8000) : (short)0;

        var orig = _getKeyStateOrig;
        return orig is not null ? orig(nVirtKey) : (short)0;
    }

    private static short GetAsyncKeyStateHook(int vKey)
    {
        if (_spoof && vKey == VK_LBUTTON)
            return _lDown ? unchecked((short)0x8000) : (short)0;

        var orig = _getAsyncKeyStateOrig;
        return orig is not null ? orig(vKey) : (short)0;
    }

    private static bool GetCursorPosHook(out POINT point)
    {
        if (_spoof)
        {
            point = new POINT { X = _sx, Y = _sy };
            return true;
        }

        var orig = _getCursorPosOrig;
        if (orig is not null)
            return orig(out point);

        point = default;
        return false;
    }

    private static uint GetMessagePosHook()
    {
        if (_spoof)
            return (uint)(((_sy & 0xFFFF) << 16) | (_sx & 0xFFFF));

        var orig = _getMessagePosOrig;
        return orig is not null ? orig() : 0u;
    }

    #endregion

    #region Interop

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate short GetKeyStateDelegate(int nVirtKey);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate short GetAsyncKeyStateDelegate(int vKey);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate bool GetCursorPosDelegate(out POINT point);

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate uint GetMessagePosDelegate();

    private const uint SMTO_ABORTIFHUNG = 0x0002;

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessageTimeoutW(
        IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeoutMs, out IntPtr result);

    [DllImport("user32.dll")]
    private static extern bool ScreenToClient(IntPtr hWnd, ref POINT point);

    #endregion
}

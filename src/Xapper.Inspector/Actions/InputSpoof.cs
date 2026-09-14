using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using MinHook;

namespace Xapper.Inspector.Actions;

/// <summary>
/// 대상 프로세스 안에서 user32 입력 상태 함수를 후킹해 "커서가 이 지점에 있고, 이 버튼·수식키가 눌려 있다"고
/// 답하게 만드는 정적 클래스. 마우스 제스처(<see cref="SyntheticMouse"/>)와 키 주입(<see cref="KeyboardInput"/>)이
/// 함께 쓴다.
///
/// WPF 는 클릭이 진짜인지, 수식키가 눌렸는지를 OS 에 직접 묻는다 — <c>MouseDevice.GetButtonStateFromSystem</c> 과
/// <c>KeyboardDevice.GetKeyStatesFromSystem</c> 이 <c>GetKeyState</c> 를, 입력 위치가 <c>GetCursorPos</c>/<c>GetMessagePos</c>
/// 를 부른다. 그래서 그 네 함수를 후킹한다. 수식키는 WPF 가 <c>Key.LeftCtrl</c>/<c>RightCtrl</c>(VK_LCONTROL/VK_RCONTROL)
/// 로 묻지 일반 VK_CONTROL 로 묻지 않는다 — 일반 VK 만 스푸프하면 <c>Keyboard.Modifiers</c> 가 None 을 돌려준다
/// (측정 확인). 그래서 L/R 변형과 일반 VK 를 모두 답한다.
///
/// WPF 는 마우스를 "활성화"할 때 <c>WindowFromPoint(커서)</c> 가 자기 창인지도 확인한다. 커서 위치만 스푸프하면
/// 그 지점을 다른 프로세스의 창(사용자가 앞에 띄운 창)이 덮고 있을 때 WPF 가 마우스를 활성화하지 않아 합성 클릭이
/// 조용히 버려진다(측정 확인). 그래서 <c>WindowFromPoint</c> 도 후킹해, 덮은 창이 다른 프로세스 것이면 대상 창을
/// 답한다 — 사람이 딴 창에서 일하는 동안에도 클릭이 대상에 닿게. 같은 프로세스의 창(팝업·대화상자)이 덮은 경우는
/// 실제 사용자도 그 창을 누르게 되므로 스푸프하지 않는다(호출자가 <see cref="IsCoveredByOwnWindow"/> 로 미리 걸러 실제 입력으로 폴백).
///
/// 스푸프가 켜진 동안 수식키 VK 는 요청된 조합만 눌림으로 답하고 나머지는 0 으로 답한다: Xapper 가 입력을 넣는
/// 짧은 순간에 사람이 실제로 누르고 있는 키가 섞여 들어가지 않게 격리한다.
///
/// 마우스 제스처의 버튼·수식키 답은 <b>UI 스레드가 우리가 보낸 메시지를 처리하는 동안</b>(<c>InSendMessage</c>)에만
/// 한다. 앱 핸들러가 느리면 그동안 프로세스 전체에 가짜 버튼 상태를 답하게 되어 사람이 같은 앱에 넣는 진짜 클릭이
/// 엉뚱하게 처리된다(측정: 느린 핸들러 2초 동안 프로세스가 가짜 커서를 봤다). 핸들러 안에서 모달 대화상자가 열리면
/// 그 중첩 루프 안에서도 <c>InSendMessage</c> 는 true 로 남으므로 이 조건만으로는 부족하다 — 그 경우는
/// <see cref="SyntheticMouse"/> 의 워치독이 메시지당 제한 시간(250ms)이 지나면 스푸프를 통째로 꺼서 막는다.
/// 같은 스레드에서 보낸 메시지(테스트)는 <c>InSendMessage</c> 가 false 라 보낸 스레드도 함께 허용한다.
/// 커서 위치 스푸프는 제스처 내내 유지한다 — 사이사이 WPF 가 레이아웃 뒤 <c>Synchronize</c> 로 커서를 다시 읽는데,
/// 그때 진짜 커서를 보면 드래그 중 요소가 튀기 때문이다. 진짜 클릭은 자기 메시지의 좌표로 히트테스트되므로 영향이 없다.
///
/// 후크는 프로세스 수명 동안 한 번만 설치하고 플래그로만 켠다 — 꺼져 있으면 트램폴린(원함수)을 그대로 부르므로
/// 대상 앱에 투명하다. 디투어 델리게이트와 엔진은 반드시 정적 필드로 붙들어야 한다: GC 되면 대상 앱이 후킹된
/// 함수를 부르는 순간 CLR 이 FailFast 한다.
/// </summary>
internal static class InputSpoof
{
    #region Fields

    private const int VK_LBUTTON = 0x01;
    private const int VK_RBUTTON = 0x02;
    private const int VK_SHIFT = 0x10;
    private const int VK_CONTROL = 0x11;
    private const int VK_MENU = 0x12;
    private const int VK_LSHIFT = 0xA0;
    private const int VK_RSHIFT = 0xA1;
    private const int VK_LCONTROL = 0xA2;
    private const int VK_RCONTROL = 0xA3;
    private const int VK_LMENU = 0xA4;
    private const int VK_RMENU = 0xA5;

    /// <summary>GetKeyState 가 "눌림" 으로 돌려주는 값(최상위 비트).</summary>
    private const short PressedState = unchecked((short)0x8000);

    private static readonly object Gate = new();

    // 후크는 대상 앱의 여러 스레드에서 불리고, 이 값들은 호출자 스레드가 쓴다. 교차 스레드에서 최신 값을
    // 읽도록 volatile 로 둔다. 요청은 MCP 서버가 직렬화하므로 두 조작이 겹치지는 않는다.
    private static volatile bool _spoofMouse;
    private static volatile bool _spoofModifiers;
    private static volatile int _sx;
    private static volatile int _sy;
    private static volatile bool _lDown;
    private static volatile bool _rDown;
    private static volatile ModifierKeys _modifiers;
    private static volatile IntPtr _hwnd;
    private static volatile uint _senderThreadId;

    private static bool _installAttempted;
    private static bool _installed;

    // 트램폴린(원함수): 스푸프가 아닐 때 원래 동작을 부른다. EnableHooks 는 다섯이 모두 대입된 뒤에만 켠다.
    private static GetKeyStateDelegate? _getKeyStateOrig;
    private static GetAsyncKeyStateDelegate? _getAsyncKeyStateOrig;
    private static GetCursorPosDelegate? _getCursorPosOrig;
    private static GetMessagePosDelegate? _getMessagePosOrig;
    private static WindowFromPointDelegate? _windowFromPointOrig;

    // 디투어 델리게이트와 엔진의 정적 루팅(GC 방지).
    private static HookEngine? _engine;
    private static GetKeyStateDelegate? _getKeyStateDetour;
    private static GetAsyncKeyStateDelegate? _getAsyncKeyStateDetour;
    private static GetCursorPosDelegate? _getCursorPosDetour;
    private static GetMessagePosDelegate? _getMessagePosDetour;
    private static WindowFromPointDelegate? _windowFromPointDetour;

    #endregion

    #region Public Methods

    /// <summary>
    /// 후크를 처음 한 번 설치합니다. 제스처·키 주입 전에 부른다. 처음 호출 때 설치를 시도한다.
    /// 실패하면 다시 시도하지 않고 계속 false 를 돌려준다 — 실패한 후킹을 매번 재시도하면 대상 앱을 흔들 뿐이다.
    /// </summary>
    public static bool EnsureInstalled()
    {
        lock (Gate)
        {
            if (_installAttempted)
                return _installed;

            _installAttempted = true;
            try
            {
                // 디투어를 먼저 정적 필드에 붙들고, 그 필드에 담긴 델리게이트로만 후킹한다.
                _engine = new HookEngine();
                _getKeyStateDetour = GetKeyStateHook;
                _getAsyncKeyStateDetour = GetAsyncKeyStateHook;
                _getCursorPosDetour = GetCursorPosHook;
                _getMessagePosDetour = GetMessagePosHook;
                _windowFromPointDetour = WindowFromPointHook;

                _getKeyStateOrig = _engine.CreateHook("user32.dll", "GetKeyState", _getKeyStateDetour);
                _getAsyncKeyStateOrig = _engine.CreateHook("user32.dll", "GetAsyncKeyState", _getAsyncKeyStateDetour);
                _getCursorPosOrig = _engine.CreateHook("user32.dll", "GetCursorPos", _getCursorPosDetour);
                _getMessagePosOrig = _engine.CreateHook("user32.dll", "GetMessagePos", _getMessagePosDetour);
                _windowFromPointOrig = _engine.CreateHook("user32.dll", "WindowFromPoint", _windowFromPointDetour);
                _engine.EnableHooks();
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

    /// <summary>
    /// 마우스 제스처용 스푸프를 켭니다: 커서 위치, 버튼(모두 뗀 상태), 수식키. 반드시 <see cref="EndMouse"/> 와 짝지어 호출한다.
    /// </summary>
    /// <param name="hwnd">제스처를 받을 대상 창. 다른 프로세스의 창이 그 지점을 덮고 있어도 WindowFromPoint 가 이 창을 답한다.</param>
    /// <param name="screen">커서가 있다고 답할 스크린 디바이스 좌표.</param>
    /// <param name="modifiers">눌려 있다고 답할 수식키. None 이면 모든 수식키를 안 눌림으로 답한다.</param>
    public static void BeginMouse(IntPtr hwnd, Point screen, ModifierKeys modifiers)
    {
        _hwnd = hwnd;
        _senderThreadId = GetCurrentThreadId();
        SetPosition(screen);
        _lDown = false;
        _rDown = false;
        _modifiers = modifiers;
        _spoofModifiers = true;
        _spoofMouse = true;
    }

    /// <summary>마우스 제스처용 스푸프를 끕니다. 이후 후크는 다시 원함수 값을 돌려준다.</summary>
    public static void EndMouse()
    {
        _spoofMouse = false;
        _hwnd = IntPtr.Zero;
        _senderThreadId = 0;
        _spoofModifiers = false;
        _lDown = false;
        _rDown = false;
        _modifiers = ModifierKeys.None;
    }

    /// <summary>
    /// 수식키만 스푸프합니다(키 주입용 — 커서 위치는 건드리지 않는다). 반드시 <see cref="EndModifiers"/> 와 짝지어 호출한다.
    /// </summary>
    /// <param name="modifiers">눌려 있다고 답할 수식키.</param>
    public static void BeginModifiers(ModifierKeys modifiers)
    {
        _modifiers = modifiers;
        _spoofModifiers = true;
    }

    /// <summary>수식키 스푸프를 끕니다.</summary>
    public static void EndModifiers()
    {
        _spoofModifiers = false;
        _modifiers = ModifierKeys.None;
    }

    /// <summary>스푸프 중 커서가 있다고 답할 위치를 바꿉니다(드래그 이동).</summary>
    public static void SetPosition(Point screen)
    {
        _sx = (int)Math.Round(screen.X);
        _sy = (int)Math.Round(screen.Y);
    }

    /// <summary>스푸프 중 왼쪽 버튼이 눌려 있다고 답할지 정합니다.</summary>
    public static void SetLeftDown(bool down) => _lDown = down;

    /// <summary>스푸프 중 오른쪽 버튼이 눌려 있다고 답할지 정합니다.</summary>
    public static void SetRightDown(bool down) => _rDown = down;

    /// <summary>
    /// 그 지점의 맨 위 창이 <paramref name="hwnd"/> 가 아닌 <b>같은 프로세스의</b> 창(팝업·대화상자)인지 판정합니다.
    /// 그런 경우 실제 사용자는 그 창을 누르게 되므로 스푸프로 뚫지 않고 호출자가 실제 입력으로 폴백해야 한다.
    /// 다른 프로세스의 창이 덮은 것은 스푸프가 처리하므로 false.
    /// </summary>
    /// <param name="hwnd">제스처 대상 창.</param>
    /// <param name="screen">확인할 스크린 좌표.</param>
    public static bool IsCoveredByOwnWindow(IntPtr hwnd, Point screen)
    {
        var top = RealWindowAt(screen);
        if (top == IntPtr.Zero || IsWithin(top, hwnd))
            return false;

        GetWindowThreadProcessId(top, out var pid);
        return pid == (uint)Environment.ProcessId;
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// 스푸프 대상 가상 키면 답할 상태를 돌려줍니다. 버튼은 마우스 스푸프 중에만, 수식키는 수식키 스푸프 중에만 답한다.
    /// </summary>
    private static bool TrySpoofKey(int virtualKey, out short state)
    {
        state = 0;

        if (_spoofMouse)
        {
            // 우리 메시지를 처리하는 중이 아니면(사람의 진짜 입력, 타이머 등) 진짜 상태를 답한다.
            if (!InSendMessage() && GetCurrentThreadId() != _senderThreadId)
                return false;

            if (virtualKey == VK_LBUTTON)
            {
                state = _lDown ? PressedState : (short)0;
                return true;
            }
            if (virtualKey == VK_RBUTTON)
            {
                state = _rDown ? PressedState : (short)0;
                return true;
            }
            return TrySpoofModifier(virtualKey, out state);
        }

        // 키 주입 경로: 키는 디스패처 작업 안에서 라우팅되므로(SendMessage 아님) 스푸프를 조건 없이 답한다.
        return _spoofModifiers && TrySpoofModifier(virtualKey, out state);
    }

    /// <summary>수식키 VK 면 요청된 조합에 따라 눌림/안 눌림을 답합니다.</summary>
    private static bool TrySpoofModifier(int virtualKey, out short state)
    {
        state = 0;
        var modifiers = _modifiers;
        switch (virtualKey)
        {
            case VK_CONTROL:
            case VK_LCONTROL:
            case VK_RCONTROL:
                state = modifiers.HasFlag(ModifierKeys.Control) ? PressedState : (short)0;
                return true;
            case VK_SHIFT:
            case VK_LSHIFT:
            case VK_RSHIFT:
                state = modifiers.HasFlag(ModifierKeys.Shift) ? PressedState : (short)0;
                return true;
            case VK_MENU:
            case VK_LMENU:
            case VK_RMENU:
                state = modifiers.HasFlag(ModifierKeys.Alt) ? PressedState : (short)0;
                return true;
            default:
                return false;
        }
    }

    #endregion

    #region Hooks

    /// <summary>스푸프와 무관하게 실제로 그 지점 맨 위에 있는 창을 돌려줍니다(트램폴린 우선, 없으면 원함수).</summary>
    private static IntPtr RealWindowAt(Point screen)
    {
        var point = new POINT { X = (int)Math.Round(screen.X), Y = (int)Math.Round(screen.Y) };
        var orig = _windowFromPointOrig;
        return orig is not null ? orig(point) : WindowFromPoint(point);
    }

    /// <summary>
    /// <paramref name="window"/> 가 대상 창 자신이거나 그 자손, 또는 같은 최상위 창에 속하는지 판정합니다.
    /// 대상이 최상위가 아닌 자식 HwndSource(ElementHost 안의 WPF 등)여도 덮인 것으로 오판하지 않게 한다.
    /// </summary>
    private static bool IsWithin(IntPtr window, IntPtr target)
    {
        return window == target || IsChild(target, window) || GetAncestor(window, GA_ROOT) == target;
    }

    private static IntPtr WindowFromPointHook(POINT point)
    {
        var orig = _windowFromPointOrig;
        var real = orig is not null ? orig(point) : IntPtr.Zero;
        if (!_spoofMouse)
            return real;

        var target = _hwnd;
        if (target == IntPtr.Zero || real == IntPtr.Zero || IsWithin(real, target))
            return real;

        // 다른 프로세스의 창이 덮고 있으면 대상 창을 답해 WPF 가 마우스를 활성화하게 한다.
        // 같은 프로세스의 창이면 실제 답을 유지한다(호출자가 IsCoveredByOwnWindow 로 미리 걸러 폴백한다).
        GetWindowThreadProcessId(real, out var pid);
        return pid == (uint)Environment.ProcessId ? real : target;
    }

    private static short GetKeyStateHook(int nVirtKey)
    {
        if (TrySpoofKey(nVirtKey, out var state))
            return state;

        var orig = _getKeyStateOrig;
        return orig is not null ? orig(nVirtKey) : (short)0;
    }

    private static short GetAsyncKeyStateHook(int vKey)
    {
        if (TrySpoofKey(vKey, out var state))
            return state;

        var orig = _getAsyncKeyStateOrig;
        return orig is not null ? orig(vKey) : (short)0;
    }

    private static bool GetCursorPosHook(out POINT point)
    {
        if (_spoofMouse)
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
        if (_spoofMouse)
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

    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate IntPtr WindowFromPointDelegate(POINT point);

    private const uint GA_ROOT = 2;

    [DllImport("user32.dll")]
    private static extern IntPtr WindowFromPoint(POINT point);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InSendMessage();

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsChild(IntPtr parent, IntPtr child);

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    #endregion
}

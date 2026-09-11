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
/// 스푸프가 켜진 동안 수식키 VK 는 요청된 조합만 눌림으로 답하고 나머지는 0 으로 답한다: Xapper 가 입력을 넣는
/// 짧은 순간에 사람이 실제로 누르고 있는 키가 섞여 들어가지 않게 격리한다.
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

    private static bool _installAttempted;
    private static bool _installed;

    // 트램폴린(원함수): 스푸프가 아닐 때 원래 동작을 부른다. EnableHooks 는 넷이 모두 대입된 뒤에만 켠다.
    private static GetKeyStateDelegate? _getKeyStateOrig;
    private static GetAsyncKeyStateDelegate? _getAsyncKeyStateOrig;
    private static GetCursorPosDelegate? _getCursorPosOrig;
    private static GetMessagePosDelegate? _getMessagePosOrig;

    // 디투어 델리게이트와 엔진의 정적 루팅(GC 방지).
    private static HookEngine? _engine;
    private static GetKeyStateDelegate? _getKeyStateDetour;
    private static GetAsyncKeyStateDelegate? _getAsyncKeyStateDetour;
    private static GetCursorPosDelegate? _getCursorPosDetour;
    private static GetMessagePosDelegate? _getMessagePosDetour;

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

                _getKeyStateOrig = _engine.CreateHook("user32.dll", "GetKeyState", _getKeyStateDetour);
                _getAsyncKeyStateOrig = _engine.CreateHook("user32.dll", "GetAsyncKeyState", _getAsyncKeyStateDetour);
                _getCursorPosOrig = _engine.CreateHook("user32.dll", "GetCursorPos", _getCursorPosDetour);
                _getMessagePosOrig = _engine.CreateHook("user32.dll", "GetMessagePos", _getMessagePosDetour);
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
    /// <param name="screen">커서가 있다고 답할 스크린 디바이스 좌표.</param>
    /// <param name="modifiers">눌려 있다고 답할 수식키. None 이면 모든 수식키를 안 눌림으로 답한다.</param>
    public static void BeginMouse(Point screen, ModifierKeys modifiers)
    {
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
        }

        if (_spoofModifiers)
        {
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
            }
        }

        return false;
    }

    #endregion

    #region Hooks

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

    #endregion
}

using System.Runtime.InteropServices;
using System.Windows.Input;

namespace Xapper.Inspector.Actions;

/// <summary>
/// 후크를 쓸 수 없을 때 실제 키보드 입력(SendInput)으로 수식키를 누르고 떼는 정적 클래스.
/// 이 입력은 OS 전체에 보이므로 사람이 다른 창에서 타이핑 중이면 그쪽까지 Ctrl 이 눌린다 — 그래서 후킹이 기본이고
/// 이건 폴백이다. 호출자는 <see cref="Press"/> 뒤에 반드시 <c>finally</c> 로 <see cref="Release"/> 를 불러
/// 사람의 키보드가 눌린 채 남지 않게 해야 한다.
/// </summary>
internal static class RealModifierKeys
{
    #region Win32

    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const ushort VK_LSHIFT = 0xA0;
    private const ushort VK_LCONTROL = 0xA2;
    private const ushort VK_LMENU = 0xA4;

    #endregion

    #region Public Methods

    /// <summary>요청된 수식키를 실제로 누릅니다(Ctrl → Shift → Alt 순). None 이면 아무것도 보내지 않는다.</summary>
    public static void Press(ModifierKeys modifiers)
    {
        foreach (var vk in VirtualKeysOf(modifiers))
            SendKey(vk, 0);
    }

    /// <summary>요청된 수식키를 실제로 뗍니다(누른 역순). None 이면 아무것도 보내지 않는다.</summary>
    public static void Release(ModifierKeys modifiers)
    {
        var keys = VirtualKeysOf(modifiers);
        for (var i = keys.Count - 1; i >= 0; i--)
            SendKey(keys[i], KEYEVENTF_KEYUP);
    }

    #endregion

    #region Private Methods

    private static List<ushort> VirtualKeysOf(ModifierKeys modifiers)
    {
        var keys = new List<ushort>(3);
        if (modifiers.HasFlag(ModifierKeys.Control)) keys.Add(VK_LCONTROL);
        if (modifiers.HasFlag(ModifierKeys.Shift)) keys.Add(VK_LSHIFT);
        if (modifiers.HasFlag(ModifierKeys.Alt)) keys.Add(VK_LMENU);
        return keys;
    }

    private static void SendKey(ushort virtualKey, uint flags)
    {
        var input = new Win32Input.INPUT { type = Win32Input.INPUT_KEYBOARD };
        input.u.ki.wVk = virtualKey;
        input.u.ki.dwFlags = flags;
        Win32Input.SendInput(1, new[] { input }, Marshal.SizeOf<Win32Input.INPUT>());
    }

    #endregion
}

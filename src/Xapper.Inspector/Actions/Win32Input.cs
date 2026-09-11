using System.Runtime.InteropServices;

namespace Xapper.Inspector.Actions;

/// <summary>
/// SendInput 에 넘기는 Win32 INPUT 구조체와 P/Invoke. 마우스(MouseInput)와 키보드(RealModifierKeys) 입력이
/// 같은 정의를 쓴다. INPUT 은 type + union 이고 cbSize 가 union 전체 크기와 맞아야 하므로 가장 큰 MOUSEINPUT 을
/// union 에 둔다(Sequential 로 두면 x86/x64 정렬을 런타임이 맞춘다).
/// </summary>
internal static class Win32Input
{
    internal const uint INPUT_MOUSE = 0;
    internal const uint INPUT_KEYBOARD = 1;

    [StructLayout(LayoutKind.Sequential)]
    internal struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    [StructLayout(LayoutKind.Explicit)]
    internal struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MOUSEINPUT
    {
        public int dx;
        public int dy;
        public uint mouseData;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [DllImport("user32.dll")]
    internal static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);
}

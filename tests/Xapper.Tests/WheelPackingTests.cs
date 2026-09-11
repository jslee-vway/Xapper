using System.Windows;
using System.Windows.Input;
using Xapper.Inspector.Actions;

namespace Xapper.Tests;

/// <summary>WM_MOUSEWHEEL 의 wParam/lParam 이 Win32 규약대로 채워지는지 검증한다(delta 는 상위 워드, MK 플래그는 하위 워드, lParam 은 스크린 좌표).</summary>
public class WheelPackingTests
{
    [Fact]
    public void PackWheel_PutsDeltaInHighWordAndModifiersInLowWord()
    {
        var (wParam, lParam) = SyntheticMouse.PackWheel(2, ModifierKeys.Control, new Point(100, 200));

        var w = (uint)(long)wParam;
        Assert.Equal((short)240, unchecked((short)(w >> 16)));   // 2 notches × 120
        Assert.Equal(0x0008u, w & 0xFFFF);                       // MK_CONTROL
        var l = (uint)(long)lParam;
        Assert.Equal(100u, l & 0xFFFF);
        Assert.Equal(200u, l >> 16);
    }

    [Fact]
    public void PackWheel_NegativeNotchesScrollDown()
    {
        var (wParam, _) = SyntheticMouse.PackWheel(-1, ModifierKeys.None, new Point(0, 0));

        var w = (uint)(long)wParam;
        Assert.Equal((short)-120, unchecked((short)(w >> 16)));
        Assert.Equal(0u, w & 0xFFFF);
    }

    [Fact]
    public void PackWheel_KeepsNegativeScreenCoordinates()
    {
        // 주 모니터 왼쪽/위쪽의 보조 모니터는 음수 스크린 좌표를 가진다.
        var (_, lParam) = SyntheticMouse.PackWheel(1, ModifierKeys.None, new Point(-100, -50));

        var l = (uint)(long)lParam;
        Assert.Equal((short)-100, unchecked((short)(l & 0xFFFF)));
        Assert.Equal((short)-50, unchecked((short)(l >> 16)));
    }
}

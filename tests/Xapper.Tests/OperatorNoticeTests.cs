using System.Runtime.InteropServices;
using Xapper.McpServer.Infrastructure;

namespace Xapper.Tests;

/// <summary>
/// 알림 창이 "알리기만 하고 아무것도 방해하지 않는다"는 성질을 Win32 로 직접 잰다: 전경 창을 그대로 두고,
/// 그 자리에 떨어지는 클릭을 삼키지 않으며, hide 하면 반드시 사라진다.
/// </summary>
[Collection(DesktopWindowCollection.Name)]
public class OperatorNoticeTests
{
    #region Interop

    private const int GwlExStyle = -20;
    private const long WsExTopmost = 0x00000008;
    private const long WsExTransparent = 0x00000020;
    private const long WsExToolWindow = 0x00000080;
    private const long WsExNoActivate = 0x08000000;

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT { public int Left; public int Top; public int Right; public int Bottom; }

    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(POINT point);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hWnd, int index);

    private static long ExtendedStyleOf(IntPtr handle) => GetWindowLongPtr(handle, GwlExStyle).ToInt64();

    #endregion

    [Fact]
    public async Task Show_PutsAnInertTopmostWindowOnScreen()
    {
        await using var notice = new OperatorNotice();

        var (shown, error) = await notice.ShowAsync("로그인 테스트", targetProcessId: null, CancellationToken.None);

        Assert.True(shown, error);
        Assert.True(notice.IsVisible);
        Assert.NotEqual(IntPtr.Zero, notice.Handle);
        Assert.True(IsWindowVisible(notice.Handle));
        var style = ExtendedStyleOf(notice.Handle);
        Assert.NotEqual(0, style & WsExTopmost);
        Assert.NotEqual(0, style & WsExNoActivate);
        Assert.NotEqual(0, style & WsExTransparent);
        Assert.NotEqual(0, style & WsExToolWindow);
    }

    [Fact]
    public async Task Show_LeavesTheForegroundWindowAlone()
    {
        await using var notice = new OperatorNotice();
        var foregroundBefore = GetForegroundWindow();

        var (shown, error) = await notice.ShowAsync(null, null, CancellationToken.None);

        // 알림 창이 포커스를 가져가면, 알리려던 바로 그 방해를 알림 창이 저지르는 꼴이다.
        Assert.True(shown, error);
        Assert.NotEqual(notice.Handle, GetForegroundWindow());
        Assert.Equal(foregroundBefore, GetForegroundWindow());
    }

    [Fact]
    public async Task Show_LetsClicksPassThroughTheNotice()
    {
        await using var notice = new OperatorNotice();
        var (shown, error) = await notice.ShowAsync(null, null, CancellationToken.None);
        Assert.True(shown, error);

        // 클릭 통과 창은 WindowFromPoint 에서도 건너뛰므로, 한가운데를 찍으면 다른 창이 나와야 한다.
        Assert.True(GetWindowRect(notice.Handle, out var rect));
        var centre = new POINT { X = (rect.Left + rect.Right) / 2, Y = (rect.Top + rect.Bottom) / 2 };
        Assert.NotEqual(notice.Handle, WindowFromPoint(centre));
    }

    [Fact]
    public async Task Hide_TakesTheWindowDown_AndShowBringsItBack()
    {
        await using var notice = new OperatorNotice();
        var (shown, error) = await notice.ShowAsync(null, null, CancellationToken.None);
        Assert.True(shown, error);

        await notice.HideAsync(CancellationToken.None);
        Assert.False(notice.IsVisible);
        Assert.False(IsWindowVisible(notice.Handle));

        (shown, error) = await notice.ShowAsync("again", null, CancellationToken.None);
        Assert.True(shown, error);
        Assert.True(IsWindowVisible(notice.Handle));
    }

    [Fact]
    public async Task Hide_BeforeAnyShow_DoesNothing()
    {
        await using var notice = new OperatorNotice();

        await notice.HideAsync(CancellationToken.None);

        Assert.False(notice.IsVisible);
        Assert.Equal(IntPtr.Zero, notice.Handle);
    }

    [Fact]
    public async Task Show_OverAWindow_SitsAtItsTopCentre()
    {
        await using var notice = new OperatorNotice();

        // 이 테스트 프로세스 자신의 창 위에 놓아 본다: PID 를 넘기면 그 프로세스의 주 창 사각형을 읽는다.
        // 테스트 러너에는 주 창이 없으므로 여기서는 사각형 조회 실패 → 주 모니터 폴백이 오류 없이 돌아오는지만 본다.
        var (shown, error) = await notice.ShowAsync(null, Environment.ProcessId, CancellationToken.None);

        Assert.True(shown, error);
        Assert.True(GetWindowRect(notice.Handle, out var rect));
        Assert.True(rect.Top >= 0);
    }
}

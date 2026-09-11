using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using Xapper.Inspector.Actions;

namespace Xapper.Tests;

/// <summary>
/// in-process 마우스 후킹의 델리게이트 수명을 검증하는 테스트 클래스.
///
/// 결함(f3d7e47 회귀): MinHook 디투어 델리게이트를 정적 필드에 붙들지 않아 GC 가 수거했고, 이후 무언가
/// 후킹된 user32 함수(GetMessagePos 등)를 부르는 순간 "수거된 델리게이트로 콜백" 으로 CLR 이 FailFast 해
/// 대상 앱을 죽였다. 실제 앱에서는 창이 파괴되며 HwndMouseInputProvider.Dispose → GetMessagePos 가
/// 그 방아쇠였다.
///
/// 여기서는 그 방아쇠를 결정적으로 재현한다: 후크 설치 → GC 강제 → 후킹된 네 함수를 직접 호출.
/// 디투어 델리게이트가 수거됐다면 그 호출이 이 테스트를 도는 프로세스 자체를 FailFast 로 죽이므로,
/// 호출을 넘겨 여기까지 살아 돌아오는 것 자체가 통과 조건이다. 창을 실제로 열었다 닫아
/// WmDestroy 경로도 함께 태운다(현실적 경로 확인). 후크는 프로세스 전역이라 화면 경합을 피하려고
/// <see cref="DesktopWindowCollection"/> 에 넣어 다른 창 테스트와 직렬화한다.
/// </summary>
[Collection(DesktopWindowCollection.Name)]
public class SyntheticMouseHookLifetimeTests
{
    private const int VK_LBUTTON = 0x01;

    [DllImport("user32.dll")] private static extern uint GetMessagePos();
    [DllImport("user32.dll")] private static extern short GetKeyState(int nVirtKey);
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT point);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X; public int Y; }

    [Fact]
    public void Hooks_SurviveGarbageCollectionAndAreStillInvokable()
    {
        StaThread.Run(() =>
        {
            // 후크를 걸 수 없는 환경(라이브러리 로드 실패 등)이면 검증할 대상이 없으니 물러난다.
            if (!SyntheticMouse.EnsureInstalledForTests())
                return;

            // 붙들지 않은 디투어 델리게이트라면 여기서 수거된다.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            // 후킹된 네 함수를 직접 호출해 디투어를 강제로 태운다. 델리게이트가 수거됐다면 이 호출들이
            // "수거된 델리게이트로 콜백" 으로 프로세스를 FailFast 시킨다. 반환값은 원함수(트램폴린)의 것이므로
            // 그 값 자체는 검증하지 않는다 — 호출이 크래시 없이 돌아오는 것이 검증 대상이다.
            _ = GetMessagePos();
            _ = GetKeyState(VK_LBUTTON);
            _ = GetAsyncKeyState(VK_LBUTTON);
            _ = GetCursorPos(out _);

            // 현실적 경로: 창을 열었다 닫아 WmDestroy → HwndMouseInputProvider.Dispose → GetMessagePos 도 태운다.
            var window = new Window
            {
                Width = 120,
                Height = 90,
                Left = -10000,
                Top = -10000,
                ShowActivated = false,
                ShowInTaskbar = false,
                WindowStartupLocation = WindowStartupLocation.Manual,
                WindowStyle = WindowStyle.None
            };
            window.Show();
            PumpUntilRendered(window);
            window.Close();
            Pump();

            // FailFast 없이 여기 도달 = 디투어 델리게이트가 GC 후에도 살아 있었다. 후크 상태도 여전히 유효해야 한다.
            Assert.True(SyntheticMouse.EnsureInstalledForTests());
        });
    }

    #region Helpers

    private static void PumpUntilRendered(Window window)
    {
        var frame = new DispatcherFrame();
        window.ContentRendered += (_, _) => frame.Continue = false;
        var timeout = new DispatcherTimer(TimeSpan.FromSeconds(3), DispatcherPriority.Normal,
            (_, _) => frame.Continue = false, Dispatcher.CurrentDispatcher);
        timeout.Start();
        try { Dispatcher.PushFrame(frame); }
        finally { timeout.Stop(); }
    }

    private static void Pump()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.Background, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    #endregion
}

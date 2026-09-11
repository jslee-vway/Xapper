using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Xapper.Inspector.Actions;

namespace Xapper.Tests;

/// <summary>
/// 후킹 경로 제스처가 실제 WPF 창에서 의도한 라우팅 이벤트를 일으키는지 검증한다. 커서를 옮기지 않고
/// WM 메시지 + 입력 상태 스푸프만으로 휠·우클릭·Ctrl+클릭이 WPF 에 진짜 입력으로 받아들여지는지 측정한다.
/// 창은 화면 밖에 두며, 같은 스레드에서 SendMessage 하면 WndProc 로 동기 전달되므로 펌프가 필요 없다.
/// </summary>
[Collection(DesktopWindowCollection.Name)]
public class SyntheticMouseGestureTests
{
    [Fact]
    public void TryWheel_ScrollsTheScrollViewerUnderThePoint()
    {
        WithWindow(
            () =>
            {
                var content = new StackPanel();
                for (var i = 0; i < 60; i++)
                    content.Children.Add(new TextBlock { Text = $"line {i}", Height = 20 });
                return new ScrollViewer { Content = content, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
            },
            (window, hwnd, viewer) =>
            {
                Assert.Equal(0, viewer.VerticalOffset);

                var ok = SyntheticMouse.TryWheel(hwnd, CentreOf(viewer), -3);
                window.UpdateLayout();

                Assert.True(ok);
                Assert.True(viewer.VerticalOffset > 0, $"VerticalOffset={viewer.VerticalOffset}");
            });
    }

    [Fact]
    public void TryRightClick_RaisesContextMenuOpeningOnTheElement()
    {
        WithWindow(
            () => new TextBox { Text = "hello", ContextMenu = new ContextMenu { Items = { new MenuItem { Header = "Probe" } } } },
            (window, hwnd, box) =>
            {
                var opening = 0;
                var rightUp = 0;
                box.ContextMenuOpening += (_, e) => { opening++; e.Handled = true; };
                box.MouseRightButtonUp += (_, _) => rightUp++;

                var ok = SyntheticMouse.TryRightClick(hwnd, CentreOf(box));

                Assert.True(ok);
                Assert.Equal(1, rightUp);
                Assert.Equal(1, opening);
            });
    }

    [Fact]
    public void TryClick_WithControl_HandlerSeesTheControlModifier()
    {
        WithWindow(
            () => new Border { Background = System.Windows.Media.Brushes.LightGray, Width = 120, Height = 60 },
            (window, hwnd, border) =>
            {
                var seen = ModifierKeys.None;
                var downs = 0;
                border.PreviewMouseLeftButtonDown += (_, _) => { downs++; seen = Keyboard.Modifiers; };

                var ok = SyntheticMouse.TryClick(hwnd, CentreOf(border), ModifierKeys.Control);

                Assert.True(ok);
                Assert.Equal(1, downs);
                Assert.Equal(ModifierKeys.Control, seen);
                Assert.Equal(ModifierKeys.None, Keyboard.Modifiers); // 제스처가 끝나면 스푸프가 꺼진다.
            });
    }

    #region Helpers

    private static Point CentreOf(FrameworkElement element)
        => element.PointToScreen(new Point(element.ActualWidth / 2, element.ActualHeight / 2));

    /// <summary>
    /// 화면 밖에 창을 세워 요소 하나를 담고, 후크가 있을 때만 본문을 실행합니다. 후크를 설치할 수 없는 환경에서는
    /// 검증 대상이 없으므로 조용히 통과한다.
    /// </summary>
    private static void WithWindow<T>(Func<T> createContent, Action<Window, IntPtr, T> body) where T : FrameworkElement
    {
        StaThread.Run(() =>
        {
            if (!InputSpoof.EnsureInstalled())
                return;

            var content = createContent();
            var window = new Window
            {
                Content = content, Width = 300, Height = 200,
                Left = -10000, Top = -10000,
                ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None
            };
            try
            {
                window.Show();
                window.UpdateLayout();

                if (PresentationSource.FromVisual(window) is not HwndSource source)
                    throw new InvalidOperationException("창에 HwndSource 가 없다.");

                body(window, source.Handle, content);
            }
            finally
            {
                window.Close();
                System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
    }

    #endregion
}

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Xapper.Inspector.Capture;
using Xapper.Protocol.Messages.Responses;

namespace Xapper.Tests;

/// <summary>
/// 화면에 합성된 픽셀을 읽어 오는 캡처를 실제 창으로 검증하는 테스트 클래스.
/// P/Invoke, DPI 배율, 축소, 알파 채널이 한꺼번에 얽혀 있고 어느 하나가 어긋나도 크기는 멀쩡해 보인다.
/// 그래서 크기뿐 아니라 실제 픽셀 값까지 확인한다 — 전부 투명한 그림이 크기 검사만으로는 통과했었다.
/// </summary>
[Collection(DesktopWindowCollection.Name)]
public class DesktopCaptureTests
{
    #region Helpers

    /// <summary>창이 화면에 실제로 그려질 때까지 기다릴 최대 시간.</summary>
    private static readonly TimeSpan RenderTimeout = TimeSpan.FromSeconds(5);

    /// <summary>화면에 실제로 그려진 창을 만들어 본문에 넘기고, 끝나면 닫습니다.</summary>
    private static void WithVisibleWindow(Action<Window> body)
    {
        StaThread.Run(() =>
        {
            var window = new Window
            {
                Width = 400,
                Height = 300,
                Left = 0,
                Top = 0,
                WindowStyle = WindowStyle.None,
                ShowActivated = false,
                ShowInTaskbar = false,
                // 화면 캡처는 위에 덮인 것을 찍는다. 다른 창이 가리면 단언이 흔들리므로 맨 앞에 고정한다.
                Topmost = true,
                Content = new Border { Background = Brushes.CornflowerBlue }
            };

            try
            {
                window.Show();
                WaitUntilRendered(window);
                body(window);
            }
            finally
            {
                window.Close();
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
    }

    /// <summary>
    /// 창이 그려질 때까지 메시지 루프를 돌립니다.
    /// 그려지는 신호가 오지 않는 환경(헤드리스 등)에서도 반드시 빠져나오도록 시간 제한을 둔다.
    /// </summary>
    private static void WaitUntilRendered(Window window)
    {
        var frame = new DispatcherFrame();
        window.ContentRendered += (_, _) => frame.Continue = false;

        var timeout = new DispatcherTimer(RenderTimeout, DispatcherPriority.Normal,
            (_, _) => frame.Continue = false, Dispatcher.CurrentDispatcher);
        timeout.Start();

        try
        {
            Dispatcher.PushFrame(frame);
        }
        finally
        {
            timeout.Stop();
        }
    }


    /// <summary>창이 데스크톱에 실제로 나타날 때까지 기다렸다가 캡처합니다.
    /// WPF가 그렸다는 신호와 화면 합성이 끝난 시점은 다르고, 그 간격은 머신이 바쁠수록 벌어진다.
    /// 기대한 색이 보일 때까지 다시 찍되, 시간이 다하면 마지막 결과를 그대로 돌려주어
    /// 실패 메시지에 실제로 무엇이 찍혔는지 남게 한다.</summary>
    private static ScreenshotResponse CaptureOnceVisible(Func<ScreenshotResponse> capture)
    {
        var deadline = DateTime.UtcNow + RenderTimeout;
        var response = capture();

        while (!ShowsTheWindow(response) && DateTime.UtcNow < deadline)
        {
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
            Thread.Sleep(30);
            response = capture();
        }

        return response;
    }

    /// <summary>가운데 픽셀이 창을 채운 색인지 확인합니다.</summary>
    private static bool ShowsTheWindow(ScreenshotResponse response)
    {
        var (pixels, width, height) = CapturedImage.BgraPixelsOf(response);
        var centre = ((height / 2) * width + width / 2) * 4;
        return pixels[centre] == Colors.CornflowerBlue.B
            && pixels[centre + 1] == Colors.CornflowerBlue.G
            && pixels[centre + 2] == Colors.CornflowerBlue.R;
    }

    #endregion

    [Fact]
    public void CaptureApplicationWindows_ProducesAnOpaqueImageOfTheWindowItself()
    {
        WithVisibleWindow(_ =>
        {
            var response = CaptureOnceVisible(() => DesktopCapture.CaptureApplicationWindows());

            var (pixels, width, height) = CapturedImage.BgraPixelsOf(response);

            var transparent = 0;
            for (var i = 3; i < pixels.Length; i += 4)
                if (pixels[i] == 0) transparent++;

            Assert.Equal(0, transparent);

            // 크기만 맞고 엉뚱한 곳을 찍었을 수도 있다. 창을 채운 색이 실제로 담겼는지 가운데 픽셀로 확인한다.
            var centre = ((height / 2) * width + width / 2) * 4;
            var expected = Colors.CornflowerBlue;
            Assert.Equal(expected.B, pixels[centre]);
            Assert.Equal(expected.G, pixels[centre + 1]);
            Assert.Equal(expected.R, pixels[centre + 2]);
        });
    }


    [Fact]
    public void CaptureApplicationWindows_IncludesAPopupThatExtendsBeyondTheWindow()
    {
        WithVisibleWindow(_ =>
        {
            var withoutPopup = DesktopCapture.CaptureApplicationWindows();

            var popup = new Popup
            {
                Placement = PlacementMode.Absolute,
                HorizontalOffset = 0,
                VerticalOffset = 400,
                Width = 200,
                Height = 150,
                Child = new Border { Background = Brushes.OrangeRed }
            };

            popup.IsOpen = true;
            try
            {
                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Loaded);

                var withPopup = DesktopCapture.CaptureApplicationWindows();

                // 팝업은 Window 가 아니라 자기 HWND 로 뜬다. 영역이 그만큼 아래로 늘어나야 담긴 것이다.
                Assert.True(withPopup.Height > withoutPopup.Height,
                    $"팝업을 열었는데 영역이 늘지 않았다: {withoutPopup.Height} -> {withPopup.Height}");
            }
            finally
            {
                popup.IsOpen = false;
            }
        });
    }

    [Fact]
    public void CaptureApplicationWindows_WithMaxWidth_ShrinksAndKeepsAspectRatio()
    {
        WithVisibleWindow(_ =>
        {
            var full = DesktopCapture.CaptureApplicationWindows();
            var target = full.Width / 2;

            var shrunk = DesktopCapture.CaptureApplicationWindows(maxWidth: target);

            Assert.InRange(shrunk.Width, target - 1, target + 1);
            Assert.Equal(shrunk.Width, CapturedImage.SizeOf(shrunk).Width);
            Assert.InRange(shrunk.Height, full.Height / 2 - 1, full.Height / 2 + 1);
        });
    }

    [Fact]
    public void CaptureElement_CapturesTheElementRegionOpaquely()
    {
        WithVisibleWindow(window =>
        {
            var content = (UIElement)window.Content;

            var part = CaptureOnceVisible(() => DesktopCapture.CaptureElement(content));

            var (pixels, width, height) = CapturedImage.BgraPixelsOf(part);
            Assert.Equal(part.Width, width);

            var centre = ((height / 2) * width + width / 2) * 4;
            Assert.Equal(Colors.CornflowerBlue.B, pixels[centre]);
            Assert.NotEqual(0, pixels[centre + 3]);
        });
    }

    [Fact]
    public void CaptureApplicationWindows_WithNoWindowOnScreen_SaysWhatIsWrong()
    {
        StaThread.Run(() =>
        {
            var failure = Assert.Throws<InvalidOperationException>(
                () => DesktopCapture.CaptureApplicationWindows());

            Assert.Contains("No window of this application is visible", failure.Message);
        });
    }

    #region 찍을 영역 정하기

    [Fact]
    public void RegionAroundTheMainWindow_IgnoresAWindowSittingOnAnotherMonitor()
    {
        // 전부 합치면 앱을 찍으려던 그림에 남의 프로그램이 절반을 차지한다
        // (실측: 가로 4000픽셀이 넘는 그림에 대상 앱이 오른쪽 3분의 1만 있었다).
        var main = new Int32Rect(2560, 0, 1280, 1000);
        var display = new Int32Rect(2560, 0, 1280, 1000);
        var strayOnTheLeftMonitor = new Int32Rect(0, 0, 100, 100);

        var region = DesktopCapture.RegionAroundTheMainWindow([main, strayOnTheLeftMonitor], display);

        Assert.Equal(main, region);
    }

    [Fact]
    public void RegionAroundTheMainWindow_TakesInADialogOverTheWindow()
    {
        // 팝업과 대화상자를 담는 것이 이 모드의 목적이다. 그것들은 주 창 위에 뜬다.
        var main = new Int32Rect(100, 100, 800, 600);
        var dialog = new Int32Rect(300, 500, 400, 300);

        var region = DesktopCapture.RegionAroundTheMainWindow([main, dialog], display: null);

        Assert.Equal(new Int32Rect(100, 100, 800, 700), region);
    }

    [Fact]
    public void RegionAroundTheMainWindow_TakesInADropdownBelowTheWindow()
    {
        // 창 아래로 펼쳐지는 드롭다운은 창과 겹치지 않으면서도 같은 화면에 있다. 같은 화면이면 담는다.
        var main = new Int32Rect(100, 100, 800, 400);
        var display = new Int32Rect(0, 0, 1920, 1080);
        var dropdown = new Int32Rect(200, 520, 200, 150);

        var region = DesktopCapture.RegionAroundTheMainWindow([main, dropdown], display);

        Assert.Equal(new Int32Rect(100, 100, 800, 570), region);
    }

    [Fact]
    public void RegionAroundTheMainWindow_SkipsWindowsWithNoArea()
    {
        // 너비나 높이가 없는 창은 화면에 아무것도 내놓지 않으면서 영역만 늘린다.
        var main = new Int32Rect(100, 100, 800, 600);
        var degenerate = new Int32Rect(0, 0, 0, 0);

        Assert.Equal(main, DesktopCapture.RegionAroundTheMainWindow([main, degenerate], display: null));
    }

    [Fact]
    public void RegionAroundTheMainWindow_IsNull_WhenNothingIsUsable()
    {
        Assert.Null(DesktopCapture.RegionAroundTheMainWindow([new Int32Rect(0, 0, 1, 1)], display: null));
    }

    #endregion
}

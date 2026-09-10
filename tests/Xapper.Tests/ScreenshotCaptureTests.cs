using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xapper.Inspector.Capture;
using Xapper.Protocol.Messages.Responses;

namespace Xapper.Tests;

/// <summary>
/// 스크린샷 캡처가 실제로 인코딩한 이미지와 보고하는 크기가 일치하는지,
/// 축소 요청이 가로세로 비율을 지키는지 검증하는 테스트 클래스.
/// </summary>
public class ScreenshotCaptureTests
{
    #region Helpers

    /// <summary>레이아웃까지 끝난, 렌더링 가능한 요소를 만듭니다.</summary>
    private static UIElement BuildRenderableElement()
    {
        var element = new Border
        {
            Width = 400,
            Height = 200,
            Background = Brushes.CornflowerBlue
        };
        element.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        element.Arrange(new Rect(0, 0, 400, 200));
        element.UpdateLayout();
        return element;
    }

    /// <summary>응답에 담긴 base64 PNG를 실제로 디코딩해 픽셀 크기를 읽습니다.</summary>
    private static (int Width, int Height) DecodedSizeOf(ScreenshotResponse response)
    {
        using var stream = new MemoryStream(Convert.FromBase64String(response.Base64Png));
        var frame = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        return (frame.PixelWidth, frame.PixelHeight);
    }

    #endregion

    [Fact]
    public void CaptureElement_ReportedSizeMatchesTheEncodedImage()
    {
        StaThread.Run(() =>
        {
            var response = ScreenshotCapture.CaptureElement(BuildRenderableElement());

            var decoded = DecodedSizeOf(response);
            Assert.Equal(decoded.Width, response.Width);
            Assert.Equal(decoded.Height, response.Height);
        });
    }

    [Fact]
    public void CaptureElement_WithMaxWidth_ShrinksAndKeepsAspectRatio()
    {
        StaThread.Run(() =>
        {
            var full = ScreenshotCapture.CaptureElement(BuildRenderableElement());
            var target = full.Width / 2;

            var shrunk = ScreenshotCapture.CaptureElement(BuildRenderableElement(), maxWidth: target);

            var decoded = DecodedSizeOf(shrunk);
            Assert.InRange(decoded.Width, target - 1, target + 1);
            Assert.Equal(decoded.Width, shrunk.Width);
            Assert.InRange(shrunk.Height, full.Height / 2 - 1, full.Height / 2 + 1);
        });
    }

    [Fact]
    public void CaptureElement_WithMaxWidthLargerThanImage_LeavesItUntouched()
    {
        StaThread.Run(() =>
        {
            var full = ScreenshotCapture.CaptureElement(BuildRenderableElement());

            var unchanged = ScreenshotCapture.CaptureElement(BuildRenderableElement(), maxWidth: full.Width * 2);

            Assert.Equal(full.Width, unchanged.Width);
            Assert.Equal(full.Height, unchanged.Height);
        });
    }
}

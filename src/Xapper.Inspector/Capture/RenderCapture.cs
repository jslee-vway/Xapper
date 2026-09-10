using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xapper.Protocol.Messages.Responses;

namespace Xapper.Inspector.Capture;

/// <summary>
/// WPF 윈도우 또는 개별 UI 요소를 RenderTargetBitmap으로 다시 그려 Base64 PNG로 변환하는 유틸리티 클래스.
/// 시각 트리 하나만 그리므로 창이 가려져 있어도 찍히지만, 별도 창·팝업·컨텍스트 메뉴·드롭다운처럼
/// 자기 HWND에 사는 것은 담기지 않는다. 그런 것까지 필요하면 <see cref="DesktopCapture"/>를 쓴다.
/// </summary>
public static class RenderCapture
{
    /// <summary>
    /// 지정된 UI 요소를 네이티브 DPI로 렌더링하여 Base64 PNG 스크린샷을 반환합니다.
    /// </summary>
    /// <param name="element">캡처할 UI 요소.</param>
    /// <param name="maxWidth">인코딩할 최대 가로 픽셀 수. 원본이 더 넓으면 비율을 유지한 채 축소. null이면 축소하지 않음.</param>
    /// <returns>Base64 인코딩된 PNG 이미지와 인코딩된 픽셀 크기.</returns>
    /// <exception cref="InvalidOperationException">요소에 렌더링 가능한 영역이 없는 경우.</exception>
    public static ScreenshotResponse CaptureElement(UIElement element, int? maxWidth = null)
    {
        var bounds = VisualTreeHelper.GetDescendantBounds(element);
        if (bounds.IsEmpty)
            throw new InvalidOperationException("Element has no renderable bounds");

        var dpi = VisualTreeHelper.GetDpi(element);
        var width = (int)Math.Ceiling(bounds.Width);
        var height = (int)Math.Ceiling(bounds.Height);

        var scale = ShrinkFactor(width * dpi.DpiScaleX, maxWidth);

        var renderBitmap = new RenderTargetBitmap(
            (int)(width * dpi.DpiScaleX * scale),
            (int)(height * dpi.DpiScaleY * scale),
            dpi.PixelsPerInchX * scale,
            dpi.PixelsPerInchY * scale,
            PixelFormats.Pbgra32);

        renderBitmap.Render(element);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(renderBitmap));

        using var memoryStream = new MemoryStream();
        encoder.Save(memoryStream);

        return new ScreenshotResponse
        {
            Base64Png = Convert.ToBase64String(memoryStream.ToArray()),
            Width = renderBitmap.PixelWidth,
            Height = renderBitmap.PixelHeight
        };
    }

    /// <summary>
    /// 허용된 가로 픽셀 수에 맞추기 위한 렌더링 배율을 구합니다.
    /// 다 그린 그림을 사후에 줄이지 않고 처음부터 줄인 배율로 그리는 이유는 두 가지다.
    /// 글자가 리샘플로 뭉개지지 않고 그 크기에 맞게 다시 그려지며, 대상 앱 안에 원본 크기 버퍼를 잡지 않는다.
    /// </summary>
    /// <param name="naturalPixelWidth">축소하지 않았을 때의 가로 픽셀 수.</param>
    /// <param name="maxWidth">허용할 최대 가로 픽셀 수. null이거나 0 이하면 축소하지 않음.</param>
    /// <returns>1.0 이하의 렌더링 배율. 축소가 필요 없으면 1.0.</returns>
    private static double ShrinkFactor(double naturalPixelWidth, int? maxWidth)
    {
        if (!maxWidth.HasValue || maxWidth.Value <= 0 || naturalPixelWidth <= maxWidth.Value)
            return 1.0;

        return maxWidth.Value / naturalPixelWidth;
    }

    /// <summary>
    /// 지정된 윈도우(또는 메인 윈도우)를 캡처합니다.
    /// </summary>
    /// <param name="window">캡처할 윈도우. null이면 Application.Current.MainWindow 사용.</param>
    /// <param name="maxWidth">인코딩할 최대 가로 픽셀 수. null이면 축소하지 않음.</param>
    /// <returns>Base64 인코딩된 PNG 이미지와 인코딩된 픽셀 크기.</returns>
    public static ScreenshotResponse CaptureWindow(Window? window = null, int? maxWidth = null)
    {
        window ??= Application.Current.MainWindow;
        if (window == null)
            throw new InvalidOperationException("No window available to capture");

        return CaptureElement(window, maxWidth);
    }
}

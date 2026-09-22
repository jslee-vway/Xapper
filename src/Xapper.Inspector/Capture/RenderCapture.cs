using System.Globalization;
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
    /// <param name="marks">
    /// 겹쳐 그릴 번호 상자. null 이거나 비어 있으면 아무것도 그리지 않는다. 사각형은 <paramref name="element"/> 의
    /// DIP 좌표계 기준이므로, 같은 비트맵에 한 번 더 그리면 DPI 배율과 축소 배율이 자동으로 함께 적용된다.
    /// </param>
    /// <returns>Base64 인코딩된 PNG 이미지와 인코딩된 픽셀 크기.</returns>
    /// <exception cref="InvalidOperationException">요소에 렌더링 가능한 영역이 없는 경우.</exception>
    public static ScreenshotResponse CaptureElement(
        UIElement element, int? maxWidth = null, IReadOnlyList<MarkCandidate>? marks = null)
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

        if (marks is { Count: > 0 })
            renderBitmap.Render(BuildOverlay(marks, bounds, dpi.PixelsPerDip));

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(renderBitmap));

        using var memoryStream = new MemoryStream();
        encoder.Save(memoryStream);

        var origin = ScreenOriginOf(element);

        return new ScreenshotResponse
        {
            Base64Png = Convert.ToBase64String(memoryStream.ToArray()),
            Width = renderBitmap.PixelWidth,
            Height = renderBitmap.PixelHeight,
            OriginX = origin?.X,
            OriginY = origin?.Y,
            Scale = scale
        };
    }

    /// <summary>번호 상자 외곽선의 두께 (DIP).</summary>
    private const double MarkLineThickness = 2;

    /// <summary>번호 라벨의 글자 크기 (DIP).</summary>
    private const double MarkFontSize = 11;

    /// <summary>
    /// 번호 상자를 그린 시각 요소를 만듭니다. 캡처 대상과 같은 DIP 좌표계에 그리므로 픽셀 변환을 하지 않는다.
    /// <paramref name="bounds"/> 의 왼쪽 위가 비트맵의 (0,0) 이므로 그만큼 옮겨 그린다.
    /// </summary>
    private static DrawingVisual BuildOverlay(IReadOnlyList<MarkCandidate> marks, Rect bounds, double pixelsPerDip)
    {
        var outline = new Pen(Brushes.Red, MarkLineThickness);
        var label = Brushes.Red;
        var visual = new DrawingVisual();

        using var context = visual.RenderOpen();
        context.PushTransform(new TranslateTransform(-bounds.X, -bounds.Y));

        foreach (var mark in marks)
        {
            context.DrawRectangle(null, outline, mark.Rect);

            var text = new FormattedText(
                mark.Number.ToString(CultureInfo.InvariantCulture),
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                MarkFontSize,
                Brushes.White,
                pixelsPerDip);

            // 번호는 요소 바깥 왼쪽에 얹는다. 요소 안에 그리면 바로 그 자리에 있는 글자를 덮어, 그림을
            // 읽으려고 찍은 목적을 해친다(실측: "Alpha" 가 "pha" 로 보였다). 컨트롤의 글자는 대개 왼쪽에
            // 붙으므로 왼쪽 바깥이 가장 비어 있고, 자리가 없을 때만 안으로 접어 넣는다.
            var plateWidth = text.Width + 6;
            var plateHeight = text.Height + 2;
            var plateX = mark.Rect.X - plateWidth >= bounds.X ? mark.Rect.X - plateWidth : mark.Rect.X;
            var plate = new Rect(plateX, mark.Rect.Y, plateWidth, plateHeight);
            context.DrawRectangle(label, null, plate);
            context.DrawText(text, new Point(plate.X + 3, plate.Y + 1));
        }

        context.Pop();
        return visual;
    }

    /// <summary>
    /// 그려진 영역의 왼쪽 위가 화면 어디인지 구합니다.
    /// 화면에 붙어 있지 않은 요소는 화면 좌표를 가질 수 없으므로 null.
    /// </summary>
    private static Point? ScreenOriginOf(UIElement element)
    {
        if (PresentationSource.FromVisual(element) is null)
            return null;

        try
        {
            return element.PointToScreen(new Point(0, 0));
        }
        catch
        {
            return null;
        }
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
    /// <param name="marks">겹쳐 그릴 번호 상자. null 이거나 비어 있으면 아무것도 그리지 않는다.</param>
    /// <returns>Base64 인코딩된 PNG 이미지와 인코딩된 픽셀 크기.</returns>
    public static ScreenshotResponse CaptureWindow(
        Window? window = null, int? maxWidth = null, IReadOnlyList<MarkCandidate>? marks = null)
    {
        window ??= Application.Current.MainWindow;
        if (window == null)
            throw new InvalidOperationException("No window available to capture");

        return CaptureElement(window, maxWidth, marks);
    }
}

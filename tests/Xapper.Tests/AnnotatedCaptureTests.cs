using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Xapper.Inspector.Capture;

namespace Xapper.Tests;

/// <summary>
/// 번호 상자를 겹쳐 그린 캡처를 검증한다. 상자는 캡처 대상과 같은 DIP 좌표계에 그리므로,
/// 축소를 걸어도 보고되는 크기가 어긋나지 않아야 한다.
/// </summary>
public class AnnotatedCaptureTests
{
    #region Helpers

    private static Canvas BuildCanvas()
    {
        var canvas = new Canvas { Width = 400, Height = 200, Background = Brushes.White };
        var box = new Border { Width = 100, Height = 40, Background = Brushes.SteelBlue };
        Canvas.SetLeft(box, 20);
        Canvas.SetTop(box, 20);
        canvas.Children.Add(box);
        canvas.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        canvas.Arrange(new Rect(0, 0, 400, 200));
        canvas.UpdateLayout();
        return canvas;
    }

    private static List<MarkCandidate> MarksOf(Canvas canvas)
        => MarkPicker.Pick(canvas, new Rect(0, 0, canvas.Width, canvas.Height), 40, 16, out _);

    #endregion

    [Fact]
    public void CaptureElement_WithMarks_ReportedSizeStillMatchesTheEncodedImage()
    {
        StaThread.Run(() =>
        {
            var canvas = BuildCanvas();

            var response = RenderCapture.CaptureElement(canvas, maxWidth: null, MarksOf(canvas));

            var decoded = CapturedImage.SizeOf(response);
            Assert.Equal(decoded.Width, response.Width);
            Assert.Equal(decoded.Height, response.Height);
        });
    }

    [Fact]
    public void CaptureElement_WithMarks_ChangesThePixels()
    {
        StaThread.Run(() =>
        {
            var canvas = BuildCanvas();
            var marks = MarksOf(canvas);
            Assert.NotEmpty(marks);

            var plain = RenderCapture.CaptureElement(canvas);
            var annotated = RenderCapture.CaptureElement(canvas, maxWidth: null, marks);

            // 같은 요소를 같은 크기로 찍었으므로, 달라진 것은 겹쳐 그린 상자뿐이다.
            Assert.Equal(plain.Width, annotated.Width);
            Assert.NotEqual(plain.Base64Png, annotated.Base64Png);
        });
    }

    [Fact]
    public void CaptureElement_WithMarksAndMaxWidth_StillShrinks()
    {
        StaThread.Run(() =>
        {
            var canvas = BuildCanvas();

            var response = RenderCapture.CaptureElement(canvas, maxWidth: 200, MarksOf(canvas));

            Assert.True(response.Width <= 200);
            var decoded = CapturedImage.SizeOf(response);
            Assert.Equal(decoded.Width, response.Width);
        });
    }

    [Fact]
    public void CaptureElement_WithoutMarks_IsUnchanged()
    {
        StaThread.Run(() =>
        {
            var canvas = BuildCanvas();

            var withNull = RenderCapture.CaptureElement(canvas, maxWidth: null, marks: null);
            var withEmpty = RenderCapture.CaptureElement(canvas, maxWidth: null, new List<MarkCandidate>());

            Assert.Equal(withNull.Base64Png, withEmpty.Base64Png);
        });
    }
}

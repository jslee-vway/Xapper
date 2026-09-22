using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
    public void CaptureElement_WhenDescendantBoundsStartLeftOfTheOrigin_DrawsTheBoxOnTheElement()
    {
        StaThread.Run(() =>
        {
            // RenderTargetBitmap.Render 는 요소의 자기 원점을 비트맵 (0,0) 에 놓고, 음수 좌표에 있는 것은 잘라낸다.
            // GetDescendantBounds 의 왼쪽 위에 맞추는 것이 아니다. 그래서 겹쳐 그리는 쪽도 옮기지 않아야 한다.
            var canvas = new Canvas { Width = 200, Height = 100, Background = Brushes.White };

            // 이 자식 때문에 자손 경계가 x=-30 에서 시작하지만, 그림에는 담기지 않는다.
            var offscreen = new Border { Width = 20, Height = 20, Background = Brushes.Lime };
            Canvas.SetLeft(offscreen, -30);
            Canvas.SetTop(offscreen, 0);
            canvas.Children.Add(offscreen);

            var target = new Border { Width = 60, Height = 30, Background = Brushes.Blue };
            Canvas.SetLeft(target, 40);
            Canvas.SetTop(target, 40);
            canvas.Children.Add(target);

            canvas.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            canvas.Arrange(new Rect(0, 0, 200, 100));
            canvas.UpdateLayout();

            var bounds = VisualTreeHelper.GetDescendantBounds(canvas);
            Assert.True(bounds.X < 0, "이 테스트는 자손 경계가 음수에서 시작하는 경우를 다룬다.");

            var marks = MarkPicker.Pick(canvas, bounds, 40, 16, out _);
            var mark = Assert.Single(marks, candidate => ReferenceEquals(candidate.Element, target));

            var response = RenderCapture.CaptureElement(canvas, maxWidth: null, marks);

            // 상자의 오른쪽 모서리는 대상 요소의 오른쪽 끝에 맞아야 한다. 왼쪽은 번호 라벨이 요소 바깥으로
            // 나가 있으므로 기준으로 쓸 수 없다. 경계 원점만큼 옮겨 그리면 이 값이 30 픽셀 밀린다.
            var redColumns = RedColumnsOf(response);
            Assert.NotEmpty(redColumns);
            Assert.InRange(redColumns.Max(), (int)mark.Rect.Right - 3, (int)mark.Rect.Right + 3);
        });
    }

    /// <summary>그림에서 빨간 외곽선이 그려진 가로 좌표들을 모읍니다.</summary>
    private static List<int> RedColumnsOf(Xapper.Protocol.Messages.Responses.ScreenshotResponse response)
    {
        var png = Convert.FromBase64String(response.Base64Png);
        using var stream = new System.IO.MemoryStream(png);
        var frame = BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
        var stride = frame.PixelWidth * 4;
        var pixels = new byte[stride * frame.PixelHeight];
        frame.CopyPixels(pixels, stride, 0);

        var columns = new List<int>();
        for (var x = 0; x < frame.PixelWidth; x++)
        {
            for (var y = 0; y < frame.PixelHeight; y++)
            {
                var offset = y * stride + x * 4;
                // Bgra32: 빨강이 강하고 초록·파랑이 약한 픽셀만 외곽선으로 본다.
                if (pixels[offset + 2] > 200 && pixels[offset + 1] < 80 && pixels[offset] < 80)
                {
                    columns.Add(x);
                    break;
                }
            }
        }

        return columns;
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

using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xapper.Protocol.Messages.Responses;

namespace Xapper.Inspector.Capture;

/// <summary>
/// 데스크톱에 실제로 그려진 픽셀을 그대로 떠 오는 캡처.
/// 시각 트리를 다시 그리는 <see cref="RenderCapture"/>와 달리 합성이 끝난 화면을 읽으므로,
/// 별도 창·팝업·컨텍스트 메뉴·드롭다운처럼 자기 HWND에 사는 것들이 보이는 그대로 담긴다.
/// 대신 다른 앱이 위를 덮고 있으면 그 모습이 찍힌다.
/// 영역을 Window 객체가 아니라 최상위 HWND 기준으로 구하는 이유가 여기에 있다 — 팝업은 Window가 아니다.
/// </summary>
public static class DesktopCapture
{
    #region Win32

    [DllImport("user32.dll")]
    private static extern IntPtr GetDC(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern IntPtr GetAncestor(IntPtr hWnd, uint flags);

    /// <summary>창이 속한 최상위 창을 구하는 GetAncestor 플래그.</summary>
    private const uint GA_ROOT = 2;

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(IntPtr hdc, int width, int height);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr hdc, IntPtr hObject);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(IntPtr hObject);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(IntPtr hdc);

    [DllImport("gdi32.dll")]
    private static extern int SetStretchBltMode(IntPtr hdc, int mode);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetBrushOrgEx(IntPtr hdc, int x, int y, IntPtr lppt);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool StretchBlt(
        IntPtr hdcDest, int xDest, int yDest, int wDest, int hDest,
        IntPtr hdcSrc, int xSrc, int ySrc, int wSrc, int hSrc, uint rop);

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    /// <summary>원본을 그대로 복사한다.</summary>
    private const uint SRCCOPY = 0x00CC0020;

    /// <summary>계층형 창(투명도를 쓰는 팝업 등)도 결과에 포함시킨다.</summary>
    private const uint CAPTUREBLT = 0x40000000;

    /// <summary>축소할 때 주변 픽셀을 섞어 글자가 뭉개지는 것을 줄인다.</summary>
    private const int HALFTONE = 4;

    #endregion

    #region Public Methods

    /// <summary>
    /// 이 UI 스레드가 띄운 최상위 창을 모두 덮는 화면 영역을 캡처합니다.
    /// 대화상자뿐 아니라 열려 있는 팝업·메뉴·드롭다운도 각자 최상위 HWND라 함께 포함된다.
    /// </summary>
    /// <param name="maxWidth">인코딩할 최대 가로 픽셀 수. null이면 축소하지 않음.</param>
    /// <returns>Base64 인코딩된 PNG 이미지와 인코딩된 픽셀 크기.</returns>
    /// <exception cref="InvalidOperationException">화면에 보이는 창이 없는 경우.</exception>
    /// <remarks>UI 스레드에서 호출해야 합니다.</remarks>
    public static ScreenshotResponse CaptureApplicationWindows(int? maxWidth = null)
    {
        var bounds = UnionOfWindowBounds()
            ?? throw new InvalidOperationException(
                "No window of this application is visible on screen. It may be minimized or closing. " +
                "Restore the window, or use the render mode, which does not depend on what is on screen.");

        return CaptureRegion(bounds, maxWidth);
    }

    /// <summary>
    /// 지정된 요소가 화면에서 차지하는 영역을 캡처합니다.
    /// </summary>
    /// <param name="element">캡처할 요소.</param>
    /// <param name="maxWidth">인코딩할 최대 가로 픽셀 수. null이면 축소하지 않음.</param>
    /// <returns>Base64 인코딩된 PNG 이미지와 인코딩된 픽셀 크기.</returns>
    /// <exception cref="InvalidOperationException">요소가 화면에 그려져 있지 않은 경우.</exception>
    /// <remarks>UI 스레드에서 호출해야 합니다.</remarks>
    public static ScreenshotResponse CaptureElement(UIElement element, int? maxWidth = null)
    {
        var size = element.RenderSize;
        if (size.Width <= 0 || size.Height <= 0)
            throw new InvalidOperationException("Element has no rendered area on screen.");

        if (PresentationSource.FromVisual(element) is null)
            throw new InvalidOperationException(
                "Element is not attached to a window on screen, so there is nothing to read from the desktop.");

        var topLeft = element.PointToScreen(new Point(0, 0));
        var bottomRight = element.PointToScreen(new Point(size.Width, size.Height));

        var bounds = new Int32Rect(
            (int)Math.Floor(topLeft.X),
            (int)Math.Floor(topLeft.Y),
            (int)Math.Ceiling(bottomRight.X - topLeft.X),
            (int)Math.Ceiling(bottomRight.Y - topLeft.Y));

        return CaptureRegion(bounds, maxWidth);
    }

    /// <summary>
    /// 이 앱의 창 중 하나라도 화면 맨 앞에 있는지 확인합니다.
    /// 하나도 앞에 없으면 다른 앱이 캡처 영역을 덮은 그림이 찍혔을 수 있다.
    /// </summary>
    /// <returns>앱의 창이 포그라운드면 true.</returns>
    /// <remarks>UI 스레드에서 호출해야 합니다.</remarks>
    public static bool IsAnyWindowInForeground()
    {
        var foreground = GetForegroundWindow();
        return TopLevelWindowHandles().Any(handle => handle == foreground);
    }

    /// <summary>
    /// 화면에 보이는 이 앱의 최상위 창 개수를 셉니다. 팝업과 메뉴도 각자 하나로 센다.
    /// </summary>
    /// <returns>보이는 최상위 창의 수.</returns>
    /// <remarks>UI 스레드에서 호출해야 합니다.</remarks>
    public static int CountVisibleWindows() => TopLevelWindowHandles().Count();

    #endregion

    #region Private Methods

    /// <summary>
    /// 화면에 보이는 최상위 창 핸들을 열거합니다.
    /// WPF는 팝업과 메뉴도 각자 HwndSource로 띄우므로 Window 목록으로는 이들을 볼 수 없다.
    /// 다만 입력 소스 중에는 다른 창 안에 얹힌 자식 창도 있어, 그것까지 세면 "다른 창이 N개 열려 있다"는
    /// 경고가 부풀고 사용자가 없는 창을 찾게 된다. 최소화된 창은 화면 밖 좌표를 돌려주므로 함께 제외한다.
    /// </summary>
    private static IEnumerable<IntPtr> TopLevelWindowHandles()
    {
        foreach (var source in VisualTree.VisualRoots.Sources())
        {
            var handle = source.Handle;

            if (GetAncestor(handle, GA_ROOT) != handle)
                continue;

            if (!IsWindowVisible(handle) || IsIconic(handle))
                continue;

            yield return handle;
        }
    }

    /// <summary>
    /// 보이는 창들을 모두 포함하는 최소 사각형을 구합니다. 창의 테두리와 제목 줄까지 포함한다.
    /// </summary>
    private static Int32Rect? UnionOfWindowBounds()
    {
        int? left = null, top = null, right = null, bottom = null;

        foreach (var handle in TopLevelWindowHandles())
        {
            if (!GetWindowRect(handle, out var rect))
                continue;

            left = left is null ? rect.Left : Math.Min(left.Value, rect.Left);
            top = top is null ? rect.Top : Math.Min(top.Value, rect.Top);
            right = right is null ? rect.Right : Math.Max(right.Value, rect.Right);
            bottom = bottom is null ? rect.Bottom : Math.Max(bottom.Value, rect.Bottom);
        }

        if (left is null || top is null || right is null || bottom is null)
            return null;

        return new Int32Rect(left.Value, top.Value, right.Value - left.Value, bottom.Value - top.Value);
    }

    /// <summary>
    /// 화면의 지정된 영역을 읽어 PNG로 인코딩합니다.
    /// 축소는 복사하는 순간에 이뤄지므로 원본 크기 버퍼를 따로 잡지 않는다.
    /// </summary>
    private static ScreenshotResponse CaptureRegion(Int32Rect region, int? maxWidth)
    {
        if (region.Width <= 0 || region.Height <= 0)
            throw new InvalidOperationException("The region to capture is empty.");

        var scale = ShrinkFactor(region.Width, maxWidth);
        var targetWidth = Math.Max(1, (int)(region.Width * scale));
        var targetHeight = Math.Max(1, (int)(region.Height * scale));

        var screenDc = GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero)
            throw new InvalidOperationException("Could not read the screen.");

        var memoryDc = IntPtr.Zero;
        var bitmap = IntPtr.Zero;
        try
        {
            memoryDc = CreateCompatibleDC(screenDc);

            // 화면 DC 를 기준으로 만들어야 화면과 같은 색 깊이가 된다. 메모리 DC 기준이면 흑백 비트맵이 나온다.
            bitmap = CreateCompatibleBitmap(screenDc, targetWidth, targetHeight);
            if (memoryDc == IntPtr.Zero || bitmap == IntPtr.Zero)
                throw new InvalidOperationException(
                    $"Could not allocate a {targetWidth}x{targetHeight} capture buffer. " +
                    "Pass maxWidth to ask for a smaller image.");

            CopyScreenInto(memoryDc, bitmap, region, targetWidth, targetHeight);

            var response = Encode(bitmap, targetWidth, targetHeight);
            response.OriginX = region.X;
            response.OriginY = region.Y;
            response.Scale = scale;
            return response;
        }
        finally
        {
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
            if (memoryDc != IntPtr.Zero) DeleteDC(memoryDc);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    /// <summary>
    /// 화면 영역을 준비된 비트맵으로 복사합니다. 복사 중 무슨 일이 있어도 비트맵을 DC 에서 반드시 뺀다.
    /// 선택된 채로 남으면 호출자의 정리에서 비트맵 삭제가 실패해 핸들이 샌다.
    /// </summary>
    private static void CopyScreenInto(IntPtr memoryDc, IntPtr bitmap, Int32Rect region, int targetWidth, int targetHeight)
    {
        var screenDc = GetDC(IntPtr.Zero);
        if (screenDc == IntPtr.Zero)
            throw new InvalidOperationException("Could not read the screen.");

        var previous = SelectObject(memoryDc, bitmap);
        try
        {
            SetStretchBltMode(memoryDc, HALFTONE);
            SetBrushOrgEx(memoryDc, 0, 0, IntPtr.Zero);

            var copied = StretchBlt(
                memoryDc, 0, 0, targetWidth, targetHeight,
                screenDc, region.X, region.Y, region.Width, region.Height,
                SRCCOPY | CAPTUREBLT);

            if (!copied)
                throw new InvalidOperationException("Reading the screen region failed.");
        }
        finally
        {
            SelectObject(memoryDc, previous);
            ReleaseDC(IntPtr.Zero, screenDc);
        }
    }

    /// <summary>
    /// GDI 비트맵을 PNG로 인코딩해 응답으로 만듭니다.
    /// </summary>
    private static ScreenshotResponse Encode(IntPtr bitmap, int width, int height)
    {
        var source = Imaging.CreateBitmapSourceFromHBitmap(
            bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());

        // GDI 는 32비트 비트맵에 색 세 바이트만 쓰고 네 번째 바이트를 건드리지 않는다.
        // 그 바이트가 알파로 해석되므로, 그대로 인코딩하면 색은 맞는데 전부 투명한 PNG 가 나온다.
        var opaque = new FormatConvertedBitmap(source, PixelFormats.Bgr24, null, 0);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(opaque));

        using var memoryStream = new System.IO.MemoryStream();
        encoder.Save(memoryStream);

        return new ScreenshotResponse
        {
            Base64Png = Convert.ToBase64String(memoryStream.ToArray()),
            Width = width,
            Height = height
        };
    }

    /// <summary>
    /// 허용된 가로 픽셀 수에 맞추기 위한 축소 배율을 구합니다.
    /// </summary>
    private static double ShrinkFactor(int naturalWidth, int? maxWidth)
    {
        if (!maxWidth.HasValue || maxWidth.Value <= 0 || naturalWidth <= maxWidth.Value)
            return 1.0;

        return (double)maxWidth.Value / naturalWidth;
    }

    #endregion
}

using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Xapper.Protocol.Messages.Responses;

namespace Xapper.Tests;

/// <summary>
/// 캡처 응답에 담긴 base64 PNG를 실제로 디코딩해 확인하는 테스트 헬퍼.
/// 보고된 숫자만 믿으면 그림이 비어 있어도 테스트가 통과하므로, 인코딩된 결과를 직접 읽는다.
/// </summary>
internal static class CapturedImage
{
    /// <summary>인코딩된 PNG의 실제 픽셀 크기를 읽습니다.</summary>
    /// <param name="response">확인할 캡처 응답.</param>
    public static (int Width, int Height) SizeOf(ScreenshotResponse response)
    {
        var frame = FrameOf(response);
        return (frame.PixelWidth, frame.PixelHeight);
    }

    /// <summary>인코딩된 PNG를 BGRA 바이트 배열로 읽습니다.</summary>
    /// <param name="response">확인할 캡처 응답.</param>
    public static (byte[] Pixels, int Width, int Height) BgraPixelsOf(ScreenshotResponse response)
    {
        var converted = new FormatConvertedBitmap(FrameOf(response), PixelFormats.Bgra32, null, 0);
        var pixels = new byte[converted.PixelWidth * converted.PixelHeight * 4];
        converted.CopyPixels(pixels, converted.PixelWidth * 4, 0);
        return (pixels, converted.PixelWidth, converted.PixelHeight);
    }

    private static BitmapFrame FrameOf(ScreenshotResponse response)
    {
        using var stream = new MemoryStream(Convert.FromBase64String(response.Base64Png));
        return BitmapFrame.Create(stream, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
    }
}

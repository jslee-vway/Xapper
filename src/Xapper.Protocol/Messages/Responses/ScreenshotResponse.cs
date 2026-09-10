namespace Xapper.Protocol.Messages.Responses;

/// <summary>
/// 스크린샷 캡처 결과 응답.
/// </summary>
public sealed class ScreenshotResponse
{
    /// <summary>캡처된 이미지의 Base64 인코딩 PNG 데이터.</summary>
    public required string Base64Png { get; set; }

    /// <summary>인코딩된 이미지의 실제 가로 픽셀 수. 축소가 적용되면 축소 후 값.</summary>
    public int Width { get; set; }

    /// <summary>인코딩된 이미지의 실제 세로 픽셀 수. 축소가 적용되면 축소 후 값.</summary>
    public int Height { get; set; }
}

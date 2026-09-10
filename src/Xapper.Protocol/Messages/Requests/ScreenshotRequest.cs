namespace Xapper.Protocol.Messages.Requests;

/// <summary>
/// 윈도우 또는 특정 UI 요소의 스크린샷 캡처를 요청하는 메시지.
/// </summary>
public sealed class ScreenshotRequest
{
    /// <summary>캡처할 요소의 참조 번호. null이면 전체 윈도우를 캡처.</summary>
    public int? Ref { get; set; }

    /// <summary>인코딩할 이미지의 최대 가로 픽셀 수. 원본이 더 넓으면 가로세로 비율을 유지한 채 축소. null이면 축소하지 않음.</summary>
    public int? MaxWidth { get; set; }
}

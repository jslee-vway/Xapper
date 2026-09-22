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

    /// <summary>캡처 방식. <see cref="ScreenshotModes"/>의 값 중 하나이며, 비어 있으면 렌더 방식.</summary>
    public string? Mode { get; set; }

    /// <summary>true 면 히트테스트로 닿을 수 있는 요소에 번호 상자를 그리고 번호마다 ref 를 발급한다. 렌더 방식에서만 유효.</summary>
    public bool Annotate { get; set; }
}

/// <summary>
/// 스크린샷 캡처 방식의 이름. 요청과 도구 인자가 같은 문자열을 쓰도록 한곳에 둔다.
/// </summary>
public static class ScreenshotModes
{
    /// <summary>시각 트리를 다시 그린다. 창이 가려져 있어도 찍히지만 다른 HWND에 사는 것은 담기지 않는다.</summary>
    public const string Render = "render";

    /// <summary>화면에 합성된 픽셀을 그대로 읽는다. 팝업까지 담기지만 위를 덮은 창도 함께 찍힌다.</summary>
    public const string Screen = "screen";
}

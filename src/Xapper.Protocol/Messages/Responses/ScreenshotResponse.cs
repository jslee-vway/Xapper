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

    /// <summary>이 그림이 화면과 다를 수 있는 이유. 문제가 없으면 null.</summary>
    public string? Warning { get; set; }

    /// <summary>그림의 (0,0) 픽셀이 놓인 화면 X 좌표. 구할 수 없으면 null.</summary>
    public double? OriginX { get; set; }

    /// <summary>그림의 (0,0) 픽셀이 놓인 화면 Y 좌표. 구할 수 없으면 null.</summary>
    public double? OriginY { get; set; }

    /// <summary>화면 1픽셀이 그림에서 차지하는 픽셀 수. 축소하지 않았으면 1.</summary>
    public double Scale { get; set; } = 1.0;

    /// <summary>그림에 그린 번호 상자 목록. annotate 를 켜지 않았으면 빈 목록.</summary>
    public List<ScreenMark> Marks { get; set; } = [];

    /// <summary>상한을 넘어 그리지 않은 요소 수. 없으면 0.</summary>
    public int MarksOmitted { get; set; }

    /// <summary>
    /// 이 그림을 찍은 순간 주 창이 보이던 화면의 지문.
    /// 그림과 같은 호출에 실어 보내는 이유는, 부르는 쪽이 이 화면을 이미 배워 두었는지 따로 물어보지 않고도
    /// 알 수 있게 하기 위해서다. 구할 수 없으면 null.
    /// </summary>
    public string? Signature { get; set; }
}

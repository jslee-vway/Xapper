namespace Xapper.Protocol.Messages.Responses;

/// <summary>
/// annotate 스크린샷에 그린 번호 상자 하나. 그림의 번호와 조작에 쓸 ref 를 잇는다.
/// </summary>
public sealed class ScreenMark
{
    /// <summary>그림에 그려진 번호. 1부터 센다.</summary>
    public int Number { get; set; }

    /// <summary>이 요소를 조작할 때 쓸 참조 번호.</summary>
    public int Ref { get; set; }

    /// <summary>요소의 런타임 타입 이름.</summary>
    public required string Type { get; set; }

    /// <summary>FrameworkElement.Name 값. 없으면 null.</summary>
    public string? Name { get; set; }

    /// <summary>AutomationId 값. 없으면 null.</summary>
    public string? AutomationId { get; set; }

    /// <summary>표시 텍스트. 없으면 null.</summary>
    public string? Text { get; set; }

    /// <summary>
    /// 세션을 넘겨 이 요소에 다시 닿기 위한 기준점 셀렉터("id=…" 또는 "name=…").
    /// 요소 스스로 셀렉터로 지목될 수 있으면 null 이다. ref 는 이번 세션에서만 유효하지만 이것은 남는다.
    /// </summary>
    public string? Anchor { get; set; }

    /// <summary>기준점 안에서 이 요소 중심의 가로 비율(0.0~1.0). 기준점이 없으면 null.</summary>
    public double? AnchorX { get; set; }

    /// <summary>기준점 안에서 이 요소 중심의 세로 비율(0.0~1.0). 기준점이 없으면 null.</summary>
    public double? AnchorY { get; set; }

    /// <summary>
    /// 컨트롤 템플릿이 만들어 낸 요소인지. 스크롤바의 버튼이나 편집기 내부의 Border 처럼 앱 작성자가 놓은 것이
    /// 아니라 컨트롤이 제 모양을 갖추려고 만든 부품이면 true 다. 그림에는 그려도 기록해 둘 값어치는 없다.
    /// </summary>
    public bool FromTemplate { get; set; }
}

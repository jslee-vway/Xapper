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
}

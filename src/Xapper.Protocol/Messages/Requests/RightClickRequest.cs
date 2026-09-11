namespace Xapper.Protocol.Messages.Requests;

/// <summary>
/// 요소 위 한 지점을 마우스 오른쪽 버튼으로 클릭하도록 요청하는 메시지. 접근성에 우클릭 패턴은 없으므로 항상 좌표 제스처다.
/// </summary>
public sealed class RightClickRequest
{
    /// <summary>우클릭할 대상 요소의 참조 번호.</summary>
    public int Ref { get; set; }

    /// <summary>요소 내 상대 X 좌표 (0.0~1.0). null 이면 중앙.</summary>
    public double? X { get; set; }

    /// <summary>요소 내 상대 Y 좌표 (0.0~1.0). null 이면 중앙.</summary>
    public double? Y { get; set; }

    /// <summary>함께 누를 수식키("Ctrl", "Shift", "Alt", 조합). null 이면 없음.</summary>
    public string? Modifiers { get; set; }

    /// <summary>요소가 준비될 때까지 대기하는 최대 시간 (밀리초). 기본값 5000ms.</summary>
    public int Timeout { get; set; } = 5000;
}

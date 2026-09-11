namespace Xapper.Protocol.Messages.Requests;

/// <summary>
/// 요소 위 한 지점에서 마우스 휠을 굴리도록 요청하는 메시지. ScrollViewer 오프셋을 직접 바꾸는 scroll 과 달리
/// 실제 휠 제스처라 Ctrl+휠 줌이나 커스텀 MouseWheel 핸들러를 건드린다.
/// </summary>
public sealed class WheelRequest
{
    /// <summary>휠을 굴릴 대상 요소의 참조 번호.</summary>
    public int Ref { get; set; }

    /// <summary>굴릴 눈금 수. 양수는 위(앞), 음수는 아래(뒤). 0 은 오류.</summary>
    public int Notches { get; set; }

    /// <summary>요소 내 상대 X 좌표 (0.0~1.0). null 이면 중앙.</summary>
    public double? X { get; set; }

    /// <summary>요소 내 상대 Y 좌표 (0.0~1.0). null 이면 중앙.</summary>
    public double? Y { get; set; }

    /// <summary>함께 누를 수식키("Ctrl", "Shift", "Alt", 조합). null 이면 없음.</summary>
    public string? Modifiers { get; set; }

    /// <summary>요소가 준비될 때까지 대기하는 최대 시간 (밀리초). 기본값 5000ms.</summary>
    public int Timeout { get; set; } = 5000;
}

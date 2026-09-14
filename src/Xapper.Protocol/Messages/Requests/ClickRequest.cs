namespace Xapper.Protocol.Messages.Requests;

/// <summary>
/// UI 요소 클릭 액션을 요청하는 메시지.
/// </summary>
public sealed class ClickRequest
{
    /// <summary>클릭할 대상 요소의 참조 번호. null 이면 Target 으로 찾는다.</summary>
    public int? Ref { get; set; }

    /// <summary>요소를 찾는 selector("id=…", "name=…", "text=…", "type=…", 콤마로 AND). Ref 가 없을 때 쓴다.</summary>
    public string? Target { get; set; }

    /// <summary>요소가 준비될 때까지 대기하는 최대 시간 (밀리초). 기본값 5000ms.</summary>
    public int Timeout { get; set; } = 5000;

    /// <summary>요소 내 상대 X 좌표 (0.0~1.0 비율). null이면 중앙 클릭.</summary>
    public double? X { get; set; }

    /// <summary>요소 내 상대 Y 좌표 (0.0~1.0 비율). null이면 중앙 클릭.</summary>
    public double? Y { get; set; }

    /// <summary>true면 같은 지점을 연속 두 번 눌러 더블클릭한다. 좌표(X/Y)가 있어야 한다.</summary>
    public bool DoubleClick { get; set; }

    /// <summary>함께 누를 수식키("Ctrl", "Shift", "Alt", 조합 "Ctrl+Shift"). null 이면 없음.</summary>
    public string? Modifiers { get; set; }
}

namespace Xapper.Protocol.Messages.Requests;

/// <summary>
/// ComboBox, ListBox, TabControl 등 Selector 계열 컨트롤에서 항목 선택을 요청하는 메시지.
/// </summary>
public sealed class SelectRequest
{
    /// <summary>대상 Selector 컨트롤의 참조 번호. null 이면 Target 으로 찾는다.</summary>
    public int? Ref { get; set; }

    /// <summary>요소를 찾는 selector("id=…", "name=…", "text=…", "type=…", 콤마로 AND). Ref 가 없을 때 쓴다.</summary>
    public string? Target { get; set; }

    /// <summary>선택할 항목의 텍스트. ItemIndex와 함께 사용 시 텍스트 우선.</summary>
    public string? ItemText { get; set; }

    /// <summary>선택할 항목의 0-based 인덱스.</summary>
    public int? ItemIndex { get; set; }

    /// <summary>요소가 준비될 때까지 대기하는 최대 시간 (밀리초). 기본값 5000ms.</summary>
    public int Timeout { get; set; } = 5000;
}

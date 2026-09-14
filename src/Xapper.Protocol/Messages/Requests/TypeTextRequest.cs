namespace Xapper.Protocol.Messages.Requests;

/// <summary>
/// UI 요소에 텍스트를 입력하는 액션을 요청하는 메시지.
/// </summary>
public sealed class TypeTextRequest
{
    /// <summary>텍스트를 입력할 대상 요소의 참조 번호. null 이면 현재 키보드 포커스 요소에 실제 키 입력으로 타이핑한다(F2 로 연 인라인 편집기 등).</summary>
    public int? Ref { get; set; }

    /// <summary>요소를 찾는 selector("id=…", "name=…", "text=…", "type=…", 콤마로 AND). Ref 가 없을 때 쓴다.</summary>
    public string? Target { get; set; }

    /// <summary>입력할 텍스트 문자열.</summary>
    public required string Text { get; set; }

    /// <summary>true이면 기존 텍스트를 지우고 입력, false이면 기존 텍스트 뒤에 추가. 기본값 true.</summary>
    public bool Clear { get; set; } = true;

    /// <summary>요소가 준비될 때까지 대기하는 최대 시간 (밀리초). 기본값 5000ms.</summary>
    public int Timeout { get; set; } = 5000;
}

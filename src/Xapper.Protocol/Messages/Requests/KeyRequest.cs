namespace Xapper.Protocol.Messages.Requests;

/// <summary>
/// 대상 프로세스 안에서 키보드 포커스 요소(또는 지정 요소)에 키를 넣도록 요청하는 메시지.
/// 전경 창·물리 키보드와 무관하게 대상 앱의 WPF 입력 파이프라인에 직접 주입한다.
/// </summary>
public sealed class KeyRequest
{
    /// <summary>보낼 키. WPF Key 열거형 이름("F2","Enter","Escape","Tab","Down") 또는 한 글자("a").</summary>
    public string Key { get; set; } = "";

    /// <summary>함께 누를 수식키("Ctrl", "Shift", "Alt", 조합 "Ctrl+Shift"). 대상 프로세스 안에서 스푸프하며, 후크 불가 시 오류로 안내한다.</summary>
    public string? Modifiers { get; set; }

    /// <summary>먼저 키보드 포커스를 줄 대상 요소의 참조 번호. 생략 시 현재 포커스 요소에 보낸다.</summary>
    public int? Ref { get; set; }

    /// <summary>요소를 찾는 selector("id=…", "name=…", "text=…", "type=…", 콤마로 AND). Ref 가 없을 때 쓴다.</summary>
    public string? Target { get; set; }

    /// <summary>요소가 준비될 때까지 대기하는 최대 시간 (밀리초). 기본값 5000ms.</summary>
    public int Timeout { get; set; } = 5000;
}

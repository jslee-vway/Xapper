namespace Xapper.Protocol.Messages.Requests;

/// <summary>
/// UI 요소의 특정 프로퍼티 값을 조회하는 요청 메시지.
/// DependencyProperty와 CLR 프로퍼티 모두 조회 가능.
/// </summary>
public sealed class GetPropertyRequest
{
    /// <summary>대상 요소의 참조 번호. null 이면 Target 으로 찾는다.</summary>
    public int? Ref { get; set; }

    /// <summary>요소를 찾는 selector("id=…", "name=…", "text=…", "type=…", 콤마로 AND). Ref 가 없을 때 쓴다.</summary>
    public string? Target { get; set; }

    /// <summary>조회할 프로퍼티 이름 (예: "IsEnabled", "Text", "Visibility").</summary>
    public required string PropertyName { get; set; }
}

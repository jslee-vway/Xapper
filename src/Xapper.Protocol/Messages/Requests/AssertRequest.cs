namespace Xapper.Protocol.Messages.Requests;

/// <summary>
/// UI 요소의 프로퍼티 값이 기대값과 일치하는지 검증하는 요청 메시지.
/// </summary>
public sealed class AssertRequest
{
    /// <summary>검증 대상 요소의 참조 번호. null 이면 Target 으로 찾는다.</summary>
    public int? Ref { get; set; }

    /// <summary>요소를 찾는 selector("id=…", "name=…", "text=…", "type=…", 콤마로 AND). Ref 가 없을 때 쓴다.</summary>
    public string? Target { get; set; }

    /// <summary>검증할 프로퍼티 이름.</summary>
    public required string Property { get; set; }

    /// <summary>기대하는 프로퍼티 값 (문자열 비교).</summary>
    public required string Expected { get; set; }
}

namespace Xapper.Protocol.Messages.Requests;

/// <summary>
/// 앱에서 최근에 일어난 이벤트를 달라는 메시지.
/// </summary>
public sealed class EventsRequest
{
    /// <summary>돌려받을 최대 건수. 최근 것부터 센다.</summary>
    public int Count { get; set; } = 10;

    /// <summary>이름·타입·요소 이름 중 하나에 이 글자가 든 것만. 비어 있으면 거르지 않는다.</summary>
    public string? Filter { get; set; }
}

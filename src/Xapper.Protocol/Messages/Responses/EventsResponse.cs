namespace Xapper.Protocol.Messages.Responses;

/// <summary>
/// 최근에 일어난 이벤트 목록. 시간 순서대로이며 마지막 줄이 가장 최근이다.
/// </summary>
public sealed class EventsResponse
{
    /// <summary>이벤트 목록.</summary>
    public List<EventEntry> Events { get; set; } = [];

    /// <summary>감시를 시작한 뒤 담아 둔 전체 건수. 요청한 수보다 많으면 더 있다는 뜻이다.</summary>
    public int Stored { get; set; }
}

/// <summary>
/// 이벤트 한 건.
/// </summary>
public sealed class EventEntry
{
    /// <summary>이벤트 이름. 예: SelectionChanged.</summary>
    public required string Name { get; set; }

    /// <summary>이벤트를 낸 요소의 타입 이름.</summary>
    public required string SourceType { get; set; }

    /// <summary>그 요소의 자동화 ID 또는 x:Name. 둘 다 없으면 null.</summary>
    public string? SourceName { get; set; }

    /// <summary>값이 있는 이벤트면 그 값 한 조각. 없으면 null.</summary>
    public string? Detail { get; set; }

    /// <summary>지금으로부터 몇 밀리초 전에 일어났는지.</summary>
    public long AgoMs { get; set; }
}

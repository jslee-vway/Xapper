namespace Xapper.Inspector.Events;

/// <summary>
/// 최근에 일어난 이벤트만 담아 두는 고리 버퍼.
///
/// 전부 모아 두지 않는 이유는 둘이다. 이벤트는 사람이 손을 조금만 움직여도 쏟아지므로 모아 두면 메모리가
/// 늘고, 오래된 것은 어차피 쓸모가 없다. 묻는 쪽이 알고 싶은 것은 언제나 "방금 무슨 일이 있었는가" 다.
///
/// 여러 스레드에서 들어온다. 이벤트는 UI 스레드가 넣고, 읽기는 IPC 스레드가 한다.
/// </summary>
public sealed class EventLog
{
    #region Constants

    /// <summary>담아 두는 최대 건수. 넘으면 오래된 것부터 밀려난다.</summary>
    public const int Capacity = 200;

    #endregion

    #region Fields

    private readonly object _gate = new();
    private readonly Queue<LoggedEvent> _entries = new(Capacity);

    #endregion

    #region Public Methods

    /// <summary>이벤트 한 건을 담습니다. 상한을 넘으면 가장 오래된 것을 버린다.</summary>
    /// <param name="entry">담을 이벤트.</param>
    public void Add(LoggedEvent entry)
    {
        lock (_gate)
        {
            _entries.Enqueue(entry);
            while (_entries.Count > Capacity)
                _entries.Dequeue();
        }
    }

    /// <summary>
    /// 가장 최근 것부터 세어 요청한 수만큼 돌려줍니다. 시간 순서는 그대로 두므로 마지막 줄이 가장 최근이다.
    /// </summary>
    /// <param name="count">돌려줄 최대 건수. 0 이하이면 빈 목록.</param>
    /// <param name="filter">이름·타입·요소 이름 중 하나에 이 글자가 들어간 것만. 비어 있으면 거르지 않는다.</param>
    public List<LoggedEvent> Recent(int count, string? filter = null)
    {
        if (count <= 0)
            return [];

        List<LoggedEvent> all;
        lock (_gate)
            all = [.. _entries];

        var matching = string.IsNullOrWhiteSpace(filter)
            ? all
            : all.Where(entry => Matches(entry, filter.Trim())).ToList();

        return matching.Count <= count
            ? matching
            : matching.GetRange(matching.Count - count, count);
    }

    /// <summary>담긴 건수. 시험이 상한을 확인하는 데 쓴다.</summary>
    public int Count
    {
        get
        {
            lock (_gate)
                return _entries.Count;
        }
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// 이 이벤트가 찾는 글자에 걸리는지.
    /// 이벤트 이름만 보면 "그 그리드에서 무슨 일이 있었나" 를 물을 수 없으므로 요소 쪽도 함께 본다.
    /// </summary>
    private static bool Matches(LoggedEvent entry, string filter)
    {
        return entry.Name.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || entry.SourceType.Contains(filter, StringComparison.OrdinalIgnoreCase)
            || (entry.SourceName?.Contains(filter, StringComparison.OrdinalIgnoreCase) ?? false);
    }

    #endregion
}

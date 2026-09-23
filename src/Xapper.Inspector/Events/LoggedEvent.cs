namespace Xapper.Inspector.Events;

/// <summary>
/// 앱에서 일어난 라우티드 이벤트 한 건을, 요소를 붙들지 않고 남길 수 있는 모양으로 옮긴 것.
///
/// 요소 참조를 들고 있으면 버퍼가 앱의 수명을 늘린다. 화면에서 사라진 행과 닫힌 대화상자가 기록 때문에
/// 살아 있게 되므로, 일어나는 그 자리에서 문자열로 바꾸어 담는다.
/// </summary>
/// <param name="At">일어난 시각. <see cref="Environment.TickCount64"/> 기준이며, 앞뒤를 가르는 데만 쓴다.</param>
/// <param name="Name">이벤트 이름. 예: SelectionChanged.</param>
/// <param name="SourceType">이벤트를 낸 요소의 타입 이름. 예: GridControl.</param>
/// <param name="SourceName">그 요소의 자동화 ID 또는 x:Name. 둘 다 없으면 null.</param>
/// <param name="Detail">값이 있는 이벤트면 그 값 한 조각. 없으면 null.</param>
public sealed record LoggedEvent(
    long At,
    string Name,
    string SourceType,
    string? SourceName,
    string? Detail);

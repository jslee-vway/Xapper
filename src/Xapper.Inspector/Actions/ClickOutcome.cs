namespace Xapper.Inspector.Actions;

/// <summary>
/// 클릭이 실제로 어느 경로로 수행됐는지와, 주의가 필요한 상황을 함께 담는 결과.
/// 경로마다 대상 앱에 닿는 방식이 다르고 어떤 경로는 마우스 이벤트를 전혀 만들지 않으므로,
/// 호출자가 결과를 해석하려면 어느 길로 갔는지 알아야 한다.
/// </summary>
/// <param name="Path">수행된 클릭 경로에 대한 사람이 읽을 수 있는 설명.</param>
/// <param name="Warning">주의가 필요한 상황. 없으면 null.</param>
public readonly record struct ClickOutcome(string Path, string? Warning);

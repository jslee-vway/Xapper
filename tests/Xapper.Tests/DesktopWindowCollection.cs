namespace Xapper.Tests;

/// <summary>
/// 실제 창을 화면에 띄우는 테스트들을 한 줄로 세우는 컬렉션.
/// 화면은 전역 자원이다. 이 테스트들은 모두 같은 자리에 창을 놓고 그 화면을 찍거나 그 지점을 조회하므로,
/// 병렬로 돌면 서로의 창을 찍고 서로의 좌표를 가로챈다. 실패가 코드가 아니라 동시 실행 때문에 나는 것을 막는다.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class DesktopWindowCollection
{
    /// <summary>컬렉션 이름.</summary>
    public const string Name = "desktop windows";
}

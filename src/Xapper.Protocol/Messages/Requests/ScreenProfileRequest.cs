namespace Xapper.Protocol.Messages.Requests;

/// <summary>
/// 현재 화면의 지문과, 요청하면 조작할 수 있는 영역 목록까지 함께 달라는 메시지.
/// </summary>
public sealed class ScreenProfileRequest
{
    /// <summary>
    /// true 면 영역 목록도 함께 돌려준다. 영역 계산은 요소마다 히트테스트를 하므로 지문 계산보다 비싸다.
    /// 조회는 지문만 있으면 되고, 기록할 때만 영역이 필요하다.
    /// </summary>
    public bool IncludeRegions { get; set; }

    /// <summary>
    /// true 면 셀렉터로 지목되는 영역만 돌려준다. 기준점이 붙는 영역은 "클릭이 닿는가" 를 따져야 해서
    /// 요소마다 히트테스트가 돌지만, 셀렉터로 잡히는 영역은 트리를 한 번 훑는 것으로 끝난다.
    /// 조회는 이미 아는 영역의 지금 텍스트만 있으면 되므로 이쪽을 쓴다.
    /// </summary>
    public bool AddressableOnly { get; set; }
}

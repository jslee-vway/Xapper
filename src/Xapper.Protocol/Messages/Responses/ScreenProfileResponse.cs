namespace Xapper.Protocol.Messages.Responses;

/// <summary>
/// 현재 화면의 지문과 영역 목록.
/// </summary>
public sealed class ScreenProfileResponse
{
    /// <summary>주 창의 구조에서 뽑은 지문.</summary>
    public required string Signature { get; set; }

    /// <summary>지문에 들어간 고유 요소 수. 화면 규모를 가늠하는 데 쓴다.</summary>
    public int ElementCount { get; set; }

    /// <summary>조작할 수 있는 영역 목록. 요청하지 않았으면 빈 목록.</summary>
    public List<ScreenMark> Regions { get; set; } = [];
}

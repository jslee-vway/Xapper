namespace Xapper.Protocol.Messages.Requests;

/// <summary>
/// 화면 좌표에 있는 UI 요소를 조회하는 요청.
/// </summary>
public sealed class ElementAtRequest
{
    /// <summary>조회할 화면 X 좌표 (픽셀).</summary>
    public double X { get; set; }

    /// <summary>조회할 화면 Y 좌표 (픽셀).</summary>
    public double Y { get; set; }

    /// <summary>함께 돌려받을 조상의 최대 수. 히트테스트는 가장 깊은 요소를 주므로 위쪽 층도 함께 봐야 고를 수 있다.</summary>
    public int MaxAncestors { get; set; } = 4;
}

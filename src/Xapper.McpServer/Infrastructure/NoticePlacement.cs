namespace Xapper.McpServer.Infrastructure;

/// <summary>
/// 알림 창을 어디에 둘지 계산한다. 좌표계는 호출자가 넘긴 그대로(물리 픽셀)라 DPI 변환을 하지 않는다 —
/// 대상 창 사각형과 알림 창 크기를 같은 API(GetWindowRect)로 읽으면 단위가 맞는다.
/// </summary>
internal static class NoticePlacement
{
    #region Fields

    /// <summary>대상 창 위쪽 가장자리에서 알림까지의 거리(픽셀). 제목 표시줄을 가리지 않을 만큼만 내린다.</summary>
    public const int TopMargin = 24;

    #endregion

    #region Public Methods

    /// <summary>대상 사각형의 위쪽 가운데에 알림을 놓을 때의 왼쪽 위 좌표.</summary>
    public static (int X, int Y) TopCentre(int left, int top, int right, int bottom, int noticeWidth, int noticeHeight)
    {
        var targetWidth = right - left;
        var x = left + Math.Max(0, (targetWidth - noticeWidth) / 2);
        var y = top + TopMargin;
        return (x, y);
    }

    #endregion
}

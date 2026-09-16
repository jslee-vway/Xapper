using Xapper.McpServer.Infrastructure;

namespace Xapper.Tests;

/// <summary>
/// 알림 창 좌표: 대상 창의 위쪽 가운데. 대상 창이 없으면 주어진 화면 영역의 위쪽 가운데.
/// </summary>
public class NoticePlacementTests
{
    [Fact]
    public void TopCentre_CentresHorizontallyAndKeepsTheTopMargin()
    {
        // 대상 창 (100,200)-(700,600), 알림 200x60 → x = 100 + (600-200)/2 = 300, y = 200 + 24
        var (x, y) = NoticePlacement.TopCentre(left: 100, top: 200, right: 700, bottom: 600, noticeWidth: 200, noticeHeight: 60);

        Assert.Equal(300, x);
        Assert.Equal(224, y);
    }

    [Fact]
    public void TopCentre_WhenTheNoticeIsWiderThanTheTarget_AlignsToTheTargetLeft()
    {
        // 알림이 대상보다 넓으면 음수 오프셋으로 왼쪽 밖으로 나가지 않게 대상 왼쪽에 맞춘다.
        var (x, _) = NoticePlacement.TopCentre(left: 100, top: 0, right: 200, bottom: 100, noticeWidth: 300, noticeHeight: 60);

        Assert.Equal(100, x);
    }
}

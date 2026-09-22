using Xapper.McpServer.Infrastructure;
using Xapper.McpServer.Tools;

namespace Xapper.Tests;

/// <summary>
/// 기록을 응답 문장으로 옮기는 형식을 고정한다. 모델은 이 문장만 읽고 스크린샷 없이 조작해야 하므로,
/// 무엇을 어떻게 지목할지가 한 줄에 다 있어야 한다.
/// </summary>
public class ScreenRecallSummaryTests
{
    private static ScreenRecord Record() => new()
    {
        Signature = "abc", App = "Demo", Name = "로그인 화면", Notes = "저장은 Ctrl+S 로도 된다", SeenCount = 3,
        Regions =
        [
            new ScreenRegion { Type = "TextBox", Selector = "id=UsernameInput" },
            new ScreenRegion { Type = "Grid", Anchor = "id=Panel", AnchorX = 0.5, AnchorY = 0.72 }
        ]
    };

    [Fact]
    public void Known_NamesTheScreenAndListsHowToReachEachRegion()
    {
        var text = ScreenRecallSummary.Known(Record());

        Assert.Contains("로그인 화면", text);
        Assert.Contains("id=UsernameInput", text);
        Assert.Contains("in id=Panel at 0.5,0.72", text);
        Assert.Contains("저장은 Ctrl+S 로도 된다", text);
    }

    [Fact]
    public void Unknown_TellsTheCallerWhatToDoNext()
    {
        var text = ScreenRecallSummary.Unknown("a3f9c1e8", changedSinceLastRecall: true);

        Assert.Contains("a3f9c1e8", text);
        Assert.Contains("xapper_screen_learn", text);
        Assert.Contains("changed", text);
    }

    [Fact]
    public void Unknown_WithoutAChangeDoesNotClaimOne()
    {
        var text = ScreenRecallSummary.Unknown("a3f9c1e8", changedSinceLastRecall: false);

        Assert.DoesNotContain("changed", text);
    }
}

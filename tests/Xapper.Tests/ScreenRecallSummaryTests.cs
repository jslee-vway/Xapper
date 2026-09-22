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
    public void Known_ListsEveryRegionItWasGiven()
    {
        // 저장 단계에서 텍스트로만 잡히는 데이터 행과 컨트롤 템플릿 부품을 이미 걸렀으므로,
        // 요약은 받은 것을 빠짐없이 보여 주기만 하면 된다.
        var record = Record();

        var text = ScreenRecallSummary.Known(record);

        var listed = text.Split(Environment.NewLine.ToCharArray())
            .Count(line => line.Contains("id=") || line.Contains(" in "));
        Assert.Equal(record.Regions.Count, listed);
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

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
    public void Known_SaysSo_WhenTheRegionsWereJustRetaken()
    {
        // 영역이 방금 바뀌었다는 사실을 알리지 않으면, 이 기록을 예전에 본 쪽이 옛 목록을 들고 움직인다.
        var text = ScreenRecallSummary.Known(Record(), regionsRefreshed: true);

        Assert.Contains("taken again from the live screen", text);
    }

    [Fact]
    public void Known_StaysQuiet_WhenTheRegionsWereNotTouched()
    {
        Assert.DoesNotContain("taken again from the live screen", ScreenRecallSummary.Known(Record()));
    }

    [Fact]
    public void Known_ExplainsHowToActOnAnAnchoredLine()
    {
        // 기준점 줄의 좌표는 요소 안의 비율인데, 그것을 모르면 픽셀로 착각해 엉뚱한 자리를 누른다.
        // 쓰는 법이 줄 자체에 드러나지 않으므로 응답이 직접 일러 준다.
        var text = ScreenRecallSummary.Known(Record());

        Assert.Contains("fractions of X", text);
        Assert.Contains("target=X", text);
    }

    [Fact]
    public void Known_DoesNotExplainAnchors_WhenEveryRegionHasASelector()
    {
        // 기준점 줄이 없으면 그 설명은 자리만 차지한다.
        var record = Record();
        record.Regions = [new ScreenRegion { Type = "TextBox", Selector = "id=UsernameInput" }];

        Assert.DoesNotContain("fractions of X", ScreenRecallSummary.Known(record));
    }

    [Fact]
    public void Known_TellsTheReaderToActOnTheNotesRatherThanRecheckThem()
    {
        var text = ScreenRecallSummary.Known(Record());

        Assert.Contains("act on them rather than finding out again", text);
        Assert.Contains("replace", text);
    }

    [Fact]
    public void Known_RefusesToPromiseAnythingFromARecordWithNoSelector()
    {
        // 좌표 몇 줄만 들고 "스크린샷 없이 조작하라" 고 말하면 잘못된 확신을 준다.
        // 실측에서 기준점 여섯 줄짜리 기록을 받은 에이전트가 바로 다음 호출로 스크린샷을 찍었다.
        var record = Record();
        record.Regions =
        [
            new ScreenRegion { Type = "BarItemLinkInfo", Anchor = "name=RootGrid", AnchorX = 0.06, AnchorY = 0.39 }
        ];

        var text = ScreenRecallSummary.Known(record);

        Assert.DoesNotContain("Act on these without a screenshot", text);
        Assert.Contains("xapper_screen_learn", text);
    }

    [Fact]
    public void Known_KeepsTheNotes_EvenWhenTheRecordIsTooThinToActOn()
    {
        // 영역이 쓸모없어도 비고는 값이 있다. 함정을 적어 둔 줄이 여기서 사라지면 다시 겪는다.
        var record = Record();
        record.Regions = [new ScreenRegion { Type = "Grid", Anchor = "id=Panel", AnchorX = 0.5, AnchorY = 0.5 }];

        Assert.Contains("저장은 Ctrl+S 로도 된다", ScreenRecallSummary.Known(record));
    }

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

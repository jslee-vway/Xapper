using Xapper.McpServer.Tools;
using Xapper.Protocol.Messages.Responses;

namespace Xapper.Tests;

/// <summary>
/// 번호 상자 목록을 요약 텍스트로 옮기는 형식을 검증한다. 에이전트는 그림에서 번호를 읽고
/// 이 목록에서 ref 를 찾으므로, 번호와 ref 가 한 줄에 함께 있어야 한다.
/// </summary>
public class ScreenMarkSummaryTests
{
    [Fact]
    public void Describe_PutsTheNumberAndRefOnOneLine()
    {
        var marks = new List<ScreenMark>
        {
            new() { Number = 1, Ref = 12, Type = "Button", AutomationId = "LoginButton", Text = "Log In" }
        };

        var summary = ScreenMarkSummary.Describe(marks, omitted: 0);

        Assert.Contains("1: Button id=\"LoginButton\" text=\"Log In\" ref=12", summary);
    }

    [Fact]
    public void Describe_LeavesOutFieldsTheElementDoesNotHave()
    {
        var marks = new List<ScreenMark> { new() { Number = 2, Ref = 13, Type = "LightweightCellEditor" } };

        var summary = ScreenMarkSummary.Describe(marks, omitted: 0);

        Assert.Contains("2: LightweightCellEditor ref=13", summary);
        Assert.DoesNotContain("id=", summary);
        Assert.DoesNotContain("name=", summary);
    }

    [Fact]
    public void Describe_ShowsTheAnchorSoTheElementCanBeReachedAgainLater()
    {
        var marks = new List<ScreenMark>
        {
            new() { Number = 3, Ref = 8, Type = "Grid", Anchor = "id=LoginPanel", AnchorX = 0.5, AnchorY = 0.72 }
        };

        var summary = ScreenMarkSummary.Describe(marks, omitted: 0);

        Assert.Contains("3: Grid in id=LoginPanel at 0.5,0.72 ref=8", summary);
    }

    [Fact]
    public void Describe_SaysHowManyWereLeftOut()
    {
        var marks = new List<ScreenMark> { new() { Number = 1, Ref = 1, Type = "Button" } };

        var summary = ScreenMarkSummary.Describe(marks, omitted: 7);

        Assert.Contains("7 more", summary);
    }

    [Fact]
    public void Describe_OfNothingIsEmpty()
    {
        Assert.Equal("", ScreenMarkSummary.Describe(new List<ScreenMark>(), omitted: 0));
    }
}

using Xapper.McpServer.Infrastructure;

namespace Xapper.Tests;

/// <summary>
/// 안전망 판정: 응답이 실제 마우스를 썼다고 말하고 알림이 내려가 있을 때만 올린다.
/// </summary>
public class OperatorNoticePolicyTests
{
    [Theory]
    [InlineData("Clicked ref=3 at (0.50,0.50) via real mouse input", false, true)]
    [InlineData("Clicked ref=3 at (0.50,0.50) via real mouse input", true, false)]
    [InlineData("Clicked ref=3 at (0.50,0.50) via synthetic mouse input (no cursor movement)", false, false)]
    [InlineData("Dragged (10,10) -> (50,50) via its own drag events (no real input)", false, false)]
    [InlineData("Error: element not found", false, false)]
    public void ShouldRaise_OnlyWhenTheRealMouseWasUsedAndTheNoticeIsDown(string text, bool visible, bool expected)
    {
        Assert.Equal(expected, OperatorNoticePolicy.ShouldRaise(text, visible));
    }

    [Fact]
    public void Annotate_AppendsTheHideReminder()
    {
        var annotated = OperatorNoticePolicy.Annotate("Clicked via real mouse input");

        Assert.StartsWith("Clicked via real mouse input | NOTE: the notice was raised automatically", annotated);
        Assert.Contains("xapper_notice_hide", annotated);
    }
}

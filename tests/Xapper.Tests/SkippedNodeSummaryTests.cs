using Xapper.McpServer.Tools;

namespace Xapper.Tests;

/// <summary>
/// 건너뛴 노드 목록을 한 줄로 묶는 규칙을 검증한다. 같은 사유가 반복되는 것이 보통이라 사유별로 세어 알린다.
/// </summary>
public class SkippedNodeSummaryTests
{
    [Fact]
    public void Summarize_GroupsOneReasonWithItsParentTypeAndDepth()
    {
        var lines = new[]
        {
            "a depth=150 child[0]: child slot was empty",
            "a depth=150 child[0]: child slot was empty",
            "a depth=150 child[0]: child slot was empty"
        };

        var summary = SkippedNodeSummary.Summarize(lines);

        Assert.Equal("3x \"child slot was empty\" (under a, depth 150)", summary);
    }

    [Fact]
    public void Summarize_ListsEachReasonMostCommonFirst()
    {
        var lines = new[]
        {
            "a depth=150 child[0]: child slot was empty",
            "a depth=150 child[1]: child slot was empty",
            "Grid name=\"root\" depth=3: NullReferenceException"
        };

        var summary = SkippedNodeSummary.Summarize(lines);

        Assert.StartsWith("2x \"child slot was empty\" (under a, depth 150)", summary);
        Assert.Contains("1x \"NullReferenceException\" (under Grid, depth 3)", summary);
    }

    [Fact]
    public void Summarize_ShowsARangeWhenTheDepthsDiffer()
    {
        var lines = new[]
        {
            "a depth=12 child[0]: child slot was empty",
            "a depth=150 child[0]: child slot was empty"
        };

        var summary = SkippedNodeSummary.Summarize(lines);

        Assert.Contains("depth 12-150", summary);
    }

    [Fact]
    public void Summarize_KeepsGoingWhenALineHasNoReason()
    {
        var lines = new[] { "something unparseable" };

        var summary = SkippedNodeSummary.Summarize(lines);

        Assert.Contains("unknown", summary);
    }

    [Fact]
    public void Summarize_CapsTheReasonListAndCountsTheRest()
    {
        var lines = new[]
        {
            "A depth=1: reason one",
            "B depth=2: reason two",
            "C depth=3: reason three",
            "D depth=4: reason four"
        };

        var summary = SkippedNodeSummary.Summarize(lines);

        Assert.Contains("+1 other reason(s)", summary);
    }

    [Fact]
    public void Summarize_SaysSoWhenTheSameReasonCameFromSeveralParentTypes()
    {
        var lines = new[]
        {
            "VirtualizingStackPanel depth=6: child slot was empty",
            "VirtualizingStackPanel depth=7: child slot was empty",
            "ItemsPresenter depth=9: child slot was empty"
        };

        var summary = SkippedNodeSummary.Summarize(lines);

        // 가장 흔한 부모만 적고 말면 호출자가 셋 다 그 아래라고 읽는다.
        Assert.Contains("under VirtualizingStackPanel +1 more type(s)", summary);
    }

    [Fact]
    public void Summarize_OfNothingIsEmpty()
    {
        Assert.Equal("", SkippedNodeSummary.Summarize(Array.Empty<string>()));
    }
}

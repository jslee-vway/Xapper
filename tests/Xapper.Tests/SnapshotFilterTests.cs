using Xapper.McpServer.Tools;
using Xapper.Protocol;

namespace Xapper.Tests;

/// <summary>
/// 스냅샷에서 셀렉터로 지목할 수 없는 레이아웃 컨테이너를 접는 규칙을 검증한다.
/// 접힌 컨테이너 아래의 지목 가능한 요소는 사라지지 않고 가장 가까운 남은 조상 밑으로 올라와야 한다.
/// </summary>
public class SnapshotFilterTests
{
    private static ElementSnapshot Node(string type, string? name = null, string? id = null, string? text = null,
        params ElementSnapshot[] children)
        => new()
        {
            Type = type, Name = name, AutomationId = id, Text = text,
            Children = children.ToList()
        };

    [Fact]
    public void KeepAddressable_KeepsElementsThatHaveAnId_NameOrText()
    {
        var root = Node("Window", name: "main",
            children: new[]
            {
                Node("Button", id: "Save"),
                Node("TextBlock", text: "Hello"),
                Node("TextBox", name: "txtUser")
            });

        var (kept, folded) = SnapshotFilter.KeepAddressable(root);

        Assert.Equal(3, kept.Children.Count);
        Assert.Equal(0, folded);
    }

    [Fact]
    public void KeepAddressable_FoldsAnonymousContainersAndPromotesTheirDescendants()
    {
        var root = Node("Window", name: "main",
            children: Node("Grid",
                children: Node("Border",
                    children: Node("Button", id: "Save"))));

        var (kept, folded) = SnapshotFilter.KeepAddressable(root);

        var only = Assert.Single(kept.Children);
        Assert.Equal("Save", only.AutomationId);
        Assert.Equal(2, folded);
    }

    [Fact]
    public void KeepAddressable_KeepsTheRootEvenWhenItHasNothingToTargetBy()
    {
        var root = Node("Application", children: Node("Button", id: "Save"));

        var (kept, _) = SnapshotFilter.KeepAddressable(root);

        Assert.Equal("Application", kept.Type);
        Assert.Single(kept.Children);
    }

    [Fact]
    public void KeepAddressable_CarriesTheRefAndStateOver()
    {
        var root = Node("Window", name: "main");
        root.Ref = 1;
        var button = Node("Button", id: "Save");
        button.Ref = 7;
        button.IsEnabled = false;
        button.IsVisible = true;
        root.Children.Add(button);

        var (kept, _) = SnapshotFilter.KeepAddressable(root);

        var copy = Assert.Single(kept.Children);
        Assert.Equal(7, copy.Ref);
        Assert.False(copy.IsEnabled);
        Assert.True(copy.IsVisible);
    }

    [Fact]
    public void KeepAddressable_LeavesTheOriginalTreeAlone()
    {
        var root = Node("Window", name: "main",
            children: Node("Grid", children: Node("Button", id: "Save")));

        SnapshotFilter.KeepAddressable(root);

        var grid = Assert.Single(root.Children);
        Assert.Equal("Grid", grid.Type);
        Assert.Single(grid.Children);
    }

    [Fact]
    public void KeepAddressable_TreatsBlankStringsAsNothingToTargetBy()
    {
        var root = Node("Window", name: "main",
            children: Node("Grid", name: "", id: "", text: "",
                children: Node("Button", id: "Save")));

        var (kept, folded) = SnapshotFilter.KeepAddressable(root);

        Assert.Equal("Save", Assert.Single(kept.Children).AutomationId);
        Assert.Equal(1, folded);
    }
}

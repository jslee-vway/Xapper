using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Xapper.Inspector.VisualTree;

namespace Xapper.Tests;

/// <summary>
/// 화면 지문이 무엇에 흔들리고 무엇에 흔들리지 않아야 하는지 고정한다.
/// 설계는 실측으로 정했다: 텍스트를 빼면 데이터 변화를 견디고, 중복 없는 집합으로 세면 가상화 목록의
/// 스크롤을 견딘다. 이 테스트는 그 두 성질이 깨지지 않게 지킨다.
/// </summary>
public class ScreenSignatureTests
{
    #region Helpers

    private static Border Item(string type, string? name = null, string? id = null, string? text = null)
    {
        var border = new Border { Width = 40, Height = 20, Background = Brushes.Gray };
        if (name is not null) border.Name = name;
        if (id is not null) border.SetValue(System.Windows.Automation.AutomationProperties.AutomationIdProperty, id);
        if (text is not null) border.Child = new TextBlock { Text = text };
        return border;
    }

    private static Panel Tree(params UIElement[] children)
    {
        var panel = new StackPanel();
        foreach (var child in children) panel.Children.Add(child);
        panel.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        panel.Arrange(new Rect(0, 0, 200, 400));
        panel.UpdateLayout();
        return panel;
    }

    #endregion

    [Fact]
    public void Of_IsTheSameForTheSameTree()
    {
        StaThread.Run(() =>
        {
            var tree = Tree(Item("a", id: "One"), Item("b", name: "two"));

            Assert.Equal(ScreenSignature.Of(tree), ScreenSignature.Of(tree));
        });
    }

    [Fact]
    public void Of_IgnoresTextSoDataChangesDoNotMoveIt()
    {
        StaThread.Run(() =>
        {
            var before = Tree(Item("a", id: "Cell", text: "Alpha"));
            var after = Tree(Item("a", id: "Cell", text: "완전히 다른 값"));

            Assert.Equal(ScreenSignature.Of(before), ScreenSignature.Of(after));
        });
    }

    [Fact]
    public void Of_IgnoresHowManyOfTheSameKindThereAreSoScrollingDoesNotMoveIt()
    {
        StaThread.Run(() =>
        {
            // 가상화 목록을 스크롤하면 같은 종류의 행 컨테이너가 더 만들어진다. 개수를 세면 지문이 흔들린다.
            var few = Tree(Item("row", id: "Row"), Item("row", id: "Row"));
            var many = Tree(Item("row", id: "Row"), Item("row", id: "Row"),
                            Item("row", id: "Row"), Item("row", id: "Row"));

            Assert.Equal(ScreenSignature.Of(few), ScreenSignature.Of(many));
        });
    }

    [Fact]
    public void Of_ChangesWhenADifferentKindOfElementAppears()
    {
        StaThread.Run(() =>
        {
            var before = Tree(Item("a", id: "One"));
            var after = Tree(Item("a", id: "One"), Item("b", id: "Two"));

            Assert.NotEqual(ScreenSignature.Of(before), ScreenSignature.Of(after));
        });
    }

    [Fact]
    public void Of_OfNothingIsStillAValue()
    {
        StaThread.Run(() => Assert.False(string.IsNullOrEmpty(ScreenSignature.Of(Tree()))));
    }
}

using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Xapper.Inspector.Actions;

namespace Xapper.Tests;

/// <summary>
/// 액션 실행기가 "할 수 없는" 상황에서 예외 대신 실패 결과를 돌려주는지 검증하는 테스트 클래스.
/// 주입된 프로세스 안에서 던지면 대상 앱의 first-chance 핸들러가 앱을 죽일 수 있으므로(결함 2),
/// 지원하지 않는 컨트롤·없는 항목 같은 정상적 실패는 반드시 값으로 돌아와야 한다.
/// 이 실행기들은 <c>Application.Current</c> 없이 현재 STA 스레드에서 동작하므로 직접 호출해 검증한다.
/// </summary>
public class ActionResultTests
{
    [Fact]
    public void Type_OnAnElementThatIsNotEditable_FailsWithoutThrowing()
    {
        StaThread.Run(() =>
        {
            var result = TypeAction.Execute(new Border(), "hello", clear: true);
            Assert.NotNull(result.Error);
            Assert.Contains("does not support text input", result.Error);
        });
    }

    [Fact]
    public void Select_WithTextThatDoesNotMatchAnyItem_FailsWithoutThrowing()
    {
        StaThread.Run(() =>
        {
            var combo = new ComboBox();
            combo.Items.Add("Alpha");
            combo.Items.Add("Bravo");

            var result = SelectAction.Execute(combo, "Nonexistent", itemIndex: null);
            Assert.NotNull(result.Error);
            Assert.Contains("not found", result.Error);
        });
    }

    [Fact]
    public void Select_ByIndex_Succeeds()
    {
        StaThread.Run(() =>
        {
            var combo = new ComboBox();
            combo.Items.Add("Alpha");
            combo.Items.Add("Bravo");

            var result = SelectAction.Execute(combo, itemText: null, itemIndex: 1);
            Assert.Null(result.Error);
            Assert.Equal(1, combo.SelectedIndex);
        });
    }

    [Fact]
    public void Toggle_OnAToggleButton_Succeeds()
    {
        StaThread.Run(() =>
        {
            var check = new ToggleButton { IsChecked = false };
            var result = ToggleAction.Execute(check);
            Assert.Null(result.Error);
            Assert.Equal(true, check.IsChecked);
        });
    }

    [Fact]
    public void Toggle_OnSomethingUntoggleable_FailsWithoutThrowing()
    {
        StaThread.Run(() =>
        {
            var result = ToggleAction.Execute(new Border());
            Assert.NotNull(result.Error);
            Assert.Contains("does not support toggle", result.Error);
        });
    }

    [Fact]
    public void Expand_OnSomethingThatDoesNotExpand_FailsWithoutThrowing()
    {
        StaThread.Run(() =>
        {
            var result = ExpandAction.Execute(new Border(), expand: true);
            Assert.NotNull(result.Error);
            Assert.Contains("does not support expand/collapse", result.Error);
        });
    }

    [Fact]
    public void Scroll_OnSomethingThatDoesNotScroll_FailsWithoutThrowing()
    {
        StaThread.Run(() =>
        {
            var result = ScrollAction.Execute(new Border(), horizontalPercent: -1, verticalPercent: 50);
            Assert.NotNull(result.Error);
            Assert.Contains("does not support scrolling", result.Error);
        });
    }
}

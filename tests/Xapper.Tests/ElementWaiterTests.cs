using Xapper.Inspector.AutoWait;

namespace Xapper.Tests;

/// <summary>
/// 요소가 준비되기를 기다리는 규칙과, 기다림이 실패했을 때 무엇이 막고 있었다고 알리는지를 검증하는 테스트 클래스.
/// </summary>
public class ElementWaiterTests
{
    #region 조기 중단 규칙

    [Fact]
    public void ShouldStopEarly_IsFalse_WhileTheGraceHasNotPassed()
    {
        var disabled = new ElementReadiness(Visible: true, Enabled: false, Loaded: true);

        Assert.False(ElementWaiter.ShouldStopEarly(disabled, TimeSpan.FromMilliseconds(100)));
    }

    [Fact]
    public void ShouldStopEarly_IsTrue_OnceTheGraceHasPassed()
    {
        // 보이고 로드도 끝났는데 활성만 아니면 앱이 그 동작을 거부하고 있는 것이므로, 남은 제한 시간을
        // 마저 쓰는 일은 기다림이 아니라 낭비다(실측: 비활성 버튼 세 번에 15초를 버렸다).
        var disabled = new ElementReadiness(Visible: true, Enabled: false, Loaded: true);

        Assert.True(ElementWaiter.ShouldStopEarly(disabled, TimeSpan.FromMilliseconds(600)));
    }

    [Fact]
    public void ShouldStopEarly_IsFalse_ForSomethingStillHidden()
    {
        // 화면이 아직 안 보이는 상황은 실제로 몇 초가 걸릴 수 있으므로 끝까지 기다려야 한다.
        var hidden = new ElementReadiness(Visible: false, Enabled: true, Loaded: true);

        Assert.False(ElementWaiter.ShouldStopEarly(hidden, TimeSpan.FromSeconds(3)));
    }

    [Fact]
    public void ShouldStopEarly_IsFalse_ForSomethingStillLoading()
    {
        var loading = new ElementReadiness(Visible: true, Enabled: true, Loaded: false);

        Assert.False(ElementWaiter.ShouldStopEarly(loading, TimeSpan.FromSeconds(3)));
    }

    #endregion

    #region 막고 있던 이유

    [Fact]
    public void Obstacle_NamesTheDisabledState()
    {
        var disabled = new ElementReadiness(Visible: true, Enabled: false, Loaded: true);

        Assert.Equal("stayed disabled", disabled.Obstacle);
    }

    [Fact]
    public void Obstacle_PrefersHidden_WhenTheElementIsBothHiddenAndDisabled()
    {
        // 보이지 않는 요소의 활성 여부를 알려 주어도 부르는 쪽이 할 수 있는 일이 없다.
        // 먼저 화면에 띄우는 것이 다음 할 일이므로 그쪽을 짚는다.
        var both = new ElementReadiness(Visible: false, Enabled: false, Loaded: true);

        Assert.Equal("stayed hidden", both.Obstacle);
    }

    [Fact]
    public void Obstacle_NamesLoading_BeforeDisabled()
    {
        var loading = new ElementReadiness(Visible: true, Enabled: false, Loaded: false);

        Assert.Equal("was still loading", loading.Obstacle);
    }

    [Fact]
    public void IsReady_RequiresAllThree()
    {
        Assert.True(new ElementReadiness(true, true, true).IsReady);
        Assert.False(new ElementReadiness(true, true, false).IsReady);
        Assert.False(new ElementReadiness(true, false, true).IsReady);
        Assert.False(new ElementReadiness(false, true, true).IsReady);
    }

    #endregion
}

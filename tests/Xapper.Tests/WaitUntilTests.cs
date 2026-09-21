using Xapper.Inspector.AutoWait;

namespace Xapper.Tests;

/// <summary>
/// 조건이 참이 될 때까지 기다리는 헬퍼를 검증한다. 앱이 아직 기동 중이라 WPF Application 이 없는 구간을
/// 넘기는 데 쓰므로, 늦게 참이 되는 경우와 끝내 안 되는 경우가 모두 정확해야 한다.
/// </summary>
public class WaitUntilTests
{
    [Fact]
    public async Task ReadyAsync_WhenTheConditionAlreadyHolds_ReturnsAtOnce()
    {
        var ready = await WaitUntil.ReadyAsync(() => true, TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(10));

        Assert.True(ready);
    }

    [Fact]
    public async Task ReadyAsync_WhenTheConditionBecomesTrue_ReturnsTrue()
    {
        var deadline = Environment.TickCount64 + 150;

        var ready = await WaitUntil.ReadyAsync(
            () => Environment.TickCount64 >= deadline, TimeSpan.FromSeconds(5), TimeSpan.FromMilliseconds(10));

        Assert.True(ready);
    }

    [Fact]
    public async Task ReadyAsync_WhenTheConditionNeverHolds_GivesUp()
    {
        var ready = await WaitUntil.ReadyAsync(
            () => false, TimeSpan.FromMilliseconds(150), TimeSpan.FromMilliseconds(10));

        Assert.False(ready);
    }
}

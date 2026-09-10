using System.Windows.Threading;
using Xapper.Inspector.AutoWait;

namespace Xapper.Tests;

/// <summary>
/// 조작이 처리될 때까지 기다리는 장치가 실제로 기다리는지, 그리고 기다릴 수 없을 때 어떻게 물러나는지
/// 검증하는 테스트 클래스. 무제한으로 기다리면 세션 전체가 멈추고, 앱이 닫히는 조작에서 실패로 보고하면
/// 성공한 클릭이 오류가 된다.
/// </summary>
public class DispatcherDrainTests
{
    #region Helpers

    /// <summary>메시지 루프가 도는 STA 디스패처를 띄웁니다.</summary>
    private static Dispatcher StartDispatcher()
    {
        var ready = new TaskCompletionSource<Dispatcher>();
        var thread = new Thread(() =>
        {
            ready.SetResult(Dispatcher.CurrentDispatcher);
            Dispatcher.Run();
        })
        {
            IsBackground = true
        };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return ready.Task.GetAwaiter().GetResult();
    }

    #endregion

    [Fact]
    public async Task WaitAsync_ReturnsOnlyAfterQueuedWorkHasRun()
    {
        var dispatcher = StartDispatcher();
        try
        {
            var order = new List<string>();
            // 일부러 기다리지 않는다. 이 작업이 대기 중인 상태에서 WaitAsync 를 부르는 것이 시나리오다.
            _ = dispatcher.InvokeAsync(() => order.Add("queued"), DispatcherPriority.Input);

            var settled = await DispatcherDrain.WaitAsync(dispatcher, TimeSpan.FromSeconds(5));

            Assert.True(settled);
            Assert.Equal(["queued"], order);
        }
        finally
        {
            dispatcher.InvokeShutdown();
        }
    }

    [Fact]
    public async Task WaitAsync_GivesUpInsteadOfBlockingForeverOnABusyApplication()
    {
        var dispatcher = StartDispatcher();
        using var busy = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        try
        {
            // 디스패처를 붙들어 두는 작업. 완료를 기다리면 시나리오가 성립하지 않는다.
            _ = dispatcher.InvokeAsync(() =>
            {
                busy.Set();
                release.Wait(TimeSpan.FromSeconds(10));
            });
            busy.Wait(TimeSpan.FromSeconds(5));

            var settled = await DispatcherDrain.WaitAsync(dispatcher, TimeSpan.FromMilliseconds(200));

            Assert.False(settled);
        }
        finally
        {
            release.Set();
            dispatcher.InvokeShutdown();
        }
    }

    [Fact]
    public async Task WaitAsync_TreatsAShutDownApplicationAsNothingLeftToWaitFor()
    {
        var dispatcher = StartDispatcher();
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        dispatcher.ShutdownFinished += (_, _) => finished.TrySetResult();

        dispatcher.InvokeShutdown();
        await finished.Task.WaitAsync(TimeSpan.FromSeconds(5));

        var settled = await DispatcherDrain.WaitAsync(dispatcher, TimeSpan.FromSeconds(5));

        Assert.True(settled);
    }
}

using System.Diagnostics;
using System.Windows.Threading;
using Xapper.Inspector.AutoWait;

namespace Xapper.Tests;

/// <summary>
/// UI 스레드 작업이 모달 대화상자처럼 중첩 메시지 루프에 갇혔을 때 제한 시간에 돌아오는지 검증한다.
/// MessageBox 대신 <see cref="DispatcherFrame"/> 을 밀어 넣어 같은 상황(핸들러가 반환하지 않고 루프를 펌프)을 만든다.
/// </summary>
public class DispatcherCallTests
{
    [Fact]
    public async Task RunAsync_ReturnsTheResultWhenTheActionFinishesInTime()
    {
        var (dispatcher, shutdown) = StartDispatcherThread();
        try
        {
            var (completed, result) = await DispatcherCall.RunAsync(dispatcher, () => 42, TimeSpan.FromSeconds(5));

            Assert.True(completed);
            Assert.Equal(42, result);
        }
        finally
        {
            shutdown();
        }
    }

    [Fact]
    public async Task RunAsync_GivesUpWhenTheActionSitsInANestedMessageLoop()
    {
        var (dispatcher, shutdown) = StartDispatcherThread();
        DispatcherFrame? frame = null;
        try
        {
            var watch = Stopwatch.StartNew();
            var (completed, _) = await DispatcherCall.RunAsync(dispatcher, () =>
            {
                frame = new DispatcherFrame(); // 디스패처 스레드에서 만들어야 한다.
                Dispatcher.PushFrame(frame);   // 모달 대화상자처럼 여기서 반환하지 않고 펌프한다.
                return 1;
            }, TimeSpan.FromMilliseconds(300));
            watch.Stop();

            Assert.False(completed);
            Assert.True(watch.ElapsedMilliseconds < 3000, $"took {watch.ElapsedMilliseconds} ms");

            // 갇힌 동안에도 디스패처는 펌프되므로 다른 작업은 처리된다(대화상자가 떠 있어도 조회가 되는 이유).
            var (otherDone, other) = await DispatcherCall.RunAsync(dispatcher, () => 7, TimeSpan.FromSeconds(5));
            Assert.True(otherDone);
            Assert.Equal(7, other);
        }
        finally
        {
            // 갇힌 루프 안에서 실행되도록 디스패처에 맡겨 프레임을 끝낸다.
            await dispatcher.InvokeAsync(() => { if (frame is not null) frame.Continue = false; });
            shutdown();
        }
    }

    #region Helpers

    /// <summary>디스패처를 돌리는 STA 스레드를 세우고, 그 디스패처와 종료 함수를 돌려줍니다.</summary>
    private static (Dispatcher Dispatcher, Action Shutdown) StartDispatcherThread()
    {
        var ready = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            ready.SetResult(Dispatcher.CurrentDispatcher);
            Dispatcher.Run();
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();

        var dispatcher = ready.Task.GetAwaiter().GetResult();
        return (dispatcher, () => dispatcher.InvokeShutdown());
    }

    #endregion
}

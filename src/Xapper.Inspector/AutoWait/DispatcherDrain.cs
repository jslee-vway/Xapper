using System.Windows.Threading;

namespace Xapper.Inspector.AutoWait;

/// <summary>
/// UI 스레드 큐에 걸린 작업이 처리될 때까지 기다리는 헬퍼.
/// 접근성 경로는 조작을 큐에 걸어 두고 곧바로 돌아오므로, 그대로 응답하면 호출자의 다음 호출이
/// 조작 이전 상태를 보게 된다.
/// </summary>
internal static class DispatcherDrain
{
    #region Public Methods

    /// <summary>
    /// 지정된 디스패처의 큐가 비워질 때까지 기다립니다.
    /// </summary>
    /// <param name="dispatcher">기다릴 대상 디스패처.</param>
    /// <param name="timeout">기다릴 최대 시간. 조작 하나가 오래 걸려도 세션 전체가 멈추지 않게 한다.</param>
    /// <returns>제한 시간 안에 비워졌거나 기다릴 대상이 사라졌으면 true, 아직 처리 중이면 false.</returns>
    public static async Task<bool> WaitAsync(Dispatcher dispatcher, TimeSpan timeout)
    {
        Task drained;
        try
        {
            // Background는 접근성 경로가 조작을 걸어 두는 Input보다 낮다.
            // 이 빈 작업이 실행됐다는 것은 그보다 높은 우선순위의 작업이 모두 끝났다는 뜻이다.
            drained = dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background).Task;
        }
        catch (Exception ex) when (IsShuttingDown(dispatcher, ex))
        {
            return true;
        }

        if (await Task.WhenAny(drained, Task.Delay(timeout)) != drained)
            return false;

        try
        {
            await drained;
            return true;
        }
        catch (Exception ex) when (IsShuttingDown(dispatcher, ex))
        {
            return true;
        }
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// 디스패처가 내려가는 중이라 기다릴 대상이 남지 않은 경우인지 판별합니다.
    /// 창을 닫는 조작에서는 정상적으로 일어나는 일이므로 실패로 보고하지 않는다.
    /// 종료와 무관한 오류까지 삼키지 않도록 예외 형식만으로 판단하지 않고 디스패처 상태를 함께 확인한다.
    /// </summary>
    private static bool IsShuttingDown(Dispatcher dispatcher, Exception exception)
    {
        return exception is OperationCanceledException or InvalidOperationException
            && (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished);
    }

    #endregion
}

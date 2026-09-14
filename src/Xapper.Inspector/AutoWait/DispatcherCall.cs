using System.Windows.Threading;

namespace Xapper.Inspector.AutoWait;

/// <summary>
/// 앱의 핸들러를 실행하는 UI 스레드 작업을 제한 시간까지만 기다리는 헬퍼.
/// 핸들러가 MessageBox 같은 모달 대화상자를 열면 UI 스레드가 그 안의 중첩 메시지 루프에 머물러 작업이 끝나지
/// 않는다 — 그때까지 응답을 미루면 사람이 대화상자를 닫을 때까지 호출자가 매달린다. 조작은 이미 전달됐으므로
/// 기다리다 말고 "처리 중" 으로 알린다. 작업 자체는 취소하지 않는다(핸들러는 나중에 끝나고 결과는 버려진다).
/// </summary>
internal static class DispatcherCall
{
    #region Public Methods

    /// <summary>
    /// UI 스레드에서 <paramref name="action"/> 을 실행하고 제한 시간까지 기다립니다.
    /// </summary>
    /// <param name="dispatcher">실행할 디스패처.</param>
    /// <param name="action">실행할 작업.</param>
    /// <param name="timeout">기다릴 최대 시간.</param>
    /// <returns>제한 시간 안에 끝났으면 (true, 결과), 아니면 (false, 기본값).</returns>
    public static async Task<(bool Completed, T? Result)> RunAsync<T>(Dispatcher dispatcher, Func<T> action, TimeSpan timeout)
    {
        var operation = dispatcher.InvokeAsync(action);
        if (await Task.WhenAny(operation.Task, Task.Delay(timeout)) != operation.Task)
        {
            // 나중에 핸들러가 던지면 아무도 기다리지 않는 Task 가 실패 상태로 남는다. 관측해 두어 미관측 예외 이벤트가
            // 대상 앱에 올라가지 않게 한다(그 이벤트로 프로세스를 죽이는 호스트가 있다).
            _ = operation.Task.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted);
            return (false, default);
        }

        return (true, await operation.Task);
    }

    #endregion
}

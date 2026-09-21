namespace Xapper.Inspector.AutoWait;

/// <summary>
/// 조건이 참이 될 때까지 짧은 간격으로 다시 물으며 기다리는 헬퍼.
/// 런치 경로에서는 앱의 진입점보다 먼저 들어가므로, WPF Application 이 아직 없는 구간을 이걸로 넘긴다.
/// </summary>
internal static class WaitUntil
{
    #region Public Methods

    /// <summary>
    /// 조건이 참이 될 때까지 기다립니다. 제한 시간 안에 참이 되면 true, 끝내 아니면 false — 예외는 던지지 않는다.
    /// </summary>
    /// <param name="ready">참이 되기를 기다릴 조건.</param>
    /// <param name="timeout">기다릴 최대 시간.</param>
    /// <param name="poll">조건을 다시 묻는 간격.</param>
    public static async Task<bool> ReadyAsync(Func<bool> ready, TimeSpan timeout, TimeSpan poll)
    {
        var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        while (true)
        {
            if (ready())
                return true;
            if (Environment.TickCount64 >= deadline)
                return false;

            await Task.Delay(poll);
        }
    }

    #endregion
}

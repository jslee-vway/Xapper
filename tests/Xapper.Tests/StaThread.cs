namespace Xapper.Tests;

/// <summary>
/// WPF 요소를 다루는 테스트 본문을 STA 스레드에서 실행하는 헬퍼.
/// xUnit은 테스트를 STA가 아닌 스레드에서 돌리므로, 비주얼 트리를 만들거나 렌더링하려면 직접 스레드를 세워야 한다.
/// </summary>
internal static class StaThread
{
    /// <summary>
    /// STA 스레드에서 본문을 실행하고, 그 안에서 난 실패를 호출한 테스트로 전달합니다.
    /// </summary>
    /// <param name="body">STA 스레드에서 실행할 본문.</param>
    public static void Run(Action body)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try { body(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        if (failure is not null)
            throw new Xunit.Sdk.XunitException($"STA 스레드에서 실패: {failure}");
    }
}

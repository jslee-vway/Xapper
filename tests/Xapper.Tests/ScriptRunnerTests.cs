using Xapper.McpServer.Scripting;
using Xapper.Protocol;

namespace Xapper.Tests;

/// <summary>
/// 러너가 스크립트를 실행하고, 문법 오류·타임아웃·무한 루프·반환값·로그 상한을 올바른 결과로 만드는지 검증한다.
/// 전송은 항상 성공 응답을 주는 가짜를 쓴다.
/// </summary>
public class ScriptRunnerTests
{
    private static ScriptRunner NewRunner() => new(
        send: (_, _) => Task.FromResult(Ok()),
        processId: 1,
        notice: null);

    private static IpcMessage Ok() => new()
    {
        Id = "1", Type = "response", Method = "x",
        Payload = System.Text.Json.JsonSerializer.SerializeToElement(new { success = true, message = "done" })
    };

    [Fact]
    public async Task Run_ReturnsTheReturnValueAsJson()
    {
        var result = await NewRunner().RunAsync("return { rows: 12, ok: true };", timeoutMs: 5000, CancellationToken.None);

        Assert.Equal(ScriptOutcome.Ok, result.Outcome);
        Assert.Contains("\"rows\":12", result.ReturnValueJson);
    }

    [Fact]
    public async Task Run_SyntaxError_ReportsTheLine()
    {
        var result = await NewRunner().RunAsync("var = ;", timeoutMs: 5000, CancellationToken.None);

        Assert.Equal(ScriptOutcome.SyntaxError, result.Outcome);
        Assert.True(result.FailureLine > 0);
    }

    [Fact]
    public async Task Run_InfiniteLoop_TimesOut()
    {
        var result = await NewRunner().RunAsync("while (true) {}", timeoutMs: 200, CancellationToken.None);

        Assert.Equal(ScriptOutcome.Timeout, result.Outcome);
    }

    [Fact]
    public async Task Run_Fail_ProducesFailOutcomeWithLine()
    {
        var result = await NewRunner().RunAsync("log('a');\nfail('boom');", timeoutMs: 5000, CancellationToken.None);

        Assert.Equal(ScriptOutcome.Fail, result.Outcome);
        Assert.Equal("fail", result.FailureApi);
        Assert.Contains("boom", result.FailureMessage);
        Assert.Contains("a", result.Logs);
    }

    [Fact]
    public async Task Run_LogIsCappedAt200Lines()
    {
        var result = await NewRunner().RunAsync("for (var i = 0; i < 500; i++) log('x');", timeoutMs: 5000, CancellationToken.None);

        Assert.Equal(ScriptOutcome.Ok, result.Outcome);
        Assert.True(result.Logs.Count <= 200);
    }
}

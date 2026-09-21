using System.Diagnostics;
using System.IO.Pipes;
using Xapper.Inspector;
using Xapper.Protocol;

namespace Xapper.Tests;

/// <summary>
/// <see cref="IpcServer"/>가 <c>applicationWait</c>를 받았을 때만 WPF Application 을 기다리고,
/// 받지 않았을 때(주입 경로의 기본값)는 곧바로 지나가는지 검증한다.
/// </summary>
public class ApplicationWaitGateTests
{
    [Fact]
    public async Task ProcessMessage_WithApplicationWait_WaitsThenReturnsTheGateError()
    {
        // WPF Application 이 없는 이 테스트 프로세스에서 짧은 대기를 주면, 시간이 다 찼을 때
        // 게이트가 자체 오류 메시지를 돌려주는지 본다.
        var pipeName = IpcPipeNames.ForProcess(FakeInspector.NextProcessId());
        var server = new IpcServer(pipeName, applicationWait: TimeSpan.FromMilliseconds(150));
        _ = Task.Run(server.StartListening);

        try
        {
            await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await client.ConnectAsync(5_000);

            var request = IpcSerializer.CreateRequest("find", new { });
            await client.WriteAsync(IpcSerializer.Serialize(request));

            var response = await IpcSerializer.DeserializeAsync(client);
            if (response is null)
            {
                Assert.Fail("expected a response");
                return;
            }
            Assert.Equal("error", response.Type);
            Assert.Contains("WPF Application", response.Payload?.GetRawText() ?? "");
        }
        finally
        {
            server.Stop();
        }
    }

    [Fact]
    public async Task ProcessMessage_WithoutApplicationWait_SkipsTheGate()
    {
        // applicationWait 를 주지 않은 것은 주입 경로를 흉내 낸 것 — 이미 떠 있는 앱에 들어간다는 뜻이라
        // Application 이 없어도 게이트가 기다리지 않고 곧바로 핸들러로 넘겨야 한다.
        var wait = TimeSpan.FromMilliseconds(150);
        var pipeName = IpcPipeNames.ForProcess(FakeInspector.NextProcessId());
        var server = new IpcServer(pipeName);
        _ = Task.Run(server.StartListening);

        try
        {
            await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await client.ConnectAsync(5_000);

            var request = IpcSerializer.CreateRequest("find", new { });
            var stopwatch = Stopwatch.StartNew();
            await client.WriteAsync(IpcSerializer.Serialize(request));

            var response = await IpcSerializer.DeserializeAsync(client);
            stopwatch.Stop();

            if (response is null)
            {
                Assert.Fail("expected a response");
                return;
            }
            Assert.True(stopwatch.Elapsed < wait, $"expected the gate to be skipped, but the response took {stopwatch.Elapsed}");
            Assert.DoesNotContain("WPF Application", response.Payload?.GetRawText() ?? "");
        }
        finally
        {
            server.Stop();
        }
    }
}

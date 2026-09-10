using System.IO.Pipes;
using Xapper.Inspector;
using Xapper.McpServer.Ipc;
using Xapper.Protocol;

namespace Xapper.Tests;

/// <summary>
/// 프레임이 어긋난 요청을 받았을 때 IPC 양쪽이 어떻게 회복하는지 검증하는 테스트 클래스.
/// 길이 접두사가 한 번 밀리면 그 연결로는 더 이상 올바른 응답을 읽을 수 없으므로,
/// 서버는 연결 하나만 버리고 계속 살아 있어야 하고 클라이언트는 어긋난 연결을 계속 쓰지 말아야 한다.
/// </summary>
public class IpcResilienceTests
{
    [Fact]
    public async Task Server_SurvivesMalformedFrame_AndServesNextConnection()
    {
        var pipeName = IpcPipeNames.ForProcess(FakeInspector.NextProcessId());
        var dropped = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var server = new IpcServer(pipeName, dropped.Enqueue);
        var listening = Task.Run(server.StartListening);

        try
        {
            // 첫 연결: 프로토콜을 위반한 길이 접두사를 보내고 끊는다.
            await using (var broken = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
            {
                await broken.ConnectAsync(5_000);
                await broken.WriteAsync(BitConverter.GetBytes(0));
            }

            // 두 번째 연결: 서버가 살아 있다면 정상적인 요청에 응답해야 한다.
            await using var healthy = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await healthy.ConnectAsync(5_000);

            var request = IpcSerializer.CreateRequest("ping");
            await healthy.WriteAsync(IpcSerializer.Serialize(request));

            var response = await IpcSerializer.DeserializeAsync(healthy);

            Assert.NotNull(response);
            Assert.Equal(request.Id, response.Id);
            Assert.Equal("response", response.Type);

            // 루프가 살아남는 것만으로는 부족하다. 무슨 일이 있었는지도 남아 있어야 한다.
            Assert.Single(dropped);
            Assert.Contains("Connection dropped", dropped.Single());
        }
        finally
        {
            server.Stop();
            await StopQuietly(listening);
        }
    }

    [Fact]
    public async Task Client_DropsConnection_WhenResponseCannotBeReadToTheEnd()
    {
        var processId = FakeInspector.NextProcessId();
        await using var fakeInspector = FakeInspector.Create(processId);

        var client = new InspectorClient(processId);
        var accepting = fakeInspector.WaitForConnectionAsync();
        await client.ConnectAsync();
        await accepting;

        // 요청은 정상적으로 받되, 응답으로는 해석할 수 없는 길이 접두사를 돌려준다.
        var brokenResponder = Task.Run(async () =>
        {
            await IpcSerializer.DeserializeAsync(fakeInspector);
            await fakeInspector.WriteAsync(BitConverter.GetBytes(0));
        });

        await Assert.ThrowsAsync<InvalidOperationException>(() => client.PingAsync());
        await brokenResponder;

        // 이 단언이 수정 여부를 가른다. 예외 자체는 수정 전에도 났다 — 달라진 것은 연결을 버린다는 점이다.
        Assert.False(client.IsConnected);
    }

    [Fact]
    public async Task Client_DropsConnection_WhenTheCallIsCancelled()
    {
        var processId = FakeInspector.NextProcessId();
        await using var fakeInspector = FakeInspector.Create(processId);

        var client = new InspectorClient(processId);
        var accepting = fakeInspector.WaitForConnectionAsync();
        await client.ConnectAsync();
        await accepting;

        using var cancellation = new CancellationTokenSource();

        // 요청은 받되 응답 헤더를 절반만 보낸다. 취소는 대개 이 대기 중에 걸리지만,
        // 쓰기나 잠금 대기 단계에서 걸려도 결과는 같아야 한다 — 어느 쪽이든 연결을 버려야 한다.
        var halfResponder = Task.Run(async () =>
        {
            await IpcSerializer.DeserializeAsync(fakeInspector);
            await fakeInspector.WriteAsync(new byte[] { 0x10, 0x00 });
            cancellation.Cancel();
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.PingAsync(cancellation.Token));
        await halfResponder;

        // 읽다 만 바이트가 파이프에 남았으므로 이 연결로는 다음 응답을 해석할 수 없다.
        Assert.False(client.IsConnected);
    }

    #region Private Methods

    /// <summary>
    /// 수신 루프를 정리합니다. 중지 요청은 연결 대기 중인 호출을 취소시키므로 그 취소만 삼키고,
    /// 다른 이유로 죽은 루프는 그대로 드러나게 둡니다.
    /// </summary>
    private static async Task StopQuietly(Task listening)
    {
        try
        {
            await listening.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (OperationCanceledException)
        {
        }
        catch (TimeoutException)
        {
        }
    }

    #endregion
}

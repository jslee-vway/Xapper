using System.IO;
using System.IO.Pipes;
using System.Runtime.ExceptionServices;
using Xapper.Inspector;
using Xapper.Protocol;

namespace Xapper.Tests;

/// <summary>
/// 주입된 Inspector 가 정상 제어 흐름(연결 종료, 오래된 ref)에서 예외를 던지지 않는지 검증하는 테스트 클래스.
///
/// 근거(결함 2): 어떤 대상 앱은 <see cref="AppDomain.FirstChanceException"/> 에 Debug.Assert 를 걸어 두어,
/// 던져진 예외가 누구에게 잡히기도 전에 프로세스를 FailFast 시킨다. Xapper 가 그 예외를 스스로 잡아
/// 정상 처리하더라도, first-chance 는 catch 보다 먼저 발동하므로 대상 앱이 죽는다. 따라서 주입된 코드는
/// 정상 경로에서 아예 던지지 않아야 한다. 여기서는 예외를 삼키는 대상을 흉내 내(FailFast 대신 기록만)
/// 그 경로들이 first-chance 를 한 번도 건드리지 않는지 단언한다.
/// </summary>
public class FirstChanceSafetyTests
{
    #region First-chance watcher

    /// <summary>
    /// 스코프가 열려 있는 동안 발생하는 first-chance 예외 중, 메시지에 주어진 표식이 든 것만 모은다.
    /// first-chance 는 AppDomain 전역이라 병렬 테스트나 취소 예외까지 보이므로, 검증하려는 시나리오에
    /// 고유한 표식(예: 그 시나리오에서만 쓰는 ref 번호)으로 걸러 오탐을 없앤다.
    /// </summary>
    private sealed class ThrowWatcher : IDisposable
    {
        private readonly string _marker;
        private readonly List<string> _throws = new();
        private readonly object _gate = new();

        public ThrowWatcher(string marker)
        {
            _marker = marker;
            AppDomain.CurrentDomain.FirstChanceException += OnFirstChance;
        }

        private void OnFirstChance(object? sender, FirstChanceExceptionEventArgs e)
        {
            if (e.Exception.Message.Contains(_marker, StringComparison.Ordinal))
                lock (_gate) _throws.Add($"{e.Exception.GetType().Name}: {e.Exception.Message}");
        }

        public IReadOnlyList<string> Throws
        {
            get { lock (_gate) return _throws.ToArray(); }
        }

        public void Dispose() => AppDomain.CurrentDomain.FirstChanceException -= OnFirstChance;
    }

    #endregion

    #region DeserializeAsync — no throw on end-of-stream or bad length

    [Fact]
    public async Task DeserializeAsync_ReturnsNull_OnImmediateEndOfStream()
    {
        using var stream = new MemoryStream(Array.Empty<byte>());
        Assert.Null(await IpcSerializer.DeserializeAsync(stream));
    }

    [Fact]
    public async Task DeserializeAsync_ReturnsNull_OnTruncatedLengthPrefix()
    {
        using var stream = new MemoryStream(new byte[] { 1, 0 }); // 4바이트 길이 접두사가 덜 왔다
        Assert.Null(await IpcSerializer.DeserializeAsync(stream));
    }

    [Fact]
    public async Task DeserializeAsync_ReturnsNull_OnTruncatedPayload()
    {
        var frame = new List<byte>();
        frame.AddRange(BitConverter.GetBytes(100)); // 100바이트라고 해놓고
        frame.AddRange(new byte[10]);               // 10바이트만 준다
        using var stream = new MemoryStream(frame.ToArray());
        Assert.Null(await IpcSerializer.DeserializeAsync(stream));
    }

    [Fact]
    public async Task DeserializeAsync_ReturnsNull_OnNonPositiveLength()
    {
        using var stream = new MemoryStream(BitConverter.GetBytes(0));
        Assert.Null(await IpcSerializer.DeserializeAsync(stream));
    }

    [Fact]
    public async Task DeserializeAsync_ReturnsNull_OnOversizeLength()
    {
        using var stream = new MemoryStream(BitConverter.GetBytes(IpcSerializer.MaxPayloadBytes + 1));
        Assert.Null(await IpcSerializer.DeserializeAsync(stream));
    }

    #endregion

    #region End-to-end — the injected server does not throw on disconnect or a stale ref

    [Fact]
    public async Task Server_DoesNotTakeTheCaughtExceptionPath_WhenClientDisconnects()
    {
        // 예전에는 정상 연결 종료가 EndOfStreamException 을 던졌고, 서버는 그것을 잡아 "Connection dropped"
        // 로 로그에 남겼다 — 던졌다는 뜻이고, 대상 앱의 first-chance 핸들러엔 그걸로 충분히 치명적이다.
        // 이제는 던지지 않으므로 그 로그가 남지 않는다. 로그 콜백을 신호로 삼으면 전역 first-chance 에
        // 기대지 않고 결정적으로 검증할 수 있다.
        var logged = new System.Collections.Concurrent.ConcurrentQueue<string>();
        var pipeName = IpcPipeNames.ForProcess(FakeInspector.NextProcessId());
        var server = new IpcServer(pipeName, logged.Enqueue);
        _ = Task.Run(server.StartListening);

        try
        {
            // 프레임을 온전히 보내지 않고 그냥 끊는다 — MCP 서버 종료·클라이언트 취소의 일상 경로.
            await using (var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous))
                await client.ConnectAsync(5_000);

            // 서버가 그 연결을 정리하고 여전히 살아 다음 요청에 답하는지로 처리 완료를 확정한다.
            await using var healthy = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await healthy.ConnectAsync(5_000);
            await healthy.WriteAsync(IpcSerializer.Serialize(IpcSerializer.CreateRequest("ping")));
            var pong = await IpcSerializer.DeserializeAsync(healthy);
            Assert.NotNull(pong);

            Assert.Empty(logged);
        }
        finally
        {
            server.Stop();
        }
    }

    [Fact]
    public async Task Server_ReturnsError_WithoutThrowing_OnStaleRef()
    {
        // 시나리오 고유의 ref 번호를 표식으로 삼아, 이 번호가 든 예외가 던져졌는지만 본다.
        // 예전에는 ResolveRef 가 "Element ref=999919 cannot be resolved" 를 던져 대상 앱을 죽였다.
        using var watcher = new ThrowWatcher("999919");
        var pipeName = IpcPipeNames.ForProcess(FakeInspector.NextProcessId());
        var server = new IpcServer(pipeName);
        _ = Task.Run(server.StartListening);

        try
        {
            await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await client.ConnectAsync(5_000);

            var request = IpcSerializer.CreateRequest("click", new { @ref = 999919, timeout = 1000 });
            await client.WriteAsync(IpcSerializer.Serialize(request));

            var response = await IpcSerializer.DeserializeAsync(client);
            if (response is null)
            {
                Assert.Fail("expected a response");
                return;
            }
            Assert.Equal("error", response.Type);
            Assert.Contains("999919", response.Payload?.GetRawText() ?? "");

            // 오류 응답은 왔고(정상 처리), 그 과정에서 던져진 예외는 없어야 한다.
            Assert.Empty(watcher.Throws);
        }
        finally
        {
            server.Stop();
        }
    }

    [Fact]
    public async Task Server_ReturnsError_WithoutThrowing_OnDragWithNoCoordinates()
    {
        // ref 도 좌표도 없는 드래그. 예전에는 ResolveStartPoint 가 UI 스레드 람다 안에서 던졌다.
        // 이제는 람다 진입 전에 오류로 돌려주므로 대상 앱을 죽이지 않는다.
        using var watcher = new ThrowWatcher("Drag requires sourceRef");
        var pipeName = IpcPipeNames.ForProcess(FakeInspector.NextProcessId());
        var server = new IpcServer(pipeName);
        _ = Task.Run(server.StartListening);

        try
        {
            await using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
            await client.ConnectAsync(5_000);

            var request = IpcSerializer.CreateRequest("drag", new { timeout = 1000 });
            await client.WriteAsync(IpcSerializer.Serialize(request));

            var response = await IpcSerializer.DeserializeAsync(client);
            if (response is null)
            {
                Assert.Fail("expected a response");
                return;
            }
            Assert.Equal("error", response.Type);
            Assert.Contains("Drag requires sourceRef", response.Payload?.GetRawText() ?? "");
            Assert.Empty(watcher.Throws);
        }
        finally
        {
            server.Stop();
        }
    }

    #endregion
}

using System.IO.Pipes;
using Xapper.Protocol;

namespace Xapper.McpServer.Ipc;

/// <summary>
/// MCP 서버에서 주입된 Inspector의 Named Pipe 서버에 연결하는 IPC 클라이언트.
/// SemaphoreSlim으로 요청을 직렬화하여 동시 접근을 방지.
/// </summary>
public sealed class InspectorClient : IAsyncDisposable
{
    #region Fields

    private readonly string _pipeName;
    private NamedPipeClientStream? _pipe;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);

    #endregion

    #region Constructor

    /// <summary>
    /// <see cref="InspectorClient"/>의 새 인스턴스를 생성합니다.
    /// </summary>
    /// <param name="processId">연결 대상 프로세스의 PID.</param>
    public InspectorClient(int processId)
    {
        _pipeName = IpcPipeNames.ForProcess(processId);
    }

    #endregion

    #region Connection

    /// <summary>
    /// Inspector의 Named Pipe 서버에 비동기로 연결합니다.
    /// </summary>
    /// <param name="ct">취소 토큰.</param>
    public async Task ConnectAsync(CancellationToken ct = default)
    {
        _pipe = new NamedPipeClientStream(".", _pipeName, PipeDirection.InOut, PipeOptions.Asynchronous);
        await _pipe.ConnectAsync((int)ConnectTimeout.TotalMilliseconds, ct);
    }

    /// <summary>Named Pipe 연결 상태.</summary>
    public bool IsConnected => _pipe?.IsConnected ?? false;

    /// <summary>
    /// IPC 요청을 전송하고 응답을 수신합니다. SemaphoreSlim으로 동시 접근을 직렬화.
    /// </summary>
    /// <param name="request">전송할 IPC 요청 메시지.</param>
    /// <param name="ct">취소 토큰.</param>
    /// <returns>Inspector로부터 수신한 응답 메시지.</returns>
    public async Task<IpcMessage> SendAsync(IpcMessage request, CancellationToken ct = default)
    {
        if (_pipe == null || !_pipe.IsConnected)
            throw new InvalidOperationException("Not connected to inspector");

        await _sendLock.WaitAsync(ct);
        try
        {
            var bytes = IpcSerializer.Serialize(request);
            await _pipe.WriteAsync(bytes, ct);
            await _pipe.FlushAsync(ct);

            var response = await IpcSerializer.DeserializeAsync(_pipe, ct);
            return response ?? throw new InvalidOperationException("Received null response");
        }
        finally
        {
            _sendLock.Release();
        }
    }

    #endregion

    #region IPC Methods

    /// <summary>Inspector 연결 상태를 확인합니다.</summary>
    public async Task<IpcMessage> PingAsync(CancellationToken ct = default)
    {
        var request = IpcSerializer.CreateRequest("ping");
        return await SendAsync(request, ct);
    }

    /// <summary>비주얼 트리 스냅샷을 요청합니다.</summary>
    public async Task<IpcMessage> SnapshotAsync(int? rootRef = null, int maxDepth = 5, CancellationToken ct = default)
    {
        var payload = new { rootRef, maxDepth };
        var request = IpcSerializer.CreateRequest("snapshot", payload);
        return await SendAsync(request, ct);
    }

    /// <summary>UI 요소를 클릭합니다.</summary>
    public async Task<IpcMessage> ClickAsync(int @ref, int timeout = 5000, double? x = null, double? y = null, CancellationToken ct = default)
    {
        var payload = new { @ref, timeout, x, y };
        var request = IpcSerializer.CreateRequest("click", payload);
        return await SendAsync(request, ct);
    }

    /// <summary>UI 요소에 텍스트를 입력합니다.</summary>
    public async Task<IpcMessage> TypeAsync(int @ref, string text, bool clear = true, int timeout = 5000, CancellationToken ct = default)
    {
        var payload = new { @ref, text, clear, timeout };
        var request = IpcSerializer.CreateRequest("type", payload);
        return await SendAsync(request, ct);
    }

    /// <summary>Selector 컨트롤에서 항목을 선택합니다.</summary>
    public async Task<IpcMessage> SelectAsync(int @ref, string? itemText = null, int? itemIndex = null, int timeout = 5000, CancellationToken ct = default)
    {
        var payload = new { @ref, itemText, itemIndex, timeout };
        var request = IpcSerializer.CreateRequest("select", payload);
        return await SendAsync(request, ct);
    }

    /// <summary>토글 상태를 전환합니다.</summary>
    public async Task<IpcMessage> ToggleAsync(int @ref, int timeout = 5000, CancellationToken ct = default)
    {
        var payload = new { @ref, timeout };
        var request = IpcSerializer.CreateRequest("toggle", payload);
        return await SendAsync(request, ct);
    }

    /// <summary>TreeViewItem/Expander를 확장 또는 축소합니다.</summary>
    public async Task<IpcMessage> ExpandAsync(int @ref, bool expand = true, int timeout = 5000, CancellationToken ct = default)
    {
        var payload = new { @ref, expand, timeout };
        var request = IpcSerializer.CreateRequest("expand", payload);
        return await SendAsync(request, ct);
    }

    /// <summary>ScrollViewer의 스크롤 위치를 변경합니다.</summary>
    public async Task<IpcMessage> ScrollAsync(int @ref, double horizontalPercent = -1, double verticalPercent = -1, int timeout = 5000, CancellationToken ct = default)
    {
        var payload = new { @ref, horizontalPercent, verticalPercent, timeout };
        var request = IpcSerializer.CreateRequest("scroll", payload);
        return await SendAsync(request, ct);
    }

    /// <summary>마우스 버튼을 누른 채 두 지점 사이를 드래그합니다.</summary>
    public async Task<IpcMessage> DragAsync(
        int? sourceRef = null,
        double? sourceX = null,
        double? sourceY = null,
        int? targetRef = null,
        double? targetX = null,
        double? targetY = null,
        double? offsetX = null,
        double? offsetY = null,
        int timeout = 5000,
        CancellationToken ct = default)
    {
        var payload = new { sourceRef, sourceX, sourceY, targetRef, targetX, targetY, offsetX, offsetY, timeout };
        var request = IpcSerializer.CreateRequest("drag", payload);
        return await SendAsync(request, ct);
    }

    /// <summary>요소의 프로퍼티 값을 조회합니다.</summary>
    public async Task<IpcMessage> GetPropertyAsync(int @ref, string propertyName, CancellationToken ct = default)
    {
        var payload = new { @ref, propertyName };
        var request = IpcSerializer.CreateRequest("getProperty", payload);
        return await SendAsync(request, ct);
    }

    /// <summary>요소의 바인딩 정보를 조회합니다.</summary>
    public async Task<IpcMessage> GetBindingsAsync(int @ref, CancellationToken ct = default)
    {
        var payload = new { @ref };
        var request = IpcSerializer.CreateRequest("getBindings", payload);
        return await SendAsync(request, ct);
    }

    /// <summary>윈도우 또는 요소의 스크린샷을 캡처합니다.</summary>
    public async Task<IpcMessage> ScreenshotAsync(int? @ref = null, CancellationToken ct = default)
    {
        var payload = new { @ref };
        var request = IpcSerializer.CreateRequest("screenshot", payload);
        return await SendAsync(request, ct);
    }

    /// <summary>프로퍼티 값 검증(assert)을 수행합니다.</summary>
    public async Task<IpcMessage> AssertAsync(int @ref, string property, string expected, CancellationToken ct = default)
    {
        var payload = new { @ref, property, expected };
        var request = IpcSerializer.CreateRequest("assert", payload);
        return await SendAsync(request, ct);
    }

    /// <summary>비주얼 트리에서 조건에 맞는 요소를 검색합니다.</summary>
    public async Task<IpcMessage> FindAsync(string? name = null, string? automationId = null, string? type = null, string? text = null, CancellationToken ct = default)
    {
        var payload = new { name, automationId, type, text };
        var request = IpcSerializer.CreateRequest("find", payload);
        return await SendAsync(request, ct);
    }

    #endregion

    #region Dispose

    /// <summary>
    /// Named Pipe 연결과 세마포어를 정리합니다.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_pipe != null)
        {
            await _pipe.DisposeAsync();
            _pipe = null;
        }
        _sendLock.Dispose();
    }

    #endregion
}

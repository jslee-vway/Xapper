using System.ComponentModel;
using ModelContextProtocol.Server;
using Xapper.Injector;
using Xapper.McpServer.Infrastructure;

namespace Xapper.McpServer.Tools;

/// <summary>
/// WPF 프로세스 목록 조회, 연결(attach), 해제(detach) MCP 도구를 제공하는 클래스.
/// </summary>
[McpServerToolType]
public sealed class ProcessTools
{
    #region Fields

    private readonly SessionManager _sessionManager;
    private readonly WpfProcessInjector _injector;
    private readonly IOperatorNotice _notice;

    #endregion

    #region Constructor

    /// <summary>
    /// <see cref="ProcessTools"/>의 새 인스턴스를 생성합니다.
    /// </summary>
    public ProcessTools(SessionManager sessionManager, WpfProcessInjector injector, IOperatorNotice notice)
    {
        _sessionManager = sessionManager;
        _injector = injector;
        _notice = notice;
    }

    #endregion

    #region MCP Tools

    /// <summary>
    /// 현재 실행 중인 WPF 프로세스 목록을 반환합니다.
    /// </summary>
    [McpServerTool(Name = "xapper_list_processes"), Description("List running WPF processes available for attachment")]
    public string ListProcesses()
    {
        var processes = WpfProcessInjector.GetWpfProcesses();

        if (processes.Count == 0)
            return "No WPF processes found.";

        var lines = new List<string> { $"Found {processes.Count} WPF process(es):", "" };
        foreach (var p in processes)
        {
            lines.Add($"  PID={p.ProcessId}  {p.ProcessName}  \"{p.MainWindowTitle}\"");
        }
        return string.Join("\n", lines);
    }

    /// <summary>
    /// 대상 WPF 프로세스에 Inspector를 주입하고 IPC 연결을 수립합니다.
    /// 각 단계(주입, 연결, 핑)에서 실패 시 구체적인 에러 메시지를 반환.
    /// </summary>
    [McpServerTool(Name = "xapper_attach"), Description("Attach to a WPF process by injecting the Xapper inspector")]
    public async Task<string> Attach(
        [Description("Process ID of the target WPF application")] int pid,
        [Description("Timeout in ms to wait for connection (default 10000)")] int timeout = 10000,
        CancellationToken ct = default)
    {
        try
        {
            _injector.Inject(pid);
        }
        catch (Exception ex)
        {
            return $"[INJECT FAILED] {ex.Message}";
        }

        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, timeoutCts.Token);

        try
        {
            await Task.Delay(2000, linkedCts.Token);
        }
        catch (OperationCanceledException)
        {
            return "[TIMEOUT] Cancelled during wait for inspector startup";
        }

        try
        {
            await _sessionManager.AttachAsync(pid, linkedCts.Token);
        }
        catch (Exception ex)
        {
            return $"[CONNECT FAILED] {ex.Message}";
        }

        try
        {
            var client = _sessionManager.GetActive();
            await client.PingAsync(linkedCts.Token);
        }
        catch (Exception ex)
        {
            return $"[PING FAILED] {ex.Message}";
        }

        return $"Attached to process {pid}. Connection verified.";
    }

    /// <summary>
    /// 현재 연결된 WPF 프로세스와의 세션을 해제합니다.
    /// </summary>
    [McpServerTool(Name = "xapper_detach"), Description("Detach from the currently attached WPF process")]
    public async Task<string> Detach(
        [Description("Process ID (optional, defaults to active session)")] int? pid = null,
        CancellationToken ct = default)
    {
        // 세션이 끝나면 조작도 끝난 것이다. 알림이 남아 사람이 헛되이 기다리지 않게 먼저 내린다.
        await _notice.HideAsync(ct);

        try
        {
            await _sessionManager.DetachAsync(pid, ct);
        }
        catch (InvalidOperationException)
        {
            // DetachAsync 는 활성 세션이 없을 때만 이 예외를 던진다 — 이미 분리된 상태이므로 오류가 아니다.
            // (다른 예외는 삼키지 않는다: 연결 정리가 실제로 실패한 것을 성공으로 위장하면 세션이 어긋난 채 남는다.)
            return "Already detached (no active session).";
        }

        return pid.HasValue
            ? $"Detached from process {pid}."
            : "Detached from active session.";
    }

    #endregion
}

using System.ComponentModel;
using System.Diagnostics;
using ModelContextProtocol.Server;
using Xapper.Injector;
using Xapper.McpServer.Infrastructure;
using Xapper.Protocol;

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

    /// <summary>띄운 앱의 파이프가 생겼는지 다시 확인하는 간격 (밀리초).</summary>
    private const int PipePollMs = 50;

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
    /// 앱을 직접 띄우되 Inspector 가 이미 들어간 채로 시작하게 합니다.
    /// .NET 런타임의 startup hook 은 앱의 진입점보다 먼저 돌므로 주입 절차도, 그 뒤의 고정 대기도 필요 없고
    /// 스플래시·로그인 창처럼 주 창보다 먼저 뜨는 화면도 사정권에 들어온다.
    /// </summary>
    [McpServerTool(Name = "xapper_launch"), Description(
        "Start a WPF app with the inspector already inside it, then attach. Prefer this over xapper_attach " +
        "whenever you can start the app yourself: there is no injection step, so it is faster, and it catches " +
        "the app from its first moment - splash screens and login dialogs that open before the main window. " +
        "The launched app becomes the active session; xapper_detach later disconnects without closing it. " +
        "Framework-dependent .NET apps only - for .NET Framework or self-contained builds, start the app " +
        "yourself and use xapper_attach.")]
    public async Task<string> Launch(
        [Description("Absolute path to the app's .exe")] string exePath,
        [Description("Command-line arguments to pass to the app")] string? args = null,
        [Description("Timeout in ms to wait for the app's inspector to come up (default 15000)")] int timeoutMs = 15000,
        CancellationToken ct = default)
    {
        if (!File.Exists(exePath))
            return $"Error: no file at {exePath}.";

        string hookDll;
        try
        {
            hookDll = _injector.ResolveInspectorDllForExe(exePath);
        }
        catch (Exception ex) when (ex is InvalidOperationException or FileNotFoundException)
        {
            return $"Error: {ex.Message}";
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = exePath,
            // 환경변수를 넘기려면 셸을 거치지 않아야 한다. 작업 디렉터리는 exe 폴더 — WPF 앱이 리소스를
            // 상대 경로로 찾는 것이 보통이다.
            UseShellExecute = false,
            WorkingDirectory = Path.GetDirectoryName(exePath) ?? Environment.CurrentDirectory
        };
        if (!string.IsNullOrWhiteSpace(args))
            startInfo.Arguments = args;
        startInfo.Environment["DOTNET_STARTUP_HOOKS"] = hookDll;

        Process? started;
        try
        {
            started = Process.Start(startInfo);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return $"Error: could not start {Path.GetFileName(exePath)} ({ex.Message}).";
        }

        if (started is null)
            return $"Error: could not start {Path.GetFileName(exePath)}.";

        using var process = started;
        var name = Path.GetFileNameWithoutExtension(exePath);
        var watch = Stopwatch.StartNew();
        var pipePath = $@"\\.\pipe\{IpcPipeNames.ForProcess(process.Id)}";

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeoutMs);

        try
        {
            // 훅은 진입점보다 먼저 돌지만 CLR 기동 자체에 시간이 걸린다. 고정 대기 대신 파이프가 생기는 즉시 붙는다.
            while (!File.Exists(pipePath))
            {
                if (process.HasExited)
                    return $"Error: {name} exited with code {process.ExitCode} before the inspector came up.";

                await Task.Delay(PipePollMs, timeoutCts.Token);
            }

            await _sessionManager.AttachAsync(process.Id, timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            return $"Error: the inspector in {name} (pid {process.Id}) did not come up within {timeoutMs} ms. " +
                   "The app is running - you can retry with xapper_attach.";
        }
        catch (Exception ex)
        {
            return $"Error: connecting to {name} (pid {process.Id}) failed ({ex.Message}).";
        }

        watch.Stop();
        return $"Launched process {process.Id} ({name}) in {watch.Elapsed.TotalSeconds:F1} s. " +
               "The app may still be starting; UI tools wait for its WPF Application.";
    }

    /// <summary>
    /// 현재 연결된 WPF 프로세스와의 세션을 해제합니다.
    /// </summary>
    [McpServerTool(Name = "xapper_detach"), Description("Detach from the currently attached WPF process")]
    public async Task<string> Detach(
        [Description("Process ID (optional, defaults to active session)")] int? pid = null,
        CancellationToken ct = default)
    {
        try
        {
            await _sessionManager.DetachAsync(pid, ct);
        }
        catch (InvalidOperationException)
        {
            // DetachAsync 는 활성 세션이 없을 때만 이 예외를 던진다 — 이미 분리된 상태이므로 오류가 아니다.
            // (다른 예외는 삼키지 않는다: 연결 정리가 실제로 실패한 것을 성공으로 위장하면 세션이 어긋난 채 남는다.)
            await _notice.HideAsync(ct);
            return "Already detached (no active session).";
        }

        // 세션이 끝나면 조작도 끝난 것이다. 알림이 남아 사람이 헛되이 기다리지 않게 내린다 — 분리가 거절된
        // 경우(다른 pid)는 세션이 살아 있으므로 그대로 둔다.
        await _notice.HideAsync(ct);

        return pid.HasValue
            ? $"Detached from process {pid}."
            : "Detached from active session.";
    }

    #endregion
}

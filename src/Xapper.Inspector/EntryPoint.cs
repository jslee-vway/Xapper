using System.IO;
using System.Runtime.Loader;

namespace Xapper.Inspector;

/// <summary>
/// GenericInjector의 ExecuteInDefaultAppDomain에 의해 호출되는 주입 진입점.
/// 대상 WPF 프로세스 내부에서 실행되며 어셈블리 리졸버 등록 후 IPC 서버를 시작.
/// </summary>
public static class EntryPoint
{
    #region Fields

    private static IpcServer? _server;
    private static readonly string LogPath = Path.Combine(Path.GetTempPath(), "xapper_inspector.log");

    /// <summary>startup hook 으로 먼저 들어왔을 때, 앱이 WPF Application 을 만들기를 기다릴 최대 시간.</summary>
    private static readonly TimeSpan ApplicationWait = TimeSpan.FromSeconds(10);

    #endregion

    #region Public Methods

    /// <summary>
    /// 주입 초기화 메서드. GenericInjector의 ExecuteInDefaultAppDomain에 의해 호출됨.
    /// 어셈블리 리졸버를 등록한 뒤 Named Pipe IPC 서버를 백그라운드에서 시작.
    /// </summary>
    /// <param name="args">GenericInjector로부터 전달받는 인자 문자열.</param>
    /// <returns>성공 시 0, 실패 시 1.</returns>
    public static int Initialize(string args)
    {
        try
        {
            Log($"Initialize called. Args={args}");

            // .NET Core의 ExecuteInDefaultAppDomain은 의존성을 자동으로 리졸브하지 않으므로
            // 의존 타입 참조 전에 어셈블리 리졸버를 먼저 등록해야 함
            var assemblyLocation = typeof(EntryPoint).Assembly.Location;
            var thisDir = !string.IsNullOrEmpty(assemblyLocation)
                ? Path.GetDirectoryName(assemblyLocation)
                : null;

            if (string.IsNullOrEmpty(thisDir))
            {
                thisDir = AppContext.BaseDirectory;
                Log($"Assembly.Location was empty, using AppContext.BaseDirectory: {thisDir}");
            }
            else
            {
                Log($"Assembly dir: {thisDir}");
            }

            AssemblyLoadContext.Default.Resolving += (ctx, name) =>
            {
                var path = Path.Combine(thisDir, name.Name + ".dll");
                Log($"Resolving: {name.Name} -> {path} (exists={File.Exists(path)})");
                if (File.Exists(path))
                    return ctx.LoadFromAssemblyPath(path);
                return null;
            };

            StartServer(string.Equals(args, "startup-hook", StringComparison.Ordinal));
            return 0;
        }
        catch (Exception ex)
        {
            Log($"Initialize FAILED: {ex}");
            return 1;
        }
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// 로그 메시지를 임시 폴더의 로그 파일에 기록합니다.
    /// </summary>
    private static void Log(string msg)
    {
        try { File.AppendAllText(LogPath, $"[{DateTime.Now:HH:mm:ss.fff}] {msg}\n"); } catch { }
    }

    /// <summary>
    /// IPC 서버를 백그라운드 태스크에서 시작합니다.
    /// Protocol 타입이 어셈블리 리졸버 등록 후에 JIT 리졸브되도록 별도 메서드로 분리.
    /// </summary>
    /// <param name="startedBeforeApp">
    /// startup hook 으로 앱의 진입점보다 먼저 기동했는지. 그 경우에만 서버가 WPF Application 을 기다린다.
    /// </param>
    private static void StartServer(bool startedBeforeApp)
    {
        var pipeName = Protocol.IpcPipeNames.ForProcess(Environment.ProcessId);
        Log($"Starting server on pipe: {pipeName} (startedBeforeApp={startedBeforeApp})");
        _server = new IpcServer(pipeName, Log, startedBeforeApp ? ApplicationWait : null);
        _ = Task.Run(async () =>
        {
            try
            {
                Log("Server task started");
                await _server.StartListening();
            }
            catch (Exception ex)
            {
                Log($"Server CRASHED: {ex}");
            }
        });
        Log("Server task fired");
    }

    #endregion
}

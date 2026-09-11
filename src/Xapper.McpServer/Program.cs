// Xapper MCP 서버 진입점.
// stdio 전송을 통해 AI 에이전트와 통신하며, WPF 프로세스 인젝션 및 UI 자동화 도구를 제공.
// 환경 변수 XAPPER_INSPECTOR_BASE_DIR, XAPPER_GENERIC_INJECTOR_DIR로 경로 오버라이드 가능.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using Xapper.Injector;
using Xapper.McpServer;
using Xapper.McpServer.Infrastructure;
using Xapper.McpServer.Tools;

var builder = Host.CreateApplicationBuilder(args);

// MCP SDK의 StdioServerTransport가 stdout을 점유하므로 콘솔 로깅 비활성화
builder.Logging.ClearProviders();

// 경로 해석: 환경 변수 우선, 없으면 임베디드 리소스 추출
var envInspectorDir = Environment.GetEnvironmentVariable("XAPPER_INSPECTOR_BASE_DIR");
var envInjectorDir = Environment.GetEnvironmentVariable("XAPPER_GENERIC_INJECTOR_DIR");

string inspectorBaseDir;
string genericInjectorDir;

if (envInspectorDir is not null && envInjectorDir is not null)
{
    inspectorBaseDir = envInspectorDir;
    genericInjectorDir = envInjectorDir;
}
else
{
    var extractor = new PayloadExtractor();
    extractor.ExtractAll();
    inspectorBaseDir = envInspectorDir ?? extractor.InspectorBaseDir;
    genericInjectorDir = envInjectorDir ?? extractor.GenericInjectorDir;
}

// 서버 전체에 걸친 사용 지침. MCP 규격이 초기화 때 한 번 전달하는 자리로, 도구 설명에 같은 문장을
// 반복하지 않도록 여기에만 둔다.
var serverInstructions = """
    Xapper drives a WPF application by injecting into its process and walking the real visual tree, not the
    UI Automation tree. Seeing and acting are not equally unconstrained, and the difference matters:

    Seeing is unconstrained. Snapshots and searches walk the actual visual tree, so controls that expose
    nothing to accessibility-based tooling are still listed here.

    Acting is not. Five actions can refuse: type, toggle, expand, select and scroll each try an accessibility
    pattern and one stock WPF type - TextBox, ToggleButton, Expander or TreeViewItem, Selector, ScrollViewer -
    and report that the element does not support the action when neither fits. Select is the odd one out: it
    reads a Selector's items directly before it considers the pattern at all.

    Click never refuses. It tries the Invoke or Toggle pattern, then a ButtonBase click event, and failing
    both it raises four simulated routed mouse events at any UIElement - reporting success in every case. So
    on a control that offers nothing, click is the action that still does something, but read the path named
    in its response rather than trusting the word "Clicked". When the element needs genuine input - hit
    testing, mouse capture, a handler on MouseDown rather than MouseLeftButtonDown - pass x/y.

    Click with x/y and any drag that does not start on a splitter, slider or scrollbar thumb are normally
    driven from inside the target process: real WM mouse messages with the OS button-state and cursor-position
    reads briefly spoofed, so WPF accepts them as genuine input while the physical cursor never moves and the
    person's focus is never taken. You can keep working while Xapper clicks and drags. Only when that
    in-process path cannot be set up does it fall back to real mouse input, which does move the cursor and take
    focus; the response names which path ran ("synthetic mouse input" vs "real mouse input"). Prefer refs and
    coordinates freely - the cursor stays the person's in the normal case.

    Attach first: xapper_list_processes, then xapper_attach. Attaching again to a process you are already
    attached to is rejected, but the rejection surfaces only after the injection attempt, as a connect
    failure - so detach before re-attaching rather than retrying.

    Element refs come from xapper_snapshot and xapper_find. Only xapper_snapshot resets them, and it does
    more than invalidate: it also restarts numbering from 1, so a ref you obtained before a snapshot may
    now resolve to a completely different element rather than fail. Use refs from the most recent snapshot.
    xapper_find leaves existing refs alone and only hands out new numbers, so one element can hold several.
    A ref can also stop resolving because the element left the visual tree, as virtualized rows and closed
    dialogs do.

    Calls are serialized inside the server, so overlapping tool calls queue rather than interleave. A call
    cancelled while it is reading a reply invalidates the connection, because the remaining bytes can no
    longer be told apart from the next reply; the following call will ask you to detach and attach again.
    """;

builder.Services.AddSingleton<SessionManager>();
builder.Services.AddSingleton(new WpfProcessInjector(inspectorBaseDir, genericInjectorDir));

builder.Services.AddMcpServer(options =>
{
    options.ServerInfo = new()
    {
        Name = "Xapper",
        Version = "0.1.0"
    };
    options.ServerInstructions = serverInstructions;
})
.WithStdioServerTransport()
.WithTools<ProcessTools>()
.WithTools<SnapshotTools>()
.WithTools<ActionTools>()
.WithTools<InteractionTools>()
.WithTools<DiagnosticTools>()
.WithTools<CaptureTools>()
.WithTools<FindTools>();

var app = builder.Build();
await app.RunAsync();

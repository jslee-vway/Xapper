using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace Xapper.McpServer.Infrastructure;

/// <summary>
/// "Xapper 조작중" 알림 창. 이 프로세스(MCP 서버)가 띄운다 — 주입된 쪽이 띄우면 대상 앱의 시각 트리에 섞여
/// 스냅샷과 검색에 잡히기 때문이다. 전용 STA 스레드를 처음 쓸 때 세워 프로세스가 사는 동안 유지하고,
/// 보이기와 숨기기는 그 스레드의 디스패처로 넘긴다.
///
/// 창은 알리기만 해야 한다. 그래서 세 가지 확장 스타일을 건다 — 활성화되지 않고(포커스를 가져가면 알리려던
/// 바로 그 방해를 저지르는 꼴이다), 클릭이 통과하며(Xapper 가 보내는 클릭이 하필 이 자리에 떨어져도 삼키지
/// 않고, WindowFromPoint 도 건너뛴다), 작업표시줄과 Alt+Tab 에 나타나지 않는다.
/// </summary>
public sealed class OperatorNotice : IOperatorNotice, IAsyncDisposable
{
    #region Fields

    /// <summary>사람에게 보이는 헤드라인.</summary>
    private const string Headline = "Xapper 조작중";

    /// <summary>헤드라인 아래 기본 안내. 에이전트가 message 를 주면 그것으로 바뀐다.</summary>
    private const string DefaultDetail = "AI 가 이 프로그램을 조작하고 있습니다 — 알림이 사라질 때까지 마우스와 키보드를 잠시 두세요";

    /// <summary>
    /// 알림 스레드의 응답을 기다릴 최대 시간. 화면이 느려도 조작을 이만큼 이상 늦추지 않고, 알림 스레드가 죽어
    /// 디스패처 작업이 영영 돌지 않아도 도구 호출이 여기서 끝난다.
    /// </summary>
    private static readonly TimeSpan OperationTimeout = TimeSpan.FromSeconds(1);

    private readonly object _gate = new();
    private Thread? _thread;

    /// <summary>알림 스레드가 창을 만들고 넘겨준 디스패처. 첫 호출이 만들고 그 뒤 호출은 같은 작업을 기다린다 —
    /// 동시에 두 번 부르면 스레드와 창이 둘 생기고 하나는 영영 안 내려가므로, 완료 여부가 아니라 작업 자체를 공유한다.</summary>
    private Task<Dispatcher>? _ready;
    private Window? _window;
    private TextBlock? _detail;
    private IntPtr _handle;
    private volatile bool _visible;

    #endregion

    #region Public Properties

    /// <inheritdoc />
    public bool IsVisible => _visible;

    /// <summary>알림 창의 윈도우 핸들. 창이 아직 만들어지지 않았으면 0. 테스트가 창의 성질을 재는 데 쓴다.</summary>
    internal IntPtr Handle => _handle;

    #endregion

    #region Public Methods

    /// <inheritdoc />
    public async Task<(bool Shown, string? Error)> ShowAsync(string? message, int? targetProcessId, CancellationToken ct)
    {
        Dispatcher dispatcher;
        try
        {
            dispatcher = await EnsureWindowAsync();
        }
        catch (Exception failure)
        {
            // 알림은 정보다. 화면이 없는 환경에서 창을 못 띄웠다고 조작까지 막으면 본말이 뒤바뀐다.
            return (false, $"the notice window could not be created: {failure.Message}");
        }

        var target = TargetRectangleOf(targetProcessId);
        var shown = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _visible = true;
        _ = dispatcher.InvokeAsync(() => Show(message, target, shown));

        try
        {
            // 입력이 나가기 전에 알림이 화면에 있어야 하므로 그려질 때까지 기다린다. 단, 무한정은 아니다 —
            // 늦게라도 뜨고, 상태는 이미 "보임" 이므로 hide 가 내린다.
            await shown.Task.WaitAsync(OperationTimeout, ct);
        }
        catch (TimeoutException)
        {
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            // Show 안에서 난 예외: 창은 안 떴다.
            _visible = false;
            return (false, $"the notice window could not be shown: {failure.Message}");
        }

        return (true, null);
    }

    /// <inheritdoc />
    public async Task HideAsync(CancellationToken ct)
    {
        _visible = false;
        var dispatcher = DispatcherIfRunning();
        if (dispatcher is null)
            return;

        try
        {
            await dispatcher.InvokeAsync(() => _window?.Hide()).Task.WaitAsync(OperationTimeout, ct);
        }
        catch (TimeoutException)
        {
            // 알림 스레드가 응답하지 않는다. 내릴 창이 있다면 어차피 못 내리고, 도구 호출을 여기서 붙들지 않는다.
        }
    }

    /// <summary>
    /// 알림 스레드를 내리고 창을 닫습니다. 서버 프로세스가 끝날 때, 그리고 테스트가 정리할 때 쓴다.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        Thread? thread;
        var dispatcher = DispatcherIfRunning();
        lock (_gate)
        {
            thread = _thread;
            _ready = null;
            _thread = null;
        }

        if (dispatcher is null || thread is null)
            return;

        await dispatcher.InvokeAsync(() => _window?.Close());
        dispatcher.InvokeShutdown();
        thread.Join();
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// 알림 창을 들고 있는 STA 스레드를 처음 한 번 세우고, 창이 만들어진 뒤 그 디스패처를 돌려줍니다.
    /// </summary>
    private Task<Dispatcher> EnsureWindowAsync()
    {
        lock (_gate)
        {
            if (_ready is not null)
                return _ready;

            var ready = new TaskCompletionSource<Dispatcher>(TaskCreationOptions.RunContinuationsAsynchronously);
            _thread = new Thread(() =>
            {
                try
                {
                    var dispatcher = Dispatcher.CurrentDispatcher;
                    _window = BuildWindow();
                    ready.SetResult(dispatcher);
                    Dispatcher.Run();
                }
                catch (Exception failure)
                {
                    ready.TrySetException(failure);
                }
            })
            {
                Name = "Xapper operator notice",
                IsBackground = true
            };
            _thread.SetApartmentState(ApartmentState.STA);
            _thread.Start();

            _ready = ready.Task;
            return _ready;
        }
    }

    /// <summary>알림 스레드가 창을 만들어 디스패처를 넘겨준 상태면 그 디스패처, 아직이거나 실패했거나 내려갔으면 null.</summary>
    private Dispatcher? DispatcherIfRunning()
    {
        lock (_gate)
        {
            return _ready is { IsCompletedSuccessfully: true } ready ? ready.Result : null;
        }
    }

    /// <summary>
    /// 알림 창을 만듭니다. 확장 스타일은 창 핸들이 생긴 직후, 화면에 나타나기 전에 건다.
    /// </summary>
    private Window BuildWindow()
    {
        var headline = new TextBlock
        {
            Text = Headline,
            FontSize = 22,
            FontWeight = FontWeights.Bold,
            Foreground = Brushes.White,
            HorizontalAlignment = HorizontalAlignment.Center
        };

        _detail = new TextBlock
        {
            FontSize = 13,
            Foreground = Brushes.White,
            Opacity = 0.85,
            Margin = new Thickness(0, 4, 0, 0),
            HorizontalAlignment = HorizontalAlignment.Center
        };

        var stack = new StackPanel();
        stack.Children.Add(headline);
        stack.Children.Add(_detail);

        var window = new Window
        {
            Title = "Xapper",
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            AllowsTransparency = true,
            Background = Brushes.Transparent,
            SizeToContent = SizeToContent.WidthAndHeight,
            WindowStartupLocation = WindowStartupLocation.Manual,
            Topmost = true,
            ShowInTaskbar = false,
            ShowActivated = false,
            Focusable = false,
            IsHitTestVisible = false,
            Content = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0xE6, 0xB0, 0x2A, 0x2A)),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(22, 12, 22, 12),
                Child = stack
            }
        };

        window.SourceInitialized += (_, _) =>
        {
            var handle = new WindowInteropHelper(window).Handle;
            MakeInert(handle);
            _handle = handle;
        };

        return window;
    }

    /// <summary>
    /// 창을 보이고 대상 창 위쪽 가운데로 옮긴 뒤, 그려진 뒤 신호를 보냅니다. 위치는 보인 뒤에 잡는다 —
    /// 내용 크기에 맞춘 창의 크기는 첫 레이아웃이 지나야 알 수 있다. 좌표는 물리 픽셀로 SetWindowPos 에 넘겨
    /// WPF 의 DIP 변환(모니터마다 DPI 가 다를 때 어긋난다)을 거치지 않는다.
    /// </summary>
    private void Show(string? message, RECT? target, TaskCompletionSource shown)
    {
        if (_window is null || _detail is null)
        {
            shown.TrySetResult();
            return;
        }

        try
        {
            _detail.Text = string.IsNullOrWhiteSpace(message) ? DefaultDetail : message.Trim();
            _window.Show();

            if (_handle != IntPtr.Zero && GetWindowRect(_handle, out var self))
            {
                var area = target ?? PrimaryWorkArea();
                var (x, y) = NoticePlacement.TopCentre(area.Left, area.Top, area.Right, area.Bottom,
                    self.Right - self.Left, self.Bottom - self.Top);
                SetWindowPos(_handle, HwndTopmost, x, y, 0, 0, SwpNoSize | SwpNoActivate);
            }
        }
        catch (Exception failure)
        {
            // 호출자가 (false, 이유) 로 돌려준다. 여기서 던지면 디스패처 작업 안에서 삼켜져 아무도 모른다.
            shown.TrySetException(failure);
            return;
        }

        // 그리기 뒤에 도는 우선순위에 걸어 두면, 여기 도달했을 때는 창이 실제로 화면에 있다.
        _window.Dispatcher.InvokeAsync(() => shown.TrySetResult(), DispatcherPriority.ContextIdle);
    }

    /// <summary>
    /// 대상 프로세스의 주 창 사각형. PID 가 없거나 주 창이 없거나 프로세스가 사라졌으면 null. 최소화된 창도 null 이다 —
    /// 그 사각형은 화면 밖(-32000)이라 알림까지 안 보이게 된다.
    /// </summary>
    private static RECT? TargetRectangleOf(int? processId)
    {
        if (processId is null)
            return null;

        try
        {
            using var process = Process.GetProcessById(processId.Value);
            var handle = process.MainWindowHandle;
            if (handle == IntPtr.Zero || IsIconic(handle) || !GetWindowRect(handle, out var rect))
                return null;
            return rect;
        }
        catch (ArgumentException)
        {
            // 프로세스가 이미 없다.
            return null;
        }
        catch (InvalidOperationException)
        {
            // 프로세스가 종료 중이라 창 정보를 읽을 수 없다.
            return null;
        }
    }

    /// <summary>주 모니터의 작업 영역(물리 픽셀). 대상 창을 모를 때의 폴백.</summary>
    private static RECT PrimaryWorkArea()
    {
        var rect = new RECT();
        SystemParametersInfo(SpiGetWorkArea, 0, ref rect, 0);
        return rect;
    }

    /// <summary>
    /// 창을 "알리기만 하는" 창으로 만듭니다: 활성화되지 않고, 클릭이 통과하며, 작업표시줄과 Alt+Tab 에 없다.
    /// </summary>
    private static void MakeInert(IntPtr handle)
    {
        var style = GetExtendedStyle(handle);
        SetExtendedStyle(handle, style | WsExNoActivate | WsExTransparent | WsExToolWindow);
    }

    #endregion

    #region Interop

    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private const int GwlExStyle = -20;
    private const long WsExTransparent = 0x00000020;
    private const long WsExToolWindow = 0x00000080;
    private const long WsExNoActivate = 0x08000000;
    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoActivate = 0x0010;
    private const uint SpiGetWorkArea = 0x0030;
    private static readonly IntPtr HwndTopmost = new(-1);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SystemParametersInfo(uint action, uint param, ref RECT rect, uint winIni);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong32(IntPtr hWnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW")]
    private static extern int SetWindowLong32(IntPtr hWnd, int index, int value);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int index);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int index, IntPtr value);

    /// <summary>64비트 user32 에만 있는 Ptr 변형을 쓰되, 32비트 프로세스에서는 원래 함수로 돌아간다.</summary>
    private static long GetExtendedStyle(IntPtr handle)
    {
        return IntPtr.Size == 8
            ? GetWindowLongPtr64(handle, GwlExStyle).ToInt64()
            : GetWindowLong32(handle, GwlExStyle);
    }

    /// <summary>64비트 user32 에만 있는 Ptr 변형을 쓰되, 32비트 프로세스에서는 원래 함수로 돌아간다.</summary>
    private static void SetExtendedStyle(IntPtr handle, long style)
    {
        if (IntPtr.Size == 8)
            SetWindowLongPtr64(handle, GwlExStyle, new IntPtr(style));
        else
            SetWindowLong32(handle, GwlExStyle, unchecked((int)style));
    }

    #endregion
}

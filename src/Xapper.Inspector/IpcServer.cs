using System.IO.Pipes;
using System.Text.Json;
using System.Windows;
using Xapper.Protocol;
using Xapper.Protocol.Messages.Requests;
using Xapper.Protocol.Messages.Responses;
using Xapper.Inspector.VisualTree;
using Xapper.Inspector.Actions;
using Xapper.Inspector.Diagnostics;
using Xapper.Inspector.Capture;

namespace Xapper.Inspector;

/// <summary>
/// 주입된 WPF 프로세스 내부에서 실행되는 Named Pipe IPC 서버.
/// McpServer로부터 요청을 수신하고, Dispatcher를 통해 UI 스레드에서 액션을 실행한 뒤 응답을 반환.
/// </summary>
public sealed class IpcServer
{
    #region Fields

    private readonly string _pipeName;
    private readonly Action<string>? _log;
    private readonly RefRegistry _refRegistry = new();
    private readonly TreeWalker _treeWalker = new();
    private CancellationTokenSource? _cts;

    #endregion

    #region Constructor

    /// <summary>
    /// <see cref="IpcServer"/>의 새 인스턴스를 생성합니다.
    /// </summary>
    /// <param name="pipeName">수신 대기할 Named Pipe 이름.</param>
    /// <param name="log">진단 메시지를 기록할 대상. 주입된 프로세스에서는 이것이 유일한 관찰 통로이다.</param>
    public IpcServer(string pipeName, Action<string>? log = null)
    {
        _pipeName = pipeName;
        _log = log;
    }

    #endregion

    #region Lifecycle

    /// <summary>
    /// Named Pipe 연결을 무한 루프로 수신 대기합니다.
    /// 한 번에 하나의 연결만 허용 (maxNumberOfServerInstances=1).
    /// </summary>
    public async Task StartListening()
    {
        _cts = new CancellationTokenSource();

        while (!_cts.IsCancellationRequested)
        {
            await using var pipe = new NamedPipeServerStream(
                _pipeName,
                PipeDirection.InOut,
                1,
                PipeTransmissionMode.Byte,
                PipeOptions.Asynchronous);

            await pipe.WaitForConnectionAsync(_cts.Token);

            try
            {
                await HandleConnection(pipe, _cts.Token);
            }
            catch (Exception ex)
            {
                // 연결 해제, 종료 요청, 프레임이 어긋난 요청 모두 이 연결 하나만 버리고 다음 연결을 계속 받는다.
                // 예외가 여기를 빠져나가면 수신 루프가 끝나 대상 앱을 재시작하기 전에는 다시 붙을 수 없다.
                // 다만 조용히 삼키지는 않는다 — 주입된 프로세스 안에서 무슨 일이 있었는지 볼 방법이 이 기록뿐이다.
                _log?.Invoke($"Connection dropped: {ex}");
            }
        }
    }

    /// <summary>
    /// IPC 서버를 중지합니다.
    /// </summary>
    public void Stop() => _cts?.Cancel();

    #endregion

    #region Message Handling

    /// <summary>
    /// 단일 파이프 연결에서 메시지를 반복적으로 수신하고 처리합니다.
    /// </summary>
    private async Task HandleConnection(NamedPipeServerStream pipe, CancellationToken ct)
    {
        while (pipe.IsConnected && !ct.IsCancellationRequested)
        {
            var message = await IpcSerializer.DeserializeAsync(pipe, ct);
            if (message == null) break;

            var response = await ProcessMessage(message);
            var responseBytes = IpcSerializer.Serialize(response);
            await pipe.WriteAsync(responseBytes, ct);
            await pipe.FlushAsync(ct);
        }
    }

    /// <summary>
    /// 수신된 메시지의 Method에 따라 적절한 핸들러로 디스패치합니다.
    /// </summary>
    private async Task<IpcMessage> ProcessMessage(IpcMessage message)
    {
        try
        {
            return message.Method switch
            {
                "ping" => await HandlePing(message),
                "snapshot" => await HandleSnapshot(message),
                "click" => await HandleClick(message),
                "type" => await HandleType(message),
                "select" => await HandleSelect(message),
                "toggle" => await HandleToggle(message),
                "expand" => await HandleExpand(message),
                "scroll" => await HandleScroll(message),
                "drag" => await HandleDrag(message),
                "getProperty" => await HandleGetProperty(message),
                "getBindings" => await HandleGetBindings(message),
                "screenshot" => await HandleScreenshot(message),
                "assert" => await HandleAssert(message),
                "find" => await HandleFind(message),
                _ => IpcSerializer.CreateError(message.Id, $"Unknown method: {message.Method}")
            };
        }
        catch (Exception ex)
        {
            return IpcSerializer.CreateError(message.Id, ex.Message);
        }
    }

    #endregion

    #region Action Handlers

    /// <summary>
    /// 핑 요청을 처리하여 프로세스 정보를 반환합니다.
    /// </summary>
    private Task<IpcMessage> HandlePing(IpcMessage message)
    {
        var response = new PongResponse
        {
            ProcessId = Environment.ProcessId,
            ProcessName = System.Diagnostics.Process.GetCurrentProcess().ProcessName
        };
        return Task.FromResult(IpcSerializer.CreateResponse(message.Id, response));
    }

    /// <summary>
    /// 비주얼 트리 스냅샷을 생성합니다. RefRegistry를 초기화하고 새 참조를 할당.
    /// </summary>
    private async Task<IpcMessage> HandleSnapshot(IpcMessage message)
    {
        var request = message.Payload.HasValue
            ? IpcSerializer.DeserializePayload<SnapshotRequest>(message.Payload.Value)
            : new SnapshotRequest();

        var skipped = new List<string>();

        var snapshot = await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            _refRegistry.Clear();

            // 특정 요소가 지정된 경우 그 요소부터 탐색
            if (request.RootRef.HasValue)
            {
                var root = _refRegistry.Resolve(request.RootRef.Value)
                    ?? throw new InvalidOperationException($"Element ref={request.RootRef} not found");
                return _treeWalker.Walk(root, _refRegistry, request.MaxDepth, skipped);
            }

            // 모든 열린 윈도우를 탐색 (다이얼로그 등 별도 Window 포함)
            var windows = Application.Current.Windows;
            if (windows.Count == 0)
                throw new InvalidOperationException("No windows found");

            // 윈도우가 1개면 그대로 반환
            if (windows.Count == 1)
                return _treeWalker.Walk(windows[0], _refRegistry, request.MaxDepth, skipped);

            // 여러 윈도우면 가상 루트 아래에 배치
            var virtualRoot = new ElementSnapshot
            {
                Ref = 0,
                Type = "Application",
                IsEnabled = true,
                IsVisible = true
            };
            foreach (Window window in windows)
            {
                virtualRoot.Children.Add(_treeWalker.Walk(window, _refRegistry, request.MaxDepth, skipped));
            }
            return virtualRoot;
        });

        var response = new SnapshotResponse
        {
            Generation = _refRegistry.Generation,
            Root = snapshot,
            SkippedNodes = skipped
        };
        return IpcSerializer.CreateResponse(message.Id, response);
    }

    /// <summary>
    /// 지정된 요소를 클릭합니다.
    /// </summary>
    private async Task<IpcMessage> HandleClick(IpcMessage message)
    {
        var request = IpcSerializer.DeserializePayload<ClickRequest>(message.Payload!.Value);

        var element = _refRegistry.Resolve(request.Ref)
            ?? throw new InvalidOperationException($"Element ref={request.Ref} not found. Call snapshot first.");

        var waiter = new AutoWait.ElementWaiter(TimeSpan.FromMilliseconds(request.Timeout));
        await waiter.WaitForReady(element);

        var warning = await Application.Current.Dispatcher.InvokeAsync(
            () => ClickAction.Execute(element, request.X, request.Y));

        var posInfo = request.X.HasValue && request.Y.HasValue
            ? $" at ({request.X:F2},{request.Y:F2})"
            : "";
        var text = $"Clicked ref={request.Ref}{posInfo}";
        if (warning is not null)
            text += $" | {warning}";

        var response = new ActionResponse { Success = true, Message = text };
        return IpcSerializer.CreateResponse(message.Id, response);
    }

    /// <summary>
    /// 지정된 요소에 텍스트를 입력합니다.
    /// </summary>
    private async Task<IpcMessage> HandleType(IpcMessage message)
    {
        var request = IpcSerializer.DeserializePayload<TypeTextRequest>(message.Payload!.Value);

        var element = _refRegistry.Resolve(request.Ref)
            ?? throw new InvalidOperationException($"Element ref={request.Ref} not found. Call snapshot first.");

        var waiter = new AutoWait.ElementWaiter(TimeSpan.FromMilliseconds(request.Timeout));
        await waiter.WaitForReady(element);

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            TypeAction.Execute(element, request.Text, request.Clear);
        });

        var response = new ActionResponse { Success = true, Message = $"Typed into ref={request.Ref}" };
        return IpcSerializer.CreateResponse(message.Id, response);
    }

    /// <summary>
    /// Selector 컨트롤에서 항목을 선택합니다.
    /// </summary>
    private async Task<IpcMessage> HandleSelect(IpcMessage message)
    {
        var request = IpcSerializer.DeserializePayload<SelectRequest>(message.Payload!.Value);
        var element = _refRegistry.Resolve(request.Ref)
            ?? throw new InvalidOperationException($"Element ref={request.Ref} not found.");

        var waiter = new AutoWait.ElementWaiter(TimeSpan.FromMilliseconds(request.Timeout));
        await waiter.WaitForReady(element);

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            SelectAction.Execute(element, request.ItemText, request.ItemIndex);
        });

        var response = new ActionResponse { Success = true, Message = $"Selected item in ref={request.Ref}" };
        return IpcSerializer.CreateResponse(message.Id, response);
    }

    /// <summary>
    /// CheckBox/ToggleButton의 토글 상태를 전환합니다.
    /// </summary>
    private async Task<IpcMessage> HandleToggle(IpcMessage message)
    {
        var request = IpcSerializer.DeserializePayload<ToggleRequest>(message.Payload!.Value);
        var element = _refRegistry.Resolve(request.Ref)
            ?? throw new InvalidOperationException($"Element ref={request.Ref} not found.");

        var waiter = new AutoWait.ElementWaiter(TimeSpan.FromMilliseconds(request.Timeout));
        await waiter.WaitForReady(element);

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            ToggleAction.Execute(element);
        });

        var response = new ActionResponse { Success = true, Message = $"Toggled ref={request.Ref}" };
        return IpcSerializer.CreateResponse(message.Id, response);
    }

    /// <summary>
    /// TreeViewItem/Expander를 확장 또는 축소합니다.
    /// </summary>
    private async Task<IpcMessage> HandleExpand(IpcMessage message)
    {
        var request = IpcSerializer.DeserializePayload<ExpandRequest>(message.Payload!.Value);
        var element = _refRegistry.Resolve(request.Ref)
            ?? throw new InvalidOperationException($"Element ref={request.Ref} not found.");

        var waiter = new AutoWait.ElementWaiter(TimeSpan.FromMilliseconds(request.Timeout));
        await waiter.WaitForReady(element);

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            ExpandAction.Execute(element, request.Expand);
        });

        var action = request.Expand ? "Expanded" : "Collapsed";
        var response = new ActionResponse { Success = true, Message = $"{action} ref={request.Ref}" };
        return IpcSerializer.CreateResponse(message.Id, response);
    }

    /// <summary>
    /// ScrollViewer의 스크롤 위치를 변경합니다.
    /// </summary>
    private async Task<IpcMessage> HandleScroll(IpcMessage message)
    {
        var request = IpcSerializer.DeserializePayload<ScrollRequest>(message.Payload!.Value);
        var element = _refRegistry.Resolve(request.Ref)
            ?? throw new InvalidOperationException($"Element ref={request.Ref} not found.");

        var waiter = new AutoWait.ElementWaiter(TimeSpan.FromMilliseconds(request.Timeout));
        await waiter.WaitForReady(element);

        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            ScrollAction.Execute(element, request.HorizontalPercent, request.VerticalPercent);
        });

        var response = new ActionResponse { Success = true, Message = $"Scrolled ref={request.Ref}" };
        return IpcSerializer.CreateResponse(message.Id, response);
    }

    /// <summary>
    /// 요소 사이 또는 화면 좌표 사이를 마우스 버튼을 누른 채 드래그합니다.
    /// 좌표 변환과 포그라운드 전환만 UI 스레드에서 수행하고 입력 주입은 UI 스레드 밖에서 실행하여,
    /// 드래그가 시작된 뒤 대상 앱이 중첩 메시지 루프를 돌 수 있게 한다.
    /// </summary>
    private async Task<IpcMessage> HandleDrag(IpcMessage message)
    {
        if (message.Payload is null)
            throw new InvalidOperationException("drag request requires a payload.");

        var request = IpcSerializer.DeserializePayload<DragRequest>(message.Payload.Value);
        var source = ResolveDragElement(request.SourceRef);
        var target = ResolveDragElement(request.TargetRef);

        var waiter = new AutoWait.ElementWaiter(TimeSpan.FromMilliseconds(request.Timeout));
        if (source is not null)
            await waiter.WaitForReady(source);
        if (target is not null)
            await waiter.WaitForReady(target);

        var endpoints = await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var start = ResolveStartPoint(source, request);
            var end = ResolveEndPoint(target, request, start);

            var anchor = source ?? target;
            var activated = anchor is null || MouseInput.BringToForeground(anchor);

            return (Start: start, End: end, Activated: activated);
        });

        await Actions.DragAction.ExecuteAsync(endpoints.Start, endpoints.End);

        var text = $"Dragged ({endpoints.Start.X:F0},{endpoints.Start.Y:F0}) -> ({endpoints.End.X:F0},{endpoints.End.Y:F0})";
        if (!endpoints.Activated)
            text += " | WARNING: the target window could not be brought to the foreground. The first input may have " +
                    "been consumed by window activation, so the drag may not have reached the control. " +
                    "Bring the window to the front and retry.";

        var response = new ActionResponse { Success = true, Message = text };
        return IpcSerializer.CreateResponse(message.Id, response);
    }

    /// <summary>
    /// 드래그용 참조 번호를 UIElement로 해석합니다. 번호가 지정되지 않았으면 null을 반환합니다.
    /// </summary>
    private UIElement? ResolveDragElement(int? elementRef)
    {
        if (!elementRef.HasValue)
            return null;

        var element = _refRegistry.Resolve(elementRef.Value)
            ?? throw new InvalidOperationException($"Element ref={elementRef} not found. Call snapshot first.");

        return element as UIElement
            ?? throw new InvalidOperationException($"Element ref={elementRef} ({element.GetType().Name}) is not a UIElement.");
    }

    /// <summary>
    /// 드래그 출발 지점을 스크린 좌표로 계산합니다. 요소가 있으면 요소 내 상대 비율, 없으면 화면 좌표로 해석.
    /// </summary>
    private static Point ResolveStartPoint(UIElement? source, DragRequest request)
    {
        if (source is not null)
            return MouseInput.ToScreenPoint(source, request.SourceX ?? 0.5, request.SourceY ?? 0.5);

        if (!request.SourceX.HasValue || !request.SourceY.HasValue)
            throw new InvalidOperationException(
                "Drag requires sourceRef, or both sourceX and sourceY as screen coordinates.");

        return new Point(request.SourceX.Value, request.SourceY.Value);
    }

    /// <summary>
    /// 드래그 도착 지점을 스크린 좌표로 계산합니다. 대상 요소, 출발점 기준 오프셋, 화면 좌표 순으로 해석.
    /// </summary>
    private static Point ResolveEndPoint(UIElement? target, DragRequest request, Point start)
    {
        if (target is not null)
            return MouseInput.ToScreenPoint(target, request.TargetX ?? 0.5, request.TargetY ?? 0.5);

        if (request.OffsetX.HasValue || request.OffsetY.HasValue)
            return new Point(start.X + (request.OffsetX ?? 0), start.Y + (request.OffsetY ?? 0));

        if (!request.TargetX.HasValue || !request.TargetY.HasValue)
            throw new InvalidOperationException(
                "Drag requires targetRef, offsetX/offsetY, or both targetX and targetY as screen coordinates.");

        return new Point(request.TargetX.Value, request.TargetY.Value);
    }

    /// <summary>
    /// 요소의 프로퍼티 값을 조회합니다.
    /// </summary>
    private async Task<IpcMessage> HandleGetProperty(IpcMessage message)
    {
        var request = IpcSerializer.DeserializePayload<GetPropertyRequest>(message.Payload!.Value);
        var element = _refRegistry.Resolve(request.Ref)
            ?? throw new InvalidOperationException($"Element ref={request.Ref} not found.");

        var response = await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var result = PropertyReader.ReadProperty(element, request.PropertyName);
            result.Ref = request.Ref;
            return result;
        });

        return IpcSerializer.CreateResponse(message.Id, response);
    }

    /// <summary>
    /// 요소의 데이터 바인딩 정보를 조회합니다.
    /// </summary>
    private async Task<IpcMessage> HandleGetBindings(IpcMessage message)
    {
        var request = IpcSerializer.DeserializePayload<GetBindingsRequest>(message.Payload!.Value);
        var element = _refRegistry.Resolve(request.Ref)
            ?? throw new InvalidOperationException($"Element ref={request.Ref} not found.");

        var response = await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var result = BindingInspector.GetBindings(element);
            result.Ref = request.Ref;
            return result;
        });

        return IpcSerializer.CreateResponse(message.Id, response);
    }

    /// <summary>
    /// 윈도우 또는 특정 요소의 스크린샷을 캡처합니다.
    /// </summary>
    private async Task<IpcMessage> HandleScreenshot(IpcMessage message)
    {
        var request = message.Payload.HasValue
            ? IpcSerializer.DeserializePayload<ScreenshotRequest>(message.Payload.Value)
            : new ScreenshotRequest();

        var response = await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            if (request.Ref.HasValue)
            {
                var element = _refRegistry.Resolve(request.Ref.Value) as UIElement
                    ?? throw new InvalidOperationException($"Element ref={request.Ref} not found or not a UIElement.");
                return ScreenshotCapture.CaptureElement(element);
            }
            return ScreenshotCapture.CaptureWindow();
        });

        return IpcSerializer.CreateResponse(message.Id, response);
    }

    /// <summary>
    /// 프로퍼티 값이 기대값과 일치하는지 검증합니다.
    /// </summary>
    private async Task<IpcMessage> HandleAssert(IpcMessage message)
    {
        var request = IpcSerializer.DeserializePayload<AssertRequest>(message.Payload!.Value);
        var element = _refRegistry.Resolve(request.Ref)
            ?? throw new InvalidOperationException($"Element ref={request.Ref} not found.");

        var (actual, passed) = await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var prop = PropertyReader.ReadProperty(element, request.Property);
            var actualValue = prop.Value ?? "null";
            var pass = actualValue.Equals(request.Expected, StringComparison.OrdinalIgnoreCase);
            return (actualValue, pass);
        });

        var response = new ActionResponse
        {
            Success = passed,
            Message = passed
                ? $"PASS: {request.Property} == \"{request.Expected}\""
                : $"FAIL: {request.Property} expected \"{request.Expected}\" but was \"{actual}\""
        };
        return IpcSerializer.CreateResponse(message.Id, response);
    }

    /// <summary>
    /// 비주얼 트리에서 조건에 맞는 요소를 검색합니다.
    /// </summary>
    private async Task<IpcMessage> HandleFind(IpcMessage message)
    {
        var request = IpcSerializer.DeserializePayload<FindElementRequest>(message.Payload!.Value);

        var response = await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var finder = new ElementFinder();
            var allResults = new FindElementResponse { Matches = [] };

            // 모든 열린 윈도우에서 검색
            foreach (Window window in Application.Current.Windows)
            {
                var result = finder.Find(window, request, _refRegistry);
                allResults.Matches.AddRange(result.Matches);
                allResults.SkippedNodes.AddRange(result.SkippedNodes);
            }

            return allResults;
        });

        return IpcSerializer.CreateResponse(message.Id, response);
    }

    #endregion
}

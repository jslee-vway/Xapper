using System.Diagnostics.CodeAnalysis;
using System.IO.Pipes;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
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

            // 상한을 넘는 프레임은 수신 측이 거부하고, 그 시점에는 이미 연결이 어긋나 세션이 끝난다.
            // 보내기 전에 잡아 무엇을 어떻게 줄이면 되는지 알려주는 오류로 바꾼다.
            if (responseBytes.Length > IpcSerializer.MaxPayloadBytes)
                responseBytes = IpcSerializer.Serialize(TooLargeError(message.Id, responseBytes.Length));

            await pipe.WriteAsync(responseBytes, ct);
            await pipe.FlushAsync(ct);
        }
    }

    /// <summary>
    /// 조작이 처리될 때까지 기다린 뒤 결과 메시지를 돌려줍니다.
    /// 접근성 경로는 조작을 큐에 걸어 두고 곧바로 돌아오므로, 기다리지 않으면 호출자의 다음 호출이
    /// 조작 이전 상태를 보게 된다. 제한 시간 안에 끝나지 않으면 그 사실을 메시지에 적어 알린다.
    /// </summary>
    /// <param name="message">조작 결과 메시지.</param>
    /// <param name="timeoutMs">기다릴 최대 시간 (밀리초).</param>
    /// <returns>필요하면 안내가 덧붙은 결과 메시지.</returns>
    private static async Task<string> SettleAsync(string message, int timeoutMs)
    {
        // 조작이 앱을 닫았을 수 있다. 그 경우 기다릴 UI 작업 자체가 남아 있지 않다.
        var application = Application.Current;
        if (application is null)
            return message;

        var settled = await AutoWait.DispatcherDrain.WaitAsync(
            application.Dispatcher, TimeSpan.FromMilliseconds(timeoutMs));

        return settled
            ? message
            : message + $" | NOTE: the application was still busy after {timeoutMs} ms, so it may not have " +
              "finished processing this action. Re-read the state before deciding whether it took effect.";
    }

    /// <summary>
    /// 앱의 핸들러를 실행하는 작업을 UI 스레드에서 제한 시간까지만 기다립니다. 핸들러가 모달 대화상자를 열면
    /// 끝나지 않으므로(<see cref="AutoWait.DispatcherCall"/>), 그때는 (false, 기본값) 을 돌려 호출자가
    /// <see cref="StillHandlingResponse"/> 로 응답하게 한다.
    /// </summary>
    private static Task<(bool Completed, T? Result)> RunOnUiThread<T>(Func<T> action, int timeoutMs)
    {
        return AutoWait.DispatcherCall.RunAsync(Application.Current.Dispatcher, action, TimeSpan.FromMilliseconds(timeoutMs));
    }

    /// <summary>
    /// 조작은 전달됐지만 앱이 제한 시간 안에 처리를 끝내지 못했을 때의 응답을 만듭니다. 대개 핸들러가 MessageBox 같은
    /// 모달 대화상자를 연 경우다 — 그 대화상자는 Win32 창이라 스냅샷에 안 보이고, 닫힐 때까지 호출자가 매달리면 안 되므로
    /// 성공(전달됨)으로 돌려주되 상황을 알린다.
    /// </summary>
    private static IpcMessage StillHandlingResponse(string messageId, string what, int timeoutMs)
    {
        return IpcSerializer.CreateResponse(messageId, new ActionResponse
        {
            Success = true,
            Pending = true,
            Message = $"{what} was delivered, but the application has not finished handling it after {timeoutMs} ms. " +
                      "It is most likely showing a modal dialog (a MessageBox is a Win32 window and does not appear in " +
                      "snapshots - use xapper_screenshot with mode=\"screen\" to see it). The person has to dismiss it; " +
                      "until then other calls may report the application as busy, and this action's own outcome is unknown."
        });
    }

    /// <summary>
    /// 참조 번호를 해석하고, 사라졌으면 무엇을 해야 하는지 알려주는 오류 응답을 <paramref name="error"/> 에 담습니다.
    /// 예외를 던지지 않는다: 주입된 프로세스 안에서 던지면 대상 앱의 first-chance 핸들러가 그 예외로
    /// 앱을 죽일 수 있다(결함 2). 오래된 ref 는 흔한 정상 경로이므로 호출자가 오류 응답으로 처리한다.
    /// </summary>
    /// <param name="elementRef">해석할 참조 번호.</param>
    /// <param name="messageId">응답에 실을 메시지 ID.</param>
    /// <param name="element">해석된 요소 (성공 시).</param>
    /// <param name="error">해석 실패 시 돌려줄 오류 응답.</param>
    /// <returns>해석에 성공하면 true.</returns>
    private bool TryResolveRef(
        int elementRef, string messageId,
        [NotNullWhen(true)] out DependencyObject? element,
        [NotNullWhen(false)] out IpcMessage? error)
    {
        element = _refRegistry.Resolve(elementRef);
        if (element is null)
        {
            error = StaleRefError(messageId, elementRef);
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>
    /// 참조 번호를 <see cref="UIElement"/> 로 해석합니다. 액션 실행기는 UIElement 를 요구하므로,
    /// 오래된 번호와 "UIElement 가 아님"을 한곳에서 걸러 오류 응답으로 돌려준다(예외를 던지지 않는다).
    /// </summary>
    /// <param name="elementRef">해석할 참조 번호.</param>
    /// <param name="messageId">응답에 실을 메시지 ID.</param>
    /// <param name="element">해석된 UIElement (성공 시).</param>
    /// <param name="error">해석 실패 시 돌려줄 오류 응답.</param>
    /// <returns>UIElement 로 해석되면 true.</returns>
    private bool TryResolveUiElement(
        int elementRef, string messageId,
        [NotNullWhen(true)] out UIElement? element,
        [NotNullWhen(false)] out IpcMessage? error)
    {
        element = null;

        if (!TryResolveRef(elementRef, messageId, out var resolved, out error))
            return false;

        element = resolved as UIElement;
        if (element is null)
        {
            error = IpcSerializer.CreateError(messageId,
                $"Element ref={elementRef} ({resolved.GetType().Name}) is not a UIElement, so this action " +
                "cannot be performed on it. Pick an interactive element from a snapshot.");
            return false;
        }

        error = null;
        return true;
    }

    /// <summary>
    /// 오래된 참조 번호에 대한, 왜 사라졌고 무엇을 해야 하는지 알려주는 오류 응답을 만듭니다.
    /// </summary>
    private static IpcMessage StaleRefError(string messageId, int elementRef)
    {
        return IpcSerializer.CreateError(messageId,
            $"Element ref={elementRef} cannot be resolved. Either xapper_snapshot ran after you obtained it " +
            "- that discards every earlier ref - or the element has since left the visual tree, which " +
            "happens to virtualized rows and closed dialogs. Take a fresh snapshot and use a ref from it. " +
            "xapper_find does not discard refs.");
    }

    /// <summary>
    /// 제한 시간 안에 요소가 상호작용 가능한 상태가 되지 않았을 때의 오류 응답을 만듭니다.
    /// </summary>
    private static IpcMessage NotReadyError(string messageId, int elementRef, int timeoutMs)
    {
        return IpcSerializer.CreateError(messageId,
            $"Element ref={elementRef} did not become visible, enabled and loaded within {timeoutMs} ms. " +
            "It may be disabled until a form validates, hidden behind another view, or still loading. " +
            "Re-read the state, or raise the timeout.");
    }

    /// <summary>
    /// 요청이 가리키는 요소의 참조 번호를 정합니다. ref 가 있으면 그대로 쓰고, 없으면 target selector 로
    /// 열려 있는 모든 창을 검색해 정확히 하나가 나올 때만 그 번호를 돌려준다. 0개·여러 개·잘못된 selector·둘 다 없음은
    /// 모두 흔한 정상 경로이므로 예외 대신 오류 응답으로 알린다(결함 2). 돌려준 번호는 레지스트리에 등록돼 있어
    /// 기존 <see cref="TryResolveRef"/> / <see cref="TryResolveUiElement"/> 에 그대로 넣을 수 있다.
    /// </summary>
    /// <param name="elementRef">요청에 실린 참조 번호. 있으면 target 은 보지 않는다.</param>
    /// <param name="target">요청에 실린 selector("id=…", "name=…", "text=…", "type=…", 콤마로 AND).</param>
    /// <param name="messageId">응답에 실을 메시지 ID.</param>
    /// <returns>정해진 참조 번호와, 실패했을 때의 오류 응답.</returns>
    private async Task<(int Ref, IpcMessage? Error)> ResolveTargetAsync(int? elementRef, string? target, string messageId)
    {
        if (elementRef is { } knownRef)
            return (knownRef, null);

        if (target is null)
            return (0, IpcSerializer.CreateError(messageId,
                "Pass ref (from a snapshot or find) or target (\"id=…\", \"name=…\", \"text=…\", \"type=…\", " +
                "comma-separated for AND)."));

        if (!TargetSelector.TryParse(target, out var query, out var parseError))
            return (0, IpcSerializer.CreateError(messageId, parseError));

        var matches = await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            var finder = new ElementFinder();
            var all = new List<ElementMatch>();

            // find 와 같은 범위를 훑는다: 팝업·메뉴·드롭다운도 각자 최상위 창이라 여기에 포함된다.
            foreach (var source in VisualRoots.Sources())
                all.AddRange(finder.Find(source.RootVisual, query, _refRegistry).Matches);

            return all;
        });

        if (matches.Count == 0)
            return (0, IpcSerializer.CreateError(messageId,
                $"No element matches target \"{target}\". Use xapper_find to see what exists, or pass ref."));

        if (matches.Count > 1)
            return (0, IpcSerializer.CreateError(messageId, AmbiguousTargetMessage(target, matches)));

        return (matches[0].Ref, null);
    }

    /// <summary>
    /// target 이 여러 요소에 맞았을 때, 후보를 ref 와 함께 나열해 selector 를 좁히거나 ref 를 고르게 하는 문구를 만듭니다.
    /// 목록은 10개까지만 싣는다.
    /// </summary>
    private static string AmbiguousTargetMessage(string target, List<ElementMatch> matches)
    {
        const int maxListed = 10;

        var listed = matches.Take(maxListed).Select(match =>
        {
            var text = $"[ref={match.Ref}] {match.Type}";
            if (!string.IsNullOrEmpty(match.Name))
                text += $" name=\"{match.Name}\"";
            if (!string.IsNullOrEmpty(match.AutomationId))
                text += $" id=\"{match.AutomationId}\"";
            if (!string.IsNullOrEmpty(match.Text))
                text += $" text=\"{match.Text}\"";
            return text;
        });

        var message = $"target \"{target}\" matches {matches.Count} elements - refine it or pass ref: " +
                      string.Join("; ", listed);
        if (matches.Count > maxListed)
            message += $"; … and {matches.Count - maxListed} more";
        return message;
    }

    /// <summary>
    /// 응답이 전송 상한을 넘었을 때 무엇을 줄여야 하는지 알려주는 오류를 만듭니다.
    /// </summary>
    private static IpcMessage TooLargeError(string id, int actualBytes)
    {
        return IpcSerializer.CreateError(id,
            $"Response is too large to send ({actualBytes} bytes; limit {IpcSerializer.MaxPayloadBytes}). " +
            "Request a smaller result: pass maxWidth to shrink a screenshot, or lower maxDepth for a snapshot.");
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
                "wheel" => await HandleWheel(message),
                "rightclick" => await HandleRightClick(message),
                "type" => await HandleType(message),
                "key" => await HandleKey(message),
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
                "elementAt" => await HandleElementAt(message),
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

        // rootRef 는 레지스트리를 비우기 전에 해석해야 한다. 순서가 뒤바뀌면 방금 돌려받은 번호까지
        // 사라져 rootRef 를 쓸 수 없다. 여기서(Clear 이전에) 미리 해석해 오래된 번호를 걸러낸다.
        DependencyObject? root = null;
        if (request.RootRef.HasValue)
        {
            if (!TryResolveRef(request.RootRef.Value, message.Id, out root, out var rootError))
                return rootError;
        }

        var snapshot = await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            _refRegistry.Clear();

            if (root is not null)
                return _treeWalker.Walk(root, _refRegistry, request.MaxDepth, skipped);

            // 열려 있는 최상위 창을 모두 순회한다. 다이얼로그뿐 아니라 팝업·메뉴·드롭다운도 여기에 포함된다.
            var roots = VisualRoots.Sources();
            if (roots.Count == 0)
                return null;

            // 창이 하나면 그대로 반환
            if (roots.Count == 1)
                return _treeWalker.Walk(roots[0].RootVisual, _refRegistry, request.MaxDepth, skipped);

            // 여러 윈도우면 가상 루트 아래에 배치
            var virtualRoot = new ElementSnapshot
            {
                Ref = 0,
                Type = "Application",
                IsEnabled = true,
                IsVisible = true
            };
            foreach (var source in roots)
            {
                virtualRoot.Children.Add(_treeWalker.Walk(source.RootVisual, _refRegistry, request.MaxDepth, skipped));
            }
            return virtualRoot;
        });

        if (snapshot is null)
            return IpcSerializer.CreateError(message.Id,
                "No window of this application is open. It may be starting up or closing.");

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

        if (!ModifierParser.TryParse(request.Modifiers, out var modifiers, out var modifierError))
            return IpcSerializer.CreateError(message.Id, modifierError);

        var (elementRef, targetError) = await ResolveTargetAsync(request.Ref, request.Target, message.Id);
        if (targetError is not null)
            return targetError;
        if (!TryResolveUiElement(elementRef, message.Id, out var element, out var error))
            return error;

        var waiter = new AutoWait.ElementWaiter(TimeSpan.FromMilliseconds(request.Timeout));
        if (!await waiter.WaitForReady(element))
            return NotReadyError(message.Id, elementRef, request.Timeout);

        var withMods = modifiers == ModifierKeys.None ? "" : $" with {request.Modifiers?.Trim()}";

        // 좌표 클릭: 먼저 후킹 경로로 실제 커서 없이 시도한다. 좌표 변환과 창 핸들은 UI 스레드에서 구하고,
        // WM 메시지 전송은 UI 스레드 밖에서(대상 창으로 크로스스레드) 돌린다.
        if (request.X.HasValue && request.Y.HasValue)
        {
            var target = await Application.Current.Dispatcher.InvokeAsync(
                () => (Hwnd: HwndHandleOf(element), Screen: MouseInput.ToScreenPoint(element, request.X.Value, request.Y.Value)));

            var hooked = request.DoubleClick
                ? SyntheticMouse.TryDoubleClick(target.Hwnd, target.Screen, modifiers)
                : SyntheticMouse.TryClick(target.Hwnd, target.Screen, modifiers);
            if (hooked)
            {
                var hookVerb = request.DoubleClick ? "Double-clicked" : "Clicked";
                var hookText = $"{hookVerb}{withMods} ref={elementRef} at ({request.X:F2},{request.Y:F2}) " +
                               "via synthetic mouse input (no cursor movement)";
                return IpcSerializer.CreateResponse(message.Id,
                    new ActionResponse { Success = true, Message = await SettleAsync(hookText, request.Timeout) });
            }
            // 후킹을 걸 수 없으면 아래의 기존 좌표 클릭(실제 입력)으로 폴백한다.
        }

        var (clickDone, outcome) = await RunOnUiThread(
            () => ClickAction.Execute(element, request.X, request.Y, request.DoubleClick, modifiers), request.Timeout);
        if (!clickDone)
            return StillHandlingResponse(message.Id, $"The click on ref={elementRef}", request.Timeout);

        var posInfo = request.X.HasValue && request.Y.HasValue
            ? $" at ({request.X:F2},{request.Y:F2})"
            : "";
        var verb = request.DoubleClick ? "Double-clicked" : "Clicked";
        var text = $"{verb}{withMods} ref={elementRef}{posInfo} via {outcome.Path}";
        if (outcome.Warning is not null)
            text += $" | {outcome.Warning}";

        var response = new ActionResponse { Success = true, Message = await SettleAsync(text, request.Timeout) };
        return IpcSerializer.CreateResponse(message.Id, response);
    }

    /// <summary>
    /// 요소 위 한 지점의 마우스 제스처를 후킹 경로로 먼저, 안 되면 실제 입력으로 수행합니다. 우클릭·휠이 같은
    /// 흐름(좌표 계산 → 후킹 → 전경 전환 + 실제 수식키 누름/뗌 → 경로·경고)을 쓰므로 한곳에 둔다.
    /// </summary>
    /// <param name="element">대상 요소.</param>
    /// <param name="rx">요소 내 상대 X.</param>
    /// <param name="ry">요소 내 상대 Y.</param>
    /// <param name="modifiers">함께 누를 수식키.</param>
    /// <param name="hooked">후킹 경로 제스처(창 핸들, 스크린 좌표 → 수행 여부).</param>
    /// <param name="real">실제 입력 제스처(스크린 좌표).</param>
    /// <param name="noun">경고 문구에 쓸 제스처 이름("click", "wheel").</param>
    /// <returns>수행 경로 설명과, 필요하면 경고.</returns>
    private static async Task<(string How, string? Warning)> RunPointGesture(
        UIElement element, double rx, double ry, ModifierKeys modifiers,
        Func<IntPtr, Point, bool> hooked, Action<Point> real, string noun)
    {
        var target = await Application.Current.Dispatcher.InvokeAsync(
            () => (Hwnd: HwndHandleOf(element), Screen: MouseInput.ToScreenPoint(element, rx, ry)));

        if (hooked(target.Hwnd, target.Screen))
            return ("synthetic mouse input (no cursor movement)", null);

        var activated = await Application.Current.Dispatcher.InvokeAsync(() => MouseInput.BringToForeground(element));
        RealModifierKeys.Press(modifiers);
        try
        {
            real(target.Screen);
        }
        finally
        {
            RealModifierKeys.Release(modifiers);
        }

        var warning = activated
            ? null
            : $"the target window could not be brought to the foreground, so the {noun} may not have reached the control. Bring the window to the front and retry.";
        return ("real mouse input", warning);
    }

    /// <summary>
    /// 요소 위 한 지점을 우클릭합니다. 항상 좌표 제스처이며(접근성에 우클릭 패턴이 없다), 먼저 후킹 경로로
    /// 커서 없이 시도하고 안 되면 실제 마우스 입력으로 폴백한다. 실제 입력일 때의 수식키는 실제 키로 누르고
    /// finally 로 뗀다.
    /// </summary>
    private async Task<IpcMessage> HandleRightClick(IpcMessage message)
    {
        var request = IpcSerializer.DeserializePayload<RightClickRequest>(message.Payload!.Value);

        if (!ModifierParser.TryParse(request.Modifiers, out var modifiers, out var modifierError))
            return IpcSerializer.CreateError(message.Id, modifierError);

        var (elementRef, targetError) = await ResolveTargetAsync(request.Ref, request.Target, message.Id);
        if (targetError is not null)
            return targetError;
        if (!TryResolveUiElement(elementRef, message.Id, out var element, out var error))
            return error;

        var waiter = new AutoWait.ElementWaiter(TimeSpan.FromMilliseconds(request.Timeout));
        if (!await waiter.WaitForReady(element))
            return NotReadyError(message.Id, elementRef, request.Timeout);

        var rx = request.X ?? 0.5;
        var ry = request.Y ?? 0.5;

        var withMods = modifiers == ModifierKeys.None ? "" : $" with {request.Modifiers?.Trim()}";
        var (how, warning) = await RunPointGesture(element, rx, ry, modifiers,
            (hwnd, screen) => SyntheticMouse.TryRightClick(hwnd, screen, modifiers),
            screen => MouseInput.RightClickAt(screen),
            "click");

        var text = $"Right-clicked{withMods} ref={elementRef} at ({rx:F2},{ry:F2}) via {how}";
        if (warning is not null)
            text += " | WARNING: " + warning;

        return IpcSerializer.CreateResponse(message.Id,
            new ActionResponse { Success = true, Message = await SettleAsync(text, request.Timeout) });
    }

    /// <summary>
    /// 요소 위 한 지점에서 휠을 굴립니다. 먼저 후킹 경로로 커서 없이 시도하고 안 되면 실제 입력으로 폴백한다.
    /// scroll 과 달리 실제 휠 제스처라 Ctrl+휠 줌·커스텀 MouseWheel 핸들러를 건드린다.
    /// </summary>
    private async Task<IpcMessage> HandleWheel(IpcMessage message)
    {
        var request = IpcSerializer.DeserializePayload<WheelRequest>(message.Payload!.Value);

        if (request.Notches == 0)
            return IpcSerializer.CreateError(message.Id,
                "notches must be non-zero: positive rolls up (away from you), negative rolls down.");

        if (request.Notches is < -100 or > 100)
            return IpcSerializer.CreateError(message.Id,
                "notches must be between -100 and 100. Roll in several calls for a longer scroll.");

        if (!ModifierParser.TryParse(request.Modifiers, out var modifiers, out var modifierError))
            return IpcSerializer.CreateError(message.Id, modifierError);

        var (elementRef, targetError) = await ResolveTargetAsync(request.Ref, request.Target, message.Id);
        if (targetError is not null)
            return targetError;
        if (!TryResolveUiElement(elementRef, message.Id, out var element, out var error))
            return error;

        var waiter = new AutoWait.ElementWaiter(TimeSpan.FromMilliseconds(request.Timeout));
        if (!await waiter.WaitForReady(element))
            return NotReadyError(message.Id, elementRef, request.Timeout);

        var rx = request.X ?? 0.5;
        var ry = request.Y ?? 0.5;

        var withMods = modifiers == ModifierKeys.None ? "" : $" with {request.Modifiers?.Trim()}";
        var (how, warning) = await RunPointGesture(element, rx, ry, modifiers,
            (hwnd, screen) => SyntheticMouse.TryWheel(hwnd, screen, request.Notches, modifiers),
            screen => MouseInput.WheelAt(screen, request.Notches),
            "wheel");

        var direction = request.Notches > 0 ? "up" : "down";
        var text = $"Wheeled {Math.Abs(request.Notches)} notch(es) {direction}{withMods} on ref={elementRef} at ({rx:F2},{ry:F2}) via {how}";
        if (warning is not null)
            text += " | WARNING: " + warning;

        return IpcSerializer.CreateResponse(message.Id,
            new ActionResponse { Success = true, Message = await SettleAsync(text, request.Timeout) });
    }

    /// <summary>
    /// 요소가 속한 최상위 창(HwndSource)의 핸들을 돌려줍니다. 팝업·메뉴·드롭다운은 각자 HwndSource 라
    /// 그 자신의 핸들이 나온다. 소스가 없으면 IntPtr.Zero. UI 스레드에서 호출해야 한다.
    /// </summary>
    private static IntPtr HwndHandleOf(DependencyObject element)
    {
        return PresentationSource.FromDependencyObject(element) is HwndSource source
            ? source.Handle
            : IntPtr.Zero;
    }

    /// <summary>
    /// 지정된 요소에 텍스트를 입력합니다.
    /// </summary>
    private async Task<IpcMessage> HandleType(IpcMessage message)
    {
        var request = IpcSerializer.DeserializePayload<TypeTextRequest>(message.Payload!.Value);

        // ref 도 target 도 없으면 현재 포커스 요소에 키 입력으로 타이핑한다(F2 로 연 인라인 편집기처럼 스냅샷에 없는 편집기).
        if (request.Ref is null && request.Target is null)
        {
            var (focusedDone, focusedResult) = await RunOnUiThread(
                () => TypeAction.ExecuteOnFocused(request.Text, request.Clear), request.Timeout);
            if (!focusedDone)
                return StillHandlingResponse(message.Id, "The typed text", request.Timeout);
            if (focusedResult.Error is { } focusedReason)
                return IpcSerializer.CreateError(message.Id, focusedReason);

            return IpcSerializer.CreateResponse(message.Id, new ActionResponse
            {
                Success = true,
                Message = await SettleAsync("Typed into the focused element as keystrokes", request.Timeout)
            });
        }

        var (elementRef, targetError) = await ResolveTargetAsync(request.Ref, request.Target, message.Id);
        if (targetError is not null)
            return targetError;
        if (!TryResolveUiElement(elementRef, message.Id, out var element, out var error))
            return error;

        var waiter = new AutoWait.ElementWaiter(TimeSpan.FromMilliseconds(request.Timeout));
        if (!await waiter.WaitForReady(element))
            return NotReadyError(message.Id, elementRef, request.Timeout);

        var (typeDone, result) = await RunOnUiThread(
            () => TypeAction.Execute(element, request.Text, request.Clear), request.Timeout);
        if (!typeDone)
            return StillHandlingResponse(message.Id, $"The typed text for ref={elementRef}", request.Timeout);
        if (result.Error is { } reason)
            return IpcSerializer.CreateError(message.Id, reason);

        var response = new ActionResponse
        {
            Success = true,
            Message = await SettleAsync($"Typed into ref={elementRef}", request.Timeout)
        };
        return IpcSerializer.CreateResponse(message.Id, response);
    }

    /// <summary>
    /// 키보드 포커스 요소(또는 지정 요소)에 키를 넣습니다. 전경 창과 무관하게 대상 앱 안에서 라우팅한다.
    /// </summary>
    private async Task<IpcMessage> HandleKey(IpcMessage message)
    {
        var request = IpcSerializer.DeserializePayload<KeyRequest>(message.Payload!.Value);

        if (!ModifierParser.TryParse(request.Modifiers, out var modifiers, out var modifierError))
            return IpcSerializer.CreateError(message.Id, modifierError);

        // ref 도 target 도 없으면 현재 포커스 요소에 보낸다.
        DependencyObject? refElement = null;
        if (request.Ref is not null || request.Target is not null)
        {
            var (elementRef, targetError) = await ResolveTargetAsync(request.Ref, request.Target, message.Id);
            if (targetError is not null)
                return targetError;
            if (!TryResolveRef(elementRef, message.Id, out refElement, out var error))
                return error;

            var waiter = new AutoWait.ElementWaiter(TimeSpan.FromMilliseconds(request.Timeout));
            if (!await waiter.WaitForReady(refElement))
                return NotReadyError(message.Id, elementRef, request.Timeout);
        }

        var (keyDone, keyResult) = await RunOnUiThread(
            () => KeyboardInput.Send(refElement, request.Key, modifiers), request.Timeout);
        if (!keyDone)
        {
            // 핸들러가 모달 대화상자 안에 머물러 Send 의 finally 가 아직 안 돌았다. 대화상자가 떠 있는 동안 수식키가
            // 눌린 것으로 남으면 사람의 입력이 오염되므로 여기서 스푸프를 푼다(나중에 finally 가 다시 풀어도 무해).
            InputSpoof.EndModifiers();
            return StillHandlingResponse(message.Id, $"The key {request.Key}", request.Timeout);
        }
        var (keyError, focused, modifierPath) = keyResult;
        if (keyError is not null)
            return IpcSerializer.CreateError(message.Id, keyError);

        var keyText = string.IsNullOrWhiteSpace(request.Modifiers) ? request.Key : $"{request.Modifiers.Trim()}+{request.Key}";
        var text = $"{keyText} sent -> {focused} now focused";
        if (modifierPath.Length > 0)
            text += $" ({modifierPath})";
        return IpcSerializer.CreateResponse(message.Id,
            new ActionResponse { Success = true, Message = await SettleAsync(text, request.Timeout) });
    }

    /// <summary>
    /// Selector 컨트롤에서 항목을 선택합니다.
    /// </summary>
    private async Task<IpcMessage> HandleSelect(IpcMessage message)
    {
        var request = IpcSerializer.DeserializePayload<SelectRequest>(message.Payload!.Value);

        var (elementRef, targetError) = await ResolveTargetAsync(request.Ref, request.Target, message.Id);
        if (targetError is not null)
            return targetError;
        if (!TryResolveUiElement(elementRef, message.Id, out var element, out var error))
            return error;

        var waiter = new AutoWait.ElementWaiter(TimeSpan.FromMilliseconds(request.Timeout));
        if (!await waiter.WaitForReady(element))
            return NotReadyError(message.Id, elementRef, request.Timeout);

        var (selectDone, result) = await RunOnUiThread(
            () => SelectAction.Execute(element, request.ItemText, request.ItemIndex), request.Timeout);
        if (!selectDone)
            return StillHandlingResponse(message.Id, $"The selection on ref={elementRef}", request.Timeout);
        if (result.Error is { } reason)
            return IpcSerializer.CreateError(message.Id, reason);

        var response = new ActionResponse
        {
            Success = true,
            Message = await SettleAsync($"Selected item in ref={elementRef}", request.Timeout)
        };
        return IpcSerializer.CreateResponse(message.Id, response);
    }

    /// <summary>
    /// CheckBox/ToggleButton의 토글 상태를 전환합니다.
    /// </summary>
    private async Task<IpcMessage> HandleToggle(IpcMessage message)
    {
        var request = IpcSerializer.DeserializePayload<ToggleRequest>(message.Payload!.Value);

        var (elementRef, targetError) = await ResolveTargetAsync(request.Ref, request.Target, message.Id);
        if (targetError is not null)
            return targetError;
        if (!TryResolveUiElement(elementRef, message.Id, out var element, out var error))
            return error;

        var waiter = new AutoWait.ElementWaiter(TimeSpan.FromMilliseconds(request.Timeout));
        if (!await waiter.WaitForReady(element))
            return NotReadyError(message.Id, elementRef, request.Timeout);

        var (toggleDone, result) = await RunOnUiThread(() => ToggleAction.Execute(element), request.Timeout);
        if (!toggleDone)
            return StillHandlingResponse(message.Id, $"The toggle on ref={elementRef}", request.Timeout);
        if (result.Error is { } reason)
            return IpcSerializer.CreateError(message.Id, reason);

        var response = new ActionResponse
        {
            Success = true,
            Message = await SettleAsync($"Toggled ref={elementRef}", request.Timeout)
        };
        return IpcSerializer.CreateResponse(message.Id, response);
    }

    /// <summary>
    /// TreeViewItem/Expander를 확장 또는 축소합니다.
    /// </summary>
    private async Task<IpcMessage> HandleExpand(IpcMessage message)
    {
        var request = IpcSerializer.DeserializePayload<ExpandRequest>(message.Payload!.Value);

        var (elementRef, targetError) = await ResolveTargetAsync(request.Ref, request.Target, message.Id);
        if (targetError is not null)
            return targetError;
        if (!TryResolveUiElement(elementRef, message.Id, out var element, out var error))
            return error;

        var waiter = new AutoWait.ElementWaiter(TimeSpan.FromMilliseconds(request.Timeout));
        if (!await waiter.WaitForReady(element))
            return NotReadyError(message.Id, elementRef, request.Timeout);

        var (expandDone, result) = await RunOnUiThread(
            () => ExpandAction.Execute(element, request.Expand), request.Timeout);
        if (!expandDone)
            return StillHandlingResponse(message.Id, $"The expand/collapse on ref={elementRef}", request.Timeout);
        if (result.Error is { } reason)
            return IpcSerializer.CreateError(message.Id, reason);

        var action = request.Expand ? "Expanded" : "Collapsed";
        var response = new ActionResponse
        {
            Success = true,
            Message = await SettleAsync($"{action} ref={elementRef}", request.Timeout)
        };
        return IpcSerializer.CreateResponse(message.Id, response);
    }

    /// <summary>
    /// ScrollViewer의 스크롤 위치를 변경합니다.
    /// </summary>
    private async Task<IpcMessage> HandleScroll(IpcMessage message)
    {
        var request = IpcSerializer.DeserializePayload<ScrollRequest>(message.Payload!.Value);

        var (elementRef, targetError) = await ResolveTargetAsync(request.Ref, request.Target, message.Id);
        if (targetError is not null)
            return targetError;
        if (!TryResolveUiElement(elementRef, message.Id, out var element, out var error))
            return error;

        var waiter = new AutoWait.ElementWaiter(TimeSpan.FromMilliseconds(request.Timeout));
        if (!await waiter.WaitForReady(element))
            return NotReadyError(message.Id, elementRef, request.Timeout);

        var (scrollDone, result) = await RunOnUiThread(
            () => ScrollAction.Execute(element, request.HorizontalPercent, request.VerticalPercent), request.Timeout);
        if (!scrollDone)
            return StillHandlingResponse(message.Id, $"The scroll on ref={elementRef}", request.Timeout);
        if (result.Error is { } reason)
            return IpcSerializer.CreateError(message.Id, reason);

        var response = new ActionResponse
        {
            Success = true,
            Message = await SettleAsync($"Scrolled ref={elementRef}", request.Timeout)
        };
        return IpcSerializer.CreateResponse(message.Id, response);
    }

    /// <summary>
    /// 요소 사이 또는 화면 좌표 사이를 마우스 버튼을 누른 채 드래그합니다.
    /// 시작 지점이 Thumb 이면 그 컨트롤의 드래그 이벤트로 처리해 실제 입력을 쓰지 않는다.
    /// 그 외에는 좌표 변환과 포그라운드 전환만 UI 스레드에서 수행하고 입력 주입은 UI 스레드 밖에서 실행하여,
    /// 드래그가 시작된 뒤 대상 앱이 중첩 메시지 루프를 돌 수 있게 한다.
    /// </summary>
    private async Task<IpcMessage> HandleDrag(IpcMessage message)
    {
        if (message.Payload is null)
            throw new InvalidOperationException("drag request requires a payload.");

        var request = IpcSerializer.DeserializePayload<DragRequest>(message.Payload.Value);

        if (!TryResolveDragElement(request.SourceRef, message.Id, out var source, out var dragError))
            return dragError;
        if (!TryResolveDragElement(request.TargetRef, message.Id, out var target, out dragError))
            return dragError;

        var waiter = new AutoWait.ElementWaiter(TimeSpan.FromMilliseconds(request.Timeout));
        // source/target 이 null 이 아니라는 것은 해당 ref 가 지정됐다는 뜻이다(GetValueOrDefault 는 그 실제 값).
        if (source is not null && !await waiter.WaitForReady(source))
            return NotReadyError(message.Id, request.SourceRef.GetValueOrDefault(), request.Timeout);
        if (target is not null && !await waiter.WaitForReady(target))
            return NotReadyError(message.Id, request.TargetRef.GetValueOrDefault(), request.Timeout);

        // 좌표가 덜 지정된 드래그는 예외 대신 여기서 오류로 돌려준다(결함 2): 좌표 계산은 UI 스레드 안에서
        // 일어나므로 그 안에서 던지면 대상 앱을 죽일 수 있다. 아래 검사는 ResolveStartPoint/ResolveEndPoint 의
        // 조건과 같으며, 그 둘의 throw 를 여기서 앞질러 도달 불가로 만든다.
        if (source is null && (!request.SourceX.HasValue || !request.SourceY.HasValue))
            return IpcSerializer.CreateError(message.Id,
                "Drag requires sourceRef, or both sourceX and sourceY as screen coordinates.");
        if (target is null && !request.OffsetX.HasValue && !request.OffsetY.HasValue
            && (!request.TargetX.HasValue || !request.TargetY.HasValue))
            return IpcSerializer.CreateError(message.Id,
                "Drag requires targetRef, offsetX/offsetY, or both targetX and targetY as screen coordinates.");

        if (!ModifierParser.TryParse(request.Modifiers, out var modifiers, out var modifierError))
            return IpcSerializer.CreateError(message.Id, modifierError);

        var (dragResolved, outcome) = await RunOnUiThread(() =>
        {
            var start = ResolveStartPoint(source, request);
            var end = ResolveEndPoint(target, request, start);

            // 스플리터·슬라이더·스크롤바는 Thumb 의 드래그 이벤트로 움직인다. 그 이벤트는 이동량을 숫자로
            // 받으므로 커서를 옮기지 않고, 실제 마우스보다 오히려 정확하게 조작할 수 있다.
            if (source is not null && ThumbDrag.FindAt(source, start) is { } thumb)
            {
                // 이동량은 반드시 그 Thumb 의 좌표계로 재야 한다. 출발 요소 기준으로 재면 중간에 배율이
                // 걸려 있을 때(예: Viewbox) 그만큼 어긋난다.
                var grab = thumb.PointFromScreen(start);
                ThumbDrag.Perform(thumb, thumb.PointFromScreen(end) - grab, grab);
                return (Start: start, End: end, UsedThumb: true, Hwnd: IntPtr.Zero);
            }

            // 후킹 드래그를 걸 대상 창. source(없으면 target)의 HwndSource 를 UI 스레드에서 구한다.
            var hwnd = source is not null ? HwndHandleOf(source)
                     : target is not null ? HwndHandleOf(target)
                     : IntPtr.Zero;
            return (Start: start, End: end, UsedThumb: false, Hwnd: hwnd);
        }, request.Timeout);
        if (!dragResolved)
            return StillHandlingResponse(message.Id, "The drag", request.Timeout);

        var withMods = modifiers == ModifierKeys.None ? "" : $" with {request.Modifiers?.Trim()}";

        string how;
        string? warning = null;
        if (outcome.UsedThumb)
        {
            how = "its own drag events (no real input)";
            if (modifiers != ModifierKeys.None)
                warning = "modifiers do not apply to a thumb drag (the control's own drag events were used) and were ignored.";
        }
        else if (SyntheticMouse.TryDrag(outcome.Hwnd, outcome.Start, outcome.End, modifiers))
        {
            // 후킹 경로: 커서를 옮기지 않고 대상 창에 드래그를 보낸다.
            how = "synthetic mouse input (no cursor movement)";
        }
        else
        {
            // 후킹을 걸 수 없으면 실제 마우스 입력으로 폴백한다(Step 3에서 알림 2단계로 대체).
            bool activated;
            if ((source ?? target) is { } anchor)
                activated = await Application.Current.Dispatcher.InvokeAsync(() => MouseInput.BringToForeground(anchor));
            else
                activated = true;

            RealModifierKeys.Press(modifiers);
            try
            {
                await Actions.DragAction.ExecuteAsync(outcome.Start, outcome.End);
            }
            finally
            {
                RealModifierKeys.Release(modifiers);
            }
            how = "real mouse input";
            if (!activated)
                warning = "the target window could not be brought to the foreground. The first input may have " +
                          "been consumed by window activation, so the drag may not have reached the control. " +
                          "Bring the window to the front and retry.";
        }

        var text = $"Dragged{withMods} ({outcome.Start.X:F0},{outcome.Start.Y:F0}) -> ({outcome.End.X:F0},{outcome.End.Y:F0}) via {how}";
        if (warning is not null)
            text += " | WARNING: " + warning;

        var response = new ActionResponse { Success = true, Message = await SettleAsync(text, request.Timeout) };
        return IpcSerializer.CreateResponse(message.Id, response);
    }

    /// <summary>
    /// 드래그용 참조 번호를 UIElement로 해석합니다. 번호가 없으면 element 는 null 이고 성공으로 취급한다
    /// (출발/도착을 화면 좌표로 지정하는 경우). 오래된 번호나 UIElement 가 아닌 요소는 예외 대신
    /// <paramref name="error"/> 로 알린다(결함 2).
    /// </summary>
    /// <param name="elementRef">해석할 참조 번호. null 이면 좌표 기반.</param>
    /// <param name="messageId">응답에 실을 메시지 ID.</param>
    /// <param name="element">해석된 요소, 또는 번호가 없으면 null.</param>
    /// <param name="error">해석 실패 시 돌려줄 오류 응답.</param>
    /// <returns>번호가 없거나 UIElement 로 해석되면 true.</returns>
    private bool TryResolveDragElement(
        int? elementRef, string messageId,
        out UIElement? element,
        [NotNullWhen(false)] out IpcMessage? error)
    {
        element = null;
        error = null;

        if (!elementRef.HasValue)
            return true;

        if (!TryResolveRef(elementRef.Value, messageId, out var resolved, out error))
            return false;

        element = resolved as UIElement;
        if (element is null)
        {
            error = IpcSerializer.CreateError(messageId,
                $"Element ref={elementRef} is not a UIElement, so it cannot be a drag source or target.");
            return false;
        }

        return true;
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

        var (elementRef, targetError) = await ResolveTargetAsync(request.Ref, request.Target, message.Id);
        if (targetError is not null)
            return targetError;
        if (!TryResolveRef(elementRef, message.Id, out var element, out var error))
            return error;

        var result = await Application.Current.Dispatcher.InvokeAsync(
            () => PropertyReader.ReadProperty(element, request.PropertyName));
        if (result.Response is not { } response)
            return IpcSerializer.CreateError(message.Id, result.Error ?? "property read failed");

        response.Ref = elementRef;
        return IpcSerializer.CreateResponse(message.Id, response);
    }

    /// <summary>
    /// 요소의 데이터 바인딩 정보를 조회합니다.
    /// </summary>
    private async Task<IpcMessage> HandleGetBindings(IpcMessage message)
    {
        var request = IpcSerializer.DeserializePayload<GetBindingsRequest>(message.Payload!.Value);
        if (!TryResolveRef(request.Ref, message.Id, out var element, out var error))
            return error;

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

        // ref 는 렌더가 필요한 요소여야 한다. ref 없음(전체 창)과 오래된 ref 를 구분해 걸러낸다.
        UIElement? target = null;
        if (request.Ref.HasValue)
        {
            if (!TryResolveRef(request.Ref.Value, message.Id, out var resolved, out var refError))
                return refError;

            target = resolved as UIElement;
            if (target is null)
                return IpcSerializer.CreateError(message.Id,
                    $"Element ref={request.Ref} cannot be captured because it is not a UIElement. " +
                    "Pick an element that renders, or omit ref to capture the whole window.");
        }

        // 모드 검증은 문자열 비교뿐이라 UI 스레드가 필요 없다. 잘못된 모드는 예외 대신 여기서 오류로 돌려준다.
        var mode = string.IsNullOrWhiteSpace(request.Mode) ? ScreenshotModes.Render : request.Mode.Trim();
        var isScreen = mode.Equals(ScreenshotModes.Screen, StringComparison.OrdinalIgnoreCase);
        var isRender = mode.Equals(ScreenshotModes.Render, StringComparison.OrdinalIgnoreCase);
        if (!isScreen && !isRender)
            return IpcSerializer.CreateError(message.Id,
                $"Unknown screenshot mode \"{mode}\". Use \"{ScreenshotModes.Render}\" to redraw the visual " +
                $"tree, or \"{ScreenshotModes.Screen}\" to read what is on the desktop.");

        var response = await Application.Current.Dispatcher.InvokeAsync(() => isScreen
            ? CaptureFromScreen(target, request.MaxWidth)
            : CaptureByRendering(target, request.MaxWidth));

        return IpcSerializer.CreateResponse(message.Id, response);
    }

    /// <summary>
    /// 시각 트리를 다시 그려 캡처합니다. 다른 창이 열려 있으면 이 그림에 담기지 않았다는 사실을 함께 알립니다.
    /// </summary>
    private static ScreenshotResponse CaptureByRendering(UIElement? element, int? maxWidth)
    {
        var response = element is not null
            ? RenderCapture.CaptureElement(element, maxWidth)
            : RenderCapture.CaptureWindow(maxWidth: maxWidth);

        // 렌더 방식은 시각 트리 하나만 그린다. 별도 창은 물론이고 팝업·메뉴·드롭다운도 자기 HWND에 살아 담기지 않는다.
        // 그래서 Window 목록이 아니라 최상위 창 개수로 센다 — 팝업은 Window가 아니라서 그 목록에 나타나지 않는다.
        var otherWindows = DesktopCapture.CountVisibleWindows() - 1;
        if (otherWindows > 0)
            response.Warning =
                $"{otherWindows} other window(s) are open and are not in this image. Popups, context menus and " +
                $"drop-downs are never in it either. Use mode=\"{ScreenshotModes.Screen}\" to capture what is " +
                "actually on the desktop.";

        return response;
    }

    /// <summary>
    /// 화면에 합성된 픽셀을 그대로 읽어 캡처합니다. 대상 창이 앞에 없으면 다른 창이 덮였을 수 있다고 알립니다.
    /// </summary>
    private static ScreenshotResponse CaptureFromScreen(UIElement? element, int? maxWidth)
    {
        var response = element is not null
            ? DesktopCapture.CaptureElement(element, maxWidth)
            : DesktopCapture.CaptureApplicationWindows(maxWidth);

        if (!DesktopCapture.IsAnyWindowInForeground())
            response.Warning =
                "None of the application's windows is in the foreground, so another application may be covering " +
                "the captured region in this image. Bring the window to the front and capture again, or use " +
                $"mode=\"{ScreenshotModes.Render}\", which does not depend on what is on screen.";

        return response;
    }

    /// <summary>
    /// 프로퍼티 값이 기대값과 일치하는지 검증합니다.
    /// </summary>
    private async Task<IpcMessage> HandleAssert(IpcMessage message)
    {
        var request = IpcSerializer.DeserializePayload<AssertRequest>(message.Payload!.Value);

        var (elementRef, targetError) = await ResolveTargetAsync(request.Ref, request.Target, message.Id);
        if (targetError is not null)
            return targetError;
        if (!TryResolveRef(elementRef, message.Id, out var element, out var error))
            return error;

        var read = await Application.Current.Dispatcher.InvokeAsync(
            () => PropertyReader.ReadProperty(element, request.Property));

        // 속성을 읽지 못한 것(오타·경로 표기)은 통과/실패가 아니라 오류다. FAIL 로 뭉개면 값이 달랐던
        // 것처럼 읽혀 원인을 엉뚱한 곳에서 찾게 된다.
        if (read.Response is not { } prop)
            return IpcSerializer.CreateError(message.Id, read.Error ?? "property read failed");

        var actual = prop.Value ?? "null";
        var passed = actual.Equals(request.Expected, StringComparison.OrdinalIgnoreCase);

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
    /// 화면 좌표에 실제로 그려져 있는 요소와 그 조상들을 조회합니다.
    /// 이름도 AutomationId도 없어 검색으로 찾을 수 없는 컨트롤을, 보이는 위치로 지목해 참조를 얻는 길이다.
    /// 히트테스트만 하므로 커서도 포커스도 건드리지 않는다.
    /// </summary>
    private async Task<IpcMessage> HandleElementAt(IpcMessage message)
    {
        if (message.Payload is null)
            throw new InvalidOperationException("elementAt request requires a payload.");

        var request = IpcSerializer.DeserializePayload<ElementAtRequest>(message.Payload.Value);

        var (response, resultError) = await Application.Current.Dispatcher.InvokeAsync<(FindElementResponse?, string?)>(() =>
        {
            var screenPoint = new Point(request.X, request.Y);

            var source = VisualRoots.SourceAt(screenPoint);
            if (source is null)
                return (null,
                    $"No window of this application is on top at ({request.X:F0},{request.Y:F0}). " +
                    "Another application may be covering that point, or the coordinates may be outside the window. " +
                    "Take a screenshot with mode=\"screen\" and read the coordinates from it.");

            var elements = VisualRoots.ElementsAt(source, screenPoint, request.MaxAncestors);
            if (elements.Count == 0)
                return (null,
                    $"Nothing at ({request.X:F0},{request.Y:F0}) takes hit-testing. The point may be over an " +
                    "element with no brush behind it, one with IsHitTestVisible off, or the window border " +
                    "rather than its content.");

            var found = new FindElementResponse();
            foreach (var element in elements)
            {
                found.Matches.Add(new ElementMatch
                {
                    Ref = _refRegistry.Register(element),
                    Type = element.GetType().Name,
                    Name = (element as FrameworkElement)?.Name,
                    AutomationId = System.Windows.Automation.AutomationProperties.GetAutomationId(element),
                    Text = VisualTree.ElementText.Of(element)
                });
            }
            return (found, null);
        });

        if (response is null)
            return IpcSerializer.CreateError(message.Id, resultError ?? "no element at that point");

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

            // 열려 있는 최상위 창을 모두 검색한다. 팝업·메뉴·드롭다운도 각자 최상위 창이라 여기에 포함된다.
            foreach (var source in VisualRoots.Sources())
            {
                var result = finder.Find(source.RootVisual, request, _refRegistry);
                allResults.Matches.AddRange(result.Matches);
                allResults.SkippedNodes.AddRange(result.SkippedNodes);
            }

            return allResults;
        });

        return IpcSerializer.CreateResponse(message.Id, response);
    }

    #endregion
}

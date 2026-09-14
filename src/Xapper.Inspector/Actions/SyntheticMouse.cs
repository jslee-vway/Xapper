using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Threading;

namespace Xapper.Inspector.Actions;

/// <summary>후킹 경로 제스처의 결과.</summary>
internal enum GestureResult
{
    /// <summary>후크를 쓸 수 없거나 같은 앱의 창이 덮고 있어 보내지 않았다 — 호출자가 실제 입력으로 폴백한다.</summary>
    Unavailable,

    /// <summary>모든 메시지를 보냈고 앱이 제때 처리했다.</summary>
    Delivered,

    /// <summary>메시지는 보냈지만 앱이 제한 시간 안에 처리하지 못해 스푸프를 일찍 껐다(느린 핸들러 또는 모달 대화상자). 결과는 아직 알 수 없다.</summary>
    Stalled,
}

/// <summary>
/// 실제 커서를 옮기지 않고 대상 창에 마우스 제스처(클릭·더블클릭·우클릭·휠·드래그)를 전달하는 정적 클래스.
/// 입력 상태(<c>GetKeyState</c>/<c>GetCursorPos</c> 등)는 <see cref="InputSpoof"/> 가 스푸프하고, 여기서는
/// 그 스푸프를 켠 채 창에 WM 마우스 메시지를 보낸다. 커서는 한 픽셀도 움직이지 않고, 다른 프로세스의
/// 포커스도 뺏지 않는다. 후크를 설치할 수 없으면 Unavailable 을 돌려 호출자가 실제 입력으로 폴백한다.
/// </summary>
internal static class SyntheticMouse
{
    #region Fields

    private const uint WM_MOUSEMOVE = 0x0200;
    private const uint WM_LBUTTONDOWN = 0x0201;
    private const uint WM_LBUTTONUP = 0x0202;
    private const int MK_LBUTTON = 0x0001;
    private const uint WM_RBUTTONDOWN = 0x0204;
    private const uint WM_RBUTTONUP = 0x0205;
    private const uint WM_MOUSEWHEEL = 0x020A;
    private const int MK_RBUTTON = 0x0002;
    private const int MK_SHIFT = 0x0004;
    private const int MK_CONTROL = 0x0008;

    /// <summary>휠 한 눈금의 delta(Win32 WHEEL_DELTA).</summary>
    private const int WheelDelta = 120;

    /// <summary>WM 메시지 사이 간격 (밀리초). 대상 앱이 눌림을 처리한 뒤 떼기를 받도록 여유를 준다.</summary>
    private const int StepDelayMs = 16;

    /// <summary>드래그를 나누어 보낼 이동 횟수.</summary>
    private const int DragSteps = 8;

    /// <summary>
    /// WM 메시지 하나가 대상 UI 스레드에서 처리되기를 기다리는 한도 (밀리초). 이 안에 끝나지 않으면 앱 핸들러가 느리거나
    /// 모달 대화상자를 연 것이다 — 그때는 기다리지 않고 <b>스푸프를 즉시 끈다</b>. 스푸프가 켜진 동안 사람의 진짜 마우스가
    /// 그 앱에서 오작동하므로(측정: 2초 핸들러 동안 프로세스 전체가 가짜 커서를 봤다) 마스킹을 이 시간으로 묶는다.
    /// Down 은 이미 처리됐고 Up 은 자기 좌표로 라우팅되므로 클릭은 그래도 완성된다. 드래그는 다르다: 스푸프가 꺼진 뒤의
    /// 이동·뗌은 진짜 버튼 상태(안 눌림)로 보이므로 그 시점에 드래그가 끝난다 — 응답이 그 사실을 알린다. OLE 드래그앤드롭
    /// (<c>DragDrop.DoDragDrop</c>)은 실제 커서를 추적하므로 워치독과 무관하게 무입력으로는 완성되지 않는다(측정 확인).
    /// 스푸프를 끄는 것과 응답을 돌려주는 것은 별개다: 호출자는 <see cref="WaitUntilHandledAsync"/> 로 핸들러가 끝나기를
    /// 자기 제한 시간까지 더 기다린 뒤에야 "아직 처리 중" 으로 응답한다.
    /// </summary>
    internal const int StallTimeoutMs = 250;

    /// <summary>현재 제스처에서 한 메시지라도 제한 시간 안에 처리되지 않았는지. Gate 안에서만 쓴다.</summary>
    private static bool _stalled;

    /// <summary><see cref="WaitUntilHandledAsync"/> 가 핸들러 종료를 다시 묻는 간격 (밀리초).</summary>
    private const int HandledPollMs = 50;

    /// <summary>SendMessageTimeout 이 제한 시간 때문에 0 을 돌려줬을 때의 Win32 오류 코드.</summary>
    private const int ErrorTimeout = 1460;

    private static readonly object Gate = new();

    #endregion

    #region Public Methods

    /// <summary>지정된 창의 한 지점을 커서 이동 없이 클릭합니다.</summary>
    /// <param name="hwnd">대상 요소가 속한 최상위 창(HwndSource) 핸들.</param>
    /// <param name="screen">클릭할 스크린 디바이스 좌표.</param>
    /// <param name="modifiers">함께 눌린 것으로 볼 수식키.</param>
    /// <returns>후킹 경로로 보냈으면 Delivered(앱이 제때 처리) 또는 Stalled(앱이 느리거나 모달을 열어 스푸프를 일찍 끔), 후크를 쓸 수 없으면 Unavailable.</returns>
    public static GestureResult TryClick(IntPtr hwnd, Point screen, ModifierKeys modifiers = ModifierKeys.None)
    {
        return RunGesture(hwnd, screen, modifiers, mk =>
        {
            var client = ToClient(hwnd, screen);
            Send(hwnd, WM_MOUSEMOVE, (IntPtr)mk, client);
            Sleep();
            InputSpoof.SetLeftDown(true);
            Send(hwnd, WM_LBUTTONDOWN, (IntPtr)(mk | MK_LBUTTON), client);
            Sleep();
            InputSpoof.SetLeftDown(false);
            Send(hwnd, WM_LBUTTONUP, (IntPtr)mk, client);
            Sleep();
        });
    }

    /// <summary>지정된 창의 한 지점을 커서 이동 없이 더블클릭합니다(누름-뗌 두 번, WPF 가 ClickCount=2 로 인식).</summary>
    /// <param name="hwnd">대상 요소가 속한 최상위 창(HwndSource) 핸들.</param>
    /// <param name="screen">더블클릭할 스크린 디바이스 좌표.</param>
    /// <param name="modifiers">함께 눌린 것으로 볼 수식키.</param>
    /// <returns>후킹 경로로 보냈으면 Delivered(앱이 제때 처리) 또는 Stalled(앱이 느리거나 모달을 열어 스푸프를 일찍 끔), 후크를 쓸 수 없으면 Unavailable.</returns>
    public static GestureResult TryDoubleClick(IntPtr hwnd, Point screen, ModifierKeys modifiers = ModifierKeys.None)
    {
        return RunGesture(hwnd, screen, modifiers, mk =>
        {
            var client = ToClient(hwnd, screen);
            Send(hwnd, WM_MOUSEMOVE, (IntPtr)mk, client);
            Sleep();
            for (var i = 0; i < 2; i++)
            {
                InputSpoof.SetLeftDown(true);
                Send(hwnd, WM_LBUTTONDOWN, (IntPtr)(mk | MK_LBUTTON), client);
                Sleep();
                InputSpoof.SetLeftDown(false);
                Send(hwnd, WM_LBUTTONUP, (IntPtr)mk, client);
                Sleep();
            }
        });
    }

    /// <summary>
    /// 지정된 창의 한 지점을 커서 이동 없이 우클릭합니다. WPF 는 오른쪽 버튼 뗌에서 ContextMenu 를 연다.
    /// 접근성에 우클릭 패턴은 없으므로 항상 이 좌표 제스처로 수행한다.
    /// </summary>
    /// <param name="hwnd">대상 요소가 속한 최상위 창(HwndSource) 핸들.</param>
    /// <param name="screen">우클릭할 스크린 디바이스 좌표.</param>
    /// <param name="modifiers">함께 눌린 것으로 볼 수식키.</param>
    /// <returns>후킹 경로로 보냈으면 Delivered 또는 Stalled, 후크를 쓸 수 없으면 Unavailable.</returns>
    public static GestureResult TryRightClick(IntPtr hwnd, Point screen, ModifierKeys modifiers = ModifierKeys.None)
    {
        return RunGesture(hwnd, screen, modifiers, mk =>
        {
            var client = ToClient(hwnd, screen);
            Send(hwnd, WM_MOUSEMOVE, (IntPtr)mk, client);
            Sleep();
            InputSpoof.SetRightDown(true);
            Send(hwnd, WM_RBUTTONDOWN, (IntPtr)(mk | MK_RBUTTON), client);
            Sleep();
            InputSpoof.SetRightDown(false);
            Send(hwnd, WM_RBUTTONUP, (IntPtr)mk, client);
            Sleep();
        });
    }

    /// <summary>
    /// 지정된 창의 한 지점에서 커서 이동 없이 휠을 굴립니다. WPF 는 스푸프된 커서 위치 아래 요소로 MouseWheel 을 라우팅한다.
    /// </summary>
    /// <param name="hwnd">대상 요소가 속한 최상위 창(HwndSource) 핸들.</param>
    /// <param name="screen">휠을 굴릴 스크린 디바이스 좌표.</param>
    /// <param name="notches">굴릴 눈금 수. 양수는 위(앞), 음수는 아래(뒤).</param>
    /// <param name="modifiers">함께 눌린 것으로 볼 수식키(Ctrl+휠 줌 등).</param>
    /// <returns>후킹 경로로 보냈으면 Delivered 또는 Stalled, 후크를 쓸 수 없으면 Unavailable.</returns>
    public static GestureResult TryWheel(IntPtr hwnd, Point screen, int notches, ModifierKeys modifiers = ModifierKeys.None)
    {
        return RunGesture(hwnd, screen, modifiers, mk =>
        {
            Send(hwnd, WM_MOUSEMOVE, (IntPtr)mk, ToClient(hwnd, screen));
            Sleep();
            var (wParam, lParam) = PackWheel(notches, modifiers, screen);
            Send(hwnd, WM_MOUSEWHEEL, wParam, lParam);
            Sleep();
        });
    }

    /// <summary>지정된 창에서 한 지점을 누른 채 다른 지점까지 끌고 뗍니다. 커서는 움직이지 않습니다.</summary>
    /// <param name="hwnd">대상 요소가 속한 최상위 창(HwndSource) 핸들.</param>
    /// <param name="start">드래그 시작 스크린 디바이스 좌표.</param>
    /// <param name="end">드래그 끝 스크린 디바이스 좌표.</param>
    /// <param name="modifiers">함께 눌린 것으로 볼 수식키(Ctrl+드래그 복사 등).</param>
    /// <returns>후킹 경로로 보냈으면 Delivered(앱이 제때 처리) 또는 Stalled(앱이 느리거나 모달을 열어 스푸프를 일찍 끔), 후크를 쓸 수 없으면 Unavailable.</returns>
    public static GestureResult TryDrag(IntPtr hwnd, Point start, Point end, ModifierKeys modifiers = ModifierKeys.None)
    {
        return RunGesture(hwnd, start, modifiers, mk =>
        {
            Send(hwnd, WM_MOUSEMOVE, (IntPtr)mk, ToClient(hwnd, start));
            Sleep();
            InputSpoof.SetLeftDown(true);
            Send(hwnd, WM_LBUTTONDOWN, (IntPtr)(mk | MK_LBUTTON), ToClient(hwnd, start));
            Sleep();

            for (var step = 1; step <= DragSteps; step++)
            {
                var progress = (double)step / DragSteps;
                var point = new Point(
                    start.X + (end.X - start.X) * progress,
                    start.Y + (end.Y - start.Y) * progress);
                InputSpoof.SetPosition(point);
                Send(hwnd, WM_MOUSEMOVE, (IntPtr)(mk | MK_LBUTTON), ToClient(hwnd, point));
                Sleep();
            }

            InputSpoof.SetLeftDown(false);
            Send(hwnd, WM_LBUTTONUP, (IntPtr)mk, ToClient(hwnd, end));
            Sleep();
        });
    }

    /// <summary>
    /// WM_MOUSEWHEEL 의 wParam(상위 워드 delta, 하위 워드 MK 플래그)과 lParam(스크린 좌표 — 버튼 메시지와 달리
    /// 클라이언트 좌표가 아니다)을 만듭니다. 테스트에서 검증하기 위해 internal 로 둔다.
    /// </summary>
    /// <param name="notches">굴릴 눈금 수. 양수는 위(앞), 음수는 아래(뒤).</param>
    /// <param name="modifiers">함께 눌린 것으로 볼 수식키.</param>
    /// <param name="screen">휠을 굴릴 스크린 디바이스 좌표.</param>
    internal static (IntPtr WParam, IntPtr LParam) PackWheel(int notches, ModifierKeys modifiers, Point screen)
    {
        var delta = unchecked((short)(notches * WheelDelta));
        var packed = ((uint)(ushort)delta << 16) | (uint)MkFlags(modifiers);
        // 32비트 프로세스에서 IntPtr(long) 은 checked 라 음수 delta(0xFF88…)에서 넘친다. int 로 잘라 넣는다.
        var wParam = (IntPtr)unchecked((int)packed);
        var lParam = PackPoint((int)Math.Round(screen.X), (int)Math.Round(screen.Y));
        return (wParam, lParam);
    }

    /// <summary>
    /// 정체된 제스처 뒤에, UI 스레드가 우리가 보낸 메시지 처리를 끝낼 때까지 기다립니다. 느린 핸들러(수백 ms)는 끝나므로
    /// 조작을 정상 완료로 보고할 수 있고, 모달 대화상자를 연 핸들러는 끝나지 않으므로 "아직 처리 중" 으로 남는다.
    /// 판별은 UI 스레드에 낮은 우선순위 작업을 걸어 <c>InSendMessage</c> 를 묻는 것으로 한다 — 모달 루프 안에서도 디스패처
    /// 작업은 돌지만, 그 안에서는 아직 크로스스레드 전송 메시지를 처리하는 중이라 true 가 나온다. 디스패처 큐만 비기를
    /// 기다리면(<see cref="AutoWait.DispatcherDrain"/>) 모달 루프 안에서도 비므로 구분이 안 된다.
    /// </summary>
    /// <param name="dispatcher">대상 앱의 UI 디스패처.</param>
    /// <param name="timeout">기다릴 최대 시간.</param>
    /// <returns>핸들러가 끝났으면(또는 앱이 내려가 기다릴 대상이 없으면) true, 제한 시간이 지나도 처리 중이면 false.</returns>
    internal static async Task<bool> WaitUntilHandledAsync(Dispatcher dispatcher, TimeSpan timeout)
    {
        var deadline = Environment.TickCount64 + (long)timeout.TotalMilliseconds;
        while (true)
        {
            var remaining = deadline - Environment.TickCount64;
            if (remaining <= 0)
                return false;

            var probe = dispatcher.InvokeAsync(InSendMessage, DispatcherPriority.Background).Task;
            if (await Task.WhenAny(probe, Task.Delay(TimeSpan.FromMilliseconds(remaining))) != probe)
                return false;

            try
            {
                if (!await probe)
                    return true;
            }
            catch (Exception ex) when (ex is OperationCanceledException or InvalidOperationException
                                       && (dispatcher.HasShutdownStarted || dispatcher.HasShutdownFinished))
            {
                // 조작이 앱을 닫았다: 기다릴 핸들러가 남아 있지 않다.
                return true;
            }

            await Task.Delay(HandledPollMs);
        }
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// 스푸프를 켜고 잠금 안에서 제스처 본문을 실행한 뒤 반드시 스푸프를 끕니다. 다섯 제스처가 같은 골격을 쓰므로
    /// 가드·잠금·Begin/End 짝을 한곳에서 보장한다.
    /// </summary>
    /// <param name="hwnd">대상 창 핸들. 0 이면 false.</param>
    /// <param name="screen">스푸프할 커서 시작 위치.</param>
    /// <param name="modifiers">눌린 것으로 볼 수식키.</param>
    /// <param name="body">WM 메시지를 보내는 본문. 인자는 수식키의 MK 플래그.</param>
    /// <returns>후킹 경로로 보냈으면 Delivered 또는 Stalled, 후크를 쓸 수 없으면 Unavailable.</returns>
    private static GestureResult RunGesture(IntPtr hwnd, Point screen, ModifierKeys modifiers, Action<int> body)
    {
        if (hwnd == IntPtr.Zero || !InputSpoof.EnsureInstalled())
            return GestureResult.Unavailable;

        // 같은 앱의 팝업·대화상자가 그 지점을 덮고 있으면 실제 사용자도 그 창을 누르게 된다. 스푸프로 뚫지 않고
        // 실제 입력으로 폴백해 그 창이 눌리게 한다(다른 프로세스의 창이 덮은 것은 스푸프가 처리한다).
        if (InputSpoof.IsCoveredByOwnWindow(hwnd, screen))
            return GestureResult.Unavailable;

        lock (Gate)
        {
            _stalled = false;
            InputSpoof.BeginMouse(hwnd, screen, modifiers);
            try
            {
                body(MkFlags(modifiers));
            }
            finally
            {
                InputSpoof.EndMouse();
            }

            return _stalled ? GestureResult.Stalled : GestureResult.Delivered;
        }
    }

    private static void Sleep() => Thread.Sleep(StepDelayMs);

    /// <summary>수식키를 WM 마우스 메시지의 wParam MK 플래그로 바꿉니다(Alt 는 MK 플래그가 없다).</summary>
    private static int MkFlags(ModifierKeys modifiers)
    {
        var flags = 0;
        if (modifiers.HasFlag(ModifierKeys.Control)) flags |= MK_CONTROL;
        if (modifiers.HasFlag(ModifierKeys.Shift)) flags |= MK_SHIFT;
        return flags;
    }

    /// <summary>스크린 좌표를 대상 창의 클라이언트 좌표로 바꿔 lParam 으로 만듭니다.</summary>
    private static IntPtr ToClient(IntPtr hwnd, Point screen)
    {
        var point = new POINT { X = (int)Math.Round(screen.X), Y = (int)Math.Round(screen.Y) };
        ScreenToClient(hwnd, ref point);
        return PackPoint(point.X, point.Y);
    }

    /// <summary>x 를 하위 워드, y 를 상위 워드에 담은 lParam 을 만듭니다(음수 좌표도 16비트로 잘라 담는다).</summary>
    private static IntPtr PackPoint(int x, int y)
    {
        return (IntPtr)(((y & 0xFFFF) << 16) | (x & 0xFFFF));
    }

    private static void Send(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        // 타임아웃이 있는 동기 전송. 제한 시간 안에 처리되지 않으면(느린 핸들러·모달·멎은 앱) 메시지는 그대로 전달된
        // 채 두고 스푸프만 즉시 끈다 — 사람의 진짜 마우스가 가짜 상태를 보는 시간을 여기서 묶는다.
        // 0 은 제한 시간 초과뿐 아니라 창이 이미 파괴된 경우(핸들러가 자기 창을 닫음)에도 돌아온다 — 그건 정체가 아니라
        // 조작이 끝난 것이므로 오류 코드로 구분한다.
        var handled = SendMessageTimeoutW(hwnd, message, wParam, lParam, SMTO_ABORTIFHUNG, (uint)StallTimeoutMs, out _);
        if (handled == IntPtr.Zero && Marshal.GetLastWin32Error() == ErrorTimeout && !_stalled)
        {
            _stalled = true;
            InputSpoof.EndMouse();
        }
    }

    #endregion

    #region Interop

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    private const uint SMTO_ABORTIFHUNG = 0x0002;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool InSendMessage();

    [DllImport("user32.dll", SetLastError = true)]
    private static extern IntPtr SendMessageTimeoutW(
        IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam, uint flags, uint timeoutMs, out IntPtr result);

    [DllImport("user32.dll")]
    private static extern bool ScreenToClient(IntPtr hWnd, ref POINT point);

    #endregion
}

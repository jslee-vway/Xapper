using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace Xapper.Inspector.Actions;

/// <summary>
/// UI 요소에 대한 클릭 액션을 수행하는 정적 클래스.
/// 좌표 미지정 시: AutomationPeer(IInvokeProvider/IToggleProvider) → ButtonBase.ClickEvent → 마우스 이벤트 시뮬레이션 순으로 시도.
/// 좌표 지정 시: <see cref="MouseInput"/>을 통해 SendInput(MOUSEEVENTF_ABSOLUTE | VIRTUALDESK)으로 클릭.
/// 좌표가 입력 이벤트에 원자적으로 포함되므로 타이밍 이슈 없음.
/// 듀얼 모니터, Per-Monitor DPI 환경에서 동작.
/// 두 경로는 신뢰도가 다르므로, 호출자가 판단할 수 있도록 각 경로의 위험 신호를 경고 문구로 돌려준다.
/// </summary>
public static class ClickAction
{
    #region Fields

    /// <summary>이벤트 방식이 히트테스트를 건너뛰어 실제로는 누를 수 없는 요소를 눌렀을 때의 경고.</summary>
    private const string UnreachableWarning =
        "WARNING: this element is not reachable by a real mouse click (covered by another element, " +
        "or hit-testing is disabled). The event-based click bypassed hit-testing, so it succeeded " +
        "where a real user could not. Re-run with x/y to reproduce actual user behavior.";

    /// <summary>대상 창을 활성화하지 못해 입력이 유실될 수 있을 때의 경고.</summary>
    private const string NotForegroundWarning =
        "WARNING: the target window could not be brought to the foreground. The click may have been " +
        "consumed by window activation and never reached the control. Bring the window to the front and retry.";

    #endregion

    #region Public Methods

    /// <summary>
    /// 지정된 UI 요소를 클릭합니다.
    /// </summary>
    /// <param name="element">클릭할 대상 요소.</param>
    /// <param name="relativeX">요소 내 상대 X 좌표 (0.0~1.0). null이면 기본 클릭.</param>
    /// <param name="relativeY">요소 내 상대 Y 좌표 (0.0~1.0). null이면 기본 클릭.</param>
    /// <returns>주의가 필요한 상황이면 경고 문구, 문제가 없으면 null.</returns>
    /// <exception cref="InvalidOperationException">요소가 UIElement가 아닌 경우.</exception>
    public static string? Execute(DependencyObject element, double? relativeX = null, double? relativeY = null)
    {
        if (element is not UIElement uiElement)
            throw new InvalidOperationException($"Element {element.GetType().Name} is not a UIElement");

        // 좌표가 지정된 경우: SendInput(ABSOLUTE)으로 원자적 마우스 클릭
        if (relativeX.HasValue && relativeY.HasValue)
            return ClickAtPosition(uiElement, relativeX.Value, relativeY.Value);

        RaiseClick(uiElement);

        return MouseInput.IsReachableByMouse(uiElement, 0.5, 0.5) ? null : UnreachableWarning;
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// 접근성 인터페이스와 라우티드 이벤트로 클릭을 발생시킵니다. 히트테스트를 거치지 않습니다.
    /// </summary>
    private static void RaiseClick(UIElement uiElement)
    {
        // Priority 1: AutomationPeer
        var peer = UIElementAutomationPeer.CreatePeerForElement(uiElement);
        if (peer != null)
        {
            if (peer.GetPattern(PatternInterface.Invoke) is IInvokeProvider invoker)
            {
                invoker.Invoke();
                return;
            }

            if (peer.GetPattern(PatternInterface.Toggle) is IToggleProvider toggler)
            {
                toggler.Toggle();
                return;
            }
        }

        // Priority 2: RaiseEvent fallback
        if (uiElement is ButtonBase button)
        {
            button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
            return;
        }

        // Priority 3: 마우스 이벤트 시뮬레이션 (최후 수단)
        uiElement.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
        {
            RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent
        });
        uiElement.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
        {
            RoutedEvent = UIElement.MouseLeftButtonDownEvent
        });
        uiElement.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
        {
            RoutedEvent = UIElement.PreviewMouseLeftButtonUpEvent
        });
        uiElement.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
        {
            RoutedEvent = UIElement.MouseLeftButtonUpEvent
        });
    }

    /// <summary>
    /// 요소 내 상대 좌표를 스크린 좌표로 변환하고, 대상 윈도우를 포그라운드로 올린 뒤 클릭합니다.
    /// </summary>
    private static string? ClickAtPosition(UIElement element, double relativeX, double relativeY)
    {
        var screenPoint = MouseInput.ToScreenPoint(element, relativeX, relativeY);
        var activated = MouseInput.BringToForeground(element);
        MouseInput.ClickAt(screenPoint);
        return activated ? null : NotForegroundWarning;
    }

    #endregion
}

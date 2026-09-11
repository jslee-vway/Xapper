using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls.Primitives;

namespace Xapper.Inspector.Actions;

/// <summary>
/// CheckBox, ToggleButton 등의 토글 상태를 전환하는 정적 클래스.
/// AutomationPeer(IToggleProvider) → ToggleButton 직접 조작 순으로 시도.
/// </summary>
public static class ToggleAction
{
    /// <summary>
    /// 지정된 UI 요소의 토글 상태를 전환합니다.
    /// 토글할 수 없는 요소는 예외 대신 실패 결과로 알린다(결함 2).
    /// </summary>
    /// <param name="element">대상 토글 요소.</param>
    /// <returns>성공 또는 실패 사유.</returns>
    public static ActionResult Execute(UIElement element)
    {
        // Priority 1: AutomationPeer IToggleProvider
        var peer = UIElementAutomationPeer.CreatePeerForElement(element);
        if (peer?.GetPattern(PatternInterface.Toggle) is IToggleProvider toggler)
        {
            toggler.Toggle();
            return ActionResult.Ok;
        }

        // Priority 2: Direct ToggleButton manipulation
        if (element is ToggleButton toggleButton)
        {
            toggleButton.IsChecked = !toggleButton.IsChecked;
            return ActionResult.Ok;
        }

        return ActionResult.Fail($"Element {element.GetType().Name} does not support toggle");
    }
}

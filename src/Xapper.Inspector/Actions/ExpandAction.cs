using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;

namespace Xapper.Inspector.Actions;

/// <summary>
/// TreeViewItem, Expander 등의 확장/축소 액션을 수행하는 정적 클래스.
/// AutomationPeer(IExpandCollapseProvider) → Expander 직접 조작 → TreeViewItem 직접 조작 순으로 시도.
/// </summary>
public static class ExpandAction
{
    /// <summary>
    /// 지정된 UI 요소를 확장 또는 축소합니다.
    /// 확장/축소를 지원하지 않는 요소는 예외 대신 실패 결과로 알린다(결함 2).
    /// </summary>
    /// <param name="element">대상 요소.</param>
    /// <param name="expand">true이면 확장, false이면 축소.</param>
    /// <returns>성공 또는 실패 사유.</returns>
    public static ActionResult Execute(UIElement element, bool expand)
    {
        // Priority 1: AutomationPeer IExpandCollapseProvider
        var peer = UIElementAutomationPeer.CreatePeerForElement(element);
        if (peer?.GetPattern(PatternInterface.ExpandCollapse) is IExpandCollapseProvider expander)
        {
            if (expand)
                expander.Expand();
            else
                expander.Collapse();
            return ActionResult.Ok;
        }

        // Priority 2: Direct Expander control
        if (element is Expander expanderControl)
        {
            expanderControl.IsExpanded = expand;
            return ActionResult.Ok;
        }

        // Priority 3: TreeViewItem
        if (element is TreeViewItem treeItem)
        {
            treeItem.IsExpanded = expand;
            return ActionResult.Ok;
        }

        return ActionResult.Fail($"Element {element.GetType().Name} does not support expand/collapse");
    }
}

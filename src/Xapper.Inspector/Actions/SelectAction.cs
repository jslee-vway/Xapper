using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls.Primitives;

namespace Xapper.Inspector.Actions;

/// <summary>
/// ComboBox, ListBox, TabControl 등 Selector 계열 컨트롤에서 항목 선택을 수행하는 정적 클래스.
/// Selector 인덱스/텍스트 매칭 → AutomationPeer(ISelectionItemProvider) 순으로 시도.
/// </summary>
public static class SelectAction
{
    /// <summary>
    /// 지정된 Selector 컨트롤에서 항목을 선택합니다.
    /// 선택할 수 없는 경우(항목 없음·미지원)는 예외 대신 실패 결과로 알린다(결함 2).
    /// </summary>
    /// <param name="element">대상 Selector 요소.</param>
    /// <param name="itemText">선택할 항목의 텍스트. 대소문자 무시 매칭.</param>
    /// <param name="itemIndex">선택할 항목의 0-based 인덱스.</param>
    /// <returns>성공 또는 실패 사유.</returns>
    public static ActionResult Execute(UIElement element, string? itemText, int? itemIndex)
    {
        // Try Selector-based controls (ComboBox, ListBox, TabControl)
        if (element is Selector selector)
        {
            if (itemIndex.HasValue)
            {
                selector.SelectedIndex = itemIndex.Value;
                return ActionResult.Ok;
            }

            if (itemText != null)
            {
                for (int i = 0; i < selector.Items.Count; i++)
                {
                    var item = selector.Items[i];
                    var text = item?.ToString() ?? "";
                    if (text.Equals(itemText, StringComparison.OrdinalIgnoreCase))
                    {
                        selector.SelectedIndex = i;
                        return ActionResult.Ok;
                    }
                }
                return ActionResult.Fail($"Item \"{itemText}\" not found in {element.GetType().Name}");
            }
        }

        // Try AutomationPeer ISelectionItemProvider
        var peer = UIElementAutomationPeer.CreatePeerForElement(element);
        if (peer?.GetPattern(PatternInterface.SelectionItem) is ISelectionItemProvider selectionItem)
        {
            selectionItem.Select();
            return ActionResult.Ok;
        }

        return ActionResult.Fail(
            $"Element {element.GetType().Name} does not support selection. Provide a Selector control or element with SelectionItem pattern.");
    }
}

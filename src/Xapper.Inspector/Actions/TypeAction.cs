using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;

namespace Xapper.Inspector.Actions;

/// <summary>
/// TextBox 등 텍스트 입력이 가능한 UI 요소에 텍스트를 입력하는 정적 클래스.
/// AutomationPeer(IValueProvider) → TextBox 직접 조작 순으로 시도.
/// </summary>
public static class TypeAction
{
    /// <summary>
    /// 지정된 UI 요소에 텍스트를 입력합니다.
    /// 텍스트 입력을 지원하지 않는 요소는 예외 대신 실패 결과로 알린다(결함 2).
    /// </summary>
    /// <param name="element">대상 텍스트 입력 요소.</param>
    /// <param name="text">입력할 텍스트.</param>
    /// <param name="clear">true이면 기존 텍스트를 지우고 입력, false이면 뒤에 추가.</param>
    /// <returns>성공 또는 실패 사유.</returns>
    public static ActionResult Execute(UIElement element, string text, bool clear)
    {
        // Priority 1: AutomationPeer IValueProvider
        var peer = UIElementAutomationPeer.CreatePeerForElement(element);
        if (peer != null)
        {
            if (peer.GetPattern(PatternInterface.Value) is IValueProvider valueProvider)
            {
                if (clear)
                    valueProvider.SetValue(text);
                else
                    valueProvider.SetValue(valueProvider.Value + text);
                return ActionResult.Ok;
            }
        }

        // Priority 2: Direct TextBox manipulation
        if (element is TextBox textBox)
        {
            textBox.Focus();
            if (clear)
                textBox.Text = text;
            else
                textBox.AppendText(text);
            textBox.CaretIndex = textBox.Text.Length;
            return ActionResult.Ok;
        }

        return ActionResult.Fail($"Element {element.GetType().Name} does not support text input");
    }
}

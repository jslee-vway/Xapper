using System.Windows;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Controls;
using System.Windows.Input;

namespace Xapper.Inspector.Actions;

/// <summary>
/// TextBox 등 텍스트 입력이 가능한 UI 요소에 텍스트를 입력하는 정적 클래스.
/// AutomationPeer(IValueProvider) → TextBox 직접 조작 → 포커스를 준 뒤 실제 키 입력(<see cref="KeyboardInput"/>) 순으로 시도한다.
/// 마지막 경로 덕에 Value 패턴이 없는 편집기(RichTextBox, 그리드 셀 인라인 편집기 등)에도 글자가 들어간다.
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

        // Priority 3: 포커스를 주고 실제 키 입력으로 타이핑한다(Value 패턴이 없는 편집기).
        return TypeByKeystrokes(element, text, clear);
    }

    /// <summary>
    /// 현재 키보드 포커스 요소에 실제 키 입력으로 타이핑합니다. ref 없이 부르는 경로 — F2 로 연 인라인 편집기처럼
    /// 스냅샷에 아직 없는 편집기에 글자를 넣는다. 요소가 글자를 소비하는지는 알 수 없으므로 호출자가 결과를 확인해야 한다.
    /// </summary>
    /// <param name="text">입력할 텍스트.</param>
    /// <param name="clear">true이면 전체 선택 후 대체.</param>
    /// <returns>성공 또는 실패 사유.</returns>
    public static ActionResult ExecuteOnFocused(string text, bool clear) => TypeByKeystrokes(null, text, clear);

    /// <summary>
    /// 요소(null 이면 포커스 요소)에 키 입력으로 타이핑합니다. clear 면 Ctrl+A 로 전체 선택한 뒤 타이핑해 대체하고,
    /// 빈 문자열이면 Delete 로 비운다. 제어 문자는 Ctrl+A 를 보내기 전에 거절해 오류 경로에 부작용을 남기지 않는다.
    /// </summary>
    private static ActionResult TypeByKeystrokes(UIElement? element, string text, bool clear)
    {
        var name = element?.GetType().Name ?? "focused element";
        if (text.Any(char.IsControl))
            return ActionResult.Fail($"{name} does not support text input: text must not contain control characters; " +
                                     "send named keys (Enter, Tab) with the key tool.");

        if (clear)
        {
            var (clearError, _, _) = KeyboardInput.Send(element, "a", ModifierKeys.Control);
            if (clearError is not null)
                return ActionResult.Fail($"{name} does not support text input (no value pattern, and the existing " +
                                         $"text could not be selected for clearing - retry with clear=false to append): {clearError}");
        }

        // 빈 문자열은 타이핑으로 선택을 대체할 수 없으므로 지워서 비운다.
        if (clear && text.Length == 0)
        {
            var (deleteError, _, _) = KeyboardInput.Send(element, "Delete", ModifierKeys.None);
            return deleteError is null
                ? ActionResult.Ok
                : ActionResult.Fail($"{name} does not support text input: {deleteError}");
        }

        var (typeError, _) = KeyboardInput.TypeText(element, text);
        return typeError is null
            ? ActionResult.Ok
            : ActionResult.Fail($"{name} does not support text input: {typeError}");
    }
}

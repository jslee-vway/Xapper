using System.Windows;
using System.Windows.Input;

namespace Xapper.Inspector.Actions;

/// <summary>
/// 대상 프로세스 안에서 WPF 입력 파이프라인에 키를 주입하는 정적 클래스.
///
/// SendKeys/SendInput 은 OS 전경 창으로 키를 보내므로, 사용자가 앞에 띄운 다른 창으로 키가 샌다.
/// 여기서는 대상 앱의 UI 스레드에서 <see cref="InputManager"/> 에 직접 키 이벤트를 넣어, 그 앱의
/// <see cref="Keyboard.FocusedElement"/> 로 라우팅한다 — 전경 창·물리 키보드와 무관하다.
///
/// Preview 이벤트만 넣고 WPF 가 본 이벤트(KeyDown/KeyUp)로 승격하게 둔다. 둘 다 넣으면 키가 두 번
/// 눌린 것처럼 처리된다(측정 확인: Preview+Main 을 함께 넣으면 Backspace 가 두 글자를 지웠다).
/// 글자 입력은 라우팅만으로는 텍스트가 들어가지 않으므로 <see cref="TextCompositionManager.TextInputEvent"/>
/// 로 함께 넣는다.
///
/// 예외를 던지지 않는다: 주입된 프로세스 안에서 던지면 대상 앱의 first-chance 핸들러가 앱을 죽일 수
/// 있다(결함 2). 잘못된 키 이름·포커스 없음 등 정상적 실패는 오류 문구로 돌려준다.
/// </summary>
internal static class KeyboardInput
{
    #region Public Methods

    /// <summary>
    /// 키를 주입합니다. 반드시 대상 UI 스레드에서 호출해야 한다.
    /// </summary>
    /// <param name="refElement">먼저 포커스를 줄 요소. null 이면 현재 포커스 요소에 보낸다.</param>
    /// <param name="keyName">보낼 키(WPF Key 이름 또는 한 글자).</param>
    /// <param name="modifiers">수식키 조합. 현재 미지원이라 지정되면 오류를 돌려준다.</param>
    /// <returns>실패 사유(없으면 null)와, 주입 후 포커스 요소의 타입 이름.</returns>
    public static (string? Error, string Focused) Send(DependencyObject? refElement, string keyName, string? modifiers)
    {
        if (!string.IsNullOrWhiteSpace(modifiers))
            return (
                $"Modifiers (\"{modifiers}\") are not supported yet. WPF reads Ctrl/Shift/Alt state from the OS " +
                "keyboard, which cannot be set from inside the process without real input, so a synthesized " +
                "modifier does not register. Send the base key without modifiers.",
                "");

        if (!TryResolveKey(keyName, out var key, out var text))
            return (
                $"Unknown key \"{keyName}\". Use a WPF Key name (F2, Enter, Escape, Tab, Down, ...) or a single " +
                "printable character to type.",
                "");

        // 포커스 대상 결정: ref 가 있으면 그 요소에 포커스를 준 뒤, 없으면 현재 포커스 요소.
        IInputElement? target;
        if (refElement is IInputElement requested)
        {
            // Keyboard.Focus 는 실제로 포커스가 간 요소를 돌려준다. 요청한 ref 가 포커스를 못 받으면
            // 키가 엉뚱한 요소로 라우팅되므로, 조용히 잘못 보내지 말고 그 사실을 알린다.
            var landed = Keyboard.Focus(requested);
            if (!ReferenceEquals(landed, requested))
                return (
                    "Could not move keyboard focus to that ref - it may not be a focusable element. No key was " +
                    "sent. Pick a focusable control, or omit ref to send to whatever currently has focus.",
                    "");
            target = requested;
        }
        else
        {
            target = Keyboard.FocusedElement;
        }

        if (target is null)
            return (
                "No element has keyboard focus and no ref was given. Focus a control first (for example by " +
                "clicking it), or pass ref to focus one.",
                "");

        if (target is not DependencyObject targetObject
            || PresentationSource.FromDependencyObject(targetObject) is not { } source)
            return ("The focused element has no presentation source, so keys cannot be routed to it.", "");

        if (key != Key.None)
            RaiseKey(source, key, Keyboard.PreviewKeyDownEvent);

        // 글자는 라우팅만으로 들어가지 않으므로 TextInput 으로 넣는다.
        if (text is not null)
            RaiseText(target, text);

        if (key != Key.None)
            RaiseKey(source, key, Keyboard.PreviewKeyUpEvent);

        return (null, FocusedTypeName());
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// 키 이벤트를 대상에 넣습니다. Preview 이벤트만 넣으면 WPF 가 본 이벤트로 승격한다.
    /// </summary>
    private static void RaiseKey(PresentationSource source, Key key, RoutedEvent previewEvent)
    {
        InputManager.Current.ProcessInput(
            new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = previewEvent });
    }

    /// <summary>
    /// 글자를 TextInput 이벤트로 넣습니다. TextBox 등은 이 이벤트로 실제 텍스트를 받는다.
    /// </summary>
    private static void RaiseText(IInputElement target, string text)
    {
        var composition = new TextComposition(InputManager.Current, target, text);
        InputManager.Current.ProcessInput(
            new TextCompositionEventArgs(Keyboard.PrimaryDevice, composition)
            {
                RoutedEvent = TextCompositionManager.TextInputEvent
            });
    }

    /// <summary>
    /// 키 이름을 <see cref="Key"/> 로 해석합니다. 한 글자 인쇄 문자는 그 글자를 타이핑할 텍스트로도 돌려준다.
    /// </summary>
    /// <param name="keyName">해석할 키 이름 또는 한 글자.</param>
    /// <param name="key">해석된 키. 텍스트만 있고 대응 키가 없으면 <see cref="Key.None"/>.</param>
    /// <param name="text">타이핑할 글자. 명령키(F2 등)면 null.</param>
    /// <returns>키나 글자로 해석되면 true.</returns>
    private static bool TryResolveKey(string keyName, out Key key, out string? text)
    {
        key = Key.None;
        text = null;

        if (string.IsNullOrEmpty(keyName))
            return false;

        // 한 글자 인쇄 문자: 그 글자를 타이핑한다(대응 Key 가 있으면 키 이벤트도 함께).
        if (keyName.Length == 1 && !char.IsControl(keyName[0]))
        {
            text = keyName;
            key = KeyFromChar(keyName[0]);
            return true;
        }

        // 그 외는 WPF Key 이름으로 해석한다(F2, Enter, Escape, Tab, Down ...).
        // Enum.TryParse 는 "999" 같은 숫자 문자열도 (Key)999 로 성공시키는데, 이는 정의되지 않은
        // 값이라 KeyEventArgs 생성자에서 InvalidEnumArgumentException 을 던진다(결함 2 위반).
        // Enum.IsDefined 로 실제 정의된 이름만 통과시켜, 그 외는 "Unknown key" 오류로 돌린다.
        return Enum.TryParse(keyName, ignoreCase: true, out key)
            && key != Key.None
            && Enum.IsDefined(typeof(Key), key);
    }

    /// <summary>글자에 대응하는 <see cref="Key"/> 를 돌려줍니다. 없으면 <see cref="Key.None"/>.</summary>
    private static Key KeyFromChar(char ch)
    {
        if (ch is >= 'a' and <= 'z') return Key.A + (ch - 'a');
        if (ch is >= 'A' and <= 'Z') return Key.A + (ch - 'A');
        if (ch is >= '0' and <= '9') return Key.D0 + (ch - '0');
        return Key.None;
    }

    /// <summary>주입 후 키보드 포커스 요소의 타입 이름을 돌려줍니다.</summary>
    private static string FocusedTypeName()
    {
        return Keyboard.FocusedElement switch
        {
            DependencyObject d => d.GetType().Name,
            { } other => other.GetType().Name,
            _ => "null"
        };
    }

    #endregion
}

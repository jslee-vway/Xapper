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
/// 눌린 것처럼 처리된다(측정 확인: Preview+Main 을 함께 넣으면 Backspace 가 두 글자를 지웠다). 글자 입력은
/// 라우팅만으로는 텍스트가 들어가지 않으므로 <see cref="TextCompositionManager.TextInputEvent"/> 로 함께 넣는다
/// — 단 Ctrl/Alt 가 걸리면 실제 키보드처럼 글자는 넣지 않는다(Ctrl+A 가 "a" 를 타이핑하면 안 된다).
/// Alt+글자 접근키(AccessKeyManager)는 Key.System 경로가 필요해 이 주입으로는 발동하지 않으며, Shift+글자는
/// 글자를 준 그대로 넣는다(대문자는 호출자가 "A" 로 준다).
///
/// 수식키는 WPF 가 <c>GetKeyState</c> 로 OS 에 묻는다. 그래서 라우팅 이벤트로는 못 만들고,
/// <see cref="InputSpoof"/> 로 그 답을 스푸프한 채 키를 넣는다. 후크를 쓸 수 없으면 수식키를 걸 방법이
/// 없다 — SendInput 으로 실제 Ctrl 을 눌러도 GetKeyState 는 스레드가 키 메시지를 펌프해야 갱신되므로,
/// 펌프 없이 동기 실행되는 이 경로에서는 반영되지 않는다. 그래서 거짓 성공 대신 오류를 돌려준다.
///
/// 예외를 던지지 않는다: 주입된 프로세스 안에서 던지면 대상 앱을 죽일 수 있다(결함 2).
/// </summary>
internal static class KeyboardInput
{
    #region Public Methods

    /// <summary>
    /// 키를 주입합니다. 반드시 대상 UI 스레드에서 호출해야 한다.
    /// </summary>
    /// <param name="refElement">먼저 포커스를 줄 요소. null 이면 현재 포커스 요소에 보낸다.</param>
    /// <param name="keyName">보낼 키(WPF Key 이름 또는 한 글자).</param>
    /// <param name="modifiers">함께 눌린 것으로 볼 수식키.</param>
    /// <returns>실패 사유(없으면 null), 주입 후 포커스 요소의 타입 이름, 수식키를 어떻게 걸었는지("" 이면 수식키 없음).</returns>
    public static (string? Error, string Focused, string ModifierPath) Send(
        DependencyObject? refElement, string keyName, ModifierKeys modifiers)
    {
        if (!TryResolveKey(keyName, out var key, out var text))
            return (
                $"Unknown key \"{keyName}\". Use a WPF Key name (F2, Enter, Escape, Tab, Down, ...) or a single " +
                "printable character to type.",
                "", "");

        // 후크가 없으면 키를 넣기 전에(포커스를 옮기기 전에) 알린다 — 오류 경로에 부작용을 남기지 않는다.
        if (modifiers != ModifierKeys.None && !InputSpoof.EnsureInstalled())
            return (
                "Modifier keys need the in-process input hook, which could not be installed in this process, so " +
                "Ctrl/Shift/Alt cannot be held for an injected key. Send the key without modifiers.",
                "", "");

        IInputElement? target;
        if (refElement is IInputElement requested)
        {
            var landed = Keyboard.Focus(requested);
            if (!ReferenceEquals(landed, requested))
                return (
                    "Could not move keyboard focus to that ref - it may not be a focusable element. No key was " +
                    "sent. Pick a focusable control, or omit ref to send to whatever currently has focus.",
                    "", "");
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
                "", "");

        if (target is not DependencyObject targetObject
            || PresentationSource.FromDependencyObject(targetObject) is not { } source)
            return ("The focused element has no presentation source, so keys cannot be routed to it.", "", "");

        // Ctrl/Alt 조합은 글자를 타이핑하지 않는다(실제 키보드와 동일).
        var suppressText = (modifiers & (ModifierKeys.Control | ModifierKeys.Alt)) != 0;

        var modifierPath = modifiers != ModifierKeys.None ? "modifiers spoofed in-process" : "";
        if (modifiers != ModifierKeys.None)
            InputSpoof.BeginModifiers(modifiers);

        try
        {
            if (key != Key.None)
                RaiseKey(source, key, Keyboard.PreviewKeyDownEvent);

            if (text is not null && !suppressText)
                RaiseText(target, text);

            if (key != Key.None)
                RaiseKey(source, key, Keyboard.PreviewKeyUpEvent);
        }
        finally
        {
            if (modifiers != ModifierKeys.None)
                InputSpoof.EndModifiers();
        }

        return (null, FocusedTypeName(), modifierPath);
    }

    #endregion

    #region Private Methods

    /// <summary>키 이벤트를 대상에 넣습니다. Preview 이벤트만 넣으면 WPF 가 본 이벤트로 승격한다.</summary>
    private static void RaiseKey(PresentationSource source, Key key, RoutedEvent previewEvent)
    {
        InputManager.Current.ProcessInput(
            new KeyEventArgs(Keyboard.PrimaryDevice, source, 0, key) { RoutedEvent = previewEvent });
    }

    /// <summary>글자를 TextInput 이벤트로 넣습니다. TextBox 등은 이 이벤트로 실제 텍스트를 받는다.</summary>
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
    /// Enum.TryParse 는 "999" 같은 숫자도 통과시키는데 그건 정의되지 않은 값이라 KeyEventArgs 가 던지므로
    /// Enum.IsDefined 로 실제 정의된 이름만 통과시킨다.
    /// </summary>
    private static bool TryResolveKey(string keyName, out Key key, out string? text)
    {
        key = Key.None;
        text = null;

        if (string.IsNullOrEmpty(keyName))
            return false;

        if (keyName.Length == 1 && !char.IsControl(keyName[0]))
        {
            text = keyName;
            key = KeyFromChar(keyName[0]);
            return true;
        }

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

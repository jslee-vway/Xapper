using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Xapper.Inspector.Actions;

namespace Xapper.Tests;

/// <summary>
/// 프로세스 내부 키 주입이 실제로 대상 요소에 라우팅되는지, 정상적 실패에서 예외를 던지지 않는지
/// 검증하는 테스트 클래스. 실행기들은 <c>Application.Current</c> 없이 현재 STA 스레드의 InputManager 로
/// 동작하므로, 창과 포커스만 세우면 직접 호출해 검증할 수 있다.
/// </summary>
[Collection(DesktopWindowCollection.Name)]
public class KeyboardInputTests
{
    [Fact]
    public void Send_TypesASingleCharacterIntoTheFocusedTextBox()
    {
        OffscreenTextBoxes.Run(box =>
        {
            var (error, focused, _) = KeyboardInput.Send(null, "a", ModifierKeys.None);

            Assert.Null(error);
            Assert.Equal("a", box.Text);
            Assert.Equal(nameof(TextBox), focused);
        });
    }

    [Fact]
    public void Send_RoutesANamedKeyToTheFocusedElement()
    {
        OffscreenTextBoxes.Run(box =>
        {
            var seen = new List<Key>();
            box.KeyDown += (_, e) => seen.Add(e.Key);

            var (error, _, _) = KeyboardInput.Send(null, "Escape", ModifierKeys.None);

            Assert.Null(error);
            Assert.Contains(Key.Escape, seen);
        });
    }

    [Fact]
    public void Send_ToARef_FocusesThatElementFirst()
    {
        OffscreenTextBoxes.Run((box, other) =>
        {
            // box 가 포커스인 상태에서 other 를 ref 로 주면, 키가 나가기 전에 other 로 포커스가 옮겨져야 한다.
            var (error, focused, _) = KeyboardInput.Send(other, "b", ModifierKeys.None);

            Assert.Null(error);
            Assert.Equal("b", other.Text);
            Assert.Equal("", box.Text);
            Assert.Same(other, Keyboard.FocusedElement);
            Assert.Equal(nameof(TextBox), focused);
        });
    }

    [Fact]
    public void Send_TypesAWholeStringAsSequentialKeystrokes()
    {
        OffscreenTextBoxes.Run(box =>
        {
            var downs = new List<Key>();
            box.PreviewKeyDown += (_, e) => downs.Add(e.Key);

            var (error, focused, _) = KeyboardInput.Send(null, "qwerty", ModifierKeys.None);

            Assert.Null(error);
            Assert.Equal("qwerty", box.Text);
            Assert.Equal(new[] { Key.Q, Key.W, Key.E, Key.R, Key.T, Key.Y }, downs);
            Assert.Equal(nameof(TextBox), focused);
        });
    }

    [Fact]
    public void Send_AWordThatIsAKeyName_SendsTheKeyNotTheLetters()
    {
        OffscreenTextBoxes.Run(box =>
        {
            var downs = new List<Key>();
            box.PreviewKeyDown += (_, e) => downs.Add(e.Key);

            var (error, _, _) = KeyboardInput.Send(null, "Enter", ModifierKeys.None);

            Assert.Null(error);
            Assert.Equal(new[] { Key.Enter }, downs);
            Assert.Equal("", box.Text);
        });
    }

    [Fact]
    public void Send_AStringWithModifiers_ReturnsErrorWithoutThrowing()
    {
        OffscreenTextBoxes.Run(box =>
        {
            var (error, _, _) = KeyboardInput.Send(null, "abc", ModifierKeys.Control);

            Assert.NotNull(error);
            Assert.Contains("single key", error);
            Assert.Equal("", box.Text);
        });
    }

    [Fact]
    public void TypeText_TypesLiterallyEvenWhenTheWordIsAKeyName()
    {
        OffscreenTextBoxes.Run(box =>
        {
            var (error, focused) = KeyboardInput.TypeText(null, "Enter");

            Assert.Null(error);
            Assert.Equal("Enter", box.Text);
            Assert.Equal(nameof(TextBox), focused);
        });
    }

    [Fact]
    public void TypeText_WithControlCharacters_ReturnsErrorWithoutThrowing()
    {
        OffscreenTextBoxes.Run(box =>
        {
            var (error, _) = KeyboardInput.TypeText(null, "a\tb");

            Assert.NotNull(error);
            Assert.Equal("", box.Text);
        });
    }

    [Fact]
    public void Send_AWordThatIsNotAKeyName_IsTypedLiterally()
    {
        OffscreenTextBoxes.Run(box =>
        {
            var (error, _, _) = KeyboardInput.Send(null, "NotAKey", ModifierKeys.None);

            Assert.Null(error);
            Assert.Equal("NotAKey", box.Text);
        });
    }

    [Fact]
    public void Send_WithControlCharacters_ReturnsErrorWithoutThrowing()
    {
        OffscreenTextBoxes.Run(box =>
        {
            var (error, _, _) = KeyboardInput.Send(null, "a" + '\n' + "b", ModifierKeys.None);

            Assert.NotNull(error);
            Assert.Contains("Unknown key", error);
            Assert.Equal("", box.Text);
        });
    }

    [Fact]
    public void Send_WithANumericString_TypesTheDigitsInsteadOfThrowing()
    {
        OffscreenTextBoxes.Run(box =>
        {
            // "999" 는 Enum.TryParse<Key> 를 (Key)999 로 통과시키지만 정의되지 않은 값이라 KeyEventArgs 생성자가
            // 던진다(결함 2). Key 로 해석하지 않고 숫자 세 글자로 타이핑해야 한다.
            var (error, _, _) = KeyboardInput.Send(null, "999", ModifierKeys.None);

            Assert.Null(error);
            Assert.Equal("999", box.Text);
        });
    }

    [Fact]
    public void Send_WithNoFocusAndNoRef_ReturnsErrorWithoutThrowing()
    {
        StaThread.Run(() =>
        {
            // 창도 포커스도 없이 호출한다.
            Keyboard.ClearFocus();
            var (error, _, _) = KeyboardInput.Send(null, "a", ModifierKeys.None);

            Assert.NotNull(error);
            Assert.Contains("no ref", error);
        });
    }
}

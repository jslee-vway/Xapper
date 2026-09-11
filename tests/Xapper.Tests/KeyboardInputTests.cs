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
    public void Send_WithAnUnknownKey_ReturnsErrorWithoutThrowing()
    {
        OffscreenTextBoxes.Run(_ =>
        {
            var (error, _, _) = KeyboardInput.Send(null, "NotAKey", ModifierKeys.None);

            Assert.NotNull(error);
            Assert.Contains("Unknown key", error);
        });
    }

    [Fact]
    public void Send_WithANumericKeyName_ReturnsErrorWithoutThrowing()
    {
        OffscreenTextBoxes.Run(_ =>
        {
            // "999" 는 Enum.TryParse<Key> 를 (Key)999 로 통과시키지만 정의되지 않은 값이라,
            // KeyEventArgs 생성자가 예외를 던진다(결함 2). 던지지 말고 오류로 돌려줘야 한다.
            var (error, _, _) = KeyboardInput.Send(null, "999", ModifierKeys.None);

            Assert.NotNull(error);
            Assert.Contains("Unknown key", error);
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

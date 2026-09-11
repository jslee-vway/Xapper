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
        WithFocusedTextBox(box =>
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
        WithFocusedTextBox(box =>
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
        WithFocusedTextBox((box, other) =>
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
        WithFocusedTextBox(_ =>
        {
            var (error, _, _) = KeyboardInput.Send(null, "NotAKey", ModifierKeys.None);

            Assert.NotNull(error);
            Assert.Contains("Unknown key", error);
        });
    }

    [Fact]
    public void Send_WithANumericKeyName_ReturnsErrorWithoutThrowing()
    {
        WithFocusedTextBox(_ =>
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

    #region Helpers

    private static void WithFocusedTextBox(Action<TextBox> body)
        => WithFocusedTextBox((box, _) => body(box));

    /// <summary>
    /// 화면 밖에 창을 하나 세워 TextBox 두 개를 담고 첫 번째에 포커스를 준 뒤 본문을 실행합니다.
    /// InputManager 는 창이 실제로 있어야 입력을 라우팅하므로 창을 띄운다.
    /// </summary>
    private static void WithFocusedTextBox(Action<TextBox, TextBox> body)
    {
        StaThread.Run(() =>
        {
            var box = new TextBox();
            var other = new TextBox();
            var panel = new StackPanel();
            panel.Children.Add(box);
            panel.Children.Add(other);
            var window = new Window
            {
                Content = panel, Width = 200, Height = 120,
                Left = -10000, Top = -10000,
                ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None
            };

            try
            {
                window.Show();
                box.Focus();
                body(box, other);
            }
            finally
            {
                window.Close();
                System.Windows.Threading.Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
    }

    #endregion
}

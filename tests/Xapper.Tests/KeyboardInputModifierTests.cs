using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Xapper.Inspector.Actions;

namespace Xapper.Tests;

/// <summary>
/// 수식키가 걸린 키 주입이 실제 커맨드로 이어지는지 검증한다. Ctrl+Z 는 TextBox 의 Undo 커맨드 바인딩을 타므로
/// 텍스트가 되돌아가는 것으로 수식키가 진짜로 인식됐음을 측정한다.
/// </summary>
[Collection(DesktopWindowCollection.Name)]
public class KeyboardInputModifierTests
{
    [Fact]
    public void Send_CtrlZ_TriggersUndoOnTheTextBox()
    {
        WithFocusedTextBox(box =>
        {
            if (!InputSpoof.EnsureInstalled())
                return;

            var (typeError, _, _) = KeyboardInput.Send(null, "a", ModifierKeys.None);
            Assert.Null(typeError);
            Assert.Equal("a", box.Text);

            var (error, _, path) = KeyboardInput.Send(null, "z", ModifierKeys.Control);

            Assert.Null(error);
            Assert.Equal("", box.Text);
            Assert.Equal("modifiers spoofed in-process", path);
        });
    }

    [Fact]
    public void Send_CharacterWithCtrl_DoesNotTypeTheCharacter()
    {
        WithFocusedTextBox(box =>
        {
            // 후크가 없으면 Send 가 오류를 돌려주므로(실제 키를 누르지 않는다) 검증 대상이 없다.
            if (!InputSpoof.EnsureInstalled())
                return;

            var (error, _, _) = KeyboardInput.Send(null, "a", ModifierKeys.Control);

            Assert.Null(error);
            Assert.Equal("", box.Text);
        });
    }

    [Fact]
    public void Send_ShiftTab_MovesFocusBackward()
    {
        WithFocusedTextBoxes((box, other) =>
        {
            if (!InputSpoof.EnsureInstalled())
                return;

            other.Focus();
            Assert.Same(other, Keyboard.FocusedElement);

            var seen = ModifierKeys.None;
            other.PreviewKeyDown += (_, e) => seen = e.KeyboardDevice.Modifiers;

            var (error, focused, _) = KeyboardInput.Send(null, "Tab", ModifierKeys.Shift);

            Assert.Null(error);
            Assert.Same(box, Keyboard.FocusedElement);
            Assert.Equal(nameof(TextBox), focused);
            Assert.Equal(ModifierKeys.Shift, seen);
        });
    }

    private static void WithFocusedTextBox(Action<TextBox> body)
        => WithFocusedTextBoxes((box, _) => body(box));

    private static void WithFocusedTextBoxes(Action<TextBox, TextBox> body)
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
}

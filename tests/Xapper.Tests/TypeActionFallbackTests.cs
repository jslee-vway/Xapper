using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Threading;
using Xapper.Inspector.Actions;

namespace Xapper.Tests;

/// <summary>
/// Value 패턴도 TextBox 도 아닌 편집기에 type 액션이 실제 키 입력으로 폴백해 글자를 넣는지 검증한다.
/// RichTextBox 는 TextAutomationPeer 라 Value 패턴이 없어 예전에는 "does not support text input" 으로 실패했다.
/// 리치 편집기는 글자 입력을 Background 우선순위로 미뤄 적용하므로(실제 도구는 SettleAsync 가 그때까지 기다린다)
/// 읽기 전에 디스패처를 한 번 비운다. 수식키 스푸프가 필요한 경로라 후크가 없는 환경에서는 검증하지 않는다.
/// </summary>
[Collection(DesktopWindowCollection.Name)]
public class TypeActionFallbackTests
{
    [Fact]
    public void Execute_OnARichTextBox_TypesThroughKeystrokes()
    {
        WithRichTextBox("", box =>
        {
            var result = TypeAction.Execute(box, "hello", clear: true);
            Drain();

            Assert.Null(result.Error);
            Assert.Equal("hello", TextOf(box));
        });
    }

    [Fact]
    public void Execute_WithClear_ReplacesTheExistingText()
    {
        WithRichTextBox("old", box =>
        {
            var result = TypeAction.Execute(box, "new", clear: true);
            Drain();

            Assert.Null(result.Error);
            Assert.Equal("new", TextOf(box));
        });
    }

    [Fact]
    public void Execute_WithoutClear_AppendsAtTheCaret()
    {
        WithRichTextBox("ab", box =>
        {
            box.CaretPosition = box.Document.ContentEnd;
            var result = TypeAction.Execute(box, "cd", clear: false);
            Drain();

            Assert.Null(result.Error);
            Assert.Equal("abcd", TextOf(box));
        });
    }

    [Fact]
    public void Execute_WithClearAndEmptyText_EmptiesTheEditor()
    {
        WithRichTextBox("gone", box =>
        {
            var result = TypeAction.Execute(box, "", clear: true);
            Drain();

            Assert.Null(result.Error);
            Assert.Equal("", TextOf(box));
        });
    }

    [Fact]
    public void Execute_WithControlCharacters_FailsBeforeTouchingTheEditor()
    {
        WithRichTextBox("keep", box =>
        {
            var result = TypeAction.Execute(box, "a\nb", clear: true);
            Drain();

            Assert.NotNull(result.Error);
            Assert.Contains("does not support text input", result.Error);
            Assert.Equal("keep", TextOf(box));
            Assert.Equal("", box.Selection.Text); // Ctrl+A 를 보내기 전에 거절했으므로 선택이 남지 않는다.
        });
    }

    [Fact]
    public void ExecuteOnFocused_TypesIntoWhateverHasFocus()
    {
        WithRichTextBox("", box =>
        {
            box.Focus();
            var result = TypeAction.ExecuteOnFocused("typed", clear: false);
            Drain();

            Assert.Null(result.Error);
            Assert.Equal("typed", TextOf(box));
        });
    }

    #region Helpers

    private static string TextOf(RichTextBox box)
        => new TextRange(box.Document.ContentStart, box.Document.ContentEnd).Text.TrimEnd();

    private static void Drain()
        => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);

    private static void WithRichTextBox(string initialText, Action<RichTextBox> body)
    {
        StaThread.Run(() =>
        {
            if (!InputSpoof.EnsureInstalled())
                return;

            var box = new RichTextBox();
            if (initialText.Length > 0)
                new TextRange(box.Document.ContentStart, box.Document.ContentEnd).Text = initialText;
            var window = new Window
            {
                Content = box, Width = 200, Height = 100, Left = -10000, Top = -10000,
                ShowActivated = false, ShowInTaskbar = false, WindowStyle = WindowStyle.None
            };
            try
            {
                window.Show();
                body(box);
            }
            finally
            {
                window.Close();
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
    }

    #endregion
}

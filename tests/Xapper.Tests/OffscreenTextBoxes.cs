using System.Windows;
using System.Windows.Controls;

namespace Xapper.Tests;

/// <summary>
/// 화면 밖에 창을 하나 세워 TextBox 두 개를 담고 첫 번째에 포커스를 준 뒤 본문을 실행하는 테스트 헬퍼.
/// InputManager 는 창이 실제로 있어야 입력을 라우팅하므로 창을 띄운다. 키 주입 테스트들이 같은 창을 쓴다.
/// </summary>
internal static class OffscreenTextBoxes
{
    /// <summary>TextBox 두 개(첫 번째 포커스)를 담은 창에서 본문을 실행합니다.</summary>
    public static void Run(Action<TextBox, TextBox> body)
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

    /// <summary>첫 번째 TextBox 만 쓰는 본문용 축약.</summary>
    public static void Run(Action<TextBox> body) => Run((box, _) => body(box));
}

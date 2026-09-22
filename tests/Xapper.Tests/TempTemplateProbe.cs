using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Xunit.Abstractions;

namespace Xapper.Tests;

public class TempTemplateProbe
{
    private readonly ITestOutputHelper _out;
    public TempTemplateProbe(ITestOutputHelper output) => _out = output;

    [Fact]
    public void Probe_TemplatedParentOfDataTemplateContent()
    {
        StaThread.Run(() =>
        {
            // DataTemplate 으로 만들어진 요소
            var dataTemplate = new DataTemplate();
            var inner = new FrameworkElementFactory(typeof(Border));
            inner.SetValue(FrameworkElement.NameProperty, "FromDataTemplate");
            inner.SetValue(FrameworkElement.WidthProperty, 100.0);
            inner.SetValue(FrameworkElement.HeightProperty, 40.0);
            inner.SetValue(Border.BackgroundProperty, Brushes.LightGreen);
            dataTemplate.VisualTree = inner;

            var host = new ContentControl { Content = "데이터", ContentTemplate = dataTemplate, Width = 200, Height = 80 };

            // 앱 XAML 에 직접 놓인 요소
            var direct = new Border { Name = "DirectChild", Width = 100, Height = 40, Background = Brushes.LightBlue };

            var panel = new StackPanel { Width = 300, Height = 300 };
            panel.Children.Add(host);
            panel.Children.Add(direct);
            panel.Measure(new Size(300, 300));
            panel.Arrange(new Rect(0, 0, 300, 300));
            panel.UpdateLayout();

            void Walk(DependencyObject node, int depth)
            {
                if (node is FrameworkElement fe)
                    _out.WriteLine($"{new string(' ', depth * 2)}{fe.GetType().Name} name='{fe.Name}' TemplatedParent={fe.TemplatedParent?.GetType().Name ?? "(null)"}");
                for (var i = 0; i < VisualTreeHelper.GetChildrenCount(node); i++)
                    Walk(VisualTreeHelper.GetChild(node, i), depth + 1);
            }

            Walk(panel, 0);
        });
    }
}

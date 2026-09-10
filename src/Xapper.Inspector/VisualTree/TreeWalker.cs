using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Xapper.Protocol;

namespace Xapper.Inspector.VisualTree;

/// <summary>
/// WPF 비주얼 트리를 재귀적으로 순회하여 ElementSnapshot 트리를 생성하는 클래스.
/// 각 노드의 타입, 이름, AutomationId, 텍스트, 상태, 바운딩 박스를 캡처.
/// </summary>
public sealed class TreeWalker
{
    /// <summary>
    /// 루트 요소부터 지정된 깊이까지 비주얼 트리를 순회합니다.
    /// 순회할 수 없는 노드를 만나면 그 가지만 건너뛰고 나머지 순회를 계속합니다.
    /// </summary>
    /// <param name="root">순회 시작 요소.</param>
    /// <param name="registry">요소를 등록할 참조 레지스트리.</param>
    /// <param name="maxDepth">최대 탐색 깊이.</param>
    /// <param name="skipped">건너뛴 노드의 설명을 기록할 목록.</param>
    /// <returns>루트 요소의 스냅샷 (자식 포함).</returns>
    public ElementSnapshot Walk(DependencyObject root, RefRegistry registry, int maxDepth, List<string> skipped)
    {
        return WalkElement(root, registry, 0, maxDepth, skipped);
    }

    #region Private Methods

    private ElementSnapshot WalkElement(DependencyObject element, RefRegistry registry, int depth, int maxDepth, List<string> skipped)
    {
        var refId = registry.Register(element);

        var snapshot = new ElementSnapshot
        {
            Ref = refId,
            Type = element.GetType().Name,
            Name = GetName(element),
            AutomationId = GetAutomationId(element),
            Text = GetText(element),
            IsEnabled = GetIsEnabled(element),
            IsVisible = GetIsVisible(element),
            Bounds = GetBounds(element)
        };

        if (depth < maxDepth)
        {
            foreach (var child in VisualChildren.Of(element, depth, skipped))
            {
                snapshot.Children.Add(WalkElement(child, registry, depth + 1, maxDepth, skipped));
            }
        }

        return snapshot;
    }

    private static string? GetName(DependencyObject element)
    {
        return element is FrameworkElement fe ? fe.Name : null;
    }

    private static string? GetAutomationId(DependencyObject element)
    {
        var id = AutomationProperties.GetAutomationId(element);
        return string.IsNullOrEmpty(id) ? null : id;
    }

    private static string? GetText(DependencyObject element)
    {
        return element switch
        {
            TextBlock tb => tb.Text,
            TextBox tb => tb.Text,
            ContentControl cc when cc.Content is string s => s,
            _ => null
        };
    }

    private static bool GetIsEnabled(DependencyObject element)
    {
        return element is UIElement ui && ui.IsEnabled;
    }

    private static bool GetIsVisible(DependencyObject element)
    {
        return element is UIElement ui && ui.Visibility == Visibility.Visible;
    }

    private static BoundingBox? GetBounds(DependencyObject element)
    {
        if (element is not UIElement ui)
            return null;

        var size = ui.RenderSize;
        if (size.Width == 0 && size.Height == 0)
            return null;

        try
        {
            var topLeft = ui.TranslatePoint(new Point(0, 0), Application.Current.MainWindow);
            return new BoundingBox
            {
                X = topLeft.X,
                Y = topLeft.Y,
                Width = size.Width,
                Height = size.Height
            };
        }
        catch
        {
            return null;
        }
    }

    #endregion
}

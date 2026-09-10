using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Xapper.Inspector.Actions;

namespace Xapper.Tests;

/// <summary>
/// 실제 마우스 입력 없이 수행하는 드래그를 검증하는 테스트 클래스.
/// 스플리터·슬라이더·스크롤바는 Thumb 의 드래그 이벤트로 움직이고 그 이벤트는 이동량을 숫자로 받으므로,
/// 커서를 옮기지 않고 조작할 수 있다. 다만 요소 아래 아무 Thumb 이나 집으면 스크롤 가능한 목록에서
/// 항목 드래그가 조용히 스크롤로 바뀌므로, 시작 지점에 실제로 놓인 것만 골라야 한다.
/// </summary>
[Collection(DesktopWindowCollection.Name)]
public class ThumbDragTests
{
    #region Helpers

    /// <summary>레이아웃과 렌더가 끝난 창을 띄워 본문에 넘기고, 끝나면 정리합니다.</summary>
    private static void WithWindow(Func<Grid, UIElement> build, Action<Window, UIElement> body)
    {
        StaThread.Run(() =>
        {
            var content = new Grid();
            var window = new Window
            {
                Width = 400, Height = 300, Left = 150, Top = 150,
                WindowStyle = WindowStyle.None, ShowActivated = false, Topmost = true, Content = content
            };

            try
            {
                var subject = build(content);
                window.Show();
                Settle();
                body(window, subject);
            }
            finally
            {
                window.Close();
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
    }

    private static void Settle()
    {
        for (var i = 0; i < 4; i++)
        {
            var frame = new DispatcherFrame();
            Dispatcher.CurrentDispatcher.BeginInvoke(
                DispatcherPriority.Background, new Action(() => frame.Continue = false));
            Dispatcher.PushFrame(frame);
        }
    }

    private static Point CentreOf(FrameworkElement element) =>
        element.PointToScreen(new Point(element.ActualWidth / 2, element.ActualHeight / 2));

    /// <summary>시각 트리에서 지정한 형식의 첫 자손을 찾습니다.</summary>
    private static T? DescendantOf<T>(DependencyObject node) where T : DependencyObject
    {
        if (node is T match) return match;
        var count = VisualTreeHelper.GetChildrenCount(node);
        for (var i = 0; i < count; i++)
        {
            var found = DescendantOf<T>(VisualTreeHelper.GetChild(node, i));
            if (found is not null) return found;
        }
        return null;
    }

    #endregion

    [Fact]
    public void FindAt_FindsASplitterAtTheStartPoint()
    {
        GridSplitter splitter = null!;
        WithWindow(content =>
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            splitter = new GridSplitter { HorizontalAlignment = HorizontalAlignment.Stretch, Background = Brushes.Gray };
            Grid.SetColumn(splitter, 1);
            grid.Children.Add(splitter);
            content.Children.Add(grid);
            return grid;
        },
        (_, _) => Assert.Same(splitter, ThumbDrag.FindAt(splitter, CentreOf(splitter))));
    }

    [Fact]
    public void FindAt_DoesNotTreatAnItemDragAsAScrollbarDrag()
    {
        ListBox list = null!;
        WithWindow(content =>
        {
            list = new ListBox { Height = 120 };
            for (var i = 0; i < 50; i++) list.Items.Add($"item {i}");
            content.Children.Add(list);
            return list;
        },
        (_, _) =>
        {
            // 목록에는 스크롤바가 있고 그 안에 Thumb 이 있다. 항목 위에서 시작한 드래그가 그것을 집으면
            // 요청은 조용히 스크롤로 바뀌고 성공으로 보고된다.
            Assert.NotNull(DescendantOf<Thumb>(list));

            var itemPoint = list.PointToScreen(new Point(20, 20));
            Assert.Null(ThumbDrag.FindAt(list, itemPoint));
        });
    }

    [Fact]
    public void FindAt_FindsTheScrollbarThumbWhenTheStartPointIsOnIt()
    {
        ListBox list = null!;
        WithWindow(content =>
        {
            list = new ListBox { Height = 120 };
            for (var i = 0; i < 50; i++) list.Items.Add($"item {i}");
            content.Children.Add(list);
            return list;
        },
        (_, _) =>
        {
            var thumb = DescendantOf<Thumb>(list);
            Assert.NotNull(thumb);

            Assert.Same(thumb, ThumbDrag.FindAt(list, CentreOf(thumb)));
        });
    }

    [Fact]
    public void Perform_MovesTheSplitterByExactlyTheGivenAmount()
    {
        Grid grid = null!;
        GridSplitter splitter = null!;
        WithWindow(content =>
        {
            grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(8) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            splitter = new GridSplitter { HorizontalAlignment = HorizontalAlignment.Stretch };
            Grid.SetColumn(splitter, 1);
            grid.Children.Add(splitter);
            content.Children.Add(grid);
            return grid;
        },
        (_, _) =>
        {
            var before = grid.ColumnDefinitions[0].ActualWidth;

            ThumbDrag.Perform(splitter, new Vector(60, 0), new Point(4, 10));
            grid.UpdateLayout();

            Assert.Equal(before + 60, grid.ColumnDefinitions[0].ActualWidth);
        });
    }
}

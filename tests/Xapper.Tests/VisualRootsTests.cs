using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Threading;
using Xapper.Inspector.VisualTree;
using Xapper.Protocol.Messages.Requests;

namespace Xapper.Tests;

/// <summary>
/// 최상위 창 열거와 좌표 조회를 검증하는 테스트 클래스.
/// 팝업·메뉴·드롭다운은 Window가 아니라 자기 최상위 창으로 뜨므로, 창 목록으로 순회하면 그 안이 보이지 않는다.
/// 이름도 AutomationId도 없는 컨트롤을 지목하는 유일한 길이 좌표 조회라 정확도가 곧 기능의 값어치다.
/// </summary>
[Collection(DesktopWindowCollection.Name)]
public class VisualRootsTests
{
    #region Helpers

    /// <summary>레이아웃이 끝난 창을 띄워 본문에 넘기고, 끝나면 정리합니다.</summary>
    private static void WithWindow(Action<Window, Grid> body)
    {
        StaThread.Run(() =>
        {
            var content = new Grid();
            var window = new Window
            {
                Width = 400, Height = 300, Left = 200, Top = 200,
                WindowStyle = WindowStyle.None, ShowActivated = false, Content = content,
                // 좌표 조회는 그 지점의 맨 앞 창을 찾는다. 다른 창이 덮으면 찾지 못하므로 맨 앞에 고정한다.
                Topmost = true
            };

            try
            {
                window.Show();
                Settle();
                body(window, content);
            }
            finally
            {
                window.Close();
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            }
        });
    }

    /// <summary>레이아웃과 렌더가 끝날 때까지 디스패처 큐를 비웁니다.</summary>
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

    /// <summary>부모의 시각 자식을 열거합니다. 팝업 요소가 트리에 실제로 들어갔는지 확인하는 데 씁니다.</summary>
    private static IEnumerable<DependencyObject> LogicalAndVisualChildrenOf(DependencyObject parent)
    {
        foreach (var child in System.Windows.LogicalTreeHelper.GetChildren(parent))
        {
            if (child is DependencyObject node)
                yield return node;
        }
    }

    /// <summary>요소의 화면상 중앙 좌표를 구합니다.</summary>
    private static Point CentreOf(FrameworkElement element) =>
        element.PointToScreen(new Point(element.ActualWidth / 2, element.ActualHeight / 2));

    #endregion

    [Fact]
    public void Sources_IncludeAnOpenPopup_WhichTheWindowTreeCannotReach()
    {
        WithWindow((window, content) =>
        {
            var popup = new Popup
            {
                Placement = PlacementMode.Absolute, HorizontalOffset = 220, VerticalOffset = 520,
                Width = 160, Height = 90,
                Child = new Border { Background = Brushes.Wheat, Child = new Button { Name = "insidePopup" } }
            };
            // 팝업 요소 자체를 부모 트리에 넣는다. 그래야 "부모 트리에 있는데도 그 내용에는 도달 못 한다"를
            // 실제로 검증하게 된다 — 넣지 않으면 애초에 트리 밖이라 단언이 저절로 성립한다.
            content.Children.Add(popup);
            popup.IsOpen = true;
            Settle();

            Assert.Contains(LogicalAndVisualChildrenOf(content), child => ReferenceEquals(child, popup));

            try
            {
                var finder = new ElementFinder();
                var request = new FindElementRequest { Name = "insidePopup" };

                var viaWindow = finder.Find(window, request, new RefRegistry());
                Assert.Empty(viaWindow.Matches);

                var viaRoots = 0;
                foreach (var source in VisualRoots.Sources())
                    viaRoots += finder.Find(source.RootVisual, request, new RefRegistry()).Matches.Count;

                Assert.Equal(1, viaRoots);
            }
            finally
            {
                popup.IsOpen = false;
            }
        });
    }


    [Fact]
    public void Sources_ExcludeWindowsOwnedByAnotherUiThread()
    {
        WithWindow((window, content) =>
        {
            content.Children.Add(new Button { Name = "onThisThread", Width = 120, Height = 40 });
            Settle();

            using var ready = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();

            // 다른 UI 스레드가 띄운 창. 입력 소스 목록은 프로세스 전체를 담으므로 여기에도 나타난다.
            var other = new Thread(() =>
            {
                var stranger = new Window { Width = 200, Height = 120, Left = 900, Top = 700, ShowActivated = false };
                stranger.Show();
                ready.Set();
                release.Wait(TimeSpan.FromSeconds(5));
                stranger.Close();
                Dispatcher.CurrentDispatcher.InvokeShutdown();
            })
            {
                IsBackground = true
            };
            other.SetApartmentState(ApartmentState.STA);
            other.Start();
            ready.Wait(TimeSpan.FromSeconds(5));

            try
            {
                var sources = VisualRoots.Sources();

                // 남의 스레드 요소는 읽는 것만으로 예외가 난다. 걸러지지 않으면 아래 순회가 통째로 무너진다.
                Assert.All(sources, source => Assert.True(source.CheckAccess()));

                var finder = new ElementFinder();
                var found = 0;
                foreach (var source in sources)
                    found += finder.Find(source.RootVisual, new FindElementRequest { Name = "onThisThread" }, new RefRegistry()).Matches.Count;

                Assert.Equal(1, found);
            }
            finally
            {
                release.Set();
                other.Join(TimeSpan.FromSeconds(5));
            }
        });
    }

    [Fact]
    public void ElementsAt_ReturnsTheDeepestElementFirstAndItsAncestors()
    {
        WithWindow((window, content) =>
        {
            var button = new Button { Name = "pickMe", Content = "hello", Width = 200, Height = 80 };
            content.Children.Add(button);
            Settle();

            // 이 테스트가 보는 것은 히트테스트 결과이지 화면 Z 순서가 아니다.
            // 창의 소스를 직접 얻어, 다른 앱이 잠깐 위를 덮어도 흔들리지 않게 한다.
            var source = (System.Windows.Interop.HwndSource)PresentationSource.FromVisual(window);

            var chain = VisualRoots.ElementsAt(source, CentreOf(button), maxAncestors: 6);

            Assert.NotEmpty(chain);
            Assert.Contains(chain, node => node is Button { Name: "pickMe" });
            // 가장 깊은 것은 보통 컨트롤 자신이 아니라 그 안의 조각이다.
            Assert.IsNotType<Button>(chain[0]);
        });
    }

    [Fact]
    public void SourceAt_FindsThisApplicationsWindowUnderThePoint()
    {
        WithWindow((window, content) =>
        {
            var button = new Button { Width = 200, Height = 80 };
            content.Children.Add(button);
            Settle();

            var source = VisualRoots.SourceAt(CentreOf(button));

            Assert.NotNull(source);
            Assert.Same(PresentationSource.FromVisual(window), source);
        });
    }

    [Fact]
    public void ElementsAt_ReportsTheCoveringElement_NotTheOneUnderneath()
    {
        WithWindow((window, content) =>
        {
            var button = new Button { Name = "underneath", Width = 200, Height = 80 };
            var cover = new Border { Name = "cover", Background = Brushes.Transparent };
            content.Children.Add(button);
            content.Children.Add(cover);
            Settle();

            var source = (System.Windows.Interop.HwndSource)PresentationSource.FromVisual(window);

            var chain = VisualRoots.ElementsAt(source, CentreOf(button), maxAncestors: 6);

            // 실제 클릭이 닿는 곳과 같아야 한다 — 덮은 쪽이 잡히고 밑의 버튼은 잡히지 않는다.
            Assert.Contains(chain, node => node is Border { Name: "cover" });
            Assert.DoesNotContain(chain, node => node is Button { Name: "underneath" });
        });
    }
}

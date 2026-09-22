using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Xapper.Inspector.Capture;

namespace Xapper.Tests;

/// <summary>
/// 번호 상자를 받을 요소를 고르는 규칙을 검증한다. 기준은 "그 자리를 클릭하면 실제로 닿는가" 하나이므로,
/// 가려진 요소와 겹쳐 그려지는 부모·자식이 모두 걸러져야 한다.
/// </summary>
public class MarkPickerTests
{
    #region Helpers

    /// <summary>레이아웃까지 끝낸 캔버스를 만듭니다. 히트테스트가 되도록 배경을 반드시 칠합니다.</summary>
    private static Canvas BuildCanvas(params UIElement[] children)
    {
        var canvas = new Canvas { Width = 400, Height = 300, Background = Brushes.White };
        foreach (var child in children)
            canvas.Children.Add(child);

        canvas.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        canvas.Arrange(new Rect(0, 0, 400, 300));
        canvas.UpdateLayout();
        return canvas;
    }

    /// <summary>캔버스의 지정된 자리에 놓이는, 히트테스트 되는 사각형을 만듭니다.</summary>
    private static Border Box(double x, double y, double width, double height)
    {
        var border = new Border { Width = width, Height = height, Background = Brushes.SteelBlue };
        Canvas.SetLeft(border, x);
        Canvas.SetTop(border, y);
        return border;
    }

    private static List<MarkCandidate> Pick(Canvas canvas, int maxMarks = 40)
        => MarkPicker.Pick(canvas, new Rect(0, 0, canvas.Width, canvas.Height), maxMarks, minSize: 16, out _);

    #endregion

    [Fact]
    public void Pick_KeepsElementsAClickCanReach()
    {
        StaThread.Run(() =>
        {
            var first = Box(10, 10, 80, 40);
            var second = Box(120, 10, 80, 40);
            var canvas = BuildCanvas(first, second);

            var marks = Pick(canvas);

            Assert.Contains(marks, mark => ReferenceEquals(mark.Element, first));
            Assert.Contains(marks, mark => ReferenceEquals(mark.Element, second));
        });
    }

    [Fact]
    public void Pick_SkipsElementsThatAreNotVisible()
    {
        StaThread.Run(() =>
        {
            var hidden = Box(10, 10, 80, 40);
            hidden.Visibility = Visibility.Collapsed;
            var canvas = BuildCanvas(hidden);

            Assert.DoesNotContain(Pick(canvas), mark => ReferenceEquals(mark.Element, hidden));
        });
    }

    [Fact]
    public void Pick_SkipsElementsSmallerThanTheMinimum()
    {
        StaThread.Run(() =>
        {
            var tiny = Box(10, 10, 6, 6);
            var canvas = BuildCanvas(tiny);

            Assert.DoesNotContain(Pick(canvas), mark => ReferenceEquals(mark.Element, tiny));
        });
    }

    [Fact]
    public void Pick_SkipsAnElementAnotherOneCoversCompletely()
    {
        StaThread.Run(() =>
        {
            var covered = Box(10, 10, 80, 40);
            var cover = Box(10, 10, 80, 40);
            // 뒤에 추가된 것이 위에 그려진다: covered 의 중심에서 히트테스트를 하면 cover 가 잡힌다.
            var canvas = BuildCanvas(covered, cover);

            var marks = Pick(canvas);

            Assert.DoesNotContain(marks, mark => ReferenceEquals(mark.Element, covered));
            Assert.Contains(marks, mark => ReferenceEquals(mark.Element, cover));
        });
    }

    [Fact]
    public void Pick_MarksAButtonOnceEvenThoughItsContentIsAnElementToo()
    {
        StaThread.Run(() =>
        {
            var button = new Button { Width = 100, Height = 40, Content = "Log In" };
            Canvas.SetLeft(button, 10);
            Canvas.SetTop(button, 10);
            var canvas = BuildCanvas(button);

            var marks = Pick(canvas);

            // 버튼의 중심에서 잡히는 것은 버튼의 후손이므로 버튼만 남고, 내부 TextBlock 은 자기 중심에서
            // 자기(혹은 자기 후손)가 잡히므로 함께 남을 수 있다. 상자가 버튼 하나당 한 겹을 넘지 않는지만 본다.
            Assert.Contains(marks, mark => ReferenceEquals(mark.Element, button));
            Assert.All(marks, mark => Assert.False(mark.Rect.IsEmpty));
        });
    }

    [Fact]
    public void Pick_WhenThereAreMoreThanTheCap_KeepsTheSmallerOnesAndReportsTheRest()
    {
        StaThread.Run(() =>
        {
            var small = Box(10, 10, 20, 20);
            var medium = Box(40, 10, 60, 60);
            var large = Box(120, 10, 200, 200);
            var canvas = BuildCanvas(large, medium, small);

            var marks = MarkPicker.Pick(canvas, new Rect(0, 0, 400, 300), maxMarks: 1, minSize: 16, out var omitted);

            var kept = Assert.Single(marks);
            Assert.Same(small, kept.Element);
            Assert.True(omitted >= 1);
        });
    }

    [Fact]
    public void Pick_SkipsContainersThatHoldOtherMarks()
    {
        StaThread.Run(() =>
        {
            // 실측에서 드러난 문제다: 컨테이너까지 상자를 받으면 26개가 그려져 그림이 테두리로 뒤덮였다.
            var inner = Box(20, 20, 40, 40);
            var container = new Border { Width = 200, Height = 150, Background = Brushes.Gainsboro };
            Canvas.SetLeft(container, 10);
            Canvas.SetTop(container, 10);
            var canvas = BuildCanvas(container, inner);

            var marks = Pick(canvas);

            Assert.Contains(marks, mark => ReferenceEquals(mark.Element, inner));
            Assert.DoesNotContain(marks, mark => ReferenceEquals(mark.Element, container));
        });
    }

    [Fact]
    public void Pick_KeepsOnlyTheOutermostOfElementsSharingTheSameBox()
    {
        StaThread.Run(() =>
        {
            // 컨트롤 템플릿은 항목과 똑같은 크기의 Border 를 두는 일이 흔하다. 둘 다 상자를 받으면 테두리가 겹친다.
            var inner = new Border { Width = 80, Height = 40, Background = Brushes.SteelBlue };
            var outer = new Border { Background = Brushes.SteelBlue, Child = inner };
            Canvas.SetLeft(outer, 10);
            Canvas.SetTop(outer, 10);
            var canvas = BuildCanvas(outer);

            var marks = Pick(canvas);

            Assert.Contains(marks, mark => ReferenceEquals(mark.Element, outer));
            Assert.DoesNotContain(marks, mark => ReferenceEquals(mark.Element, inner));
        });
    }

    [Fact]
    public void Pick_GivesNoAnchorToAnElementASelectorCanAlreadyReach()
    {
        StaThread.Run(() =>
        {
            var named = Box(10, 10, 80, 40);
            named.SetValue(System.Windows.Automation.AutomationProperties.AutomationIdProperty, "SaveButton");
            var canvas = BuildCanvas(named);

            var mark = Assert.Single(Pick(canvas), candidate => ReferenceEquals(candidate.Element, named));

            // id 가 있으면 셀렉터로 닿으므로 좌표를 붙일 이유가 없다.
            Assert.Null(mark.Anchor);
            Assert.Null(mark.AnchorX);
        });
    }

    [Fact]
    public void Pick_AnchorsANamelessElementToItsNearestUniqueAncestor()
    {
        StaThread.Run(() =>
        {
            var nameless = new Border { Width = 40, Height = 20, Background = Brushes.SteelBlue };
            var panel = new Canvas { Width = 200, Height = 100, Background = Brushes.WhiteSmoke };
            panel.SetValue(System.Windows.Automation.AutomationProperties.AutomationIdProperty, "LoginPanel");
            Canvas.SetLeft(nameless, 80);
            Canvas.SetTop(nameless, 40);
            panel.Children.Add(nameless);
            Canvas.SetLeft(panel, 0);
            Canvas.SetTop(panel, 0);
            var canvas = BuildCanvas(panel);

            var mark = Assert.Single(Pick(canvas), candidate => ReferenceEquals(candidate.Element, nameless));

            Assert.Equal("id=LoginPanel", mark.Anchor);
            // 상자 중심은 패널 안에서 가로 (80+20)/200 = 0.5, 세로 (40+10)/100 = 0.5 이다.
            Assert.NotNull(mark.AnchorX);
            Assert.Equal(0.5, mark.AnchorX.Value, precision: 2);
            Assert.Equal(0.5, mark.AnchorY.Value, precision: 2);
        });
    }

    [Fact]
    public void Pick_RefusesAnAnchorWhoseIdIsNotUnique()
    {
        StaThread.Run(() =>
        {
            // 같은 id 가 둘이면 target 셀렉터가 실행을 거부하므로, 기준점으로 저장해서는 안 된다.
            var nameless = new Border { Width = 40, Height = 20, Background = Brushes.SteelBlue };
            var panel = new Canvas { Width = 200, Height = 100, Background = Brushes.WhiteSmoke };
            panel.SetValue(System.Windows.Automation.AutomationProperties.AutomationIdProperty, "Twin");
            Canvas.SetLeft(nameless, 80);
            Canvas.SetTop(nameless, 40);
            panel.Children.Add(nameless);

            var twin = new Canvas { Width = 60, Height = 40, Background = Brushes.WhiteSmoke };
            twin.SetValue(System.Windows.Automation.AutomationProperties.AutomationIdProperty, "Twin");
            Canvas.SetLeft(twin, 220);
            Canvas.SetTop(twin, 10);

            var canvas = BuildCanvas(panel, twin);

            var mark = Assert.Single(Pick(canvas), candidate => ReferenceEquals(candidate.Element, nameless));

            Assert.Null(mark.Anchor);
        });
    }

    [Fact]
    public void Pick_NumbersMarksFromOneInOrder()
    {
        StaThread.Run(() =>
        {
            var canvas = BuildCanvas(Box(10, 10, 40, 40), Box(60, 10, 40, 40));

            var marks = Pick(canvas);

            Assert.Equal(Enumerable.Range(1, marks.Count), marks.Select(mark => mark.Number));
        });
    }
}

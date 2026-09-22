using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Xapper.Inspector.Capture;

namespace Xapper.Tests;

/// <summary>
/// 화면 기록에 담을 요소를 고르는 규칙을 검증하는 테스트 클래스.
/// </summary>
public class ScreenRegionPickerTests
{
    #region Helpers

    /// <summary>레이아웃까지 끝낸 캔버스를 만듭니다.</summary>
    private static Canvas BuildCanvas(double width, double height, params UIElement[] children)
    {
        var canvas = new Canvas { Width = width, Height = height, Background = Brushes.White };
        foreach (var child in children)
            canvas.Children.Add(child);

        canvas.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        canvas.Arrange(new Rect(0, 0, width, height));
        canvas.UpdateLayout();
        return canvas;
    }

    /// <summary>자동화 ID 가 붙은, 앱 작성자가 놓았을 법한 컨트롤을 만듭니다.</summary>
    private static Border Named(string automationId, double x, double y)
    {
        var border = new Border { Width = 140, Height = 36, Background = Brushes.LightGray };
        border.SetValue(System.Windows.Automation.AutomationProperties.AutomationIdProperty, automationId);
        Canvas.SetLeft(border, x);
        Canvas.SetTop(border, y);
        return border;
    }

    /// <summary>안쪽에 작은 부품 하나를 두는 컨트롤 템플릿을 만듭니다. 그리드 셀을 흉내 낸다.</summary>
    private static ControlTemplate SmallPartTemplate()
    {
        var template = new ControlTemplate(typeof(Button));
        var part = new FrameworkElementFactory(typeof(Border));
        part.SetValue(FrameworkElement.WidthProperty, 20.0);
        part.SetValue(FrameworkElement.HeightProperty, 20.0);
        part.SetValue(Border.BackgroundProperty, Brushes.SteelBlue);
        template.VisualTree = part;
        return template;
    }

    /// <summary>자동화 ID 목록으로 옮깁니다.</summary>
    private static List<string> IdsOf(IEnumerable<UIElement> elements)
        => elements
            .Select(element => System.Windows.Automation.AutomationProperties.GetAutomationId(element))
            .ToList();

    #endregion

    [Fact]
    public void Pick_KeepsNamedControlsOnAGridLikeScreen()
    {
        StaThread.Run(() =>
        {
            // annotate 의 고르기를 그대로 쓰면 작은 것부터 40개를 남기기 때문에, 이 화면에서는 템플릿이 만든
            // 작은 부품이 상한을 다 채우고 이름 있는 컨트롤이 하나도 남지 않았다(실측). 기록은 반대로 골라야 한다.
            var children = new List<UIElement>();
            for (var i = 0; i < 12; i++)
                children.Add(Named($"AppButton{i}", 800, 10 + i * 40));

            var template = SmallPartTemplate();
            for (var i = 0; i < 200; i++)
            {
                var cell = new Button { Width = 36, Height = 36, Template = template };
                Canvas.SetLeft(cell, 10 + i % 20 * 38);
                Canvas.SetTop(cell, 10 + i / 20 * 38);
                children.Add(cell);
            }

            var canvas = BuildCanvas(960, 640, children.ToArray());

            var picked = ScreenRegionPicker.Pick(canvas, maxRegions: 50);

            Assert.Equal(
                Enumerable.Range(0, 12).Select(i => $"AppButton{i}"),
                IdsOf(picked));
        });
    }

    [Fact]
    public void Pick_SkipsPartsAControlTemplateBuilt()
    {
        StaThread.Run(() =>
        {
            // 템플릿 안쪽 부품에 이름이 붙어 있어도 기록에 넣지 않는다. 스크롤바의 PART_Track 처럼
            // 앱 작성자가 놓은 것이 아니라 컨트롤의 내부 구조이기 때문이다.
            var template = new ControlTemplate(typeof(Button));
            var part = new FrameworkElementFactory(typeof(Border));
            part.SetValue(FrameworkElement.WidthProperty, 40.0);
            part.SetValue(FrameworkElement.HeightProperty, 20.0);
            part.SetValue(Border.BackgroundProperty, Brushes.SteelBlue);
            part.SetValue(System.Windows.Automation.AutomationProperties.AutomationIdProperty, "PART_Track");
            template.VisualTree = part;

            var button = new Button { Width = 60, Height = 30, Template = template };
            button.SetValue(System.Windows.Automation.AutomationProperties.AutomationIdProperty, "SaveButton");
            Canvas.SetLeft(button, 10);
            Canvas.SetTop(button, 10);

            var canvas = BuildCanvas(400, 300, button);

            var picked = ScreenRegionPicker.Pick(canvas, maxRegions: 50);

            Assert.Equal(["SaveButton"], IdsOf(picked));
        });
    }

    [Fact]
    public void Pick_LooksInsideTemplatePartsForTheAppsOwnContent()
    {
        StaThread.Run(() =>
        {
            // 창의 내용물은 창 템플릿 안쪽 ContentPresenter 아래에 놓인다. 그러니 앱 컨트롤에 닿으려면
            // 반드시 템플릿 부품을 먼저 지나야 한다. 부품에서 멈추면 화면 전체를 놓친다(실측: 요소 576개짜리
            // VisualPro 화면에서 셀렉터를 가진 영역이 0개였다).
            var template = new ControlTemplate(typeof(ContentControl));
            var chrome = new FrameworkElementFactory(typeof(Border));
            chrome.SetValue(FrameworkElement.NameProperty, "PART_WindowRoot");
            chrome.SetValue(Border.BackgroundProperty, Brushes.WhiteSmoke);
            var host = new FrameworkElementFactory(typeof(ContentPresenter));
            chrome.AppendChild(host);
            template.VisualTree = chrome;

            var shell = new ContentControl
            {
                Width = 360,
                Height = 260,
                Template = template,
                Content = Named("AppControl", 0, 0)
            };
            Canvas.SetLeft(shell, 10);
            Canvas.SetTop(shell, 10);

            var canvas = BuildCanvas(400, 300, shell);

            Assert.Equal(["AppControl"], IdsOf(ScreenRegionPicker.Pick(canvas, maxRegions: 50)));
        });
    }

    [Fact]
    public void Pick_SkipsAnIdThatIsNotUnique()
    {
        StaThread.Run(() =>
        {
            // 같은 id 가 둘이면 target 셀렉터가 실행을 거부하므로, 기록해 두어도 다음 방문에 쓸 수 없다.
            var canvas = BuildCanvas(400, 300,
                Named("Twin", 10, 10), Named("Twin", 10, 60), Named("Only", 10, 110));

            var picked = ScreenRegionPicker.Pick(canvas, maxRegions: 50);

            Assert.Equal(["Only"], IdsOf(picked));
        });
    }

    [Fact]
    public void Pick_PrefersTheIdOverTheNameWhenJudgingUniqueness()
    {
        StaThread.Run(() =>
        {
            // 저장할 때 id 를 먼저 쓰므로, id 가 겹치면 이름이 유일하더라도 넣지 않는다.
            // 넣었다가는 기록에 남는 셀렉터가 정작 그 겹치는 id 가 된다.
            var first = Named("Twin", 10, 10);
            first.Name = "UniqueName";
            var second = Named("Twin", 10, 60);

            var canvas = BuildCanvas(400, 300, first, second);

            Assert.Empty(ScreenRegionPicker.Pick(canvas, maxRegions: 50));
        });
    }

    [Fact]
    public void Pick_SkipsHiddenBranches()
    {
        StaThread.Run(() =>
        {
            var hidden = new Canvas { Width = 200, Height = 100, Visibility = Visibility.Collapsed };
            hidden.SetValue(System.Windows.Automation.AutomationProperties.AutomationIdProperty, "HiddenPanel");
            hidden.Children.Add(Named("InsideHidden", 0, 0));

            var canvas = BuildCanvas(400, 300, hidden, Named("Shown", 10, 150));

            Assert.Equal(["Shown"], IdsOf(ScreenRegionPicker.Pick(canvas, maxRegions: 50)));
        });
    }

    [Fact]
    public void Pick_StopsAtTheCap()
    {
        StaThread.Run(() =>
        {
            var children = Enumerable.Range(0, 20)
                .Select(i => (UIElement)Named($"Control{i}", 10, i * 15))
                .ToArray();

            var canvas = BuildCanvas(400, 400, children);

            var picked = ScreenRegionPicker.Pick(canvas, maxRegions: 5);

            Assert.Equal(
                Enumerable.Range(0, 5).Select(i => $"Control{i}"),
                IdsOf(picked));
        });
    }
}

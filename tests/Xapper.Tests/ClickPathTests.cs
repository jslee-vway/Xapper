using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Xapper.Inspector.Actions;
using Xapper.Inspector.Diagnostics;
using Xapper.Inspector.VisualTree;

namespace Xapper.Tests;

/// <summary>
/// 좌표 없는 클릭이 실제로 어느 경로를 타는지, 그리고 그 경로가 결과에 드러나는지 검증하는 테스트 클래스.
/// 접근성 경로는 마우스 이벤트를 전혀 만들지 않으므로, 마우스 핸들러에 의존하는 요소는
/// 아무 일도 일어나지 않은 채 성공으로 보고될 수 있었다.
/// </summary>
public class ClickPathTests
{
    #region Helpers

    /// <summary>
    /// 디스패처 큐를 Background 우선순위까지 비웁니다. 큐에 걸린 작업이 실제로 실행됐는지 확인하는 데 쓴다.
    /// </summary>
    private static void PumpDispatcher()
    {
        var frame = new DispatcherFrame();
        Dispatcher.CurrentDispatcher.BeginInvoke(
            DispatcherPriority.Background, new Action(() => frame.Continue = false));
        Dispatcher.PushFrame(frame);
    }

    #endregion

    [Fact]
    public void Click_OnButton_UsesTheAutomationPatternAndSaysSo()
    {
        StaThread.Run(() =>
        {
            var button = new Button { Content = "OK" };
            var clicked = false;
            button.Click += (_, _) => clicked = true;

            var outcome = ClickAction.Execute(button);

            Assert.Contains("automation Invoke pattern", outcome.Path);
            Assert.Contains("no mouse events raised", outcome.Path);

            // 접근성 Invoke는 즉시 실행되지 않고 디스패처에 걸린다. 큐를 비우기 전에는 핸들러가 돌지 않는다.
            Assert.False(clicked);
            PumpDispatcher();
            Assert.True(clicked);
        });
    }

    [Fact]
    public void Click_OnElementWithOnlyMouseHandlers_FallsBackToMouseEventsAndSaysSo()
    {
        StaThread.Run(() =>
        {
            var border = new Border();
            var released = false;
            border.MouseLeftButtonUp += (_, _) => released = true;

            var outcome = ClickAction.Execute(border);

            Assert.Contains("simulated routed mouse events", outcome.Path);
            Assert.True(released);
        });
    }
}

/// <summary>
/// 속성 조회가 지원하지 않는 입력을 어떻게 알리는지 검증하는 테스트 클래스.
/// </summary>
public class PropertyReaderTests
{
    [Fact]
    public void ReadProperty_WithDottedName_ReturnsAnErrorWithoutThrowing()
    {
        StaThread.Run(() =>
        {
            // 던지지 않고 값으로 알린다: 주입된 프로세스 안에서 던지면 대상 앱을 죽일 수 있다(결함 2).
            var result = PropertyReader.ReadProperty(new Button(), "ItemsSource.Count");

            Assert.Null(result.Response);
            Assert.NotNull(result.Error);
            Assert.Contains("Property paths are not supported", result.Error);
            Assert.Contains("ItemsSource", result.Error);
        });
    }

    [Fact]
    public void ReadProperty_WithMissingProperty_ReturnsAnErrorWithoutThrowing()
    {
        StaThread.Run(() =>
        {
            var result = PropertyReader.ReadProperty(new Button(), "ThisPropertyDoesNotExist");

            Assert.Null(result.Response);
            Assert.NotNull(result.Error);
            Assert.Contains("not found", result.Error);
        });
    }

    [Fact]
    public void ReadProperty_WithRealProperty_ReturnsTheValue()
    {
        StaThread.Run(() =>
        {
            var result = PropertyReader.ReadProperty(new Button { IsEnabled = false }, "IsEnabled");

            Assert.Null(result.Error);
            if (result.Response is not { } response)
            {
                Assert.Fail("expected a property value");
                return;
            }
            Assert.Equal("IsEnabled", response.PropertyName);
            Assert.Equal("False", response.Value);
        });
    }
}

/// <summary>
/// 참조 번호의 수명을 검증하는 테스트 클래스.
/// 도구 설명이 "검색은 기존 번호를 무효화하지 않는다"고 약속하므로 그 약속을 고정한다.
/// </summary>
public class RefRegistryTests
{
    [Fact]
    public void Register_DoesNotInvalidateEarlierRefs()
    {
        StaThread.Run(() =>
        {
            var registry = new RefRegistry();
            var element = new Button();

            var first = registry.Register(element);
            var second = registry.Register(element);

            Assert.NotEqual(first, second);
            Assert.Same(element, registry.Resolve(first));
            Assert.Equal(0, registry.Generation);
        });
    }

    [Fact]
    public void Clear_InvalidatesEveryRefAndAdvancesTheGeneration()
    {
        StaThread.Run(() =>
        {
            var registry = new RefRegistry();
            var existing = registry.Register(new Button());

            registry.Clear();

            Assert.Null(registry.Resolve(existing));
            Assert.Equal(1, registry.Generation);
        });
    }
}

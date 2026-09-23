using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Xapper.Inspector.Events;

namespace Xapper.Tests;

/// <summary>
/// 앱이 일으킨 이벤트를 담고 꺼내는 규칙을 검증하는 테스트 클래스.
/// </summary>
public class EventLogTests
{
    #region Helpers

    private static LoggedEvent Entry(string name, string type = "Button", string? elementName = null)
        => new(Environment.TickCount64, name, type, elementName, null);

    #endregion

    #region 담아 두기

    [Fact]
    public void Add_KeepsOnlyTheMostRecent_WhenTheBufferIsFull()
    {
        // 이벤트는 사람이 손을 조금만 움직여도 쏟아진다. 전부 모아 두면 메모리가 늘고, 오래된 것은 쓸모가 없다.
        var log = new EventLog();
        for (var i = 0; i < EventLog.Capacity + 50; i++)
            log.Add(Entry($"Event{i}"));

        Assert.Equal(EventLog.Capacity, log.Count);

        var recent = log.Recent(1);
        Assert.Equal($"Event{EventLog.Capacity + 49}", Assert.Single(recent).Name);
    }

    [Fact]
    public void Recent_ReturnsTheNewestOnes_WithTheNewestLast()
    {
        // 마지막 줄이 가장 최근이어야 "방금 무슨 일이 있었나" 를 위에서 아래로 읽을 수 있다.
        var log = new EventLog();
        log.Add(Entry("First"));
        log.Add(Entry("Second"));
        log.Add(Entry("Third"));

        var recent = log.Recent(2);

        Assert.Equal(["Second", "Third"], recent.Select(entry => entry.Name));
    }

    [Fact]
    public void Recent_ReturnsNothing_WhenNoneIsAskedFor()
    {
        var log = new EventLog();
        log.Add(Entry("First"));

        Assert.Empty(log.Recent(0));
    }

    #endregion

    #region 거르기

    [Fact]
    public void Recent_FiltersOnTheElementAsWellAsTheEventName()
    {
        // 이벤트 이름만으로 거르면 "그 그리드에서 무슨 일이 있었나" 를 물을 수 없다.
        var log = new EventLog();
        log.Add(Entry("SelectionChanged", "GridControl", "failureGrid"));
        log.Add(Entry("Click", "Button", "SaveButton"));

        Assert.Equal("SelectionChanged", Assert.Single(log.Recent(10, "failureGrid")).Name);
        Assert.Equal("Click", Assert.Single(log.Recent(10, "SaveButton")).Name);
        Assert.Equal("SelectionChanged", Assert.Single(log.Recent(10, "grid")).Name);
    }

    [Fact]
    public void IsNoise_DropsTheEventsNobodyCanActOn()
    {
        // 마우스를 몇 센티미터 움직이는 것만으로 수백 건이 되므로, 담아 두면 정작 필요한 것이 밀려난다.
        Assert.True(EventWatcher.IsNoise("MouseMove"));
        Assert.True(EventWatcher.IsNoise("PreviewMouseMove"));
        Assert.True(EventWatcher.IsNoise("LayoutUpdated"));

        Assert.False(EventWatcher.IsNoise("Click"));
        Assert.False(EventWatcher.IsNoise("SelectionChanged"));
        Assert.False(EventWatcher.IsNoise("TextChanged"));
    }

    #endregion

    #region 실제 이벤트 잡기

    [Fact]
    public void Start_CatchesAClickRaisedOnARealElement()
    {
        StaThread.Run(() =>
        {
            // 버튼을 먼저 만든다. 라우티드 이벤트는 그 타입이 처음 쓰일 때 등록되므로, 만들기 전에는
            // ButtonBase.ClickEvent 자체가 존재하지 않아 걸 대상이 없다.
            var button = new Button { Name = "TheOneBeingWatched", Content = "누르기" };
            EventWatcher.EnsureRegistered();

            button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent, button));

            var caught = EventWatcher.Log.Recent(EventLog.Capacity, "TheOneBeingWatched");

            Assert.Contains(caught, entry => entry.Name == "Click" && entry.SourceType == "Button");
        });
    }

    #endregion
}

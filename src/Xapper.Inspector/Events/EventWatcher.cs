using System.Windows;
using System.Windows.Controls;

namespace Xapper.Inspector.Events;

/// <summary>
/// 앱이 일으키는 라우티드 이벤트를 엿보아 <see cref="EventLog"/> 에 쌓는다.
///
/// 이것이 있는 이유는 시각 트리가 답하지 못하는 물음이 있기 때문이다. 고성능 그리드는 셀을 직접 그려서
/// 셀이 요소로 존재하지 않으므로, "행을 눌렀는데 눌렸는가" 를 트리에서는 확인할 수 없고 그림밖에 길이 없었다.
/// 그런데 요소가 없어도 선택이 바뀌면 이벤트는 난다. 그래서 확인의 통로를 하나 더 낸다.
///
/// 예외를 던지지 않는다. 대상 앱 안에서 도는 코드이므로(결함 2 원칙) 읽을 수 없는 것은 건너뛴다.
/// </summary>
public static class EventWatcher
{
    #region Constants

    /// <summary>
    /// 값이 없으면서 쏟아지는 이벤트. 사람이 무엇을 했는지 알려 주지 않으면서 버퍼만 밀어낸다.
    /// 마우스를 몇 센티미터 움직이는 것만으로 수백 건이 되므로, 담아 두면 정작 필요한 것이 밀려난다.
    /// </summary>
    private static readonly string[] Noise =
    [
        "MouseMove",
        "QueryCursor",
        "GiveFeedback",
        "DragOver",
        "MouseEnter",
        "MouseLeave",
        "MouseHover",
        "LayoutUpdated",
        "RequestBringIntoView",
        "SizeChanged",
        "ToolTipOpening",
        "ToolTipClosing"
    ];

    #endregion

    #region Fields

    private static readonly object Gate = new();

    /// <summary>이미 핸들러를 건 이벤트. 다시 훑을 때 같은 것에 두 번 걸지 않으려고 기억해 둔다.</summary>
    private static readonly HashSet<RoutedEvent> Registered = [];

    #endregion

    #region Public Properties

    /// <summary>쌓인 이벤트. 감시를 시작하지 않았으면 비어 있다.</summary>
    public static EventLog Log { get; } = new();

    /// <summary>핸들러를 걸어 둔 이벤트의 수. 시험과 진단이 본다.</summary>
    public static int WatchedCount
    {
        get
        {
            lock (Gate)
                return Registered.Count;
        }
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// 지금 등록되어 있는 라우티드 이벤트에 핸들러를 겁니다. 이미 건 것은 건너뛰므로 몇 번이고 불러도 된다.
    ///
    /// 한 번만 걸고 끝내지 않는 이유가 있다. 라우티드 이벤트는 그 컨트롤 타입이 처음 쓰일 때 정적 생성자에서
    /// 등록되므로, 아직 화면에 나온 적 없는 대화상자나 편집기의 이벤트는 그 시점에 존재하지도 않는다
    /// (실측: 감시를 먼저 걸고 버튼을 만들었더니 Click 이 잡히지 않았다). 그래서 물어볼 때마다 다시 훑어 보탠다.
    ///
    /// 표준 타입의 이벤트만 골라 걸지 않는 이유도 있다. 상용 컨트롤 묶음은 자기 이벤트를 자기 어셈블리에
    /// 등록하므로, 미리 정한 목록으로는 정작 확인하고 싶은 그리드 이벤트를 놓친다.
    /// </summary>
    /// <returns>이번에 새로 건 이벤트의 수.</returns>
    /// <remarks>UI 스레드에서 호출해야 합니다.</remarks>
    public static int EnsureRegistered()
    {
        var handler = new RoutedEventHandler(OnEvent);
        var added = 0;

        foreach (var routedEvent in RoutedEventsWorthWatching())
        {
            lock (Gate)
            {
                if (!Registered.Add(routedEvent))
                    continue;
            }

            try
            {
                // 처리된 이벤트도 받아야 한다. 앱이 이미 받아 처리한 것이야말로 "먹혔다" 는 증거다.
                EventManager.RegisterClassHandler(typeof(UIElement), routedEvent, handler, handledEventsToo: true);
                added++;
            }
            catch (Exception)
            {
                // 이 이벤트 하나를 못 걸었다고 나머지를 포기할 이유가 없다.
            }
        }

        return added;
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// 걸어 둘 이벤트를 고릅니다.
    /// 터널링(Preview) 은 같은 일을 한 번 더 적는 셈이라 뺀다. 버블링만 보아도 무슨 일이 있었는지는 같다.
    /// </summary>
    private static IEnumerable<RoutedEvent> RoutedEventsWorthWatching()
    {
        RoutedEvent[] all;
        try
        {
            all = EventManager.GetRoutedEvents();
        }
        catch (Exception)
        {
            return [];
        }

        return all.Where(routedEvent =>
            routedEvent.RoutingStrategy != RoutingStrategy.Tunnel && !IsNoise(routedEvent.Name));
    }

    /// <summary>담아 둘 값어치가 없는 이름인지.</summary>
    internal static bool IsNoise(string name)
    {
        return Noise.Any(noisy => name.Contains(noisy, StringComparison.Ordinal));
    }

    /// <summary>
    /// 이벤트 한 건을 버퍼에 담습니다. UI 스레드에서 도는 자리이므로 여기서 하는 일을 최소로 유지한다.
    /// </summary>
    private static void OnEvent(object sender, RoutedEventArgs args)
    {
        try
        {
            if (args.OriginalSource is not DependencyObject source)
                return;

            Log.Add(new LoggedEvent(
                Environment.TickCount64,
                args.RoutedEvent?.Name ?? "(unknown)",
                source.GetType().Name,
                NameOf(source),
                DetailOf(args)));
        }
        catch (Exception)
        {
            // 엿보기가 앱을 방해해서는 안 된다.
        }
    }

    /// <summary>요소를 가리키는 이름. 자동화 ID 를 먼저 보고, 없으면 x:Name 을 본다. 둘 다 없으면 null.</summary>
    private static string? NameOf(DependencyObject source)
    {
        try
        {
            var id = System.Windows.Automation.AutomationProperties.GetAutomationId(source);
            if (!string.IsNullOrEmpty(id))
                return id;

            var name = (source as FrameworkElement)?.Name;
            return string.IsNullOrEmpty(name) ? null : name;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// 이벤트가 값을 들고 있으면 그 값을 한 조각으로 옮깁니다.
    /// 타입을 늘어놓지 않고 표준 몇 가지만 본다. 나머지는 이벤트 이름과 요소만으로도 무슨 일인지 알 수 있다.
    /// </summary>
    private static string? DetailOf(RoutedEventArgs args)
    {
        try
        {
            return args switch
            {
                SelectionChangedEventArgs selection when selection.AddedItems.Count > 0
                    => $"selected {Describe(selection.AddedItems[0])}",
                TextChangedEventArgs when args.OriginalSource is TextBox box
                    => $"text is now \"{Shorten(box.Text)}\"",
                RoutedPropertyChangedEventArgs<object> changed when changed.NewValue is not null
                    => $"now {Describe(changed.NewValue)}",
                _ => null
            };
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>값 하나를 사람이 읽을 한 조각으로. 길면 자른다.</summary>
    private static string Describe(object? value)
    {
        if (value is null)
            return "(null)";

        var text = value as string ?? value.ToString();
        return string.IsNullOrEmpty(text) ? value.GetType().Name : Shorten(text);
    }

    /// <summary>응답이 길어지지 않도록 자릅니다.</summary>
    private static string Shorten(string text)
    {
        const int limit = 60;
        return text.Length <= limit ? text : text[..limit] + "…";
    }

    #endregion
}

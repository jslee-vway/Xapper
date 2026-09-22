using System.Windows;

namespace Xapper.Inspector.AutoWait;

/// <summary>
/// 요소가 상호작용 가능한 상태(보이고, 활성이고, 로드됨)가 될 때까지 폴링 방식으로 기다린다.
/// 모든 액션 앞에 놓여 UI 가 아직 준비되지 않아 생기는 경합을 막는다.
/// </summary>
public sealed class ElementWaiter
{
    #region Fields

    private readonly TimeSpan _timeout;
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// 보이고 로드도 끝났는데 활성만 아닌 요소를 더 기다려 줄 시간.
    ///
    /// 이 경우만 따로 짧게 끊는 이유가 있다. 활성 여부는 속성 변경 알림을 타고 바뀌므로 대개 한두 프레임
    /// 안에 결정되는 반면, 화면이 아직 안 보이거나 로드 중인 상황은 실제로 몇 초가 걸릴 수 있다.
    /// 그래서 앞의 경우에 제한 시간을 다 쓰는 것은 기다림이 아니라 낭비다(실측: 비활성 버튼을 세 번 눌러
    /// 15초와 왕복 세 번을 버렸다).
    /// </summary>
    private static readonly TimeSpan DisabledGrace = TimeSpan.FromMilliseconds(500);

    #endregion

    #region Constructor

    /// <summary>
    /// <see cref="ElementWaiter"/>의 새 인스턴스를 생성합니다.
    /// </summary>
    /// <param name="timeout">최대 대기 시간.</param>
    public ElementWaiter(TimeSpan timeout)
    {
        _timeout = timeout;
    }

    #endregion

    #region Properties

    /// <summary>
    /// 마지막으로 살펴본 상태. 기다림이 실패했을 때 무엇이 막고 있었는지 한 번에 알리려고 남긴다.
    /// 아직 한 번도 살펴보지 않았으면 null 이다.
    /// </summary>
    public ElementReadiness? LastState { get; private set; }

    /// <summary>
    /// 활성만 아니어서 <see cref="DisabledGrace"/> 만큼만 기다리고 끊었으면 true.
    /// 제한 시간을 다 쓰지 않았다는 사실을 호출자가 응답에 적을 수 있게 한다.
    /// </summary>
    public bool StoppedEarly { get; private set; }

    #endregion

    #region Public Methods

    /// <summary>
    /// 요소가 준비될 때까지 50ms 간격으로 살펴봅니다.
    /// 제한 시간 안에 준비되지 않으면 예외 대신 false 를 돌려준다: 주입된 프로세스 안에서 던지면
    /// 대상 앱의 first-chance 핸들러가 그 예외로 앱을 죽일 수 있고(결함 2), 잠깐 비활성·숨김 상태인
    /// 컨트롤을 기다리다 시간이 지나는 것은 흔한 정상 경로다.
    /// </summary>
    /// <param name="element">기다릴 대상 요소.</param>
    /// <returns>준비되면 true, 아니면 false.</returns>
    public async Task<bool> WaitForReady(DependencyObject element)
    {
        var started = DateTime.UtcNow;
        var deadline = started + _timeout;

        while (DateTime.UtcNow < deadline)
        {
            var state = await Application.Current.Dispatcher.InvokeAsync(() => ReadinessOf(element));
            LastState = state;

            if (state.IsReady)
                return true;

            if (ShouldStopEarly(state, DateTime.UtcNow - started))
            {
                StoppedEarly = true;
                return false;
            }

            await Task.Delay(PollInterval);
        }

        return false;
    }

    #endregion

    #region Internal Methods

    /// <summary>
    /// 더 기다려도 소용없다고 보고 끊을 때인지 판단합니다.
    /// 활성만 아닌 상태가 유예를 넘겨 이어지면 앱이 이 동작을 거부하고 있다고 본다.
    /// 보이지 않거나 로드 중인 경우는 해당하지 않는다. 그쪽은 실제로 시간이 걸릴 수 있다.
    /// </summary>
    /// <param name="state">마지막으로 읽은 상태.</param>
    /// <param name="elapsed">기다리기 시작한 뒤 흐른 시간.</param>
    internal static bool ShouldStopEarly(ElementReadiness state, TimeSpan elapsed)
    {
        return state is { Visible: true, Loaded: true, Enabled: false } && elapsed >= DisabledGrace;
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// 요소의 현재 상태를 읽습니다. UI 스레드에서만 부른다.
    /// <see cref="FrameworkElement"/> 가 아닌 요소는 로드 개념이 없으므로 로드된 것으로 본다.
    /// </summary>
    private static ElementReadiness ReadinessOf(DependencyObject element)
    {
        if (element is FrameworkElement fe)
            return new ElementReadiness(fe.IsVisible, fe.IsEnabled, fe.IsLoaded);

        if (element is UIElement ui)
            return new ElementReadiness(ui.Visibility == Visibility.Visible, ui.IsEnabled, true);

        return new ElementReadiness(true, true, true);
    }

    #endregion
}

/// <summary>
/// 요소가 조작을 받을 수 있는지를 가르는 세 가지 상태.
/// </summary>
/// <param name="Visible">화면에 보이면 true.</param>
/// <param name="Enabled">활성 상태이면 true.</param>
/// <param name="Loaded">로드가 끝났으면 true.</param>
public sealed record ElementReadiness(bool Visible, bool Enabled, bool Loaded)
{
    /// <summary>세 조건이 모두 갖추어졌는지.</summary>
    public bool IsReady => Visible && Enabled && Loaded;

    /// <summary>
    /// 무엇이 막고 있었는지를 한 조각의 말로 옮깁니다.
    /// 보이지 않는 것을 가장 먼저 보는 이유는, 그 경우 활성 여부를 읽어도 뜻이 없기 때문이다.
    /// </summary>
    public string Obstacle => this switch
    {
        { Visible: false } => "stayed hidden",
        { Loaded: false } => "was still loading",
        { Enabled: false } => "stayed disabled",
        _ => "was not ready"
    };
}

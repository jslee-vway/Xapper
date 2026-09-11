namespace Xapper.Inspector.Actions;

/// <summary>
/// 액션 시도의 결과. 성공이면 <see cref="Error"/> 가 null, 실패면 사람이 읽을 사유 문자열.
/// 주입된 프로세스 안에서는 예외를 던지면 대상 앱의 first-chance 핸들러가 그 예외로 앱을 죽일 수
/// 있으므로(결함 2), "지원하지 않음"·"항목 없음" 같은 정상적 실패를 예외 대신 이 값으로 돌려준다.
/// </summary>
public readonly record struct ActionResult
{
    #region Constructor

    private ActionResult(string? error) => Error = error;

    #endregion

    #region Properties

    /// <summary>실패 사유. 성공이면 null.</summary>
    public string? Error { get; }

    #endregion

    #region Factory

    /// <summary>성공 결과.</summary>
    public static ActionResult Ok { get; } = new((string?)null);

    /// <summary>주어진 사유로 실패한 결과.</summary>
    /// <param name="reason">사람이 읽을 실패 사유.</param>
    public static ActionResult Fail(string reason) => new(reason);

    #endregion
}

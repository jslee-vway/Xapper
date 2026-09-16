namespace Xapper.McpServer.Infrastructure;

/// <summary>
/// 책상 앞 사람에게 "Xapper 조작중" 을 알리는 창. 에이전트가 도구로 올리고 내리며, 서버는 실제 마우스 입력이
/// 나갔는데 알림이 없으면 대신 올린다. 알리는 것이 목적이므로 포커스를 가져가거나 입력을 가로채서는 안 되고,
/// 띄우지 못하더라도 조작 자체를 막아서는 안 된다.
/// </summary>
public interface IOperatorNotice
{
    /// <summary>알림이 올라가 있는지(마지막 명령 기준).</summary>
    bool IsVisible { get; }

    /// <summary>
    /// 알림을 띄웁니다. 이미 떠 있으면 문구만 갱신한다. 창이 화면에 나타난 뒤(최대 1초)에 돌아온다.
    /// </summary>
    /// <param name="message">헤드라인 아래 한 줄. null 이면 헤드라인만.</param>
    /// <param name="targetProcessId">알림을 그 위에 놓을 대상 앱의 PID. null 이거나 주 창이 없으면 주 모니터 위쪽.</param>
    /// <param name="ct">취소 토큰.</param>
    /// <returns>띄웠으면 (true, null), 못 띄웠으면 (false, 이유).</returns>
    Task<(bool Shown, string? Error)> ShowAsync(string? message, int? targetProcessId, CancellationToken ct);

    /// <summary>알림을 내립니다. 안 떠 있으면 아무것도 하지 않는다.</summary>
    Task HideAsync(CancellationToken ct);
}

/// <summary>도구들이 응답을 돌려주기 직전에 한 번 부르는 안전망 진입점.</summary>
public static class OperatorNoticeExtensions
{
    /// <summary>
    /// 응답이 실제 마우스를 썼다고 말하고 알림이 내려가 있으면 올리고, 응답에 그 사실을 덧붙여 돌려줍니다.
    /// 알림을 못 띄워도 응답은 그대로 돌려준다 — 조작은 이미 끝났고 알림은 정보다.
    /// </summary>
    public static async Task<string> AfterActionAsync(
        this IOperatorNotice notice, string responseText, int? targetProcessId, CancellationToken ct)
    {
        if (!OperatorNoticePolicy.ShouldRaise(responseText, notice.IsVisible))
            return responseText;

        var (shown, _) = await notice.ShowAsync(null, targetProcessId, ct);
        return shown ? OperatorNoticePolicy.Annotate(responseText) : responseText;
    }
}

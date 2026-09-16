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

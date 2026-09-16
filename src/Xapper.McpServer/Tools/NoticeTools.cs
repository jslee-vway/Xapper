using System.ComponentModel;
using ModelContextProtocol.Server;
using Xapper.McpServer.Infrastructure;

namespace Xapper.McpServer.Tools;

/// <summary>
/// 책상 앞 사람에게 "Xapper 조작중" 을 알리는 창을 올리고 내리는 MCP 도구. 하나의 도구에 bool 인자를 두면
/// 에이전트가 인자를 놓치는 일이 잦아(더블클릭 분리의 전례) 올리기와 내리기를 따로 둔다.
/// </summary>
[McpServerToolType]
public sealed class NoticeTools
{
    #region Fields

    private readonly SessionManager _sessionManager;
    private readonly IOperatorNotice _notice;

    #endregion

    #region Constructor

    /// <summary><see cref="NoticeTools"/>의 새 인스턴스를 생성합니다.</summary>
    public NoticeTools(SessionManager sessionManager, IOperatorNotice notice)
    {
        _sessionManager = sessionManager;
        _notice = notice;
    }

    #endregion

    #region MCP Tools

    /// <summary>알림을 띄웁니다. 이미 떠 있으면 문구만 갱신한다.</summary>
    [McpServerTool(Name = "xapper_notice_show"), Description(
        "Show a topmost 'Xapper 조작중' banner over the target app so the person at the desk knows the mouse is " +
        "about to be driven and waits. Call it before a stretch of actions that will use real mouse input (a " +
        "response said 'via real mouse input', or the in-process path is unavailable), and xapper_notice_hide " +
        "when that stretch is over. The banner takes no focus and lets clicks through; it never blocks actions.")]
    public async Task<string> Show(
        [Description("One line shown under the headline, e.g. what you are testing. Optional")] string? message = null,
        CancellationToken ct = default)
    {
        var (shown, error) = await _notice.ShowAsync(message, _sessionManager.ActiveProcessId, ct);
        return shown
            ? "Notice shown. Call xapper_notice_hide when you are done driving the app."
            : $"Notice could not be shown ({error}). Actions still work; tell the person yourself.";
    }

    /// <summary>알림을 내립니다.</summary>
    [McpServerTool(Name = "xapper_notice_hide"), Description(
        "Hide the 'Xapper 조작중' banner raised by xapper_notice_show (or raised automatically after a real-mouse " +
        "action). Safe to call when it is not showing.")]
    public async Task<string> Hide(CancellationToken ct = default)
    {
        await _notice.HideAsync(ct);
        return "Notice hidden.";
    }

    #endregion
}

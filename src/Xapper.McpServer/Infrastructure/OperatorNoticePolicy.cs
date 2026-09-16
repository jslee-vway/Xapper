namespace Xapper.McpServer.Infrastructure;

/// <summary>
/// 알림 안전망의 판정. 에이전트가 알림을 올리지 않은 채 조작이 실제 마우스로 떨어졌을 때, 서버가 대신 올리고
/// 응답에 그 사실을 적는다. 창이 없어도 검증할 수 있게 순수 함수로 둔다.
/// </summary>
internal static class OperatorNoticePolicy
{
    #region Fields

    /// <summary>Inspector 가 실제 입력 경로를 탔을 때 응답에 적는 경로 이름(ClickAction.RealMouseInputPath 와 동일).</summary>
    private const string RealMouseMarker = "via real mouse input";

    /// <summary>자동으로 올렸을 때 응답 뒤에 붙이는 안내.</summary>
    private const string Note =
        " | NOTE: the notice was raised automatically because this action drove the real mouse; " +
        "call xapper_notice_hide when you are done driving the app.";

    #endregion

    #region Public Methods

    /// <summary>응답이 실제 마우스를 썼다고 말하고 알림이 내려가 있으면 true.</summary>
    public static bool ShouldRaise(string responseText, bool noticeVisible)
        => !noticeVisible && responseText.Contains(RealMouseMarker, StringComparison.Ordinal);

    /// <summary>자동으로 올린 뒤 응답에 안내를 덧붙인다.</summary>
    public static string Annotate(string responseText) => responseText + Note;

    #endregion
}

namespace Xapper.McpServer.Scripting;

/// <summary>
/// 스크립트 실행 중 조작이 실패했음을 나타내는 예외. 도구가 오류·Pending 응답을 주거나 인자가 잘못됐을 때 던진다.
/// 러너가 이것을 잡아 실패한 API 이름과 함께 응답에 적는다.
/// </summary>
internal sealed class ScriptFailure : Exception
{
    #region Constructor

    /// <summary><see cref="ScriptFailure"/> 를 만듭니다.</summary>
    /// <param name="api">실패한 스크립트 API 이름(click, assert 등).</param>
    /// <param name="message">사람이 읽을 실패 사유.</param>
    public ScriptFailure(string api, string message) : base(message)
    {
        Api = api;
    }

    #endregion

    #region Public Properties

    /// <summary>실패한 스크립트 API 이름.</summary>
    public string Api { get; }

    #endregion
}

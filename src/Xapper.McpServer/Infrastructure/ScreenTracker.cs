namespace Xapper.McpServer.Infrastructure;

/// <summary>
/// 직전 조회에서 본 화면의 지문을 기억한다. 지금 지문과 견주어 "화면이 바뀌었다" 고 알려 주기 위해서만 쓴다.
/// 모르는 화면이 왜 모르는 화면인지, 즉 처음 보는 것인지 방금 무언가 눌러 다른 화면으로 넘어간 것인지를
/// 모델이 구분할 수 있게 해 준다.
///
/// 도구 클래스가 아니라 여기에 두는 이유가 있다. MCP 호스트는 도구를 호출마다 새로 만들기 때문에
/// 도구의 인스턴스 필드는 호출 사이에 남지 않는다(실측: 탭을 바꿔도 "바뀌었다" 가 뜨지 않았다).
/// </summary>
public sealed class ScreenTracker
{
    #region Public Properties

    /// <summary>직전 조회에서 본 지문. 아직 조회한 적이 없으면 null.</summary>
    public string? LastSignature { get; set; }

    #endregion
}

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

    #region Fields

    /// <summary>화면마다, 마지막으로 무언가를 기록한 뒤로 찍은 그림의 수.</summary>
    private readonly Dictionary<string, int> _picturesSinceRecording = new(StringComparer.Ordinal);

    #endregion

    #region Public Methods

    /// <summary>
    /// 이 화면을 한 장 더 찍었다고 세고, 마지막으로 기록한 뒤 몇 장째인지 돌려줍니다.
    /// 같은 화면을 거듭 찍으면서 아무것도 남기지 않는 것이 가장 흔한 낭비라, 그 횟수를 세어 두었다가
    /// 응답의 말투를 바꾸는 데 쓴다.
    /// </summary>
    /// <param name="signature">찍은 화면의 지문.</param>
    public int CountPicture(string signature)
    {
        lock (_picturesSinceRecording)
        {
            var count = _picturesSinceRecording.TryGetValue(signature, out var taken) ? taken + 1 : 1;
            _picturesSinceRecording[signature] = count;
            return count;
        }
    }

    /// <summary>이 화면에 무언가를 기록했으므로 세어 둔 장수를 지웁니다.</summary>
    /// <param name="signature">기록한 화면의 지문.</param>
    public void Recorded(string signature)
    {
        lock (_picturesSinceRecording)
            _picturesSinceRecording.Remove(signature);
    }

    #endregion
}

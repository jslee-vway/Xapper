namespace Xapper.McpServer.Scripting;

/// <summary>스크립트 실행 결과의 종류.</summary>
internal enum ScriptOutcome
{
    /// <summary>끝까지 실행됨.</summary>
    Ok,

    /// <summary>스크립트가 실패를 던짐(도구 오류·assert·fail·Pending).</summary>
    Fail,

    /// <summary>제한 시간을 넘겨 중단됨.</summary>
    Timeout,

    /// <summary>실행 전 문법 오류.</summary>
    SyntaxError
}

/// <summary>스크립트 러너가 돌려주는 결과. RunTools 가 이것을 응답 문자열로 만든다.</summary>
internal sealed class ScriptResult
{
    #region Public Properties

    /// <summary>결과 종류.</summary>
    public required ScriptOutcome Outcome { get; init; }

    /// <summary>소요 시간.</summary>
    public TimeSpan Elapsed { get; init; }

    /// <summary>성공한 액션 수(추적에 남은 것 기준의 총계).</summary>
    public int ActionCount { get; init; }

    /// <summary>log() 출력.</summary>
    public IReadOnlyList<string> Logs { get; init; } = Array.Empty<string>();

    /// <summary>return 값의 JSON(없으면 null).</summary>
    public string? ReturnValueJson { get; init; }

    /// <summary>실패·타임아웃·문법오류가 난 줄(모르면 0).</summary>
    public int FailureLine { get; init; }

    /// <summary>실패한 API 이름(Fail 일 때).</summary>
    public string? FailureApi { get; init; }

    /// <summary>실패 사유.</summary>
    public string? FailureMessage { get; init; }

    /// <summary>실패 시 최근 액션 추적.</summary>
    public IReadOnlyList<string> Trace { get; init; } = Array.Empty<string>();

    /// <summary>실패 시 저장한 스크린샷 경로(없으면 null).</summary>
    public string? ScreenshotPath { get; init; }

    #endregion
}

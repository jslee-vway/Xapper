using Xapper.Protocol.Messages.Responses;

namespace Xapper.Inspector.Diagnostics;

/// <summary>
/// 프로퍼티 읽기 시도의 결과. 성공이면 <see cref="Response"/> 가 값이고 <see cref="Error"/> 가 null,
/// 실패면 그 반대. 주입된 프로세스 안에서 예외를 던지지 않기 위해 실패를 값으로 표현한다(결함 2).
/// </summary>
public readonly record struct PropertyReadResult
{
    private PropertyReadResult(PropertyResponse? response, string? error)
    {
        Response = response;
        Error = error;
    }

    /// <summary>성공 시의 값 응답. 실패면 null.</summary>
    public PropertyResponse? Response { get; }

    /// <summary>실패 사유. 성공이면 null.</summary>
    public string? Error { get; }

    /// <summary>주어진 값 응답으로 성공한 결과.</summary>
    public static PropertyReadResult Ok(PropertyResponse response) => new(response, null);

    /// <summary>주어진 사유로 실패한 결과.</summary>
    public static PropertyReadResult Fail(string reason) => new(null, reason);
}

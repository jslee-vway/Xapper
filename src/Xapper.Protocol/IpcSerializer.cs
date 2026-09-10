using System.Buffers;
using System.Text.Json;
#if !NET7_0_OR_GREATER
using Xapper.Protocol.Polyfills;
#endif

namespace Xapper.Protocol;

/// <summary>
/// IPC 메시지의 직렬화/역직렬화를 담당하는 유틸리티 클래스.
/// 길이 접두사 프로토콜(4바이트 LE int32 + UTF-8 JSON 페이로드)을 사용하여 Named Pipe 스트림에서 메시지 경계를 구분.
/// </summary>
public static class IpcSerializer
{
    #region Fields

    /// <summary>한 메시지가 가질 수 있는 최대 페이로드 바이트 수. 이보다 큰 프레임은 수신 측에서 거부된다.</summary>
    public const int MaxPayloadBytes = 10 * 1024 * 1024;

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    #endregion

    #region Serialization

    /// <summary>
    /// IPC 메시지를 길이 접두사가 포함된 바이트 배열로 직렬화합니다.
    /// </summary>
    /// <param name="message">직렬화할 IPC 메시지.</param>
    /// <returns>4바이트 길이 헤더 + UTF-8 JSON 바이트 배열.</returns>
    public static byte[] Serialize(IpcMessage message)
    {
        var json = JsonSerializer.SerializeToUtf8Bytes(message, Options);
        var result = new byte[4 + json.Length];
        BitConverter.TryWriteBytes(result.AsSpan(0, 4), json.Length);
        json.CopyTo(result, 4);
        return result;
    }

    /// <summary>
    /// 객체를 JSON <see cref="JsonElement"/>로 직렬화합니다.
    /// </summary>
    /// <typeparam name="T">직렬화할 객체 타입.</typeparam>
    /// <param name="payload">직렬화할 페이로드 객체.</param>
    /// <returns>직렬화된 <see cref="JsonElement"/>.</returns>
    public static JsonElement SerializePayload<T>(T payload)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(payload, Options);
        using var doc = JsonDocument.Parse(bytes);
        return doc.RootElement.Clone();
    }

    #endregion

    #region Deserialization

    /// <summary>
    /// 스트림에서 길이 접두사 프로토콜에 따라 IPC 메시지를 비동기로 읽어 역직렬화합니다.
    /// </summary>
    /// <param name="stream">Named Pipe 스트림.</param>
    /// <param name="ct">취소 토큰.</param>
    /// <returns>역직렬화된 <see cref="IpcMessage"/>. 스트림 끝이면 null.</returns>
    /// <exception cref="InvalidOperationException">메시지 길이가 0 이하이거나 10MB를 초과하는 경우.</exception>
    public static async Task<IpcMessage?> DeserializeAsync(Stream stream, CancellationToken ct = default)
    {
        var lengthBuffer = new byte[4];
        await stream.ReadExactlyAsync(lengthBuffer, ct);
        var length = BitConverter.ToInt32(lengthBuffer);

        if (length <= 0 || length > MaxPayloadBytes)
            throw new InvalidOperationException($"Invalid message length: {length}");

        var jsonBuffer = new byte[length];
        await stream.ReadExactlyAsync(jsonBuffer, ct);

        return JsonSerializer.Deserialize<IpcMessage>(jsonBuffer, Options);
    }

    /// <summary>
    /// JSON 페이로드를 지정된 타입으로 역직렬화합니다.
    /// </summary>
    /// <typeparam name="T">대상 타입.</typeparam>
    /// <param name="payload">역직렬화할 <see cref="JsonElement"/>.</param>
    /// <returns>역직렬화된 객체.</returns>
    /// <exception cref="InvalidOperationException">역직렬화 실패 시.</exception>
    public static T DeserializePayload<T>(JsonElement payload)
    {
        return payload.Deserialize<T>(Options)
            ?? throw new InvalidOperationException($"Failed to deserialize payload as {typeof(T).Name}");
    }

    #endregion

    #region Factory Methods

    /// <summary>
    /// 새 IPC 요청 메시지를 생성합니다.
    /// </summary>
    /// <param name="method">요청 메서드명 (예: "ping", "click").</param>
    /// <param name="payload">요청 페이로드. null 허용.</param>
    /// <returns>GUID가 할당된 요청 메시지.</returns>
    public static IpcMessage CreateRequest(string method, object? payload = null)
    {
        return new IpcMessage
        {
            Id = Guid.NewGuid().ToString("N"),
            Type = "request",
            Method = method,
            Payload = payload != null ? SerializePayload(payload) : null
        };
    }

    /// <summary>
    /// IPC 응답 메시지를 생성합니다.
    /// </summary>
    /// <param name="id">원본 요청의 ID.</param>
    /// <param name="payload">응답 페이로드. null 허용.</param>
    /// <returns>요청 ID와 매칭되는 응답 메시지.</returns>
    public static IpcMessage CreateResponse(string id, object? payload = null)
    {
        return new IpcMessage
        {
            Id = id,
            Type = "response",
            Method = "",
            Payload = payload != null ? SerializePayload(payload) : null
        };
    }

    /// <summary>
    /// IPC 에러 메시지를 생성합니다.
    /// </summary>
    /// <param name="id">원본 요청의 ID.</param>
    /// <param name="error">에러 메시지 문자열.</param>
    /// <returns>에러 정보가 포함된 응답 메시지.</returns>
    public static IpcMessage CreateError(string id, string error)
    {
        return new IpcMessage
        {
            Id = id,
            Type = "error",
            Method = "",
            Payload = SerializePayload(new { error })
        };
    }

    #endregion
}

using System.Buffers;
using System.Text.Json;

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
    /// <returns>역직렬화된 <see cref="IpcMessage"/>. 스트림이 끝났거나 프레임이 어긋났으면 null.</returns>
    public static async Task<IpcMessage?> DeserializeAsync(Stream stream, CancellationToken ct = default)
    {
        var lengthBuffer = new byte[4];
        if (!await TryReadExactlyAsync(stream, lengthBuffer, ct))
            return null;

        var length = BitConverter.ToInt32(lengthBuffer);

        // 잘못된 길이는 스트림이 어긋났다는 뜻이다. 예외를 던지면 대상 프로세스의 first-chance 핸들러가
        // 그 예외로 앱을 죽일 수 있으므로(결함 2), 던지지 않고 null 로 알려 호출자가 연결을 정리하게 한다.
        if (length <= 0 || length > MaxPayloadBytes)
            return null;

        var jsonBuffer = new byte[length];
        if (!await TryReadExactlyAsync(stream, jsonBuffer, ct))
            return null;

        return JsonSerializer.Deserialize<IpcMessage>(jsonBuffer, Options);
    }

    /// <summary>
    /// 버퍼가 가득 찰 때까지 스트림에서 읽습니다. 스트림이 끝나면 예외 대신 false 를 돌려줍니다.
    /// 정상적인 연결 종료(파이프 닫힘)를 예외로 신호하면, 그 예외가 대상 프로세스의 first-chance
    /// 핸들러를 건드려 앱을 죽일 수 있다(결함 2). .NET 내장 <c>ReadExactlyAsync</c> 도, net6 폴리필도
    /// 스트림 끝에서 던지므로 여기서 직접 읽어 값으로 신호한다.
    /// </summary>
    /// <param name="stream">읽을 스트림.</param>
    /// <param name="buffer">채울 버퍼.</param>
    /// <param name="ct">취소 토큰.</param>
    /// <returns>버퍼를 다 채웠으면 true, 도중에 스트림이 끝났으면 false.</returns>
    private static async Task<bool> TryReadExactlyAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(offset, buffer.Length - offset), ct);
            if (read == 0)
                return false;
            offset += read;
        }

        return true;
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

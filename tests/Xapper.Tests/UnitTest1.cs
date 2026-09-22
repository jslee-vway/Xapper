using System.Text.Json;
using Xapper.Protocol;
using Xapper.Protocol.Messages.Requests;
using Xapper.Protocol.Messages.Responses;

namespace Xapper.Tests;

/// <summary>
/// IpcSerializer의 직렬화/역직렬화 및 팩토리 메서드를 검증하는 테스트 클래스.
/// </summary>
public class IpcSerializerTests
{
    [Fact]
    public async Task Serialize_Deserialize_RoundTrip()
    {
        var original = IpcSerializer.CreateRequest("snapshot", new SnapshotRequest { MaxDepth = 3 });

        var bytes = IpcSerializer.Serialize(original);
        using var stream = new MemoryStream(bytes);
        var deserialized = await IpcSerializer.DeserializeAsync(stream);

        Assert.NotNull(deserialized);
        Assert.Equal(original.Id, deserialized.Id);
        Assert.Equal("request", deserialized.Type);
        Assert.Equal("snapshot", deserialized.Method);
    }

    [Fact]
    public void DeserializePayload_SnapshotRequest()
    {
        var request = new SnapshotRequest { RootRef = 5, MaxDepth = 3 };
        var payload = IpcSerializer.SerializePayload(request);
        var result = IpcSerializer.DeserializePayload<SnapshotRequest>(payload);

        Assert.Equal(5, result.RootRef);
        Assert.Equal(3, result.MaxDepth);
    }

    [Fact]
    public void DeserializePayload_ActionResponse()
    {
        var response = new ActionResponse { Success = true, Message = "Clicked ref=3" };
        var payload = IpcSerializer.SerializePayload(response);
        var result = IpcSerializer.DeserializePayload<ActionResponse>(payload);

        Assert.True(result.Success);
        Assert.Equal("Clicked ref=3", result.Message);
        Assert.Null(result.Error);
    }

    [Fact]
    public void CreateError_ContainsErrorMessage()
    {
        var error = IpcSerializer.CreateError("abc123", "Element not found");

        Assert.Equal("error", error.Type);
        Assert.Equal("abc123", error.Id);
        Assert.True(error.Payload.HasValue);
        Assert.Contains("Element not found", error.Payload.Value.GetRawText());
    }

    [Fact]
    public void Serialize_LengthPrefix_IsCorrect()
    {
        var message = IpcSerializer.CreateRequest("ping");
        var bytes = IpcSerializer.Serialize(message);

        var expectedLength = BitConverter.ToInt32(bytes, 0);
        Assert.Equal(bytes.Length - 4, expectedLength);
    }
}

/// <summary>
/// ElementSnapshot의 JSON 직렬화 라운드트립을 검증하는 테스트 클래스.
/// </summary>
public class ElementSnapshotTests
{
    [Fact]
    public void ElementSnapshot_Serialization()
    {
        var snapshot = new ElementSnapshot
        {
            Ref = 1,
            Type = "Button",
            Name = "btnLogin",
            AutomationId = "LoginButton",
            Text = "Log In",
            IsEnabled = true,
            IsVisible = true,
            Bounds = new BoundingBox { X = 10, Y = 20, Width = 100, Height = 35 },
            Children = []
        };

        var json = JsonSerializer.Serialize(snapshot);
        var deserialized = JsonSerializer.Deserialize<ElementSnapshot>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(1, deserialized.Ref);
        Assert.Equal("Button", deserialized.Type);
        Assert.Equal("btnLogin", deserialized.Name);
        Assert.Equal("LoginButton", deserialized.AutomationId);
        Assert.Equal("Log In", deserialized.Text);
        Assert.True(deserialized.IsEnabled);
        Assert.NotNull(deserialized.Bounds);
        Assert.Equal(100, deserialized.Bounds.Width);
    }
}

/// <summary>
/// IpcPipeNames의 파이프 이름 생성 규칙을 검증하는 테스트 클래스.
/// </summary>
public class IpcPipeNamesTests
{
    [Fact]
    public void ForProcess_ReturnsExpectedFormat()
    {
        Assert.Equal("xapper_1234", IpcPipeNames.ForProcess(1234));
        Assert.Equal("xapper_0", IpcPipeNames.ForProcess(0));
    }
}

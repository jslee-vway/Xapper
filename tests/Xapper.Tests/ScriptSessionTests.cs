using System.Text.Json;
using Xapper.McpServer.Scripting;
using Xapper.Protocol;

namespace Xapper.Tests;

/// <summary>
/// xapper 전역의 각 메서드가 올바른 IPC 요청을 만들고, 오류·Pending 응답을 예외로 승격하며, 값을 변환하는지 검증한다.
/// 전송 델리게이트를 가짜로 주입해 요청을 기록하고 응답을 지정한다.
/// </summary>
public class ScriptSessionTests
{
    private readonly List<IpcMessage> _sent = new();
    private Func<IpcMessage, IpcMessage> _reply = _ => Ok("done");

    private ScriptSession NewSession() => new(
        send: (msg, _) => { _sent.Add(msg); return Task.FromResult(_reply(msg)); },
        processId: 1234,
        notice: null,
        ct: CancellationToken.None);

    private static IpcMessage Ok(string message) => Response(new { success = true, message });
    private static IpcMessage Fail(string error) => Response(new { success = false, error });
    private static IpcMessage Pending() => Response(new { success = true, pending = true, message = "modal" });

    private static IpcMessage Response(object payload) => new()
    {
        Id = "1", Type = "response", Method = "x",
        Payload = JsonSerializer.SerializeToElement(payload)
    };

    private static IpcMessage FindReply(params (int Ref, string Type)[] matches) => Response(new
    {
        matches = matches.Select(m => new { @ref = m.Ref, type = m.Type }).ToArray(),
        skippedNodes = Array.Empty<string>()
    });

    [Fact]
    public void Click_SendsAClickRequestWithTheRef()
    {
        var session = NewSession();
        session.Click(57, options: null);
        Assert.Single(_sent);
        Assert.Equal("click", _sent[0].Method);
        Assert.Equal(57, _sent[0].Payload.GetValueOrDefault().GetProperty("ref").GetInt32());
    }

    [Fact]
    public void Click_WhenTheResponseIsAnError_ThrowsScriptFailure()
    {
        _reply = _ => Fail("element not found");
        var session = NewSession();
        var failure = Assert.Throws<ScriptFailure>(() => session.Click(57, options: null));
        Assert.Equal("click", failure.Api);
        Assert.Contains("element not found", failure.Message);
    }

    [Fact]
    public void Click_WhenTheResponseIsPending_ThrowsScriptFailure()
    {
        _reply = _ => Pending();
        var session = NewSession();
        Assert.Throws<ScriptFailure>(() => session.Click(57, options: null));
    }

    [Fact]
    public void Find_ReturnsElementObjectsCarryingTheRef()
    {
        _reply = _ => FindReply((1, "Button"), (2, "TextBox"));
        var session = NewSession();
        var elements = session.Find("type=Button");
        Assert.Equal(2, elements.Length);
        Assert.Equal(1, ((ScriptElement)elements[0]).Ref);
        Assert.Equal("find", _sent[0].Method);
        Assert.Equal("Button", _sent[0].Payload.GetValueOrDefault().GetProperty("type").GetString());
    }

    [Fact]
    public void Get_ParsesNumericValues()
    {
        _reply = _ => Response(new { @ref = 3, propertyName = "Severity", value = "9", valueType = "Int32" });
        var session = NewSession();
        var value = session.Get(3, "Severity");
        Assert.Equal(9d, value);
    }

    [Fact]
    public void Get_KeepsNonNumericTextAsString()
    {
        _reply = _ => Response(new { @ref = 3, propertyName = "Text", value = "hello", valueType = "String" });
        var session = NewSession();
        Assert.Equal("hello", session.Get(3, "Text"));
    }

    [Fact]
    public void Type_WithoutOptions_SendsANumericTimeoutNotNull()
    {
        var session = NewSession();

        session.Type((object)"id=x", "hi", options: null);

        // Inspector 요청 타입의 timeout 은 non-nullable int 라, null 을 보내면 역직렬화가 깨진다(실측 회귀).
        var timeout = _sent[0].Payload.GetValueOrDefault().GetProperty("timeout");
        Assert.Equal(System.Text.Json.JsonValueKind.Number, timeout.ValueKind);
        Assert.Equal(5000, timeout.GetInt32());
    }

    [Fact]
    public void One_WhenNotExactlyOneMatch_Throws()
    {
        _reply = _ => FindReply((1, "Button"), (2, "Button"));
        var session = NewSession();
        Assert.Throws<ScriptFailure>(() => session.One("type=Button"));
    }
}

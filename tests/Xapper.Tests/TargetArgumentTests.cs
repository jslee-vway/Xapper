using Xapper.McpServer.Scripting;

namespace Xapper.Tests;

/// <summary>
/// 스크립트의 target 인자(문자열 셀렉터 / ref 숫자 / 요소 객체)를 (ref, target) 으로 푸는 규칙을 검증한다.
/// </summary>
public class TargetArgumentTests
{
    [Fact]
    public void Resolve_StringBecomesTargetSelector()
    {
        var (@ref, target) = TargetArgument.Resolve("id=Save");
        Assert.Null(@ref);
        Assert.Equal("id=Save", target);
    }

    [Fact]
    public void Resolve_NumberBecomesRef()
    {
        var (@ref, target) = TargetArgument.Resolve(57d);
        Assert.Equal(57, @ref);
        Assert.Null(target);
    }

    [Fact]
    public void Resolve_ElementBecomesItsRef()
    {
        var element = new ScriptElement(session: null!, @ref: 42, type: "Button", name: null, id: null, text: null);
        var (@ref, target) = TargetArgument.Resolve(element);
        Assert.Equal(42, @ref);
        Assert.Null(target);
    }

    [Fact]
    public void Resolve_NullThrows()
    {
        Assert.Throws<ScriptFailure>(() => TargetArgument.Resolve(null));
    }
}

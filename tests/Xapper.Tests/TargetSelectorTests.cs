using Xapper.Inspector.Actions;

namespace Xapper.Tests;

/// <summary>target selector 파서가 네 가지 키를 검색 조건으로 옮기고, 잘못된 문법은 예외 없이 오류로 돌려주는지 검증한다.</summary>
public class TargetSelectorTests
{
    [Theory]
    [InlineData("id=LoginButton", "LoginButton", null, null, null)]
    [InlineData("automationId=LoginButton", "LoginButton", null, null, null)]
    [InlineData("AutomationId=LoginButton", "LoginButton", null, null, null)]
    [InlineData("ID=LoginButton", "LoginButton", null, null, null)]
    [InlineData("name=txtUser", null, "txtUser", null, null)]
    [InlineData("NAME=txtUser", null, "txtUser", null, null)]
    [InlineData("text=Log In", null, null, "Log In", null)]
    [InlineData("Text=Log In", null, null, "Log In", null)]
    [InlineData("type=Button", null, null, null, "Button")]
    [InlineData("TYPE=Button", null, null, null, "Button")]
    public void TryParse_MapsEachKeyCaseInsensitively(string target, string? id, string? name, string? text, string? type)
    {
        var ok = TargetSelector.TryParse(target, out var request, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(id, request.AutomationId);
        Assert.Equal(name, request.Name);
        Assert.Equal(text, request.Text);
        Assert.Equal(type, request.Type);
    }

    [Theory]
    [InlineData("id=")]
    [InlineData("name= ,type=TextBox")]
    public void TryParse_RejectsEmptyValues(string target)
    {
        // 빈 값은 부분 일치에서 모든 요소와 맞아 "N개 매칭" 오류로 새므로 파싱에서 거른다.
        var ok = TargetSelector.TryParse(target, out _, out var error);

        Assert.False(ok);
        Assert.NotNull(error);
        Assert.Contains("empty value", error);
    }

    [Fact]
    public void TryParse_TrimsKeysAndValues()
    {
        var ok = TargetSelector.TryParse("  name =  txtUser  ,  type= TextBox ", out var request, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal("txtUser", request.Name);
        Assert.Equal("TextBox", request.Type);
    }

    [Fact]
    public void TryParse_CombinesPartsAsAnd()
    {
        var ok = TargetSelector.TryParse("name=txtUser,type=TextBox,id=User,text=hello", out var request, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal("txtUser", request.Name);
        Assert.Equal("TextBox", request.Type);
        Assert.Equal("User", request.AutomationId);
        Assert.Equal("hello", request.Text);
    }

    [Fact]
    public void TryParse_KeepsValueAfterFirstEquals()
    {
        var ok = TargetSelector.TryParse("text=a=b", out var request, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal("a=b", request.Text);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void TryParse_RejectsEmptyTarget(string? target)
    {
        var ok = TargetSelector.TryParse(target, out _, out var error);

        Assert.False(ok);
        Assert.Equal("target is empty", error);
    }

    [Theory]
    [InlineData("LoginButton")]
    [InlineData("id=LoginButton,Button")]
    public void TryParse_RejectsPartWithoutEqualsWithoutThrowing(string target)
    {
        var ok = TargetSelector.TryParse(target, out _, out var error);

        Assert.False(ok);
        Assert.NotNull(error);
        Assert.Contains("no '='", error);
        Assert.Contains("\"id=LoginButton\"", error);
        Assert.Contains("\"name=txtUser,type=TextBox\"", error);
    }

    [Theory]
    [InlineData("class=Button")]
    [InlineData("id=A,label=B")]
    public void TryParse_RejectsUnknownKeyWithoutThrowing(string target)
    {
        var ok = TargetSelector.TryParse(target, out _, out var error);

        Assert.False(ok);
        Assert.NotNull(error);
        Assert.Contains("Unknown key", error);
        Assert.Contains("id (AutomationId), name, text and type", error);
        Assert.Contains("\"id=LoginButton\"", error);
    }
}

using Xapper.McpServer.Scripting;

namespace Xapper.Tests;

/// <summary>
/// 셀렉터 문자열("id=…,type=…")을 find 요청 필드로 분해하는 규칙을 검증한다.
/// </summary>
public class SelectorParserTests
{
    [Fact]
    public void Parse_SplitsEachKeyIntoItsField()
    {
        var ok = SelectorParser.TryParse("id=Save,type=Button", out var f, out var error);

        Assert.True(ok, error);
        Assert.Equal("Save", f.AutomationId);
        Assert.Equal("Button", f.Type);
        Assert.Null(f.Name);
        Assert.Null(f.Text);
    }

    [Fact]
    public void Parse_MapsNameAndText()
    {
        SelectorParser.TryParse("name=txtUser", out var byName, out _);
        SelectorParser.TryParse("text=로그인", out var byText, out _);

        Assert.Equal("txtUser", byName.Name);
        Assert.Equal("로그인", byText.Text);
    }

    [Fact]
    public void Parse_RejectsEmptyValueBecauseItMatchesEverything()
    {
        var ok = SelectorParser.TryParse("text=", out _, out var error);

        Assert.False(ok);
        Assert.Contains("empty", error);
    }

    [Fact]
    public void Parse_RejectsUnknownKey()
    {
        var ok = SelectorParser.TryParse("foo=bar", out _, out var error);

        Assert.False(ok);
        Assert.Contains("foo", error);
    }

    [Fact]
    public void Parse_RejectsAPartWithoutEquals()
    {
        var ok = SelectorParser.TryParse("Save", out _, out var error);

        Assert.False(ok);
        Assert.Contains("id=", error);
    }
}

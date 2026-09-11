using System.Windows.Input;
using Xapper.Inspector.Actions;

namespace Xapper.Tests;

/// <summary>수식키 문자열 파서가 허용 어휘를 해석하고, 모르는 토큰은 예외 없이 오류로 돌려주는지 검증한다.</summary>
public class ModifierParserTests
{
    [Theory]
    [InlineData(null, ModifierKeys.None)]
    [InlineData("", ModifierKeys.None)]
    [InlineData("Ctrl", ModifierKeys.Control)]
    [InlineData("control", ModifierKeys.Control)]
    [InlineData("SHIFT", ModifierKeys.Shift)]
    [InlineData("Alt", ModifierKeys.Alt)]
    [InlineData("Ctrl+Shift", ModifierKeys.Control | ModifierKeys.Shift)]
    [InlineData("ctrl, alt", ModifierKeys.Control | ModifierKeys.Alt)]
    [InlineData("Shift Ctrl Alt", ModifierKeys.Control | ModifierKeys.Shift | ModifierKeys.Alt)]
    [InlineData("  Ctrl+Shift  ", ModifierKeys.Control | ModifierKeys.Shift)]
    [InlineData("Ctrl+Ctrl", ModifierKeys.Control)]
    public void TryParse_AcceptsTheVocabulary(string? text, ModifierKeys expected)
    {
        var ok = ModifierParser.TryParse(text, out var modifiers, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(expected, modifiers);
    }

    [Theory]
    [InlineData("Win")]
    [InlineData("Ctrl+Meta")]
    public void TryParse_RejectsUnknownTokensWithoutThrowing(string text)
    {
        var ok = ModifierParser.TryParse(text, out var modifiers, out var error);

        Assert.False(ok);
        Assert.Equal(ModifierKeys.None, modifiers);
        Assert.NotNull(error);
        Assert.Contains("Ctrl, Shift, Alt", error);
    }
}

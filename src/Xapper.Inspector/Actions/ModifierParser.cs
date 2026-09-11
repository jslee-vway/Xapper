using System.Diagnostics.CodeAnalysis;
using System.Windows.Input;

namespace Xapper.Inspector.Actions;

/// <summary>
/// 도구 인자로 들어온 수식키 문자열("Ctrl", "Ctrl+Shift")을 <see cref="ModifierKeys"/> 로 해석하는 순수 파서.
/// 예외를 던지지 않는다: 주입된 프로세스 안에서 던지면 대상 앱을 죽일 수 있으므로(결함 2) 모르는 토큰은
/// 오류 문구로 돌려준다. 구분자는 '+', ',', 공백을 모두 허용하고 대소문자는 무시한다.
/// </summary>
internal static class ModifierParser
{
    private static readonly char[] Separators = { '+', ',', ' ' };

    /// <summary>
    /// 수식키 문자열을 해석합니다.
    /// </summary>
    /// <param name="text">해석할 문자열. null 이나 빈 문자열은 수식키 없음.</param>
    /// <param name="modifiers">해석된 수식키 조합. 실패하면 <see cref="ModifierKeys.None"/>.</param>
    /// <param name="error">실패 사유. 성공하면 null.</param>
    /// <returns>해석에 성공하면 true.</returns>
    public static bool TryParse(string? text, out ModifierKeys modifiers, [NotNullWhen(false)] out string? error)
    {
        modifiers = ModifierKeys.None;
        error = null;

        if (string.IsNullOrWhiteSpace(text))
            return true;

        var result = ModifierKeys.None;
        foreach (var token in text.Split(Separators, StringSplitOptions.RemoveEmptyEntries))
        {
            switch (token.Trim().ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    result |= ModifierKeys.Control;
                    break;
                case "shift":
                    result |= ModifierKeys.Shift;
                    break;
                case "alt":
                    result |= ModifierKeys.Alt;
                    break;
                default:
                    error = $"Unknown modifier \"{token}\". Use Ctrl, Shift, Alt, or a combination like \"Ctrl+Shift\".";
                    return false;
            }
        }

        modifiers = result;
        return true;
    }
}

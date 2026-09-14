using System.Diagnostics.CodeAnalysis;
using Xapper.Protocol.Messages.Requests;

namespace Xapper.Inspector.Actions;

/// <summary>
/// 도구 인자로 들어온 target selector("id=LoginButton", "name=txtUser,type=TextBox")를 검색 조건으로 바꾸는 순수 파서.
/// 예외를 던지지 않는다: 주입된 프로세스 안에서 던지면 대상 앱을 죽일 수 있으므로(결함 2) 잘못된 문법은
/// 오류 문구로 돌려준다. 키는 대소문자를 무시하고, 값의 앞뒤 공백은 지운다. 값 안의 콤마는 지원하지 않는다.
/// </summary>
internal static class TargetSelector
{
    private const string Usage =
        "Accepted keys are id (AutomationId), name, text and type, written as key=value and joined with " +
        "commas for AND, e.g. \"id=LoginButton\" or \"name=txtUser,type=TextBox\".";

    /// <summary>
    /// selector 문자열을 검색 조건으로 해석합니다.
    /// </summary>
    /// <param name="target">해석할 selector.</param>
    /// <param name="request">해석된 검색 조건. 실패하면 비어 있다.</param>
    /// <param name="error">실패 사유. 성공하면 null.</param>
    /// <returns>해석에 성공하면 true.</returns>
    public static bool TryParse(string? target, out FindElementRequest request, [NotNullWhen(false)] out string? error)
    {
        request = new FindElementRequest();
        error = null;

        if (string.IsNullOrWhiteSpace(target))
        {
            error = "target is empty";
            return false;
        }

        foreach (var part in target.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = part.IndexOf('=');
            if (separator < 0)
            {
                error = $"\"{part}\" in target has no '='. {Usage}";
                return false;
            }

            var key = part[..separator].Trim();
            var value = part[(separator + 1)..].Trim();
            if (value.Length == 0)
            {
                error = $"\"{part.Trim()}\" in target has an empty value; an empty text would match every element.";
                return false;
            }

            switch (key.ToLowerInvariant())
            {
                case "id":
                case "automationid":
                    request.AutomationId = value;
                    break;
                case "name":
                    request.Name = value;
                    break;
                case "text":
                    request.Text = value;
                    break;
                case "type":
                    request.Type = value;
                    break;
                default:
                    error = $"Unknown key \"{key}\" in target. {Usage}";
                    return false;
            }
        }

        return true;
    }
}

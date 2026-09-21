using Xapper.Protocol.Messages.Requests;

namespace Xapper.McpServer.Scripting;

/// <summary>
/// 스크립트가 쓰는 셀렉터 문자열("id=…", "name=…", "text=…", "type=…", 콤마 AND)을 find 요청 필드로 바꾼다.
/// 여러 요소를 돌려주는 find 용이라 "정확히 하나" 를 요구하지 않는다. 예외를 던지지 않고 오류 문장을 돌려준다.
/// </summary>
internal static class SelectorParser
{
    #region Public Methods

    /// <summary>셀렉터를 find 요청으로 분해합니다. 성공하면 true, 실패하면 <paramref name="error"/> 에 이유.</summary>
    public static bool TryParse(string selector, out FindElementRequest request, out string error)
    {
        request = new FindElementRequest();
        error = "";

        foreach (var rawPart in selector.Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var part = rawPart.Trim();
            var equals = part.IndexOf('=');
            if (equals <= 0)
            {
                error = $"\"{part}\" has no key; use id=…, name=…, text=… or type=….";
                return false;
            }

            var key = part[..equals].Trim();
            var value = part[(equals + 1)..].Trim();
            if (value.Length == 0)
            {
                error = $"\"{part}\" has an empty value; an empty match would select every element.";
                return false;
            }

            switch (key)
            {
                case "id": request.AutomationId = value; break;
                case "name": request.Name = value; break;
                case "text": request.Text = value; break;
                case "type": request.Type = value; break;
                default:
                    error = $"unknown selector key \"{key}\"; use id, name, text or type.";
                    return false;
            }
        }

        if (request.Name is null && request.AutomationId is null && request.Text is null && request.Type is null)
        {
            error = "empty selector; give at least one of id=…, name=…, text=…, type=….";
            return false;
        }

        return true;
    }

    #endregion
}

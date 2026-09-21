using Jint.Native;

namespace Xapper.McpServer.Scripting;

/// <summary>
/// 스크립트가 target 자리에 넘긴 값(셀렉터 문자열 / ref 숫자 / 요소 객체)을 (ref, target) 으로 푼다.
/// 도구는 ref 또는 target 중 하나만 받으므로 여기서 한 쪽으로 정규화한다.
/// </summary>
internal static class TargetArgument
{
    #region Public Methods

    /// <summary>target 값을 (ref, target) 으로 풉니다. null 이거나 알 수 없는 타입이면 <see cref="ScriptFailure"/>.</summary>
    public static (int? Ref, string? Target) Resolve(object? value)
    {
        switch (value)
        {
            case ScriptElement element:
                return (element.Ref, null);
            case string selector:
                return (null, selector);
            case double number:
                return ((int)number, null);
            case int number:
                return (number, null);
            case JsValue js:
                return Resolve(js.ToObject());
            case null:
                throw new ScriptFailure("target", "target is null; pass a selector string, a ref number, or an element from find().");
            default:
                throw new ScriptFailure("target", $"target is a {value.GetType().Name}; pass a selector string, a ref number, or an element from find().");
        }
    }

    #endregion
}

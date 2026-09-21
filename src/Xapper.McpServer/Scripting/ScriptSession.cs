using Jint.Native;

namespace Xapper.McpServer.Scripting;

/// <summary>스크립트 전역 xapper 의 구현. 다음 작업에서 채운다. 지금은 ScriptElement 가 참조하는 표면만 둔다.</summary>
internal sealed partial class ScriptSession
{
    /// <summary>요소 ref 를 클릭합니다(임시).</summary>
    public string Click(int @ref, JsValue? options) => throw new NotImplementedException();
    /// <summary>요소 ref 를 더블클릭합니다(임시).</summary>
    public string DoubleClick(int @ref, JsValue? options) => throw new NotImplementedException();
    /// <summary>요소 ref 를 우클릭합니다(임시).</summary>
    public string RightClick(int @ref, JsValue? options) => throw new NotImplementedException();
    /// <summary>요소 ref 에 텍스트를 입력합니다(임시).</summary>
    public string Type(int @ref, string text, JsValue? options) => throw new NotImplementedException();
    /// <summary>요소 ref 에 키를 보냅니다(임시).</summary>
    public string KeyOn(int @ref, string keys, JsValue? options) => throw new NotImplementedException();
    /// <summary>요소 ref 의 프로퍼티를 읽습니다(임시).</summary>
    public object? Get(int @ref, string prop) => throw new NotImplementedException();
    /// <summary>요소 ref 의 프로퍼티를 단언합니다(임시).</summary>
    public bool Assert(int @ref, string prop, string expected, JsValue? options) => throw new NotImplementedException();
}

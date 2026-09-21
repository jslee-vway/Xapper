using Jint.Native;

namespace Xapper.McpServer.Scripting;

/// <summary>
/// find() 가 돌려주는 요소 객체. 식별 필드와, 그 요소를 대상으로 하는 축약 메서드를 제공한다.
/// 축약 메서드는 세션에 ref 를 넘겨 위임하므로, xapper.click(el) 과 el.click() 은 같은 일을 한다.
/// </summary>
internal sealed class ScriptElement
{
    #region Fields

    private readonly ScriptSession _session;

    #endregion

    #region Constructor

    /// <summary><see cref="ScriptElement"/> 를 만듭니다.</summary>
    public ScriptElement(ScriptSession session, int @ref, string type, string? name, string? id, string? text)
    {
        _session = session;
        Ref = @ref;
        Type = type;
        Name = name;
        Id = id;
        Text = text;
    }

    #endregion

    #region Public Properties

    /// <summary>스냅샷·find 가 부여한 요소 번호.</summary>
    public int Ref { get; }

    /// <summary>요소의 런타임 타입 이름.</summary>
    public string Type { get; }

    /// <summary>요소 이름(없으면 null).</summary>
    public string? Name { get; }

    /// <summary>AutomationId(없으면 null).</summary>
    public string? Id { get; }

    /// <summary>표시 텍스트(없으면 null).</summary>
    public string? Text { get; }

    #endregion

    #region Public Methods

    /// <summary>이 요소를 클릭합니다. <paramref name="options"/> 는 xapper.click 과 같다.</summary>
    public string Click(JsValue? options = null) => _session.Click(Ref, options);

    /// <summary>이 요소를 더블클릭합니다.</summary>
    public string DoubleClick(JsValue? options = null) => _session.DoubleClick(Ref, options);

    /// <summary>이 요소를 우클릭합니다.</summary>
    public string RightClick(JsValue? options = null) => _session.RightClick(Ref, options);

    /// <summary>이 요소에 텍스트를 입력합니다.</summary>
    public string Type_(string text, JsValue? options = null) => _session.Type(Ref, text, options);

    /// <summary>이 요소에 키를 보냅니다.</summary>
    public string Key(string keys, JsValue? options = null) => _session.KeyOn(Ref, keys, options);

    /// <summary>이 요소의 프로퍼티 값을 읽습니다.</summary>
    public object? Get(string prop) => _session.Get(Ref, prop);

    /// <summary>이 요소의 프로퍼티를 단언합니다.</summary>
    public bool Assert(string prop, string expected, JsValue? options = null) => _session.Assert(Ref, prop, expected, options);

    #endregion
}

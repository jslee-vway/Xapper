using Jint;
using Jint.Native;
using Jint.Native.Object;

namespace Xapper.McpServer.Scripting;

/// <summary>
/// 스크립트 메서드의 선택 옵션 객체({x, y, modifiers, timeout, clear, allowFail, text, index, h, v, maxDepth, maxWidth, mode, path, ref})를
/// C# 값으로 읽는다. 없는 키는 null 로 둔다.
/// </summary>
internal readonly struct Options
{
    #region Public Properties

    /// <summary>클릭 지점의 상대 X.</summary>
    public double? X { get; private init; }

    /// <summary>클릭 지점의 상대 Y.</summary>
    public double? Y { get; private init; }

    /// <summary>함께 누를 수식키.</summary>
    public string? Modifiers { get; private init; }

    /// <summary>제한 시간(ms).</summary>
    public int? Timeout { get; private init; }

    /// <summary>입력 전 기존 값을 지울지.</summary>
    public bool? Clear { get; private init; }

    /// <summary>true 면 실패 응답을 예외로 올리지 않고 문자열로 돌려준다.</summary>
    public bool AllowFail { get; private init; }

    /// <summary>선택할 항목의 텍스트(select).</summary>
    public string? Text { get; private init; }

    /// <summary>선택할 항목의 인덱스(select).</summary>
    public int? Index { get; private init; }

    /// <summary>가로 스크롤 비율(scroll).</summary>
    public double? H { get; private init; }

    /// <summary>세로 스크롤 비율(scroll).</summary>
    public double? V { get; private init; }

    /// <summary>스냅샷 최대 깊이.</summary>
    public int? MaxDepth { get; private init; }

    /// <summary>스크린샷 최대 가로 픽셀.</summary>
    public int? MaxWidth { get; private init; }

    /// <summary>스크린샷 모드.</summary>
    public string? Mode { get; private init; }

    /// <summary>스크린샷 저장 경로.</summary>
    public string? Path { get; private init; }

    /// <summary>대상 요소 ref(스크린샷 등).</summary>
    public int? Ref { get; private init; }

    #endregion

    #region Public Methods

    /// <summary>JS 옵션 객체를 읽습니다. null 이면 모두 비어 있는 옵션.</summary>
    public static Options From(JsValue? options)
    {
        if (options is not ObjectInstance obj)
            return new Options();

        return new Options
        {
            X = Number(obj, "x"),
            Y = Number(obj, "y"),
            Modifiers = String(obj, "modifiers"),
            Timeout = Number(obj, "timeout") is double t ? (int)t : null,
            Clear = Bool(obj, "clear"),
            AllowFail = Bool(obj, "allowFail") ?? false,
            Text = String(obj, "text"),
            Index = Number(obj, "index") is double idx ? (int)idx : null,
            H = Number(obj, "h"),
            V = Number(obj, "v"),
            MaxDepth = Number(obj, "maxDepth") is double md ? (int)md : null,
            MaxWidth = Number(obj, "maxWidth") is double mw ? (int)mw : null,
            Mode = String(obj, "mode"),
            Path = String(obj, "path"),
            Ref = Number(obj, "ref") is double r ? (int)r : null
        };
    }

    #endregion

    #region Private Methods

    private static double? Number(ObjectInstance obj, string key)
    {
        var v = obj.Get(key);
        return v.IsNumber() ? v.AsNumber() : null;
    }

    private static string? String(ObjectInstance obj, string key)
    {
        var v = obj.Get(key);
        return v.IsString() ? v.AsString() : null;
    }

    private static bool? Bool(ObjectInstance obj, string key)
    {
        var v = obj.Get(key);
        return v.IsBoolean() ? v.AsBoolean() : null;
    }

    #endregion
}

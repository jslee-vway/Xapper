namespace Xapper.McpServer.Infrastructure;

/// <summary>
/// 한 화면에 대해 기억해 두는 것. 지문으로 찾고, 영역 목록으로 조작한다.
/// </summary>
public sealed class ScreenRecord
{
    /// <summary>주 창의 구조에서 뽑은 지문. 기본 키다.</summary>
    public required string Signature { get; set; }

    /// <summary>이 화면이 속한 앱의 프로세스 이름.</summary>
    public required string App { get; set; }

    /// <summary>모델이 붙인 한 줄 이름.</summary>
    public required string Name { get; set; }

    /// <summary>모델이 남긴 주의 사항. 없으면 null.</summary>
    public string? Notes { get; set; }

    /// <summary>조작할 수 있는 영역 목록.</summary>
    public List<ScreenRegion> Regions { get; set; } = [];

    /// <summary>이 기록이 조회된 횟수. 저장소가 채운다.</summary>
    public int SeenCount { get; set; }

    /// <summary>처음 기록한 시각. 저장소가 채운다.</summary>
    public string? LearnedAt { get; set; }
}

/// <summary>
/// 화면 안에서 조작할 수 있는 자리 하나.
/// 셀렉터로 지목되면 <see cref="Selector"/> 가, 그렇지 않으면 <see cref="Anchor"/> 와 상대 좌표가 채워진다.
/// ref 는 담지 않는다. 스냅샷마다 다시 매겨져 다음 세션에서 무의미하기 때문이다.
/// </summary>
public sealed class ScreenRegion
{
    /// <summary>요소의 런타임 타입 이름.</summary>
    public required string Type { get; set; }

    /// <summary>이 요소를 바로 지목하는 셀렉터("id=…" 등). 없으면 null.</summary>
    public string? Selector { get; set; }

    /// <summary>셀렉터가 없을 때, 기준으로 삼을 조상의 셀렉터.</summary>
    public string? Anchor { get; set; }

    /// <summary>기준점 안에서의 가로 비율(0.0~1.0).</summary>
    public double? AnchorX { get; set; }

    /// <summary>기준점 안에서의 세로 비율(0.0~1.0).</summary>
    public double? AnchorY { get; set; }

    /// <summary>표시 텍스트. 없으면 null.</summary>
    public string? Text { get; set; }
}

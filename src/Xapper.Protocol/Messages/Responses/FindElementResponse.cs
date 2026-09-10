namespace Xapper.Protocol.Messages.Responses;

/// <summary>
/// 요소 검색 결과 응답.
/// </summary>
public sealed class FindElementResponse
{
    /// <summary>검색 조건에 매칭된 요소 목록.</summary>
    public List<ElementMatch> Matches { get; set; } = [];

    /// <summary>순회할 수 없어 건너뛴 노드의 설명 목록. 비어 있으면 트리를 끝까지 탐색한 것.</summary>
    public List<string> SkippedNodes { get; set; } = [];
}

/// <summary>
/// 검색에 매칭된 단일 UI 요소의 요약 정보.
/// </summary>
public sealed class ElementMatch
{
    /// <summary>매칭된 요소의 참조 번호.</summary>
    public int Ref { get; set; }

    /// <summary>컨트롤 타입의 전체 이름.</summary>
    public required string Type { get; set; }

    /// <summary>FrameworkElement.Name 속성값.</summary>
    public string? Name { get; set; }

    /// <summary>AutomationProperties.AutomationId 속성값.</summary>
    public string? AutomationId { get; set; }

    /// <summary>텍스트 콘텐츠.</summary>
    public string? Text { get; set; }
}

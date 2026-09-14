namespace Xapper.McpServer.Tools;

/// <summary>
/// 여러 도구가 똑같이 받는 파라미터의 설명 문구. 요소를 가리키는 두 방식(ref, target)은 11개 도구에 같은 뜻으로
/// 쓰이므로 한곳에 두어 어긋나지 않게 한다.
/// </summary>
internal static class ToolDescriptions
{
    /// <summary>ref 파라미터 설명. target 이 있으면 생략할 수 있음을 알린다.</summary>
    public const string Ref = "Element ref from snapshot/find (or use target)";

    /// <summary>target 파라미터 설명. 문법과 0개/여러 개 매칭 시의 결과를 한 줄로 알린다.</summary>
    public const string Target =
        "Selector instead of ref: \"id=…\", \"name=…\", \"text=…\", \"type=…\" (comma = AND; substring match except " +
        "type; must match exactly one element)";
}

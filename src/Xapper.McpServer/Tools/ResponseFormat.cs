using System.Text;
using Xapper.Protocol;
using Xapper.Protocol.Messages.Responses;

namespace Xapper.McpServer.Tools;

/// <summary>
/// Inspector 응답을 도구 결과 문자열로 바꾸는 포매터 모음.
/// 단일 도구와 xapper_batch 가 같은 포매터를 쓰므로 에이전트는 한 가지 출력 형식만 익히면 된다.
/// </summary>
internal static class ResponseFormat
{
    #region Public Methods

    /// <summary>
    /// 액션 응답(<see cref="ActionResponse"/>)을 문자열로 만든다.
    /// 성공이면 Inspector 가 준 메시지, 메시지가 없으면 <paramref name="successFallback"/>.
    /// </summary>
    /// <param name="response">Inspector 응답.</param>
    /// <param name="successFallback">성공했지만 메시지가 없을 때 쓸 문구.</param>
    public static string Action(IpcMessage response, string successFallback)
    {
        if (response.Type == "error")
            return $"Error: {response.Payload}";

        var result = IpcSerializer.DeserializePayload<ActionResponse>(response.Payload!.Value);
        return result.Success ? result.Message ?? successFallback : $"Failed: {result.Error}";
    }

    /// <summary>검색 응답(<see cref="FindElementResponse"/>)을 번호 매긴 매치 목록으로 만든다.</summary>
    /// <param name="response">Inspector 응답.</param>
    public static string Find(IpcMessage response)
    {
        if (response.Type == "error")
            return $"Error: {response.Payload}";

        var result = IpcSerializer.DeserializePayload<FindElementResponse>(response.Payload!.Value);

        var sb = new StringBuilder();

        if (result.Matches.Count == 0)
        {
            sb.AppendLine("No matching elements found.");
        }
        else
        {
            sb.AppendLine($"Found {result.Matches.Count} match(es):");
            foreach (var match in result.Matches)
            {
                var parts = new List<string> { $"[ref={match.Ref}] {match.Type}" };
                if (!string.IsNullOrEmpty(match.Name)) parts.Add($"name=\"{match.Name}\"");
                if (!string.IsNullOrEmpty(match.AutomationId)) parts.Add($"id=\"{match.AutomationId}\"");
                if (!string.IsNullOrEmpty(match.Text)) parts.Add($"text=\"{match.Text}\"");
                sb.AppendLine($"  {string.Join(" ", parts)}");
            }
        }

        AppendSkippedNodes(sb, result.SkippedNodes);
        return sb.ToString();
    }

    /// <summary>프로퍼티 조회 응답(<see cref="PropertyResponse"/>)을 한 줄로 만든다.</summary>
    /// <param name="response">Inspector 응답.</param>
    public static string Property(IpcMessage response)
    {
        if (response.Type == "error")
            return $"Error: {response.Payload}";

        var result = IpcSerializer.DeserializePayload<PropertyResponse>(response.Payload!.Value);
        return $"ref={result.Ref} {result.PropertyName} = \"{result.Value}\" ({result.ValueType})";
    }

    /// <summary>단언 응답을 PASS/FAIL 문자열로 만든다. Inspector 가 상세 메시지를 주면 그것을 우선한다.</summary>
    /// <param name="response">Inspector 응답.</param>
    public static string Assert(IpcMessage response)
    {
        if (response.Type == "error")
            return $"Error: {response.Payload}";

        var result = IpcSerializer.DeserializePayload<ActionResponse>(response.Payload!.Value);
        return result.Message ?? (result.Success ? "PASS" : "FAIL");
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// 순회하지 못해 건너뛴 노드를 결과 끝에 한 줄로 덧붙입니다.
    /// 검색이 트리 전체를 보지 못했다는 사실만 알리면 충분하다 — 사유는 대개 하나로 같고, 하나하나 나열하면
    /// 호출자가 읽을 것이 없는 줄만 늘어난다. 어디서 막혔는지 낱낱이 보려면 스냅샷의 json 출력을 쓴다.
    /// </summary>
    private static void AppendSkippedNodes(StringBuilder sb, List<string> skippedNodes)
    {
        if (skippedNodes.Count == 0)
            return;

        sb.AppendLine();
        sb.AppendLine($"WARNING: skipped {skippedNodes.Count} node(s); their subtrees were not searched. " +
                      $"{SkippedNodeSummary.Summarize(skippedNodes)}. " +
                      "Use xapper_snapshot with format=\"json\" to see each one.");
    }

    #endregion
}

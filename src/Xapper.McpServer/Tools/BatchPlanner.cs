using System.Text.Json;
using System.Text.Json.Nodes;

namespace Xapper.McpServer.Tools;

/// <summary>
/// xapper_batch 의 한 단계. 에이전트가 적은 도구 이름과, 그것을 보낼 IPC 메서드명, 그대로 전달할 페이로드.
/// </summary>
internal sealed class BatchStep
{
    /// <summary>에이전트가 적은 도구 이름(click, get_property 등). 결과 포매터를 고르는 데 쓴다.</summary>
    public required string Tool { get; init; }

    /// <summary>Inspector 에 보낼 IPC 메서드명.</summary>
    public required string Method { get; init; }

    /// <summary>단계 객체에서 <c>tool</c> 을 뺀 나머지. Inspector 요청 페이로드로 그대로 나간다.</summary>
    public required JsonObject Payload { get; init; }
}

/// <summary>
/// xapper_batch 의 <c>steps</c> JSON 을 실행 계획으로 바꾼다. 어떤 입력에도 예외를 던지지 않고,
/// 잘못된 곳을 단계 번호와 함께 오류 문장으로 돌려준다. IPC 를 모르므로 단위 테스트가 가능하다.
/// </summary>
internal static class BatchPlanner
{
    #region Fields

    /// <summary>한 배치가 가질 수 있는 최대 단계 수. 에이전트가 실수로 거대한 배열을 보내 세션을 오래 붙드는 것을 막는다.</summary>
    public const int MaxSteps = 50;

    /// <summary>도구 이름 → IPC 메서드명. 여기 없는 이름은 배치에서 지원하지 않는다.</summary>
    private static readonly Dictionary<string, string> Methods = new(StringComparer.Ordinal)
    {
        ["click"] = "click",
        ["doubleclick"] = "click",
        ["rightclick"] = "rightclick",
        ["wheel"] = "wheel",
        ["type"] = "type",
        ["key"] = "key",
        ["select"] = "select",
        ["toggle"] = "toggle",
        ["expand"] = "expand",
        ["scroll"] = "scroll",
        ["find"] = "find",
        ["get_property"] = "getProperty",
        ["assert"] = "assert",
    };

    private static readonly string SupportedTools = string.Join(", ", Methods.Keys);

    #endregion

    #region Public Methods

    /// <summary>
    /// <paramref name="steps"/> 를 검사해 실행 계획을 만든다.
    /// </summary>
    /// <param name="steps">에이전트가 넘긴 steps 값. 객체 배열이어야 한다.</param>
    /// <param name="plan">성공 시 순서대로 실행할 단계 목록. 실패 시 빈 목록.</param>
    /// <param name="error">실패 이유. 성공 시 null.</param>
    /// <returns>계획을 만들었으면 true.</returns>
    public static bool TryPlan(JsonElement steps, out List<BatchStep> plan, out string? error)
    {
        // 실패하면 빈 계획을 돌려준다. 앞 단계까지만 담긴 반쪽 계획을 호출자가 실수로 돌리지 못하게 한다.
        plan = new List<BatchStep>();
        var built = new List<BatchStep>();

        // 클라이언트가 배열을 JSON 문자열로 감싸 보내는 경우가 있어 한 번 풀어 준다.
        if (steps.ValueKind == JsonValueKind.String)
        {
            try
            {
                using var parsed = JsonDocument.Parse(steps.GetString() ?? "");
                steps = parsed.RootElement.Clone();
            }
            catch (JsonException)
            {
                error = "Error: steps is a string that is not valid JSON; pass a JSON array of step objects.";
                return false;
            }
        }

        if (steps.ValueKind != JsonValueKind.Array)
        {
            error = "Error: steps must be a JSON array of step objects.";
            return false;
        }

        var count = steps.GetArrayLength();
        if (count > MaxSteps)
        {
            error = $"Error: too many steps ({count}); the limit is {MaxSteps} per batch.";
            return false;
        }

        var index = 0;
        foreach (var element in steps.EnumerateArray())
        {
            index++;

            if (element.ValueKind != JsonValueKind.Object)
            {
                error = $"Error: step {index} is not an object.";
                return false;
            }

            if (!element.TryGetProperty("tool", out var toolElement) || toolElement.ValueKind != JsonValueKind.String)
            {
                error = $"Error: step {index} has no \"tool\" string.";
                return false;
            }

            var tool = toolElement.GetString() ?? "";
            if (!Methods.TryGetValue(tool, out var method))
            {
                error = $"Error: step {index} uses unsupported tool \"{tool}\". Supported: {SupportedTools}.";
                return false;
            }

            var payload = new JsonObject();
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name == "tool")
                    continue;
                payload[property.Name] = JsonNode.Parse(property.Value.GetRawText());
            }

            // 단일 도구가 스키마·검사로 막는 조합을 배치도 똑같이 막는다. Inspector 는 좌표 없는 클릭에서
            // doubleClick·modifiers 를 조용히 무시하므로, 여기서 거르지 않으면 거짓 성공이 된다.
            var hasPoint = payload.ContainsKey("x") && payload.ContainsKey("y");
            if (tool == "doubleclick" && !hasPoint)
            {
                error = $"Error: step {index} (doubleclick) needs x and y.";
                return false;
            }
            if (tool == "click" && payload.ContainsKey("modifiers") && !hasPoint)
            {
                error = $"Error: step {index} (click) needs x and y when modifiers are given.";
                return false;
            }

            if (tool == "doubleclick")
                payload["doubleClick"] = true;

            built.Add(new BatchStep { Tool = tool, Method = method, Payload = payload });
        }

        plan = built;
        error = null;
        return true;
    }

    #endregion
}

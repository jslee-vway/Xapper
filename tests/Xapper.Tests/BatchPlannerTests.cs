using System.Text.Json;
using System.Text.Json.Nodes;
using Xapper.McpServer.Tools;
using Xapper.Protocol;

namespace Xapper.Tests;

/// <summary>
/// xapper_batch 의 단계 계획기가 올바른 입력을 IPC 메서드와 페이로드로 옮기고,
/// 잘못된 입력은 예외 없이 단계 번호가 든 오류 문장으로 돌려주는지 검증한다.
/// </summary>
public class BatchPlannerTests
{
    private static JsonElement Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    [Fact]
    public void TryPlan_MapsToolsToIpcMethodsAndKeepsArguments()
    {
        var steps = Parse("""
            [
              { "tool": "type", "target": "id=UserName", "text": "admin" },
              { "tool": "doubleclick", "ref": 3, "x": 0.5, "y": 0.5, "modifiers": "Ctrl" },
              { "tool": "get_property", "ref": 7, "propertyName": "Text" }
            ]
            """);

        var ok = BatchPlanner.TryPlan(steps, out var plan, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Equal(3, plan.Count);

        Assert.Equal("type", plan[0].Tool);
        Assert.Equal("type", plan[0].Method);
        Assert.Equal("id=UserName", (string?)plan[0].Payload["target"]);
        Assert.Equal("admin", (string?)plan[0].Payload["text"]);
        Assert.False(plan[0].Payload.ContainsKey("tool"));

        Assert.Equal("doubleclick", plan[1].Tool);
        Assert.Equal("click", plan[1].Method);
        Assert.True((bool?)plan[1].Payload["doubleClick"]);
        Assert.Equal(3, (int?)plan[1].Payload["ref"]);
        Assert.Equal(0.5, (double?)plan[1].Payload["x"]);
        Assert.Equal("Ctrl", (string?)plan[1].Payload["modifiers"]);
        Assert.False(plan[1].Payload.ContainsKey("tool"));

        Assert.Equal("get_property", plan[2].Tool);
        Assert.Equal("getProperty", plan[2].Method);
        Assert.Equal("Text", (string?)plan[2].Payload["propertyName"]);
    }

    [Fact]
    public void TryPlan_UnsupportedToolNamesTheStep()
    {
        var steps = Parse("""[ { "tool": "click", "ref": 1 }, { "tool": "drag", "sourceRef": 1 } ]""");

        var ok = BatchPlanner.TryPlan(steps, out var plan, out var error);

        Assert.False(ok);
        Assert.Empty(plan);
        Assert.NotNull(error);
        Assert.Contains("step 2", error);
        Assert.Contains("\"drag\"", error);
        Assert.Contains("get_property", error);
    }

    [Theory]
    [InlineData("""{ "tool": "click" }""")]
    [InlineData("\"click\"")]
    [InlineData("null")]
    public void TryPlan_RejectsNonArray(string json)
    {
        var ok = BatchPlanner.TryPlan(Parse(json), out var plan, out var error);

        Assert.False(ok);
        Assert.Empty(plan);
        Assert.Contains("array", error);
    }

    [Fact]
    public void TryPlan_RejectsMoreThanMaxSteps()
    {
        var json = "[" + string.Join(",", Enumerable.Repeat("""{ "tool": "toggle", "ref": 1 }""", BatchPlanner.MaxSteps + 1)) + "]";

        var ok = BatchPlanner.TryPlan(Parse(json), out var plan, out var error);

        Assert.False(ok);
        Assert.Empty(plan);
        Assert.Contains(BatchPlanner.MaxSteps.ToString(), error);
    }

    [Theory]
    [InlineData("""[ { "ref": 1 } ]""")]
    [InlineData("""[ { "tool": 5, "ref": 1 } ]""")]
    [InlineData("""[ { "tool": "click" }, 42 ]""")]
    public void TryPlan_RejectsStepsWithoutToolString(string json)
    {
        var ok = BatchPlanner.TryPlan(Parse(json), out var plan, out var error);

        Assert.False(ok);
        Assert.Empty(plan);
        Assert.NotNull(error);
        Assert.Contains("step", error);
    }

    [Fact]
    public void TryPlan_AcceptsTheArrayWrappedInAJsonString()
    {
        var steps = Parse("\"[{\\\"tool\\\":\\\"toggle\\\",\\\"ref\\\":2}]\"");

        var ok = BatchPlanner.TryPlan(steps, out var plan, out var error);

        Assert.True(ok);
        Assert.Null(error);
        Assert.Single(plan);
        Assert.Equal("toggle", plan[0].Method);
    }

    [Theory]
    [InlineData("""[{ "tool": "doubleclick", "ref": 1 }]""", "needs x and y")]
    [InlineData("""[{ "tool": "click", "ref": 1, "modifiers": "Ctrl" }]""", "needs x and y when modifiers")]
    public void TryPlan_AppliesTheSingleToolGuards(string json, string expectedFragment)
    {
        // 단일 도구가 스키마·검사로 막는 조합(좌표 없는 더블클릭, 좌표 없는 수식키 클릭)을 배치도 거른다 —
        // Inspector 는 그 인자를 조용히 무시해 거짓 성공이 되기 때문이다.
        var ok = BatchPlanner.TryPlan(Parse(json), out var plan, out var error);

        Assert.False(ok);
        Assert.Empty(plan);
        Assert.NotNull(error);
        Assert.Contains("step 1", error);
        Assert.Contains(expectedFragment, error);
    }

    [Fact]
    public void CreateRequest_EmitsJsonObjectPayloadVerbatim()
    {
        var payload = new JsonObject
        {
            ["ref"] = 3,
            ["target"] = "id=Login",
            ["doubleClick"] = true,
        };

        var request = IpcSerializer.CreateRequest("click", payload);
        var bytes = IpcSerializer.Serialize(request);
        using var doc = JsonDocument.Parse(bytes.AsSpan(4).ToArray());
        var sent = doc.RootElement.GetProperty("payload");

        Assert.Equal(JsonValueKind.Object, sent.ValueKind);
        Assert.Equal(3, sent.GetProperty("ref").GetInt32());
        Assert.Equal("id=Login", sent.GetProperty("target").GetString());
        Assert.True(sent.GetProperty("doubleClick").GetBoolean());
    }
}

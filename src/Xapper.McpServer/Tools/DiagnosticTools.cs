using System.ComponentModel;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Server;
using Xapper.Protocol;
using Xapper.Protocol.Messages.Responses;

namespace Xapper.McpServer.Tools;

/// <summary>
/// 프로퍼티 조회, 바인딩 검사, 값 검증(assert) 등 진단 MCP 도구를 제공하는 클래스.
/// </summary>
[McpServerToolType]
public sealed class DiagnosticTools
{
    private readonly SessionManager _sessionManager;

    public DiagnosticTools(SessionManager sessionManager)
    {
        _sessionManager = sessionManager;
    }

    [McpServerTool(Name = "xapper_get_property"), Description(
        "Read one property of an element: a dependency property or public property of that control type; " +
        "paths like ItemsSource.Count are not resolved.")]
    public async Task<string> GetProperty(
        [Description("Property name (e.g., 'Text', 'IsEnabled', 'Visibility', 'Content')")] string propertyName,
        [Description(ToolDescriptions.Ref)] int? @ref = null,
        [Description(ToolDescriptions.Target)] string? target = null,
        CancellationToken ct = default)
    {
        if (@ref is null && target is null)
            return "Error: pass ref or target.";

        var client = _sessionManager.GetActive();
        var response = await client.GetPropertyAsync(@ref, target: target, propertyName: propertyName, ct: ct);

        return ResponseFormat.Property(response);
    }

    [McpServerTool(Name = "xapper_get_bindings"), Description("Get data binding info and errors for an element")]
    public async Task<string> GetBindings(
        [Description("Element ref from snapshot/find")] int @ref,
        CancellationToken ct = default)
    {
        var client = _sessionManager.GetActive();
        var response = await client.GetBindingsAsync(@ref, ct);

        if (response.Type == "error")
            return $"Error: {response.Payload}";

        var result = IpcSerializer.DeserializePayload<BindingsResponse>(response.Payload!.Value);

        if (result.Bindings.Count == 0)
            return $"ref={@ref}: No bindings found.";

        var sb = new StringBuilder();
        sb.AppendLine($"ref={@ref} Bindings ({result.Bindings.Count}):");
        foreach (var b in result.Bindings)
        {
            var status = b.HasError ? $" [ERROR: {b.ErrorMessage}]" : "";
            sb.AppendLine($"  {b.PropertyName} ← {b.Path} (Source: {b.Source}, Mode: {b.Mode}){status}");
        }
        return sb.ToString();
    }

    [McpServerTool(Name = "xapper_assert"), Description("Assert a property value on an element (returns PASS/FAIL)")]
    public async Task<string> Assert(
        [Description("Property name to check")] string property,
        [Description("Expected value (string comparison, case-insensitive)")] string expected,
        [Description(ToolDescriptions.Ref)] int? @ref = null,
        [Description(ToolDescriptions.Target)] string? target = null,
        CancellationToken ct = default)
    {
        if (@ref is null && target is null)
            return "Error: pass ref or target.";

        var client = _sessionManager.GetActive();
        var response = await client.AssertAsync(@ref, target: target, property: property, expected: expected, ct: ct);

        return ResponseFormat.Assert(response);
    }
}

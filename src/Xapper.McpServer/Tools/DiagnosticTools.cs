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
        "Get a single property value of an element. The name must be one dependency property or public " +
        "instance property of that exact control type; property paths such as 'ItemsSource.Count' are not " +
        "resolved and are reported as such. To reach into a value, read the property itself and inspect the " +
        "result, or find the child elements with xapper_find.")]
    public async Task<string> GetProperty(
        [Description("Element ref from last snapshot")] int @ref,
        [Description("Property name (e.g., 'Text', 'IsEnabled', 'Visibility', 'Content')")] string propertyName,
        CancellationToken ct = default)
    {
        var client = _sessionManager.GetActive();
        var response = await client.GetPropertyAsync(@ref, propertyName, ct);

        if (response.Type == "error")
            return $"Error: {response.Payload}";

        var result = IpcSerializer.DeserializePayload<PropertyResponse>(response.Payload!.Value);
        return $"ref={result.Ref} {result.PropertyName} = \"{result.Value}\" ({result.ValueType})";
    }

    [McpServerTool(Name = "xapper_get_bindings"), Description("Get data binding info and errors for an element")]
    public async Task<string> GetBindings(
        [Description("Element ref from last snapshot")] int @ref,
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
        [Description("Element ref from last snapshot")] int @ref,
        [Description("Property name to check")] string property,
        [Description("Expected value (string comparison, case-insensitive)")] string expected,
        CancellationToken ct = default)
    {
        var client = _sessionManager.GetActive();
        var response = await client.AssertAsync(@ref, property, expected, ct);

        if (response.Type == "error")
            return $"Error: {response.Payload}";

        var result = IpcSerializer.DeserializePayload<ActionResponse>(response.Payload!.Value);
        return result.Message ?? (result.Success ? "PASS" : "FAIL");
    }
}

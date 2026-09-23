using System.ComponentModel;
using System.Text;
using ModelContextProtocol.Server;
using Xapper.McpServer.Ipc;
using Xapper.Protocol;
using Xapper.Protocol.Messages.Requests;
using Xapper.Protocol.Messages.Responses;

namespace Xapper.McpServer.Tools;

/// <summary>
/// 앱이 일으킨 이벤트를 읽는 MCP 도구.
///
/// 시각 트리가 답하지 못하는 물음이 있어서 낸 통로다. 고성능 그리드는 셀을 요소로 만들지 않으므로
/// "행을 눌렀는데 눌렸는가" 를 트리에서는 확인할 수 없었고, 그때마다 그림을 찍는 수밖에 없었다.
/// 요소가 없어도 선택이 바뀌면 이벤트는 난다.
/// </summary>
[McpServerToolType]
public sealed class EventTools
{
    #region Fields

    private readonly SessionManager _sessionManager;

    #endregion

    #region Constructor

    /// <summary><see cref="EventTools"/>의 새 인스턴스를 생성합니다.</summary>
    /// <param name="sessionManager">활성 Inspector 세션을 제공하는 세션 관리자.</param>
    public EventTools(SessionManager sessionManager)
    {
        _sessionManager = sessionManager;
    }

    #endregion

    #region MCP Tools

    /// <summary>앱에서 최근에 일어난 이벤트를 읽습니다.</summary>
    [McpServerTool(Name = "xapper_events"), Description(
        "Read what the app itself just did, as a list of the routed events it raised. Use it to confirm an " +
        "action landed instead of taking a picture: a click on a grid row shows up as the selection changing, " +
        "typing shows up as the text changing, and a button that did nothing raises nothing. It works where the " +
        "visual tree does not - a grid that paints its own cells has no element to inspect, but it still raises " +
        "events. Xapper keeps the last 200, minus the noisy ones nobody can act on, and hands back the most " +
        "recent few. Call it right after the action, while what you are looking for is still near the end.")]
    public async Task<string> Events(
        [Description("How many of the most recent events to return (default 10)")] int count = 10,
        [Description("Keep only events whose name, source type or source name contains this. Optional")] string? filter = null,
        CancellationToken ct = default)
    {
        if (count <= 0)
            return "Error: count must be greater than 0.";

        InspectorClient client;
        try
        {
            client = _sessionManager.GetActive();
        }
        catch (InvalidOperationException ex)
        {
            return $"Error: {ex.Message}";
        }

        var request = IpcSerializer.CreateRequest(
            "events", new EventsRequest { Count = count, Filter = filter });
        var response = await client.SendAsync(request, ct);

        if (response.Type == "error")
            return $"Error: {response.Payload}";

        if (response.Payload is not { } payload)
            return "Error: the inspector returned no events.";

        var result = IpcSerializer.DeserializePayload<EventsResponse>(payload);
        return result is null ? "Error: the inspector returned no events." : Describe(result, filter);
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// 이벤트 목록을 읽을 문장으로 옮깁니다. 오래된 것부터 적어 마지막 줄이 가장 최근이 되게 한다.
    /// 비어 있을 때 그 사실만 돌려주면 부르는 쪽이 왜 비었는지 몰라 다시 부르므로, 그럴 만한 이유를 함께 적는다.
    /// </summary>
    /// <param name="result">Inspector 가 돌려준 이벤트.</param>
    /// <param name="filter">요청에 실렸던 거르기 조건.</param>
    private static string Describe(EventsResponse result, string? filter)
    {
        if (result.Events.Count == 0)
        {
            return result.Stored == 0
                ? "No events have been recorded yet. Watching starts on the first call, so act and ask again."
                : $"None of the {result.Stored} recorded events match" +
                  (string.IsNullOrWhiteSpace(filter) ? "." : $" \"{filter}\". Ask again without the filter.");
        }

        var sb = new StringBuilder();
        sb.AppendLine($"{result.Events.Count} of the last {result.Stored} events, oldest first:");

        foreach (var entry in result.Events)
        {
            sb.Append("  ").Append($"{entry.AgoMs} ms ago  ").Append(entry.Name).Append("  on ")
              .Append(entry.SourceType);

            if (!string.IsNullOrWhiteSpace(entry.SourceName))
                sb.Append(" \"").Append(entry.SourceName).Append('"');

            if (!string.IsNullOrWhiteSpace(entry.Detail))
                sb.Append("  - ").Append(entry.Detail);

            sb.AppendLine();
        }

        return sb.ToString().TrimEnd();
    }

    #endregion
}

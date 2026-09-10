using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

// 우리 네임스페이스 이름과 SDK 타입 이름이 같아 별칭을 둔다.
using McpServerInstance = ModelContextProtocol.Server.McpServer;

namespace Xapper.McpServer.Tools;

/// <summary>
/// 실제 마우스 입력을 쓰기 전에 사람에게 직접 허락을 받는 관문.
/// 인자 하나로 여는 문은 에이전트가 스스로 열 수 있어 사람의 승인이 아니다. 물리 커서를 가져가고
/// 하던 작업의 포커스를 끊는 조작이므로, 그 순간 화면 앞에 있는 사람이 결정해야 한다.
/// </summary>
internal static class RealInputApproval
{
    #region Public Methods

    /// <summary>
    /// 실제 입력을 써도 되는지 사용자에게 묻습니다.
    /// </summary>
    /// <param name="server">묻기를 중계할 서버.</param>
    /// <param name="whatItWillDo">무엇을 하려는지 한 구절로 설명.</param>
    /// <param name="ct">취소 토큰.</param>
    /// <returns>사용자가 허락했으면 true.</returns>
    public static async Task<bool> AskAsync(McpServerInstance server, string whatItWillDo, CancellationToken ct)
    {
        // 물어볼 길이 없는 클라이언트에서는 허락으로 간주하지 않는다. 조용히 커서를 가져가는 것이
        // 이 관문이 막으려는 바로 그 일이다.
        if (server.ClientCapabilities?.Elicitation is null)
            return false;

        try
        {
            var result = await server.ElicitAsync(new ElicitRequestParams
            {
                Message =
                    $"Xapper wants to {whatItWillDo} using real mouse input. That moves the physical cursor and " +
                    "gives the target window focus, so it will interrupt whatever you are typing into right now. " +
                    "There is no other way to reach this control. Allow it?",

                // 규격상 form 모드에는 스키마가 있어야 한다. 물어보는 것이 예/아니오뿐이라 빈 스키마를 준다.
                RequestedSchema = new ElicitRequestParams.RequestSchema()
            }, ct);

            return string.Equals(result.Action, "accept", StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // 물음 자체가 실패하면 승인받지 못한 것이다. 취소는 흡수하지 않고 호출자에게 그대로 전한다.
            return false;
        }
    }

    #endregion
}

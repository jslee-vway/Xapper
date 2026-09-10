using Xapper.McpServer.Tools;
using Xapper.Protocol;

namespace Xapper.Tests;

/// <summary>
/// 실제 입력이 필요하다는 거절을 알아채는 판별을 검증하는 테스트 클래스.
/// 썸으로 처리할 수 있는지는 주입된 쪽만 알기 때문에, 도구는 그쪽 응답을 보고서야 사람에게 물을지 정한다.
/// 이 판별이 조용히 어긋나면 물어보지도 않고 그냥 실패하게 된다.
/// </summary>
public class RealInputGateTests
{
    [Fact]
    public void NeedsRealInput_RecognisesTheRefusalThatAsksForRealInput()
    {
        var refusal = IpcSerializer.CreateError(
            "id", RealInputRequired.Marker + " This drag would need real mouse input.");

        Assert.True(InteractionTools.NeedsRealInput(refusal));
    }

    [Fact]
    public void NeedsRealInput_IgnoresOtherFailures()
    {
        var other = IpcSerializer.CreateError("id", "Element ref=3 cannot be resolved.");

        Assert.False(InteractionTools.NeedsRealInput(other));
    }

    [Fact]
    public void NeedsRealInput_IgnoresASuccessfulDrag()
    {
        var success = IpcSerializer.CreateResponse("id", new { message = "Dragged via its own drag events" });

        Assert.False(InteractionTools.NeedsRealInput(success));
    }
}

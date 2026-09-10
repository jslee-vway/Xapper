using ModelContextProtocol.Protocol;
using Xapper.McpServer;
using Xapper.McpServer.Tools;
using Xapper.Protocol;
using Xapper.Protocol.Messages.Responses;

namespace Xapper.Tests;

/// <summary>
/// 스크린샷 도구가 그림을 온전히 돌려주는지 검증하는 테스트 클래스.
/// 이전에는 base64 앞 100자만 붙이고 잘라 버려 호출자가 화면을 볼 방법이 없었다.
/// </summary>
public class CaptureToolsTests
{
    #region Helpers

    /// <summary>PNG 자리에 쓸 임의의 바이트. 도구는 내용을 해석하지 않고 그대로 전달한다.</summary>
    private static readonly byte[] PngBytes = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0x01, 0x02, 0x03];

    /// <summary>스크린샷 요청 한 건에 정해진 응답을 돌려주는 가짜 Inspector를 띄웁니다.</summary>
    private static Task RespondWithScreenshot(System.IO.Pipes.NamedPipeServerStream pipe) => Task.Run(async () =>
    {
        var request = await IpcSerializer.DeserializeAsync(pipe);
        var payload = new ScreenshotResponse
        {
            Base64Png = Convert.ToBase64String(PngBytes),
            Width = 320,
            Height = 240
        };
        await pipe.WriteAsync(IpcSerializer.Serialize(IpcSerializer.CreateResponse(request!.Id, payload)));
    });

    #endregion

    [Fact]
    public async Task Screenshot_ReturnsTheWholeImage_NotATruncatedString()
    {
        var processId = FakeInspector.NextProcessId();
        await using var fakeInspector = FakeInspector.Create(processId);

        await using var sessions = new SessionManager();
        var accepting = fakeInspector.WaitForConnectionAsync();
        await sessions.AttachAsync(processId);
        await accepting;

        var responding = RespondWithScreenshot(fakeInspector);
        var blocks = (await new CaptureTools(sessions).Screenshot()).ToList();
        await responding;

        var summary = Assert.IsType<TextContentBlock>(blocks[0]);
        Assert.Contains("320x240", summary.Text);

        var image = Assert.IsType<ImageContentBlock>(blocks[1]);
        Assert.Equal("image/png", image.MimeType);
        Assert.Equal(PngBytes, image.DecodedData.ToArray());
    }

    [Fact]
    public async Task Screenshot_WithSavePath_WritesTheFileAndReportsWhereItWent()
    {
        var processId = FakeInspector.NextProcessId();
        await using var fakeInspector = FakeInspector.Create(processId);

        await using var sessions = new SessionManager();
        var accepting = fakeInspector.WaitForConnectionAsync();
        await sessions.AttachAsync(processId);
        await accepting;

        var savePath = Path.Combine(Path.GetTempPath(), $"xapper_test_{processId}.png");
        try
        {
            var responding = RespondWithScreenshot(fakeInspector);
            var blocks = (await new CaptureTools(sessions).Screenshot(savePath: savePath)).ToList();
            await responding;

            var summary = Assert.IsType<TextContentBlock>(blocks[0]);
            Assert.Contains(savePath, summary.Text);
            Assert.Equal(PngBytes, await File.ReadAllBytesAsync(savePath));
        }
        finally
        {
            File.Delete(savePath);
        }
    }

    [Fact]
    public async Task Screenshot_WithNonPositiveMaxWidth_SaysSoInsteadOfSilentlyIgnoringIt()
    {
        var blocks = (await new CaptureTools(new SessionManager()).Screenshot(maxWidth: 0)).ToList();

        var only = Assert.IsType<TextContentBlock>(Assert.Single(blocks));
        Assert.Contains("maxWidth", only.Text);
    }
}

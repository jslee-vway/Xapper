using ModelContextProtocol.Protocol;
using Xapper.McpServer;
using Xapper.McpServer.Infrastructure;
using Xapper.McpServer.Tools;
using Xapper.Protocol;
using Xapper.Protocol.Messages.Responses;

namespace Xapper.Tests;

/// <summary>
/// 스크린샷 도구가 그림을 온전히 돌려주는지 검증하는 테스트 클래스.
/// 이전에는 base64 앞 100자만 붙이고 잘라 버려 호출자가 화면을 볼 방법이 없었다.
/// </summary>
public class CaptureToolsTests : IDisposable
{
    #region Helpers

    /// <summary>테스트마다 제 몫의 빈 저장소를 쓴다. 기록이 섞이면 그림 대신 기록이 나가는 길로 빠진다.</summary>
    private readonly string _storePath =
        Path.Combine(Path.GetTempPath(), $"xapper-capture-{Guid.NewGuid():N}.db");

    private ScreenStore NewStore() => new(_storePath);

    public void Dispose()
    {
        try { File.Delete(_storePath); } catch (IOException) { }
    }

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

    /// <summary>지문을 실은 스크린샷 응답을 돌려주는 가짜 Inspector를 띄웁니다.</summary>
    private static Task RespondWithScreenshot(System.IO.Pipes.NamedPipeServerStream pipe, string signature) =>
        Task.Run(async () =>
        {
            var request = await IpcSerializer.DeserializeAsync(pipe);
            var payload = new ScreenshotResponse
            {
                Base64Png = Convert.ToBase64String(PngBytes),
                Width = 320,
                Height = 240,
                Signature = signature
            };
            await pipe.WriteAsync(IpcSerializer.Serialize(IpcSerializer.CreateResponse(request!.Id, payload)));
        });

    /// <summary>셀렉터를 가진 영역이 있는, 쓸 만한 기록 하나를 저장소에 넣습니다.</summary>
    private void LearnScreen(string signature)
    {
        using var store = NewStore();
        store.Save(new ScreenRecord
        {
            Signature = signature,
            App = "DemoApp",
            Name = "로그인 화면",
            Regions = [new ScreenRegion { Type = "Button", Selector = "id=Save" }]
        });
    }

    #endregion

    [Fact]
    public async Task Screenshot_WithAnnotate_ReturnsTheRecordInsteadOfAnImage_OnALearnedScreen()
    {
        // annotate 가 주려는 것은 "눌러 쓸 수 있는 목록" 이고, 배워 둔 기록이 바로 그 목록이다.
        // 같은 것을 그림으로 한 번 더 받으면 토큰만 나간다.
        const string signature = "sig-known";
        LearnScreen(signature);

        var processId = FakeInspector.NextProcessId();
        await using var fakeInspector = FakeInspector.Create(processId);
        await using var sessions = new SessionManager();
        var accepting = fakeInspector.WaitForConnectionAsync();
        await sessions.AttachAsync(processId);
        await accepting;

        var responding = RespondWithScreenshot(fakeInspector, signature);
        var blocks = (await new CaptureTools(sessions, NewStore()).Screenshot(annotate: true)).ToList();
        await responding;

        var only = Assert.IsType<TextContentBlock>(Assert.Single(blocks));
        Assert.Contains("id=Save", only.Text);
        Assert.Contains("without annotate", only.Text);
        Assert.DoesNotContain(blocks, block => block is ImageContentBlock);
    }

    [Fact]
    public async Task Screenshot_TellsTheCallerWhenTheScreenIsNotInTheRecordYet()
    {
        // 그림을 받아 드는 순간이 배우기에 가장 좋은 시점이므로, 그 사실을 그림에 딸려 보낸다.
        var processId = FakeInspector.NextProcessId();
        await using var fakeInspector = FakeInspector.Create(processId);
        await using var sessions = new SessionManager();
        var accepting = fakeInspector.WaitForConnectionAsync();
        await sessions.AttachAsync(processId);
        await accepting;

        var responding = RespondWithScreenshot(fakeInspector, "sig-unknown");
        var blocks = (await new CaptureTools(sessions, NewStore()).Screenshot()).ToList();
        await responding;

        var summary = Assert.IsType<TextContentBlock>(blocks[0]);
        Assert.Contains("xapper_screen_learn", summary.Text);
        Assert.Contains(blocks, block => block is ImageContentBlock);
    }

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
        var blocks = (await new CaptureTools(sessions, NewStore()).Screenshot()).ToList();
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
            var blocks = (await new CaptureTools(sessions, NewStore()).Screenshot(savePath: savePath)).ToList();
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
        var blocks = (await new CaptureTools(new SessionManager(), NewStore()).Screenshot(maxWidth: 0)).ToList();

        var only = Assert.IsType<TextContentBlock>(Assert.Single(blocks));
        Assert.Contains("maxWidth", only.Text);
    }
}

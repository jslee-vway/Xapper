using System.ComponentModel;
using System.IO;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Xapper.Protocol;
using Xapper.Protocol.Messages.Responses;

namespace Xapper.McpServer.Tools;

/// <summary>
/// 스크린샷 캡처 MCP 도구를 제공하는 클래스.
/// 캡처한 그림을 MCP 이미지 콘텐츠로 그대로 돌려주어 호출자가 화면을 직접 볼 수 있게 한다.
/// </summary>
[McpServerToolType]
public sealed class CaptureTools
{
    #region Fields

    private readonly SessionManager _sessionManager;

    #endregion

    #region Constructor

    /// <summary>
    /// <see cref="CaptureTools"/>의 새 인스턴스를 생성합니다.
    /// </summary>
    /// <param name="sessionManager">활성 Inspector 세션을 제공하는 세션 관리자.</param>
    public CaptureTools(SessionManager sessionManager)
    {
        _sessionManager = sessionManager;
    }

    #endregion

    #region Tools

    /// <summary>
    /// 윈도우 또는 특정 요소를 캡처하여 요약 텍스트와 PNG 이미지를 반환합니다.
    /// </summary>
    /// <param name="ref">캡처할 요소의 참조 번호. 생략하면 전체 윈도우.</param>
    /// <param name="maxWidth">축소할 최대 가로 픽셀 수. 생략하면 원본 크기.</param>
    /// <param name="savePath">PNG를 추가로 저장할 절대 경로. 생략하면 저장하지 않음.</param>
    /// <param name="ct">취소 토큰.</param>
    /// <returns>요약 텍스트와 PNG 이미지 콘텐츠.</returns>
    [McpServerTool(Name = "xapper_screenshot"), Description(
        "Capture the window or a single element and return the image itself, so it can be viewed directly. " +
        "The mode decides where the pixels come from and the two are not interchangeable. \"render\" (the " +
        "default) redraws the application's own visual tree: it works while the window sits behind another " +
        "application or has no focus, and nothing foreign can appear in it - but it draws one visual tree, so " +
        "a dialog in its own window, a popup, a context menu or a combo-box drop-down is simply absent, and " +
        "the response says so when other windows are open. \"screen\" reads the pixels already composited on " +
        "the desktop, so everything a person can see is there, popups included; in exchange it shows whatever " +
        "is on top, and the response warns when none of the application's windows is in front. Reach for " +
        "\"screen\" whenever a dialog or menu is open, or when you need to see what the user sees.")]
    public async Task<IEnumerable<ContentBlock>> Screenshot(
        [Description("Element ref to capture (omit for the whole window)")] int? @ref = null,
        [Description("Shrink to at most this many pixels wide, keeping the aspect ratio (omit for full size)")] int? maxWidth = null,
        [Description("Absolute path to also write the PNG to (omit to skip saving)")] string? savePath = null,
        [Description("Where the pixels come from: 'render' (default, redraws the app) or 'screen' (reads the desktop, includes popups)")] string mode = "render",
        CancellationToken ct = default)
    {
        if (maxWidth is <= 0)
            return [new TextContentBlock { Text = "Error: maxWidth must be greater than 0. Omit it to get the image at full size." }];

        var client = _sessionManager.GetActive();
        var response = await client.ScreenshotAsync(@ref, maxWidth, mode, ct);

        if (response.Type == "error")
            return [new TextContentBlock { Text = $"Error: {response.Payload}" }];

        var result = IpcSerializer.DeserializePayload<ScreenshotResponse>(response.Payload!.Value);
        var png = Convert.FromBase64String(result.Base64Png);

        var summary = $"Screenshot captured: {result.Width}x{result.Height} pixels";
        if (result.Warning is not null)
            summary += $"\nWARNING: {result.Warning}";
        if (savePath is not null)
            summary += await SaveAsync(png, savePath, ct);

        return
        [
            new TextContentBlock { Text = summary },
            ImageContentBlock.FromBytes(png, "image/png")
        ];
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// PNG를 지정된 경로에 저장하고 그 결과를 한 줄로 설명합니다.
    /// 저장에 실패해도 그림 자체는 돌려줄 수 있어야 하므로 실패는 경고로 바꿔 전달하되,
    /// 호출이 취소된 경우는 성공한 척하지 않고 그대로 전파한다.
    /// </summary>
    private static async Task<string> SaveAsync(byte[] png, string savePath, CancellationToken ct)
    {
        // 상대 경로는 MCP 서버 프로세스의 현재 디렉터리를 기준으로 풀리는데 호출자는 그곳을 알 수 없다.
        // 실제로 쓴 경로를 그대로 돌려주어야 호출자가 파일을 찾을 수 있다.
        var resolvedPath = savePath;
        try
        {
            resolvedPath = Path.GetFullPath(savePath);
            await File.WriteAllBytesAsync(resolvedPath, png, ct);
            return $"\nSaved to: {resolvedPath}";
        }
        catch (OperationCanceledException)
        {
            DeleteIfPartiallyWritten(resolvedPath);
            throw;
        }
        catch (Exception ex)
        {
            DeleteIfPartiallyWritten(resolvedPath);
            return $"\nWARNING: could not save to {resolvedPath}: {ex.Message}";
        }
    }

    /// <summary>
    /// 쓰다 만 파일을 지웁니다. 쓰기는 대상 파일을 먼저 비우므로, 실패한 자리에는 PNG처럼 보이지만 깨진 파일이 남는다.
    /// 정리가 실패하더라도 원래의 실패를 덮어쓰지 않는다.
    /// </summary>
    private static void DeleteIfPartiallyWritten(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch
        {
        }
    }

    #endregion
}

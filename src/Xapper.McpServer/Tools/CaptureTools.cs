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
    /// <param name="annotate">true 면 닿을 수 있는 요소에 번호 상자를 그리고 번호마다 ref 를 함께 돌려준다.</param>
    /// <param name="ct">취소 토큰.</param>
    /// <returns>요약 텍스트와 PNG 이미지 콘텐츠.</returns>
    [McpServerTool(Name = "xapper_screenshot"), Description(
        "Capture the window or an element as an image. mode=\"render\" (default) redraws the app's visual tree: " +
        "works while covered or unfocused but omits other windows (dialogs, popups, menus). mode=\"screen\" " +
        "reads the desktop pixels: shows dialogs and popups but also anything on top (the response warns about other " +
        "open windows or the app not being in front). Images cost many tokens, so reach for one only when you need " +
        "to see how something is drawn. To read what a panel or a grid currently holds, call xapper_snapshot with " +
        "rootRef set to it: that returns the same content as text for a fraction of the cost. To check one value, " +
        "call xapper_get_property or xapper_assert. When you do take a picture, narrow it with ref instead of " +
        "capturing the whole window at full size - ref narrows both modes, so choosing screen to catch a dialog " +
        "does not force a full-desktop shot. Only annotate is render-only; with annotate a narrow shot also " +
        "keeps the numbers readable.")]
    public async Task<IEnumerable<ContentBlock>> Screenshot(
        [Description("Element ref to capture (omit for the whole window)")] int? @ref = null,
        [Description("Shrink to at most this many pixels wide, keeping the aspect ratio (omit for full size)")] int? maxWidth = null,
        [Description("Absolute path to also write the PNG to (omit to skip saving)")] string? savePath = null,
        [Description("Where the pixels come from: 'render' (default, redraws the app) or 'screen' (reads the desktop, includes popups)")] string mode = "render",
        [Description("Draw numbered boxes on the elements a click can reach and list each number's ref. Turns one screenshot into refs you can act on, instead of following up with xapper_element_at. An element no selector can reach also gets an anchor ('in id=X at 0.45,0.2'), which still works in a later session when the ref does not. Render mode only")] bool annotate = false,
        CancellationToken ct = default)
    {
        if (maxWidth is <= 0)
            return [new TextContentBlock { Text = "Error: maxWidth must be greater than 0. Omit it to get the image at full size." }];

        var client = _sessionManager.GetActive();
        var response = await client.ScreenshotAsync(@ref, maxWidth, mode, annotate, ct);

        if (response.Type == "error")
            return [new TextContentBlock { Text = $"Error: {response.Payload}" }];

        var result = IpcSerializer.DeserializePayload<ScreenshotResponse>(response.Payload!.Value);
        var png = Convert.FromBase64String(result.Base64Png);

        var summary = $"Screenshot captured: {result.Width}x{result.Height} pixels";
        if (result.Marks.Count > 0)
            summary += $" ({result.Marks.Count} marks)";
        summary += MapToScreen(result);

        var markList = ScreenMarkSummary.Describe(result.Marks, result.MarksOmitted);
        if (markList.Length > 0)
            summary += "\n" + markList;

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
    /// 그림의 픽셀을 화면 좌표로 되돌리는 방법을 설명합니다.
    /// 캡처는 화면 전체가 아니라 창이 놓인 영역의 일부이고 축소까지 되므로,
    /// 이 값 없이는 그림에서 읽은 위치를 xapper_element_at 에 그대로 넣을 수 없다.
    /// </summary>
    private static string MapToScreen(ScreenshotResponse result)
    {
        if (result.OriginX is not { } originX || result.OriginY is not { } originY)
            return "";

        return $"\nImage pixel (0,0) is screen ({originX:F0},{originY:F0}) and the image is scaled by " +
               $"{result.Scale:0.###}. To turn an image position into a screen position for " +
               $"xapper_element_at: screen = origin + image / scale.";
    }

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

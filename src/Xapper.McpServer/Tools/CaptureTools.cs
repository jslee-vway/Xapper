using System.ComponentModel;
using System.IO;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Xapper.McpServer.Infrastructure;
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
    private readonly ScreenStore _store;
    private readonly ScreenTracker _tracker;

    #endregion

    #region Constructor

    /// <summary>
    /// <see cref="CaptureTools"/>의 새 인스턴스를 생성합니다.
    /// </summary>
    /// <param name="sessionManager">활성 Inspector 세션을 제공하는 세션 관리자.</param>
    /// <param name="store">화면 기록이 사는 저장소. 이미 배워 둔 화면이면 그림 대신 기록을 내주기 위해 본다.</param>
    /// <param name="tracker">같은 화면을 몇 장째 찍고 있는지 세어 두는 싱글턴. 이 도구는 호출마다 새로 만들어진다.</param>
    public CaptureTools(SessionManager sessionManager, ScreenStore store, ScreenTracker tracker)
    {
        _sessionManager = sessionManager;
        _store = store;
        _tracker = tracker;
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
        "to see how something is drawn. On a screen you have already learned, xapper_screen_recall hands you the " +
        "same selectors a picture would - and asking for annotate there returns the record instead of an image. " +
        "Every response says whether this screen is in the record, so learn it right after you have looked. To read what a panel or a grid currently holds, call xapper_snapshot with " +
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
        var known = result.Signature is { } signature ? _store.Find(signature) : null;
        var learned = known is not null && known.Regions.Any(region => !string.IsNullOrWhiteSpace(region.Selector));

        // annotate 로 얻으려는 것은 그림 자체가 아니라 "눌러 쓸 수 있는 목록" 이다. 이미 배워 둔 화면이면
        // 기록이 바로 그 목록이므로, 같은 것을 그림으로 한 번 더 받을 이유가 없다.
        if (annotate && learned && known is not null)
            return [new TextContentBlock { Text = InsteadOfAnnotating(known) }];

        var png = Convert.FromBase64String(result.Base64Png);

        var summary = $"Screenshot captured: {result.Width}x{result.Height} pixels";
        if (result.Marks.Count > 0)
            summary += $" ({result.Marks.Count} marks)";
        summary += MapToScreen(result);

        var markList = ScreenMarkSummary.Describe(result.Marks, result.MarksOmitted);
        if (markList.Length > 0)
            summary += "\n" + markList;

        summary += RecordHint(known, result.Signature is { } taken ? _tracker.CountPicture(taken) : 1);

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
    /// 이미 배워 둔 화면에서 annotate 를 요청했을 때, 그림 대신 내줄 문장을 만듭니다.
    /// annotate 가 하는 일과 기록이 하는 일이 같기 때문에 바꿔치기해도 잃는 정보가 없다.
    /// 다만 그리드 내용처럼 픽셀로만 확인되는 것도 있으므로 그림으로 가는 길을 함께 알려 준다.
    /// </summary>
    /// <param name="record">지금 화면의 기록.</param>
    private static string InsteadOfAnnotating(ScreenRecord record)
    {
        return ScreenRecallSummary.Known(record) +
               "\n\nThe image was skipped because this screen is already learned, and the record above says the " +
               "same thing for a fraction of the tokens. Call again without annotate when you need the pixels " +
               "themselves - what a grid currently holds, or how something is drawn.";
    }

    /// <summary>
    /// 이 화면이 기록에 있는지를 한 줄로 덧붙입니다.
    /// 그림을 받아 드는 순간이 배우기에 가장 좋은 시점인데, 지침에만 적어 두면 그 순간에 떠오르지 않는다
    /// (실측: 46장을 찍는 동안 학습이 한 번도 일어나지 않았다). 그래서 사실을 그림에 딸려 보낸다.
    ///
    /// 기록이 있기만 하면 영역의 상태는 따지지 않는다. 영역이 낡았다면 다음 조회가 알아서 다시 뽑기 때문이다.
    /// 여기서 "다시 배우라" 고 권하면 서버가 이미 맡은 일을 모델에게 또 시키는 꼴이 되고, 두 통로가 같은
    /// 상태를 두고 다른 말을 하게 된다. 배우는 일이 남는 것은 기록이 아예 없을 때뿐이다.
    /// </summary>
    /// <param name="record">지금 화면의 기록. 없으면 null.</param>
    /// <param name="pictureCount">마지막으로 이 화면에 무언가를 기록한 뒤 몇 장째인지.</param>
    private static string RecordHint(ScreenRecord? record, int pictureCount)
    {
        var hint = record is null
            ? "\nThis screen is not in the record yet. Put what this picture told you into xapper_screen_learn " +
              "before you move on, so the next visit needs no picture."
            : $"\nThis screen is already learned as \"{record.Name}\". Call xapper_screen_recall for its " +
              "selectors instead of looking again - stale ones are retaken there without being asked - and put " +
              "what this picture told you into xapper_screen_note before you move on.";

        // 같은 화면을 거듭 찍으면서 아무것도 남기지 않는 것이 가장 흔한 낭비다. 한 장째에는 권하고, 그 뒤로는
        // 몇 장째인지를 들이민다. 세어 보이는 편이 같은 문장을 되풀이하는 것보다 잘 읽힌다.
        if (pictureCount > 1)
            hint += $" This is picture {pictureCount} of this screen since anything was last recorded about it; " +
                    "whatever the earlier ones told you is still only in this conversation and dies with it.";

        return hint;
    }

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

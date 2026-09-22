using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using ModelContextProtocol.Server;
using Xapper.McpServer.Infrastructure;
using Xapper.McpServer.Ipc;
using Xapper.Protocol;
using Xapper.Protocol.Messages.Requests;
using Xapper.Protocol.Messages.Responses;

namespace Xapper.McpServer.Tools;

/// <summary>
/// 한 번 파악한 화면을 다시 파악하지 않게 해 주는 MCP 도구 묶음.
/// 화면에 들어설 때 조회하고, 모르는 화면이면 한 번만 들여다본 뒤 기록하고, 앱이 바뀌어 기록이 쓸모없어지면 비운다.
///
/// 기록할 때 모델에게 받는 것은 이름과 메모뿐이다. 조작할 자리가 어디에 몇 개 있는지는 기계가 정확히 알지만,
/// "이 화면이 무슨 화면인가" 와 "여기엔 이런 함정이 있다" 는 모델만 판단할 수 있기 때문이다.
/// 이렇게 나누면 기록의 품질이 모델의 성실함에 좌우되지 않는다.
/// </summary>
[McpServerToolType]
public sealed class ScreenTools
{
    #region Fields

    /// <summary>프로필을 받지 못했을 때 호출자에게 돌려줄 문장.</summary>
    private const string NoProfile = "Error: the inspector returned no screen profile.";

    private readonly SessionManager _sessionManager;
    private readonly ScreenStore _store;

    /// <summary>
    /// 직전 조회에서 본 화면의 지문. 지금 지문과 견주어 "화면이 바뀌었다" 고 알려 주기 위해서만 쓴다.
    /// 모르는 화면이 왜 모르는 화면인지 — 처음 보는 것인지, 방금 무언가 눌러서 다른 화면으로 넘어간 것인지 —
    /// 를 모델이 구분할 수 있게 해 준다.
    /// </summary>
    private string? _lastSignature;

    #endregion

    #region Constructor

    /// <summary><see cref="ScreenTools"/>의 새 인스턴스를 생성합니다.</summary>
    /// <param name="sessionManager">활성 Inspector 세션을 제공하는 세션 관리자.</param>
    /// <param name="store">화면 기록이 사는 저장소.</param>
    public ScreenTools(SessionManager sessionManager, ScreenStore store)
    {
        _sessionManager = sessionManager;
        _store = store;
    }

    #endregion

    #region MCP Tools

    /// <summary>현재 화면의 지문으로 저장된 기록을 찾습니다.</summary>
    [McpServerTool(Name = "xapper_screen_recall"), Description(
        "Look up what is known about the screen the app is showing right now, keyed by a fingerprint taken from " +
        "the structure of its main window. Call it first on arriving at a screen. A known screen comes back with " +
        "the selectors and anchors to act on, so you need no screenshot at all. An unknown screen comes back with " +
        "what to do instead, and says whether the screen changed since your last recall. Cheap - it reads no " +
        "pixels and hit-tests nothing.")]
    public async Task<string> Recall(CancellationToken ct = default)
    {
        InspectorClient client;
        try
        {
            client = _sessionManager.GetActive();
        }
        catch (InvalidOperationException ex)
        {
            return $"Error: {ex.Message}";
        }

        var (profile, error) = await ProfileAsync(client, includeRegions: false, ct);
        if (profile is null)
            return error ?? NoProfile;

        var changed = _lastSignature is { } previous && previous != profile.Signature;
        _lastSignature = profile.Signature;

        var record = _store.Find(profile.Signature);
        return record is null
            ? ScreenRecallSummary.Unknown(profile.Signature, changed)
            : ScreenRecallSummary.Known(record);
    }

    /// <summary>현재 화면을 이름과 메모와 함께 기록합니다.</summary>
    [McpServerTool(Name = "xapper_screen_learn"), Description(
        "Remember the screen the app is showing right now, so a later visit needs no screenshot. You supply only " +
        "the name and any gotcha; the server works out the regions itself from the live visual tree, storing a " +
        "selector for each one it can address and an anchor with relative coordinates for the rest. Learning the " +
        "same screen again overwrites the previous record. Do this right after you have looked at an unknown " +
        "screen with xapper_screenshot(annotate: true) and understood it.")]
    public async Task<string> Learn(
        [Description("One line saying what this screen is, e.g. '로그인 화면' or 'DFMEA Step 3 기능분석'")] string name,
        [Description("Anything worth remembering that the structure cannot show: a gotcha, a shortcut, where a button leads. Optional")] string? notes = null,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            return "Error: name is required. Give one line saying what this screen is.";

        InspectorClient client;
        try
        {
            client = _sessionManager.GetActive();
        }
        catch (InvalidOperationException ex)
        {
            return $"Error: {ex.Message}";
        }

        var (profile, error) = await ProfileAsync(client, includeRegions: true, ct);
        if (profile is null)
            return error ?? NoProfile;

        var trimmedNotes = notes?.Trim();
        var record = new ScreenRecord
        {
            Signature = profile.Signature,
            App = ActiveAppName(),
            Name = name.Trim(),
            Notes = string.IsNullOrEmpty(trimmedNotes) ? null : trimmedNotes,
            Regions = profile.Regions.Select(ToRegion).ToList()
        };

        _store.Save(record);
        _lastSignature = profile.Signature;

        return $"Learned \"{record.Name}\" for {record.App} (signature {record.Signature}): " +
               $"{record.Regions.Count} region(s) out of {profile.ElementCount} elements. " +
               "Call xapper_screen_recall on the next visit instead of taking a screenshot.";
    }

    /// <summary>한 앱의 화면 기록을 모두 지웁니다.</summary>
    [McpServerTool(Name = "xapper_screen_forget"), Description(
        "Delete every screen record for an app. Records never expire on their own - a rebuilt app gets new " +
        "fingerprints, so stale records stop matching rather than answering wrongly - so this is the deliberate " +
        "way to clear them, for instance after a redesign you want re-learned from scratch. Omit app to clear " +
        "the one you are attached to.")]
    public string Forget(
        [Description("Process name of the app to clear, e.g. 'Xapper.TestApp'. Omit for the attached app")] string? app = null)
    {
        try
        {
            _sessionManager.GetActive();
        }
        catch (InvalidOperationException ex)
        {
            return $"Error: {ex.Message}";
        }

        var target = string.IsNullOrWhiteSpace(app) ? ActiveAppName() : app.Trim();
        var removed = _store.Forget(target);
        _lastSignature = null;

        return removed == 0
            ? $"No screen records were stored for {target}; nothing to forget."
            : $"Forgot {removed} screen record(s) for {target}.";
    }

    #endregion

    #region Private Methods

    /// <summary>
    /// Inspector 에게 현재 화면의 프로필을 물어봅니다.
    /// 영역까지 달라고 하면 요소마다 히트테스트가 돌아 지문만 받을 때보다 비싸므로, 부르는 쪽이 필요할 때만 켠다.
    /// </summary>
    /// <param name="client">활성 세션의 Inspector 연결.</param>
    /// <param name="includeRegions">true 면 영역 목록까지 함께 받는다.</param>
    /// <param name="ct">취소 토큰.</param>
    /// <returns>프로필. 실패했으면 프로필은 null 이고 호출자가 그대로 돌려줄 오류 문장이 담긴다.</returns>
    private static async Task<(ScreenProfileResponse? Profile, string? Error)> ProfileAsync(
        InspectorClient client, bool includeRegions, CancellationToken ct)
    {
        var request = IpcSerializer.CreateRequest(
            "screenProfile", new ScreenProfileRequest { IncludeRegions = includeRegions });
        var response = await client.SendAsync(request, ct);

        if (response.Type == "error")
            return (null, $"Error: {response.Payload}");

        if (response.Payload is not { } payload)
            return (null, NoProfile);

        return (IpcSerializer.DeserializePayload<ScreenProfileResponse>(payload), null);
    }

    /// <summary>
    /// 기록을 걸어 둘 앱 이름. 프로세스 이름을 쓰는 이유는 앱을 다시 띄워도 그대로이기 때문이다 —
    /// PID 는 실행마다 바뀌어 다음 세션에서 같은 앱의 기록을 찾지 못하게 만든다.
    /// 프로세스가 이미 사라져 이름을 읽을 수 없으면 PID 를 대신 쓴다. 기록을 통째로 잃는 것보다는 낫다.
    /// </summary>
    private string ActiveAppName()
    {
        if (_sessionManager.ActiveProcessId is not { } pid)
            return "unknown";

        try
        {
            using var process = Process.GetProcessById(pid);
            return process.ProcessName;
        }
        catch (Exception)
        {
            return pid.ToString(CultureInfo.InvariantCulture);
        }
    }

    /// <summary>
    /// 화면에서 뽑은 영역 하나를 저장할 모양으로 옮깁니다.
    /// 하는 일은 다음 세션에도 살아남는 지목 방법을 고르는 것이다: ref 는 스냅샷마다 다시 매겨져 쓸 수 없으므로,
    /// 가장 안정적인 자동화 ID 를 먼저 쓰고, 없으면 이름, 그것도 없으면 표시 텍스트를 쓴다.
    /// 셋 다 없어 지목할 길이 없는 요소는 이름 있는 조상을 기준점 삼아 그 안에서의 상대 위치로 남긴다.
    /// </summary>
    /// <param name="mark">Inspector 가 골라 준 영역 하나.</param>
    private static ScreenRegion ToRegion(ScreenMark mark)
    {
        var text = string.IsNullOrWhiteSpace(mark.Text) ? null : mark.Text;

        if (!string.IsNullOrWhiteSpace(mark.AutomationId))
            return new ScreenRegion { Type = mark.Type, Selector = $"id={mark.AutomationId}", Text = text };

        if (!string.IsNullOrWhiteSpace(mark.Name))
            return new ScreenRegion { Type = mark.Type, Selector = $"name={mark.Name}", Text = text };

        // 텍스트가 셀렉터가 된 경우에는 텍스트를 따로 담지 않는다. 같은 값이 한 줄에 두 번 나올 뿐이다.
        if (text is not null)
            return new ScreenRegion { Type = mark.Type, Selector = $"text={text}" };

        return new ScreenRegion
        {
            Type = mark.Type,
            Anchor = mark.Anchor,
            AnchorX = mark.AnchorX,
            AnchorY = mark.AnchorY
        };
    }

    #endregion
}

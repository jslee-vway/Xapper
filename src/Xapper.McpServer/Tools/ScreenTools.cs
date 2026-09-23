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
    private readonly ScreenTracker _tracker;

    #endregion

    #region Constructor

    /// <summary><see cref="ScreenTools"/>의 새 인스턴스를 생성합니다.</summary>
    /// <param name="sessionManager">활성 Inspector 세션을 제공하는 세션 관리자.</param>
    /// <param name="store">화면 기록이 사는 저장소.</param>
    /// <param name="tracker">직전에 본 화면을 기억하는 싱글턴. 이 도구는 호출마다 새로 만들어지므로 여기에 둔다.</param>
    public ScreenTools(SessionManager sessionManager, ScreenStore store, ScreenTracker tracker)
    {
        _sessionManager = sessionManager;
        _store = store;
        _tracker = tracker;
    }

    #endregion

    #region MCP Tools

    /// <summary>현재 화면의 지문으로 저장된 기록을 찾습니다.</summary>
    [McpServerTool(Name = "xapper_screen_recall"), Description(
        "Look up what is known about the screen the app is showing right now, keyed by a fingerprint taken from " +
        "the structure of its main window. Call it every time the screen changes, not once at the start. " +
        "A known screen comes back with the selectors and anchors to act on, so you need no screenshot at all. " +
        "Read the answer like this: a line that starts with a selector goes straight into target; a line reading " +
        "\"in X at a,b\" has no selector, so act on it with target=X and x=a, y=b, which are fractions of X " +
        "rather than pixels. The notes are standing facts earlier visits worked out - act on them instead of " +
        "finding out again, and when one proves wrong rewrite it with xapper_screen_note. The learned date and " +
        "seen count tell you how much may have changed since, not whether it is right. Regions are derived from " +
        "the live tree, so a record whose regions have gone stale is refreshed here without being asked - you " +
        "never have to re-learn a screen to fix its selectors, only to change its name. " +
        "An unknown screen comes back with what to do instead, and says whether the screen changed since your " +
        "last recall. Cheap - it reads no pixels and hit-tests nothing.")]
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

        // 지문과 함께 지금 화면의 텍스트까지 한 번에 받는다. 기록은 "무엇을 만질 수 있는가" 만 담고 있어서,
        // "지금 무엇이라고 적혀 있는가" 를 알려면 그림을 찍는 수밖에 없었다(실측: 이미 배운 화면인데도
        // 상황을 파악하려고 창 전체를 찍은 것이 열 장 중 셋이었다). 기준점은 빼고 받으므로 히트테스트가 돌지 않는다.
        var (profile, error) = await ProfileAsync(client, includeRegions: true, ct, addressableOnly: true);
        if (profile is null)
            return error ?? NoProfile;

        var changed = _tracker.LastSignature is { } previous && previous != profile.Signature;
        _tracker.LastSignature = profile.Signature;

        var record = _store.Find(profile.Signature);
        if (record is null)
            return ScreenRecallSummary.Unknown(profile.Signature, changed);

        FillInCurrentText(record, profile);

        // 영역 목록은 기계가 시각 트리에서 뽑는 것이지 모델에게 받는 것이 아니다. 그러니 낡았을 때
        // "다시 배워 달라" 고 부탁할 이유가 없다. 지금 앱이 눈앞에 있으므로 여기서 다시 뽑아 채운다.
        // 이름과 비고는 모델만 지을 수 있는 것이므로 그대로 둔다(실측: 다시 배우라는 안내를 두 세션 연속
        // 무시했고, 낡은 규칙으로 만들어진 기록은 아무도 되살리지 않아 그대로 남아 있었다).
        var refreshed = false;
        if (NeedsFreshRegions(record))
            refreshed = await RefreshRegionsAsync(client, record, ct);

        return ScreenRecallSummary.Known(record, refreshed);
    }

    /// <summary>현재 화면을 이름과 메모와 함께 기록합니다.</summary>
    [McpServerTool(Name = "xapper_screen_learn"), Description(
        "Remember the screen the app is showing right now, so a later visit needs no screenshot. You supply only " +
        "the name and any gotcha; the server works out the regions itself from the live visual tree, storing a " +
        "selector for each one it can address and an anchor with relative coordinates for the rest. Learning the " +
        "same screen again overwrites the previous record. Do this right after you have looked at an unknown " +
        "screen with xapper_screenshot(annotate: true) and understood it.")]
    public async Task<string> Learn(
        [Description("One line saying what this screen is, e.g. '로그인 화면' or '주문 목록 - 검색 조건 펼친 상태'")] string name,
        [Description("What is true about this screen that its structure cannot show: which shortcut does what, what a button needs before it works, a dialog that can appear from here. Write standing facts, not what you just did. Optional")] string? notes = null,
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

        // 비고를 주지 않았다면 이미 적어 둔 것을 그대로 둔다. 덮어쓰면 앞서 알아낸 함정이 소리 없이 사라지고,
        // 다시 배우는 쪽은 그런 것이 있었는지조차 모른다(실측: 이름만 주고 다시 배우자 비고가 비었다).
        var trimmedNotes = notes?.Trim();
        var keptNotes = string.IsNullOrEmpty(trimmedNotes)
            ? _store.Find(profile.Signature)?.Notes
            : trimmedNotes;

        var record = new ScreenRecord
        {
            Signature = profile.Signature,
            App = ActiveAppName(),
            Name = name.Trim(),
            Notes = string.IsNullOrEmpty(keptNotes) ? null : keptNotes,
            Regions = profile.Regions.Where(IsWorthStoring).Select(ToRegion).ToList()
        };

        _store.Save(record);
        _tracker.LastSignature = profile.Signature;
        _tracker.Recorded(profile.Signature);

        return $"Learned \"{record.Name}\" for {record.App} (signature {record.Signature}): " +
               $"{record.Regions.Count} region(s) out of {profile.ElementCount} elements. " +
               "Call xapper_screen_recall on the next visit instead of taking a screenshot.";
    }

    /// <summary>
    /// 저장해 둔 영역의 텍스트를 지금 화면의 것으로 갈아 끼웁니다.
    /// 저장된 텍스트는 배울 때의 것이라 데이터가 바뀌면 거짓이 된다. 셀렉터가 같은 것끼리 맞춰 넣으면
    /// 조회 한 번으로 "무엇을 만질 수 있고 지금 무엇이라고 적혀 있는지" 가 함께 나온다.
    /// 지금 화면에 없는 영역은 텍스트를 비운다. 옛 값을 남겨 두면 없는 것을 있다고 말하는 셈이다.
    /// </summary>
    /// <param name="record">텍스트를 채울 기록. 메모리 안에서만 바꾸며 저장하지 않는다.</param>
    /// <param name="profile">지금 화면의 프로필.</param>
    private static void FillInCurrentText(ScreenRecord record, ScreenProfileResponse profile)
    {
        var live = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var mark in profile.Regions)
        {
            var selector = SelectorFor(mark);
            if (selector is not null)
                live[selector] = string.IsNullOrWhiteSpace(mark.Text) ? null : mark.Text;
        }

        foreach (var region in record.Regions)
        {
            if (region.Selector is null)
                continue;

            region.Text = live.TryGetValue(region.Selector, out var text) ? text : null;
        }
    }

    /// <summary>
    /// 화면에서 뽑은 영역 하나를 가리키는 셀렉터. <see cref="ToRegion"/> 과 같은 순서로 고른다.
    /// 순서가 어긋나면 저장된 줄과 지금 줄이 다른 열쇠를 갖게 되어 짝이 맞지 않는다.
    /// </summary>
    private static string? SelectorFor(ScreenMark mark)
    {
        if (!string.IsNullOrWhiteSpace(mark.AutomationId))
            return $"id={mark.AutomationId}";

        if (!string.IsNullOrWhiteSpace(mark.Name))
            return $"name={mark.Name}";

        return string.IsNullOrWhiteSpace(mark.Text) ? null : $"text={mark.Text}";
    }

    /// <summary>
    /// 저장된 영역을 다시 뽑아야 하는지 판단합니다.
    /// 두 가지를 본다. 셀렉터가 하나도 없으면 다시 방문해도 쓸 데가 없고, 컨트롤 템플릿 부품이 섞여 있으면
    /// 그것을 걸러내기 전의 규칙으로 만들어진 기록이다. 어느 쪽이든 지금 다시 뽑는 편이 정확하다.
    /// </summary>
    /// <param name="record">살펴볼 기록.</param>
    private static bool NeedsFreshRegions(ScreenRecord record)
    {
        if (record.Regions.Count == 0)
            return true;

        if (!record.Regions.Any(region => !string.IsNullOrWhiteSpace(region.Selector)))
            return true;

        return record.Regions.Any(region =>
            region.Selector is { } selector && selector.Contains("=PART_", StringComparison.Ordinal));
    }

    /// <summary>
    /// 지금 화면에서 영역을 다시 뽑아 기록에 채웁니다. 이름과 비고는 건드리지 않습니다.
    /// 뽑는 데 실패하면 기록을 그대로 두고 false 를 돌려준다. 있던 것을 지우는 것보다는 낡은 채로 두는 편이 낫다.
    /// </summary>
    /// <param name="client">활성 세션의 Inspector 연결.</param>
    /// <param name="record">채워 넣을 기록. 성공하면 영역이 새 것으로 바뀐다.</param>
    /// <param name="ct">취소 토큰.</param>
    /// <returns>다시 뽑아 저장했으면 true.</returns>
    private async Task<bool> RefreshRegionsAsync(InspectorClient client, ScreenRecord record, CancellationToken ct)
    {
        var (profile, _) = await ProfileAsync(client, includeRegions: true, ct);
        if (profile is null)
            return false;

        var regions = profile.Regions.Where(IsWorthStoring).Select(ToRegion).ToList();
        if (regions.Count == 0)
            return false;

        record.Regions.Clear();
        record.Regions.AddRange(regions);
        _store.Save(record);
        return true;
    }

    /// <summary>
    /// 이 영역을 기록해 둘 값어치가 있는지 판단합니다.
    /// id·name 으로 잡히는 요소와, 셀렉터가 없어 기준점이 붙은 요소만 남긴다.
    ///
    /// 텍스트로만 잡히는 요소를 빼는 이유는 지문에서 텍스트를 뺀 이유와 같다. 그것은 구조가 아니라 데이터다.
    /// 실측에서 목록의 "row 0"~"row 9" 열 줄이 그대로 기록에 들어갔는데, 그런 셀렉터는 데이터가 바뀌는 순간
    /// 가리키는 것이 없어진다. 게다가 글자가 붙은 버튼은 필요할 때 xapper_find 로 즉시, 정확하게 찾을 수 있어
    /// 미리 적어 둘 이유가 없다. 기록이 값을 하는 것은 다시 찾기 비싼 것, 즉 id·name 과 기준점이다.
    /// </summary>
    private static bool IsWorthStoring(ScreenMark mark)
    {
        // 컨트롤 템플릿이 만든 부품은 앱 작성자가 놓은 것이 아니라 스크롤바나 편집기의 내부 구조다.
        // 실측에서 저장된 여덟 개 중 다섯 개가 그런 것들(PART_Track, splitBorder 등)이라 기록이 거의 쓸모없었다.
        // 그림에는 그려 두되(가려진 것을 누르지 않게 하려면 필요하다) 기록에서는 뺀다.
        if (mark.FromTemplate)
            return false;

        return !string.IsNullOrEmpty(mark.AutomationId)
            || !string.IsNullOrEmpty(mark.Name)
            || !string.IsNullOrEmpty(mark.Anchor);
    }

    /// <summary>지금 화면의 기록에 비고를 덧붙이거나, 있던 비고를 갈아 끼웁니다.</summary>
    [McpServerTool(Name = "xapper_screen_note"), Description(
        "Write down how this screen behaves, so the next visit can act on it without finding out again. " +
        "Note a standing fact about the screen, not a report of what you did: 'Ctrl+Z undoes the last edit here', " +
        "'the Add button does nothing unless the search box has text', 'opening a project from here can raise a " +
        "backup-recovery dialog that the visual tree does not show', 'the X icon in the tree deletes and asks to " +
        "confirm'. A line that begins with a date or with what you verified is worth nothing later; a line the " +
        "next agent can act on without checking is worth the call. " +
        "The note lands on the screen showing right now, so write it the moment you find the thing out, before " +
        "you navigate away and before you move on to the next step. Held back until the end of the session, it " +
        "is lost if the session ends first. Lines are " +
        "appended and the regions are untouched; pass replace to rewrite the notes when one has gone stale. " +
        "The screen must have been learned first.")]
    public async Task<string> Note(
        [Description("What is true about this screen, one fact per line, e.g. 'Ctrl+Z 는 여기서 마지막 편집을 되돌린다'")] string notes,
        [Description("Replace the existing notes instead of appending. Use it when a note has become wrong, and include everything still true")] bool replace = false,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(notes))
            return "Error: notes is required. Say what is true about this screen.";

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

        var written = replace
            ? _store.ReplaceNotes(profile.Signature, notes.Trim())
            : _store.AppendNote(profile.Signature, notes.Trim());

        if (!written)
            return $"No screen record exists for the screen you are on (signature {profile.Signature}), so there " +
                   "is nothing to note against. Call xapper_screen_learn first - you can pass the same lines " +
                   "as its notes.";

        _tracker.LastSignature = profile.Signature;
        _tracker.Recorded(profile.Signature);
        return $"{(replace ? "Rewrote the notes" : "Noted")} on the record for signature {profile.Signature}. " +
               "xapper_screen_recall will show it on the next visit.";
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
        _tracker.LastSignature = null;

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
        InspectorClient client, bool includeRegions, CancellationToken ct, bool addressableOnly = false)
    {
        var request = IpcSerializer.CreateRequest(
            "screenProfile",
            new ScreenProfileRequest { IncludeRegions = includeRegions, AddressableOnly = addressableOnly });
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

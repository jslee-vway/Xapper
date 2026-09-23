using Xapper.McpServer.Infrastructure;

namespace Xapper.Tests;

/// <summary>
/// 화면 기록 저장소의 약속을 고정한다. 같은 지문이면 덮어쓰고, 조회할 때마다 쓰인 흔적이 쌓이며,
/// 날짜로 지우지는 않되 앱 단위로 비울 수 있고 상한을 넘으면 가장 오래 안 쓰인 것이 밀려난다.
/// </summary>
public class ScreenStoreTests : IDisposable
{
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"xapper-screens-{Guid.NewGuid():N}.db");

    public void Dispose()
    {
        try { File.Delete(_path); } catch (IOException) { }
    }

    private ScreenStore NewStore() => new(_path);

    /// <summary>
    /// 시험용 기록 하나. 영역은 <paramref name="screen"/> 을 따라 달라진다.
    /// 저장소가 같은 영역 집합을 같은 화면으로 보기 때문에, 서로 다른 화면을 뜻하려면 영역도 달라야 한다.
    /// </summary>
    private static ScreenRecord Record(
        string signature, string app = "DemoApp", string name = "화면", string? screen = null) => new()
    {
        Signature = signature,
        App = app,
        Name = name,
        Notes = "메모",
        Regions =
        [
            new ScreenRegion { Type = "Button", Selector = $"id=Save_{screen ?? signature}" },
            new ScreenRegion { Type = "Grid", Anchor = "id=Panel", AnchorX = 0.5, AnchorY = 0.2 }
        ]
    };

    [Fact]
    public void AppendNote_AddsToWhatWasAlreadyThere()
    {
        using var store = NewStore();
        store.Save(Record("aaa"));

        Assert.True(store.AppendNote("aaa", "Undo 버튼은 변경 이력이 있어야 활성화된다"));

        var found = store.Find("aaa");
        Assert.NotNull(found);
        Assert.Contains("메모", found.Notes);
        Assert.Contains("Undo 버튼은 변경 이력이 있어야 활성화된다", found.Notes);
    }

    [Fact]
    public void AppendNote_FillsTheNotes_WhenThereWereNone()
    {
        using var store = NewStore();
        var record = Record("aaa");
        record.Notes = null;
        store.Save(record);

        store.AppendNote("aaa", "첫 줄");

        var found = store.Find("aaa");
        Assert.NotNull(found);
        Assert.Equal("첫 줄", found.Notes);
    }

    [Fact]
    public void Opening_MergesRecordsThatAlreadySharedTheirRegions()
    {
        // 열쇠를 더하는 것만으로는 이미 갈라진 기록이 붙지 않는다. 앞으로만 막고 지난 것을 두면 알아낸 사실이
        // 쪼개진 채로 남아, 어느 쪽으로 들어오느냐에 따라 절반만 읽힌다.
        using (var before = NewStore())
        {
            before.Save(Record("지문하나", name: "선택 전", screen: "같은화면"));
            before.Save(Record("지문둘", name: "선택 후", screen: "같은화면"));
        }

        // 두 줄이 되도록 열쇠를 지우고, 다시 열어 합쳐지는지 본다.
        using (var raw = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={_path}"))
        {
            raw.Open();
            using var command = raw.CreateCommand();
            command.CommandText = @"UPDATE screens SET region_key = NULL;
                INSERT INTO screens (signature, app, name, notes, learned_at, last_seen_at, seen_count)
                VALUES ('지문둘', 'DemoApp', '선택 후', '팝업은 스냅샷으로 읽힌다', '2026-01-01', '2026-01-01', 1);
                INSERT INTO regions (signature, ordinal, type, selector)
                SELECT '지문둘', ordinal, type, selector FROM regions WHERE signature = '지문하나';";
            command.ExecuteNonQuery();
        }

        using var store = NewStore();

        var kept = store.Find("지문하나") ?? store.Find("지문둘");
        Assert.NotNull(kept);
        Assert.Contains("팝업은 스냅샷으로 읽힌다", kept.Notes);
        Assert.True(store.Find("지문하나") is null || store.Find("지문둘") is null);
    }

    [Fact]
    public void Save_JoinsARecordThatAlreadyHasTheSameRegions()
    {
        // 같은 화면에서 행을 고르거나 목록을 펼치면 인라인 편집기 같은 요소가 트리에 나타나 지문이 갈린다
        // (실측: 최적화 화면 하나가 지문 셋으로 갈렸고 셋의 영역이 완전히 같았다). 그러면 알아낸 것이 쪼개진다.
        using var store = NewStore();
        store.Save(Record("첫번째지문", screen: "같은화면"));

        var storedUnder = store.Save(Record("다른지문", screen: "같은화면"));

        Assert.Equal("첫번째지문", storedUnder);
        Assert.Null(store.Find("다른지문"));
        Assert.NotNull(store.Find("첫번째지문"));
    }

    [Fact]
    public void FindByRegionKey_ReachesTheRecordWhenTheFingerprintMisses()
    {
        using var store = NewStore();
        store.Save(Record("aaa"));

        var key = ScreenStore.RegionKeyOf(Record("aaa").Regions);
        Assert.NotNull(key);

        var found = store.FindByRegionKey(key);
        Assert.NotNull(found);
        Assert.Equal("aaa", found.Signature);
    }

    [Fact]
    public void RegionKeyOf_IgnoresOrderAndRegionsNoSelectorCanReach()
    {
        // 순서는 훑는 방향에 따라 달라질 수 있고, 기준점 영역의 좌표는 창 크기에 흔들린다.
        var one = ScreenStore.RegionKeyOf([
            new ScreenRegion { Type = "Button", Selector = "id=Save" },
            new ScreenRegion { Type = "Grid", Selector = "name=List" },
            new ScreenRegion { Type = "Cell", Anchor = "id=Panel", AnchorX = 0.5, AnchorY = 0.2 }
        ]);
        var other = ScreenStore.RegionKeyOf([
            new ScreenRegion { Type = "Grid", Selector = "name=List" },
            new ScreenRegion { Type = "Button", Selector = "id=Save" }
        ]);

        Assert.Equal(one, other);
    }

    [Fact]
    public void RegionKeyOf_IsNull_WhenNoSelectorIsThere()
    {
        Assert.Null(ScreenStore.RegionKeyOf([
            new ScreenRegion { Type = "Cell", Anchor = "id=Panel", AnchorX = 0.5, AnchorY = 0.2 }
        ]));
    }

    [Fact]
    public void ReplaceNotes_SwapsTheWholeText()
    {
        // 덧붙이기만 있으면 틀린 줄을 바로잡을 길이 없어, 한 번 잘못 적힌 사실이 계속 읽힌다.
        using var store = NewStore();
        store.Save(Record("aaa"));
        store.AppendNote("aaa", "Undo 버튼은 늘 활성 상태다");

        Assert.True(store.ReplaceNotes("aaa", "Undo 버튼은 변경 이력이 있어야 활성화된다"));

        var found = store.Find("aaa");
        Assert.NotNull(found);
        Assert.Equal("Undo 버튼은 변경 이력이 있어야 활성화된다", found.Notes);
        Assert.DoesNotContain("늘 활성 상태", found.Notes);
    }

    [Fact]
    public void ReplaceNotes_SaysSo_WhenNoRecordMatches()
    {
        using var store = NewStore();

        Assert.False(store.ReplaceNotes("없는지문", "아무 말"));
    }

    [Fact]
    public void AppendNote_SaysSo_WhenNoRecordMatches()
    {
        using var store = NewStore();

        Assert.False(store.AppendNote("없는지문", "아무 말"));
    }

    [Fact]
    public void AppendNote_LeavesTheRegionsAlone()
    {
        // 비고를 남기려고 다시 배우게 하면 영역까지 다시 뽑히고, 그 사이 화면이 조금만 달라져도
        // 기록이 통째로 바뀐다. 이 길은 비고만 건드린다.
        using var store = NewStore();
        store.Save(Record("aaa"));

        store.AppendNote("aaa", "덧붙인 줄");

        var found = store.Find("aaa");
        Assert.NotNull(found);
        Assert.Equal(2, found.Regions.Count);
        Assert.Equal("id=Save_aaa", found.Regions[0].Selector);
    }

    [Fact]
    public void Find_AfterSave_ReturnsWhatWasStored()
    {
        using var store = NewStore();
        store.Save(Record("aaa"));

        var found = store.Find("aaa");

        Assert.NotNull(found);
        Assert.Equal("화면", found.Name);
        Assert.Equal(2, found.Regions.Count);
        Assert.Equal("id=Save_aaa", found.Regions[0].Selector);
        Assert.Equal(0.5, found.Regions[1].AnchorX);
    }

    [Fact]
    public void Find_OfAnUnknownSignature_IsNull()
    {
        using var store = NewStore();

        Assert.Null(store.Find("nothing"));
    }

    [Fact]
    public void Save_WithTheSameSignature_ReplacesInsteadOfPilingUp()
    {
        using var store = NewStore();
        store.Save(Record("aaa", name: "처음"));

        var second = Record("aaa", name: "나중");
        second.Regions.RemoveAt(1);
        store.Save(second);

        var found = store.Find("aaa");
        Assert.NotNull(found);
        Assert.Equal("나중", found.Name);
        Assert.Single(found.Regions);
    }

    [Fact]
    public void Find_CountsEachUse()
    {
        using var store = NewStore();
        store.Save(Record("aaa"));

        store.Find("aaa");
        store.Find("aaa");
        var found = store.Find("aaa");

        Assert.NotNull(found);
        Assert.True(found.SeenCount >= 3, $"seen {found.SeenCount} times");
    }

    [Fact]
    public void Forget_ClearsOneAppAndLeavesTheOthers()
    {
        using var store = NewStore();
        store.Save(Record("aaa", app: "Gone"));
        store.Save(Record("bbb", app: "Kept"));

        var removed = store.Forget("Gone");

        Assert.Equal(1, removed);
        Assert.Null(store.Find("aaa"));
        Assert.NotNull(store.Find("bbb"));
    }

    [Fact]
    public void Save_BeyondTheCap_DropsTheLeastRecentlySeen()
    {
        using var store = new ScreenStore(_path, maxPerApp: 2);
        store.Save(Record("first"));
        store.Save(Record("second"));
        store.Find("first");                       // first 를 최근에 쓴 것으로 만든다
        store.Save(Record("third"));

        Assert.NotNull(store.Find("first"));
        Assert.NotNull(store.Find("third"));
        Assert.Null(store.Find("second"));
    }
}

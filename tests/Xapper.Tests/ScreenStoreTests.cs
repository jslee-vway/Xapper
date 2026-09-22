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

    private static ScreenRecord Record(string signature, string app = "DemoApp", string name = "화면") => new()
    {
        Signature = signature,
        App = app,
        Name = name,
        Notes = "메모",
        Regions =
        [
            new ScreenRegion { Type = "Button", Selector = "id=Save" },
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
        Assert.Equal("id=Save", found.Regions[0].Selector);
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
        Assert.Equal("id=Save", found.Regions[0].Selector);
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

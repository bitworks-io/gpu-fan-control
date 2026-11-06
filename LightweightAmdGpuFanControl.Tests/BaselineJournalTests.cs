using LightweightAmdGpuFanControl.Recovery;
using Xunit;

public class BaselineJournalTests
{
    private static readonly DateTime T = new(2026, 11, 6, 18, 0, 0, DateTimeKind.Utc);
    private static GpuIdentity Id(string name = "GPU A", int uid = 1, int states = 5) => new(name, uid, states);
    private static FanBaseline Base(params int[] speeds) => new(speeds, true, false);
    private static FanBaseline Captured() => new(new[] { 99, 98, 97, 96, 95 }, true, true);

    private static BaselineEntry Dirty(GpuIdentity id, int[]? speeds = null, bool zeroSupported = true)
    {
        var j = new BaselineJournal();
        j.MarkDirty(id, new FanBaseline(speeds ?? new[] { 20, 30, 40, 50, 60 }, zeroSupported, true), T);
        return j.Find(id)!;
    }

    [Fact]
    public void Choose_NoEntry_Captured()
    {
        var c = BaselinePlanner.Choose(Id(), Captured(), null);
        Assert.Equal(BaselineSource.Captured, c.Source);
        Assert.False(string.IsNullOrWhiteSpace(c.Reason));
        Assert.Equal(new[] { 99, 98, 97, 96, 95 }, c.Baseline.Speeds);
    }

    [Fact]
    public void Choose_CleanEntry_Captured()
    {
        var e = Dirty(Id());
        e.Dirty = false;
        var c = BaselinePlanner.Choose(Id(), Captured(), e);
        Assert.Equal(BaselineSource.Captured, c.Source);
        Assert.False(string.IsNullOrWhiteSpace(c.Reason));
    }

    [Fact]
    public void Choose_DirtyValid_Journal()
    {
        var e = Dirty(Id());
        var c = BaselinePlanner.Choose(Id(), Captured(), e);
        Assert.Equal(BaselineSource.Journal, c.Source);
        Assert.Equal(new[] { 20, 30, 40, 50, 60 }, c.Baseline.Speeds);
        Assert.True(c.Baseline.ZeroRpmSupported);
        Assert.True(c.Baseline.ZeroRpm);
        Assert.False(string.IsNullOrWhiteSpace(c.Reason));
    }

    [Fact]
    public void Choose_KeyMismatch_Captured()
    {
        var e = Dirty(Id(uid: 2));
        Assert.Equal(BaselineSource.Captured, BaselinePlanner.Choose(Id(uid: 1), Captured(), e).Source);
    }

    [Fact]
    public void Choose_SpeedOutOfRange_Captured()
    {
        Assert.Equal(BaselineSource.Captured, BaselinePlanner.Choose(Id(), Captured(), Dirty(Id(), new[] { 20, 30, 40, 50, 101 })).Source);
        Assert.Equal(BaselineSource.Captured, BaselinePlanner.Choose(Id(), Captured(), Dirty(Id(), new[] { -1, 30, 40, 50, 60 })).Source);
    }

    [Fact]
    public void Choose_CountMismatch_Captured()
    {
        Assert.Equal(BaselineSource.Captured, BaselinePlanner.Choose(Id(), Captured(), Dirty(Id(), new[] { 20, 30, 40 })).Source);
        var e = Dirty(Id());
        e.Speeds = null;
        Assert.Equal(BaselineSource.Captured, BaselinePlanner.Choose(Id(), Captured(), e).Source);
    }

    [Fact]
    public void Choose_ZeroRpmCapabilityMismatch_Captured()
    {
        var e = Dirty(Id(), zeroSupported: false); // journal says unsupported, captured says supported
        Assert.Equal(BaselineSource.Captured, BaselinePlanner.Choose(Id(), Captured(), e).Source);
    }

    [Fact]
    public void Choose_Journal_DoesNotAliasEntrySpeeds()
    {
        var e = Dirty(Id());
        var c = BaselinePlanner.Choose(Id(), Captured(), e);
        e.Speeds![0] = 0;
        Assert.Equal(20, c.Baseline.Speeds[0]);
    }

    [Fact]
    public void Journal_MarkDirtyThenFind()
    {
        var j = new BaselineJournal();
        j.MarkDirty(Id(), Base(1, 2, 3, 4, 5), T);
        var e = j.Find(Id())!;
        Assert.True(e.Dirty);
        Assert.True(j.HasDirty);
        Assert.Equal("GPU A|1", e.Key);
        Assert.Equal("GPU A", e.Name);
        Assert.Equal(1, e.UniqueId);
        Assert.Equal(5, e.StateCount);
        Assert.Equal(new[] { 1, 2, 3, 4, 5 }, e.Speeds);
        Assert.True(e.ZeroRpmSupported);
        Assert.Equal("2026-11-06T18:00:00Z", e.Utc);
        // upsert, not duplicate
        j.MarkDirty(Id(), Base(9, 9, 9, 9, 9), T.AddMinutes(1));
        Assert.Single(j.Entries);
        Assert.Equal(9, j.Find(Id())!.Speeds![0]);
    }

    [Fact]
    public void Journal_MarkCleanKeepsEntryNotDirty()
    {
        var j = new BaselineJournal();
        j.MarkDirty(Id(), Base(1, 2, 3, 4, 5), T);
        Assert.True(j.MarkClean(Id()));
        Assert.False(j.Find(Id())!.Dirty);
        Assert.False(j.HasDirty);
        Assert.False(j.MarkClean(Id("other")));
    }

    [Fact]
    public void Journal_SameGpuNewStateCount_ReplacesEntry()
    {
        var j = new BaselineJournal();
        j.MarkDirty(Id(states: 5), Base(1, 2, 3, 4, 5), T);
        j.MarkDirty(Id(states: 3), Base(7, 8, 9), T);
        Assert.Single(j.Entries);
        var e = j.Find(Id(states: 3))!;
        Assert.Equal(3, e.StateCount);
        Assert.Equal(new[] { 7, 8, 9 }, e.Speeds);
        Assert.Same(e, j.Find(Id(states: 5)));
    }

    [Fact]
    public void Choose_EntryStateCountMismatch_Captured()
    {
        var e = Dirty(Id(states: 5));
        var c = BaselinePlanner.Choose(Id(states: 3), new FanBaseline(new[] { 1, 2, 3 }, true, false), e);
        Assert.Equal(BaselineSource.Captured, c.Source);
        // matching speeds length but stale StateCount on the entry is also refused
        e.StateCount = 4;
        Assert.Equal(BaselineSource.Captured, BaselinePlanner.Choose(Id(states: 5), Captured(), e).Source);
    }

    [Fact]
    public void Journal_SameNameDifferentUniqueId_SeparateEntries()
    {
        var j = new BaselineJournal();
        j.MarkDirty(Id(uid: 1), Base(1, 2, 3, 4, 5), T);
        j.MarkDirty(Id(uid: 2), Base(6, 7, 8, 9, 10), T);
        Assert.Equal(2, j.Entries.Count);
        Assert.Equal(1, j.Find(Id(uid: 1))!.Speeds![0]);
        Assert.Equal(6, j.Find(Id(uid: 2))!.Speeds![0]);
    }

    [Fact]
    public void Journal_PruneAbsent_RemovesOnlyAbsent()
    {
        var j = new BaselineJournal();
        j.MarkDirty(Id(uid: 1), Base(1, 2, 3, 4, 5), T);
        j.MarkDirty(Id(uid: 2), Base(1, 2, 3, 4, 5), T);
        j.MarkDirty(Id(uid: 3), Base(1, 2, 3, 4, 5), T);
        j.MarkClean(Id(uid: 3));
        var removed = j.PruneAbsent(new[] { Id(uid: 1) });
        Assert.Equal(2, removed.Count);
        Assert.Single(j.Entries);
        Assert.NotNull(j.Find(Id(uid: 1)));
    }

    [Fact]
    public void Journal_RoundTrip()
    {
        var j = new BaselineJournal();
        j.MarkDirty(Id(), new FanBaseline(new[] { 20, 30, 40, 50, 60 }, true, true), T);
        var json = j.ToJson();
        Assert.Contains("\"schema\"", json);
        Assert.Contains("\"entries\"", json);
        Assert.Contains("\"zeroRpmSupported\"", json);
        var back = BaselineJournal.Parse(json, out var err);
        Assert.Null(err);
        var e = back.Find(Id())!;
        Assert.True(e.Dirty);
        Assert.Equal(new[] { 20, 30, 40, 50, 60 }, e.Speeds);
        Assert.True(e.ZeroRpm);
        Assert.Equal("2026-11-06T18:00:00Z", e.Utc);
        Assert.True(back.HasDirty);
    }

    [Fact]
    public void Parse_Null_Empty()
    {
        foreach (var s in new string?[] { null, "", "  \n" })
        {
            var j = BaselineJournal.Parse(s, out var err);
            Assert.Null(err);
            Assert.Empty(j.Entries);
            Assert.False(j.HasDirty);
        }
    }

    [Fact]
    public void Parse_Truncated_EmptyWithError()
    {
        var j = BaselineJournal.Parse("{\"schema\":1,\"entries\":[{\"key\":", out var err);
        Assert.Empty(j.Entries);
        Assert.False(string.IsNullOrWhiteSpace(err));
    }

    [Fact]
    public void Parse_WrongSchema_EmptyWithError()
    {
        var j = BaselineJournal.Parse("{\"schema\":2,\"entries\":[{\"key\":\"a\",\"dirty\":true}]}", out var err);
        Assert.Empty(j.Entries);
        Assert.False(string.IsNullOrWhiteSpace(err));
    }

    [Fact]
    public void Parse_NullEntries_EmptyNoError()
    {
        var j = BaselineJournal.Parse("{\"schema\":1,\"entries\":null}", out var err);
        Assert.Null(err);
        Assert.NotNull(j.Entries);
        Assert.Empty(j.Entries);
    }

    [Fact]
    public void Parse_WrongShape_EmptyWithError()
    {
        foreach (var s in new[] { "[]", "null", "42", "{\"schema\":1,\"entries\":{}}" })
        {
            var j = BaselineJournal.Parse(s, out var err);
            Assert.Empty(j.Entries);
            Assert.False(string.IsNullOrWhiteSpace(err), s);
        }
    }

    [Fact]
    public void ExitCode_RestoredZero_FailedOne_NothingThree()
    {
        Assert.Equal(0, RestoreAutoResult.ExitCode(1, 0));
        Assert.Equal(1, RestoreAutoResult.ExitCode(1, 1));
        Assert.Equal(1, RestoreAutoResult.ExitCode(0, 2));
        Assert.Equal(3, RestoreAutoResult.ExitCode(0, 0));
    }
}

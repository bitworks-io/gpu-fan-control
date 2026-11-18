using LightweightAmdGpuFanControl.Recovery;
using Xunit;

public class BaselineJournalTests
{
    private static readonly DateTime T = new(2026, 11, 6, 18, 0, 0, DateTimeKind.Utc);
    private static GpuIdentity Id(string name = "GPU A", int uid = 1, int states = 5) => new(name, uid, states);
    private static FanBaseline Base(params int[] speeds) => new(speeds, true, false);
    // App-like captured state: one flat duty, zero-RPM supported but off.
    private static FanBaseline Captured() => new(new[] { 60, 60, 60, 60, 60 }, true, false);

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
        Assert.Equal(new[] { 60, 60, 60, 60, 60 }, c.Baseline.Speeds);
        Assert.Equal(BaselineChoiceKind.NoEntry, c.Kind);
    }

    [Fact]
    public void Choose_CleanEntry_Captured()
    {
        var e = Dirty(Id());
        e.Dirty = false;
        var c = BaselinePlanner.Choose(Id(), Captured(), e);
        Assert.Equal(BaselineSource.Captured, c.Source);
        Assert.Equal(BaselineChoiceKind.Clean, c.Kind);
        Assert.False(string.IsNullOrWhiteSpace(c.Reason));
    }

    [Fact]
    public void Choose_DirtyValid_Journal()
    {
        var e = Dirty(Id());
        var c = BaselinePlanner.Choose(Id(), Captured(), e);
        Assert.Equal(BaselineSource.Journal, c.Source);
        Assert.Equal(BaselineChoiceKind.Journal, c.Kind);
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
    public void Choose_CapturedNotFlat_Stale()
    {
        var c = BaselinePlanner.Choose(Id(), new FanBaseline(new[] { 25, 35, 50, 70, 90 }, true, false), Dirty(Id()));
        Assert.Equal(BaselineChoiceKind.Stale, c.Kind);
        Assert.Equal(BaselineSource.Captured, c.Source);
        Assert.False(string.IsNullOrWhiteSpace(c.Reason));
    }

    [Fact]
    public void Choose_CapturedZeroRpmOn_Stale()
    {
        var c = BaselinePlanner.Choose(Id(), new FanBaseline(new[] { 60, 60, 60, 60, 60 }, true, true), Dirty(Id()));
        Assert.Equal(BaselineChoiceKind.Stale, c.Kind);
        Assert.Equal(BaselineSource.Captured, c.Source);
    }

    [Fact]
    public void Choose_CapturedAboveCeiling_Stale()
    {
        var c = BaselinePlanner.Choose(Id(), new FanBaseline(new[] { 100, 100, 100, 100, 100 }, true, false), Dirty(Id()));
        Assert.Equal(BaselineChoiceKind.Stale, c.Kind);
        Assert.Equal(BaselineSource.Captured, c.Source);
    }

    [Fact]
    public void Choose_CapturedEqualsEntry_AlreadyRestored()
    {
        var e = Dirty(Id(), new[] { 20, 30, 40, 50, 60 });
        var captured = new FanBaseline(new[] { 20, 30, 40, 50, 60 }, true, true);
        var c = BaselinePlanner.Choose(Id(), captured, e);
        Assert.Equal(BaselineChoiceKind.AlreadyRestored, c.Kind);
        Assert.Equal(BaselineSource.Captured, c.Source);
        Assert.Contains("already equals the journaled pre-app state", c.Reason);
    }

    [Fact]
    public void Choose_FlatWithinTolerance_Journal()
    {
        var c = BaselinePlanner.Choose(Id(), new FanBaseline(new[] { 60, 60, 61, 60, 60 }, true, false), Dirty(Id()));
        Assert.Equal(BaselineChoiceKind.Journal, c.Kind);
        Assert.Equal(BaselineSource.Journal, c.Source);
    }

    [Fact]
    public void Choose_InvalidEntry_KindInvalid()
    {
        var c = BaselinePlanner.Choose(Id(), Captured(), Dirty(Id(), new[] { 20, 30, 40, 50, 101 }));
        Assert.Equal(BaselineChoiceKind.Invalid, c.Kind);
        Assert.Equal(BaselineSource.Captured, c.Source);
        Assert.Equal(BaselineChoiceKind.Invalid, BaselinePlanner.Choose(Id(uid: 1), Captured(), Dirty(Id(uid: 2))).Kind);
        Assert.Equal(BaselineChoiceKind.Invalid, BaselinePlanner.Choose(Id(), Captured(), Dirty(Id(), zeroSupported: false)).Kind);
    }

    [Fact]
    public void DecisionLine_AppWritten_ShowsValues()
    {
        var captured = new FanBaseline(new[] { 60, 60, 60, 60, 60 }, true, false);
        var c = BaselinePlanner.Choose(Id(), captured, Dirty(Id()));
        var line = c.DecisionLine("GPU A");
        Assert.StartsWith("journal: 'GPU A' dirty entry -> app-written (", line);
        Assert.Contains("current=[60,60,60,60,60] zeroRpm=off;", line);
        Assert.Contains("journal=[20,30,40,50,60] zeroRpm=on;", line);
        Assert.Contains(c.Reason, line);
        Assert.EndsWith(")", line);
    }

    [Fact]
    public void DecisionLine_Stale_ShowsValues()
    {
        var captured = new FanBaseline(new[] { 25, 35, 50, 70, 90 }, false, false);
        var c = BaselinePlanner.Choose(Id(), captured, Dirty(Id(), zeroSupported: false));
        var line = c.DecisionLine("GPU A");
        Assert.StartsWith("journal: 'GPU A' dirty entry -> stale (", line);
        Assert.Contains("current=[25,35,50,70,90] zeroRpm=n/a;", line);
        Assert.Contains("journal=[20,30,40,50,60] zeroRpm=n/a;", line);
        Assert.Contains("stale", line);
    }

    [Fact]
    public void DecisionLine_AlreadyRestoredAndInvalid_Decisions()
    {
        var e = Dirty(Id(), new[] { 20, 30, 40, 50, 60 });
        var same = new FanBaseline(new[] { 20, 30, 40, 50, 60 }, true, true);
        Assert.Contains("-> already-restored (", BaselinePlanner.Choose(Id(), same, e).DecisionLine("GPU A"));
        var bad = Dirty(Id(), new[] { 20, 30, 40, 50, 101 });
        Assert.Contains("-> invalid (", BaselinePlanner.Choose(Id(), Captured(), bad).DecisionLine("GPU A"));
        var nullSpeeds = Dirty(Id());
        nullSpeeds.Speeds = null;
        Assert.Contains("journal=[]", BaselinePlanner.Choose(Id(), Captured(), nullSpeeds).DecisionLine("GPU A"));
    }

    [Fact]
    public void LooksAppWritten_Cases()
    {
        Assert.True(BaselinePlanner.LooksAppWritten(new FanBaseline(new[] { 20, 20, 20, 20, 20 }, true, false)));
        Assert.True(BaselinePlanner.LooksAppWritten(new FanBaseline(new[] { 85, 86, 85 }, false, false)));
        Assert.True(BaselinePlanner.LooksAppWritten(new FanBaseline(new[] { 19, 20 }, true, false)));
        Assert.True(BaselinePlanner.LooksAppWritten(new FanBaseline(new[] { 60, 60 }, false, true))); // zero-RPM flag ignored when unsupported
        Assert.False(BaselinePlanner.LooksAppWritten(new FanBaseline(Array.Empty<int>(), true, false)));
        Assert.False(BaselinePlanner.LooksAppWritten(new FanBaseline(new[] { 60, 62 }, true, false)));
        Assert.False(BaselinePlanner.LooksAppWritten(new FanBaseline(new[] { 18, 18 }, true, false)));
        Assert.False(BaselinePlanner.LooksAppWritten(new FanBaseline(new[] { 87, 87 }, true, false)));
        Assert.False(BaselinePlanner.LooksAppWritten(new FanBaseline(new[] { 60, 60 }, true, true)));
    }

    [Fact]
    public void Prune_EmptyPresent_RemovesNothing()
    {
        var j = new BaselineJournal();
        j.MarkDirty(Id(uid: 1), Base(1, 2, 3, 4, 5), T);
        j.MarkDirty(Id(uid: 2), Base(1, 2, 3, 4, 5), T);
        j.MarkClean(Id(uid: 2));
        Assert.Empty(j.PruneAbsent(Array.Empty<GpuIdentity>()));
        Assert.Empty(j.PruneAbsent(Array.Empty<GpuIdentity>(), includeDirty: false));
        Assert.Equal(2, j.Entries.Count);
    }

    [Fact]
    public void Prune_ExcludeDirty_KeepsAbsentDirty()
    {
        var j = new BaselineJournal();
        j.MarkDirty(Id(uid: 1), Base(1, 2, 3, 4, 5), T);
        j.MarkDirty(Id(uid: 2), Base(1, 2, 3, 4, 5), T);
        j.MarkDirty(Id(uid: 3), Base(1, 2, 3, 4, 5), T);
        j.MarkClean(Id(uid: 3));
        var removed = j.PruneAbsent(new[] { Id(uid: 1) }, includeDirty: false);
        Assert.Single(removed);
        Assert.Equal("GPU A|3", removed[0].Key);
        Assert.NotNull(j.Find(Id(uid: 1)));
        Assert.NotNull(j.Find(Id(uid: 2))); // absent but dirty: kept
        Assert.Null(j.Find(Id(uid: 3)));
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

    // The fan state rc.2's points-only restore leaves behind: the captured curve, applied as a manual curve.
    private static readonly int[] CapturedCurve = { 15, 32, 49, 66, 100 };

    private static BaselineJournal DirtyJournal(bool? preAppAtFactory, int[]? speeds = null)
    {
        var j = new BaselineJournal();
        j.MarkDirty(Id(), new FanBaseline(speeds ?? CapturedCurve, true, false, preAppAtFactory), T);
        return j;
    }

    [Fact]
    public void PreAppAtFactory_RoundTrips_AndLegacyParsesNull()
    {
        foreach (bool? value in new bool?[] { true, false, null })
        {
            var back = BaselineJournal.Parse(DirtyJournal(value).ToJson(), out var err);
            Assert.Null(err);
            Assert.Equal(value, back.Find(Id())!.PreAppAtFactory);
        }
        Assert.Contains("\"preAppAtFactory\": true", DirtyJournal(true).ToJson());

        const string legacy = "{\"schema\":1,\"entries\":[{\"key\":\"GPU A|1\",\"name\":\"GPU A\",\"uniqueId\":1,\"stateCount\":5," +
            "\"speeds\":[15,32,49,66,100],\"zeroRpmSupported\":true,\"zeroRpm\":false,\"dirty\":true,\"utc\":\"2026-11-06T18:00:00Z\"}]}";
        var parsed = BaselineJournal.Parse(legacy, out var legacyErr);
        Assert.Null(legacyErr);
        Assert.Null(parsed.Find(Id())!.PreAppAtFactory);
    }

    [Fact]
    public void RecoveredDirtyEntry_KeepsOriginalPreAppAtFactory()
    {
        // Engage with a factory pre-app state, force-kill (entry stays dirty), restart: the fan is still
        // manual, so IsAtFactory now reads false. The plan must still be a factory reset.
        var entry = DirtyJournal(true).Find(Id())!;
        var capturedNow = new FanBaseline(new[] { 60, 60, 60, 60, 60 }, true, false, false);
        var c = BaselinePlanner.Choose(Id(), capturedNow, entry);
        Assert.Equal(BaselineChoiceKind.Journal, c.Kind);
        Assert.Equal(true, c.Baseline.PreAppAtFactory);
        Assert.Equal(ReleaseAction.FactoryReset, ReleasePlanner.Plan(c.Baseline.PreAppAtFactory).Action);
    }

    [Fact]
    public void RecoveredDirtyEntry_UserManualCurve_ReappliesPoints()
    {
        var entry = DirtyJournal(false).Find(Id())!;
        var capturedNow = new FanBaseline(new[] { 60, 60, 60, 60, 60 }, true, false, false);
        var c = BaselinePlanner.Choose(Id(), capturedNow, entry);
        Assert.Equal(BaselineChoiceKind.Journal, c.Kind);
        Assert.Equal(ReleaseAction.ReapplyPoints, ReleasePlanner.Plan(c.Baseline.PreAppAtFactory).Action);
        Assert.Equal(CapturedCurve, c.Baseline.Speeds);
    }

    [Fact]
    public void PointsEqualButNotAtFactory_IsNotAlreadyRestored()
    {
        // The bench state after rc.2's restore: points equal the journal's, the pre-app GPU was at factory,
        // and IsAtFactory reads false now. The curve is not flat, so this must not fall to Stale either.
        var entry = DirtyJournal(true).Find(Id())!;
        var capturedNow = new FanBaseline(CapturedCurve, true, false, false);
        var c = BaselinePlanner.Choose(Id(), capturedNow, entry);
        Assert.Equal(BaselineChoiceKind.Journal, c.Kind);
        Assert.Equal(BaselineSource.Journal, c.Source);
        Assert.Equal(ReleaseAction.FactoryReset, ReleasePlanner.Plan(c.Baseline.PreAppAtFactory).Action);
    }

    [Fact]
    public void PointsEqualAndIsAtFactoryUnreadable_IsNotAlreadyRestored()
    {
        var entry = DirtyJournal(true).Find(Id())!;
        var c = BaselinePlanner.Choose(Id(), new FanBaseline(CapturedCurve, true, false, null), entry);
        Assert.Equal(BaselineChoiceKind.Journal, c.Kind);
    }

    [Fact]
    public void PointsEqual_AtFactoryNow_IsAlreadyRestored()
    {
        var entry = DirtyJournal(true).Find(Id())!;
        var c = BaselinePlanner.Choose(Id(), new FanBaseline(CapturedCurve, true, false, true), entry);
        Assert.Equal(BaselineChoiceKind.AlreadyRestored, c.Kind);
        Assert.Equal(BaselineSource.Captured, c.Source);
    }

    [Fact]
    public void PointsEqual_PreAppNotFactoryOrLegacy_IsAlreadyRestored()
    {
        foreach (bool? pre in new bool?[] { false, null })
        {
            var entry = DirtyJournal(pre).Find(Id())!;
            var c = BaselinePlanner.Choose(Id(), new FanBaseline(CapturedCurve, true, false, false), entry);
            Assert.Equal(BaselineChoiceKind.AlreadyRestored, c.Kind);
        }
    }

    [Fact]
    public void Choose_Captured_CarriesCapturedPreAppAtFactory()
    {
        var c = BaselinePlanner.Choose(Id(), new FanBaseline(CapturedCurve, true, false, true), null);
        Assert.Equal(true, c.Baseline.PreAppAtFactory);
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

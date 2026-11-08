using LightweightAmdGpuFanControl.Diagnostics;
using Xunit;

public class ProbeReportTests
{
    private static ProbeReport Report(params bool[] supported)
    {
        var r = new ProbeReport { PreloadLine = "adl-preload: NotNeeded", AdlxInit = "ok", JournalLine = "journal: none" };
        for (int i = 0; i < supported.Length; i++)
            r.Gpus.Add(new ProbeGpu { Index = i, Name = "GPU" + i, ManualFanTuningSupported = supported[i] });
        return r;
    }

    [Fact]
    public void ExitCode_AnySupported_Zero() => Assert.Equal(0, Report(false, true).ExitCode);

    [Fact]
    public void ExitCode_NoneSupported_Three()
    {
        Assert.Equal(3, Report(false).ExitCode);
        Assert.Equal(3, Report().ExitCode);
    }

    [Fact]
    public void Format_ContainsPreloadLineJournalLineAndEachGpu()
    {
        var r = Report(true, false);
        r.Gpus[0].SpeedMin = 10; r.Gpus[0].SpeedMax = 100; r.Gpus[0].ZeroRpmSupported = true; r.Gpus[0].ZeroRpmEnabled = false;
        r.Gpus[0].CurrentSpeeds = new[] { 20, 40, 60 };
        r.Gpus[1].Error = "ADLX_FAIL";
        r.AdlAdapters.Add(new ProbeAdlAdapter { Index = 2, CapsResult = 0, Supported = true, Enabled = false, Version = 7, Family = "OverdriveN" });
        r.Modules.Add(new ProbeModule { Name = "atiadlxx.dll", Path = @"C:\Windows\System32\atiadlxx.dll", Version = "1.2.3" });
        var text = r.Format();
        Assert.Contains("adl-preload: NotNeeded", text);
        Assert.Contains("journal: none", text);
        Assert.Contains("GPU0", text);
        Assert.Contains("GPU1", text);
        Assert.Contains("ADLX_FAIL", text);
        Assert.Contains("20,40,60", text);
        Assert.Contains("OverdriveN", text);
        Assert.Contains(@"C:\Windows\System32\atiadlxx.dll", text);
        Assert.Contains("1.2.3", text);
    }

    [Fact]
    public void Format_NoUserProfilePath()
    {
        var r = Report(true);
        r.Modules.Add(new ProbeModule { Name = "amdadlx64.dll", Path = @"c:\users\x\AppData\Local\amdadlx64.dll", Version = null });
        var text = r.Format();
        Assert.Contains(@"%USERPROFILE%\AppData\Local\amdadlx64.dll", text);
        Assert.DoesNotContain(@"\x\", text);
        Assert.DoesNotContain("users", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Format_MixedModule_Flagged()
    {
        var r = Report(true);
        r.Modules.Add(new ProbeModule { Name = "atiadlxx.dll", Path = @"C:\Windows\System32\atiadlxx.dll", Version = "1", MixedLine = true });
        r.Modules.Add(new ProbeModule { Name = "amdadlx64.dll", Path = @"C:\Windows\System32\amdadlx64.dll", Version = "1" });
        Assert.True(r.HasMixedModules);
        var text = r.Format();
        Assert.Contains("WARNING: mixed AMD driver line loaded", text);
        var lines = text.Split('\n');
        Assert.EndsWith(" MIXED-LINE", lines.Single(l => l.Contains("atiadlxx.dll")).TrimEnd());
        Assert.DoesNotContain("MIXED-LINE", lines.Single(l => l.Contains("amdadlx64.dll")));
        Assert.Equal(0, r.ExitCode);
    }

    [Fact]
    public void PrintsTuningAtFactory_AndPreAppMode()
    {
        var r = Report(true, true, true, true);
        r.Gpus[0].TuningAtFactory = TuningAtFactoryState.Yes;
        r.Gpus[1].TuningAtFactory = TuningAtFactoryState.No;
        r.Gpus[2].TuningAtFactory = TuningAtFactoryState.Unsupported;
        r.Gpus[3].TuningAtFactory = TuningAtFactoryState.Error;
        r.JournalEntries.Add(new ProbeJournalEntry { Name = "GPU0", Dirty = true, PreAppAtFactory = true });
        r.JournalEntries.Add(new ProbeJournalEntry { Name = "GPU1", Dirty = true, PreAppAtFactory = false });
        r.JournalEntries.Add(new ProbeJournalEntry { Name = "GPU2", Dirty = true, PreAppAtFactory = null });
        var lines = r.Format().Split('\n');
        Assert.Contains("tuningAtFactory=yes", lines.Single(l => l.Contains("'GPU0'") && l.Contains("manualFanTuning")));
        Assert.Contains("tuningAtFactory=no", lines.Single(l => l.Contains("'GPU1'") && l.Contains("manualFanTuning")));
        Assert.Contains("tuningAtFactory=unsupported", lines.Single(l => l.Contains("'GPU2'") && l.Contains("manualFanTuning")));
        Assert.Contains("tuningAtFactory=error", lines.Single(l => l.Contains("'GPU3'") && l.Contains("manualFanTuning")));
        Assert.Contains("journal entry 'GPU0' dirty=yes preAppAtFactory=true", r.Format());
        Assert.Contains("journal entry 'GPU1' dirty=yes preAppAtFactory=false", r.Format());
        Assert.Contains("journal entry 'GPU2' dirty=yes preAppAtFactory=unknown", r.Format());
        Assert.Contains("pre-app mode unknown", r.Format());
    }

    [Fact]
    public void Format_NoTuningAtFactory_WhenNotRead()
    {
        var text = Report(false).Format();
        Assert.DoesNotContain("tuningAtFactory", text);
        Assert.DoesNotContain("release incomplete", text);
    }

    [Fact]
    public void Format_DirtyFactoryEntryOnNonFactoryGpu_FlagsReleaseIncomplete()
    {
        var r = Report(true);
        r.Gpus[0].TuningAtFactory = TuningAtFactoryState.No;
        r.JournalEntries.Add(new ProbeJournalEntry { Name = "GPU0", Dirty = true, PreAppAtFactory = true });
        Assert.Contains("WARNING: release incomplete for 'GPU0'", r.Format());

        var ok = Report(true);
        ok.Gpus[0].TuningAtFactory = TuningAtFactoryState.Yes;
        ok.JournalEntries.Add(new ProbeJournalEntry { Name = "GPU0", Dirty = true, PreAppAtFactory = true });
        Assert.DoesNotContain("release incomplete", ok.Format());
    }

    [Fact]
    public void Format_LastRunGpuIssues_Printed()
    {
        var r = Report(true);
        r.LastRunIssues.Add("GPU0 degraded: Result: ADLX_FAIL");
        var text = r.Format();
        Assert.Contains("last run: GPU0 degraded: Result: ADLX_FAIL", text);
        Assert.DoesNotContain("last run:", Report(true).Format());
    }

    [Fact]
    public void HasMixedModules_FalseWhenNone()
    {
        var r = Report(true);
        r.Modules.Add(new ProbeModule { Name = "atiadlxx.dll", Path = @"C:\x\atiadlxx.dll" });
        Assert.False(r.HasMixedModules);
        Assert.DoesNotContain("WARNING", r.Format());
        Assert.DoesNotContain("MIXED-LINE", r.Format());
    }
}

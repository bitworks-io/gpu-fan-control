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
}

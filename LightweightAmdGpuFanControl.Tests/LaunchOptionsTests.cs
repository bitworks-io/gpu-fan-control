using LightweightAmdGpuFanControl.Cli;
using Xunit;

public class LaunchOptionsTests
{
    [Fact]
    public void Parse_NoArgs_TrayInteractive()
    {
        var o = LaunchOptions.Parse(Array.Empty<string>());
        Assert.Equal(LaunchMode.Tray, o.Mode);
        Assert.False(o.Automation);
        Assert.False(o.NoPreload);
        Assert.Empty(o.Ignored);
        Assert.Null(o.Error);
    }

    [Fact]
    public void Parse_Null_Tray()
    {
        var o = LaunchOptions.Parse(null);
        Assert.Equal(LaunchMode.Tray, o.Mode);
        Assert.Null(o.Error);
    }

    [Fact]
    public void Parse_Automation_TrayAutomation()
    {
        var o = LaunchOptions.Parse(new[] { "--automation" });
        Assert.Equal(LaunchMode.Tray, o.Mode);
        Assert.True(o.Automation);
    }

    [Fact]
    public void Parse_Probe_NoPreload()
    {
        var o = LaunchOptions.Parse(new[] { "--probe", "--no-preload" });
        Assert.Equal(LaunchMode.Probe, o.Mode);
        Assert.True(o.NoPreload);
        Assert.Null(o.Error);
    }

    [Fact]
    public void Parse_RestoreAuto()
    {
        var o = LaunchOptions.Parse(new[] { "--restore-auto" });
        Assert.Equal(LaunchMode.RestoreAuto, o.Mode);
        Assert.Null(o.Error);
    }

    [Fact]
    public void Parse_CaseInsensitive()
    {
        var o = LaunchOptions.Parse(new[] { "--PROBE", "--No-Preload", "--Automation" });
        Assert.Equal(LaunchMode.Probe, o.Mode);
        Assert.True(o.NoPreload);
        Assert.True(o.Automation);
    }

    [Fact]
    public void Parse_UnknownArg_IgnoredAndListed()
    {
        var o = LaunchOptions.Parse(new[] { "--future", "--probe", "-x" });
        Assert.Equal(LaunchMode.Probe, o.Mode);
        Assert.Equal(new[] { "--future", "-x" }, o.Ignored);
        Assert.Null(o.Error);
    }

    [Fact]
    public void Parse_ProbeAndRestoreAuto_UsageError()
    {
        var o = LaunchOptions.Parse(new[] { "--probe", "--restore-auto" });
        Assert.False(string.IsNullOrWhiteSpace(o.Error));
    }

    [Fact]
    public void Parse_NoPreloadWithoutProbe_UsageError()
    {
        Assert.False(string.IsNullOrWhiteSpace(LaunchOptions.Parse(new[] { "--no-preload" }).Error));
        Assert.False(string.IsNullOrWhiteSpace(LaunchOptions.Parse(new[] { "--restore-auto", "--no-preload" }).Error));
    }

    [Fact]
    public void Parse_AutomationWithProbe_Accepted()
    {
        var o = LaunchOptions.Parse(new[] { "--automation", "--probe" });
        Assert.Null(o.Error);
        Assert.True(o.Automation);
        Assert.Equal(LaunchMode.Probe, o.Mode);
        var r = LaunchOptions.Parse(new[] { "--automation", "--restore-auto" });
        Assert.Null(r.Error);
        Assert.Equal(LaunchMode.RestoreAuto, r.Mode);
    }

    [Fact]
    public void ExitCodes_AreStable()
    {
        Assert.Equal(0, ExitCodes.Ok);
        Assert.Equal(1, ExitCodes.Error);
        Assert.Equal(2, ExitCodes.Usage);
        Assert.Equal(3, ExitCodes.NothingToDo);
        Assert.Equal(4, ExitCodes.AlreadyRunning);
    }

    [Fact]
    public void Parse_StatusFile_Captured()
    {
        var o = LaunchOptions.Parse(new[] { "--automation", "--STATUS-FILE", @"D:\x y\s.json" });
        Assert.Equal(@"D:\x y\s.json", o.StatusFile);
        Assert.Null(o.StatusFileError);
        Assert.Null(o.Error);
        Assert.Empty(o.Ignored);
    }

    [Fact]
    public void Parse_StatusFileMissingValue_WarningNotError()
    {
        var o = LaunchOptions.Parse(new[] { "--automation", "--status-file" });
        Assert.Null(o.StatusFile);
        Assert.Equal("--status-file needs a path", o.StatusFileError);
        Assert.Null(o.Error);
        Assert.Equal(LaunchMode.Tray, o.Mode);
        Assert.True(o.Automation);
    }

    [Fact]
    public void Parse_StatusFileFollowedByFlag_NotConsumed()
    {
        var o = LaunchOptions.Parse(new[] { "--status-file", "--probe" });
        Assert.Null(o.StatusFile);
        Assert.Equal("--status-file needs a path", o.StatusFileError);
        Assert.Null(o.Error);
        Assert.Equal(LaunchMode.Probe, o.Mode);
    }

    [Fact]
    public void Parse_NoStatusFile_Null()
    {
        var o = LaunchOptions.Parse(new[] { "--automation" });
        Assert.Null(o.StatusFile);
        Assert.Null(o.StatusFileError);
    }

    [Fact]
    public void Parse_Stop()
    {
        var o = LaunchOptions.Parse(new[] { "--STOP" });
        Assert.Equal(LaunchMode.Stop, o.Mode);
        Assert.Null(o.Error);
    }

    [Fact]
    public void Parse_StopWithProbe_UsageError()
    {
        Assert.False(string.IsNullOrWhiteSpace(LaunchOptions.Parse(new[] { "--stop", "--probe" }).Error));
    }

    [Fact]
    public void Parse_StopWithRestoreAuto_UsageError()
    {
        Assert.False(string.IsNullOrWhiteSpace(LaunchOptions.Parse(new[] { "--stop", "--restore-auto" }).Error));
    }

    [Fact]
    public void Parse_ResetTuningToFactory_WithGpu()
    {
        var o = LaunchOptions.Parse(new[] { "--reset-tuning-to-factory", "--gpu", "1234" });
        Assert.Equal(LaunchMode.ResetTuningToFactory, o.Mode);
        Assert.Equal(1234, o.GpuUniqueId);
        Assert.Null(o.Error);
        var neg = LaunchOptions.Parse(new[] { "--GPU", "-7", "--Reset-Tuning-To-Factory" });
        Assert.Equal(-7, neg.GpuUniqueId);
        Assert.Null(neg.Error);
    }

    [Fact]
    public void Parse_ResetTuningToFactory_RequiresIntegerGpu()
    {
        Assert.NotNull(LaunchOptions.Parse(new[] { "--reset-tuning-to-factory" }).Error);
        Assert.NotNull(LaunchOptions.Parse(new[] { "--reset-tuning-to-factory", "--gpu" }).Error);
        Assert.NotNull(LaunchOptions.Parse(new[] { "--reset-tuning-to-factory", "--gpu", "--probe" }).Error);
        Assert.NotNull(LaunchOptions.Parse(new[] { "--reset-tuning-to-factory", "--gpu", "abc" }).Error);
        Assert.NotNull(LaunchOptions.Parse(new[] { "--reset-tuning-to-factory", "--gpu", "1.5" }).Error);
    }

    [Fact]
    public void Parse_Gpu_RejectedWithoutResetMode()
    {
        Assert.NotNull(LaunchOptions.Parse(new[] { "--gpu", "3" }).Error);
        Assert.NotNull(LaunchOptions.Parse(new[] { "--probe", "--gpu", "3" }).Error);
        Assert.NotNull(LaunchOptions.Parse(new[] { "--restore-auto", "--gpu", "3" }).Error);
    }

    [Fact]
    public void Parse_ResetTuningToFactory_ExclusiveWithEveryOtherMode()
    {
        foreach (var other in new[] { "--probe", "--restore-auto", "--stop" })
        {
            var o = LaunchOptions.Parse(new[] { "--reset-tuning-to-factory", "--gpu", "1", other });
            Assert.NotNull(o.Error);
        }
        Assert.NotNull(LaunchOptions.Parse(new[] { "--reset-tuning-to-factory", "--gpu", "1", "--no-preload" }).Error);
    }
}

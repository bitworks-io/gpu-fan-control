using Xunit;

public class ProbeSourceGuardTests
{
    private static readonly string[] Forbidden =
    {
        "SetFanTuningStates", "SetZeroRPM", "SetTargetFanSpeed", ".Reset(", "FanSpeedToDefault",
        "FanControlSet", "FanSpeedSet", "CustomFanSet", "FanController", "IFanControlBackend", "RestoreAutomatic",
    };

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "LightweightAmdGpuFanControl.sln"))) return dir.FullName;
            dir = dir.Parent;
        }
        throw new InvalidOperationException("repo root (LightweightAmdGpuFanControl.sln) not found above " + AppContext.BaseDirectory);
    }

    [Fact]
    public void CapabilityProbe_CallsNoFanWriteApi()
    {
        var path = Path.Combine(FindRepoRoot(), "LightweightAmdGpuFanControl", "Services", "CapabilityProbe.cs");
        var source = File.ReadAllText(path);
        foreach (var token in Forbidden)
            Assert.DoesNotContain(token, source);
    }
}

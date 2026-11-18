using Xunit;

public class StopCommandSourceGuardTests
{
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
    public void StopCommand_OpensOnlyAndNeverKills()
    {
        var path = Path.Combine(FindRepoRoot(), "LightweightAmdGpuFanControl", "Services", "StopCommand.cs");
        var source = SourceText.Read(path);
        Assert.Contains("TryOpenExisting", source);
        foreach (var token in new[] { "new EventWaitHandle", "Kill(", "taskkill", "TerminateProcess" })
            Assert.DoesNotContain(token, source);
    }
}

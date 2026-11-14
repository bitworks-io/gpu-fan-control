using System.Text.RegularExpressions;
using Xunit;

public class VersionTests
{
    private const string Expected = "1.0.2";

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

    private static string Read(params string[] parts)
        => SourceText.Read(Path.Combine(new[] { FindRepoRoot() }.Concat(parts).ToArray()));

    [Fact]
    public void VersionFile_IsCurrent() => Assert.Equal(Expected, Read("VERSION").Trim());

    [Fact]
    public void AppProject_VersionIsCurrent()
        => Assert.Contains($"<Version>{Expected}</Version>", Read("LightweightAmdGpuFanControl", "LightweightAmdGpuFanControl.csproj"));

    [Fact]
    public void InstallerDefault_VersionIsCurrent()
    {
        var iss = Read("installer", "LightweightAmdGpuFanControl.iss");
        var m = Regex.Match(iss, "#ifndef MyAppVersion\\s*\\n\\s*#define MyAppVersion \"([^\"]+)\"");
        Assert.True(m.Success, "default MyAppVersion not found");
        Assert.Equal(Expected, m.Groups[1].Value);
    }
}

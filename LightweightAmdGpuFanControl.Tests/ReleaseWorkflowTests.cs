using Xunit;

public class ReleaseWorkflowTests
{
    private const string Prerelease = "prerelease: ${{ contains(github.ref_name, '-') }}";
    private const string ReleaseAction = "uses: softprops/action-gh-release@v2";

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

    private static string Workflow()
        => SourceText.Read(Path.Combine(FindRepoRoot(), ".github", "workflows", "build-windows.yml"));

    private static int Count(string text, string token)
    {
        int n = 0, i = 0;
        while ((i = text.IndexOf(token, i, StringComparison.Ordinal)) >= 0) { n++; i += token.Length; }
        return n;
    }

    private static string ReleaseStep()
    {
        var w = Workflow();
        var at = w.IndexOf(ReleaseAction, StringComparison.Ordinal);
        Assert.True(at >= 0, "release action not found");
        return w.Substring(at);
    }

    [Fact]
    public void ReleaseStep_IsExplicitlyNeverLatestByItself()
    {
        var step = ReleaseStep();
        Assert.Equal(1, Count(step, Prerelease));
        Assert.Equal(1, Count(step, "make_latest: 'false'"));
    }

    [Fact]
    public void Workflow_NeverRequestsLatest()
    {
        var w = Workflow();
        foreach (var token in new[] { "make_latest: 'true'", "make_latest: true", "make_latest: 'legacy'" })
            Assert.DoesNotContain(token, w);
    }

    [Fact]
    public void ReleaseStep_KeepsItsAssetAndNotes()
    {
        var step = ReleaseStep();
        Assert.Contains("files: ./release/LightweightAmdGpuFanControl-Setup.exe", step);
        Assert.Contains("generate_release_notes: true", step);
    }
}

using System.Text.RegularExpressions;
using Xunit;

/// <summary>
/// Text checks over the app sources (which do not compile on the test host) that pin the rc.3 release design:
/// fans go back to the driver's automatic control through <c>ReleasePlanner</c>, never through the wrapper's
/// <c>Reset</c>, and the journal only goes clean after an exact release.
/// </summary>
public class FanReleaseSourceGuardTests
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

    /// <summary>Source with // and /* */ comments removed, so guards look at code only.</summary>
    private static string Code(params string[] relative)
    {
        var path = Path.Combine(new[] { FindRepoRoot(), "LightweightAmdGpuFanControl" }.Concat(relative).ToArray());
        var text = File.ReadAllText(path);
        text = Regex.Replace(text, @"/\*.*?\*/", "", RegexOptions.Singleline);
        return Regex.Replace(text, @"//[^\r\n]*", "");
    }

    private static string FanController() => Code("Adlx", "FanController.cs");
    private static string Runner() => Code("Services", "RestoreAutoRunner.cs");

    [Fact]
    public void NoManualFanTuningResetCall_RemainsInRestorePaths()
    {
        foreach (var source in new[] { FanController(), Runner() })
        {
            Assert.False(Regex.IsMatch(source, @"\.Reset\s*\(\s*\)"), "a ManualFanTuning Reset() call is left");
            Assert.DoesNotContain("ManualFanTuning.Reset", source);
        }
    }

    [Fact]
    public void Release_GoesThroughReleasePlannerPlan()
    {
        Assert.Contains("ReleasePlanner.Plan(", FanController());
    }

    [Fact]
    public void EveryResetToFactory_IsFollowedByAnIsAtFactoryConfirmation()
    {
        foreach (var source in new[] { FanController(), Runner() })
        {
            foreach (Match m in Regex.Matches(source, @"\bResetToFactory\s*\("))
            {
                var after = source.Substring(m.Index + m.Length);
                int next = after.IndexOf("IsAtFactory(", StringComparison.Ordinal);
                Assert.True(next >= 0 && next < 400, "ResetToFactory is not followed by an IsAtFactory confirmation");
            }
        }
        Assert.Contains("ResetToFactory(", FanController());
    }

    [Fact]
    public void JournalMarkClean_OnlyAfterExactRelease()
    {
        var source = FanController();
        var marks = Regex.Matches(source, @"\.MarkClean\s*\(");
        Assert.NotEmpty(marks);
        foreach (Match m in marks)
        {
            var before = source.Substring(Math.Max(0, m.Index - 250), Math.Min(250, m.Index));
            Assert.Contains("if (exact", before);
        }

        var runner = Runner();
        foreach (Match m in Regex.Matches(runner, @"\.MarkClean\s*\("))
        {
            var before = runner.Substring(Math.Max(0, m.Index - 250), Math.Min(250, m.Index));
            Assert.Contains("AlreadyRestored", before);
        }
    }
}

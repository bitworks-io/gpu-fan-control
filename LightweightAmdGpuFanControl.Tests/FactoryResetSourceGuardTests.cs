using System.Text.RegularExpressions;
using Xunit;

/// <summary>Text checks over <c>FactoryResetCommand.cs</c> (the app does not compile on the test host).</summary>
public class FactoryResetSourceGuardTests
{
    private static string Code()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "LightweightAmdGpuFanControl.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var text = File.ReadAllText(Path.Combine(dir!.FullName, "LightweightAmdGpuFanControl", "Services", "FactoryResetCommand.cs"));
        text = Regex.Replace(text, @"/\*.*?\*/", "", RegexOptions.Singleline);
        return Regex.Replace(text, @"//[^\r\n]*", "");
    }

    [Fact]
    public void ResetToFactory_IsPrecededByAnIsAtFactoryReadAndFollowedByAConfirmation()
    {
        var s = Code();
        var m = Regex.Matches(s, @"\.ResetToFactory\s*\(");
        Assert.Single(m);
        int at = m[0].Index;
        Assert.Contains("IsAtFactory(", s.Substring(0, at));
        int after = s.IndexOf("IsAtFactory(", at, StringComparison.Ordinal);
        Assert.True(after > at && after - at < 400, "no IsAtFactory confirmation after ResetToFactory");
    }

    [Fact]
    public void Lock_IsTakenBeforeAnyAdlxCall()
    {
        var s = Code();
        int lockAt = s.IndexOf("SingleInstance.TryAcquire(", StringComparison.Ordinal);
        Assert.True(lockAt >= 0);
        foreach (var token in new[] { "BoundAdlPreloader", "AdlxInitializer", "GetGPUs", "IsAtFactory(", "ResetToFactory(", "IsManualFanTuningSupported" })
        {
            int i = s.IndexOf(token, StringComparison.Ordinal);
            Assert.True(i > lockAt, token + " appears before the lock");
        }
    }

    [Fact]
    public void OnlyTheMatchedUniqueIdGpuIsTouched()
    {
        var s = Code();
        Assert.Contains("UniqueId == ", s);
        // A single reset call, and the fan writers are never used.
        Assert.Single(Regex.Matches(s, @"\.ResetToFactory\s*\("));
        foreach (var token in new[] { "SetFanTuningStates", "SetZeroRPM", "FanController", "foreach (var gpu in gpus)" })
            Assert.DoesNotContain(token, s);
    }

    [Fact]
    public void MarkClean_OnlyAfterAConfirmedReset()
    {
        var s = Code();
        var marks = Regex.Matches(s, @"\.MarkClean\w*\s*\(");
        Assert.Single(marks);
        var before = s.Substring(Math.Max(0, marks[0].Index - 300), Math.Min(300, marks[0].Index));
        Assert.Contains("confirmed", before);
    }
}

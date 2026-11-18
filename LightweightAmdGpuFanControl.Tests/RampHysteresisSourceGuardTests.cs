using System.Text.RegularExpressions;
using Xunit;

/// <summary>Text checks over FanControlService.cs: the ramp dwell uses a monotonic clock and a resumed GPU starts plain.</summary>
public class RampHysteresisSourceGuardTests
{
    private static string Code()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "LightweightAmdGpuFanControl.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var text = SourceText.Read(Path.Combine(dir!.FullName, "LightweightAmdGpuFanControl", "Services", "FanControlService.cs"));
        text = Regex.Replace(text, @"/\*.*?\*/", "", RegexOptions.Singleline);
        return Regex.Replace(text, @"//[^\r\n]*", "");
    }

    [Fact]
    public void TempReading_IsBuiltFromTheStopwatch_NeverTheWallClock()
    {
        var s = Code();
        Assert.Single(Regex.Matches(s, @"new TempReading\("));
        var arg = Regex.Match(s, @"new TempReading\(\s*[^,()]+,\s*(\w+\(\))\s*\)");
        Assert.True(arg.Success, "second TempReading argument must be a helper call");
        Assert.Equal("MonotonicMs()", arg.Groups[1].Value);
        var helper = Regex.Match(s, @"static long MonotonicMs\(\)\s*=>\s*([^;]*);");
        Assert.True(helper.Success);
        Assert.Contains("Stopwatch.GetTimestamp()", helper.Groups[1].Value);
        Assert.Contains("Stopwatch.Frequency", helper.Groups[1].Value);
    }

    [Fact]
    public void EveryLastAppliedReset_AlsoClearsHasPrevious()
    {
        var s = Code();
        var resets = Regex.Matches(s, @"\.LastAppliedPercent = -1;");
        Assert.True(resets.Count >= 5);
        foreach (Match r in resets)
        {
            var after = s.Substring(r.Index + r.Length, Math.Min(120, s.Length - r.Index - r.Length));
            Assert.Matches(@"^\s*channel\.State\.HasPrevious = false;", after);
        }
    }
}

using System.Text.RegularExpressions;
using Xunit;

/// <summary>Text checks over FanControlService.cs: a fan-write error never leads to a retry loop.</summary>
public class FailClosedSourceGuardTests
{
    private static string Code()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "LightweightAmdGpuFanControl.sln"))) dir = dir.Parent;
        Assert.NotNull(dir);
        var text = File.ReadAllText(Path.Combine(dir!.FullName, "LightweightAmdGpuFanControl", "Services", "FanControlService.cs"));
        text = Regex.Replace(text, @"/\*.*?\*/", "", RegexOptions.Singleline);
        return Regex.Replace(text, @"//[^\r\n]*", "");
    }

    [Fact]
    public void RegulationLoop_ChecksStateBeforeEveryWrite()
    {
        var s = Code();
        var writes = Regex.Matches(s, @"\.SetFanPercent\s*\(");
        Assert.Single(writes); // the one regulation write
        Assert.Contains("Control.MayWrite", s.Substring(Math.Max(0, writes[0].Index - 500), Math.Min(500, writes[0].Index)));
        // AdjustChannel also returns before any write for a refused or degraded GPU.
        int adjust = s.IndexOf("private GpuStatus AdjustChannel", StringComparison.Ordinal);
        Assert.True(adjust >= 0);
        Assert.Contains("Control.MayWrite", s.Substring(adjust, 600));
    }

    [Fact]
    public void WriteErrorHandler_ReleasesExactlyOnce_AndNeverRetries()
    {
        var s = Code();
        int start = s.IndexOf("private void HandleWriteError", StringComparison.Ordinal);
        Assert.True(start >= 0);
        int end = s.IndexOf("\n    }\n", start, StringComparison.Ordinal);
        var body = s.Substring(start, end - start);
        Assert.Single(Regex.Matches(body, @"\.RestoreAutomaticFanControl\s*\("));
        Assert.Contains("OnWriteError(", body);
        Assert.Contains("WriteErrorAction.ReleaseOnce", body);
        Assert.DoesNotContain("SetFanPercent", body);
        // The release call sits inside the ReleaseOnce branch.
        Assert.True(body.IndexOf("WriteErrorAction.ReleaseOnce", StringComparison.Ordinal) < body.IndexOf("RestoreAutomaticFanControl", StringComparison.Ordinal));
        // The regulation write is wrapped so its error reaches the handler.
        Assert.Contains("HandleWriteError(channel", s);
    }

    [Fact]
    public void SelfTestError_RefusesTheGpu()
    {
        var s = Code();
        Assert.Contains("RunTest(channel.Backend, _logService, out", s);
        Assert.Contains("Control.OnWriteError(", s);
        Assert.Contains("Control.BeginRegulating()", s);
    }
}

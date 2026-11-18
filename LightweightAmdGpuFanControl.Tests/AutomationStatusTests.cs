using System.Text.Json;
using LightweightAmdGpuFanControl.Automation;
using Xunit;

public class AutomationStatusTests
{
    private static readonly DateTime Started = new(2026, 11, 6, 18, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Now = new(2026, 11, 6, 18, 0, 5, DateTimeKind.Utc);

    private static StatusGpu[] Gpus() => new[]
    {
        new StatusGpu { Name = "GPU A", Backend = "ADLX" },
        new StatusGpu { Name = "GPU B", Backend = "ADL" },
    };

    [Fact]
    public void Starting_StateStartingNotEngaged()
    {
        var s = AutomationStatus.Starting(1234, Started, Now, "1.1.0");
        Assert.Equal("starting", s.State);
        Assert.False(s.IsEngaged);
        Assert.Null(s.Reason);
        Assert.Null(s.Gpu);
        Assert.Null(s.Backend);
        Assert.NotNull(s.Gpus);
        Assert.Empty(s.Gpus);
        Assert.Equal(1, s.Schema);
    }

    [Fact]
    public void Engaged_FirstGpuAndAllGpus()
    {
        var s = AutomationStatus.Engaged(1234, Started, Now, "1.1.0", Gpus());
        Assert.Equal("engaged", s.State);
        Assert.True(s.IsEngaged);
        Assert.Equal("GPU A", s.Gpu);
        Assert.Equal("ADLX", s.Backend);
        Assert.Equal(2, s.Gpus.Count);
        Assert.Equal("GPU B", s.Gpus[1].Name);
    }

    [Fact]
    public void Engaged_EmptyOrNullList_NoGpuAndGpusNotNull()
    {
        var s = AutomationStatus.Engaged(1, Started, Now, "1.1.0", null!);
        Assert.Null(s.Gpu);
        Assert.NotNull(s.Gpus);
    }

    [Fact]
    public void Unavailable_ReasonSet()
    {
        var s = AutomationStatus.Unavailable(1, Started, Now, "1.1.0", StatusReasons.NoBackend);
        Assert.Equal("unavailable", s.State);
        Assert.False(s.IsEngaged);
        Assert.Equal("no-backend", s.Reason);
        Assert.Equal("self-test-failed", StatusReasons.SelfTestFailed);
    }

    [Fact]
    public void Unavailable_NoEnabledGpu_RoundTripsInJson()
    {
        Assert.Equal("no-enabled-gpu", StatusReasons.NoEnabledGpu);
        var s = AutomationStatus.Unavailable(1, Started, Now, "1.1.0", StatusReasons.NoEnabledGpu);
        Assert.Equal("unavailable", s.State);
        Assert.False(s.IsEngaged);
        Assert.Contains("\"reason\": \"no-enabled-gpu\"", s.ToJson());
        Assert.Contains("\"state\": \"unavailable\"", s.ToJson());
    }

    [Fact]
    public void Stopped_ReleaseIssues_AreReportedInJson()
    {
        var plain = AutomationStatus.Stopped(1, Started, Now, "1.1.0");
        Assert.Empty(plain.ReleaseIssues);
        Assert.Contains("\"releaseIssues\": []", plain.ToJson());

        var issues = new[] { ReleaseIssues.Format(ReleaseIssues.Incomplete, "GPU A") };
        var s = AutomationStatus.Stopped(1, Started, Now, "1.1.0", issues);
        Assert.Equal("release-incomplete: GPU A", Assert.Single(s.ReleaseIssues));
        Assert.Contains("release-incomplete: GPU A", s.ToJson());
        Assert.Empty(AutomationStatus.Stopped(1, Started, Now, "1.1.0", null).ReleaseIssues);
    }

    [Fact]
    public void GpuIssues_ReportedInEngagedAndStopped_AndParseBack()
    {
        var issues = new[]
        {
            new StatusGpuIssue { Gpu = "GPU A", State = GpuIssueStates.Degraded, Message = "Result: ADLX_FAIL" },
            new StatusGpuIssue { Gpu = "GPU B", State = GpuIssueStates.Refused, Message = "no" },
        };
        Assert.Empty(AutomationStatus.Engaged(1, Started, Now, "1.1.0", Gpus()).GpuIssues);
        var e = AutomationStatus.Engaged(1, Started, Now, "1.1.0", Gpus(), issues);
        Assert.Equal(2, e.GpuIssues.Count);
        Assert.Contains("\"gpuIssues\"", e.ToJson());
        Assert.Contains("\"state\": \"degraded\"", e.ToJson());
        Assert.Equal(2, AutomationStatus.Stopped(1, Started, Now, "1.1.0", null, issues).GpuIssues.Count);

        var back = AutomationStatus.TryParse(e.ToJson());
        Assert.NotNull(back);
        Assert.Equal("Result: ADLX_FAIL", back!.GpuIssues[0].Message);
        Assert.Equal("engaged", back.State);
        Assert.Null(AutomationStatus.TryParse("not json"));
        Assert.Null(AutomationStatus.TryParse(null));
    }

    [Fact]
    public void Engaged_CarriesReleaseIssuesInRun()
    {
        var s = AutomationStatus.Engaged(1, Started, Now, "1.1.0", Gpus(), null, new[] { "release-incomplete: GPU A" });
        Assert.Equal("release-incomplete: GPU A", Assert.Single(s.ReleaseIssues));
        Assert.Contains("release-incomplete: GPU A", s.ToJson());
        Assert.Empty(AutomationStatus.Engaged(1, Started, Now, "1.1.0", Gpus()).ReleaseIssues);
    }

    [Fact]
    public void Stopped_ReasonExit()
    {
        var s = AutomationStatus.Stopped(1, Started, Now, "1.1.0");
        Assert.Equal("stopped", s.State);
        Assert.False(s.IsEngaged);
        Assert.Equal("exit", s.Reason);
    }

    [Fact]
    public void ToJson_CamelCaseSchema1()
    {
        var json = AutomationStatus.Starting(1234, Started, Now, "1.1.0").ToJson();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        foreach (var key in new[] { "schema", "state", "engaged", "reason", "gpu", "backend", "gpus", "pid", "startedUtc", "updatedUtc", "version" })
            Assert.True(root.TryGetProperty(key, out _), "missing key " + key);
        Assert.Equal(1, root.GetProperty("schema").GetInt32());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("reason").ValueKind);
        Assert.Equal(JsonValueKind.Array, root.GetProperty("gpus").ValueKind);
        Assert.Contains("\"reason\": null", json);
        Assert.Contains("\n", json); // indented
    }

    [Fact]
    public void ToJson_Engaged_RoundTripsGpus()
    {
        var json = AutomationStatus.Engaged(7, Started, Now, "1.1.0", Gpus()).ToJson();
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        Assert.True(root.GetProperty("engaged").GetBoolean());
        Assert.Equal("GPU A", root.GetProperty("gpu").GetString());
        Assert.Equal("ADLX", root.GetProperty("gpus")[0].GetProperty("backend").GetString());
        Assert.Equal("GPU B", root.GetProperty("gpus")[1].GetProperty("name").GetString());
    }

    [Fact]
    public void Status_CarriesPidAndStartedUtc()
    {
        var s = AutomationStatus.Engaged(4321, Started, Now, "1.1.0", Gpus());
        Assert.Equal(4321, s.Pid);
        Assert.Equal("2026-11-06T18:00:00Z", s.StartedUtc);
        Assert.Equal("1.1.0", s.Version);
        using var doc = JsonDocument.Parse(s.ToJson());
        Assert.Equal(4321, doc.RootElement.GetProperty("pid").GetInt32());
        Assert.Equal("2026-11-06T18:00:00Z", doc.RootElement.GetProperty("startedUtc").GetString());
    }

    [Fact]
    public void Utc_Iso8601Z()
    {
        var s = AutomationStatus.Starting(1, Started, Now, "1.1.0");
        Assert.Equal("2026-11-06T18:00:05Z", s.UpdatedUtc);
        // A non-UTC instant is converted to UTC.
        var offset = new DateTime(2026, 11, 6, 20, 0, 0, DateTimeKind.Utc).ToLocalTime();
        var t = AutomationStatus.Starting(1, offset, offset, "1.1.0");
        Assert.Equal("2026-11-06T20:00:00Z", t.StartedUtc);
    }
}

public class StatusFilePathTests
{
    private static readonly Func<string, bool> Exists = d => d == @"D:\auto" || d == @"D:\" || d == @"D:\auto\sub";
    private static readonly Func<string, bool> NotReparse = _ => false;

    private static string? Validate(string? path, Func<string, bool>? exists = null, Func<string, bool>? reparse = null)
        => StatusFilePath.Validate(path, exists ?? Exists, reparse ?? NotReparse, out _);

    private static string? ErrorFor(string? path, Func<string, bool>? exists = null, Func<string, bool>? reparse = null)
    {
        StatusFilePath.Validate(path, exists ?? Exists, reparse ?? NotReparse, out var e);
        return e;
    }

    [Fact]
    public void StatusFile_Valid_Returned()
    {
        StatusFilePath.Validate(@"D:\auto\status.json", Exists, NotReparse, out var err);
        Assert.Null(err);
        Assert.Equal(@"D:\auto\status.json", Validate(@"D:\auto\status.json"));
        Assert.Equal(@"D:\status.json", Validate(@"D:\status.json"));
    }

    [Fact]
    public void StatusFile_Relative_Rejected()
    {
        Assert.Null(Validate(@"auto\status.json"));
        Assert.NotNull(ErrorFor(@"auto\status.json"));
        Assert.Null(Validate(@"\auto\status.json"));
        Assert.Null(Validate(@"D:status.json"));
    }

    [Fact]
    public void StatusFile_Unc_Rejected()
    {
        Assert.Null(Validate(@"\\server\share\status.json", _ => true));
        Assert.False(string.IsNullOrWhiteSpace(ErrorFor(@"\\server\share\status.json", _ => true)));
    }

    [Fact]
    public void StatusFile_DotDot_Rejected()
    {
        Assert.Null(Validate(@"D:\auto\..\status.json", _ => true));
        Assert.Null(Validate(@"D:\auto\.\status.json", _ => true));
        Assert.NotNull(ErrorFor(@"D:\auto\..\status.json", _ => true));
    }

    [Fact]
    public void StatusFile_ParentMissing_Rejected()
    {
        Assert.Null(Validate(@"D:\nope\status.json"));
        Assert.NotNull(ErrorFor(@"D:\nope\status.json"));
    }

    [Fact]
    public void StatusFile_ReparseParent_Rejected()
    {
        Assert.Null(Validate(@"D:\auto\status.json", reparse: p => p == @"D:\auto"));
    }

    [Fact]
    public void StatusFile_ReparseFile_Rejected()
    {
        Assert.Null(Validate(@"D:\auto\status.json", reparse: p => p == @"D:\auto\status.json"));
    }

    [Fact]
    public void StatusFile_IllegalChar_Rejected()
    {
        foreach (var bad in new[] { @"D:\auto\sta<tus.json", @"D:\auto\sta>tus.json", "D:\\auto\\sta\"tus.json",
                     @"D:\auto\sta|tus.json", @"D:\auto\sta?tus.json", @"D:\auto\sta*tus.json",
                     "D:\\auto\\sta\ttus.json", @"D:\auto\sta:tus.json", @"D:\auto\sta/tus.json", @"D:\auto\" })
        {
            Assert.Null(Validate(bad, _ => true));
        }
    }

    [Fact]
    public void StatusFile_Null_Rejected()
    {
        Assert.Null(Validate(null));
        Assert.Null(Validate(""));
        Assert.False(string.IsNullOrWhiteSpace(ErrorFor(null)));
    }

    [Fact]
    public void StatusFile_ThrowingCallback_RejectedNotThrown()
    {
        Assert.Null(StatusFilePath.Validate(@"D:\auto\s.json", _ => throw new IOException("x"), NotReparse, out var e));
        Assert.False(string.IsNullOrWhiteSpace(e));
    }
}

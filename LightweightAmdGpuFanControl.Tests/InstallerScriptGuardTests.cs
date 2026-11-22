using System.Text.RegularExpressions;
using Xunit;

/// <summary>
/// Source guards for the installer's upgrade and uninstall stop ordering. A fan must never be left under app
/// control, and setup must never hang on a build that has no graceful --stop (1.0.0 / 1.0.1).
/// </summary>
public class InstallerScriptGuardTests
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

    private static string Iss() =>
        SourceText.Read(Path.Combine(FindRepoRoot(), "installer", "LightweightAmdGpuFanControl.iss"));

    /// <summary>Body of a top-level Pascal routine: from its header to the next column-0 "end;".</summary>
    private static string Routine(string source, string name)
    {
        var m = Regex.Match(source, @"^(function|procedure) " + Regex.Escape(name) + @"\(.*?^end;", RegexOptions.Singleline | RegexOptions.Multiline);
        Assert.True(m.Success, "routine not found: " + name);
        return m.Value;
    }

    private static string Section(string source, string name)
    {
        var m = Regex.Match(source, @"^\[" + name + @"\]\n(.*?)(?=^\[|\z)", RegexOptions.Singleline | RegexOptions.Multiline);
        Assert.True(m.Success, "section not found: " + name);
        return m.Groups[1].Value;
    }

    [Fact]
    public void PrepareToInstall_StopExecComesBeforeTaskkillExec()
    {
        var body = Routine(Iss(), "PrepareToInstall");
        var stop = body.IndexOf("'--stop'", StringComparison.Ordinal);
        var kill = body.IndexOf("taskkill /F /IM", StringComparison.Ordinal);
        Assert.True(stop >= 0, "--stop Exec missing");
        Assert.True(kill >= 0, "taskkill Exec missing");
        Assert.True(stop < kill, "--stop must run before the taskkill fallback");
        Assert.Equal(1, Regex.Matches(body, "'--stop'").Count);
    }

    [Fact]
    public void PrepareToInstall_GatesStopOnSupportsGracefulStopBeforeTheExec()
    {
        var body = Routine(Iss(), "PrepareToInstall");
        var gateCall = body.IndexOf("Gate := SupportsGracefulStop(", StringComparison.Ordinal);
        var stop = body.IndexOf("'--stop'", StringComparison.Ordinal);
        Assert.True(gateCall >= 0, "gate is not computed with SupportsGracefulStop");
        Assert.True(gateCall < stop, "the gate must be computed before the --stop Exec");
    }

    [Fact]
    public void GracefulStop_ExitZero_RelaunchesAndWaitsForHandles_OnlyInsideTheGate()
    {
        var body = Routine(Iss(), "PrepareToInstall");
        var gateIf = body.IndexOf("if Gate then\n  begin", StringComparison.Ordinal);
        var closing = body.IndexOf("\n  end;", gateIf, StringComparison.Ordinal);
        var branch = body[gateIf..closing];
        // --stop exit 0 means a running app stopped: [Run] must relaunch it, and file handles need a moment to drop.
        Assert.Matches(@"if ResultCode = 0 then\n\s+begin\n\s+GClosedRunningInstance := True;\n(\s*//[^\n]*\n)*\s+Sleep\(1500\);\n\s+end;", branch);
        // The graceful relaunch flag is set nowhere outside the gate branch except the taskkill path after it.
        var beforeGate = body[..gateIf];
        Assert.DoesNotContain("GClosedRunningInstance := True", beforeGate);
    }

    [Fact]
    public void GracefulStopGate_IsExactly1_0_2_0()
    {
        var iss = Iss();
        var supports = Routine(iss, "SupportsGracefulStop");
        Assert.Contains("VersionAtLeast(MS, LS, $00010000, $00020000)", supports);
        Assert.Equal(2, Regex.Matches(supports, @"\$[0-9A-Fa-f]{8}").Count);
        Assert.Contains("if MS <> MinMS then Result := MS > MinMS else Result := LS >= MinLS;", Routine(iss, "VersionAtLeast"));
    }

    [Theory]
    [InlineData(1, 0, 0, 0, false)]
    [InlineData(1, 0, 1, 0, false)]
    [InlineData(1, 0, 1, 65535, false)]
    [InlineData(1, 0, 2, 0, true)]
    [InlineData(1, 0, 2, 1, true)]
    [InlineData(1, 1, 0, 0, true)]
    [InlineData(1, 1, 1, 0, true)]
    [InlineData(2, 0, 0, 0, true)]
    [InlineData(0, 9, 9, 9, false)]
    public void GracefulStopGate_ComparisonClassifiesVersions(int a, int b, int c, int d, bool expected)
    {
        // Evaluate the comparison exactly as the script spells it, with the constants read from the script.
        var supports = Routine(Iss(), "SupportsGracefulStop");
        var m = Regex.Match(supports, @"VersionAtLeast\(MS, LS, \$([0-9A-Fa-f]{8}), \$([0-9A-Fa-f]{8})\)");
        Assert.True(m.Success);
        uint minMs = Convert.ToUInt32(m.Groups[1].Value, 16), minLs = Convert.ToUInt32(m.Groups[2].Value, 16);
        uint ms = ((uint)a << 16) | (uint)b, ls = ((uint)c << 16) | (uint)d;
        bool result = ms != minMs ? ms > minMs : ls >= minLs;
        Assert.Equal(expected, result);
    }

    [Fact]
    public void StopExec_IsReachableOnlyInsideTheGateYesBranch()
    {
        var body = Routine(Iss(), "PrepareToInstall");
        var gateIf = body.IndexOf("if Gate then\n  begin", StringComparison.Ordinal);
        var stop = body.IndexOf("'--stop'", StringComparison.Ordinal);
        var kill = body.IndexOf("taskkill /F /IM", StringComparison.Ordinal);
        Assert.True(gateIf >= 0 && gateIf < stop, "--stop Exec must sit after an 'if Gate then'");
        // The branch is closed (an 'end;' at the gate's own indentation) before the unconditional taskkill.
        var closing = body.IndexOf("\n  end;", gateIf, StringComparison.Ordinal);
        Assert.True(closing > stop && closing < kill, "the gate branch must close before the taskkill fallback");
        // The gate opens straight into the branch holding the Exec: nothing between them can route around it.
        Assert.DoesNotContain("else", body[gateIf..stop]);
    }

    [Fact]
    public void GateNo_LogLineCoversMissingAndUnknown_AndNoGateMeansNoStop()
    {
        var iss = Iss();
        var supports = Routine(iss, "SupportsGracefulStop");
        // Missing exe or a failed version read both leave the gate False.
        Assert.Contains("Result := False;", supports);
        Assert.Contains("FileExists(Exe) and GetVersionNumbers(Exe, MS, LS)", supports);
        var describe = Routine(iss, "InstalledVersionText");
        Assert.Contains("'missing'", describe);
        Assert.Contains("'unknown'", describe);
        var body = Routine(iss, "PrepareToInstall");
        Assert.Contains("'PrepareToInstall: graceful stop gate=' + GateText + ' version=' + InstalledVersionText(Exe)", body);
        Assert.Contains("GateText := 'yes'", body);
        Assert.Contains("GateText := 'no'", body);
    }

    [Fact]
    public void PrepareToInstall_LogsTheThreeLinesCiAssertsOn()
    {
        var body = Routine(Iss(), "PrepareToInstall");
        Assert.Contains("'PrepareToInstall: graceful stop gate='", body);
        Assert.Contains("'PrepareToInstall: --stop exit '", body);
        Assert.Contains("'PrepareToInstall: taskkill exit '", body);
    }

    [Fact]
    public void UninstallRun_ExeEntriesSkipIfMissing_AndOrderIsStopKillRestore()
    {
        var lines = Section(Iss(), "UninstallRun").Split('\n').Where(l => l.StartsWith("Filename:")).ToArray();
        Assert.Equal(3, lines.Length);
        Assert.Contains("--stop", lines[0]);
        Assert.Contains("taskkill", lines[1]);
        Assert.Contains("--restore-auto", lines[2]);
        Assert.Contains("skipifdoesntexist", lines[0]);
        Assert.Contains("skipifdoesntexist", lines[2]);
        Assert.DoesNotContain("skipifdoesntexist", lines[1]);
        Assert.Contains("RunOnceId: \"StopApp\"", lines[0]);
        Assert.Contains("RunOnceId: \"KillApp\"", lines[1]);
        Assert.Contains("RunOnceId: \"RestoreFans\"", lines[2]);
    }

    [Fact]
    public void VersionPins_AreAllTheSameRelease()
    {
        var root = FindRepoRoot();
        var version = File.ReadAllText(Path.Combine(root, "VERSION")).Trim();
        var csproj = File.ReadAllText(Path.Combine(root, "LightweightAmdGpuFanControl", "LightweightAmdGpuFanControl.csproj"));
        Assert.Contains("<Version>" + version + "</Version>", csproj);
        var about = File.ReadAllText(Path.Combine(root, "LightweightAmdGpuFanControl", "Forms", "AboutForm.cs"));
        Assert.Contains("return \"" + version + "\";", about);
        Assert.Contains("#define MyAppVersion \"" + version + "\"", Iss());
        Assert.Equal("1.1.1", version);
        Assert.StartsWith("## [" + version + "]", SourceText.Read(Path.Combine(root, "CHANGELOG.md")).Split('\n').First(l => l.StartsWith("## [")));
    }
}

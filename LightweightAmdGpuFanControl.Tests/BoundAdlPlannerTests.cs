using LightweightAmdGpuFanControl.Drivers;
using Xunit;

public class BoundAdlPlannerTests
{
    private const string Root = @"C:\Windows\System32\DriverStore\FileRepository\";
    private const string Pkg = Root + @"u0205305.inf_amd64_x\";

    private static BoundAdlFacts Good() => new()
    {
        PresentAmdBoundInfs = new[] { "oem3.inf" },
        StoreInfPath = Pkg + "u0205305.inf",
        DriverStoreRoot = Root,
        AtiadlxxPath = Pkg + @"B026653\atiadlxx.dll",
        AdlxPath = Pkg + @"B026653\amdadlx64.dll",
        SystemAtiadlxxVersion = "32.0.32015.7029",
        SystemAdlxVersion = "32.0.32015.7029",
        BoundAtiadlxxVersion = "32.0.21045.16010",
        BoundAdlxVersion = "32.0.21045.16010",
        AtiadlxxTrusted = true,
        AdlxTrusted = true,
    };

    [Fact]
    public void Decide_NoDevices_NoAmdDisplay()
    {
        var f = Good();
        f.PresentAmdBoundInfs = Array.Empty<string>();
        var d = BoundAdlPlanner.Decide(f);
        Assert.Equal(BoundAdlKind.NoAmdDisplay, d.Kind);
        Assert.False(string.IsNullOrWhiteSpace(d.Reason));
    }

    [Fact]
    public void Decide_TwoDistinctInfs_MixedDriverLines()
    {
        var f = Good();
        f.PresentAmdBoundInfs = new[] { "oem3.inf", "oem4.inf" };
        Assert.Equal(BoundAdlKind.MixedDriverLines, BoundAdlPlanner.Decide(f).Kind);
    }

    [Fact]
    public void Decide_IgpuPlusDiscreteDifferentInfs_MixedDriverLines()
    {
        var f = Good();
        f.PresentAmdBoundInfs = new[] { "oem3.inf", "oem21.inf" };
        Assert.Equal(BoundAdlKind.MixedDriverLines, BoundAdlPlanner.Decide(f).Kind);
    }

    [Fact]
    public void Decide_TwoDevicesSameInf_Preloads()
    {
        var f = Good();
        f.PresentAmdBoundInfs = new[] { "oem3.inf", "OEM3.INF" };
        Assert.Equal(BoundAdlKind.Preload, BoundAdlPlanner.Decide(f).Kind);
    }

    [Fact]
    public void Decide_AdapterSideFailure_UnresolvedWithThatReason()
    {
        var f = Good();
        f.Reason = "SetupGetInfDriverStoreLocation failed";
        var d = BoundAdlPlanner.Decide(f);
        Assert.Equal(BoundAdlKind.Unresolved, d.Kind);
        Assert.Contains("SetupGetInfDriverStoreLocation failed", d.Reason);
    }

    [Fact]
    public void Decide_StoreOutsideDriverStore_Unresolved()
    {
        var f = Good();
        f.StoreInfPath = @"C:\Temp\evil\u0205305.inf";
        Assert.Equal(BoundAdlKind.Unresolved, BoundAdlPlanner.Decide(f).Kind);
        f.StoreInfPath = null;
        Assert.Equal(BoundAdlKind.Unresolved, BoundAdlPlanner.Decide(f).Kind);
    }

    [Fact]
    public void Decide_RelativeSegmentEscapesRoot_Unresolved()
    {
        var f = Good();
        f.StoreInfPath = Root + @"..\..\..\Temp\evilȅ305.inf";
        Assert.Equal(BoundAdlKind.Unresolved, BoundAdlPlanner.Decide(f).Kind);

        f = Good();
        f.AtiadlxxPath = Root + @"pkg\..\..\..\..\Temp\atiadlxx.dll";
        Assert.Equal(BoundAdlKind.Unresolved, BoundAdlPlanner.Decide(f).Kind);

        f = Good();
        f.AdlxPath = Root + @"pkg\.\amdadlx64.dll";
        Assert.Equal(BoundAdlKind.Unresolved, BoundAdlPlanner.Decide(f).Kind);
    }

    [Fact]
    public void Decide_DriverStoreRootWithoutTrailingSlash_StillRequiresBoundary()
    {
        var f = Good();
        f.DriverStoreRoot = Root.TrimEnd('\\');
        Assert.Equal(BoundAdlKind.Preload, BoundAdlPlanner.Decide(f).Kind);
        f.StoreInfPath = Root.TrimEnd('\\') + @"2\p\p.inf";
        Assert.Equal(BoundAdlKind.Unresolved, BoundAdlPlanner.Decide(f).Kind);
    }

    [Fact]
    public void Decide_PrefixCheckIsCaseInsensitive()
    {
        var f = Good();
        f.StoreInfPath = f.StoreInfPath!.ToUpperInvariant();
        Assert.Equal(BoundAdlKind.Preload, BoundAdlPlanner.Decide(f).Kind);
    }

    [Fact]
    public void Decide_AtiadlxxMissing_Unresolved()
    {
        var f = Good();
        f.AtiadlxxPath = null;
        var d = BoundAdlPlanner.Decide(f);
        Assert.Equal(BoundAdlKind.Unresolved, d.Kind);
        Assert.Contains("atiadlxx", d.Reason);
    }

    [Fact]
    public void Decide_AdlxMissingFromPackage_Unresolved()
    {
        var f = Good();
        f.AdlxPath = null;
        var d = BoundAdlPlanner.Decide(f);
        Assert.Equal(BoundAdlKind.Unresolved, d.Kind);
        Assert.Contains("amdadlx64", d.Reason);
    }

    [Fact]
    public void Decide_LibraryOutsideDriverStore_Unresolved()
    {
        var f = Good();
        f.AdlxPath = @"C:\Windows\System32\amdadlx64.dll";
        Assert.Equal(BoundAdlKind.Unresolved, BoundAdlPlanner.Decide(f).Kind);
    }

    [Fact]
    public void Decide_BoundVersionUnreadable_Unresolved()
    {
        var f = Good();
        f.BoundAdlxVersion = null;
        Assert.Equal(BoundAdlKind.Unresolved, BoundAdlPlanner.Decide(f).Kind);
        f = Good();
        f.BoundAtiadlxxVersion = "";
        Assert.Equal(BoundAdlKind.Unresolved, BoundAdlPlanner.Decide(f).Kind);
    }

    [Fact]
    public void Decide_VersionsEqual_NotNeeded()
    {
        var f = Good();
        f.SystemAtiadlxxVersion = f.BoundAtiadlxxVersion = "32.0.21045.16010";
        f.SystemAdlxVersion = " 32.0.21045.16010 ";
        f.BoundAdlxVersion = "32.0.21045.16010";
        f.AtiadlxxTrusted = false; // trust is irrelevant when no pre-load is needed
        var d = BoundAdlPlanner.Decide(f);
        Assert.Equal(BoundAdlKind.NotNeeded, d.Kind);
        Assert.Contains("32.0.21045.16010", d.Reason);
    }

    [Fact]
    public void Decide_NullSystemVersion_CountsAsNotEqual()
    {
        var f = Good();
        f.SystemAtiadlxxVersion = null;
        f.BoundAtiadlxxVersion = f.BoundAdlxVersion = f.SystemAdlxVersion;
        Assert.Equal(BoundAdlKind.Preload, BoundAdlPlanner.Decide(f).Kind);
    }

    [Fact]
    public void Decide_EitherUntrusted_TrustRefused()
    {
        var f = Good();
        f.AdlxTrusted = false;
        Assert.Equal(BoundAdlKind.TrustRefused, BoundAdlPlanner.Decide(f).Kind);
        f = Good();
        f.AtiadlxxTrusted = null;
        Assert.Equal(BoundAdlKind.TrustRefused, BoundAdlPlanner.Decide(f).Kind);
    }

    [Fact]
    public void Decide_Mismatch_Trusted_PreloadsBothPaths()
    {
        var f = Good();
        var d = BoundAdlPlanner.Decide(f);
        Assert.Equal(BoundAdlKind.Preload, d.Kind);
        Assert.Equal(f.AtiadlxxPath, d.AtiadlxxPath);
        Assert.Equal(f.AdlxPath, d.AdlxPath);
        Assert.Contains("32.0.21045.16010", d.Reason);
        Assert.Contains("32.0.32015.7029", d.Reason);
    }
}

using LightweightAmdGpuFanControl.Drivers;
using Xunit;

public class InfPackageFileTests
{
    private const string Inf = @"C:\Windows\System32\DriverStore\FileRepository\u0205305.inf_amd64_x\u0205305.inf";
    private const string Folder = @"C:\Windows\System32\DriverStore\FileRepository\u0205305.inf_amd64_x";

    private static string[] Lines(params string[] l) => l;

    private static void AssertRefused(InfPackageFileResult r)
    {
        Assert.Null(r.Path);
        Assert.False(string.IsNullOrWhiteSpace(r.Reason));
    }

    [Fact]
    public void Resolve_Amd64SectionWins()
    {
        var r = InfPackageFile.Resolve(Inf, Lines(
            "[SourceDisksNames]", @"1 = %D%,,,\WRONG",
            "[SourceDisksFiles]", "atiadlxx.dll = 1",
            "[SourceDisksNames.amd64]", @"1 = %D%,,,\B026653",
            "[SourceDisksFiles.amd64]", "atiadlxx.dll = 1"), "atiadlxx.dll");
        Assert.Equal(Folder + @"\B026653\atiadlxx.dll", r.Path);
        Assert.Null(r.Reason);
    }

    [Fact]
    public void Resolve_PlainSectionOnly_Resolves()
    {
        var r = InfPackageFile.Resolve(Inf, Lines(
            "[SourceDisksNames]", @"1 = %D%,,,\B026653",
            "[SourceDisksFiles]", "amdadlx64.dll = 1"), "amdadlx64.dll");
        Assert.EndsWith(@"\u0205305.inf_amd64_x\B026653\amdadlx64.dll", r.Path);
    }

    [Fact]
    public void Resolve_SubdirAppended()
    {
        var r = InfPackageFile.Resolve(Inf, Lines(
            "[SourceDisksNames.amd64]", @"1 = %D%,,,\B026653",
            "[SourceDisksFiles.amd64]", "atiadlxx.dll = 1,sub"), "atiadlxx.dll");
        Assert.EndsWith(@"\B026653\sub\atiadlxx.dll", r.Path);
    }

    [Fact]
    public void Resolve_CommentsAndQuotes_Stripped()
    {
        var r = InfPackageFile.Resolve(Inf, Lines(
            "; header comment",
            "[SourceDisksNames.amd64] ; names",
            "1 = \"D\",,,\"\\B026653\" ; c",
            "[SourceDisksFiles.amd64]",
            "\"atiadlxx.dll\" = 1 ; c"), "atiadlxx.dll");
        Assert.Equal(Folder + @"\B026653\atiadlxx.dll", r.Path);
    }

    [Fact]
    public void Resolve_DotDot_Refused()
    {
        AssertRefused(InfPackageFile.Resolve(Inf, Lines(
            "[SourceDisksNames]", @"1 = D,,,\..\evil",
            "[SourceDisksFiles]", "atiadlxx.dll = 1"), "atiadlxx.dll"));
    }

    [Fact]
    public void Resolve_Rooted_Refused()
    {
        AssertRefused(InfPackageFile.Resolve(Inf, Lines(
            "[SourceDisksNames]", @"1 = D,,,C:\x",
            "[SourceDisksFiles]", "atiadlxx.dll = 1"), "atiadlxx.dll"));
    }

    [Fact]
    public void Resolve_PercentTokenInPath_Refused()
    {
        AssertRefused(InfPackageFile.Resolve(Inf, Lines(
            "[SourceDisksNames]", @"1 = D,,,%Dir%",
            "[SourceDisksFiles]", "atiadlxx.dll = 1"), "atiadlxx.dll"));
    }

    [Fact]
    public void Resolve_IllegalChar_Refused()
    {
        AssertRefused(InfPackageFile.Resolve(Inf, Lines(
            "[SourceDisksNames]", @"1 = D,,,\B0<26",
            "[SourceDisksFiles]", "atiadlxx.dll = 1"), "atiadlxx.dll"));
    }

    [Fact]
    public void Resolve_Unc_Refused()
    {
        AssertRefused(InfPackageFile.Resolve(Inf, Lines(
            "[SourceDisksNames]", @"1 = D,,,\\server\share",
            "[SourceDisksFiles]", "atiadlxx.dll = 1"), "atiadlxx.dll"));
    }

    [Fact]
    public void Resolve_FileNotListed_Reason()
    {
        AssertRefused(InfPackageFile.Resolve(Inf, Lines(
            "[SourceDisksNames]", @"1 = D,,,\B",
            "[SourceDisksFiles]", "other.dll = 1"), "atiadlxx.dll"));
    }

    [Fact]
    public void Resolve_UnknownDiskId_Reason()
    {
        AssertRefused(InfPackageFile.Resolve(Inf, Lines(
            "[SourceDisksNames]", @"1 = D,,,\B",
            "[SourceDisksFiles]", "atiadlxx.dll = 2"), "atiadlxx.dll"));
    }

    [Fact]
    public void Resolve_NoSourceDisksNames_Reason()
    {
        AssertRefused(InfPackageFile.Resolve(Inf, Lines(
            "[SourceDisksFiles]", "atiadlxx.dll = 1"), "atiadlxx.dll"));
    }

    [Fact]
    public void Resolve_FileNameWithSlash_Refused()
    {
        var lines = Lines("[SourceDisksNames]", @"1 = D,,,\B", "[SourceDisksFiles]", "atiadlxx.dll = 1");
        AssertRefused(InfPackageFile.Resolve(Inf, lines, @"..\atiadlxx.dll"));
        AssertRefused(InfPackageFile.Resolve(Inf, lines, "a/atiadlxx.dll"));
        AssertRefused(InfPackageFile.Resolve(Inf, lines, ""));
    }

    [Fact]
    public void Resolve_NullLines_Reason()
    {
        AssertRefused(InfPackageFile.Resolve(Inf, null, "atiadlxx.dll"));
    }

    [Fact]
    public void Resolve_SectionNamesCaseInsensitive()
    {
        var r = InfPackageFile.Resolve(Inf, Lines(
            "[ sourcedisksnames.AMD64 ]", @"1 = D,,,\B026653",
            "[SOURCEDISKSFILES.Amd64]", "ATIADLXX.DLL = 1"), "atiadlxx.dll");
        Assert.EndsWith(@"\B026653\atiadlxx.dll", r.Path);
    }

    // The INF the RDNA1/2 package actually ships (u0205305.inf, driver 32.0.21045.16010): the disk path is ".\B026653",
    // with the names in the amd64 section and the files in the plain one. rc.1 refused the "." and never pre-loaded.
    private static readonly string[] FieldInf = Lines(
        "[SourceDisksNames.amd64]", @"1 = %DiskId%,,,.\B026653",
        "[SourceDisksFiles]", "atiadlxx.dll=1", "amdadlx64.dll=1", "atiadlxy.dll=1");

    [Theory]
    [InlineData("atiadlxx.dll")]
    [InlineData("amdadlx64.dll")]
    public void Resolve_FieldInfDotPrefixedDiskPath_Resolves(string file)
    {
        var r = InfPackageFile.Resolve(Inf, FieldInf, file);
        Assert.Null(r.Reason);
        Assert.Equal(Folder + @"\B026653\" + file, r.Path);
    }

    [Fact]
    public void Resolve_DotOnlyDiskPath_UsesPackageFolder()
    {
        var r = InfPackageFile.Resolve(Inf, Lines(
            "[SourceDisksNames]", "1 = D,,,.",
            "[SourceDisksFiles]", "atiadlxx.dll = 1"), "atiadlxx.dll");
        Assert.Equal(Folder + @"\atiadlxx.dll", r.Path);
    }

    [Fact]
    public void Resolve_DotSegmentInSubdir_IsNoOp()
    {
        var r = InfPackageFile.Resolve(Inf, Lines(
            "[SourceDisksNames.amd64]", @"1 = D,,,.\B026653",
            "[SourceDisksFiles.amd64]", @"atiadlxx.dll = 1,.\sub\."), "atiadlxx.dll");
        Assert.Equal(Folder + @"\B026653\sub\atiadlxx.dll", r.Path);
    }

    [Theory]
    [InlineData(@".\.\B026653", @"\B026653\atiadlxx.dll")]
    [InlineData(@"B026653\.\x", @"\B026653\x\atiadlxx.dll")]
    [InlineData(@"\.\B026653\.", @"\B026653\atiadlxx.dll")]
    public void Resolve_DotSegmentsAnywhere_AreNoOps(string diskPath, string expectedTail)
    {
        var r = InfPackageFile.Resolve(Inf, Lines(
            "[SourceDisksNames]", "1 = D,,," + diskPath,
            "[SourceDisksFiles]", "atiadlxx.dll = 1"), "atiadlxx.dll");
        Assert.Null(r.Reason);
        Assert.Equal(Folder + expectedTail, r.Path);
    }

    [Theory]
    [InlineData(@"..\B026653")]
    [InlineData(@"B026653\..\..\x")]
    [InlineData(@".\B026653\..")]
    public void Resolve_ParentSegment_StillRefused(string diskPath)
    {
        AssertRefused(InfPackageFile.Resolve(Inf, Lines(
            "[SourceDisksNames]", "1 = D,,," + diskPath,
            "[SourceDisksFiles]", "atiadlxx.dll = 1"), "atiadlxx.dll"));
    }

    [Fact]
    public void Resolve_DotDotAfterDot_StillRefused()
    {
        AssertRefused(InfPackageFile.Resolve(Inf, Lines(
            "[SourceDisksNames]", @"1 = D,,,.\..\evil",
            "[SourceDisksFiles]", "atiadlxx.dll = 1"), "atiadlxx.dll"));
        AssertRefused(InfPackageFile.Resolve(Inf, Lines(
            "[SourceDisksNames]", @"1 = D,,,.\B026653",
            "[SourceDisksFiles]", @"atiadlxx.dll = 1,.\.."), "atiadlxx.dll"));
    }

    [Fact]
    public void Resolve_EmptyDiskPath_UsesPackageFolder()
    {
        var r = InfPackageFile.Resolve(Inf, Lines(
            "[SourceDisksNames]", "1 = D,,,",
            "[SourceDisksFiles]", "atiadlxx.dll = 1"), "atiadlxx.dll");
        Assert.Equal(Folder + @"\atiadlxx.dll", r.Path);
    }
}

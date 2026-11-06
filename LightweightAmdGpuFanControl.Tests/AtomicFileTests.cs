using LightweightAmdGpuFanControl.IO;
using Xunit;

public class AtomicFileTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "atomicfile-" + Guid.NewGuid().ToString("N"));

    public AtomicFileTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
    }

    [Fact]
    public void AtomicFile_CreatesNew()
    {
        var p = Path.Combine(_dir, "a.json");
        Assert.True(AtomicFile.TryWriteAllText(p, "héllo", out var err));
        Assert.Null(err);
        Assert.Equal("héllo", File.ReadAllText(p));
        var bytes = File.ReadAllBytes(p);
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "BOM written");
    }

    [Fact]
    public void AtomicFile_ReplacesExisting()
    {
        var p = Path.Combine(_dir, "a.json");
        File.WriteAllText(p, "old");
        Assert.True(AtomicFile.TryWriteAllText(p, "new", out var err));
        Assert.Null(err);
        Assert.Equal("new", File.ReadAllText(p));
    }

    [Fact]
    public void AtomicFile_NoTmpLeftBehind()
    {
        var p = Path.Combine(_dir, "a.json");
        AtomicFile.TryWriteAllText(p, "one", out _);
        AtomicFile.TryWriteAllText(p, "two", out _);
        Assert.False(File.Exists(p + ".tmp"));
        Assert.Single(Directory.GetFiles(_dir));
    }

    [Fact]
    public void AtomicFile_MissingFolder_FalseWithError()
    {
        var p = Path.Combine(_dir, "missing", "a.json");
        Assert.False(AtomicFile.TryWriteAllText(p, "x", out var err));
        Assert.False(string.IsNullOrWhiteSpace(err));
    }

    [Fact]
    public void AtomicFile_NullPath_FalseWithError()
    {
        Assert.False(AtomicFile.TryWriteAllText(null!, "x", out var err));
        Assert.False(string.IsNullOrWhiteSpace(err));
    }

    [Fact]
    public void AtomicFile_NoFlush_StillWrites()
    {
        var p = Path.Combine(_dir, "a.json");
        Assert.True(AtomicFile.TryWriteAllText(p, "one", out var err, flushToDisk: false));
        Assert.Null(err);
        Assert.True(AtomicFile.TryWriteAllText(p, "two", out err, flushToDisk: false));
        Assert.Null(err);
        Assert.Equal("two", File.ReadAllText(p));
        Assert.False(File.Exists(p + ".tmp"));
    }
}

using System.Text;

namespace LightweightAmdGpuFanControl.IO;

/// <summary>Crash-safe small-file writes: temp file in the same folder, flush, then replace.</summary>
public static class AtomicFile
{
    /// <summary>
    /// Writes UTF-8 (no BOM) to <c>path + ".tmp"</c>, flushes it to disk, then replaces <paramref name="path"/>
    /// (<see cref="File.Replace(string,string,string?)"/> when it exists, <see cref="File.Move(string,string)"/> otherwise).
    /// With <paramref name="flushToDisk"/> false the OS-level flush is skipped (the stream is still flushed): for
    /// records whose loss is harmless. Never throws; on failure the temp file is removed best-effort and <paramref name="error"/> is set.
    /// </summary>
    public static bool TryWriteAllText(string path, string contents, out string? error, bool flushToDisk = true)
    {
        error = null;
        string tmp = (path ?? "") + ".tmp";
        try
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentException("path is empty");
            using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                var bytes = new UTF8Encoding(false).GetBytes(contents ?? "");
                fs.Write(bytes, 0, bytes.Length);
                fs.Flush();
                if (flushToDisk) fs.Flush(true);
            }
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            return false;
        }
    }
}

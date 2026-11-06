using System.Diagnostics;
using LightweightAmdGpuFanControl.Diagnostics;

namespace LightweightAmdGpuFanControl.Gpu.Native;

/// <summary>Enumerates the <c>ati*</c>/<c>amd*</c> modules loaded in this process and flags a mixed driver line.</summary>
internal static class LoadedAmdModules
{
    /// <summary>
    /// Every loaded AMD module, with <see cref="ProbeModule.MixedLine"/> set when it was loaded from outside the
    /// bound driver folder and differs in version from the bound copy of the same file. Never throws; on failure
    /// returns one placeholder entry naming the error.
    /// </summary>
    public static IReadOnlyList<ProbeModule> Snapshot()
    {
        var result = new List<ProbeModule>();
        try
        {
            string? boundFolder = null;
            var boundPath = BoundAdlPreloader.LastFacts?.AtiadlxxPath;
            if (boundPath != null)
            {
                int slash = boundPath.LastIndexOf('\\');
                if (slash > 0) boundFolder = boundPath.Substring(0, slash);
            }

            foreach (ProcessModule m in Process.GetCurrentProcess().Modules)
            {
                string name = m.ModuleName ?? "";
                if (!name.StartsWith("ati", StringComparison.OrdinalIgnoreCase)
                    && !name.StartsWith("amd", StringComparison.OrdinalIgnoreCase))
                    continue;

                string path = m.FileName ?? "";
                string? version = Win32.BinaryVersion(path);
                var module = new ProbeModule { Name = name, Path = path, Version = version };

                if (boundFolder != null && !path.StartsWith(boundFolder + "\\", StringComparison.OrdinalIgnoreCase))
                {
                    string boundFile = boundFolder + "\\" + name;
                    if (File.Exists(boundFile))
                    {
                        string? boundVersion = Win32.BinaryVersion(boundFile);
                        module.MixedLine = !string.Equals(boundVersion, version, StringComparison.Ordinal);
                    }
                }

                result.Add(module);
            }
        }
        catch (Exception ex)
        {
            result.Add(new ProbeModule { Name = "(module enumeration failed)", Path = ex.GetType().Name + ": " + ex.Message });
        }
        return result;
    }
}

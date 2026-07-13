using System.Collections.Generic;
using System.Diagnostics;

namespace LightweightAmdGpuFanControl.Services;

/// <summary>
/// Detects third-party GPU monitoring/tuning tools whose concurrent sensor polling can
/// contend with ours on the GPU's I2C/SMBus and destabilize the AMD driver. Adrenalin /
/// RadeonSoftware is intentionally NOT listed — it is always present (the baseline).
/// </summary>
internal static class MonitoringToolDetector
{
    // Process name (no .exe), display name.
    private static readonly (string Process, string Display)[] Known =
    {
        ("GPU-Z", "GPU-Z"),
        ("MSIAfterburner", "MSI Afterburner"),
        ("RTSS", "RivaTuner Statistics Server"),
        ("HWiNFO64", "HWiNFO"),
        ("HWiNFO32", "HWiNFO"),
        ("HWiNFO", "HWiNFO"),
        ("AIDA64", "AIDA64"),
        ("HWMonitor", "CPUID HWMonitor"),
        ("GPUTweakIII", "ASUS GPU Tweak"),
        ("GPUTweakII", "ASUS GPU Tweak"),
    };

    /// <summary>Distinct display names of detected tools (empty if none). Never throws.</summary>
    public static IReadOnlyList<string> GetActiveMonitors()
    {
        var found = new List<string>();
        var seen = new HashSet<string>();
        foreach (var (proc, display) in Known)
        {
            Process[] procs;
            try { procs = Process.GetProcessesByName(proc); }
            catch { continue; }
            try { if (procs.Length > 0 && seen.Add(display)) found.Add(display); }
            finally { foreach (var p in procs) { try { p.Dispose(); } catch { } } }
        }
        return found;
    }
}

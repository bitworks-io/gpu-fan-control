using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;
using LightweightAmdGpuFanControl.Drivers;
using LightweightAmdGpuFanControl.Services;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace LightweightAmdGpuFanControl.Gpu.Native;

/// <summary>
/// Loads the bound AMD display driver line's own atiadlxx.dll / amdadlx64.dll from the DriverStore
/// (AMD-signed only) before ADLX/ADL start, so the app talks to the same driver generation that
/// drives the display. Every step before the load is read-only; nothing is ever written. Never throws.
/// </summary>
internal static class BoundAdlPreloader
{
    private const string AtiadlxxName = "atiadlxx.dll";
    private const string AdlxName = "amdadlx64.dll";

    private static readonly object Gate = new();
    private static BoundAdlDecision? _cached;
    private static BoundAdlFacts? _lastFacts;

    private static readonly Regex OemInf = new(@"^oem\d+\.inf$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>Facts gathered by the first <see cref="Run"/>; null before it ran.</summary>
    public static BoundAdlFacts? LastFacts
    {
        get { lock (Gate) return _lastFacts; }
    }

    public static BoundAdlDecision Run(LogService log, bool enabled = true)
    {
        lock (Gate)
        {
            if (_cached != null)
                return _cached;

            BoundAdlDecision decision;
            try
            {
                decision = RunCore(log, enabled);
            }
            catch (Exception ex)
            {
                var reason = $"{ex.GetType().Name}: {ex.Message}";
                SafeLog(log, $"adl-preload: error {reason}");
                decision = new BoundAdlDecision { Kind = BoundAdlKind.Unresolved, Reason = reason };
            }

            _cached = decision;
            return decision;
        }
    }

    private static BoundAdlDecision RunCore(LogService log, bool enabled)
    {
        string windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var facts = new BoundAdlFacts
        {
            DriverStoreRoot = Path.Combine(windir, "System32", "DriverStore", "FileRepository") + "\\",
        };
        _lastFacts = facts;

        string sys32 = Path.Combine(windir, "System32");
        facts.SystemAtiadlxxVersion = Win32.BinaryVersion(Path.Combine(sys32, AtiadlxxName));
        facts.SystemAdlxVersion = Win32.BinaryVersion(Path.Combine(sys32, AdlxName));

        GatherBoundFacts(log, facts, windir);

        var decision = BoundAdlPlanner.Decide(facts);
        string versions = Describe(facts, decision);

        if (!enabled)
        {
            SafeLog(log, $"adl-preload: disabled (--no-preload); would be {decision.Kind} ({versions})");
            return decision;
        }

        SafeLog(log, $"adl-preload: {decision.Kind} ({versions})");

        if (decision.Kind == BoundAdlKind.Preload)
            return Preload(log, decision);

        HardenAdlxLoad(log, sys32);
        return decision;
    }

    private static string Describe(BoundAdlFacts f, BoundAdlDecision d) =>
        $"system32 {f.SystemAtiadlxxVersion ?? "n/a"}/{f.SystemAdlxVersion ?? "n/a"}, " +
        $"bound {f.BoundAtiadlxxVersion ?? "n/a"}/{f.BoundAdlxVersion ?? "n/a"}, {d.Reason}";

    // Steps 1-6: read-only fact gathering.
    private static void GatherBoundFacts(LogService log, BoundAdlFacts facts, string windir)
    {
        var infs = new List<string>();
        EnumerateAmdBoundInfs(facts, infs);
        facts.PresentAmdBoundInfs = infs;

        if (facts.Reason != null || infs.Count == 0)
            return;

        var distinct = new HashSet<string>(infs, StringComparer.OrdinalIgnoreCase);
        if (distinct.Count != 1)
            return;

        string inf = infs[0];
        var buf = new StringBuilder(1024);
        string infPath = Path.Combine(windir, "INF", inf);
        if (!Win32.SetupGetInfDriverStoreLocationW(infPath, IntPtr.Zero, null, buf, buf.Capacity, out _))
        {
            facts.Reason = $"could not resolve the driver package for {inf}: Win32 error {System.Runtime.InteropServices.Marshal.GetLastWin32Error()}";
            return;
        }
        facts.StoreInfPath = buf.ToString();

        string[] lines;
        try
        {
            lines = File.ReadAllLines(facts.StoreInfPath);
        }
        catch (Exception ex)
        {
            facts.Reason = $"could not read the package INF: {ex.GetType().Name}";
            return;
        }

        facts.AtiadlxxPath = ResolveFile(facts, lines, AtiadlxxName);
        facts.AdlxPath = ResolveFile(facts, lines, AdlxName);

        if (facts.AtiadlxxPath != null)
        {
            facts.BoundAtiadlxxVersion = Win32.BinaryVersion(facts.AtiadlxxPath);
            facts.AtiadlxxTrusted = IsTrusted(log, facts.AtiadlxxPath);
        }
        if (facts.AdlxPath != null)
        {
            facts.BoundAdlxVersion = Win32.BinaryVersion(facts.AdlxPath);
            facts.AdlxTrusted = IsTrusted(log, facts.AdlxPath);
        }
    }

    private static void EnumerateAmdBoundInfs(BoundAdlFacts facts, List<string> infs)
    {
        var classGuid = Win32.DisplayClassGuid;
        IntPtr set = Win32.SetupDiGetClassDevsW(ref classGuid, IntPtr.Zero, IntPtr.Zero, Win32.DIGCF_PRESENT);
        if (set == IntPtr.Zero || set == Win32.InvalidHandleValue)
        {
            facts.Reason = $"could not enumerate display devices: Win32 error {System.Runtime.InteropServices.Marshal.GetLastWin32Error()}";
            return;
        }

        try
        {
            for (uint i = 0; ; i++)
            {
                var data = new Win32.SP_DEVINFO_DATA { cbSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(Win32.SP_DEVINFO_DATA)) };
                if (!Win32.SetupDiEnumDeviceInfo(set, i, ref data))
                    break;

                var id = new StringBuilder(512);
                if (!Win32.SetupDiGetDeviceInstanceIdW(set, ref data, id, id.Capacity, out _))
                    continue;
                if (id.ToString().IndexOf("VEN_1002", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                string? inf = ReadInfPath(set, ref data);
                if (inf != null && OemInf.IsMatch(inf))
                    infs.Add(inf);
                else
                    facts.Reason = "AMD display device without a valid bound INF";
            }
        }
        finally
        {
            Win32.SetupDiDestroyDeviceInfoList(set);
        }
    }

    private static string? ReadInfPath(IntPtr set, ref Win32.SP_DEVINFO_DATA data)
    {
        IntPtr hkey = Win32.SetupDiOpenDevRegKey(set, ref data);
        if (hkey == IntPtr.Zero || hkey == Win32.InvalidHandleValue)
            return null;

        using var key = RegistryKey.FromHandle(new SafeRegistryHandle(hkey, ownsHandle: true));
        return key.GetValue("InfPath") as string;
    }

    private static string? ResolveFile(BoundAdlFacts facts, string[] lines, string fileName)
    {
        var result = InfPackageFile.Resolve(facts.StoreInfPath!, lines, fileName);
        if (result.Path == null)
        {
            facts.Reason ??= $"{fileName}: {result.Reason}";
            return null;
        }

        try
        {
            if (!File.Exists(result.Path))
            {
                facts.Reason ??= $"{fileName} does not exist in the driver package";
                return null;
            }
            var dir = Path.GetDirectoryName(result.Path);
            if (HasReparsePoint(result.Path) || (dir != null && HasReparsePoint(dir)))
            {
                facts.Reason ??= $"{fileName} or its folder is a reparse point";
                return null;
            }
        }
        catch (Exception ex)
        {
            facts.Reason ??= $"{fileName}: {ex.GetType().Name}";
            return null;
        }
        return result.Path;
    }

    private static bool HasReparsePoint(string path) =>
        (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;

    private static bool IsTrusted(LogService log, string path)
    {
        try
        {
            int rc = Win32.VerifyFileTrust(path);
            if (rc != 0)
            {
                SafeLog(log, $"adl-preload: WinVerifyTrust 0x{rc:X8} for {Path.GetFileName(path)}");
                return false;
            }

            var cert = X509Certificate.CreateFromSignedFile(path);
            return AmdSignerPin.Matches(cert.Subject, cert.Issuer);
        }
        catch (Exception ex)
        {
            SafeLog(log, $"adl-preload: signature check failed for {Path.GetFileName(path)}: {ex.GetType().Name}");
            return false;
        }
    }

    // Step 8: load the bound libraries, ADL first, then ADLX.
    private static BoundAdlDecision Preload(LogService log, BoundAdlDecision decision)
    {
        string? already = Win32.ModulePath(AtiadlxxName) ?? Win32.ModulePath(AdlxName);
        if (already != null)
        {
            SafeLog(log, $"adl-preload: modules already loaded from {already}; not loading the bound libraries");
            return new BoundAdlDecision { Kind = BoundAdlKind.Unresolved, Reason = "ADL modules were already loaded" };
        }

        const uint flags = Win32.LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | Win32.LOAD_LIBRARY_SEARCH_SYSTEM32;

        if (Win32.LoadLibraryExW(decision.AtiadlxxPath!, IntPtr.Zero, flags) == IntPtr.Zero)
        {
            SafeLog(log, $"adl-preload: could not load {decision.AtiadlxxPath}: Win32 error {System.Runtime.InteropServices.Marshal.GetLastWin32Error()}");
            return decision;
        }

        if (Win32.LoadLibraryExW(decision.AdlxPath!, IntPtr.Zero, flags) == IntPtr.Zero)
            SafeLog(log, $"adl-preload: could not load {decision.AdlxPath}: Win32 error {System.Runtime.InteropServices.Marshal.GetLastWin32Error()}");

        // Never freed: the process keeps the bound libraries for its lifetime.
        CheckLoaded(log, AtiadlxxName, decision.AtiadlxxPath!);
        CheckLoaded(log, AdlxName, decision.AdlxPath!);
        return decision;
    }

    private static void CheckLoaded(LogService log, string name, string expected)
    {
        string? loaded = Win32.ModulePath(name);
        SafeLog(log, $"adl-preload: {name} loaded from {loaded ?? "not loaded"}");
        if (loaded != null && !string.Equals(loaded, expected, StringComparison.OrdinalIgnoreCase))
            SafeLog(log, $"adl-preload: WARNING loaded {name} from {loaded}, expected {expected}");
    }

    // Step 9: pin ADLXHelper's later app-directory search to the System32 copy.
    private static void HardenAdlxLoad(LogService log, string sys32)
    {
        try
        {
            string path = Path.Combine(sys32, AdlxName);
            if (!File.Exists(path) || Win32.ModulePath(AdlxName) != null)
                return;
            if (Win32.LoadLibraryExW(path, IntPtr.Zero, Win32.LOAD_LIBRARY_SEARCH_SYSTEM32) == IntPtr.Zero)
                SafeLog(log, $"adl-preload: could not load {path}: Win32 error {System.Runtime.InteropServices.Marshal.GetLastWin32Error()}");
        }
        catch (Exception ex)
        {
            SafeLog(log, $"adl-preload: hardening failed: {ex.GetType().Name}");
        }
    }

    private static void SafeLog(LogService log, string message)
    {
        try { log.Log(message); } catch { /* logging must never break startup */ }
    }
}

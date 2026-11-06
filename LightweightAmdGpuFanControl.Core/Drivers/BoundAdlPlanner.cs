namespace LightweightAmdGpuFanControl.Drivers;

public enum BoundAdlKind { NoAmdDisplay, MixedDriverLines, Unresolved, NotNeeded, TrustRefused, Preload }

/// <summary>Facts gathered by the (Windows-only) adapters; the planner itself is pure.</summary>
public sealed class BoundAdlFacts
{
    public IReadOnlyList<string> PresentAmdBoundInfs { get; set; } = Array.Empty<string>();
    public string? StoreInfPath { get; set; }
    public string DriverStoreRoot { get; set; } = "";
    public string? AtiadlxxPath { get; set; }
    public string? AdlxPath { get; set; }
    public string? SystemAtiadlxxVersion { get; set; }
    public string? SystemAdlxVersion { get; set; }
    public string? BoundAtiadlxxVersion { get; set; }
    public string? BoundAdlxVersion { get; set; }
    public bool? AtiadlxxTrusted { get; set; }
    public bool? AdlxTrusted { get; set; }
    /// <summary>Adapter-side failure while gathering facts (null when none).</summary>
    public string? Reason { get; set; }
}

public sealed class BoundAdlDecision
{
    public BoundAdlKind Kind { get; init; }
    public string Reason { get; init; } = "";
    public string? AtiadlxxPath { get; init; }
    public string? AdlxPath { get; init; }
}

/// <summary>
/// Decides whether to pre-load the bound driver line's own atiadlxx.dll / amdadlx64.dll.
/// Rule order matters (plan section 3, Phase B). Pure; never throws.
/// </summary>
public static class BoundAdlPlanner
{
    public static BoundAdlDecision Decide(BoundAdlFacts f)
    {
        if (f == null) return Make(BoundAdlKind.Unresolved, "no facts supplied");

        var infs = f.PresentAmdBoundInfs ?? Array.Empty<string>();
        if (infs.Count == 0) return Make(BoundAdlKind.NoAmdDisplay, "no present AMD display device");

        var distinct = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var inf in infs) distinct.Add(inf ?? "");
        if (distinct.Count > 1)
            return Make(BoundAdlKind.MixedDriverLines, "AMD displays are bound to different INFs: " + string.Join(", ", distinct));

        if (f.Reason != null) return Make(BoundAdlKind.Unresolved, f.Reason);

        string root = f.DriverStoreRoot ?? "";
        if (root.Length > 0 && !root.EndsWith("\\", StringComparison.Ordinal)) root += "\\";

        if (string.IsNullOrEmpty(f.StoreInfPath) || !IsUnder(f.StoreInfPath!, root))
            return Make(BoundAdlKind.Unresolved, "driver package INF is not under the DriverStore");
        if (string.IsNullOrEmpty(f.AtiadlxxPath))
            return Make(BoundAdlKind.Unresolved, "atiadlxx.dll was not resolved in the driver package");
        if (!IsUnder(f.AtiadlxxPath!, root))
            return Make(BoundAdlKind.Unresolved, "atiadlxx.dll is not under the DriverStore");
        if (string.IsNullOrEmpty(f.AdlxPath))
            return Make(BoundAdlKind.Unresolved, "amdadlx64.dll was not resolved in the driver package");
        if (!IsUnder(f.AdlxPath!, root))
            return Make(BoundAdlKind.Unresolved, "amdadlx64.dll is not under the DriverStore");

        if (string.IsNullOrWhiteSpace(f.BoundAtiadlxxVersion) || string.IsNullOrWhiteSpace(f.BoundAdlxVersion))
            return Make(BoundAdlKind.Unresolved, "bound library version unreadable");

        string versions = $"system32 {f.SystemAtiadlxxVersion ?? "?"}/{f.SystemAdlxVersion ?? "?"}, bound {f.BoundAtiadlxxVersion!.Trim()}/{f.BoundAdlxVersion!.Trim()}";

        if (SameVersion(f.SystemAtiadlxxVersion, f.BoundAtiadlxxVersion) && SameVersion(f.SystemAdlxVersion, f.BoundAdlxVersion))
            return Make(BoundAdlKind.NotNeeded, "bound libraries match System32 (" + versions + ")");

        if (f.AtiadlxxTrusted != true || f.AdlxTrusted != true)
            return Make(BoundAdlKind.TrustRefused, "signature/signer check failed (" + versions + ")");

        return new BoundAdlDecision
        {
            Kind = BoundAdlKind.Preload,
            Reason = "bound libraries differ from System32 and are AMD-signed (" + versions + ")",
            AtiadlxxPath = f.AtiadlxxPath,
            AdlxPath = f.AdlxPath,
        };
    }

    private static bool SameVersion(string? system, string? bound) =>
        system != null && bound != null && string.Equals(system.Trim(), bound.Trim(), StringComparison.Ordinal);

    /// <summary>A prefix test alone would accept <c>root\..\..\elsewhere</c>; relative segments are refused outright.</summary>
    private static bool IsUnder(string path, string rootWithSlash) =>
        rootWithSlash.Length > 0
        && path.StartsWith(rootWithSlash, StringComparison.OrdinalIgnoreCase)
        && path.Split('\\').All(s => s != "." && s != "..");

    private static BoundAdlDecision Make(BoundAdlKind kind, string reason) => new() { Kind = kind, Reason = reason };
}

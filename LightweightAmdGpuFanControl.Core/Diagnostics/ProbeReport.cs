using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace LightweightAmdGpuFanControl.Diagnostics;

public sealed class ProbeGpu
{
    public int Index { get; set; }
    public string Name { get; set; } = "";
    public int? UniqueId { get; set; }
    public bool ManualFanTuningSupported { get; set; }
    public string? Error { get; set; }
    public int? SpeedMin { get; set; }
    public int? SpeedMax { get; set; }
    public bool? ZeroRpmSupported { get; set; }
    public bool? ZeroRpmEnabled { get; set; }
    public int[]? CurrentSpeeds { get; set; }
}

public sealed class ProbeAdlAdapter
{
    public int Index { get; set; }
    public int CapsResult { get; set; }
    public bool Supported { get; set; }
    public bool Enabled { get; set; }
    public int Version { get; set; }
    public string Family { get; set; } = "";
}

public sealed class ProbeModule
{
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public string? Version { get; set; }
    /// <summary>Loaded from outside the bound driver package while that package ships a same-named file.</summary>
    public bool MixedLine { get; set; }
}

/// <summary>Read-only capability report for <c>--probe</c>: pure model plus plain-text formatter.</summary>
public sealed class ProbeReport
{

    public string PreloadLine { get; set; } = "";
    public string AdlxInit { get; set; } = "";
    public string JournalLine { get; set; } = "";
    public List<ProbeGpu> Gpus { get; } = new();
    public List<ProbeAdlAdapter> AdlAdapters { get; } = new();
    public List<ProbeModule> Modules { get; } = new();

    public bool HasMixedModules => Modules.Any(m => m.MixedLine);

    /// <summary>0 when at least one GPU supports manual fan tuning, else 3.</summary>
    public int ExitCode => Gpus.Any(g => g.ManualFanTuningSupported) ? 0 : 3;

    public string Format()
    {
        var sb = new StringBuilder();
        sb.AppendLine("preload: " + PreloadLine);
        sb.AppendLine("adlx init: " + AdlxInit);
        sb.AppendLine("journal: " + JournalLine);
        sb.AppendLine($"adlx gpus: {Gpus.Count}");
        foreach (var g in Gpus)
        {
            sb.Append($"  gpu {g.Index} '{g.Name}'");
            if (g.UniqueId.HasValue) sb.Append($" uniqueId={g.UniqueId.Value.ToString(CultureInfo.InvariantCulture)}");
            sb.Append($" manualFanTuning={(g.ManualFanTuningSupported ? "yes" : "no")}");
            if (g.Error != null) sb.Append($" error={g.Error}");
            if (g.SpeedMin.HasValue || g.SpeedMax.HasValue) sb.Append($" speedRange={Fmt(g.SpeedMin)}-{Fmt(g.SpeedMax)}");
            if (g.ZeroRpmSupported.HasValue) sb.Append($" zeroRpmSupported={g.ZeroRpmSupported.Value}");
            if (g.ZeroRpmEnabled.HasValue) sb.Append($" zeroRpmEnabled={g.ZeroRpmEnabled.Value}");
            if (g.CurrentSpeeds != null)
                sb.Append(" speeds=" + string.Join(",", g.CurrentSpeeds.Select(s => s.ToString(CultureInfo.InvariantCulture))));
            sb.AppendLine();
        }
        sb.AppendLine($"adl adapters: {AdlAdapters.Count}");
        foreach (var a in AdlAdapters)
            sb.AppendLine($"  adapter {a.Index} capsRc={a.CapsResult} supported={a.Supported} enabled={a.Enabled} version={a.Version} family={a.Family}");
        sb.AppendLine($"modules: {Modules.Count}");
        foreach (var m in Modules)
            sb.AppendLine($"  {m.Name} {m.Path} version={m.Version ?? "unknown"}{(m.MixedLine ? " MIXED-LINE" : "")}");
        if (HasMixedModules) sb.AppendLine("WARNING: mixed AMD driver line loaded");
        return RedactUserProfile(sb.ToString());
    }

    private static string Fmt(int? v) => v.HasValue ? v.Value.ToString(CultureInfo.InvariantCulture) : "?";

    /// <summary>Replaces a leading <c>X:\Users\name</c> with <c>%USERPROFILE%</c> wherever a path starts a token.</summary>
    private static string RedactUserProfile(string text)
    {
        var rx = new Regex(@"[A-Za-z]:\\Users\\(?:[^\\\r\n]+(?=\\)|[^\\\s]+)", RegexOptions.IgnoreCase);
        return rx.Replace(text, "%USERPROFILE%");
    }
}

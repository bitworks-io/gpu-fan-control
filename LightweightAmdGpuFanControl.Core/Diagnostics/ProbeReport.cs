using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace LightweightAmdGpuFanControl.Diagnostics;

/// <summary>Result of the read-only ADLX IsAtFactory query for one GPU.</summary>
public enum TuningAtFactoryState { Yes, No, Unsupported, Error }

/// <summary>One baseline.json entry as the probe reports it.</summary>
public sealed class ProbeJournalEntry
{
    public string Name { get; set; } = "";
    public int? UniqueId { get; set; }
    public bool Dirty { get; set; }
    public bool? PreAppAtFactory { get; set; }
}

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
    public TuningAtFactoryState? TuningAtFactory { get; set; }
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
    public List<ProbeJournalEntry> JournalEntries { get; } = new();
    /// <summary>Refused or degraded GPUs recorded in the last run's status.json (default path only), one line each.</summary>
    public List<string> LastRunIssues { get; } = new();
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
            if (g.TuningAtFactory.HasValue) sb.Append($" tuningAtFactory={g.TuningAtFactory.Value.ToString().ToLowerInvariant()}");
            sb.AppendLine();
        }
        foreach (var e in JournalEntries)
        {
            string mode = e.PreAppAtFactory.HasValue ? (e.PreAppAtFactory.Value ? "true" : "false") : "unknown";
            sb.AppendLine($"journal entry '{e.Name}' dirty={(e.Dirty ? "yes" : "no")} preAppAtFactory={mode}");
            if (e.Dirty && !e.PreAppAtFactory.HasValue)
                sb.AppendLine($"  note: '{e.Name}' pre-app mode unknown (no factory flag recorded); --restore-auto re-applies the saved fan points. If the fan does not return to automatic, reset GPU tuning in AMD Software.");
            if (e.Dirty && e.PreAppAtFactory == true
                && Gpus.Any(g => g.TuningAtFactory == TuningAtFactoryState.No && string.Equals(g.Name, e.Name, StringComparison.Ordinal)
                    && (!e.UniqueId.HasValue || !g.UniqueId.HasValue || e.UniqueId.Value == g.UniqueId.Value)))
                sb.AppendLine($"WARNING: release incomplete for '{e.Name}': it was at factory before the app ran but is not at factory now. Run --restore-auto, or reset GPU tuning in AMD Software.");
        }
        foreach (var issue in LastRunIssues)
            sb.AppendLine("last run: " + issue);
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

using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LightweightAmdGpuFanControl.Recovery;

/// <summary>Identity of one ADLX GPU. The key is Name + UniqueId (two identical cards do not collide); StateCount is validated separately.</summary>
public sealed record GpuIdentity(string Name, int UniqueId, int StateCount)
{
    public string Key => $"{Name}|{UniqueId}";
}

/// <summary>Pre-app fan state: per-tuning-state speeds plus the zero-RPM flag.</summary>
public sealed record FanBaseline(IReadOnlyList<int> Speeds, bool ZeroRpmSupported, bool ZeroRpm);

public sealed class BaselineEntry
{
    public string Key { get; set; } = "";
    public string Name { get; set; } = "";
    public int UniqueId { get; set; }
    public int StateCount { get; set; }
    public int[]? Speeds { get; set; }
    public bool ZeroRpmSupported { get; set; }
    public bool ZeroRpm { get; set; }
    public bool Dirty { get; set; }
    public string Utc { get; set; } = "";
}

/// <summary>
/// The persisted record of pre-app fan baselines (baseline.json, schema 1, camelCase). Pure data and
/// serialization: file IO belongs to the app. Parsing never throws.
/// </summary>
public sealed class BaselineJournal
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
    };

    public int Schema { get; set; } = 1;
    public List<BaselineEntry> Entries { get; set; } = new();

    [JsonIgnore]
    public bool HasDirty => Entries.Any(e => e.Dirty);

    public static BaselineJournal Parse(string? json, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(json)) return new BaselineJournal();
        try
        {
            var j = JsonSerializer.Deserialize<BaselineJournal>(json!, JsonOptions);
            if (j == null)
            {
                error = "journal is empty or not an object";
                return new BaselineJournal();
            }
            if (j.Schema != 1)
            {
                error = $"unsupported journal schema {j.Schema}";
                return new BaselineJournal();
            }
            j.Entries = (j.Entries ?? new List<BaselineEntry>()).Where(e => e != null).ToList();
            return j;
        }
        catch (Exception ex)
        {
            error = "journal could not be parsed: " + ex.Message;
            return new BaselineJournal();
        }
    }

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    public BaselineEntry? Find(GpuIdentity id) =>
        Entries.FirstOrDefault(e => string.Equals(e.Key, id.Key, StringComparison.Ordinal));

    public void MarkDirty(GpuIdentity id, FanBaseline b, DateTime utc)
    {
        var e = Find(id);
        if (e == null)
        {
            e = new BaselineEntry();
            Entries.Add(e);
        }
        e.Key = id.Key;
        e.Name = id.Name;
        e.UniqueId = id.UniqueId;
        e.StateCount = id.StateCount;
        e.Speeds = b.Speeds.ToArray();
        e.ZeroRpmSupported = b.ZeroRpmSupported;
        e.ZeroRpm = b.ZeroRpm;
        e.Dirty = true;
        e.Utc = utc.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    }

    /// <summary>Clears the dirty flag; returns whether an entry existed.</summary>
    public bool MarkClean(GpuIdentity id)
    {
        var e = Find(id);
        if (e == null) return false;
        e.Dirty = false;
        return true;
    }

    /// <summary>Removes and returns every entry (dirty or clean) whose key is not among <paramref name="present"/>.</summary>
    public IReadOnlyList<BaselineEntry> PruneAbsent(IEnumerable<GpuIdentity> present) => PruneAbsent(present, includeDirty: true);

    /// <summary>
    /// Removes and returns entries whose key is not among <paramref name="present"/>. An empty <paramref name="present"/>
    /// (detection failed or nothing enumerated) removes nothing. With <paramref name="includeDirty"/> false a dirty
    /// entry is never removed, so a GPU that is temporarily absent keeps its recovery record.
    /// </summary>
    public IReadOnlyList<BaselineEntry> PruneAbsent(IEnumerable<GpuIdentity> present, bool includeDirty)
    {
        var keys = new HashSet<string>(present.Select(p => p.Key), StringComparer.Ordinal);
        if (keys.Count == 0) return new List<BaselineEntry>();
        var removed = Entries.Where(e => !keys.Contains(e.Key) && (includeDirty || !e.Dirty)).ToList();
        foreach (var e in removed) Entries.Remove(e);
        return removed;
    }
}

public enum BaselineSource { Captured, Journal }

/// <summary>Why <see cref="BaselinePlanner.Choose"/> picked its baseline. <see cref="Journal"/> is the only kind that restores from the journal.</summary>
public enum BaselineChoiceKind { NoEntry, Clean, Invalid, AlreadyRestored, Stale, Journal }

public sealed class BaselineChoice
{
    public BaselineChoiceKind Kind { get; init; }
    /// <summary>The GPU's state when the choice was made; kept so <see cref="DecisionLine"/> can show the deciding values.</summary>
    public FanBaseline? CapturedState { get; init; }
    /// <summary>The journal entry that was considered, if any.</summary>
    public BaselineEntry? Entry { get; init; }
    public FanBaseline Baseline { get; init; } = new(Array.Empty<int>(), false, false);
    public BaselineSource Source { get; init; }
    public string Reason { get; init; } = "";

    /// <summary>
    /// One log line with the values that decided the restore baseline:
    /// <c>journal: '&lt;name&gt;' dirty entry -&gt; &lt;decision&gt; (current=[..] zeroRpm=..; journal=[..] zeroRpm=..; &lt;reason&gt;)</c>.
    /// </summary>
    public string DecisionLine(string name)
    {
        string decision = Kind switch
        {
            BaselineChoiceKind.Journal => "app-written",
            BaselineChoiceKind.Stale => "stale",
            BaselineChoiceKind.AlreadyRestored => "already-restored",
            BaselineChoiceKind.Invalid => "invalid",
            BaselineChoiceKind.Clean => "clean",
            _ => "no-entry",
        };
        string current = CapturedState == null ? "current=[] zeroRpm=n/a"
            : "current=" + Speeds(CapturedState.Speeds) + " zeroRpm=" + Flag(CapturedState.ZeroRpmSupported, CapturedState.ZeroRpm);
        string journal = Entry == null ? "journal=[] zeroRpm=n/a"
            : "journal=" + Speeds(Entry.Speeds) + " zeroRpm=" + Flag(Entry.ZeroRpmSupported, Entry.ZeroRpm);
        return $"journal: '{name}' dirty entry -> {decision} ({current}; {journal}; {Reason})";
    }

    private static string Speeds(IEnumerable<int>? s) => "[" + string.Join(",", s ?? Array.Empty<int>()) + "]";
    private static string Flag(bool supported, bool on) => !supported ? "n/a" : on ? "on" : "off";
}

public static class BaselinePlanner
{
    /// <summary>
    /// Picks the baseline to restore to. The journal is used only when the entry is a dirty, valid,
    /// capability-matching record for this exact GPU AND the GPU's current state looks like one this app wrote;
    /// anything else keeps the captured state (v1.0 behaviour).
    /// </summary>
    public static BaselineChoice Choose(GpuIdentity id, FanBaseline captured, BaselineEntry? entry)
    {
        if (entry == null) return Captured(BaselineChoiceKind.NoEntry, "no journal entry");
        if (!entry.Dirty) return Captured(BaselineChoiceKind.Clean, "journal entry is clean");
        if (!string.Equals(entry.Key, id.Key, StringComparison.Ordinal)) return Captured(BaselineChoiceKind.Invalid, "journal entry identity differs");
        if (entry.StateCount != id.StateCount) return Captured(BaselineChoiceKind.Invalid, "journal tuning-state count differs");
        if (entry.Speeds == null || entry.Speeds.Length != id.StateCount)
            return Captured(BaselineChoiceKind.Invalid, "journal speed count does not match the tuning-state count");
        if (entry.Speeds.Any(s => s < 0 || s > 100)) return Captured(BaselineChoiceKind.Invalid, "journal speed outside 0..100");
        if (entry.ZeroRpmSupported != captured.ZeroRpmSupported) return Captured(BaselineChoiceKind.Invalid, "zero-RPM capability differs from the journal");

        if (captured.Speeds.SequenceEqual(entry.Speeds) && (!captured.ZeroRpmSupported || captured.ZeroRpm == entry.ZeroRpm))
            return Captured(BaselineChoiceKind.AlreadyRestored, "current state already equals the journaled pre-app state");
        if (!LooksAppWritten(captured))
            return Captured(BaselineChoiceKind.Stale, "current state does not look app-written; the journal entry is stale");

        return new BaselineChoice
        {
            Kind = BaselineChoiceKind.Journal,
            CapturedState = captured,
            Entry = entry,
            Baseline = new FanBaseline(entry.Speeds.ToArray(), entry.ZeroRpmSupported, entry.ZeroRpm),
            Source = BaselineSource.Journal,
            Reason = "recovering a fan state left by an unclean stop (journal " + entry.Utc + ")",
        };

        BaselineChoice Captured(BaselineChoiceKind kind, string why) => new()
        {
            Kind = kind,
            CapturedState = captured,
            Entry = entry,
            Baseline = captured,
            Source = BaselineSource.Captured,
            Reason = why + "; using the captured state",
        };
    }

    /// <summary>
    /// True when <paramref name="s"/> looks like a state this app wrote: one flat duty (within 1 point of read-back
    /// noise) inside the 20..85 clamp (19..86 with that tolerance), with zero-RPM off.
    /// </summary>
    public static bool LooksAppWritten(FanBaseline s)
    {
        if (s.Speeds.Count == 0) return false;
        int min = s.Speeds.Min(), max = s.Speeds.Max();
        if (max - min > 1) return false;
        if (min < 19 || max > 86) return false;
        if (s.ZeroRpmSupported && s.ZeroRpm) return false;
        return true;
    }
}

public static class RestoreAutoResult
{
    public static int ExitCode(int restored, int failed) =>
        failed > 0 ? 1 : restored > 0 ? 0 : 3;
}

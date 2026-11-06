using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace LightweightAmdGpuFanControl.Recovery;

/// <summary>Identity of one ADLX GPU. UniqueId is part of the key so two identical cards do not collide.</summary>
public sealed record GpuIdentity(string Name, int UniqueId, int StateCount)
{
    public string Key => $"{Name}|{UniqueId}|{StateCount}";
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
    public IReadOnlyList<BaselineEntry> PruneAbsent(IEnumerable<GpuIdentity> present)
    {
        var keys = new HashSet<string>(present.Select(p => p.Key), StringComparer.Ordinal);
        var removed = Entries.Where(e => !keys.Contains(e.Key)).ToList();
        Entries.RemoveAll(e => !keys.Contains(e.Key));
        return removed;
    }
}

public enum BaselineSource { Captured, Journal }

public sealed class BaselineChoice
{
    public FanBaseline Baseline { get; init; } = new(Array.Empty<int>(), false, false);
    public BaselineSource Source { get; init; }
    public string Reason { get; init; } = "";
}

public static class BaselinePlanner
{
    /// <summary>
    /// Picks the baseline to restore to. A journal entry is used only when it is a dirty, valid,
    /// capability-matching record for this exact GPU; anything else keeps the captured state (v1.0 behaviour).
    /// </summary>
    public static BaselineChoice Choose(GpuIdentity id, FanBaseline captured, BaselineEntry? entry)
    {
        if (entry == null) return Captured("no journal entry");
        if (!entry.Dirty) return Captured("journal entry is clean");
        if (!string.Equals(entry.Key, id.Key, StringComparison.Ordinal)) return Captured("journal entry identity differs");
        if (entry.Speeds == null || entry.Speeds.Length != id.StateCount)
            return Captured("journal speed count does not match the tuning-state count");
        if (entry.Speeds.Any(s => s < 0 || s > 100)) return Captured("journal speed outside 0..100");
        if (entry.ZeroRpmSupported != captured.ZeroRpmSupported) return Captured("zero-RPM capability differs from the journal");

        return new BaselineChoice
        {
            Baseline = new FanBaseline(entry.Speeds.ToArray(), entry.ZeroRpmSupported, entry.ZeroRpm),
            Source = BaselineSource.Journal,
            Reason = "recovering a fan state left by an unclean stop (journal " + entry.Utc + ")",
        };

        BaselineChoice Captured(string why) => new()
        {
            Baseline = captured,
            Source = BaselineSource.Captured,
            Reason = why + "; using the captured state",
        };
    }
}

public static class RestoreAutoResult
{
    public static int ExitCode(int restored, int failed) =>
        failed > 0 ? 1 : restored > 0 ? 0 : 3;
}

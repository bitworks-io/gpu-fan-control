using LightweightAmdGpuFanControl.IO;
using LightweightAmdGpuFanControl.Recovery;

namespace LightweightAmdGpuFanControl.Services;

/// <summary>
/// File-backed baseline journal (baseline.json). Thread-safe (one lock around every public member)
/// and never throws: any failure is logged and the caller carries on with v1.0 behaviour.
/// </summary>
internal sealed class BaselineJournalSession
{
    private readonly object _gate = new();
    private readonly string _path;
    private readonly LogService _log;
    private BaselineJournal _journal;

    private BaselineJournalSession(string path, LogService log, BaselineJournal journal)
    {
        _path = path;
        _log = log;
        _journal = journal;
    }

    /// <summary>
    /// Loads the journal at <paramref name="path"/>. A missing file, an IO error or an unparseable file
    /// all yield an empty journal; an unparseable file is moved aside to <c>.bad</c> (IO errors are not).
    /// </summary>
    public static BaselineJournalSession Open(string path, LogService log)
    {
        var journal = new BaselineJournal();
        try
        {
            if (File.Exists(path))
            {
                string text = File.ReadAllText(path);
                var parsed = BaselineJournal.Parse(text, out var err);
                if (err != null)
                {
                    Log(log, $"baseline.json unreadable ({err}); ignoring it");
                    try
                    {
                        if (File.Exists(path + ".bad")) File.Delete(path + ".bad");
                        File.Move(path, path + ".bad");
                    }
                    catch (Exception ex)
                    {
                        Log(log, $"baseline.json could not be set aside: {ex.GetType().Name}: {ex.Message}");
                    }
                }
                else
                {
                    journal = parsed;
                }
            }
        }
        catch (Exception ex)
        {
            Log(log, $"baseline.json could not be read ({ex.GetType().Name}: {ex.Message}); ignoring it");
            journal = new BaselineJournal();
        }
        return new BaselineJournalSession(path, log, journal);
    }

    public bool HasDirty
    {
        get
        {
            lock (_gate)
            {
                try { return _journal.HasDirty; }
                catch (Exception ex) { Log(_log, $"journal HasDirty failed: {ex.Message}"); return false; }
            }
        }
    }

    public bool IsDirty(string name, int uniqueId)
    {
        lock (_gate)
        {
            try
            {
                var e = _journal.Find(new GpuIdentity(name ?? "", uniqueId, 0));
                return e != null && e.Dirty;
            }
            catch (Exception ex) { Log(_log, $"journal IsDirty failed: {ex.Message}"); return false; }
        }
    }

    /// <summary>Picks the baseline to restore to; falls back to <paramref name="captured"/> on any failure.</summary>
    public BaselineChoice Choose(GpuIdentity id, FanBaseline captured)
    {
        lock (_gate)
        {
            try
            {
                var entry = _journal.Find(id);
                var choice = BaselinePlanner.Choose(id, captured, entry);
                if (entry != null && entry.Dirty)
                    Log(_log, choice.DecisionLine(id.Name));
                else if (entry != null)
                    Log(_log, $"ADLX GPU '{id.Name}': journal {choice.Kind}: {choice.Reason}");
                return choice;
            }
            catch (Exception ex)
            {
                Log(_log, $"journal choose failed for '{id.Name}': {ex.GetType().Name}: {ex.Message}");
                return new BaselineChoice { Baseline = captured, Source = BaselineSource.Captured, Kind = BaselineChoiceKind.NoEntry, Reason = "journal error; using the captured state" };
            }
        }
    }

    public void MarkDirty(GpuIdentity id, FanBaseline b)
    {
        lock (_gate)
        {
            try
            {
                _journal.MarkDirty(id, b, DateTime.UtcNow);
                Save("mark dirty", flushToDisk: true);
            }
            catch (Exception ex) { Log(_log, $"journal mark-dirty failed for '{id.Name}': {ex.GetType().Name}: {ex.Message}"); }
        }
    }

    public void MarkClean(GpuIdentity id)
    {
        lock (_gate)
        {
            try
            {
                if (_journal.MarkClean(id))
                    Save("mark clean", flushToDisk: false); // losing a "clean" is harmless; exit handlers are time-limited
            }
            catch (Exception ex) { Log(_log, $"journal mark-clean failed for '{id.Name}': {ex.GetType().Name}: {ex.Message}"); }
        }
    }

    public void PruneAbsent(IEnumerable<GpuIdentity> present, bool includeDirty)
    {
        lock (_gate)
        {
            try
            {
                var removed = _journal.PruneAbsent(present, includeDirty);
                foreach (var e in removed)
                    Log(_log, $"journal: dropping entry for '{e.Name}' (GPU not present, dirty={e.Dirty.ToString().ToLowerInvariant()})");
                if (removed.Count > 0)
                    Save("prune", flushToDisk: true);
            }
            catch (Exception ex) { Log(_log, $"journal prune failed: {ex.GetType().Name}: {ex.Message}"); }
        }
    }

    private void Save(string what, bool flushToDisk)
    {
        if (!AtomicFile.TryWriteAllText(_path, _journal.ToJson(), out var err, flushToDisk))
            Log(_log, $"baseline.json write failed ({what}): {err}");
    }

    private static void Log(LogService log, string message)
    {
        try { log.Log(message); } catch { /* logging must never break fan control */ }
    }
}

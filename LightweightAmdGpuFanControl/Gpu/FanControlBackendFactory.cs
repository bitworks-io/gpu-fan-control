using LightweightAmdGpuFanControl.Adlx;
using LightweightAmdGpuFanControl.Gpu.Adl;
using LightweightAmdGpuFanControl.Recovery;
using LightweightAmdGpuFanControl.Services;

namespace LightweightAmdGpuFanControl.Gpu;

/// <summary>
/// Builds the set of per-GPU fan-control channels under a single native session. Prefers the
/// modern ADLX family (full multi-GPU); falls back to legacy ADL (primary GPU only). The two
/// families are never mixed.
/// </summary>
public static class FanControlBackendFactory
{
    /// <summary>
    /// Detects supported AMD GPUs and returns a channel set that owns one native session and
    /// one channel per controllable GPU, or null when no supported GPU/driver is available.
    /// </summary>
    internal static FanControlChannelSet? CreateAll(LogService logService, BaselineJournalSession? journal = null)
    {
        var adlx = TryCreateAdlxSet(logService, journal);
        if (adlx is { HasChannels: true })
            return adlx;
        adlx?.Dispose();

        var adl = TryCreateAdlSet(logService);
        if (adl is { HasChannels: true })
            return adl;
        adl?.Dispose();

        return null;
    }

    private static FanControlChannelSet? TryCreateAdlxSet(LogService logService, BaselineJournalSession? journal)
    {
        var session = AdlxSession.TryCreate(logService);
        if (session == null)
            return null;

        var channels = new List<IFanControlBackend>();
        int count = session.GpuCount;
        logService.Log($"ADLX: {count} GPU(s)");
        for (int i = 0; i < count; i++)
        {
            var channel = AdlxFanControlBackend.TryCreateChannel(session, i, logService, journal);
            if (channel != null)
                channels.Add(channel);
        }

        PruneJournal(session, logService, journal);

        if (channels.Count == 0)
        {
            logService.Log("ADLX session opened but no GPU supports manual fan tuning.");
            session.Dispose();
            return null;
        }

        return new FanControlChannelSet(channels, session);
    }

    // Drops journal entries for GPUs that are no longer installed. Skipped entirely if enumeration fails.
    private static void PruneJournal(AdlxSession session, LogService logService, BaselineJournalSession? journal)
    {
        if (journal == null)
            return;
        try
        {
            var present = session.Monitor.GetAllGpus()
                .Select(g => new GpuIdentity(g.Name ?? "", g.UniqueId, 0))
                .ToList();
            journal.PruneAbsent(present, includeDirty: false); // the tray path never drops a dirty entry
        }
        catch (Exception ex)
        {
            logService.Log($"journal: GPU enumeration failed; not pruning ({ex.GetType().Name}: {ex.Message})");
        }
    }

    private static FanControlChannelSet? TryCreateAdlSet(LogService logService)
    {
        if (!AdlNativeApi.TryLoad(out var api, out var error) || api == null)
        {
            logService.Log($"ADL backend unavailable: {error}");
            return null;
        }

        if (api.AdapterNumberOfAdaptersGet(api.Context, out var adapterCount) != AdlNativeApi.Ok || adapterCount <= 0)
        {
            logService.Log("ADL backend found no adapters.");
            api.Dispose();
            return null;
        }

        for (int i = 0; i < adapterCount; i++)
        {
            var channel = AdlFanControlBackend.TryCreateForAdapter(api, i, logService);
            if (channel != null)
            {
                // Legacy ADL path controls only the primary supported adapter. Enumerating every
                // ADL "adapter" would risk driving the same physical GPU through duplicate display
                // adapters; multi-GPU is supported on the modern ADLX path instead.
                return new FanControlChannelSet(new[] { channel }, api);
            }
        }

        logService.Log("ADL adapters found but none support fan control.");
        api.Dispose();
        return null;
    }
}

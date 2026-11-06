using System.Threading;
using ADLXWrapper;
using LightweightAmdGpuFanControl.Adlx;
using LightweightAmdGpuFanControl.Cli;
using LightweightAmdGpuFanControl.Gpu.Native;
using LightweightAmdGpuFanControl.Recovery;

namespace LightweightAmdGpuFanControl.Services;

/// <summary>
/// <c>--restore-auto</c>: no UI and no self-test. Puts every GPU whose journal entry is dirty back on its
/// recorded pre-app fan state, then saves the journal. Writes nothing else.
/// </summary>
internal static class RestoreAutoRunner
{
    public static int Run(LogService log)
    {
        Mutex? mutex = null;
        try
        {
            if (!SingleInstance.TryAcquire(out mutex, 3000))
            {
                log.Log("restore-auto: the app is running; not restoring");
                return ExitCodes.AlreadyRunning;
            }

            var journal = BaselineJournalSession.Open(AppPaths.BaselinePath, log);
            if (!journal.HasDirty)
            {
                log.Log("restore-auto: nothing to restore");
                return ExitCodes.NothingToDo;
            }

            return RestoreDirty(log, journal);
        }
        catch (Exception ex)
        {
            try { log.Log("restore-auto: unexpected error", ex); } catch { }
            return ExitCodes.Error;
        }
        finally
        {
            SingleInstance.Release(mutex);
        }
    }

    private static int RestoreDirty(LogService log, BaselineJournalSession journal)
    {
        BoundAdlPreloader.Run(log);

        var initializer = new AdlxInitializer();
        var gpus = new List<GPU>();
        try
        {
            if (!initializer.Initialize(out var err))
            {
                log.Log($"restore-auto: ADLX unavailable: {err}");
                return ExitCodes.Error;
            }

            var services = initializer.GetSystemServices();
            var all = services.GetGPUs();
            for (int i = 0; i < all.Count; i++)
                gpus.Add(all[i]);

            var present = new List<GpuIdentity>();
            int restored = 0, failed = 0;
            foreach (var gpu in gpus)
            {
                string name = gpu.Name ?? "";
                int uid = gpu.UniqueId;
                present.Add(new GpuIdentity(name, uid, 0));
                if (!journal.IsDirty(name, uid))
                    continue;

                var fc = new FanController(services, log);
                if (!fc.Initialize(gpu, out var reason, journal))
                {
                    failed++;
                    log.Log($"restore-auto: '{name}' could not be initialized: {reason}");
                }
                else if (fc.Identity == null)
                {
                    // Baseline capture failed: nothing trustworthy to restore or to discard.
                    failed++;
                    log.Log($"restore-auto: '{name}' baseline unavailable; leaving its journal entry");
                }
                else if (fc.BaselineSource == BaselineSource.Journal)
                {
                    if (fc.TryRestoreAutomatic())
                    {
                        restored++;
                        log.Log($"restore-auto: restored '{name}'");
                    }
                    else
                    {
                        failed++;
                        log.Log($"restore-auto: restoring '{name}' failed");
                    }
                }
                else
                {
                    // Dirty entry present but not usable (invalid or mismatched; Choose logged why).
                    journal.MarkClean(fc.Identity);
                    log.Log($"restore-auto: discarded unusable journal entry for '{name}'");
                }
            }

            journal.PruneAbsent(present);
            return RestoreAutoResult.ExitCode(restored, failed);
        }
        finally
        {
            foreach (var gpu in gpus)
            {
                try { (gpu as IDisposable)?.Dispose(); } catch { }
            }
            try { initializer.Dispose(); } catch { }
        }
    }
}

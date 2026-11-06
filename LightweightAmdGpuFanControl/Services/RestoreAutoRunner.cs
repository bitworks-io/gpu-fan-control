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
            if (!SingleInstance.TryAcquire(out mutex, 15000) /* a process killed inside a driver call can take a while to die */)
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
                else if (fc.ChoiceKind == BaselineChoiceKind.Journal)
                {
                    if (fc.TryRestoreAutomatic())
                    {
                        restored++;
                        log.Log($"restore-auto: restored '{name}'");
                    }
                    else
                    {
                        // The entry stays dirty so a later run can retry.
                        failed++;
                        log.Log($"restore-auto: restoring '{name}' failed");
                    }
                }
                else if (fc.ChoiceKind == BaselineChoiceKind.AlreadyRestored || fc.ChoiceKind == BaselineChoiceKind.Stale)
                {
                    // The fan no longer carries the killed run's curve: nothing to restore, so the entry can go clean.
                    journal.MarkClean(fc.Identity);
                    log.Log($"restore-auto: '{name}' journal {fc.ChoiceKind}; nothing to restore, entry cleared");
                }
                else
                {
                    // Invalid: the entry cannot be trusted, and the fan may still carry the killed run's curve.
                    failed++;
                    log.Log($"restore-auto: '{name}' journal {fc.ChoiceKind}; entry left dirty, fan state unverified");
                }
            }

            journal.PruneAbsent(present, includeDirty: true);
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

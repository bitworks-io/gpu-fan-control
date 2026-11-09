using System.Threading;
using ADLXWrapper;
using LightweightAmdGpuFanControl.Adlx;
using LightweightAmdGpuFanControl.Cli;
using LightweightAmdGpuFanControl.Gpu.Native;
using LightweightAmdGpuFanControl.Recovery;

namespace LightweightAmdGpuFanControl.Services;

/// <summary>
/// <c>--reset-tuning-to-factory --gpu &lt;uniqueId&gt;</c>: no UI. Resets ALL of one named GPU's tuning (clock, VRAM,
/// power and fan) to factory settings through ADLX, not only the fan, and confirms it with IsAtFactory. It takes the
/// single-instance lock first, touches only the GPU whose uniqueId matches, and writes nothing when that GPU already
/// reads at factory. After a confirmed reset it clears that GPU's dirty legacy journal entry (no pre-app factory flag).
/// </summary>
internal static class FactoryResetCommand
{
    public static int Run(LogService log, int uniqueId)
    {
        Mutex? mutex = null;
        IDisposable? oneShot = null;
        try
        {
            oneShot = SingleInstance.MarkOneShot();
            if (!SingleInstance.TryAcquire(out mutex, 15000) /* a process killed inside a driver call can take a while to die */)
            {
                log.Log("reset-tuning: the app is running; not resetting");
                return FactoryResetOutcome.ExitCode(lockAcquired: false, gpuUsable: false, atFactoryBefore: null, resetSucceeded: false, atFactoryAfter: null);
            }

            return ResetGpu(log, uniqueId);
        }
        catch (Exception ex)
        {
            try { log.Log("reset-tuning: unexpected error", ex); } catch { }
            return ExitCodes.Error;
        }
        finally
        {
            SingleInstance.Release(mutex);
            oneShot?.Dispose();
        }
    }

    private static int ResetGpu(LogService log, int uniqueId)
    {
        BoundAdlPreloader.Run(log);

        var initializer = new AdlxInitializer();
        var gpus = new List<GPU>();
        SystemServices? services = null;
        GPUTuningService? tuning = null;
        try
        {
            if (!initializer.Initialize(out var err))
            {
                log.Log($"reset-tuning: ADLX unavailable: {err}");
                return ExitCodes.Error;
            }

            services = initializer.GetSystemServices();
            var all = services.GetGPUs();
            for (int i = 0; i < all.Count; i++)
                gpus.Add(all[i]);

            GPU? target = null;
            foreach (var g in gpus)
            {
                if (g.UniqueId == uniqueId)
                {
                    target = g;
                    break;
                }
            }

            if (target == null)
            {
                log.Log($"reset-tuning: no GPU with uniqueId {uniqueId}");
                return FactoryResetOutcome.ExitCode(true, false, null, false, null);
            }

            string name = target.Name ?? "";
            tuning = services.GetGPUTuningService();
            if (!tuning.IsManualFanTuningSupported(target))
            {
                log.Log($"reset-tuning: '{name}' has no manual fan tuning; not resetting");
                return FactoryResetOutcome.ExitCode(true, false, null, false, null);
            }

            bool? before = null;
            try { before = tuning.IsAtFactory(target); }
            catch (Exception ex) { log.Log($"reset-tuning: '{name}' IsAtFactory (before) failed: {ex.GetType().Name}: {ex.Message}"); }
            log.Log($"reset-tuning: '{name}' tuningAtFactory before={Fmt(before)}");
            if (before != false)
            {
                // Already at factory (3) or unreadable (1): nothing is written.
                return FactoryResetOutcome.ExitCode(true, true, before, false, null);
            }

            bool resetOk = false;
            bool? after = null;
            try
            {
                tuning.ResetToFactory(target);
                resetOk = true;
                after = tuning.IsAtFactory(target);
            }
            catch (Exception ex)
            {
                log.Log($"reset-tuning: '{name}' reset or confirmation failed: {ex.GetType().Name}: {ex.Message}");
            }
            log.Log($"reset-tuning: '{name}' tuningAtFactory after={Fmt(after)}");

            int code = FactoryResetOutcome.ExitCode(true, true, before, resetOk, after);
            bool confirmed = code == ExitCodes.Ok;
            if (confirmed)
            {
                var journal = BaselineJournalSession.Open(AppPaths.BaselinePath, log);
                if (journal.MarkCleanIfLegacy(new GpuIdentity(name, target.UniqueId, 0)))
                    log.Log($"reset-tuning: cleared the legacy journal entry for '{name}'");
            }
            else
            {
                log.Log($"reset-tuning: '{name}' is not confirmed at factory; journal untouched");
            }
            return code;
        }
        finally
        {
            SafeDispose(tuning);
            foreach (var g in gpus)
                SafeDispose(g);
            SafeDispose(services);
            try { initializer.Dispose(); } catch { }
        }
    }

    private static string Fmt(bool? v) => v.HasValue ? (v.Value ? "yes" : "no") : "unknown";

    private static void SafeDispose(IDisposable? d)
    {
        try { d?.Dispose(); } catch { /* teardown is best-effort */ }
    }
}

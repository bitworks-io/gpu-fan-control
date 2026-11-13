using System.Diagnostics;
using ADLXWrapper;
using LightweightAmdGpuFanControl.Adlx;
using LightweightAmdGpuFanControl.Automation;
using LightweightAmdGpuFanControl.Cli;
using LightweightAmdGpuFanControl.Diagnostics;
using LightweightAmdGpuFanControl.Drivers;
using LightweightAmdGpuFanControl.Gpu.Adl;
using LightweightAmdGpuFanControl.Gpu.Native;
using LightweightAmdGpuFanControl.IO;
using LightweightAmdGpuFanControl.Recovery;

namespace LightweightAmdGpuFanControl.Services;

/// <summary>Reads what a source can report into a <see cref="ProbeReport"/>. Deliberately has no other members.</summary>
internal interface IFanCapabilitySource
{
    void Read(ProbeReport report);
}

/// <summary>
/// <c>--probe</c>: a strictly read-only capability report. It never changes a fan, tuning or zero-RPM
/// setting and never touches the fan backends; it only queries ADLX/ADL capabilities and the loaded modules.
/// </summary>
internal static class CapabilityProbe
{
    public static int Run(LogService log, LaunchOptions options)
    {
        System.Threading.Mutex? mutex = null;
        IDisposable? oneShot = null;
        try
        {
            oneShot = SingleInstance.MarkOneShot();
            if (!SingleInstance.TryAcquire(out mutex, 0))
            {
                log.Log("probe: another instance is running; exiting");
                return ExitCodes.AlreadyRunning;
            }

            var report = new ProbeReport();

            var decision = BoundAdlPreloader.Run(log, enabled: !options.NoPreload);
            report.PreloadLine = "adl-preload: "
                + (options.NoPreload ? "disabled (--no-preload); would be " : "")
                + $"{decision.Kind} ({decision.Reason})";

            report.JournalLine = ReadJournalLine(report);
            ReadLastRunIssues(report);

            new AdlxCapabilitySource(log).Read(report);
            new AdlCapabilitySource(log).Read(report);
            ReadModules(report);

            var text = report.Format();
            foreach (var line in text.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries))
                log.Log("probe: " + line);

            if (!AtomicFile.TryWriteAllText(AppPaths.NewProbePath(), text, out var err))
                log.Log("probe: could not write the report file: " + err);

            return report.ExitCode;
        }
        catch (Exception ex)
        {
            log.Log("probe: unexpected failure.", ex);
            return ExitCodes.Error;
        }
        finally
        {
            SingleInstance.Release(mutex);
            oneShot?.Dispose();
        }
    }

    /// <summary>Refused or degraded GPUs recorded by the last automation run at the default status path. Read-only.</summary>
    private static void ReadLastRunIssues(ProbeReport report)
    {
        try
        {
            string path = AppPaths.DefaultStatusPath;
            if (!File.Exists(path))
                return;
            var status = AutomationStatus.TryParse(File.ReadAllText(path));
            if (status == null)
                return;
            foreach (var issue in status.GpuIssues)
                report.LastRunIssues.Add($"{issue.Gpu} {issue.State}: {issue.Message}");
            foreach (var issue in status.ReleaseIssues)
                report.LastRunIssues.Add(issue);
        }
        catch
        {
            // diagnostics only
        }
    }

    private static string ReadJournalLine(ProbeReport report)
    {
        try
        {
            string path = AppPaths.BaselinePath;
            if (!File.Exists(path))
                return "journal: none";

            var journal = BaselineJournal.Parse(File.ReadAllText(path), out var err);
            if (err != null)
                return $"journal: unreadable ({err})";

            foreach (var e in journal.Entries)
                report.JournalEntries.Add(new ProbeJournalEntry { Name = e.Name, UniqueId = e.UniqueId, Dirty = e.Dirty, PreAppAtFactory = e.PreAppAtFactory });

            if (journal.HasDirty)
                return "journal: DIRTY for " + string.Join(", ", journal.Entries.Where(e => e.Dirty).Select(e => e.Name));
            return $"journal: clean ({journal.Entries.Count} entries)";
        }
        catch (Exception ex)
        {
            return $"journal: unreadable ({ex.GetType().Name}: {ex.Message})";
        }
    }

    /// <summary>
    /// Adds every loaded <c>ati*</c>/<c>amd*</c> module not already listed. Called while each source still
    /// holds its native session (and again at the end), because a module that ADLX or ADL pulls in by bare
    /// name — the mixed-driver-line case E1 must catch — can be unloaded again when that session is torn down.
    /// </summary>
    internal static void ReadModules(ProbeReport report)
    {
        foreach (var module in LoadedAmdModules.Snapshot())
        {
            if (report.Modules.Any(x => string.Equals(x.Path, module.Path, StringComparison.OrdinalIgnoreCase)))
                continue;
            report.Modules.Add(module);
        }
    }
}

/// <summary>ADLX capability reads: GPU list, manual-fan-tuning support, tuning-at-factory state, ranges and current state. No setters.</summary>
internal sealed class AdlxCapabilitySource : IFanCapabilitySource
{
    private readonly LogService _log;

    public AdlxCapabilitySource(LogService log) => _log = log;

    public void Read(ProbeReport report)
    {
        var initializer = new AdlxInitializer();
        SystemServices? services = null;
        IReadOnlyList<GPU>? gpus = null;
        GPUTuningService? tuning = null;
        try
        {
            if (!initializer.Initialize(out var initError))
            {
                report.AdlxInit = initError ?? "failed";
                return;
            }
            report.AdlxInit = "ok";

            services = initializer.GetSystemServices();
            gpus = services.GetGPUs();
            tuning = services.GetGPUTuningService();

            for (int i = 0; i < gpus.Count; i++)
            {
                var gpu = gpus[i];
                var entry = new ProbeGpu { Index = i, Name = gpu.Name, UniqueId = gpu.UniqueId };
                report.Gpus.Add(entry);
                entry.TuningAtFactory = ReadTuningAtFactory(tuning, gpu); // read-only query; independent of fan support
                try
                {
                    entry.ManualFanTuningSupported = tuning.IsManualFanTuningSupported(gpu);
                    if (!entry.ManualFanTuningSupported)
                        continue;

                    // Read-only: constructing and disposing ManualFanTuning only queries and releases interfaces (external/ADLX/Samples/csharp/ADLXWrapper/ManualFanTuning.cs ctor, ADLXInterfaceWrapper.Dispose); no setter is called.
                    using var mft = tuning.GetManualFanTuning(gpu);
                    entry.SpeedMin = mft.SpeedRange.Min;
                    entry.SpeedMax = mft.SpeedRange.Max;
                    entry.ZeroRpmSupported = mft.SupportsZeroRPM;
                    if (mft.SupportsZeroRPM)
                        entry.ZeroRpmEnabled = mft.GetZeroRPMState();
                    entry.CurrentSpeeds = mft.GetCurrentState();
                }
                catch (Exception ex)
                {
                    entry.Error = $"{ex.GetType().Name}: {ex.Message}";
                }
            }

            CapabilityProbe.ReadModules(report); // while the ADLX session still holds its modules
        }
        catch (Exception ex)
        {
            if (string.IsNullOrEmpty(report.AdlxInit))
                report.AdlxInit = $"{ex.GetType().Name}: {ex.Message}";
            _log.Log("probe: ADLX read failed.", ex);
        }
        finally
        {
            SafeDispose(tuning);
            if (gpus != null)
                foreach (var g in gpus) SafeDispose(g);
            SafeDispose(services);
            SafeDispose(initializer);
        }
    }

    /// <summary>IsAtFactory is a read-only query. A driver without it (missing entry point or ADLX_NOT_SUPPORTED) is "unsupported".</summary>
    private static TuningAtFactoryState ReadTuningAtFactory(GPUTuningService tuning, GPU gpu)
    {
        try
        {
            return tuning.IsAtFactory(gpu) ? TuningAtFactoryState.Yes : TuningAtFactoryState.No;
        }
        catch (Exception ex) when (ex is EntryPointNotFoundException || ex is MissingMethodException
            || ex.Message.Contains("ADLX_NOT_SUPPORTED"))
        {
            return TuningAtFactoryState.Unsupported;
        }
        catch
        {
            return TuningAtFactoryState.Error;
        }
    }

    private static void SafeDispose(IDisposable? d)
    {
        try { d?.Dispose(); } catch { /* teardown is best-effort; the probe has already read what it needs */ }
    }
}

/// <summary>ADL capability reads: adapter Overdrive caps only.</summary>
internal sealed class AdlCapabilitySource : IFanCapabilitySource
{
    private readonly LogService _log;

    public AdlCapabilitySource(LogService log) => _log = log;

    public void Read(ProbeReport report)
    {
        if (!AdlNativeApi.TryLoad(out var api, out var err) || api == null)
        {
            _log.Log($"probe: adl unavailable: {err}");
            return;
        }

        try
        {
            if (api.AdapterNumberOfAdaptersGet(api.Context, out var count) != AdlNativeApi.Ok)
            {
                _log.Log("probe: adl adapter count unavailable");
                return;
            }

            for (int i = 0; i < count; i++)
            {
                int rc = api.OverdriveCaps(api.Context, i, out var supported, out var enabled, out var version);
                report.AdlAdapters.Add(new ProbeAdlAdapter
                {
                    Index = i,
                    CapsResult = rc,
                    Supported = supported != 0,
                    Enabled = enabled != 0,
                    Version = version,
                    Family = OverdriveRouting.Classify(version).ToString(),
                });
            }

            CapabilityProbe.ReadModules(report); // while the ADL handle is still open
        }
        catch (Exception ex)
        {
            _log.Log("probe: adl read failed.", ex);
        }
        finally
        {
            api.Dispose();
        }
    }
}

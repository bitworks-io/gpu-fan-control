using System.Threading;
using System.Windows.Forms;
using LightweightAmdGpuFanControl.Control;
using LightweightAmdGpuFanControl.Gpu;
using LightweightAmdGpuFanControl.Models;

namespace LightweightAmdGpuFanControl.Services;

/// <summary>
/// Polls each enabled GPU's temperature and applies the <see cref="FanControlPolicy"/> decision,
/// and restores automatic fan control on shutdown, pause, or repeated sensor-read failure.
///
/// Concurrency: a single <c>_controlLock</c> serializes every native backend call (startup tests,
/// poll adjustments, restores) because one shared ADLX/ADL context is not guaranteed thread-safe.
/// The UI never touches the backend — it reads <see cref="LatestStatuses"/>, a snapshot the control
/// thread publishes each tick.
/// </summary>
public class FanControlService
{
    private const int NormalPollIntervalMs = 2500;
    private const int ContentionPollIntervalMs = 6000; // back off when other GPU monitors are running
    private const int ReassertEveryTicks = 24;         // re-write the fan setpoint ~periodically even if unchanged
    private const int DetectEveryTicks = 12;           // process-scan cadence

    private readonly SettingsService _settingsService;
    private readonly LogService _logService;
    private readonly FanControlTestService _fanControlTest = new();
    private readonly FanControlPolicy _policy = new();
    private readonly object _controlLock = new();

    private System.Threading.Timer? _timer;
    private FanControlChannelSet? _channelSet;
    private List<ControlChannel> _channels = new();
    private NotifyIcon? _notifyIcon;
    private SynchronizationContext? _uiContext;
    private volatile bool _testsComplete;
    private volatile bool _paused;
    private volatile bool _disposed;
    private volatile IReadOnlyList<GpuStatus> _latestStatuses = Array.Empty<GpuStatus>();
    private int _pollGate;
    private int _pollCount;
    private volatile bool _contentionMode;
    private volatile bool _warnedContention;
    private volatile IReadOnlyList<string> _activeMonitors = Array.Empty<string>();

    public FanControlService(SettingsService settingsService, LogService logService)
    {
        _settingsService = settingsService;
        _logService = logService;
    }

    /// <summary>Live per-GPU snapshot for the UI. Empty until the first post-test poll.</summary>
    public IReadOnlyList<GpuStatus> LatestStatuses => _latestStatuses;

    /// <summary>True while control is paused (fans handed back to the driver's automatic curve).</summary>
    public bool IsPaused => _paused;

    /// <summary>True once at least one controllable GPU channel was found.</summary>
    public bool HasChannels => _channels.Count > 0;

    /// <summary>True while polling has backed off because another GPU monitoring tool is running.</summary>
    public bool IsContentionMode => _contentionMode;

    /// <summary>Display names of currently detected third-party GPU monitoring tools.</summary>
    public IReadOnlyList<string> ActiveMonitors => _activeMonitors;

    public void Start(SystrayApplicationContext context, NotifyIcon notifyIcon)
    {
        _uiContext = SynchronizationContext.Current;
        _notifyIcon = notifyIcon;
        _channelSet = FanControlBackendFactory.CreateAll(_logService);

        if (_channelSet == null || !_channelSet.HasChannels)
        {
            _logService.Log("No AMD fan-control backend could be initialized.");
            notifyIcon.BalloonTipClicked += (_, _) => OpenHelp();
            notifyIcon.ShowBalloonTip(5000, "Lightweight AMD GPU Fan Control",
                "Could not initialize AMD fan control. No supported AMD GPU, driver, or tuning API was found.", ToolTipIcon.Warning);
            MessageBox.Show(
                "No supported AMD GPU or compatible driver was found.\n\nEnsure AMD Software: Adrenalin Edition is installed and up to date, and that manual fan tuning is available. The app will keep running in the system tray.",
                "AMD fan control unavailable",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return;
        }

        _channels = _channelSet.Channels.Select(b => new ControlChannel(b)).ToList();
        PersistDetectedGpus();

        // Run the hardware self-test off the UI thread so app launch is not blocked. Tests run
        // sequentially under the control lock; poll adjustments are gated until tests complete so
        // no two threads touch the shared native context at once.
        System.Threading.Tasks.Task.Run(RunStartupTests);

        _timer = new System.Threading.Timer(_ => PollAndAdjust(), null, 1000, NormalPollIntervalMs);
    }

    /// <summary>Reconcile detected GPUs into settings so the UI has entries to toggle.</summary>
    private void PersistDetectedGpus()
    {
        var settings = _settingsService.Load();
        var detectedIds = _channels.Select(c => c.Backend.GpuId).ToList();
        settings.Gpus = GpuEnablement.Reconcile(detectedIds, settings).ToList();
        _settingsService.Save(settings);
    }

    private void RunStartupTests()
    {
        bool anyPassed = false;
        foreach (var channel in _channels)
        {
            if (_disposed) return;
            bool ok;
            lock (_controlLock)
            {
                if (_disposed) return;
                ok = _fanControlTest.RunTest(channel.Backend, _logService);
            }
            channel.TestPassed = ok;
            channel.TestCompleted = true;
            anyPassed |= ok;
        }

        _testsComplete = true;

        if (!anyPassed)
        {
            _logService.Log("Startup fan control test failed on all channels.");
            var icon = _notifyIcon;
            if (icon != null)
            {
                PostToUi(() =>
                {
                    icon.BalloonTipClicked += (_, _) => OpenHelp();
                    icon.ShowBalloonTip(8000, "Fan control may not work",
                        "The fan control test failed. AMD Adrenalin may need Manual Tuning enabled. Right-click → Help for steps.",
                        ToolTipIcon.Warning);
                });
            }
        }
    }

    private void PostToUi(Action action)
    {
        if (_uiContext != null)
            _uiContext.Post(_ => action(), null);
        else
            action();
    }

    private static void OpenHelp() => AppLinks.Open(AppLinks.FanHelpUrl);

    /// <summary>Pause control and hand every GPU back to the driver's automatic curve.</summary>
    public void Pause()
    {
        _paused = true;
        // Restore happens on the control thread (next poll) to keep native access serialized,
        // but also restore immediately under the lock so the change is felt without waiting a tick.
        lock (_controlLock)
        {
            foreach (var channel in _channels)
            {
                if (channel.ControlActive)
                {
                    channel.Backend.RestoreAutomaticFanControl();
                    channel.ControlActive = false;
                    channel.LastAppliedPercent = -1;
                }
            }
        }
    }

    /// <summary>Resume automatic control on enabled GPUs.</summary>
    public void Resume() => _paused = false;

    public void Stop()
    {
        _disposed = true;
        _timer?.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
        _timer?.Dispose();
        _timer = null;

        lock (_controlLock)
        {
            _channelSet?.Dispose(); // restores every channel to auto, then tears down the session
            _channelSet = null;
            // Clear channels under the same lock as the teardown so a concurrent RestoreAll()
            // cannot iterate backends whose native session has just been freed.
            _channels = new List<ControlChannel>();
        }
    }

    /// <summary>Hand every GPU back to the driver's automatic curve. Idempotent, best-effort.</summary>
    public void RestoreAll()
    {
        lock (_controlLock)
        {
            foreach (var channel in _channels)
            {
                try
                {
                    channel.Backend.RestoreAutomaticFanControl();
                    channel.ControlActive = false;
                    channel.LastAppliedPercent = -1;
                }
                catch (Exception ex)
                {
                    _logService.Log("Restore-to-automatic failed.", ex);
                }
            }
        }
    }

    private void PollAndAdjust()
    {
        if (_disposed || !_testsComplete)
            return;

        // Prevent overlapping timer callbacks from entering the control section concurrently.
        if (Interlocked.CompareExchange(ref _pollGate, 1, 0) != 0)
            return;

        try
        {
            // Detection runs inside the try/finally (like every other gated step) so a failure
            // here — e.g. _logService.Log throwing on a full disk — still releases _pollGate via
            // the finally below instead of wedging the poll loop shut forever.
            _pollCount++;
            if (_pollCount % DetectEveryTicks == 1)
                UpdateContentionState();

            var settings = _settingsService.Load();
            var detectedIds = _channels.Select(c => c.Backend.GpuId).ToList();
            var enabledIds = new HashSet<string>(GpuEnablement.Reconcile(detectedIds, settings)
                .Where(g => g.Enabled)
                .Select(g => g.GpuId));

            var statuses = new List<GpuStatus>(_channels.Count);

            lock (_controlLock)
            {
                if (_disposed)
                    return;

                foreach (var channel in _channels)
                {
                    bool enabled = enabledIds.Contains(channel.Backend.GpuId);
                    try
                    {
                        statuses.Add(AdjustChannel(channel, settings, enabled));
                    }
                    catch (Exception ex)
                    {
                        // Isolate per-GPU failures: one channel's native error must not starve
                        // the others (a throwing primary GPU could otherwise leave a secondary
                        // GPU uncontrolled and overheating).
                        _logService.Log($"Fan control failed for {channel.Backend.AdapterName}.", ex);
                        statuses.Add(new GpuStatus(channel.Backend.AdapterName, channel.Backend.GpuId,
                            enabled, false, null, null, null));
                    }
                }
            }

            _latestStatuses = statuses;
        }
        catch (Exception ex)
        {
            _logService.Log("Fan control poll error.", ex);
        }
        finally
        {
            Interlocked.Exchange(ref _pollGate, 0);
        }
    }

    /// <summary>
    /// Scans for third-party GPU monitoring tools and adjusts polling cadence to reduce
    /// sensor-bus contention. Pure process enumeration — must not be called under _controlLock.
    /// </summary>
    private void UpdateContentionState()
    {
        IReadOnlyList<string> monitors;
        try { monitors = MonitoringToolDetector.GetActiveMonitors(); }
        catch { return; }
        bool now = monitors.Count > 0;
        _activeMonitors = monitors;
        if (now == _contentionMode) return;

        _contentionMode = now;
        int interval = now ? ContentionPollIntervalMs : NormalPollIntervalMs;
        try { _timer?.Change(interval, interval); } catch { }
        _logService.Log(now
            ? $"GPU monitoring tools detected ({string.Join(", ", monitors)}); reduced fan-control polling to {interval} ms to limit sensor-bus contention."
            : "No third-party GPU monitors detected; restored normal fan-control polling.");

        if (now && !_warnedContention)
        {
            _warnedContention = true;
            var icon = _notifyIcon;
            if (icon != null)
            {
                var names = string.Join(", ", monitors);
                PostToUi(() =>
                {
                    icon.BalloonTipClicked += (_, _) => OpenHelp();
                    icon.ShowBalloonTip(8000, "GPU monitoring tools detected",
                        $"{names} is running. Running multiple GPU monitoring tools at once can destabilize the AMD driver (crashes / \"green screen\") due to sensor-bus contention. This app has reduced its polling; for best stability, avoid running several GPU monitors together.",
                        ToolTipIcon.Warning);
                });
            }
        }
    }

    /// <summary>Applies one channel's decision. Caller holds <c>_controlLock</c>.</summary>
    private GpuStatus AdjustChannel(ControlChannel channel, AppSettings settings, bool enabled)
    {
        var backend = channel.Backend;
        double? temp = null;
        int? fanPercent = null;
        int? fanRpm = null;

        // A disabled channel, or any channel while paused / whose test failed, is relinquished
        // to the driver's automatic curve exactly once, then left alone.
        if (_paused || !enabled || (channel.TestCompleted && !channel.TestPassed))
        {
            if (channel.ControlActive)
            {
                backend.RestoreAutomaticFanControl();
                channel.ControlActive = false;
                channel.LastAppliedPercent = -1;
            }
            var telemetry = SafeTelemetry(backend);
            return new GpuStatus(backend.AdapterName, backend.GpuId, enabled, false,
                SafeTemp(backend), telemetry.percent, telemetry.rpm);
        }

        temp = backend.GetTemperatureC();
        var decision = _policy.Decide(new TempReading(temp), settings, channel.State);

        if (decision.Action == FanAction.RelinquishToAuto)
        {
            backend.RestoreAutomaticFanControl();
            channel.ControlActive = false;
            channel.LastAppliedPercent = -1;
            _logService.Log($"{backend.AdapterName}: sensor read failed repeatedly; relinquished to driver automatic.");
        }
        else
        {
            bool reassert = (_pollCount % ReassertEveryTicks) == 0;
            if (decision.Percent != channel.LastAppliedPercent || reassert)
            {
                backend.SetFanPercent(decision.Percent);
                channel.LastAppliedPercent = decision.Percent;
            }
            channel.ControlActive = true;
            fanPercent = decision.Percent;
        }

        var tele = SafeTelemetry(backend);
        return new GpuStatus(backend.AdapterName, backend.GpuId, true, channel.ControlActive,
            temp, fanPercent ?? tele.percent, tele.rpm);
    }

    private static double? SafeTemp(IFanControlBackend backend)
    {
        try { return backend.GetTemperatureC(); }
        catch { return null; }
    }

    private static (int? percent, int? rpm) SafeTelemetry(IFanControlBackend backend)
    {
        try
        {
            var t = backend.GetFanTelemetry();
            return (t.ControlPercent ?? t.PhysicalPercent, t.PhysicalRpm);
        }
        catch { return (null, null); }
    }

    /// <summary>Per-GPU control channel: the backend plus its policy state and lifecycle flags.</summary>
    private sealed class ControlChannel
    {
        public ControlChannel(IFanControlBackend backend) => Backend = backend;

        public IFanControlBackend Backend { get; }
        public PolicyState State { get; } = new();
        public volatile bool TestCompleted;
        public volatile bool TestPassed;
        public bool ControlActive; // only mutated under _controlLock
        public int LastAppliedPercent = -1; // only mutated under _controlLock
    }
}

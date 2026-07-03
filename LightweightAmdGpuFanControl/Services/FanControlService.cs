using System.Threading;
using System.Windows.Forms;
using LightweightAmdGpuFanControl.Control;
using LightweightAmdGpuFanControl.Gpu;
using LightweightAmdGpuFanControl.Models;

namespace LightweightAmdGpuFanControl.Services;

/// <summary>
/// Polls GPU temperature and applies the <see cref="FanControlPolicy"/> decision each interval,
/// and restores automatic fan control on shutdown or repeated sensor-read failure.
/// </summary>
public class FanControlService
{
    private const int PollIntervalMs = 2500;

    private readonly SettingsService _settingsService;
    private readonly LogService _logService;
    private readonly FanControlTestService _fanControlTest = new();
    private readonly FanControlPolicy _policy = new();

    private System.Threading.Timer? _timer;
    private IFanControlBackend? _backend;
    private readonly PolicyState _state = new();
    private SynchronizationContext? _uiContext;
    private volatile bool _controlEnabled;
    private bool _disposed;

    public FanControlService(SettingsService settingsService, LogService logService)
    {
        _settingsService = settingsService;
        _logService = logService;
    }

    /// <summary>The active backend, exposed for live status readout in the UI (may be null).</summary>
    public IFanControlBackend? Backend => _backend;

    public void Start(SystrayApplicationContext context, NotifyIcon notifyIcon)
    {
        _uiContext = SynchronizationContext.Current;
        _backend = FanControlBackendFactory.Create(_logService);
        if (_backend == null)
        {
            _logService.Log("No AMD fan-control backend could be initialized.");
            notifyIcon.BalloonTipClicked += (_, _) => OpenHelp();
            notifyIcon.ShowBalloonTip(5000, "Lightweight AMD GPU Fan Control",
                "Could not initialize AMD fan control. No supported AMD GPU, driver, or tuning API was found.", ToolTipIcon.Warning);
            return;
        }

        // Run the hardware self-test off the UI thread so app launch is not blocked ~4s.
        var backend = _backend;
        System.Threading.Tasks.Task.Run(() =>
        {
            bool ok = _fanControlTest.RunTest(backend, _logService);
            _controlEnabled = ok;
            if (!ok)
            {
                _logService.Log("Startup fan control test failed.");
                PostToUi(() =>
                {
                    notifyIcon.BalloonTipClicked += (_, _) => OpenHelp();
                    notifyIcon.ShowBalloonTip(8000, "Fan control may not work",
                        "The fan control test failed. AMD Adrenalin may need Manual Tuning enabled. Right-click → Help for steps.",
                        ToolTipIcon.Warning);
                });
            }
        });

        _timer = new System.Threading.Timer(_ => PollAndAdjust(), null, 1000, PollIntervalMs);
    }

    private void PostToUi(Action action)
    {
        if (_uiContext != null)
            _uiContext.Post(_ => action(), null);
        else
            action();
    }

    private static void OpenHelp()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "https://help.argusmonitor.com/GPUfancontrolforAMDRadeon.html",
                UseShellExecute = true
            });
        }
        catch { }
    }

    public void Stop()
    {
        _disposed = true;
        _timer?.Change(System.Threading.Timeout.Infinite, System.Threading.Timeout.Infinite);
        _timer?.Dispose();
        _timer = null;
        RestoreAll();
        _backend?.Dispose();
        _backend = null;
    }

    /// <summary>Hand fan control back to the driver's automatic curve. Idempotent, best-effort.</summary>
    public void RestoreAll()
    {
        try
        {
            _backend?.RestoreAutomaticFanControl();
        }
        catch (Exception ex)
        {
            _logService.Log("Restore-to-automatic failed.", ex);
        }
    }

    private void PollAndAdjust()
    {
        if (_disposed || !_controlEnabled || _backend == null)
            return;

        try
        {
            var settings = _settingsService.Load();
            var temp = _backend.GetTemperatureC();
            var decision = _policy.Decide(new TempReading(temp), settings, _state);

            if (decision.Action == FanAction.RelinquishToAuto)
            {
                _backend.RestoreAutomaticFanControl();
                _logService.Log("Sensor read failed repeatedly; relinquished fan control to driver automatic.");
            }
            else
            {
                _backend.SetFanPercent(decision.Percent);
            }
        }
        catch (Exception ex)
        {
            _logService.Log("Fan control poll error.", ex);
        }
    }
}

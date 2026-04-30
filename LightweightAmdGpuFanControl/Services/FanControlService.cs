using System.Windows.Forms;
using LightweightAmdGpuFanControl.Gpu;
using LightweightAmdGpuFanControl.Models;

namespace LightweightAmdGpuFanControl.Services;

/// <summary>
/// Polls GPU temperature and adjusts fan speed (20-85%) to maintain target temp.
/// </summary>
public class FanControlService
{
    private const int PollIntervalMs = 2500;
    private const int MinFanPercent = 20;
    private const int MaxFanPercent = 85;
    private const int RampTempRange = 25; // Ramp from target to target+25°C

    private readonly SettingsService _settingsService;
    private readonly LogService _logService;
    private readonly FanControlTestService _fanControlTest = new();
    private System.Threading.Timer? _timer;
    private IFanControlBackend? _backend;
    private bool _controlEnabled;
    private bool _disposed;

    public FanControlService(SettingsService settingsService, LogService logService)
    {
        _settingsService = settingsService;
        _logService = logService;
    }

    public void Start(SystrayApplicationContext context, NotifyIcon notifyIcon)
    {
        _backend = FanControlBackendFactory.Create(_logService);
        if (_backend == null)
        {
            _logService.Log("No AMD fan-control backend could be initialized.");
            notifyIcon.BalloonTipClicked += (_, _) => OpenHelp();
            notifyIcon.ShowBalloonTip(5000, "Lightweight AMD GPU Fan Control",
                "Could not initialize AMD fan control. No supported AMD GPU, driver, or tuning API was found.", ToolTipIcon.Warning);
            return;
        }

        if (!_fanControlTest.RunTest(_backend, _logService))
        {
            _logService.Log("Startup fan control test failed.");
            notifyIcon.BalloonTipClicked += (_, _) => OpenHelp();
            notifyIcon.ShowBalloonTip(8000, "Fan control may not work",
                "The fan control test failed. AMD Adrenalin may need Manual Tuning enabled. Right-click → Help for steps.",
                ToolTipIcon.Warning);
            _controlEnabled = false;
        }
        else
        {
            _controlEnabled = true;
        }

        _timer = new System.Threading.Timer(_ => PollAndAdjust(), null, 1000, PollIntervalMs);
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
        _backend?.Dispose();
        _backend = null;
    }

    private void PollAndAdjust()
    {
        if (_disposed || !_controlEnabled || _backend == null)
            return;

        try
        {
            var settings = _settingsService.Load();
            var temp = _backend.GetTemperatureC();
            if (!temp.HasValue) return;

            var target = settings.TargetTempC;
            int percent;

            if (temp.Value < target)
            {
                percent = MinFanPercent;
            }
            else
            {
                var excess = temp.Value - target;
                percent = MinFanPercent + (int)(excess * (MaxFanPercent - MinFanPercent) / RampTempRange);
                percent = Math.Clamp(percent, MinFanPercent, MaxFanPercent);
            }

            _backend.SetFanPercent(percent);
        }
        catch (Exception ex)
        {
            _logService.Log("Fan control poll error.", ex);
        }
    }
}

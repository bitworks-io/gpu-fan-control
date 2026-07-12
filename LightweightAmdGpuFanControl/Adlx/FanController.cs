using System.Linq;
using ADLXWrapper;
using LightweightAmdGpuFanControl.Services;

namespace LightweightAmdGpuFanControl.Adlx;

/// <summary>
/// Controls AMD GPU fan speed via ADLX manual fan tuning.
/// </summary>
public sealed class FanController
{
    private const int MinFanPercent = 20;
    private const int MaxFanPercent = 85;

    private readonly SystemServices _systemServices;
    private readonly LogService? _log;
    private GPUTuningService? _tuningService;
    private ManualFanTuning? _manualFanTuning;
    private GPU? _gpu;
    private int _lastLoggedPercent = -1;

    public FanController(SystemServices systemServices, LogService? log = null)
    {
        _systemServices = systemServices;
        _log = log;
    }

    /// <summary>
    /// Checks if manual fan tuning is supported for the given GPU.
    /// </summary>
    public bool IsFanControlSupported(GPU? gpu = null)
    {
        gpu ??= _systemServices.GetGPUs().FirstOrDefault();
        if (gpu == null) return false;

        try
        {
            _tuningService ??= _systemServices.GetGPUTuningService();
            return _tuningService.IsManualFanTuningSupported(gpu);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Initializes fan control for the primary GPU. Call before SetFanPercent.
    /// </summary>
    /// <returns>True if manual fan tuning is available.</returns>
    public bool Initialize(GPU? gpu = null)
    {
        gpu ??= _systemServices.GetGPUs().FirstOrDefault();
        if (gpu == null) return false;

        try
        {
            _tuningService ??= _systemServices.GetGPUTuningService();
            if (!_tuningService.IsManualFanTuningSupported(gpu))
                return false;

            _manualFanTuning = _tuningService.GetManualFanTuning(gpu);
            _gpu = gpu;
            return _manualFanTuning != null;
        }
        catch
        {
            _manualFanTuning = null;
            _gpu = null;
            return false;
        }
    }

    /// <summary>
    /// Sets the fan speed as a percentage. Clamped to 20-85 for this app.
    /// </summary>
    /// <param name="percent">Fan speed percent.</param>
    public void SetFanPercent(int percent)
    {
        if (_manualFanTuning == null)
            throw new InvalidOperationException("Fan control not initialized. Call Initialize() first.");

        int requested = percent;
        percent = MathCompat.Clamp(percent, MinFanPercent, MaxFanPercent);

        try
        {
            if (_manualFanTuning.SupportsZeroRPM)
                _manualFanTuning.SetZeroRPM(false);

            // Percent-native fan curve: set every tuning-state point to `percent`%. This is
            // AMD's own sample approach (ADLXHelper::SetSpeed) and lowers symmetrically. We do
            // NOT use SetTargetFanSpeed: its argument must come from the target fan-speed RPM
            // range (GetTargetFanSpeedRange), not the tuning-state % range (SpeedRange) — the
            // prior code mixed those units, sending a percent value where RPM was expected.
            _manualFanTuning.SetFanTuningStates2(percent);

            LogApplied(requested, percent);
        }
        catch (Exception ex)
        {
            _log?.Log($"[fan] set FAILED requested={requested}% clamped={percent}%", ex);
            throw new InvalidOperationException($"Failed to set fan speed: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Diagnostic: one log line per CHANGED setpoint. Records capability flags, the tuning-state
    /// speed range, and the curve read back AFTER applying — proving the app is writing the
    /// (lowered) value each time. Pair with the live physical fan % shown in Preferences to tell
    /// an application bug (setpoint not written) from a driver behaviour (setpoint ignored).
    /// Never throws; diagnostics must not affect control.
    /// </summary>
    private void LogApplied(int requested, int clamped)
    {
        if (_log == null || clamped == _lastLoggedPercent)
            return;
        _lastLoggedPercent = clamped;
        try
        {
            var state = _manualFanTuning!.GetCurrentState();
            var readback = state is { Length: > 0 } ? string.Join(",", state) : "n/a";
            _log.Log($"[fan] applied requested={requested}% clamped={clamped}% " +
                     $"zeroRpmSupported={_manualFanTuning.SupportsZeroRPM} " +
                     $"targetSpeedSupported={_manualFanTuning.SupportsTargetFanSpeed} " +
                     $"tuningSpeedRange=[{_manualFanTuning.SpeedRange.Min},{_manualFanTuning.SpeedRange.Max}] " +
                     $"curveAfter=[{readback}]");
        }
        catch
        {
            // Diagnostics must never affect control.
        }
    }

    /// <summary>
    /// Gets the current fan speed setting (from manual tuning state).
    /// </summary>
    public int[]? GetCurrentFanState()
    {
        try
        {
            return _manualFanTuning?.GetCurrentState();
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Returns the fan to the driver's automatic curve by resetting the manual fan tuning to the
    /// state captured when this controller was initialized (i.e. the pre-app curve). Best-effort.
    /// </summary>
    public void RestoreAutomatic()
    {
        try
        {
            _manualFanTuning?.Reset();
        }
        catch
        {
            // Best-effort: nothing else we can do if the driver rejects the reset.
        }
    }
}

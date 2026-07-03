using System.Linq;
using ADLXWrapper;

namespace LightweightAmdGpuFanControl.Adlx;

/// <summary>
/// Controls AMD GPU fan speed via ADLX manual fan tuning.
/// </summary>
public sealed class FanController
{
    private const int MinFanPercent = 20;
    private const int MaxFanPercent = 85;

    private readonly SystemServices _systemServices;
    private GPUTuningService? _tuningService;
    private ManualFanTuning? _manualFanTuning;
    private GPU? _gpu;

    public FanController(SystemServices systemServices)
    {
        _systemServices = systemServices;
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

        percent = Math.Clamp(percent, MinFanPercent, MaxFanPercent);

        try
        {
            if (_manualFanTuning.SupportsZeroRPM)
                _manualFanTuning.SetZeroRPM(false);

            if (_manualFanTuning.SupportsTargetFanSpeed && _manualFanTuning.SpeedRange.Max > 0)
            {
                var rpm = (int)(_manualFanTuning.SpeedRange.Max * (percent / 100.0));
                _manualFanTuning.SetTargetFanSpeed(rpm);
            }
            else
            {
                _manualFanTuning.SetFanTuningStates2(percent);
            }
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException($"Failed to set fan speed: {ex.Message}", ex);
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

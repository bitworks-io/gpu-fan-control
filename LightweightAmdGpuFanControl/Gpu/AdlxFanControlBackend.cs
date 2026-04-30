using LightweightAmdGpuFanControl.Adlx;
using LightweightAmdGpuFanControl.Services;

namespace LightweightAmdGpuFanControl.Gpu;

public sealed class AdlxFanControlBackend : IFanControlBackend
{
    private const int MinFanPercent = 20;
    private const int MaxFanPercent = 85;

    private readonly LogService _logService;
    private AdlxInitializer? _adlx;
    private FanController? _fanController;
    private GpuMonitor? _gpuMonitor;
    private ADLXWrapper.GPU? _gpu;

    public AdlxFanControlBackend(LogService logService)
    {
        _logService = logService;
    }

    public string BackendName => "ADLX";
    public string AdapterName => _gpu?.Name ?? "AMD GPU";

    public bool Initialize()
    {
        try
        {
            _adlx = new AdlxInitializer();
            if (!_adlx.Initialize())
                return false;

            var systemServices = _adlx.GetSystemServices();
            _gpuMonitor = new GpuMonitor(systemServices);
            _fanController = new FanController(systemServices);
            _gpu = _gpuMonitor.GetPrimaryGpu();

            if (_gpu == null || !_fanController.Initialize(_gpu))
                return false;

            _logService.Log($"Selected ADLX backend for {AdapterName}.");
            return true;
        }
        catch (Exception ex)
        {
            _logService.Log("ADLX backend initialization failed.", ex);
            Dispose();
            return false;
        }
    }

    public double? GetTemperatureC() => _gpuMonitor?.GetGpuTemperature(_gpu);

    public FanTelemetry GetFanTelemetry()
    {
        int? controlPercent = null;
        var state = _fanController?.GetCurrentFanState();
        if (state is { Length: > 0 })
            controlPercent = (int)Math.Round(state.Average());

        int? physicalPercent = null;
        int? physicalRpm = null;
        var fanSpeed = _gpuMonitor?.GetFanSpeed(_gpu);
        if (fanSpeed.HasValue)
        {
            if (fanSpeed.Value is >= 0 and <= 100)
                physicalPercent = fanSpeed.Value;
            else if (fanSpeed.Value > 100)
                physicalRpm = fanSpeed.Value;
        }

        return new FanTelemetry(controlPercent, physicalPercent, physicalRpm);
    }

    public void DisableZeroRpm()
    {
        // Any non-zero fan target forces Zero RPM off in the ADLX wrapper when supported.
        SetFanPercent(MinFanPercent);
    }

    public void SetFanPercent(int percent)
    {
        if (_fanController == null)
            throw new InvalidOperationException("ADLX fan controller is not initialized.");

        _fanController.SetFanPercent(Math.Clamp(percent, MinFanPercent, MaxFanPercent));
    }

    public void Dispose()
    {
        _adlx?.Dispose();
        _adlx = null;
        _fanController = null;
        _gpuMonitor = null;
        _gpu = null;
    }
}

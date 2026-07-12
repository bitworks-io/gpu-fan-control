using ADLXWrapper;
using LightweightAmdGpuFanControl.Adlx;
using LightweightAmdGpuFanControl.Services;

namespace LightweightAmdGpuFanControl.Gpu;

/// <summary>
/// A per-GPU fan-control channel that borrows a shared <see cref="AdlxSession"/>. Each channel
/// owns only its own <see cref="FanController"/> (which captures that GPU's pre-app fan state);
/// it never disposes the shared session. Restore-to-auto is therefore per-GPU correct.
/// </summary>
public sealed class AdlxFanControlBackend : IFanControlBackend
{
    private const int MinFanPercent = 20;
    private const int MaxFanPercent = 85;

    private readonly LogService _logService;
    private readonly AdlxSession _session;
    private readonly GPU _gpu;
    private readonly int _gpuIndex;
    private readonly FanController _fanController;

    private AdlxFanControlBackend(AdlxSession session, GPU gpu, int gpuIndex, FanController fanController, LogService logService)
    {
        _session = session;
        _gpu = gpu;
        _gpuIndex = gpuIndex;
        _fanController = fanController;
        _logService = logService;
    }

    /// <summary>
    /// Builds a channel for the GPU at <paramref name="gpuIndex"/> using the shared session.
    /// Returns null when that GPU has no manual fan tuning (skip it, keep the others).
    /// </summary>
    internal static AdlxFanControlBackend? TryCreateChannel(AdlxSession session, int gpuIndex, LogService logService)
    {
        try
        {
            var gpu = session.Monitor.GetGpuAtIndex(gpuIndex);
            if (gpu == null)
                return null;

            var fanController = new FanController(session.SystemServices, logService);
            if (!fanController.Initialize(gpu))
                return null;

            var backend = new AdlxFanControlBackend(session, gpu, gpuIndex, fanController, logService);
            logService.Log($"Created ADLX channel for {backend.AdapterName} (GPU index {gpuIndex}).");
            return backend;
        }
        catch (Exception ex)
        {
            logService.Log($"ADLX channel creation failed for GPU index {gpuIndex}.", ex);
            return null;
        }
    }

    public string BackendName => "ADLX";
    public string AdapterName => _gpu.Name is { Length: > 0 } name ? name : $"AMD GPU {_gpuIndex}";
    public string GpuId => _gpu.Name is { Length: > 0 } name ? $"ADLX:{name}" : $"ADLX:GPU{_gpuIndex}";

    // Channels are pre-initialized by TryCreateChannel; this is a no-op for interface compatibility.
    public bool Initialize() => true;

    public double? GetTemperatureC() => _session.Monitor.GetGpuTemperature(_gpu);

    public FanTelemetry GetFanTelemetry()
    {
        int? controlPercent = null;
        var state = _fanController.GetCurrentFanState();
        if (state is { Length: > 0 })
            controlPercent = (int)Math.Round(state.Average());

        int? physicalPercent = null;
        int? physicalRpm = null;
        var fanSpeed = _session.Monitor.GetFanSpeed(_gpu);
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
        _fanController.SetFanPercent(MathCompat.Clamp(percent, MinFanPercent, MaxFanPercent));
    }

    public void RestoreAutomaticFanControl()
    {
        try
        {
            _fanController.RestoreAutomatic();
        }
        catch (Exception ex)
        {
            _logService.Log($"ADLX restore-to-automatic failed for {AdapterName}.", ex);
        }
    }

    public void Dispose()
    {
        // Return this GPU's fan to the driver's automatic curve. Does NOT dispose the shared
        // session — the owning FanControlChannelSet tears the session down after all channels.
        RestoreAutomaticFanControl();
    }
}

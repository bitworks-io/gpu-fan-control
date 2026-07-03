namespace LightweightAmdGpuFanControl.Gpu;

public interface IFanControlBackend : IDisposable
{
    string BackendName { get; }
    string AdapterName { get; }

    /// <summary>Stable identifier for the controlled GPU, used to persist per-GPU enablement.</summary>
    string GpuId { get; }

    bool Initialize();
    double? GetTemperatureC();
    FanTelemetry GetFanTelemetry();
    void DisableZeroRpm();
    void SetFanPercent(int percent);

    /// <summary>
    /// Hand fan control back to the driver's automatic curve. Best-effort and must not throw;
    /// called on exit, on repeated sensor-read failure, and when control is paused.
    /// </summary>
    void RestoreAutomaticFanControl();
}

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

    /// <summary>
    /// As <see cref="RestoreAutomaticFanControl"/>, reporting whether the release was applied (and confirmed where
    /// the backend can confirm it). Must not throw.
    /// </summary>
    bool TryRestoreAutomaticFanControl();

    /// <summary>The action the most recent release was planned as; null when the backend has no plan.</summary>
    LightweightAmdGpuFanControl.Recovery.ReleaseAction? LastReleaseAction { get; }
}

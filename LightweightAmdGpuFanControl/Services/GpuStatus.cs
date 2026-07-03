namespace LightweightAmdGpuFanControl.Services;

/// <summary>
/// Immutable snapshot of one GPU's live state, produced by the control loop and read by the UI.
/// The UI must never call into the backend directly — all native access is serialized on the
/// control thread, and this snapshot is how state crosses back to the UI thread safely.
/// </summary>
public readonly record struct GpuStatus(
    string Name,
    string GpuId,
    bool Enabled,
    bool Controlling,
    double? TemperatureC,
    int? FanPercent,
    int? FanRpm);

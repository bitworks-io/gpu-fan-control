namespace LightweightAmdGpuFanControl.Gpu;

/// <summary>
/// Normalized fan telemetry. AMD APIs can report either percent or RPM depending on GPU generation.
/// </summary>
public sealed record FanTelemetry(
    int? ControlPercent,
    int? PhysicalPercent,
    int? PhysicalRpm)
{
    public bool HasAnyReading => ControlPercent.HasValue || PhysicalPercent.HasValue || PhysicalRpm.HasValue;
}

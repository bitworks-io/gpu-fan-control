namespace LightweightAmdGpuFanControl.Models;

/// <summary>
/// Application settings persisted to disk. The curve fields (target/min/max/hysteresis/critical)
/// form a single shared global curve applied to every enabled GPU.
/// </summary>
public class AppSettings
{
    // Target (core GPU temperature) range.
    public const int DefaultTargetTempC = 65;
    public const int MinTargetTempC = 50;
    public const int MaxTargetTempC = 90;

    // Fan-percent bounds. 85% is the manufacturer-recommended top end (hard ceiling);
    // 20% is the floor (no Zero-RPM / passive idle in v1.0).
    public const int MinFanFloor = 20;
    public const int MaxFanCeiling = 85;
    public const int DefaultMinFanPercent = 20;
    public const int DefaultMaxFanPercent = 85;

    // Hysteresis (dead-band) range.
    public const int DefaultHysteresisC = 3;
    public const int MinHysteresisC = 1;
    public const int MaxHysteresisC = 10;

    // Emergency over-temp threshold.
    public const int DefaultCriticalTempC = 90;

    public int TargetTempC { get; set; } = DefaultTargetTempC;
    public int MinFanPercent { get; set; } = DefaultMinFanPercent;
    public int MaxFanPercent { get; set; } = DefaultMaxFanPercent;
    public int HysteresisC { get; set; } = DefaultHysteresisC;
    public int CriticalTempC { get; set; } = DefaultCriticalTempC;

    public bool StartWithWindows { get; set; }

    public FanMode Mode { get; set; } = FanMode.Auto;
    public int ManualFanPercent { get; set; } = 50;

    /// <summary>Per-GPU enablement. Empty until GPUs are detected and reconciled.</summary>
    public List<GpuConfig> Gpus { get; set; } = new();
}

namespace LightweightAmdGpuFanControl.Models;

/// <summary>
/// Application settings persisted to disk.
/// </summary>
public class AppSettings
{
    public const int DefaultTargetTempC = 65;
    public const int MinTargetTempC = 50;
    public const int MaxTargetTempC = 90;

    public int TargetTempC { get; set; } = DefaultTargetTempC;
    public bool StartWithWindows { get; set; }
}

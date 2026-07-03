namespace LightweightAmdGpuFanControl.Models;

/// <summary>
/// Fan control mode. Auto follows the temperature curve; Manual holds a fixed speed
/// (subject to the over-temp emergency and sensor-loss failsafe).
/// </summary>
public enum FanMode
{
    Auto,
    Manual
}

namespace LightweightAmdGpuFanControl.Drivers;

public enum OverdriveFamily { Unsupported, Overdrive5, OverdriveN, Overdrive8 }

/// <summary>Maps an ADL Overdrive version to the backend family allowed to drive the fan.</summary>
public static class OverdriveRouting
{
    public static OverdriveFamily Classify(int version) => version switch
    {
        5 => OverdriveFamily.Overdrive5,
        7 => OverdriveFamily.OverdriveN,
        8 => OverdriveFamily.Overdrive8,
        _ => OverdriveFamily.Unsupported,
    };
}

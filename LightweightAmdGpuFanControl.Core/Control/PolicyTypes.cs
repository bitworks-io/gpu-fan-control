namespace LightweightAmdGpuFanControl.Control;

/// <summary>A single temperature sample. <see cref="TempC"/> is null when the read failed.</summary>
public readonly record struct TempReading(double? TempC);

/// <summary>What the control loop should do with a decision.</summary>
public enum FanAction
{
    /// <summary>Command a specific fan percentage.</summary>
    SetPercent,

    /// <summary>Hand control back to the driver's automatic curve (safe state on sensor loss).</summary>
    RelinquishToAuto
}

/// <summary>The policy's decision for one poll of one GPU.</summary>
public readonly record struct FanDecision(FanAction Action, int Percent);

/// <summary>Per-GPU mutable state carried across polls. One instance per controlled GPU.</summary>
public sealed class PolicyState
{
    public int PreviousFanPercent;
    public int ConsecutiveReadFailures;
    public bool EmergencyLatched;
}

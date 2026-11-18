namespace LightweightAmdGpuFanControl.Control;

/// <summary>
/// A single temperature sample. <see cref="TempC"/> is null when the read failed.
/// <see cref="MonotonicMs"/> is a monotonic clock reading (never wall-clock) used for the decrease dwell.
/// </summary>
public readonly record struct TempReading(double? TempC, long MonotonicMs);

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

    /// <summary>Monotonic ms of the last change of the returned percent (or of the first decision).</summary>
    public long LastChangeMs;

    /// <summary>False until a percent has been returned; reset when control is relinquished so a resume starts plain.</summary>
    public bool HasPrevious;
}

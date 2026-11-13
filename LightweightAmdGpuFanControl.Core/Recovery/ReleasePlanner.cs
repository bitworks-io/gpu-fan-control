namespace LightweightAmdGpuFanControl.Recovery;

/// <summary>How a GPU is returned to the state it had before the app's first fan write.</summary>
public enum ReleaseAction
{
    /// <summary>ADLX ResetToFactory: the pre-app GPU was entirely at factory settings.</summary>
    FactoryReset,
    /// <summary>Re-apply the captured or journaled fan points and zero-RPM flag.</summary>
    ReapplyPoints,
}

public sealed class ReleasePlan
{
    public ReleaseAction Action { get; init; }
    /// <summary>True when the pre-app mode was not recorded (a legacy entry, or IsAtFactory was unreadable at capture).</summary>
    public bool ModeUnknown { get; init; }
    public string Why { get; init; } = "";
}

/// <summary>Pure choice of the release action from the journaled pre-app "was at factory" flag.</summary>
public static class ReleasePlanner
{
    public static ReleasePlan Plan(bool? preAppAtFactory)
    {
        if (preAppAtFactory == true)
            return new ReleasePlan
            {
                Action = ReleaseAction.FactoryReset,
                Why = "the GPU was at factory settings before the app's first write",
            };
        if (preAppAtFactory == false)
            return new ReleasePlan
            {
                Action = ReleaseAction.ReapplyPoints,
                Why = "the GPU had its own tuning before the app's first write",
            };
        return new ReleasePlan
        {
            Action = ReleaseAction.ReapplyPoints,
            ModeUnknown = true,
            Why = "pre-app mode unknown (no factory flag was recorded)",
        };
    }
}

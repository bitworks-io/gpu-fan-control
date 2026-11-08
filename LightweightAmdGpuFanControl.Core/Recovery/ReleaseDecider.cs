namespace LightweightAmdGpuFanControl.Recovery;

/// <summary>The first thing a release does.</summary>
public enum ReleaseStep
{
    /// <summary>The app never wrote to this GPU and no recovered entry is pending: touch nothing.</summary>
    Nothing,
    FactoryReset,
    ReapplyPoints,
    /// <summary>The plan needs the saved fan points but none exist: nothing can be released.</summary>
    NoPoints,
}

public sealed record ReleaseResult(bool Exact, bool MarkClean, bool ReapplyPointsFallback, bool ReportIncomplete);

/// <summary>Pure release decision. The app executes the step and reports whether it succeeded.</summary>
public static class ReleaseDecider
{
    public static ReleaseStep First(bool releasePending, ReleasePlan plan, bool pointsPresent)
    {
        if (!releasePending) return ReleaseStep.Nothing;
        if (plan.Action == ReleaseAction.FactoryReset) return ReleaseStep.FactoryReset;
        return pointsPresent ? ReleaseStep.ReapplyPoints : ReleaseStep.NoPoints;
    }

    /// <summary>
    /// Outcome once <paramref name="step"/> ran. <paramref name="succeeded"/> means a factory reset that IsAtFactory
    /// confirmed, or points that were applied. An unconfirmed factory reset falls back to the points (when present)
    /// and is incomplete: not exact, journal entry stays dirty.
    /// </summary>
    public static ReleaseResult Result(ReleaseStep step, bool succeeded, bool pointsPresent)
    {
        switch (step)
        {
            case ReleaseStep.Nothing:
                return new ReleaseResult(true, false, false, false);
            case ReleaseStep.FactoryReset:
                return succeeded
                    ? new ReleaseResult(true, true, false, false)
                    : new ReleaseResult(false, false, pointsPresent, true);
            case ReleaseStep.ReapplyPoints:
                return succeeded
                    ? new ReleaseResult(true, true, false, false)
                    : new ReleaseResult(false, false, false, true);
            default:
                return new ReleaseResult(false, false, false, true);
        }
    }
}

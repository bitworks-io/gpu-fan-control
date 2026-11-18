namespace LightweightAmdGpuFanControl.Recovery;

/// <summary>User-facing wording for the outcome of a release, chosen from the result and the planned action.</summary>
public static class ReleaseWording
{
    public static string Describe(bool confirmed, ReleaseAction? action)
    {
        if (!confirmed)
            return "could not confirm the fan was released; check AMD Software";
        switch (action)
        {
            case ReleaseAction.FactoryReset:
                return "the GPU was reset to factory settings, so its fan is back under the driver's automatic control";
            case ReleaseAction.ReapplyPoints:
                return "the fan curve it had before the app started was re-applied";
            default:
                return "the fan was handed back to the driver";
        }
    }
}

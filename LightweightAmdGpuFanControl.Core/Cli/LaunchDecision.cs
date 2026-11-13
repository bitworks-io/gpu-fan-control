namespace LightweightAmdGpuFanControl.Cli;

/// <summary>What a launch of this exe should do. Decided before anything else runs.</summary>
public enum LaunchAction { Tray, Stop, Unsupported }

/// <summary>
/// Pure launch decision for the 1.0.x line. v1.0.x ignored all arguments, so arguments that do not start
/// with <c>--</c> still start the tray. Exactly one <c>--stop</c> stops a running instance; every other
/// <c>--</c> option (written for newer builds) is unsupported and must exit at once without side effects.
/// </summary>
public static class LaunchDecision
{
    public static LaunchAction Decide(IReadOnlyList<string>? args)
    {
        if (args == null || args.Count == 0)
            return LaunchAction.Tray;

        bool anyOption = false;
        foreach (var a in args)
        {
            if (a != null && a.StartsWith("--", StringComparison.Ordinal))
            {
                anyOption = true;
                break;
            }
        }
        if (!anyOption)
            return LaunchAction.Tray;

        if (args.Count == 1 && string.Equals(args[0], "--stop", StringComparison.OrdinalIgnoreCase))
            return LaunchAction.Stop;

        return LaunchAction.Unsupported;
    }
}

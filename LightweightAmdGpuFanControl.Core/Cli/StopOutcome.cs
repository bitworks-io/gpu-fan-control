namespace LightweightAmdGpuFanControl.Cli;

/// <summary>Exit code of <c>--stop</c>. Pure so the decision table is testable; the app supplies the three observations.</summary>
public static class StopOutcome
{
    /// <summary>
    /// Nothing holds the instance mutex: not running (3), even if a stop event exists — a same-named event
    /// without a running instance is never reported as "stopped". Running without a stop event: an older
    /// build with no listener (1). Running and signalled: 0 if it exited in time, else 1 (forcing the exit is
    /// the caller's decision).
    /// </summary>
    public static int ExitCode(bool instanceRunning, bool eventExisted, bool exitedInTime)
    {
        if (!instanceRunning)
            return ExitCodes.NothingToDo;
        if (!eventExisted)
            return ExitCodes.Error;
        return exitedInTime ? ExitCodes.Ok : ExitCodes.Error;
    }
}

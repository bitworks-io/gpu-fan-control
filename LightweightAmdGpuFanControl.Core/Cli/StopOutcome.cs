namespace LightweightAmdGpuFanControl.Cli;

/// <summary>Exit code of <c>--stop</c>. Pure so the decision table is testable; the app supplies the three observations.</summary>
public static class StopOutcome
{
    /// <summary>
    /// No stop event and the instance mutex free: not running (3). No event but the mutex held: a running
    /// instance without a stop listener (1). Event present: 0 if the app exited in time, else 1.
    /// </summary>
    public static int ExitCode(bool eventExisted, bool mutexFreeWhenNoEvent, bool exitedInTime)
    {
        if (!eventExisted)
            return mutexFreeWhenNoEvent ? ExitCodes.NothingToDo : ExitCodes.Error;
        return exitedInTime ? ExitCodes.Ok : ExitCodes.Error;
    }
}

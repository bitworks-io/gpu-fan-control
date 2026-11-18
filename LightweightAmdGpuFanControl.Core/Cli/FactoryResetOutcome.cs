namespace LightweightAmdGpuFanControl.Cli;

/// <summary>Exit code of <c>--reset-tuning-to-factory</c>. Pure so the decision table is testable; the app supplies the observations.</summary>
public static class FactoryResetOutcome
{
    /// <summary>
    /// Lock held by another instance: 4. GPU not found or without manual fan tuning: 1. Already at factory before
    /// (nothing written): 3. IsAtFactory unreadable before (nothing written): 1. Reset succeeded and IsAtFactory
    /// confirms: 0. A thrown reset, or a missing or false confirmation: 1.
    /// </summary>
    public static int ExitCode(bool lockAcquired, bool gpuUsable, bool? atFactoryBefore, bool resetSucceeded, bool? atFactoryAfter)
    {
        if (!lockAcquired) return ExitCodes.AlreadyRunning;
        if (!gpuUsable) return ExitCodes.Error;
        if (atFactoryBefore == null) return ExitCodes.Error;
        if (atFactoryBefore == true) return ExitCodes.NothingToDo;
        return resetSucceeded && atFactoryAfter == true ? ExitCodes.Ok : ExitCodes.Error;
    }
}

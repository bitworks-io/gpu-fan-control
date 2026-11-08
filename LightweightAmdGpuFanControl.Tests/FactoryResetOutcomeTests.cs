using LightweightAmdGpuFanControl.Cli;
using Xunit;

public class FactoryResetOutcomeTests
{
    [Fact]
    public void LockHeld_AlreadyRunning()
    {
        Assert.Equal(4, FactoryResetOutcome.ExitCode(lockAcquired: false, gpuUsable: true, atFactoryBefore: false, resetSucceeded: true, atFactoryAfter: true));
    }

    [Fact]
    public void GpuMissingOrUnsupported_Error()
    {
        Assert.Equal(1, FactoryResetOutcome.ExitCode(true, gpuUsable: false, atFactoryBefore: null, resetSucceeded: false, atFactoryAfter: null));
    }

    [Fact]
    public void AlreadyAtFactoryBefore_NothingToDo()
    {
        Assert.Equal(3, FactoryResetOutcome.ExitCode(true, true, atFactoryBefore: true, resetSucceeded: false, atFactoryAfter: null));
    }

    [Fact]
    public void UnreadableBefore_Error()
    {
        Assert.Equal(1, FactoryResetOutcome.ExitCode(true, true, atFactoryBefore: null, resetSucceeded: false, atFactoryAfter: null));
    }

    [Fact]
    public void ResetOkAndConfirmed_Ok()
    {
        Assert.Equal(0, FactoryResetOutcome.ExitCode(true, true, atFactoryBefore: false, resetSucceeded: true, atFactoryAfter: true));
    }

    [Fact]
    public void ResetThrewOrNotConfirmed_Error()
    {
        Assert.Equal(1, FactoryResetOutcome.ExitCode(true, true, false, resetSucceeded: false, atFactoryAfter: null));
        Assert.Equal(1, FactoryResetOutcome.ExitCode(true, true, false, resetSucceeded: true, atFactoryAfter: false));
        Assert.Equal(1, FactoryResetOutcome.ExitCode(true, true, false, resetSucceeded: true, atFactoryAfter: null));
    }
}

using LightweightAmdGpuFanControl.Cli;
using Xunit;

public class StopOutcomeTests
{
    [Fact]
    public void StopOutcome_Cases()
    {
        // Nothing holds the instance mutex: not running, whether or not a stop event exists.
        Assert.Equal(3, StopOutcome.ExitCode(instanceRunning: false, eventExisted: false, exitedInTime: false));
        // Running, but no stop listener (an older build).
        Assert.Equal(1, StopOutcome.ExitCode(instanceRunning: true, eventExisted: false, exitedInTime: false));
        // Signalled and it exited in time.
        Assert.Equal(0, StopOutcome.ExitCode(instanceRunning: true, eventExisted: true, exitedInTime: true));
        // Signalled but it did not exit in time.
        Assert.Equal(1, StopOutcome.ExitCode(instanceRunning: true, eventExisted: true, exitedInTime: false));
    }

    [Fact]
    public void StopOutcome_StaleEventWithoutInstance_NotRunning()
    {
        // A stop event that exists while nothing holds the instance mutex must never be reported as "stopped".
        Assert.Equal(3, StopOutcome.ExitCode(instanceRunning: false, eventExisted: true, exitedInTime: true));
    }
}

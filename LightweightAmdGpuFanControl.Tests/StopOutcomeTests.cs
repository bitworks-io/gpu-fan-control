using LightweightAmdGpuFanControl.Cli;
using Xunit;

public class StopOutcomeTests
{
    [Fact]
    public void StopOutcome_Cases()
    {
        Assert.Equal(3, StopOutcome.ExitCode(eventExisted: false, mutexFreeWhenNoEvent: true, exitedInTime: false));
        Assert.Equal(1, StopOutcome.ExitCode(eventExisted: false, mutexFreeWhenNoEvent: false, exitedInTime: true));
        Assert.Equal(0, StopOutcome.ExitCode(eventExisted: true, mutexFreeWhenNoEvent: false, exitedInTime: true));
        Assert.Equal(1, StopOutcome.ExitCode(eventExisted: true, mutexFreeWhenNoEvent: true, exitedInTime: false));
    }
}

using LightweightAmdGpuFanControl.Control;
using Xunit;

public class GpuControlTrackerTests
{
    [Fact]
    public void StartsInStarting_AndMayWrite()
    {
        var t = new GpuControlTracker();
        Assert.Equal(GpuControlState.Starting, t.State);
        Assert.True(t.MayWrite);
        Assert.Null(t.Message);
    }

    [Fact]
    public void Starting_ThenSelfTestPasses_Regulating()
    {
        var t = new GpuControlTracker();
        Assert.True(t.BeginRegulating());
        Assert.Equal(GpuControlState.Regulating, t.State);
        Assert.True(t.MayWrite);
    }

    [Fact]
    public void Regulating_Error_Degraded_ReleaseOnce_NoSecondRelease()
    {
        var t = new GpuControlTracker();
        t.BeginRegulating();
        Assert.Equal(WriteErrorAction.ReleaseOnce, t.OnWriteError("Result: ADLX_FAIL"));
        Assert.Equal(GpuControlState.Degraded, t.State);
        Assert.Equal("Result: ADLX_FAIL", t.Message);
        Assert.False(t.MayWrite);
        // A repeated error neither releases again nor overwrites the first message.
        Assert.Equal(WriteErrorAction.None, t.OnWriteError("another"));
        Assert.Equal("Result: ADLX_FAIL", t.Message);
        Assert.Equal(GpuControlState.Degraded, t.State);
    }

    [Fact]
    public void Starting_Error_Refused_NoReleaseAction()
    {
        var t = new GpuControlTracker();
        Assert.Equal(WriteErrorAction.None, t.OnWriteError("boom"));
        Assert.Equal(GpuControlState.Refused, t.State);
        Assert.Equal("boom", t.Message);
        Assert.False(t.MayWrite);
    }

    [Fact]
    public void Refuse_FromStarting_WithoutAnError()
    {
        var t = new GpuControlTracker();
        t.Refuse("self-test did not pass");
        Assert.Equal(GpuControlState.Refused, t.State);
        Assert.False(t.MayWrite);
    }

    [Fact]
    public void NoWayBackToRegulating()
    {
        var refused = new GpuControlTracker();
        refused.OnWriteError("x");
        Assert.False(refused.BeginRegulating());
        Assert.Equal(GpuControlState.Refused, refused.State);

        var degraded = new GpuControlTracker();
        degraded.BeginRegulating();
        degraded.OnWriteError("x");
        Assert.False(degraded.BeginRegulating());
        Assert.Equal(GpuControlState.Degraded, degraded.State);

        var regulating = new GpuControlTracker();
        regulating.BeginRegulating();
        Assert.False(regulating.BeginRegulating()); // already regulating: no transition
        regulating.Refuse("late"); // Refuse only applies while Starting
        Assert.Equal(GpuControlState.Regulating, regulating.State);
    }

    [Fact]
    public void ErrorMessage_IsSingleLine()
    {
        var t = new GpuControlTracker();
        t.OnWriteError("Result: ADLX_FAIL \r\nCouldn't set fan speed");
        Assert.DoesNotContain("\n", t.Message);
        Assert.DoesNotContain("\r", t.Message);
        Assert.Contains("Couldn't set fan speed", t.Message);
        var empty = new GpuControlTracker();
        empty.OnWriteError(null);
        Assert.Equal("(no message)", empty.Message);
    }
}

using LightweightAmdGpuFanControl.Recovery;
using Xunit;

public class ReleasePlannerTests
{
    [Fact]
    public void PreAppAtFactory_PlansFactoryReset()
    {
        var plan = ReleasePlanner.Plan(true);
        Assert.Equal(ReleaseAction.FactoryReset, plan.Action);
        Assert.False(plan.ModeUnknown);
        Assert.False(string.IsNullOrWhiteSpace(plan.Why));
    }

    [Fact]
    public void UserManualCurve_PlansReapplyPoints_SamePoints()
    {
        var plan = ReleasePlanner.Plan(false);
        Assert.Equal(ReleaseAction.ReapplyPoints, plan.Action);
        Assert.False(plan.ModeUnknown);
        Assert.False(string.IsNullOrWhiteSpace(plan.Why));
    }

    [Fact]
    public void LegacyEntry_PlansReapplyPoints_ModeUnknown()
    {
        var plan = ReleasePlanner.Plan(null);
        Assert.Equal(ReleaseAction.ReapplyPoints, plan.Action);
        Assert.True(plan.ModeUnknown);
        Assert.Contains("unknown", plan.Why);
    }

    [Fact]
    public void Plan_FromBaselineValue()
    {
        Assert.Equal(ReleaseAction.FactoryReset, ReleasePlanner.Plan(new FanBaseline(new[] { 20 }, false, false, true).PreAppAtFactory).Action);
        Assert.Equal(ReleaseAction.ReapplyPoints, ReleasePlanner.Plan(new FanBaseline(new[] { 20 }, false, false, false).PreAppAtFactory).Action);
        Assert.True(ReleasePlanner.Plan(new FanBaseline(new[] { 20 }, false, false).PreAppAtFactory).ModeUnknown);
    }
}

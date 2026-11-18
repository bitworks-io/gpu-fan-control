using LightweightAmdGpuFanControl.Recovery;
using Xunit;

public class ReleaseDeciderTests
{
    private static readonly ReleasePlan Factory = ReleasePlanner.Plan(true);
    private static readonly ReleasePlan Points = ReleasePlanner.Plan(false);

    [Fact]
    public void UntouchedGpu_ShortCircuits_NothingWritten_NoMarkClean()
    {
        foreach (var plan in new[] { Factory, Points })
        {
            Assert.Equal(ReleaseStep.Nothing, ReleaseDecider.First(false, plan, true));
            Assert.Equal(ReleaseStep.Nothing, ReleaseDecider.First(false, plan, false));
        }
        var r = ReleaseDecider.Result(ReleaseStep.Nothing, true, true);
        Assert.True(r.Exact);
        Assert.False(r.MarkClean);
        Assert.False(r.ReportIncomplete);
    }

    [Fact]
    public void Pending_FactoryPlan_StartsWithFactoryReset_EvenWithoutPoints()
    {
        Assert.Equal(ReleaseStep.FactoryReset, ReleaseDecider.First(true, Factory, true));
        Assert.Equal(ReleaseStep.FactoryReset, ReleaseDecider.First(true, Factory, false));
    }

    [Fact]
    public void Pending_PointsPlan_ReappliesPoints_OrIsIncompleteWithoutThem()
    {
        Assert.Equal(ReleaseStep.ReapplyPoints, ReleaseDecider.First(true, Points, true));
        Assert.Equal(ReleaseStep.NoPoints, ReleaseDecider.First(true, Points, false));
        var r = ReleaseDecider.Result(ReleaseStep.NoPoints, false, false);
        Assert.False(r.Exact);
        Assert.False(r.MarkClean);
        Assert.True(r.ReportIncomplete);
        Assert.False(r.ReapplyPointsFallback);
    }

    [Fact]
    public void ConfirmedFactoryReset_IsExact_AndMarksClean()
    {
        var r = ReleaseDecider.Result(ReleaseStep.FactoryReset, true, true);
        Assert.True(r.Exact);
        Assert.True(r.MarkClean);
        Assert.False(r.ReportIncomplete);
        Assert.False(r.ReapplyPointsFallback);
    }

    [Fact]
    public void UnconfirmedFactoryReset_FallsBackToPoints_Incomplete_NoMarkClean()
    {
        var r = ReleaseDecider.Result(ReleaseStep.FactoryReset, false, true);
        Assert.False(r.Exact);
        Assert.False(r.MarkClean);
        Assert.True(r.ReportIncomplete);
        Assert.True(r.ReapplyPointsFallback);
        Assert.False(ReleaseDecider.Result(ReleaseStep.FactoryReset, false, false).ReapplyPointsFallback);
    }

    [Fact]
    public void PointsReapplied_IsExact_AndMarksClean_ButAFailureIsIncomplete()
    {
        var ok = ReleaseDecider.Result(ReleaseStep.ReapplyPoints, true, true);
        Assert.True(ok.Exact);
        Assert.True(ok.MarkClean);
        var bad = ReleaseDecider.Result(ReleaseStep.ReapplyPoints, false, true);
        Assert.False(bad.Exact);
        Assert.False(bad.MarkClean);
        Assert.True(bad.ReportIncomplete);
    }
}

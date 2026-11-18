using LightweightAmdGpuFanControl.Recovery;
using Xunit;

public class ReleaseWordingTests
{
    [Fact]
    public void Confirmed_FactoryReset_SaysFactory()
    {
        var t = ReleaseWording.Describe(true, ReleaseAction.FactoryReset);
        Assert.Contains("factory", t);
        Assert.DoesNotContain("could not confirm", t);
    }

    [Fact]
    public void Confirmed_ReapplyPoints_SaysOwnCurveReapplied_NotHandedToDriver()
    {
        var t = ReleaseWording.Describe(true, ReleaseAction.ReapplyPoints);
        Assert.Contains("curve", t);
        Assert.DoesNotContain("handed", t);
        Assert.DoesNotContain("could not confirm", t);
    }

    [Fact]
    public void Confirmed_NoPlan_SaysHandedBackToDriver()
    {
        Assert.Contains("handed", ReleaseWording.Describe(true, null));
    }

    [Fact]
    public void NotConfirmed_SaysSoPlainly_WhateverTheAction()
    {
        foreach (ReleaseAction? a in new ReleaseAction?[] { ReleaseAction.FactoryReset, ReleaseAction.ReapplyPoints, null })
        {
            var t = ReleaseWording.Describe(false, a);
            Assert.Contains("could not confirm the fan was released", t);
            Assert.Contains("check AMD Software", t);
        }
    }
}

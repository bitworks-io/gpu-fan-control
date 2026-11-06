using LightweightAmdGpuFanControl.Drivers;
using Xunit;

public class OverdriveRoutingTests
{
    [Theory]
    [InlineData(5, OverdriveFamily.Overdrive5)]
    [InlineData(7, OverdriveFamily.OverdriveN)]
    [InlineData(8, OverdriveFamily.Overdrive8)]
    [InlineData(6, OverdriveFamily.Unsupported)]
    [InlineData(9, OverdriveFamily.Unsupported)]
    [InlineData(0, OverdriveFamily.Unsupported)]
    [InlineData(-1, OverdriveFamily.Unsupported)]
    public void Classify_RoutesByVersion(int version, OverdriveFamily expected) =>
        Assert.Equal(expected, OverdriveRouting.Classify(version));
}

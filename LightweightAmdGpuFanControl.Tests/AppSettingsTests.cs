using LightweightAmdGpuFanControl.Models;
using Xunit;

public class AppSettingsTests
{
    [Fact]
    public void Defaults_match_spec()
    {
        var s = new AppSettings();
        Assert.Equal(65, s.TargetTempC);
        Assert.Equal(20, s.MinFanPercent);
        Assert.Equal(85, s.MaxFanPercent);
        Assert.Equal(3, s.HysteresisC);
        Assert.Equal(90, s.CriticalTempC);
        Assert.Equal(FanMode.Auto, s.Mode);
        Assert.NotNull(s.Gpus);
        Assert.Empty(s.Gpus);
    }
}

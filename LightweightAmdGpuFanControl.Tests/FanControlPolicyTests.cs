using LightweightAmdGpuFanControl.Control;
using LightweightAmdGpuFanControl.Models;
using Xunit;

public class FanControlPolicyTests
{
    private readonly FanControlPolicy _policy = new();

    // Emergency threshold pushed out of the way so ramp behavior is observable in isolation.
    private static AppSettings RampSettings() => new()
    {
        TargetTempC = 65,
        MinFanPercent = 20,
        MaxFanPercent = 85,
        HysteresisC = 3,
        CriticalTempC = 200,
        Mode = FanMode.Auto
    };

    // Realistic critical threshold for emergency tests.
    private static AppSettings EmergencySettings() => new()
    {
        TargetTempC = 65,
        MinFanPercent = 20,
        MaxFanPercent = 85,
        HysteresisC = 3,
        CriticalTempC = 90,
        Mode = FanMode.Auto
    };

    [Fact]
    public void Below_target_minus_hysteresis_returns_min()
    {
        var d = _policy.Decide(new TempReading(50), RampSettings(), new PolicyState());
        Assert.Equal(FanAction.SetPercent, d.Action);
        Assert.Equal(20, d.Percent);
    }

    [Fact]
    public void At_target_returns_min()
    {
        var d = _policy.Decide(new TempReading(65), RampSettings(), new PolicyState());
        Assert.Equal(20, d.Percent);
    }

    [Fact]
    public void At_target_plus_range_returns_max()
    {
        var d = _policy.Decide(new TempReading(90), RampSettings(), new PolicyState());
        Assert.Equal(85, d.Percent);
    }

    [Fact]
    public void Mid_ramp_is_between_and_monotonic()
    {
        var atTarget = _policy.Decide(new TempReading(65), RampSettings(), new PolicyState()).Percent;
        var mid = _policy.Decide(new TempReading(78), RampSettings(), new PolicyState()).Percent;
        Assert.True(mid > atTarget);
        Assert.InRange(mid, 21, 84);
    }

    [Fact]
    public void Never_exceeds_max_even_far_above_target()
    {
        var d = _policy.Decide(new TempReading(165), RampSettings(), new PolicyState());
        Assert.Equal(85, d.Percent);
    }

    [Fact]
    public void Dead_band_holds_previous()
    {
        var state = new PolicyState { PreviousFanPercent = 50 };
        var d = _policy.Decide(new TempReading(63), RampSettings(), state); // between 62 and 65
        Assert.Equal(50, d.Percent);
    }

    [Fact]
    public void Sensor_loss_holds_then_relinquishes()
    {
        var s = RampSettings();
        var state = new PolicyState { PreviousFanPercent = 40 };

        var d1 = _policy.Decide(new TempReading(null), s, state);
        Assert.Equal(FanAction.SetPercent, d1.Action);
        Assert.Equal(40, d1.Percent);

        var d2 = _policy.Decide(new TempReading(null), s, state);
        Assert.Equal(FanAction.SetPercent, d2.Action);
        Assert.Equal(40, d2.Percent);

        var d3 = _policy.Decide(new TempReading(null), s, state);
        Assert.Equal(FanAction.RelinquishToAuto, d3.Action);
    }

    [Fact]
    public void Emergency_latches_and_releases_with_hysteresis()
    {
        var s = EmergencySettings();
        var state = new PolicyState();

        var hot = _policy.Decide(new TempReading(90), s, state); // == critical
        Assert.Equal(FanAction.SetPercent, hot.Action);
        Assert.Equal(85, hot.Percent);
        Assert.True(state.EmergencyLatched);

        var stillHot = _policy.Decide(new TempReading(89), s, state); // >= critical - hyst (87)
        Assert.True(state.EmergencyLatched);
        Assert.Equal(85, stillHot.Percent);

        _policy.Decide(new TempReading(86), s, state); // < critical - hyst -> unlatch
        Assert.False(state.EmergencyLatched);
    }

    [Fact]
    public void Manual_mode_returns_clamped_manual()
    {
        var s = RampSettings();
        s.Mode = FanMode.Manual;
        s.ManualFanPercent = 50;
        var d = _policy.Decide(new TempReading(40), s, new PolicyState());
        Assert.Equal(50, d.Percent);
    }

    [Fact]
    public void Manual_mode_still_forces_max_on_over_temp()
    {
        var s = EmergencySettings();
        s.Mode = FanMode.Manual;
        s.ManualFanPercent = 30;
        var d = _policy.Decide(new TempReading(95), s, new PolicyState());
        Assert.Equal(85, d.Percent);
    }
}

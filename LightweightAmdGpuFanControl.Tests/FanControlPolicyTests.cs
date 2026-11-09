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
        var d = _policy.Decide(new TempReading((50), 0), RampSettings(), new PolicyState());
        Assert.Equal(FanAction.SetPercent, d.Action);
        Assert.Equal(20, d.Percent);
    }

    [Fact]
    public void At_target_returns_min()
    {
        var d = _policy.Decide(new TempReading((65), 0), RampSettings(), new PolicyState());
        Assert.Equal(20, d.Percent);
    }

    [Fact]
    public void At_target_plus_range_returns_max()
    {
        var d = _policy.Decide(new TempReading((90), 0), RampSettings(), new PolicyState());
        Assert.Equal(85, d.Percent);
    }

    [Fact]
    public void Mid_ramp_is_between_and_monotonic()
    {
        var atTarget = _policy.Decide(new TempReading((65), 0), RampSettings(), new PolicyState()).Percent;
        var mid = _policy.Decide(new TempReading((78), 0), RampSettings(), new PolicyState()).Percent;
        Assert.True(mid > atTarget);
        Assert.InRange(mid, 21, 84);
    }

    [Fact]
    public void Never_exceeds_max_even_far_above_target()
    {
        var d = _policy.Decide(new TempReading((165), 0), RampSettings(), new PolicyState());
        Assert.Equal(85, d.Percent);
    }

    [Fact]
    public void Dead_band_holds_previous()
    {
        var state = new PolicyState { PreviousFanPercent = 50 };
        var d = _policy.Decide(new TempReading((63), 0), RampSettings(), state); // between 62 and 65
        Assert.Equal(50, d.Percent);
    }

    [Fact]
    public void Sensor_loss_holds_then_relinquishes()
    {
        var s = RampSettings();
        var state = new PolicyState { PreviousFanPercent = 40 };

        var d1 = _policy.Decide(new TempReading((null), 0), s, state);
        Assert.Equal(FanAction.SetPercent, d1.Action);
        Assert.Equal(40, d1.Percent);

        var d2 = _policy.Decide(new TempReading((null), 0), s, state);
        Assert.Equal(FanAction.SetPercent, d2.Action);
        Assert.Equal(40, d2.Percent);

        var d3 = _policy.Decide(new TempReading((null), 0), s, state);
        Assert.Equal(FanAction.RelinquishToAuto, d3.Action);
    }

    [Fact]
    public void Emergency_latches_and_releases_with_hysteresis()
    {
        var s = EmergencySettings();
        var state = new PolicyState();

        var hot = _policy.Decide(new TempReading((90), 0), s, state); // == critical
        Assert.Equal(FanAction.SetPercent, hot.Action);
        Assert.Equal(85, hot.Percent);
        Assert.True(state.EmergencyLatched);

        var stillHot = _policy.Decide(new TempReading((89), 0), s, state); // >= critical - hyst (87)
        Assert.True(state.EmergencyLatched);
        Assert.Equal(85, stillHot.Percent);

        _policy.Decide(new TempReading((86), 0), s, state); // < critical - hyst -> unlatch
        Assert.False(state.EmergencyLatched);
    }

    [Fact]
    public void Manual_mode_returns_clamped_manual()
    {
        var s = RampSettings();
        s.Mode = FanMode.Manual;
        s.ManualFanPercent = 50;
        var d = _policy.Decide(new TempReading((40), 0), s, new PolicyState());
        Assert.Equal(50, d.Percent);
    }

    [Fact]
    public void Manual_mode_still_forces_max_on_over_temp()
    {
        var s = EmergencySettings();
        s.Mode = FanMode.Manual;
        s.ManualFanPercent = 30;
        var d = _policy.Decide(new TempReading((95), 0), s, new PolicyState());
        Assert.Equal(85, d.Percent);
    }

    // ---- Ramp hysteresis (defaults T 65, min 20, max 85, H 3; polls every 2,500 ms) ----

    private int Step(PolicyState st, double t, long ms) =>
        _policy.Decide(new TempReading(t, ms), RampSettings(), st).Percent;

    [Fact]
    public void Ramp_OneDegreeWobble_HoldsTheHigherDuty()
    {
        var st = new PolicyState();
        double[] temps = { 77, 78, 77, 78, 77 };
        int[] expected = { 51, 54, 54, 54, 54 };
        for (int i = 0; i < temps.Length; i++)
            Assert.Equal(expected[i], Step(st, temps[i], i * 2500L));

        // Keep alternating well past the 10 s dwell: the hysteresis (not just the dwell) must hold 54.
        for (int i = temps.Length; i < 20; i++)
            Assert.Equal(54, Step(st, i % 2 == 0 ? 77 : 78, i * 2500L));
    }

    [Fact]
    public void Ramp_RealCooling_DecreasesAfterHysteresisAndDwell()
    {
        var st = new PolicyState();
        Assert.Equal(54, Step(st, 78, 0));
        Assert.Equal(54, Step(st, 74, 2500));   // dwell not elapsed
        Assert.Equal(51, Step(st, 74, 12500));  // Ramp(77)
        Assert.Equal(41, Step(st, 70, 25000));  // Ramp(73)
    }

    [Fact]
    public void Ramp_Increase_IsImmediate_EvenInsideTheDwell()
    {
        var st = new PolicyState();
        Assert.Equal(54, Step(st, 78, 0));
        Assert.Equal(54, Step(st, 74, 2500));
        Assert.Equal(59, Step(st, 80, 3000));
    }

    [Fact]
    public void Ramp_FirstDecision_IsThePlainRamp()
    {
        Assert.Equal(51, Step(new PolicyState(), 77, 0));
    }

    [Fact]
    public void Ramp_IncreaseRightAfterADecrease_IsImmediate()
    {
        var st = new PolicyState();
        Assert.Equal(54, Step(st, 78, 0));
        Assert.Equal(51, Step(st, 74, 12500));  // Ramp(77), a decrease
        Assert.Equal(59, Step(st, 80, 15000));  // Ramp(80), immediately
    }

    [Fact]
    public void Resume_WithStaleState_FirstDecisionIsPlain()
    {
        var st = new PolicyState { HasPrevious = false, PreviousFanPercent = 85, LastChangeMs = 0 };
        Assert.Equal(51, Step(st, 77, 1000));
    }

    [Fact]
    public void FailedRead_AfterResume_NextGoodReadIsPlain()
    {
        var st = new PolicyState { HasPrevious = false, PreviousFanPercent = 54, LastChangeMs = 0 };
        var d = _policy.Decide(new TempReading(null, 100000), RampSettings(), st);
        Assert.Equal(FanAction.SetPercent, d.Action);
        Assert.Equal(54, d.Percent);
        Assert.False(st.HasPrevious);
        Assert.Equal(51, Step(st, 77, 102500));
    }

    [Fact]
    public void EmergencyRelease_IntoTheRamp_DecreasesOnlyAfterDwellAndHysteresis()
    {
        var s = EmergencySettings();
        var st = new PolicyState();
        Assert.Equal(85, _policy.Decide(new TempReading(90, 0), s, st).Percent);
        Assert.Equal(85, _policy.Decide(new TempReading(80, 2500), s, st).Percent);   // unlatched, dwell not elapsed
        Assert.False(st.EmergencyLatched);
        Assert.Equal(67, _policy.Decide(new TempReading(80, 12500), s, st).Percent);  // Ramp(83)
    }

    [Fact]
    public void ManualToAuto_TakesTheRampWithoutDelayingAnIncrease()
    {
        var man = RampSettings(); man.Mode = FanMode.Manual; man.ManualFanPercent = 30;
        var st = new PolicyState();
        Assert.Equal(30, _policy.Decide(new TempReading(77, 0), man, st).Percent);
        Assert.Equal(51, Step(st, 77, 500));      // increase 30 -> Ramp(77), immediate

        var man2 = RampSettings(); man2.Mode = FanMode.Manual; man2.ManualFanPercent = 70;
        var st2 = new PolicyState();
        Assert.Equal(70, _policy.Decide(new TempReading(77, 0), man2, st2).Percent);
        Assert.Equal(70, Step(st2, 77, 2500));    // decrease is held inside the dwell
        Assert.Equal(59, Step(st2, 77, 10000));   // Ramp(80)
    }

    [Fact]
    public void DeadBandIntoRamp_RisesImmediately()
    {
        var st = new PolicyState();
        Assert.Equal(20, Step(st, 63, 0));      // dead band
        Assert.Equal(23, Step(st, 66, 2500));   // Ramp(66)
        Assert.Equal(33, Step(st, 70, 5000));   // Ramp(70)
    }

    [Fact]
    public void Emergency_LatchAndRelease_Unchanged()
    {
        var s = EmergencySettings();
        var st = new PolicyState();
        Assert.Equal(85, _policy.Decide(new TempReading(90, 0), s, st).Percent);
        Assert.True(st.EmergencyLatched);
        Assert.Equal(85, _policy.Decide(new TempReading(89, 2500), s, st).Percent);
        Assert.True(st.EmergencyLatched);
        _policy.Decide(new TempReading(86, 5000), s, st);
        Assert.False(st.EmergencyLatched);
    }
}

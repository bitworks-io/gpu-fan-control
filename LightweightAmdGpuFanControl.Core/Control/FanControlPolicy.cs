using LightweightAmdGpuFanControl.Models;

namespace LightweightAmdGpuFanControl.Control;

/// <summary>
/// Pure, hardware-independent fan-control policy. Given a temperature sample, the settings, and the
/// per-GPU state, it decides the fan percentage (or that control should be relinquished to the driver).
///
/// Rules (in order):
/// 1. Failed read: hold the previous percent; after <see cref="ReadFailureLimit"/> consecutive
///    failures, relinquish to the driver's automatic curve (reads and writes share a connection, so a
///    read failure means a write likely fails too — the driver's independent thermal protection is the
///    reliable backstop).
/// 2. Over-temp emergency: at/above CriticalTempC, jump to the configured max and latch until the temp
///    falls below CriticalTempC - HysteresisC.
/// 3. Manual mode: hold the manual percent (still overridden by the emergency above).
/// 4. Auto ramp: below (Target - Hysteresis) hold min; at/above Target ramp linearly min->max over
///    <see cref="RampTempRange"/> degrees; the dead-band in between holds the previous value to prevent
///    fan hunting.
/// The fan never exceeds MaxFanPercent (default 85% — the manufacturer-recommended ceiling).
/// </summary>
public sealed class FanControlPolicy
{
    public const int RampTempRange = 25;
    public const int ReadFailureLimit = 3;

    /// <summary>Minimum time since the last duty change before the ramp may lower the duty. Increases are never delayed.</summary>
    public const int MinDecreaseDwellMs = 10_000;

    /// <summary>The linear ramp min->max over <see cref="RampTempRange"/> degrees above the target, clamped.</summary>
    public static int Ramp(double t, AppSettings s)
    {
        int pct = s.MinFanPercent
            + (int)Math.Round((t - s.TargetTempC) * (s.MaxFanPercent - s.MinFanPercent) / RampTempRange);
        return MathCompat.Clamp(pct, s.MinFanPercent, s.MaxFanPercent);
    }

    public FanDecision Decide(TempReading reading, AppSettings s, PolicyState state)
    {
        // 1. Failed temperature read.
        if (reading.TempC is null)
        {
            state.ConsecutiveReadFailures++;
            if (state.ConsecutiveReadFailures >= ReadFailureLimit)
                return new FanDecision(FanAction.RelinquishToAuto, 0);
            return new FanDecision(FanAction.SetPercent, state.PreviousFanPercent);
        }

        state.ConsecutiveReadFailures = 0;
        double t = reading.TempC.Value;

        // 2. Emergency latch (hysteretic).
        if (t >= s.CriticalTempC)
            state.EmergencyLatched = true;
        else if (state.EmergencyLatched && t < s.CriticalTempC - s.HysteresisC)
            state.EmergencyLatched = false;

        int pct;
        if (state.EmergencyLatched)
        {
            pct = s.MaxFanPercent;
        }
        else if (s.Mode == FanMode.Manual)
        {
            pct = MathCompat.Clamp(s.ManualFanPercent, s.MinFanPercent, s.MaxFanPercent);
        }
        else if (t < s.TargetTempC - s.HysteresisC)
        {
            pct = s.MinFanPercent;
        }
        else if (t >= s.TargetTempC)
        {
            // Increases are immediate; a decrease needs the temperature to fall HysteresisC below the point
            // that produced the held duty AND a minimum dwell since the last change (no dither on a 1 C wobble).
            int up = Ramp(t, s);
            if (!state.HasPrevious || up >= state.PreviousFanPercent)
            {
                pct = up;
            }
            else
            {
                int down = Ramp(t + s.HysteresisC, s);
                pct = down < state.PreviousFanPercent
                      && reading.MonotonicMs - state.LastChangeMs >= MinDecreaseDwellMs
                    ? down
                    : state.PreviousFanPercent;
            }
        }
        else
        {
            // Dead-band between (Target - Hysteresis) and Target: hold to avoid hunting.
            pct = MathCompat.Clamp(state.PreviousFanPercent, s.MinFanPercent, s.MaxFanPercent);
        }

        pct = MathCompat.Clamp(pct, s.MinFanPercent, s.MaxFanPercent);
        if (!state.HasPrevious || pct != state.PreviousFanPercent)
            state.LastChangeMs = reading.MonotonicMs;
        state.HasPrevious = true;
        state.PreviousFanPercent = pct;
        return new FanDecision(FanAction.SetPercent, pct);
    }
}

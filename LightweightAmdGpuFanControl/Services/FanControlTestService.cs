using LightweightAmdGpuFanControl.Gpu;

namespace LightweightAmdGpuFanControl.Services;

/// <summary>
/// Runs a startup test that tolerates GPU generation differences in fan telemetry.
/// </summary>
public class FanControlTestService
{
    private const int TestFanPercent = 35;
    private const int WaitMs = 4000;
    private const int MinExpectedPercent = 25;
    private const int MaxExpectedPercent = 45;
    private const int MinimumUsefulRpm = 500;
    private const double MinimumRpmRiseRatio = 1.15;

    public bool RunTest(IFanControlBackend backend, LogService log)
    {
        try
        {
            var before = backend.GetFanTelemetry();
            backend.DisableZeroRpm();
            backend.SetFanPercent(TestFanPercent);
            Thread.Sleep(WaitMs);
            var after = backend.GetFanTelemetry();

            if (IsPercentClose(after.ControlPercent) || IsPercentClose(after.PhysicalPercent))
            {
                log.Log($"Fan test passed on {backend.BackendName}: requested {TestFanPercent}%, telemetry {Format(after)}.");
                return true;
            }

            if (ShowsRpmResponse(before.PhysicalRpm, after.PhysicalRpm))
            {
                log.Log($"Fan test passed on {backend.BackendName}: fan RPM responded from {before.PhysicalRpm} to {after.PhysicalRpm}.");
                return true;
            }

            if (!after.HasAnyReading)
            {
                log.Log($"Fan test degraded on {backend.BackendName}: set command succeeded but no fan telemetry was available.");
                return true;
            }

            log.Log($"Fan test failed on {backend.BackendName}: requested {TestFanPercent}%, before {Format(before)}, after {Format(after)}.");
            return false;
        }
        catch (Exception ex)
        {
            log.Log("Fan test failed with exception.", ex);
            return false;
        }
    }

    private static bool IsPercentClose(int? percent) => percent is >= MinExpectedPercent and <= MaxExpectedPercent;

    private static bool ShowsRpmResponse(int? beforeRpm, int? afterRpm)
    {
        if (!afterRpm.HasValue)
            return false;

        if (!beforeRpm.HasValue)
            return afterRpm.Value >= MinimumUsefulRpm;

        return afterRpm.Value >= MinimumUsefulRpm && afterRpm.Value >= beforeRpm.Value * MinimumRpmRiseRatio;
    }

    private static string Format(FanTelemetry telemetry)
    {
        return $"control={telemetry.ControlPercent?.ToString() ?? "n/a"}%, " +
               $"physical={telemetry.PhysicalPercent?.ToString() ?? "n/a"}%, " +
               $"rpm={telemetry.PhysicalRpm?.ToString() ?? "n/a"}";
    }
}

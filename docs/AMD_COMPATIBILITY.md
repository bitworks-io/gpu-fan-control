# AMD GPU Compatibility Notes

The utility selects AMD fan-control support by API capability, not by marketing name.

## Backend Order

1. **ADLX backend**
   - Preferred for modern Radeon GPUs.
   - Expected path for RX 5000, RX 6000, RX 7000, and RX 9000-series cards with current Adrenalin drivers.
   - Uses ADLX GPU tuning and performance monitoring.

2. **ADL OverdriveN backend**
   - Fallback for cards where ADLX fan tuning is unavailable.
   - Intended to cover RX 470/480 and RX 570/580/590 Polaris-era cards when the driver exposes OverdriveN or CustomFan APIs.
   - Uses `atiadlxx.dll` / `atiadlxy.dll` from the installed AMD driver.

3. **ADL Overdrive5 backend**
   - Best-effort fallback for older cards.
   - Uses legacy temperature and fan-speed APIs when available.

## Edge Conditions

- Fan telemetry may be reported as percent, RPM, or only as a commanded control state.
- Some cards expose temperature through ADLX but not fan tuning, requiring fallback.
- Some driver states require AMD Adrenalin Manual Tuning and Fan Tuning to be enabled.
- Zero RPM can prevent physical fan movement at low requests; the utility disables it when supported.
- Factory reset is intentionally not called during startup testing, because it can wipe user tuning profiles.
- Multi-GPU selection is not implemented yet; the first supported AMD adapter is used.
- Fan-control APIs can conflict with other tuning tools. Only one tool should own manual fan control at a time.

## Startup Test Rules

The startup test requests 35% fan speed, waits briefly, then passes if any of these is true:

- Command/control state reports roughly 35%.
- Physical fan percent reports roughly 35%.
- Physical RPM rises meaningfully from baseline.
- The set command succeeds but no fan telemetry is exposed, in which case the app logs a degraded pass.

If telemetry contradicts the request, the app disables its control loop and points the user to the troubleshooting help.

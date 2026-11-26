# AMD GPU Compatibility Notes

The utility selects AMD fan-control support by API capability, not by marketing name.

## Validation Status

Status as of 1.1.1 (released 2026-11-26). The app's fan control is unchanged since 1.1.0 (released 2026-11-23; the signed 1.1.0-rc.3 build, promoted unchanged), so these checks still apply. Only the checks below were run; nothing is claimed beyond them.

| Generation | Test card | Status |
| --- | --- | --- |
| RDNA1 | RX 5700 XT | Validated: under load the fan was regulated along its curve (increases followed, no abort); on exit the GPU returned to the driver's automatic control with the factory reset confirmed and the idle fan stop back; after a hard kill `--restore-auto` restored automatic control from the journal; the signed rc.3 installer installed cleanly over 1.0.1. |
| RDNA3 | RX 7800 XT | Validated: under load each commanded fan increase raised the fan speed; the start-up self-test passed; release to automatic control verified under load and at idle, with the idle fan stop back. |
| RDNA3 | RX 7900 XT | Release to automatic control on exit verified, factory reset confirmed. **Fan regulation is not verified:** one fan-speed check at high duty (a commanded increase from 41 % to 43 %) did not pass in 1.1.0 testing. Until a re-test passes, treat it as unverified. |
| RDNA2 | RX 6400 | Reports no manual fan tuning support through ADLX, so the app does not control its fan. Nothing to validate on this card; this is not a statement about other RDNA2 cards. |

## Backend Order

Before either backend starts, the app decides once per process which `atiadlxx.dll` / `amdadlx64.dll` to use (log lines start `adl-preload:`):

- **Bound-line pre-load.** If every present AMD display device (`VEN_1002`) is bound to the same INF, the app resolves that driver package in the DriverStore and reads the two file names from the package INF's own `[SourceDisksFiles]` entries. When the package's copies differ from System32's and both pass `WinVerifyTrust` and the AMD signer pin, both are loaded by full path (`atiadlxx.dll` first). Later bare-name loads by ADLX then reuse those modules. This is what lets an RDNA1/2-line card work when System32 holds the RDNA3/4-line copies.
- **Fallback to v1.0 behaviour**, with one log line, when there is no AMD display, the bound copies match System32 (`NotNeeded`), either file is missing or fails the signature or signer check, the INF or file paths cannot be resolved, or the load fails. The fallback still loads `amdadlx64.dll` from System32 by full path, never by DLL search.

1. **ADLX backend**
   - Preferred for modern Radeon GPUs.
   - Expected path for RX 5000, RX 6000, RX 7000, and RX 9000-series cards with current Adrenalin drivers.
   - Uses ADLX GPU tuning and performance monitoring.

2. **ADL OverdriveN backend**
   - Fallback for cards where ADLX fan tuning is unavailable.
   - Intended to cover RX 470/480 and RX 570/580/590 Polaris-era cards when the driver exposes OverdriveN or CustomFan APIs.
   - Uses `atiadlxx.dll` from the already-loaded module (for example the bound pre-load) or from System32; the DLL search path is not used.
   - Used only for Overdrive version **7**. Version 7 behaviour is unchanged from v1.0, including its CustomFan preference.
   - **Overdrive8 adapters (version 8: Vega20, RDNA1 and newer) never use an ADL fan path.** The log says `Overdrive8 — no ADL fan backend; ADLX is the supported path.` They depend on ADLX alone.

3. **ADL Overdrive5 backend**
   - Best-effort fallback for older cards.
   - Uses legacy temperature and fan-speed APIs when available.
   - Used only for Overdrive version 5. Other versions are unsupported.

## Edge Conditions

- Fan telemetry may be reported as percent, RPM, or only as a commanded control state.
- Some cards expose temperature through ADLX but not fan tuning, requiring fallback.
- Some driver states require AMD Adrenalin Manual Tuning and Fan Tuning to be enabled.
- Zero RPM can prevent physical fan movement at low requests; the utility disables it when supported.
- Factory reset is intentionally not called during startup testing, because it can wipe user tuning profiles.
- Multi-GPU selection is not implemented yet; the first supported AMD adapter is used.
- **Mixed driver lines:** if present AMD display devices are bound to different INFs (for example a Ryzen iGPU plus a discrete card on a different package), nothing is pre-loaded and the log says `MixedDriverLines`. One process can host only one ADL/ADLX copy, so v1.0 behaviour applies.
- **Restore journal (`baseline.json`).** A forced stop, crash or power loss skips the exit restore, and ADLX `Reset()` in the next run would re-apply the killed run's curve. For ADLX GPUs the app therefore journals the pre-app per-state fan speeds and Zero RPM state, keyed by GPU name plus ADLX `UniqueId`, and marks the entry dirty before the first fan write. The next start recovers that state as its baseline, and `--restore-auto` restores it directly. Scope is ADLX only; the ADL backends restore by setting the driver's automatic mode. A corrupt journal is ignored (set aside as `baseline.json.bad`) and a failed write is logged; neither blocks fan control. Dirty entries for GPUs no longer present are dropped.
- Fan-control APIs can conflict with other tuning tools. Only one tool should own manual fan control at a time.

## Startup Test Rules

The startup test requests 35% fan speed, waits briefly, then passes if any of these is true:

- Command/control state reports roughly 35%.
- Physical fan percent reports roughly 35%.
- Physical RPM rises meaningfully from baseline.
- The set command succeeds but no fan telemetry is exposed, in which case the app logs a degraded pass.

If telemetry contradicts the request, the app disables its control loop and points the user to the troubleshooting help.

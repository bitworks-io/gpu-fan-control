# Verification Evidence

Preserve proof that the project works for real user/operator workflows. Keep entries concise; update when behavior, tests, deployment, or risks change.

## Acceptance Matrix

| Story | Acceptance Criteria | Automated Proof | Manual/Smoke Proof | Last Verified | Gaps |
| --- | --- | --- | --- | --- | --- |
| Core-temp target curve | Fan% clamps min…max, ramps over target…+25 °C, dead-band holds | `FanControlPolicyTests` | Real AMD HW (Phase 5) | 2026-07-03 (unit) | HW behavior unverified |
| Over-temp safety | ≥ critical → 85 %, hysteretic recovery, overrides manual | `FanControlPolicyTests` | Phase 5 | 2026-07-03 (unit) | HW unverified |
| Sensor-loss safety | 3 failed reads → relinquish to driver auto | `FanControlPolicyTests` | Phase 5 | 2026-07-03 (unit) | HW unverified |
| Multi-GPU enablement | primary default-on, new GPUs opt-in, saved prefs respected | `GpuEnablementTests` (11) | 2-GPU HW (Phase 5) | 2026-07-03 (unit) | HW unverified |
| Settings safety clamps | min∈[20,70], max∈[40,85], min<max, critical≥target+10 | `SettingsServiceTests` | — | 2026-07-03 (unit) | — |
| Installer ships native binding | `ADLXCSharpBind.dll` + `ADLXWrapper.dll` in publish/installer | CI "Verify ADLX runtime binaries are present" | Setup.exe = 2.4 MB PE32 | 2026-07-03 (CI) | — |
| Windows compile of full app | net8.0-windows app + installer build succeed | CI "Build installer" | — | 2026-07-03 (CI) | — |
| Exit restores automatic | Adrenalin shows auto on exit (all controlled GPUs) | lifecycle wiring only | Phase 5 (incl. two-GPU both-restore) | — | **HW unverified** |
| No-admin fan control | fan-set works as `asInvoker` (no elevation) | — | Phase 5 | — | **Assumption unverified** |

## Current Verification Commands

```sh
# Core unit tests (macOS: only .NET 10 runtime present, so roll forward)
DOTNET_ROLL_FORWARD=Major dotnet test LightweightAmdGpuFanControl.Tests/LightweightAmdGpuFanControl.Tests.csproj -c Release --nologo
```

```sh
# Windows build + installer (CI only; app is net8.0-windows)
gh run view <run-id> --json status,conclusion,jobs
gh run download <run-id> -n LightweightAmdGpuFanControl-Setup
```

## Latest Acceptance Pass

- Date: 2026-07-03
- Change or release: Phase 3 (multi-GPU per-channel architecture) + Phase 4 (About/Preferences UI, live tray tooltip). Commit `fe4e89c`.
- Criteria proven (this turn): pure policy, settings clamps, and GPU-enablement reconcile logic; full Windows compile of the WinForms app; installer builds with native ADLX binaries bundled.
- Commands run: `DOTNET_ROLL_FORWARD=Major dotnet test …` → **Passed: 26, Failed: 0**. CI run `28686185843` → conclusion **success** (core tests, build installer, verify ADLX binaries all green; sign skipped).
- Manual checks: downloaded `LightweightAmdGpuFanControl-Setup.exe` → `file` reports valid PE32 Windows GUI executable (2.4 MB).
- Coverage result: not measured (no coverage tooling wired yet).
- Skipped checks and reason: installer signing (no cert secret); all on-hardware behavior (no AMD GPU on this macOS host).
- Residual risk: **all runtime fan behavior, restore-to-auto, and the no-admin assumption are unverified** — they require a real AMD Windows PC (Phase 5 checklist in `agent-handoff.md`).

## Evidence Notes

- Installer artifact: CI run `28686185843`, artifact `LightweightAmdGpuFanControl-Setup`; local copy at `~/Downloads/LightweightAmdGpuFanControl-v1.0/`.
- Full Phase 5 hardware checklist lives in `docs/agent-handoff.md` → "Remaining Work".

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
# Windows build + installer (CI only; app is net48)
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

## 2026-11-08 — E1 (read-only probe) on rc.1: FAIL, rc.1 resolver defect; fixed for rc.2

- **Setup:** an RX 5700 XT (RDNA1) bound to the RDNA1/2 driver package (32.0.21045.16010), with System32 holding the RDNA3/4 package's `atiadlxx.dll` (7.26.20.1616) and `amdadlx64.dll` (1.5.0.124). That is the field mismatch. Declared and approved beforehand per plan §6, and run as the interactive user. Bench details are kept in private records.
- **Step 0 passed on the bench:** `Get-AuthenticodeSignature` showed `Valid`, signer `O=Bitworks LLC`, SHA-256 `8024270d…8e9d`.
- **Result:** `--probe` and `--probe --no-preload` both reported 0 ADLX GPUs (exit 3). The pre-load line read `Unresolved (atiadlxx.dll: disk path contains a relative segment)`.
  - The bound INF's `[SourceDisksNames.amd64]` entry is `1 = %DiskId%,,,.\B026653`, and rc.1's resolver refused the `.` segment. So the app ran exactly the 1.0.0 path.
  - **H3 is untested, not refuted.** No fan value was written.
- **Fix (rc.2):** a `.` segment is a no-op, and `..` stays refused. New tests use the exact INF text and check resolver → planner end to end. The new `.` tests fail against the rc.1 resolver and pass with the fix. `DOTNET_ROLL_FORWARD=Major dotnet test LightweightAmdGpuFanControl.Tests` → **Passed: 173, Failed: 0**.
- **Next:** E1 again on rc.2 (a new declaration). The rc.1 run is kept as a record, not retried.

## 2026-11-06 — v1.1.0-rc.1 release check (pre-hardware)

- **Tag:** annotated `v1.1.0-rc.1` on `06e4724` (main, push run `42235778427` green). The tag run `42246782110` passed: build (tests, installer, CI-only signing) and release.
- **GitHub:** `prerelease: true`, `draft: false`; `releases/latest` is still `v1.0.0`.
- **Installer:** `LightweightAmdGpuFanControl-Setup.exe`, 2,759,880 bytes, SHA-256 `8024270d9ff312d1d6d520a524f2d9e97d3441954ac1551f2b47aaadb3a68e9d`. This matches GitHub's asset digest. PE32 GUI, ProductVersion `1.1.0`.
- **Authenticode, checked on macOS:**
  - The PE image hash equals the signed digest, and messageDigest equals the hash of the signed content.
  - The RSA signature over the signed attributes verifies with the signer certificate.
  - Signer: `O=Bitworks LLC, CN=Bitworks LLC`, issued by Microsoft ID Verified CS AOC CA 03, then PCA 2021, then the Identity Verification Root 2020. The chain is consistent within the certificate bag.
  - It carries an RFC 3161 timestamp at 2026-11-06T15:54:18Z, inside the leaf certificate's 3-day validity.
  - v1.0.0's leaf was issued by EOC CA 04 rather than AOC CA 03, because Azure Artifact Signing rotates its intermediates. This does not affect the downstream installer's publisher check. That check is `WinVerifyTrust` with the full chain to a trusted root, plus the leaf subject `O=Bitworks LLC`. It compares no issuer, serial or thumbprint.
- **Not checked:** trust in the Windows trust store, and the timestamp token's own signature. Both are covered by E1 Step 0 (`Get-AuthenticodeSignature` on the bench before anything is installed).

## 2026-11-06 — 1.1.0 implementation (pre-hardware)

- **Core unit tests:** `DOTNET_ROLL_FORWARD=Major dotnet test LightweightAmdGpuFanControl.Tests` → **Passed: 161, Failed: 0** (net8.0). Covers the INF resolver, bound-line planner, signer pin, Overdrive routing, launch options and exit codes (including `--stop`), probe report, `status.json` model and `--status-file` validation, atomic file write, the restore journal and its staleness classification, and source guards for `--probe` (no fan writes) and `--stop` (open-only, never kills).
- **App compile (net48):** `dotnet build LightweightAmdGpuFanControl/LightweightAmdGpuFanControl.csproj -p:AdlxBindPath=<any existing file>` → **0 errors** (warnings only). This is a compile check, not a Windows run.
- **Workflow lint:** `actionlint .github/workflows/build-windows.yml` → clean.
- **Not run:** the Windows CI build and installer, and all on-hardware behaviour.
- **No hardware validation has run yet.** E1 (read-only probe), E2 (controlled engagement on RDNA1) and E3 (automation regression across generations) are pending approval; see `docs/superpowers/plans/2026-11-06-rdna1-fan-control.md` section 6. RDNA1 control, the bound-line pre-load, the restore journal and `--restore-auto` are unverified on real hardware.

## 2026-07-12 Phase 5 — first hardware test (RX 7900 XTX) + fixes

- **Hardware finding:** fan increased when raising min fan speed but would not slow back down when
  lowering it. Root-caused (static analysis, all ADLX paths read) to the Manual-mode ratchet in
  `SettingsService.Validate`; two UI/packaging issues also reported. Fixes staged, **not yet
  hardware-re-tested**.
- **Automated proof (this turn, macOS):** new regression test
  `SettingsServiceTests.Manual_fan_setpoint_survives_raising_then_lowering_min_fan` — watched it FAIL
  (`Expected 50, Actual 60`) pre-fix, PASS post-fix. Full suite `dotnet test …` → **Passed: 27, Failed: 0**.
- **Windows-only changes (CI-verify pending):** `FanController` percent-native curve + diagnostic
  logging; `PreferencesForm` TableLayoutPanel rework + `Program.cs` PerMonitorV2; `build.ps1`
  self-contained. None buildable on macOS.
- **Still BLOCKED:** on-hardware re-test of all three fixes + the original Phase 5 checklist.
  Diagnostic build writes fan-apply details to `%LOCALAPPDATA%/Bitworks/LightweightAmdGpuFanControl/log.txt`.

## 2026-07-06 Re-verification (release-mechanics prep)

- Non-hardware evidence re-confirmed green before staging the release:
  - Unit tests re-run (`DOTNET_ROLL_FORWARD=Major dotnet test …`) → **Passed: 26, Failed: 0** (net8.0).
  - CI run `28714260647` for commit `8247ca0` → conclusion **success**.
  - Version consistency confirmed `1.0.0` across `VERSION`, `.csproj`, `.iss`, `AboutForm.cs`.
  - Local installer artifact re-verified: `~/Downloads/LightweightAmdGpuFanControl-v1.0/LightweightAmdGpuFanControl-Setup.exe`
    → PE32 GUI, 2,474,080 bytes, SHA-256 `43c1d892103ee33299fd306adecd575a80352045dc280c1ea4a5fb2e436a2fef`.
    (Built from CI run `28686185843` / commit `fe4e89c` — a smoke sample, **not** the release binary;
    the v1.0.0 installer is built fresh from the tagged commit.)
- Still **BLOCKED**: all Phase 5 on-hardware acceptance (no AMD GPU on this macOS host).
- Release steps are now staged in `docs/release-runbook.md` (tag intentionally uncut).

## Evidence Notes

- Installer artifact: CI run `28686185843`, artifact `LightweightAmdGpuFanControl-Setup`; local copy at `~/Downloads/LightweightAmdGpuFanControl-v1.0/`.
- Full Phase 5 hardware checklist lives in `docs/agent-handoff.md` → "Remaining Work".
- Release mechanics / one-action tag procedure: `docs/release-runbook.md`.

## Evidence Hygiene

- Preserve acceptance proof, command names, dates, environments, skipped checks, and residual risk.
- Do not paste full logs, secrets, tokens, customer data, transient cache paths, or noisy command output.
- Store large artifacts in a project-appropriate artifact location and link them here with a short interpretation.
- Prefer one concise acceptance pass per meaningful change over repeating the same proof in multiple sections.

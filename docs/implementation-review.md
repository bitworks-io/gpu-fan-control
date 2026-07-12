# Implementation Review — Lightweight AMD GPU Fan Control

_Review date: 2026-07-03 · Reviewer: Claude Code (static review)_

## Scope & method

- **Static source review**, plus **CI artifact inspection** (see below). Review host is macOS; the project cannot be built or run here (Windows + .NET + ADLX native), but the CI-produced binaries were downloaded and inspected.
- **CI is verified green on HEAD.** Using the `bitworks-io` gh account (the repo is **private** under `bitworks-io`; the `tyria-tech` account gets a 404), run `27794764644` for commit `4ab96d5` "Apply codex-env baseline refresh" succeeded (6m15s), preceded by two more successes; the only failures were early ADLX-path issues on 2026-04-30, since fixed. Both artifacts were produced: `LightweightAmdGpuFanControl-Setup` (installer, 1.85 MB) and the publish output.
- **But the green build ships broken (see B1).** The publish artifact was downloaded and inspected: it contains `ADLXWrapper.dll` but **not** the native `ADLXCSharpBind.dll`. This is a confirmed defect, detailed below.
- Terminology note: the request called this a "Windows service." It is **not** — it is a per-user WinForms **systray application** (`OutputType=WinExe`, `Program.cs:14`) autostarted via an HKCU Run key. The README correctly calls it a "systray utility." Implications are covered in Part 2.

---

## Part 1 — Build & install readiness

**Verdict: the pipeline builds green, but the installer it produces is functionally broken (missing native DLL) and is not release-ready.** CI runs end-to-end (build ADLX native → build app → compile Inno installer → upload artifacts) and the ADLX submodule is present and initialized — but the shipped output is missing `ADLXCSharpBind.dll` (B1, confirmed by artifact inspection), and signing/versioning/verification gaps remain.

### What's in place (green)
- CI `.github/workflows/build-windows.yml`: triggers on `push`/`pull_request`/`workflow_dispatch`, runs on `windows-latest`, checks out `submodules: recursive`, sets up .NET 8 + MSBuild, installs SWIG + Inno Setup via choco, runs `build.ps1`, and uploads the installer + publish output as artifacts.
- ADLX submodule present & initialized (`external/ADLX`, `v1.0-66-g240b1f3f`); both the C++ binding (`ADLXCSharpBind`) and C# `ADLXWrapper` projects are on disk.
- Installer (`installer/LightweightAmdGpuFanControl.iss`): non-admin (`PrivilegesRequired=lowest`), installs to `%ProgramFiles%\Bitworks\...`, optional (default-unchecked) "start with Windows" via HKCU Run, uninstall cleans `%LOCALAPPDATA%`.

### Blockers / risks (ordered by severity)

| # | Issue | Evidence | Impact |
|---|-------|----------|--------|
| B1 | **CONFIRMED — shipped installer is missing the native `ADLXCSharpBind.dll`.** `CopyAdlxNativeLibs` copies to `$(OutputPath)` (build dir) `AfterTargets="Build"`, but the installer + CI artifact draw from the `publish\` dir; `dotnet publish` doesn't carry that ad-hoc drop into `publish\`, and the source-path `Condition` may not match the C++ output, so it silently skips. Verified by downloading the green run's publish artifact: `ADLXWrapper.dll` present, `ADLXCSharpBind.dll` absent. | `csproj:25-30`; `.iss:35-38` (sources `publish\*.dll` only); `build.ps1:78` | Runtime P/Invoke → `DllNotFoundException` → ADLX init fails → fallback to legacy ADL or, on modern RDNA, **no fan control at all**. Builds green but doesn't work. **Highest-priority fix.** |
| B2 | **No post-build smoke test.** CI uploads artifacts but never asserts the publish/installer output contains the ADLX DLLs — which is exactly why B1 shipped green (3×) undetected. | workflow has no verify step | A broken/incomplete installer is published undetected (as happened). |
| B3 | **Unsigned binaries.** No Authenticode signing of the exe or installer. | no signing step in CI/iss | SmartScreen / Defender warnings on download & first run; some managed environments block outright. |
| B4 | **Static version + no releases.** Version hardcoded `1.0.0` in both `.csproj:16` and the `.iss`; CI publishes only Actions artifacts (retention-limited), not GitHub Releases. | `csproj:16`, `.iss` | No durable download URL; no version traceability across builds. |
| B5 | **Elevation requirement unverified.** No `app.manifest` / `requestedExecutionLevel` anywhere; app runs `asInvoker`. AMD fan tuning via ADL OverdriveN sometimes requires elevation. | no manifest in tracked source | If the driver requires admin for fan *set*, the non-elevated app silently fails its self-test and control stays off (handled with a balloon tip, but easy to miss). Needs runtime confirmation. |
| B6 | **Zero automated tests.** Only `FanControlTestService` exists, and that is a runtime *hardware* self-test, not a unit test. The repo's own `.claude/rules/testing.md` requires tests. | `git ls-files` | No regression protection for the control math, settings validation, or backend selection. |

---

## Part 2 — Functional review: automatic fan control toward a target temperature

**Verdict: the requested core capability IS implemented — but minimally, and with several correctness/safety design gaps that a fan controller should not ship with.**

### What works
- **Closed-loop target-temp control exists.** `FanControlService.PollAndAdjust()` polls every 2.5 s, reads GPU temp, and below target holds min fan; at/above target ramps fan linearly from 20% → 85% across `target … target+25 °C`. Ramp math is correct (double arithmetic reaches exactly 85% at target+25).
- **Live-applied target.** `SettingsService` caches settings and shares one instance between the preferences form and the poll loop, so changing the target takes effect on the next poll with no restart and no repeated disk I/O.
- **Two backends with graceful fallback:** ADLX (modern RDNA/Adrenalin) → legacy ADL Overdrive5/OverdriveN (Polaris-era), selected by `FanControlBackendFactory`.
- **Startup hardware self-test** and a Help path pointing users to enable AMD Manual Tuning.

### Control-variable problem (should be treated as a headline gap)
- **The tool controls on EDGE temperature, not hotspot/junction.** ADL explicitly requests `TemperatureTypeEdge` (`AdlFanControlBackend.cs:13,87`); ADLX returns `metrics.GPUTemperature` (edge). On RDNA cards the **hotspot/junction** sensor runs ~20–30 °C above edge and is the actual thermal limiter. Targeting edge 65 °C can mean hotspot ~90 °C+, so the "target" the user sets does not correspond to the temperature that actually governs throttling. This is arguably the wrong control variable, or at minimum needs to be surfaced/selectable.

### Safety & robustness gaps
_Framing: these cause **loss of cooling authority → running hot / throttling / degraded thermals**, not hardware destruction — GPUs self-protect by throttling and emergency-shutdown (~110 °C junction). They are still real design defects._

| # | Gap | Evidence |
|---|-----|----------|
| S1 | **Fan is capped at 85%, never 100%.** Even at `target+25 °C` (up to 115 °C for a 90 °C target) the controller can't command full speed, and there's no emergency "force max on over-temp" branch. | `FanControlService.cs:14` `MaxFanPercent=85`; also enforced in both backends and the ADLX `FanController`. |
| S2 | **No failsafe on sensor-read failure.** `if (!temp.HasValue) return;` leaves the fan at its last commanded value. If the sensor read fails while the GPU is hot, the fan can stay pinned low. Safer designs ramp to max on read loss. | `FanControlService.cs:92` |
| S3 | **Manual mode is never released on exit/crash.** `Stop()`/`Dispose()` just unload the API; neither backend restores automatic/driver fan control. ADL explicitly sets `iMode = ManualMode` and leaves it. So after the app exits (or crashes) with the fan at 20% idle, the GPU can remain in **manual mode at 20% with no controller running**. (Whether the ADLX driver reclaims control on process death is uncertain and should be verified — but the code has no explicit restore-to-auto, which is the defect regardless.) | ADLX `Dispose` `AdlxFanControlBackend.cs:89-96`; ADL `Dispose` `AdlFanControlBackend.cs:241-247`; `iMode=ManualMode` `AdlFanControlBackend.cs:171` |
| S3b | **The startup self-test compounds S3.** `FanControlTestService.RunTest()` sets the fan to 35% and **never restores it**, and runs synchronously (with a 4 s `Thread.Sleep`) inside the tray-context constructor — blocking the UI thread for ~4 s at launch. On test **failure**, the fan is left pinned at 35% while `_controlEnabled=false`, i.e. a concrete instance of the "stuck in manual, controller off" state. | `FanControlTestService.cs:22-24`; invoked at `FanControlService.cs:43`, from ctor `SystrayApplicationContext.cs:35` |
| S4 | **Silent "running but doing nothing" state.** If the self-test fails, `_controlEnabled=false` but the poll timer still runs and no-ops. The tray icon looks normal; the only signal is a transient balloon tip. | `FanControlService.cs:50-57, 85-86` |

### Feature gaps vs. a complete fan-control tool
The core "auto control toward a target temp" is present, but relative to user expectations set by Adrenalin / MSI Afterburner / Argus Monitor, these are missing:
1. **No custom fan curve.** Curve is a single fixed linear ramp; the only user-adjustable value is target temp. `MinFanPercent=20`, `MaxFanPercent=85`, `RampTempRange=25` are hardcoded constants (`FanControlService.cs:13-15`). No multi-point curve, no adjustable min/max, no adjustable slope/aggressiveness. (This is by design per the README, but it is a real functional limitation.)
2. **No passive / Zero-RPM idle.** Min fan is forced to 20% and the app actively disables Zero RPM (`DisableZeroRpm`), so silent idle is impossible — a common request for this class of tool.
3. **No live status.** No current temp or fan% is shown anywhere in the UI. `GetFanTelemetry()` is used only by the self-test; the tray tooltip is static. Users can't see what the tool is doing.
4. **No runtime enable/disable or manual fixed-speed override** from the tray menu.
5. **No hysteresis / smoothing / slew-rate limiting.** Fan% is recomputed from the instantaneous temp every poll, so near the target boundary the fan will hunt (oscillate) audibly.
6. **No multi-GPU selection.** `GetPrimaryGpu()` always takes GPU[0].
7. **Not a service.** Being a per-user tray app, there is no fan control at the logon screen, before any user logs in, or in other users' sessions. (A true service is genuinely harder here because ADL/ADLX generally expect a desktop session — worth a design decision, not a trivial fix.)

---

## Priority recommendations

**Before build/publish (readiness):**
1. **Fix B1 (blocker).** Get `ADLXCSharpBind.dll` into the `publish\` output — e.g. include it as a `<Content>` item with `CopyToPublishDirectory=PreserveNewest` (and/or an `AfterTargets="Publish"` copy), pointed at the C++ project's *actual* output path — and make it a **hard failure** if the file is missing rather than a silent `Condition` skip. Then add a CI verify step (B2) that asserts the ADLX DLLs are present in the publish/installer output.
2. Confirm at runtime whether elevation is required for fan-set (B5). _(CI-green is already confirmed: run `27794764644` on HEAD `4ab96d5`.)_
3. Decide on signing + a versioning/release scheme before external distribution (B3, B4).

**Before trusting it to control a real GPU unattended (safety):**
4. Restore automatic/default fan control on exit and on crash (S3), and don't leave the self-test fan value pinned (S3b).
5. Add an over-temp failsafe: allow 100% and force max fan on over-temp or on sensor-read loss (S1, S2).
6. Reconsider controlling on hotspot/junction rather than edge, or at least expose the choice (control-variable problem).

**To meet user expectations for "automatic fan control" (features):**
7. Live temp/fan readout in the tray + a runtime pause/manual toggle (S4, gaps 3–4).
8. A configurable curve (min/max/points) and hysteresis/smoothing (gaps 1, 5).

**Testing:** add unit tests for the ramp math, settings clamping, and backend selection to satisfy the repo's own testing rules (B6).

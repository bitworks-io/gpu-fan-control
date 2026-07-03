# Design Spec — Lightweight AMD GPU Fan Control, v1.0 Release Readiness

_Date: 2026-07-03 · Status: approved scope, pending spec review · Supersedes findings in [docs/implementation-review.md](../../implementation-review.md)_

## 1. Goal & user story

**Operator story:** A Windows user with one or more supported AMD Radeon GPUs installs a lightweight tray utility that automatically keeps each GPU's **core temperature** near a configurable target (default 65 °C) by ramping the fan, safely returns fan control to the driver when it exits or crashes, and never leaves the card in an unmanaged manual state.

**"Ready for release" (v1.0) means:** the installer produced by CI actually works on real hardware (ADLX path included), the control loop is safe under failure and shutdown, the user can see what it's doing and adjust it, multiple GPUs are supported, and the core behavior is covered by automated tests.

## 2. Locked decisions (from brainstorming)

| Decision | Choice |
|---|---|
| Control variable | **Core (edge / "GPU") temperature** — `metrics.GPUTemperature` (ADLX), `TemperatureTypeEdge` (ADL). No hotspot/junction. |
| Default target | **65 °C** (existing `DefaultTargetTempC`) |
| Deployment | **Per-user systray app**, autostart via HKCU Run (unchanged) |
| Fan config | **Shared global curve**: target + min/max fan %, applied to every enabled GPU |
| Max fan | Default **85 %**, and **85 % is the hard upper bound** (manufacturer guidance). User may set a *lower* max; never higher. |
| Min fan | Default **20 %**, floor **≥ 20** (no Zero-RPM / passive idle in v1.0) |
| Emergency response | On over-temp, jump to **configured max (default 85 %)** — never 100 % |
| Multi-GPU | Single supported GPU controlled by default; additional GPUs opt-in; shared curve |
| Signing | **Unsigned** v1.0; CI signing step wired but no-op until a cert secret exists |

## 3. Verified technical facts (checked against the code/submodule before writing this spec)

- **ADLX restore-to-auto EXISTS.** `ADLXWrapper.ManualFanTuning.Reset()` ([ManualFanTuning.cs:77-83](../../../external/ADLX/Samples/csharp/ADLXWrapper/ManualFanTuning.cs)) restores the fan tuning states and Zero-RPM state captured **when the `ManualFanTuning` object was constructed**. The app constructs it in `FanController.Initialize()` before any fan write, so the captured baseline is the pre-app (driver-default) curve. → App-side plumbing only; **no submodule change**.
- **ADL restore-to-auto:** OverdriveN = set `ADLODNFanControl.iMode = 0` (Default/Auto) via the existing `OverdriveNFanControlSet` delegate ([AdlNativeApi.cs:130-133,163](../../../LightweightAmdGpuFanControl/Gpu/Adl/AdlNativeApi.cs)) — no new binding. Overdrive5 = add one new delegate `ADL2_Overdrive5_FanSpeedToDefault_Set` (not currently declared).
- **Complete native dependency set (ADLX path):** the SWIG bindings P/Invoke `[DllImport("ADLXCSharpBind", …)]` ([ADLXPINVOKE.cs:36](../../../external/ADLX/Samples/csharp/ADLXWrapper/Bindings/ADLXPINVOKE.cs)). Must bundle: **`ADLXCSharpBind.dll`** (native) + **`ADLXWrapper.dll`** (managed, already present). Driver-provided (do **not** bundle): `amdadlxs64.dll` (ADLX runtime), `atiadlxx.dll`/`atiadlxy.dll` (ADL). **Verify at implementation:** `ADLXCSharpBind.dll` CRT linkage — if it links the dynamic MSVC runtime, the target needs the VC++ redist; prefer static `/MT` or bundle the redist.

## 4. Architecture

### 4.1 Pure control policy (new, testable)
Extract control math from `FanControlService` into **`FanControlPolicy`** — no hardware dependency:

```
DesiredFan Decide(TempReading reading, int previousFanPercent, GpuCurveSettings settings, PolicyState state)
```
- `reading`: `double? tempC` (null = failed read) + timestamp/tick.
- Returns the fan % to command **or** a `RelinquishToAuto` signal (see 4.3).
- Owns: ramp (min→max across `target … target+RampRange`), min/max clamp, hysteresis band, over-temp emergency, and consecutive-failed-read tracking.
- `PolicyState` (per GPU): `previousFanPercent`, `consecutiveReadFailures`, `emergencyLatched`. Kept per GPU so multi-GPU channels don't share state.

`FanControlService` becomes: for each enabled GPU channel → read temp → `policy.Decide(...)` → apply (`SetFanPercent` or `RestoreAutomaticFanControl`) → repeat on the 2.5 s timer. It also owns lifecycle (§4.4).

### 4.2 Backend interface changes
`IFanControlBackend` gains:
- **`void RestoreAutomaticFanControl()`** — ADLX: `FanController.RestoreAutomatic()` → `ManualFanTuning.Reset()`. ADL OverdriveN: set `iMode = 0`. ADL OD5: `FanSpeedToDefault` (new delegate). Best-effort, must not throw.
- **`string GpuId`** + existing `AdapterName` — stable per-GPU identity for settings (see §5.3). Use the most stable id the ADLX/ADL wrapper exposes (ADLX GPU unique id/name; ADL adapter index+name); confirm exact field at implementation.

The hardcoded `MinFanPercent=20 / MaxFanPercent=85` constants in `FanController.cs`, `AdlxFanControlBackend.cs`, and `AdlFanControlBackend.cs` are **removed**; bounds come from settings and are passed to `SetFanPercent`. Backends still defensively clamp to the absolute floor 20 / ceiling 85.

### 4.3 Multi-GPU (single family, shared init)
- **One backend family for the whole system**: probe ADLX first; if it initializes, enumerate **all** ADLX GPUs from that **single** `AdlxInitializer`/`SystemServices` and create one control channel per GPU (each its own `FanController` + `GpuMonitor` view). Only if ADLX is unavailable, fall back to ADL and enumerate its adapters. **Never mix** ADLX for one GPU and ADL for another.
- `FanControlBackendFactory.CreateAll()` → `IReadOnlyList<IFanControlBackend>` (one per supported GPU), or an equivalent "system + channels" shape. Verify ADLX tolerates multiple per-GPU `ManualFanTuning` objects from one init (expected: `GetGPUs()` returns the list; `GetManualFanTuning(gpu)` is per-GPU).
- Enabled set determined by settings (§5.3). Disabled GPUs are never written to.

### 4.4 Safety lifecycle
- **Restore-to-auto on all exits:** normal Exit, `SystemEvents.SessionEnding` (logoff/shutdown), `AppDomain.CurrentDomain.UnhandledException`, `Application.ThreadException`, and `ProcessExit`. Each calls `RestoreAutomaticFanControl()` for every channel the app engaged. Idempotent + swallow errors.
- **Residual risk (documented):** a hard kill (Task Manager End Task, power loss) can't run handlers. Mitigation: the driver reclaims control on driver reset/reboot, and the app re-establishes control within ~1 s on next launch. Stated in README + help.
- **Startup self-test:** capture prior fan state, run the 35 % test, then **restore the captured state** (do not leave it pinned). Run **off the UI thread** so launch is not blocked ~4 s. On failure, still leave the card in auto (relinquish), not pinned.

### 4.5 Failure semantics (decided)
- **Over-temp emergency** (temp read *succeeds* and `tempC ≥ CriticalTempC`): jump immediately to **configured max (default 85 %)**, bypass ramp. Latch until temp falls below `CriticalTempC − hysteresis`. Reads work, so writes work.
- **Sensor-loss** (≈3 consecutive failed reads ≈ 7.5 s): **relinquish to the driver's automatic curve** via `RestoreAutomaticFanControl()`, not "pin max." Rationale: reads and writes share the same ADLX connection — if reads fail, writes likely fail too, so pinning max may be a no-op; the driver has an independent sensor and its own thermal protection and is the correct backstop. Surface a warning (balloon + log). Resume automatic control once reads recover.

## 5. Settings & data

### 5.1 Model (`AppSettings`)
Global curve fields (shared across enabled GPUs):
- `TargetTempC` (default 65, clamp 50–90)
- `MinFanPercent` (default 20, clamp 20–70)
- `MaxFanPercent` (default 85, clamp `max(MinFanPercent+5, 40)`–85)
- `HysteresisC` (default 3, clamp 1–10)
- `CriticalTempC` (default 90, clamp `TargetTempC+10`–100)
- `StartWithWindows` (existing)
- `Gpus`: list of `{ GpuId, Enabled }` (see §5.3)
- `Mode`/manual override (see §6.3): `Auto` (default) or `Manual` with `ManualFanPercent`.

### 5.2 Validation
Extend `SettingsService.Validate` to clamp every field and enforce `MinFanPercent < MaxFanPercent`. Keep the existing "corrupt file → defaults" behavior.

### 5.3 Multi-GPU enablement & migration
- On startup, enumerate supported GPUs and reconcile with `settings.Gpus` by `GpuId`.
- **Default-on rule keys off "no saved preference for this GpuId," not "settings file absent."** "Primary" = the **first-enumerated** supported GPU (today's `GetPrimaryGpu()` = index 0). For a GPU with no stored entry: enable it **iff** it is the primary GPU **and** no other GPU is currently enabled in settings; additional newly-seen GPUs default disabled. (So a fresh install enables exactly the primary card; hot-adding a second card later leaves it opt-in.)
- **Back-compat:** an old `settings.json` containing only `TargetTempC` + `StartWithWindows` must deserialize with defaults for all new fields (System.Text.Json already does this for missing properties; add a test). Existing users keep their target and get sensible defaults for the rest, with their single GPU enabled.

## 6. UI (WinForms, minimal)

### 6.1 Preferences form
- Relabel target to **"Core GPU temperature (°C)"**.
- Add **Min fan %** and **Max fan %** numeric controls (bounds per §5.1) with `Min < Max` validation on OK.
- Add a **detected-GPU list** (checked list): each supported GPU by adapter name, checkbox = enabled. Primary pre-checked on first run.
- Add a **live status line** (timer-updated ~1 s): current core temp + commanded fan % (+ physical RPM/% when available) for the selected/primary GPU. Sourced from existing `GetTemperatureC()` + `GetFanTelemetry()`.

### 6.2 Tray
- **Tooltip** updates with current temp + fan % (primary GPU).
- Context menu adds **Pause / Resume** (relinquish to auto while paused) and **Manual fixed speed…** (set a fixed % within min/max; `Mode = Manual`).

### 6.3 Manual override
`Mode = Manual` makes the policy return `ManualFanPercent` (clamped to min/max) regardless of temp, except the over-temp emergency and sensor-loss failsafe still apply. `Pause` = relinquish to driver auto and stop writing until resumed.

### 6.4 Feedback & About
Encourage feedback and feature requests, and make it one click to reach us.
- **Single link constant:** `AppLinks.ContactFormUrl` = `https://bitworks.io/contact-us/` (the bitworks.io contact form). All feedback entry points use this one constant. Opened via the existing safe pattern: `Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true })` wrapped in try/catch (mirrors `OpenFanControlHelp`).
- **In Preferences (§6.1):** a **"Send feedback / request a feature"** link-button plus a short encouraging line (e.g., *"Ideas and feedback are welcome — tell us what you'd like to see."*). Placed at the bottom of the form, visually separated from the fan controls.
- **About dialog (new):** reachable from the tray context menu (**"About…"**) and from a button in Preferences. Contents:
  - App name + **version** (read from the assembly, so it tracks the single-source version in §7.3), publisher **"Bitworks"**, and a one-line description.
  - A prominent **"Send feedback / request a feature"** button → `ContactFormUrl`.
  - A plain link to `https://bitworks.io`.
  - Encouraging copy inviting bug reports and feature requests.
- The tray context menu gains an **"About…"** item (menu now: Preferences · Pause/Resume · Manual fixed speed… · Help · About… · Exit).
- No new persisted settings; these are outbound links only.

## 7. Build, packaging & release

### 7.1 B1 — native DLL into publish (release blocker)
- Add `ADLXCSharpBind.dll` to publish output as a **`<Content Include=… CopyToPublishDirectory="PreserveNewest">`** item pointed at the C++ project's **actual** output path (verify path; current `CopyAdlxNativeLibs` target's assumed path may be wrong).
- **Separate hard-fail guard:** a standalone `<Target BeforeTargets="Publish"><Error Condition="!Exists('$(AdlxBindPath)')" Text="ADLXCSharpBind.dll missing — build the ADLX C++ project first"/></Target>`. The `Content` item alone silently includes nothing if the path is wrong — same trap as today — so the explicit `<Error>` is required, not optional.
- Confirm the installer `.iss` picks up `ADLXCSharpBind.dll` from `publish\` (its `*.dll` glob will, once the file is there).

### 7.2 B2 — CI verification
Add a CI step after build that asserts **both** `ADLXCSharpBind.dll` and `ADLXWrapper.dll` exist in the publish output (and ideally inside the built installer). Fail the job if either is missing. This closes the exact class of bug that shipped green 3×.

### 7.3 B4 — versioning & release
- Single source of version (e.g., a `VERSION` file or CI variable) propagated to `.csproj` `<Version>` and the `.iss` `MyAppVersion`.
- On tag push (`v*`), create a **GitHub Release** and attach `LightweightAmdGpuFanControl-Setup.exe`.

### 7.4 B3 — signing hook (no-op until cert)
CI signing step guarded by presence of a `CODE_SIGNING_*` secret; skipped cleanly when absent. Document SmartScreen behavior for the unsigned v1.0 in README.

### 7.5 B5 — elevation
Add an explicit `app.manifest` with `requestedExecutionLevel level="asInvoker"`. Improve the failure message when fan-set is rejected (permissions/Manual-Tuning). **Assumption (unverified):** ADLX/ADL fan-set works without elevation (Adrenalin tunes un-elevated). **Fallback:** if real-hardware testing shows fan-set needs admin, change the manifest to `requireAdministrator` **and** the installer `PrivilegesRequired` — noted as a test-gated decision.

## 8. Testing
- **`FanControlPolicy` unit tests:** below-target holds min; at/above target ramp endpoints (min at target, max at target+RampRange); hysteresis prevents flip near boundary; over-temp latches to max and releases; consecutive-failed-reads triggers relinquish; manual mode; clamping.
- **`SettingsService` tests:** clamps each field; `Min<Max`; corrupt file → defaults; **old-schema `settings.json` deserializes with defaults** (§5.3 migration).
- **`FanControlBackendFactory` / multi-GPU tests:** selection + per-GPU channel enablement + default-on rule, via a fake `IFanControlBackend` (no hardware).
- Hardware-dependent paths (real ADLX/ADL) remain covered by the runtime self-test; documented as manual verification on a real AMD PC.
- **Post-implementation build proof:** re-run CI, re-download the publish/installer artifact, and confirm `ADLXCSharpBind.dll` + `ADLXWrapper.dll` are present (the same method that found the bug).

## 9. Out of scope for v1.0 (fast-follow)
Windows-service mode · full multi-point custom curve · Zero-RPM / passive idle (min stays ≥ 20) · per-GPU curves · hotspot/junction targeting.

## 10. Risks
- **Elevation assumption** (§7.5) — test-gated.
- **`ADLXCSharpBind.dll` CRT dependency** (§3) — may require static CRT or VC++ redist; verify vcxproj `RuntimeLibrary`.
- **ADLX per-GPU multi-channel** from a single init — expected to work; verify on multi-GPU hardware (or at least that a single-GPU path is unchanged).
- **Crash/hard-kill restore** — best-effort only; mitigated by re-establish-on-launch and driver reclaim (§4.4).

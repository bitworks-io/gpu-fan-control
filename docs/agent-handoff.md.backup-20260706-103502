# Agent Handoff

Last reviewed: 2026-07-03

This document is for the next implementation agent (Codex, Claude Code, or human). Keep it current when architecture, workflows, verification commands, release steps, or known risks change.

## Project Purpose

**Lightweight AMD GPU Fan Control** is a per-user Windows systray utility (.NET 8 WinForms, `OutputType=WinExe` — not a service) that drives AMD Radeon GPU fans toward a **core (edge) temperature target** (default 65 °C). It exists because AMD Adrenalin's fan curve UI is heavy and awkward; this tool gives a lightweight always-on curve with a safe restore-to-automatic lifecycle. Publisher: Bitworks (bitworks.io).

## Current Feature Set

- **Automatic curve**: ramps fan from Min% → Max% across `target … target+25 °C`; hysteresis dead-band; emergency latch to Max% at/above the critical temp with hysteretic un-latch; sensor-loss → relinquish to driver automatic.
- **Fan bounds (hard, by manufacturer guidance)**: floor **20 %** (no Zero-RPM/passive idle), ceiling **85 %** (recommended top end; not user-raisable to 100 %).
- **Multi-GPU**: default-on for the primary GPU; additional GPUs are opt-in. Full multi-GPU on the modern **ADLX** path; the legacy **ADL** fallback controls the **primary GPU only** (avoids driving one physical GPU through duplicate display adapters).
- **Manual fixed speed** mode (tray) and **Automatic** mode toggle.
- **Pause/Resume** (tray) — hands all GPUs back to driver automatic.
- **Restore-to-automatic** on every graceful exit path (ProcessExit, UnhandledException, ThreadException, SessionEnding) and on Stop/Dispose.
- **Preferences**: core temp target, min/max fan, start-with-Windows, per-GPU checklist, live status readout, feedback link, About button.
- **About** dialog + Preferences link to the Bitworks contact form for feedback/feature requests.
- **Start with Windows** (HKCU Run key).

## User Stories And Acceptance Evidence

| Story | Acceptance Criteria | Automated Proof | Manual/Smoke Proof | Last Verified | Gaps |
| --- | --- | --- | --- | --- | --- |
| As a user, fan follows a core-temp target curve | Fan% = clamp(min…max) ramping over target…+25 °C; dead-band holds | `FanControlPolicyTests` (ramp endpoints, mid-ramp, dead-band) | Pending on real AMD HW (Phase 5) | 2026-07-03 (unit) | Hardware behavior unverified |
| Over-temp safety | temp ≥ critical → 85 %; hysteretic recovery; overrides manual | `FanControlPolicyTests` (latch/unlatch, manual override) | Pending HW | 2026-07-03 (unit) | HW unverified |
| Sensor loss is safe | 3 failed reads → relinquish to driver auto | `FanControlPolicyTests` (sensor-loss) | Pending HW | 2026-07-03 (unit) | HW unverified |
| Primary GPU on, others opt-in | reconcile: primary default-on, new GPUs default-off, saved prefs respected | `GpuEnablementTests` (11 cases) | Pending 2-GPU HW | 2026-07-03 (unit) | HW unverified |
| Exit restores automatic control | On exit, Adrenalin shows automatic fan control (all controlled GPUs) | — (lifecycle wiring) | Pending HW | — | **HW unverified**; incl. two-GPU both-restore |
| Settings clamp to safe ranges | min∈[20,70], max∈[40,85], min<max, critical≥target+10 | `SettingsServiceTests` | — | 2026-07-03 (unit) | — |
| Installer ships native binding | `ADLXCSharpBind.dll` + `ADLXWrapper.dll` present in publish/installer | CI step "Verify ADLX runtime binaries are present" | Setup.exe built (2.4 MB PE32) | 2026-07-03 (CI) | — |
| Feedback reachable | About + Preferences open bitworks.io/contact-us/ | — | Pending HW/desktop | — | Unverified |

## Key Components

- `LightweightAmdGpuFanControl.Core/` (**net8.0**, no WinForms — cross-platform testable):
  - `Control/FanControlPolicy.cs`: pure stateless decision function (`Decide(TempReading, AppSettings, PolicyState) → FanDecision`).
  - `Control/GpuEnablement.cs`: pure `Reconcile(detectedIds, settings) → IReadOnlyList<GpuConfig>` (primary default-on, new GPUs opt-in, saved prefs respected).
  - `Models/AppSettings.cs`: settings + all range constants. `Services/SettingsService.cs`: load/save + `Validate` clamps; **caches**, so a shared instance applies UI changes live to the poll loop.
  - `AppLinks.cs`: `ContactFormUrl`, `WebsiteUrl`, `FanHelpUrl`, `Open(url)`.
- `LightweightAmdGpuFanControl/` (**net8.0-windows** WinForms app):
  - `Gpu/FanControlBackendFactory.cs`: `CreateAll(log) → FanControlChannelSet?`. Prefers ADLX (all GPUs), falls back to ADL (primary only). Never mixes families.
  - `Gpu/FanControlChannelSet.cs`: **owns the single native session** + per-GPU channels; Dispose restores every channel then tears down the session (order matters).
  - `Adlx/AdlxSession.cs`: owns one ADLX init + shared `SystemServices` + `GpuMonitor`. `Gpu/AdlxFanControlBackend.cs`: a **borrowing channel** per GPU (owns only its `FanController`, never the session).
  - `Gpu/Adl/AdlFanControlBackend.cs`: legacy ADL channel; `TryCreateForAdapter(sharedApi, index, log)` borrows a shared `AdlNativeApi`.
  - `Services/FanControlService.cs`: the control loop. One `ControlChannel` (backend + `PolicyState` + flags) per GPU. **Single `_controlLock` serializes ALL native access** (startup tests, poll, restore) — the shared context is not thread-safe. Poll gated until startup tests complete. Publishes `GpuStatus` snapshots via `LatestStatuses` for the UI (UI never calls the backend directly).
  - `Forms/PreferencesForm.cs`, `Forms/AboutForm.cs`, `SystrayApplicationContext.cs`: UI.
- `LightweightAmdGpuFanControl.Tests/` (**net8.0** xUnit): 26 tests over policy, settings, enablement.
- `external/ADLX/` (submodule): AMD ADLX SDK + C# wrapper. `ManualFanTuning.Reset()` captures/restores pre-app fan state.

## Local And Hosted Test Commands

```sh
# Core unit tests (runnable on macOS/Linux; only .NET 10 runtime here, so roll-forward)
DOTNET_ROLL_FORWARD=Major dotnet test LightweightAmdGpuFanControl.Tests/LightweightAmdGpuFanControl.Tests.csproj -c Release --nologo
# → Passed! Failed: 0, Passed: 26
```

The WinForms app targets `net8.0-windows` and **cannot be built on macOS/Linux**. Windows compile + installer build happen in CI:

```sh
gh run list --branch feature/v1.0-release-readiness --limit 1     # find run
gh run view <id> --json status,conclusion,jobs                    # confirm green
gh run download <id> -n LightweightAmdGpuFanControl-Setup         # get Setup.exe
```

## Review Findings

- **B1 (broken installer) — FIXED & VERIFIED.** The old `AfterBuild` copy target dropped `ADLXCSharpBind.dll` into `$(OutputPath)` but `dotnet publish` uses `publish/`, so the native binding was silently missing. Fixed with a `<Content CopyToPublishDirectory=PreserveNewest>` item + a `<Target BeforeTargets=Publish><Error/>` hard-fail guard, plus a CI step asserting the binaries exist. Confirmed present in the post-fix artifact.
- **Multi-GPU ownership** (advisor-reviewed): single-owner model — one native session, N borrowing channels — chosen deliberately to make AMD-SDK multi-init ref-counting questions irrelevant. `FanControlChannelSet` is the sole owner.
- CI green at commit `fe4e89c` (2026-07-03): core tests, installer build, ADLX-binary verification all pass; signing skipped (no cert yet).

## Security Posture

- **No elevation assumed**: `app.manifest` uses `asInvoker`; installer `PrivilegesRequired=lowest`. **Phase 5 must confirm** fan-set works without admin; if it requires admin, switch manifest to `requireAdministrator` + installer `PrivilegesRequired=admin`.
- No network calls except user-initiated browser opens to bitworks.io. No telemetry, no secrets.
- Native P/Invoke to AMD driver DLLs (`atiadlxx.dll`, ADLX). Driver-provided DLLs are **not** bundled (loaded from system); only the SWIG binding (`ADLXCSharpBind.dll`) and managed wrapper (`ADLXWrapper.dll`) ship.
- **Unsigned build** → Windows SmartScreen will warn. Signing is a gated CI hook (`SIGN_CERT_BASE64` secret) that is currently a no-op.

## Operational Notes

- Settings at `%LOCALAPPDATA%/Bitworks/LightweightAmdGpuFanControl/settings.json`.
- Poll interval 2.5 s; tray tooltip + Preferences status refresh from `LatestStatuses` snapshots.
- **Startup fan blip**: every detected channel (even disabled GPUs) gets a ~4 s self-test at launch that briefly sets 35 % then restores to auto. Accepted for v1.0 so a later-enabled GPU is already validated; most users have one GPU (no blip on absent disabled GPUs).
- A hard kill (Task Manager/power loss) cannot run restore handlers; the driver reclaims control on reset and the app re-establishes on next launch.

## Commit And Release Guidance

- Version single-sourced in `VERSION` (currently `1.0.0`); `build.ps1` and the installer read it. Tag-push `v*` triggers the CI `release` job (GitHub Release + Setup.exe).
- Keep native-binding csproj guard + CI verify step intact — they are the guardrail against silently shipping a broken installer.
- Branch `feature/v1.0-release-readiness`, draft PR #3 → main.

## Remaining Work (Phase 5 — hardware, real AMD PC required)

Cannot be done on this machine. Run on a Windows box with a supported AMD GPU:

1. ADLX backend initializes (no missing-DLL error) from the installed build.
2. Fan ramps and holds near the 65 °C core target under load.
3. Emergency: at critical temp fan jumps to 85 %; hysteretic recovery on cooldown.
4. Exit → Adrenalin shows **automatic** fan control restored. **With two GPUs enabled, confirm exit restores BOTH to automatic.**
5. **Fan-set works WITHOUT admin** (validates the `asInvoker` assumption — see Security Posture).
6. Feedback buttons (About + Preferences) open https://bitworks.io/contact-us/.
7. (If ADL/legacy hardware available) primary-GPU control works via the ADL fallback.

Latest installer for testing: CI run `28686185843` artifact `LightweightAmdGpuFanControl-Setup` (also saved locally at `~/Downloads/LightweightAmdGpuFanControl-v1.0/`).

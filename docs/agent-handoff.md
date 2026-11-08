# Agent Handoff

Last reviewed: 2026-11-06

This document is for the next implementation agent (Codex, Claude Code, or human). Keep it current when architecture, workflows, verification commands, release steps, or known risks change.

## Project Purpose

**Lightweight AMD GPU Fan Control** is a per-user Windows systray utility (.NET Framework 4.8 WinForms, `OutputType=WinExe` — not a service) that drives AMD Radeon GPU fans toward a **core (edge) temperature target** (default 65 °C). It exists because AMD Adrenalin's fan curve UI is heavy and awkward; this tool gives a lightweight always-on curve with a safe restore-to-automatic lifecycle. Publisher: Bitworks (bitworks.io).

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
- **1.1.0 additions** (committed, not hardware-validated): bound-line ADL/ADLX pre-load for RDNA1 (`adl-preload:` log lines); Overdrive8 routed to ADLX only; start-up diagnostics; `--probe [--no-preload]`; `--automation [--status-file <fullpath>]` with `status.json`; `baseline.json` restore journal and `--restore-auto`; uninstaller restores the pre-app fan state. CLI table and exit codes (`0` ok, `1` error, `2` usage, `3` nothing to do, `4` already running) are in `README.md`; design in `docs/superpowers/plans/2026-11-06-rdna1-fan-control.md`.

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
- `LightweightAmdGpuFanControl/` (**net48** WinForms app):
  - `Gpu/FanControlBackendFactory.cs`: `CreateAll(log) → FanControlChannelSet?`. Prefers ADLX (all GPUs), falls back to ADL (primary only). Never mixes families.
  - `Gpu/FanControlChannelSet.cs`: **owns the single native session** + per-GPU channels; Dispose restores every channel then tears down the session (order matters).
  - `Adlx/AdlxSession.cs`: owns one ADLX init + shared `SystemServices` + `GpuMonitor`. `Gpu/AdlxFanControlBackend.cs`: a **borrowing channel** per GPU (owns only its `FanController`, never the session).
  - `Gpu/Adl/AdlFanControlBackend.cs`: legacy ADL channel; `TryCreateForAdapter(sharedApi, index, log)` borrows a shared `AdlNativeApi`.
  - `Services/FanControlService.cs`: the control loop. One `ControlChannel` (backend + `PolicyState` + flags) per GPU. **Single `_controlLock` serializes ALL native access** (startup tests, poll, restore) — the shared context is not thread-safe. Poll gated until startup tests complete. Publishes `GpuStatus` snapshots via `LatestStatuses` for the UI (UI never calls the backend directly).
  - `Forms/PreferencesForm.cs`, `Forms/AboutForm.cs`, `SystrayApplicationContext.cs`: UI.
  - 1.1.0 files (app project):
    - `Gpu/Native/BoundAdlPreloader.cs`: SetupAPI device and INF lookup, signature and signer check, full-path pre-load; decides once per process.
    - `Services/CapabilityProbe.cs`: `--probe`; read-only `IFanCapabilitySource` readers for ADLX and ADL, loaded-module list, `probe-*.txt`.
    - `Services/AutomationStatusWriter.cs`: `status.json` writer for `--automation`; `--status-file` validation.
    - `Services/BaselineJournalSession.cs`: file-backed `baseline.json`, thread-safe, never throws.
    - `Services/RestoreAutoRunner.cs`: `--restore-auto` one-shot.
    - `Services/AppPaths.cs`: data-folder file paths.
  - 1.1.0 files (Core): `Cli/LaunchOptions.cs` (arguments, `ExitCodes`), `Automation/AutomationStatus.cs` (`status.json` model, `StatusFilePath`), `Diagnostics/ProbeReport.cs`, `Drivers/` (`BoundAdlPlanner`, `InfPackageFile`, `AmdSignerPin`, `OverdriveRouting`), `Recovery/BaselineJournal.cs`, `IO/AtomicFile.cs`.
- `LightweightAmdGpuFanControl.Tests/` (**net8.0** xUnit): 176 tests over policy, settings, enablement, the 1.1.0 Core logic and the `--probe` / `--stop` source guards.
- `external/ADLX/` (submodule): AMD ADLX SDK + C# wrapper. `ManualFanTuning.Reset()` captures/restores pre-app fan state.

## Local And Hosted Test Commands

```sh
# Core unit tests (runnable on macOS/Linux; only .NET 10 runtime here, so roll-forward)
DOTNET_ROLL_FORWARD=Major dotnet test LightweightAmdGpuFanControl.Tests/LightweightAmdGpuFanControl.Tests.csproj -c Release --nologo
# → Passed! Failed: 0, Passed: 176
```

Local net48 app compile (needs `git submodule update --init external/ADLX`; `AdlxBindPath` may point at any existing file). Expected: 0 errors.

```sh
dotnet build LightweightAmdGpuFanControl/LightweightAmdGpuFanControl.csproj -p:AdlxBindPath=<any existing file> -v:q -nologo
```

The WinForms app targets `net48`. The local compile above only proves it compiles; nothing can run off Windows. The real Windows build (with the native ADLX binding) and the installer happen in CI:

```sh
gh run list --branch <branch> --limit 1                           # find run
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
- **Library loading (1.1.0):** `atiadlxx.dll` / `amdadlx64.dll` are never loaded by DLL search. They come from the bound driver package in the DriverStore (only when they differ from System32, pass `WinVerifyTrust` and match the signer pin) or from `%windir%\System32` by full path. Signer pin: subject `O=Advanced Micro Devices`, issuer `CN=Sectigo Public Code Signing CA R36`. Pre-loaded modules are never freed. The pre-load never writes, copies or deletes a file. `ADLXCSharpBind.dll` (our own file) still loads from the app directory.
- `--probe` compiles against a read-only interface (`IFanCapabilitySource`) with no fan setter.
- Native P/Invoke to AMD driver DLLs (`atiadlxx.dll`, ADLX). Driver-provided DLLs are **not** bundled (loaded from system); only the SWIG binding (`ADLXCSharpBind.dll`) and managed wrapper (`ADLXWrapper.dll`) ship.
- **Unsigned build** → Windows SmartScreen will warn. Real, gated **Azure Artifact Signing** steps (`azure/login` OIDC + `azure/artifact-signing-action`) are in the workflow but skipped until the `AZURE_*` repo secrets are set; onboarding in [signing-setup.md](signing-setup.md).

## Operational Notes

- Settings at `%LOCALAPPDATA%/Bitworks/LightweightAmdGpuFanControl/settings.json`.
- Poll interval 2.5 s; tray tooltip + Preferences status refresh from `LatestStatuses` snapshots.
- **Startup fan blip**: every detected channel (even disabled GPUs) gets a ~4 s self-test at launch that briefly sets 35 % then restores to auto. Accepted for v1.0 so a later-enabled GPU is already validated; most users have one GPU (no blip on absent disabled GPUs).
- A hard kill (Task Manager/power loss) cannot run restore handlers; the driver reclaims control on reset and the app re-establishes on next launch. For ADLX GPUs (1.1.0) the pre-app state is journaled in `baseline.json` and recovered on the next start or by `--restore-auto`.
- `%LOCALAPPDATA%/Bitworks/LightweightAmdGpuFanControl/` also holds `baseline.json` (restore journal, atomic writes; `baseline.json.bad` if unreadable), `status.json` (only with `--automation`; a reader must match `pid` and `startedUtc`) and `probe-<yyyyMMddTHHmmssZ>.txt` (`--probe`).
- The uninstaller runs `taskkill /F` then `--restore-auto` (`[UninstallRun]`, before any file is removed); `[UninstallDelete]` removes the data folder afterwards. Copy `log.txt` and `probe-*.txt` out first if they are needed.

## Commit And Release Guidance

- Version single-sourced in `VERSION` (currently `1.0.0`); `build.ps1` and the installer read it. Tag-push `v*` triggers the CI `release` job (GitHub Release + Setup.exe).
- **Release mechanics are staged in `docs/release-runbook.md`** (preconditions, the one-action tag/push, tag-target decision, curated-notes step, post-release verification, rollback). Tag is intentionally uncut pending Phase 5. `CHANGELOG.md` is drafted (v1.0.0, marked unreleased) — it must ride onto the commit that gets tagged.
- Keep native-binding csproj guard + CI verify step intact — they are the guardrail against silently shipping a broken installer.
- Branch `feature/v1.0-release-readiness`, draft PR #3 → main.

## Phase 5 Progress (started 2026-07-12, Radeon RX 7900 XTX)

First on-hardware test surfaced three issues; fixes are staged (uncommitted) and pending **one CI
build → hardware re-test**:
1. **Fan wouldn't slow down after lowering min fan (Manual mode).** Root cause: `SettingsService.Validate`
   destructively clamped `ManualFanPercent` to the live Min/Max, ratcheting it up and never restoring it.
   Fixed to clamp to the hard fan floor/ceiling only; the policy still clamps to the live curve at
   decision time. Regression test `Manual_fan_setpoint_survives_raising_then_lowering_min_fan` (suite now 27).
2. **Preferences dialog clipped/overlapped on the 4K display.** Root cause: absolute pixel layout +
   AutoSize labels. Rebuilt `PreferencesForm` with a `TableLayoutPanel`; `Program.cs` DPI mode set to
   `PerMonitorV2` to match the manifest.
3. **.NET runtime download required.** Now **ported to .NET Framework 4.8** (in-box on Win10 1903+/11):
   Core → netstandard2.0, app → net48, installer gains a .NET 4.8 presence check. No download, no admin.
   (Superseded the interim self-contained approach.)

Also, `FanController.SetFanPercent` was switched to the percent-native ADLX fan curve (removing a
target-fan-speed unit bug) and given change-triggered diagnostic logging (path, capability flags,
tuning-speed range, curve read-back) to `log.txt`, so the Auto-mode fan behaviour is captured in one
hardware pass. The user was unsure which mode the failing test used; the Manual ratchet is the likely
cause (it applies the same value regardless of ADLX path, explaining both "raise works" and "lower stuck").

## Remaining Work (Phase 5 — hardware, real AMD PC required)

Re-test the fixes above on the RX 7900 XTX, plus the original checklist. Run on a Windows box with a supported AMD GPU:

1. ADLX backend initializes (no missing-DLL error) from the installed build.
2. Fan ramps and holds near the 65 °C core target under load.
3. Emergency: at critical temp fan jumps to 85 %; hysteretic recovery on cooldown.
4. Exit → Adrenalin shows **automatic** fan control restored. **With two GPUs enabled, confirm exit restores BOTH to automatic.**
5. **Fan-set works WITHOUT admin** (validates the `asInvoker` assumption — see Security Posture).
6. Feedback buttons (About + Preferences) open https://bitworks.io/contact-us/.
7. (If ADL/legacy hardware available) primary-GPU control works via the ADL fallback.

Latest installer for testing: CI run `28686185843` artifact `LightweightAmdGpuFanControl-Setup` (also saved locally at `~/Downloads/LightweightAmdGpuFanControl-v1.0/`).

## 1.1.0 status

- **Implementation: done** and committed on the 1.1.0 branch (plan Tasks 1-9). Core unit tests and the local net48 compile pass; CI is the Windows build and installer gate.
- **Hardware validation:** E1 ran on rc.1 (2026-11-08) and **failed on an rc.1 defect**: the bound-package lookup refused the RDNA1/2 INF's `.\B026653` disk path, so the pre-load never ran. H3 is still untested. rc.2 fixes the lookup ([verification evidence](verification-evidence.md)). E1 is re-declared on rc.2. E2 (controlled engagement on RDNA1) and E3 (automation-driven regression across generations) follow, each approved before it runs. E2 also needs a live temperature source that doesn't load the System32 AMD libraries. Definitions: [plan section 6](superpowers/plans/2026-11-06-rdna1-fan-control.md#6-hardware-validation--declared-experiments).
- **Release sequence** ([plan section 5](superpowers/plans/2026-11-06-rdna1-fan-control.md#5-release-and-rollout)):
  1. The implementation PR stays a draft until reviewed, then merges with CI green.
  2. Ask before the first rc tag. Tag `v1.1.0-rc.N` (rc.1 shipped 2026-11-06, rc.2 carries the INF fix); CI signs it and publishes a prerelease. Confirm it shows Pre-release and that `releases/latest` is still v1.0.0.
  3. Run E1, then E2 and E3, each approved first.
  4. Tag `v1.1.0` on the validated code (or a docs-only commit after it); CI signs it and publishes the latest release. Promoting the final tag is the rollout switch for downstream installs.
  - `VERSION` stays `1.1.0` for rc tags. Never re-run a failed tag; cut the next rc. Sign in CI only.

## 1.1.0-rc.3 status (release to the driver's automatic control)

- **Implemented locally, not validated on hardware:** plan [docs/superpowers/plans/2026-11-08-rc3-release-to-driver-auto.md](superpowers/plans/2026-11-08-rc3-release-to-driver-auto.md), Tasks 1-6. `ReleasePlanner` (Core) picks `FactoryReset` (ADLX `ResetToFactory`, confirmed by `IsAtFactory`) or `ReapplyPoints`; `baseline.json` entries carry `preAppAtFactory` (null for 1.0.x/rc.1/rc.2 entries); `FanController.Release` is the single release path and `ManualFanTuning.Reset()` is never called (source guards in `FanReleaseSourceGuardTests`).
- **Submodule:** `external/ADLX` points at the bitworks-io/ADLX fork (Rem0o/ADLX 240b1f3 plus `IsAtFactory`/`ResetToFactory` on `GPUTuningService`); see NOTICE. The fork must exist and contain the pinned commit before CI can check out the submodule.
- **Open:** `ADLX_RESET_NEEDED` handling (refuse a GPU under one-click auto tuning) is not implemented: the pinned SDK headers and the SWIG `ADLX_RESULT` enum have no such value and the wrapper's exception carries no result code. H0 (does `IsAtFactory` read false under a manual fan curve) and H1-H3 are not run.

## Context Handoff Hygiene

- Keep durable context short and decision-focused: current architecture, contracts, operational assumptions, verification commands, known risks, and unresolved blockers.
- Link to detailed logs, screenshots, traces, reports, or release artifacts instead of pasting bulky output.
- Move exploratory notes, abandoned approaches, and raw command output out of this document unless they explain a current risk or decision.
- When a chat grows long, summarize only the accepted decisions, changed files, verification evidence, and remaining work needed for the next agent to continue safely.

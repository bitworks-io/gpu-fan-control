# Agent Handoff

Last reviewed: 2026-11-24

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
- **1.1.0 additions** (released 2026-11-23 as the rc.3 build; RDNA1 validated on an RX 5700 XT test system, see "1.1.0 status"): bound-line ADL/ADLX pre-load for RDNA1 (`adl-preload:` log lines); Overdrive8 routed to ADLX only; start-up diagnostics; `--probe [--no-preload]`; `--automation [--status-file <fullpath>]` with `status.json`; `baseline.json` restore journal and `--restore-auto`; uninstaller restores the pre-app fan state. CLI table and exit codes (`0` ok, `1` error, `2` usage, `3` nothing to do, `4` already running) are in `README.md`; design in `docs/superpowers/plans/2026-11-06-rdna1-fan-control.md`.

## User Stories And Acceptance Evidence

| Story | Acceptance Criteria | Automated Proof | Manual/Smoke Proof | Last Verified | Gaps |
| --- | --- | --- | --- | --- | --- |
| As a user, fan follows a core-temp target curve | Fan% = clamp(min…max) ramping over target…+25 °C; dead-band holds | `FanControlPolicyTests` (ramp endpoints, mid-ramp, dead-band) | Regulated along the curve on an RX 5700 XT (RDNA1) test system, rc.3; on an RX 7800 XT (RDNA3) each commanded increase raised the fan speed, rc.3; not verified on an RX 7900 XT (one check, 41 to 43 %, did not pass; re-test planned) | 2026-11-23 (HW, RX 5700 XT and RX 7800 XT) | RX 7900 XT regulation unverified |
| Over-temp safety | temp ≥ critical → 85 %; hysteretic recovery; overrides manual | `FanControlPolicyTests` (latch/unlatch, manual override) | Pending HW | 2026-07-03 (unit) | HW unverified |
| Sensor loss is safe | 3 failed reads → relinquish to driver auto | `FanControlPolicyTests` (sensor-loss) | Pending HW | 2026-07-03 (unit) | HW unverified |
| Primary GPU on, others opt-in | reconcile: primary default-on, new GPUs default-off, saved prefs respected | `GpuEnablementTests` (11 cases) | Pending 2-GPU HW | 2026-07-03 (unit) | HW unverified |
| Exit restores automatic control | On exit, Adrenalin shows automatic fan control (all controlled GPUs) | — (lifecycle wiring) | Verified on rc.3: RX 5700 XT (RDNA1: factory reset confirmed, idle fan stop back, `--restore-auto` after a hard kill), RX 7800 XT (release to automatic control under load and at idle, idle fan stop back), RX 7900 XT (release on exit, factory reset confirmed) | 2026-11-23 (HW) | Two-GPU both-restore still pending |
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
- **Signing:** `v*` tag builds sign the installer through **Azure Artifact Signing** (`azure/login` OIDC + `azure/artifact-signing-action`, in the release environment); PR and `main` builds stay unsigned. The 1.1.0 installer (`d6c4a10d…`) reads Authenticode `Valid`, signer `O=Bitworks LLC`. The inner app `.exe` is not signed yet ([signing-setup.md](signing-setup.md) §4), and SmartScreen can still warn on a new release until it builds reputation. Never sign locally.

## Operational Notes

- Settings at `%LOCALAPPDATA%/Bitworks/LightweightAmdGpuFanControl/settings.json`.
- Poll interval 2.5 s; tray tooltip + Preferences status refresh from `LatestStatuses` snapshots.
- **Startup fan blip**: every detected channel (even disabled GPUs) gets a ~4 s self-test at launch that briefly sets 35 % then restores to auto. Accepted for v1.0 so a later-enabled GPU is already validated; most users have one GPU (no blip on absent disabled GPUs).
- A hard kill (Task Manager/power loss) cannot run restore handlers; the driver reclaims control on reset and the app re-establishes on next launch. For ADLX GPUs (1.1.0) the pre-app state is journaled in `baseline.json` and recovered on the next start or by `--restore-auto`.
- `%LOCALAPPDATA%/Bitworks/LightweightAmdGpuFanControl/` also holds `baseline.json` (restore journal, atomic writes; `baseline.json.bad` if unreadable), `status.json` (only with `--automation`; a reader must match `pid` and `startedUtc`) and `probe-<yyyyMMddTHHmmssZ>.txt` (`--probe`).
- The uninstaller runs `taskkill /F` then `--restore-auto` (`[UninstallRun]`, before any file is removed); `[UninstallDelete]` removes the data folder afterwards. Copy `log.txt` and `probe-*.txt` out first if they are needed.

## Commit And Release Guidance

- Version single-sourced in `VERSION` (currently `1.1.1`); `build.ps1` and the installer read it. Tag-push `v*` triggers the CI `release` job (GitHub Release + signed Setup.exe); a tag with a hyphen (`-rc.N`) publishes a prerelease that never becomes `latest`.
- **Release mechanics are in `docs/release-runbook.md`** (preconditions, the one-action tag/push, tag-target decision, rc promotion, curated-notes step, post-release verification, rollback). `CHANGELOG.md` must ride onto the commit that gets tagged. 1.1.0 was released by promoting rc.3 (see 1.1.0 status, step 4), so never push a `v1.1.0` tag.
- Keep native-binding csproj guard + CI verify step intact — they are the guardrail against silently shipping a broken installer.
- **Open work:** the 1.1.1 PR (graceful `--stop` before the force-close on upgrade) targets `main` and is out of draft for review. Its declared hardware check passed on 2026-11-25 with the signed `v1.1.1-rc.1` prerelease (see the 1.1.1 section). It merges only after review and the owner's OK; promoting the prerelease to `latest` is a separate, later step.

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

Installer used for this 1.0-era checklist: CI run `28686185843` artifact `LightweightAmdGpuFanControl-Setup`. For current testing, use the [latest release](https://github.com/bitworks-io/gpu-fan-control/releases/latest).

## 1.1.0 status

- **Implementation: done** and committed on the 1.1.0 branch (plan Tasks 1-9). Core unit tests and the local net48 compile pass; CI is the Windows build and installer gate.
- **Released 2026-11-23 as `latest`:** the signed 1.1.0-rc.3 prerelease (published 2026-11-09) was promoted unchanged with `gh release edit` (title `v1.1.0`, prerelease off, latest on, curated notes). The release's tag is `v1.1.0-rc.3`; there is no `v1.1.0` tag. Installer SHA-256 `d6c4a10db026ce47db392da04966b42181c532dfafe834eee204656967d7e494`, file version 1.1.0.0.
- **Hardware validation (current state)** ([verification evidence](verification-evidence.md)):
  - **RDNA1, RX 5700 XT test system:** under load the app regulated the fan along its curve (increases followed, no abort); on exit the GPU returned to the driver's automatic control, factory reset confirmed, idle fan stop back (0 rpm at idle); after a hard kill `--restore-auto` restored automatic control from the journal; the signed rc.3 installer installed cleanly over 1.0.1.
  - **RDNA3:** RX 7800 XT test card: under load each commanded fan increase raised the fan speed, the start-up self-test passed, and the release to automatic control was verified under load and at idle with the idle fan stop back. RX 7900 XT test card: release on exit verified, factory reset confirmed. **Fan regulation on the RX 7900 XT is not verified for 1.1.0** (one check at high duty, 41 to 43 %, did not pass; a re-test is planned and, like every hardware check, needs approval before it runs).
  - **RDNA2:** an RX 6400 test card reports no manual fan tuning support through ADLX, so the app does not control its fan; nothing to validate there (not a statement about other RDNA2 cards).
  - Over-temperature, sensor loss, two-GPU and ADL fallback remain pending. Definitions: [plan section 6](superpowers/plans/2026-11-06-rdna1-fan-control.md#6-hardware-validation--declared-experiments).
- **1.0.2:** released 2026-11-18, signed, on the 1.0 line (not `latest`). Safe command-line handling: `--stop` is a real graceful stop and any other `--` option exits at once. An uninstall after a 1.1 to 1.0.x downgrade no longer hangs when the 1.0.x is 1.0.2 (shown in CI); 1.0.0 and 1.0.1 remain affected.
- **Known issues (user-facing; also in CHANGELOG and README Troubleshooting):**
  1. RX 7900 XT fan regulation is not verified for 1.1.0 (see the RDNA3 line above).
  2. Don't downgrade below 1.0.2 after installing any 1.1 build: installing 1.0.0 or 1.0.1 over it leaves an uninstaller that can hang. Fix: install 1.0.2 or a 1.1 build over it, then uninstall.
  3. Upgrading while the app is running: installers up to 1.1.0 force-close a running app of any version, skipping its fan restore. Running 1.0.x: the new version starts from the old fan curve; stop the app before upgrading, or afterwards run `--reset-tuning-to-factory --gpu <uniqueId>` (id from `--probe`; resets all of that GPU's tuning, not only the fan). Running a 1.1 build: on an ADLX GPU the next start reuses the journaled pre-app state (`baseline.json`) only if the fan still looks app-set (flat duty; `BaselinePlanner.LooksAppWritten`, applied in `Choose`, which `FanController.CaptureBaseline` calls before the entry is marked dirty), so a later exit restores it. Exceptions: closed mid fan write (fan may not look app-set; the new version starts from the closed app's points); an rc.1/rc.2 entry has no factory flag, so its saved points are re-applied instead of a factory reset; a GPU on the ADL fallback has no journal and on exit is handed back to the driver's automatic mode (`AdlFanControlBackend.TryRestoreAutomaticFanControl`). From 1.1.1 the installer first asks a running 1.0.2 or later build to stop gracefully (it releases its fans); 1.0.0 and 1.0.1 are still force-closed, and a build that doesn't stop within its timeout is force-closed as before.
- **Release sequence** ([plan section 5](superpowers/plans/2026-11-06-rdna1-fan-control.md#5-release-and-rollout)):
  1. The implementation PR stays a draft until reviewed, then merges with CI green.
  2. Ask before the first rc tag. Tag `v1.1.0-rc.N` (rc.1 shipped 2026-11-06, rc.2 carries the INF fix, rc.3 shipped 2026-11-09); CI signs it and publishes a prerelease. Confirm it shows Pre-release and that `releases/latest` did not move (it stayed on v1.0.1 until the promotion in step 4).
  3. Run the hardware checks, each approved first (see the current state above).
  4. Done 2026-11-23 by promotion, not by a new tag: `gh release edit v1.1.0-rc.3 --prerelease=false --latest --title "v1.1.0" --notes-file <curated notes>`, so the installer that was tested on hardware is the one that ships. Making it `latest` is the rollout switch for downstream installs. **Do not push a `v1.1.0` tag:** the release workflow would build, sign and publish a second "1.1.0" with a different installer and make it `latest` (`make_latest` is true for any tag without a hyphen).
  - `VERSION` stays `1.1.0` for rc tags. Never re-run a failed tag; cut the next rc. Sign in CI only.

## 1.1.0-rc.3 status (release to the driver's automatic control)

- **Implemented and released in 1.1.0 (the rc.3 build; see "1.1.0 status" for what was validated on which test card):** plan [docs/superpowers/plans/2026-11-08-rc3-release-to-driver-auto.md](superpowers/plans/2026-11-08-rc3-release-to-driver-auto.md), Tasks 1-6. `ReleasePlanner` (Core) picks `FactoryReset` (ADLX `ResetToFactory`, confirmed by `IsAtFactory`) or `ReapplyPoints`; `baseline.json` entries carry `preAppAtFactory` (null for 1.0.x/rc.1/rc.2 entries); `FanController.Release` is the single release path and `ManualFanTuning.Reset()` is never called (source guards in `FanReleaseSourceGuardTests`).
- **Submodule:** `external/ADLX` points at the bitworks-io/ADLX fork (Rem0o/ADLX 240b1f3 plus `IsAtFactory`/`ResetToFactory` on `GPUTuningService`); see NOTICE. The fork must exist and contain the pinned commit before CI can check out the submodule.
- **D6 (fail closed, Task 5c):** the pinned SDK has no `ADLX_RESET_NEEDED` and the wrapper reports errors only as text, so every fan-write error is treated alike: `GpuControlTracker` (Core) refuses a GPU on a start/self-test error and degrades it on a regulation error (one release through the planner path, no retries). Reported as `gpuIssues` in `status.json` and `last run:` lines in `--probe`; guarded by `FailClosedSourceGuardTests`.
- **Fallback:** on a single-ADLX-GPU system, no ADLX channel (for example `FanController.Initialize` fails closed because the pre-app fan state cannot be read) means the existing fallback to the ADL backend, which has no release plan (it restores the driver default).
- **Open:** fan regulation on the RX 7900 XT is not verified (one check at 41 to 43 % did not pass; re-test planned); over-temperature, sensor loss, two-GPU and ADL fallback checks remain pending.

## 1.1.1 (in development)

- **H1, graceful stop on upgrade.** `PrepareToInstall` first runs the installed exe with `--stop` when its file version is 1.0.2.0 or newer, so the old app releases its fans to the driver's automatic control. Only then does the existing `taskkill` run, as a fallback (a build that doesn't stop within its timeout is force-closed as before; 1.0.0 and 1.0.1 are still force-closed). The gate is 1.0.2.0 because 1.0.0 and 1.0.1 ignore arguments and would open a second tray, hanging setup; a missing exe or an unreadable version skips `--stop`. `--stop` exit 0 sets the relaunch flag and waits 1500 ms, as the kill path does. The setup log records `PrepareToInstall: graceful stop gate=... version=...`, `--stop exit <n>` and `taskkill exit <n>`.
- **H2, uninstall.** The `--stop` and `--restore-auto` `[UninstallRun]` entries carry `skipifdoesntexist`, so a removed or renamed exe no longer errors. It applies to uninstall logs written by 1.1.1 or later.
- **CI scenario S7** (`tools/uninstall-repro`): upgrade over a running 1.1 tray. On a GPU-less runner `--stop` exits 1 (the tray is still inside its no-backend dialog and has no stop listener), so the taskkill fallback and the relaunch are what CI proves. Owner/session mismatch, a foreign app process, or `--stop` exit 3 are reported as preconditions, not product failures.
- **Hardware check (passed 2026-11-25, signed `v1.1.1-rc.1`):** the graceful exit-0 path (a tray that stops and releases the fans itself), which CI cannot exercise. Upgrading over a running 1.1.0 was tested on an AMD Radeon RX 7800 XT: the running app released its fans to the driver's automatic control before the upgrade replaced it. Not covered: upgrades from 1.0.2, RDNA1/RDNA2 cards, and an upgrade under load.
- **Merge condition:** `VERSION` is already 1.1.1 on this branch. Merge only after v1.1.0 is released (rc.3 promoted); met on 2026-11-23.

## Context Handoff Hygiene

- Keep durable context short and decision-focused: current architecture, contracts, operational assumptions, verification commands, known risks, and unresolved blockers.
- Link to detailed logs, screenshots, traces, reports, or release artifacts instead of pasting bulky output.
- Move exploratory notes, abandoned approaches, and raw command output out of this document unless they explain a current risk or decision.
- When a chat grows long, summarize only the accepted decisions, changed files, verification evidence, and remaining work needed for the next agent to continue safely.

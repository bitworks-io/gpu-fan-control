# RDNA1 (RX 5000-series) Fan Control — Root Cause and Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Status:** APPROVED with changes (2026-11-06). Decisions recorded:
- **Phase E** (restore after an unclean stop) is bundled in 1.1.0.
- **Phase C:** the Overdrive8 CustomFan branch is dropped.
- **Signer pin:** subject `O=` plus the Sectigo R36 issuer.
- **Phase F added:** an `--automation` mode with a `status.json` contract.

No hardware step runs until its declared experiment (§6) has been approved.

**Goal:** Make v1.x control fans on RDNA1 cards (RX 5700 / 5700 XT / 5600 XT, Navi 10/14) on machines where AMD's RDNA1/2 driver package is bound but System32 holds the newer RDNA3/4 package's user-mode ADL/ADLX.

**Architecture:** Before any ADL/ADLX call, the app resolves the bound display driver package's own `atiadlxx.dll` and `amdadlx64.dll` from the Windows DriverStore. That uses SetupAPI, then the INF's `SourceDisks*` entries, never a search path. It checks AMD's embedded signature and pre-loads both by full path, so ADLX and ADL bind to the copies that match the kernel driver. Diagnostics and a read-only `--probe` mode come first, so the remaining hypothesis can be settled on hardware without writing a fan value.

**Tech Stack:** .NET Framework 4.8 WinForms app; `netstandard2.0` Core library with `net8.0` xUnit tests; Win32 P/Invoke (setupapi, wintrust, kernel32); ADLX SDK 1.4 SWIG binding (`external/ADLX`).

**Spec:** This document (sections 1–7 are the design; section 8 is the task plan).

---

## 1. Summary

On an RX 5700 XT test bench, v1.0.0 started, logged that neither backend could drive any GPU, and showed "AMD fan control unavailable". The same build works on RDNA2 and RDNA3 benches.

- **Proven:** the app loaded the **System32** ADL/ADLX (32.0.32015.7029, RDNA3/4 package `u0205306.inf`). The card's kernel driver comes from the **RDNA1/2** package (`u0205305.inf`, 32.0.21045.16010), and that package ships its own `atiadlxx.dll` and `amdadlx64.dll` 32.0.21045.16010 in the DriverStore. ADLX initialised but reported no GPU with manual fan tuning, and the ADL fallback found adapters but no fan interface.
- **Proven (code):** the ADL fallback cannot drive any Overdrive8 GPU: RDNA1 and newer report Overdrive8 per AMD's ADL SDK, yet the code routes them to the OverdriveN API. RDNA1 therefore depends on ADLX alone.
- **Leading hypothesis, not yet proven:** the version mismatch between System32 and the bound package is why ADLX reports no tunable GPU. Loading the bound package's copies should fix it. The alternative is that ADLX has no manual fan tuning for RDNA1 even with matching copies; that case needs an ADL Overdrive8 backend (Phase C). The read-only probe experiment E1 decides between the two.

## 2. Root cause — evidence

### 2.1 Field evidence (test bench, 2026-11-05)

The app's own log (`%LOCALAPPDATA%\Bitworks\LightweightAmdGpuFanControl\log.txt`) for the failed launch, in local time:

```
2026-11-05 17:10:27 | ADLX session opened but no GPU supports manual fan tuning.
2026-11-05 17:10:27 | ADL adapters found but none support fan control.
2026-11-05 17:10:27 | No AMD fan-control backend could be initialized.
```

An earlier launch that day logged the same three lines. In July, the same bench with an RX 6700 XT logged `Created ADLX channel …` and `Fan test passed on ADLX`; its driver state at that time was not recorded.

Driver state, read-only (file binary versions):

| Location | `atiadlxx.dll` | `amdadlx64.dll` |
|---|---|---|
| `System32` | 32.0.32015.7029 | 32.0.32015.7029 |
| DriverStore `u0205305.inf_amd64_*\B026653\` (bound to the RX 5700 XT) | 32.0.21045.16010 | 32.0.21045.16010 |
| DriverStore `u0205306.inf_amd64_*\` (RDNA3/4 line, not bound) | 32.0.32015.7029 | 32.0.32015.7029 |

The bound copies are signed with AMD's embedded signature (subject `O=Advanced Micro Devices`, issuer `CN=Sectigo Public Code Signing CA R36`). As a control, an RDNA3 bench whose System32 copy matches its bound package (32.0.32015.7029) works.

### 2.2 Code paths (v1.0.0)

1. **ADLX loads System32's copy.** `FanControlBackendFactory.CreateAll` (`LightweightAmdGpuFanControl/Gpu/FanControlBackendFactory.cs:18-31`) tries ADLX first. `AdlxInitializer.Initialize` (`Adlx/AdlxInitializer.cs:20-35`) calls `new ADLXWrapper()`, and that runs `ADLXHelper::Initialize`. ADLXHelper loads `amdadlx64.dll` by bare name with `LOAD_LIBRARY_SEARCH_APPLICATION_DIR | USER_DIRS | DEFAULT_DIRS | SYSTEM32` (`external/ADLX/SDK/Platform/Windows/WinAPIs.cpp:40-43`). Nothing in the app chooses the bound copy, so the search ends at System32.
2. **ADLX found no usable GPU.** `TryCreateAdlxSet` (`FanControlBackendFactory.cs:33-56`) builds one channel per GPU through `AdlxFanControlBackend.TryCreateChannel` (`Gpu/AdlxFanControlBackend.cs:36-57`) and `FanController.Initialize` (`Adlx/FanController.cs:52-73`). The log line at `:50` is written both when ADLX sees no GPUs and when every GPU answers `IsSupportedManualFanTuning == false` or throws. Current logging cannot tell these apart.
3. **The ADL fallback has no Overdrive8 path.** `AdlNativeApi.TryLoad` loads `atiadlxx.dll` by bare name through `LoadLibrary` (`Gpu/Adl/AdlNativeApi.cs:94-96`, `:23`). Bare-name loading searches the app directory first, and the per-user install directory is user-writable. `AdlFanControlBackend.TryCreateForAdapter` treats `version >= 7` as OverdriveN (`Gpu/Adl/AdlFanControlBackend.cs:62`; the same check is at `:120`). An Overdrive8 adapter (version 8: Vega20 and Navi) therefore goes down `ADL2_OverdriveN_FanControl_Get`, which fails, and the method returns `null` (`:76-77`). The result is the log line at `FanControlBackendFactory.cs:85`.

### 2.3 Hypotheses

| | Hypothesis | Verdict |
|---|---|---|
| H1-init | ADLX fails to initialise with the mismatched DLLs | **Refuted.** The log shows the ADLX session opened. |
| H1-cap | ADLX offers no manual fan tuning for RDNA1 even with matching DLLs | **Open.** Same symptom as H3; E1 decides. |
| H2 | RDNA1 needs ADL Overdrive8, which the fallback lacks | **Confirmed as a defect** (§2.2.3). It only decides the outcome if ADLX stays unusable. |
| H3 | System32's RDNA3/4-line ADL/ADLX does not serve the RDNA1/2-line card; the bound copies would | **Leading.** It explains both failed backends at once. Downstream bench tooling has observed that, after a generation swap, ADL calls to the new card fail while the other line's copy is in System32, and it fixed that by pre-loading the bound copy. RDNA3 works when System32 matches its bound package. Not proven: no RDNA1 card has yet been observed with matching copies. |

### 2.4 Contributing defect: diagnostics hide the cause

- `AdlxInitializer.cs:30-34` swallows the ADLX init exception, which carries the `ADLX_RESULT`.
- `AdlxSession.cs:39-43` returns null without logging.
- `FanController.cs:67-72` swallows exceptions.
- `FanControlBackendFactory.cs:48-50` gives one message for "no GPU" and "unsupported".
- `AdlNativeApi.cs:113-120` overwrites the real error with "Could not load …".
- The Overdrive caps version is never logged (`AdlFanControlBackend.cs:59-89`).

## 3. Fix design

**Phase A — Diagnostics** (no behaviour change):
- Log the ADLX init result.
- Log the GPU count, then each GPU's name and `IsSupportedManualFanTuning` result or error code.
- Log the full path and binary version of each loaded `atiadlxx.dll` / `amdadlx64.dll`.
- Log each ADL adapter's `Overdrive_Caps` result, the supported/enabled flags and version.
- Keep the first real ADL load error.

**Phase B — Bound-line library pre-load** (the fix for H3). This is new code, written from the rules below; nothing is copied from other codebases.

1. Enumerate **present** display-class devices whose instance ID contains `VEN_1002` (SetupAPI). For each, read the bound INF name (`oemNN.inf`).
2. If there is no AMD display, do nothing. If present AMD displays are bound to **more than one** INF (mixed driver lines), do nothing, log it, and keep the v1.0 behaviour. One process can host only one ADL/ADLX copy.
3. `SetupGetInfDriverStoreLocationW(%windir%\INF\oemNN.inf)` returns the package INF. It must lie under `%windir%\System32\DriverStore\FileRepository\`.
4. Name `atiadlxx.dll` and `amdadlx64.dll` from the package INF's own `[SourceDisksFiles(.amd64)]` and `[SourceDisksNames(.amd64)]` sections. `.amd64` wins. Every path segment is validated: no rooted path, drive letter, `..`, `%token%` or illegal character. The result is strictly under the package folder. Both files must exist and must not be reparse points.
5. If both bound versions equal the System32 versions, the pre-load is not needed.
6. Otherwise, both files must pass `WinVerifyTrust` (embedded signature; revocation off, because benches may be offline). The signer must match the AMD pin: subject `O=Advanced Micro Devices` and issuer `CN=Sectigo Public Code Signing CA R36`, the same pin the downstream tooling uses (**decided**). If either file fails, refuse the pre-load: the app keeps v1.0 behaviour and logs one line.
7. Pre-load with `LoadLibraryExW(fullPath, 0, LOAD_LIBRARY_SEARCH_DLL_LOAD_DIR | LOAD_LIBRARY_SEARCH_SYSTEM32)` (0x900): `atiadlxx.dll` first, then `amdadlx64.dll`. Never free them once both are loaded. Both are loaded or neither: if `atiadlxx.dll` fails, nothing is loaded. If `amdadlx64.dll` fails, the just-loaded `atiadlxx.dll` is freed again, which is safe because nothing has called into it yet. Either way, the System32 fallback of step 9 applies. ADL and ADLX never come from different driver lines. Later bare-name loads by ADLXHelper and inside `amdadlx64.dll` resolve to these modules, because Windows reuses an already-loaded module of the same name.
8. Check afterwards with `GetModuleHandle` / `GetModuleFileName` that each loaded module is the intended path. Log it.
9. **Hardening, all cases:**
   - `AdlNativeApi` never searches. It uses the already-loaded module if there is one, otherwise `LoadLibraryExW(%windir%\System32\atiadlxx.dll, …SEARCH_SYSTEM32)`.
   - When no bound pre-load happens, `amdadlx64.dll` is still pre-loaded by **full System32 path** before ADLX starts, so ADLXHelper's app-directory search can never pick up a planted copy.
10. Decide once per process, log one line, never throw. Any doubt (unresolved, untrusted, mixed, error) falls back to v1.0 behaviour.

**Phase C — ADL Overdrive8 backend (conditional).**
- Always, in this plan: route only Overdrive version **7** to OverdriveN. Version **8** gets "no ADL fan backend; ADLX is the supported path". All other versions are unsupported.
  - Both routing sites change: `AdlFanControlBackend.cs:62` (channel creation) and `:120`.
  - **Decided:** Overdrive8 adapters no longer reach v1.0's `ADL2_CustomFan_*` branch. It has no known working case on OD8 (v1.0 was validated on ADLX only), and no unvalidated write path stays reachable. Version 7 (OverdriveN) keeps exactly v1.0's behaviour, including its CustomFan preference. Changing the older-card path is out of scope for an RDNA1 fix, and nothing here would validate it (§7).
- Only if E1 shows that ADLX still reports no manual fan tuning for RDNA1 with the bound copies loaded: write a separate plan for a full Overdrive8 backend, with its own declared experiment. It would use `ADL2_Overdrive8_Init_SettingX2_Get`, `…Current_SettingX2_Get`, `…Setting_Set`, `ADL2_New_QueryPMLogData_Get`, a fan curve, and zero-RPM / min-speed handling. That backend writes fan values through a second API family and needs its own safety review.

**Phase D — `--probe` mode.** `LightweightAmdGpuFanControl.exe --probe [--no-preload]`:
- Runs the Phase B decision, unless `--no-preload` is given (that flag reproduces the v1.0 load).
- Then runs a **read-only** capability probe:
  - ADLX: init result, GPU list, `IsSupportedManualFanTuning`, and for supported GPUs the speed range, zero-RPM support and current fan tuning states.
  - ADL: caps per adapter.
  - Every loaded module named `ati*` or `amd*`, with its path and version, so that bare-name loads of other AMD DLLs show up.
- Also reports the restore journal's state (Phase E): no file, clean, or which GPUs are dirty.
- Writes the report to `log.txt` and to `probe-<utc>.txt` beside it, then exits.
- Exit codes are in the CLI table under Phase F: `0` at least one GPU with ADLX manual fan tuning (ADL adapters are reported, not counted), `3` none, `4` another instance is running (refuses), `1` error, `2` usage error.
- Shows no window and no MessageBox.
- Read-only status comes from the probe's read-only capability interface plus a source-guard test (`CapabilityProbe_CallsNoFanWriteApi`) that fails if `CapabilityProbe.cs` mentions any fan-write API. It is not a compiler guarantee. Releasing an ADLX interface performs no write; verify that in `ManualFanTuning`/wrapper `Dispose` while implementing.

**Phase E — restore after an unclean stop. DECIDED: bundled in 1.1.0.** 1.1.0 final does not wait for the downstream automation to start calling `--restore-auto`. A dirty journal alone already makes the next graceful exit return to the true pre-app state, which is better than v1.0 on every generation.

"Always restore automatic fan control on exit or crash" holds only for graceful exits today. Three paths skip `RestoreAll`:
- **TerminateProcess.** Downstream automation stops the app this way on every run, and the installer uses `taskkill /F` (`installer/LightweightAmdGpuFanControl.iss:68`).
- **AccessViolation inside ADL/ADLX.** On .NET Framework 4.x, `AppDomain.UnhandledException` is not raised for corrupted-state exceptions. The app does not opt in, and it should not.
- **Power loss.**

The next instance then makes it worse. ADLX `ManualFanTuning.Reset()` re-applies the curve captured when **that** instance started (`ManualFanTuning.cs:37-41,77-82`), which is the killed instance's manual curve, so "restore" never returns to the pre-app state.
- **Exposure today:** RDNA2/3 machines under automation, every run.
- **Exposure after this plan:** RDNA1 machines join them. Until now they never engaged, so nothing was left behind.
- **Effect:** a fixed duty survives into the next workload until the app is launched again.
  - Usually that duty is high, because the stop comes near peak temperature.
  - It can be as low as the 20 % floor if the stop comes after a cool-down.
  - Firmware thermal throttling still protects the card, but this is outside the design intent.

**Why speeds plus zero-RPM are enough.** The app never writes a tuning-state temperature.
- `SetFanTuningStates2` calls `ADLXHelper::SetSpeed` (`external/ADLX/SDK/ADLXHelper/Windows/Cpp/ADLXHelper.cpp:108-127`), which calls only `SetFanSpeed` on each state.
- `Reset()` re-writes the temperatures captured at construction, which are the originals.
- So after any stop, the driver's state temperatures are still the pre-app ones. Only per-state speeds and the zero-RPM flag can differ.

This lets the journal use only public wrapper members: `GetCurrentState()`, `GetZeroRPMState()`, `SetFanTuningStates(int[])` and `SetZeroRPM(bool)`. It needs no reflection and no change to the `external/ADLX` submodule, which is a third-party fork.

**Design:**
1. **Journal file:** `%LOCALAPPDATA%\Bitworks\LightweightAmdGpuFanControl\baseline.json`.
   - One entry per ADLX GPU, keyed by GPU name plus ADLX `UniqueId`. Two identical cards share a name, which is why `UniqueId` is part of the key.
   - The tuning-state count is stored and validated, but it is not part of the key. A GPU whose count changes therefore overwrites its stale entry rather than leaving a second one.
   - Each entry holds the pre-app per-state speeds, `zeroRpmSupported`, `zeroRpm`, `dirty` and a UTC timestamp.
   - Written atomically: a temp file in the same folder, then replace.
2. **Choosing the baseline at channel creation** (`FanController.Initialize`), after the wrapper captures the current state. `BaselinePlanner.Choose` classifies the entry in this order:
   1. No entry: `NoEntry`. Not dirty: `Clean`.
   2. **Invalid:** identity, state count, speed count, speed range 0–100 or `zeroRpmSupported` does not match.
   3. **AlreadyRestored:** the captured speeds equal the entry's speeds and the zero-RPM flag is equal or unsupported.
   4. **Stale:** the captured state does not look app-written. The app writes one flat duty clamped to 20–85 with zero-RPM off, so the test is: max − min ≤ 1, min ≥ 19, max ≤ 86, zero-RPM off.
   5. Otherwise **Journal**: the entry becomes the baseline. The log says it is recovering a state left by an unclean stop.
   - In every other case, the captured state is the baseline, as in v1.0.
   - Every dirty entry logs one decision line, in the tray start and in `--restore-auto`:
     `journal: '<name>' dirty entry -> <decision> (current=[s1,..] zeroRpm=<on|off|n/a>; journal=[s1,..] zeroRpm=<on|off|n/a>; <reason>)`, where `<decision>` is `app-written` (Journal), `stale`, `already-restored` or `invalid`.
   - The current state is never captured over a valid dirty entry.
3. **Marking dirty:** immediately before the **first fan write** of a control period (the startup self-test's 35 % is the first one), the entry is written as dirty with the chosen baseline. A recovered entry is already dirty, so nothing is written.
4. **Restoring:** both `Reset()` call sites go through one baseline-aware restore.
   - The sites are `RestoreAutomatic` and the release-before-decrease in `SetFanPercent` (`FanController.cs:99`). If only one went through it, a recovering instance would briefly re-apply the killed instance's curve on every decrease.
   - When the baseline came from the capture, restore calls `Reset()` exactly as v1.0 does, so the RDNA2/3 path is unchanged.
   - When it came from the journal, restore calls `SetZeroRPM(zeroRpm)` (if supported), then `SetFanTuningStates(speeds)`. If the driver rejects that, restore logs it and falls back to `Reset()`, and reports the restore as not exact.
   - A restore that applied the journal exactly marks the entry clean. A failed one, or one that fell back to `Reset()`, leaves it dirty.
   - `MarkDirty` is saved with a disk flush. `MarkClean` is saved without one: losing a "clean" is harmless, and exit handlers are time-limited.
5. **Pruning:** after channel creation, entries whose identity matches no present GPU are dropped and logged. The tray path never drops a dirty entry, and an empty GPU list prunes nothing. `--restore-auto` also drops absent dirty entries (still never on an empty list). If `UniqueId` turns out not to be stable across reboots, the outcome is a discarded entry and v1.0 behaviour. That is a safe failure.
6. **Degrade, never block:** a corrupt journal (parse error, wrong schema) is logged and treated as empty. It is renamed to `baseline.json.bad` so it is never re-read. A failed journal write is logged and control continues as in v1.0. Fan control on a hot card is never refused because of the journal.
7. **`--restore-auto` one-shot CLI:**
   - It refuses with exit `4` if the app is running (single-instance mutex held). It first waits up to 15 s for the mutex, because a process killed inside a driver call can take a while to die. An abandoned mutex counts as free.
   - If the journal has no dirty entry, it exits `3` without loading ADL or ADLX.
   - Otherwise it runs the Phase B pre-load and opens ADLX. Per dirty GPU: a `Journal` choice is restored (`restored++`, or `failed++` with the entry left dirty); `AlreadyRestored` and `Stale` are marked clean and counted as neither; `Invalid` counts as failed and the entry stays dirty, because the fan may still carry the killed run's curve. It prunes entries for absent GPUs (logged).
   - Exit `0` when at least one GPU was restored and none failed; `1` on any failure; `3` when nothing needed restoring. No UI, no self-test, no other writes.
8. **Installer:**
   - **Uninstall** today removes the journal folder without restoring. `[UninstallRun]` runs, in order: `--stop` (graceful exit, restores every backend including ADL), `taskkill /F` as a fallback, then `--restore-auto` (covers ADLX after a kill), each with `waituntilterminated`. `--stop` is not used in `PrepareToInstall`, because the installed exe there may be v1.0.
   - Inno Setup runs `[UninstallRun]` as the **first** step of uninstallation, before any file is removed. So the exe and `baseline.json` still exist when `--restore-auto` runs, and `[UninstallDelete]` removes the folder only afterwards.
   - **Upgrade** needs no change: the relaunched app recovers from the journal.
9. **Scope:** ADLX only.
   - The ADL backends (Overdrive5 and OverdriveN, pre-RDNA) restore by setting the driver's automatic mode, which holds no captured state. A fresh instance's restore already returns them to automatic.
   - `--restore-auto` does not touch ADL adapters.
10. **Downstream automation** (coordinated separately, not this repo): after stopping the app, run `--restore-auto` and wait up to 15 s.

The fuller fix, restoring ADLX **driver defaults** through `IADLXManualFanTuning1::GetDefault*` (ADLX 1.4), needs binding work in `external/ADLX`. It stays a later follow-up.

**Phase F — `--automation` mode and `status.json` (added at plan review; contract signed off 2026-11-06).**

Downstream automation counts "engaged" when the process launches. A v1.0 app parked on the "AMD fan control unavailable" MessageBox therefore counts as engaged. With `--automation`:
- **No modal UI.** No MessageBox at start-up ("unavailable") and none for "already running". Balloon tips are non-modal and stay.
- **`status.json` beside `log.txt`** (`%LOCALAPPDATA%` of the user the process runs as), written atomically, in this sequence:
  1. Right after the single-instance mutex is acquired: `state: "starting"`.
  2. If no backend initialises: log the reasons, write `state: "unavailable"` with `reason: "no-backend"`, and exit `3`.
  3. If the startup self-test fails on every channel: write `state: "unavailable"` with `reason: "self-test-failed"`, then exit `3` gracefully (restore first).
  4. If it passes on at least one channel but every passing GPU is disabled in settings: write `state: "unavailable"` with `reason: "no-enabled-gpu"`, then exit `3` gracefully.
  5. If it passes on at least one enabled channel: write `state: "engaged"`, listing the passing enabled GPUs.
  6. On a graceful exit: `state: "stopped"`, `reason: "exit"`.
  - After `stopped` or `unavailable` is written, later writes are ignored.
  - "Already running" exits `4` and **does not** touch `status.json`, so the running instance's file stays authoritative. `--automation` waits up to 15 s for the single-instance mutex first.
  - **`--status-file <fullpath>` (optional):** writes the status file to that path instead of the default, so a caller does not have to guess which user profile it lands in.
    - The path must be drive-rooted (no UNC, no relative path, no `.`/`..` segment, no illegal characters), with an existing parent directory, and neither the parent nor the file may be a reparse point.
    - A missing or invalid value is logged and falls back to the default path. It never stops the app.
    - Without the flag, the default path is used, as before.
  - TerminateProcess leaves the last state on disk. The reader therefore matches `pid` and `startedUtc` against the process it launched.
- **Without `--automation`,** interactive behaviour and the files written are unchanged from v1.0.

`status.json` schema 1 (camelCase; unknown fields must be ignored by readers):

```json
{
  "schema": 1,
  "state": "starting | engaged | unavailable | stopped",
  "engaged": true,
  "reason": null,
  "gpu": "AMD Radeon RX 5700 XT",
  "backend": "ADLX",
  "gpus": [ { "name": "AMD Radeon RX 5700 XT", "backend": "ADLX" } ],
  "pid": 1234,
  "startedUtc": "2026-11-06T18:00:00Z",
  "updatedUtc": "2026-11-06T18:00:05Z",
  "version": "1.1.0"
}
```

- `engaged` is true only when `state` is `"engaged"`.
- `gpu` and `backend` describe the first passing GPU; `gpus` lists every one.
- `reason` is one of `null`, `"no-backend"`, `"self-test-failed"`, `"no-enabled-gpu"` or `"exit"`. Readers key off `state` and `engaged`, not `reason`.

**CLI (all modes):**

| Invocation | Behaviour | Exit codes |
|---|---|---|
| *(no args)* | Interactive tray app, unchanged from v1.0 | `0` |
| `--automation [--status-file <fullpath>]` | Tray app without modal UI, writes `status.json` | `0` graceful exit · `3` unavailable · `4` already running |
| `--probe [--no-preload]` | Read-only report (Phase D) | `0` controllable GPU · `3` none · `4` running · `1` error |
| `--restore-auto` | One-shot journal restore (Phase E) | `0` restored · `3` nothing to restore · `4` running · `1` error |
| `--stop` | Graceful stop of a running instance; opens the stop event only, never kills | `0` stopped · `3` not running · `1` timeout or older running build |

- **Usage errors exit `2`, with no UI:** combining any two of `--probe`, `--restore-auto` and `--stop`, or giving `--no-preload` without `--probe`.
- **Matching:** arguments are case-insensitive.
- **Unknown arguments** are logged and ignored, so a newer caller does not break an older build.
- **`--automation` with `--probe` or `--restore-auto`** is accepted; those modes are already non-interactive.

## 4. Safety analysis

- **Duty bounds and policy are unchanged:** floor 20 %, ceiling 85 %, over-temp latch, sensor-loss relinquish, startup test. Zero-RPM handling is unchanged (ADLX path).
- **The pre-load only chooses which AMD-signed copy of the same library is used.** It never writes, copies, renames or deletes a file, never touches System32 or the DriverStore, and never installs, removes or swaps a driver. Every uncertain case falls back to v1.0 behaviour.
- **The probe cannot write fan state;** this is enforced at compile time.
- **Restore on graceful exit, crash handlers and session end is unchanged.**
- **Unclean-stop restore gap** (kill, AccessViolation, power loss): pre-existing on all generations, and RDNA1 now joins it. Phase E closes it for ADLX:
  - the next start's graceful exit returns to the journaled pre-app state;
  - `--restore-auto` does it immediately after a kill;
  - uninstall runs `--restore-auto` too.
  A journal failure degrades to v1.0 behaviour and never blocks control.
- **Automation mode** removes the modal dialogs that left a non-controlling process looking engaged. The no-args interactive path is unchanged.
- **Startup self-test:** it writes 35 % for 4 s even when the card is already hot, and automation launches the app only after the core passes its trigger. That may sit below the driver's current duty. E2 reproduces it on purpose.
- **DLL loading:** full-path loads remove app-directory search for `atiadlxx.dll` and `amdadlx64.dll`. `ADLXCSharpBind.dll` (our own file) still loads from the app directory, as before.

## 5. Release and rollout

- **Version:** 1.1.0, a minor release: new hardware support, the new `--probe` CLI, and a change in library loading. Set it in `VERSION`, the csproj `<Version>`, the AboutForm fallback string and the `CHANGELOG.md` `[1.1.0]` entry.
  - `VERSION` stays `1.1.0` for the rc tags too, as v1.0.0's rcs did: `VERSION=1.0.0` on every `v1.0.0-rc.N`.
- **CI prerelease fix (must land before any rc tag):** the release step (`.github/workflows/build-windows.yml:128-131`) never sets `prerelease`.
  - `v1.0.0-rc.8` was created by the workflow bot and is now flagged Pre-release, so it was flipped by hand after publication. Until then it was the **latest** release.
  - A `v1.1.0-rc.1` tag would do the same. Add:
  - `prerelease: ${{ contains(github.ref_name, '-') }}`
  - `make_latest: ${{ !contains(github.ref_name, '-') && 'true' || 'false' }}`
- **Distribution:** downstream automation installs the newest **non-prerelease** GitHub release (`releases/latest`). It installs only when that is strictly newer than the installed exe's file version, and it verifies the installer's Authenticode publisher. Two consequences:
  - Promoting a tag to a final release **is** the rollout switch. Every downstream machine, including RDNA2/3 ones, picks it up at its next start, so the RDNA2/3 regression gate (E3) must pass first.
  - The installed-version check reads the exe's `FileVersion` string. An rc built from `VERSION=1.1.0` is `1.1.0.0`, the same as the final, so machines with the rc installed keep it.
  - That is acceptable only if the final's **code** equals the validated rc. Tag the final on the validated rc commit, or on a docs-only commit after it.
  - If any code changes after validation, cut a new rc and re-run the affected experiments.
  - If an rc install ever has to be removed, copy `log.txt` and `probe-*.txt` out **first**: `[UninstallDelete]` removes `%LOCALAPPDATA%\Bitworks\LightweightAmdGpuFanControl` (`installer/LightweightAmdGpuFanControl.iss:62-63`).
- **Sequence:**
  1. The implementation PR stays a draft until the lead has reviewed it. It contains Task 1 (CI), Phases A–F and the docs. It is merged with CI green.
  2. Ask the lead before the first rc tag. Tag `v1.1.0-rc.1`; CI signs it and publishes a prerelease. Confirm on GitHub that it shows Pre-release and that `releases/latest` is still v1.0.0.
  3. E1, then E2 and E3, each approved before it runs. E2 and E3 never run on a bench whose slot or card is under hardware suspicion.
  4. Tag `v1.1.0` on the validated code; CI signs it and publishes the latest release.
- **Never re-run a failed tag;** cut the next rc instead.
- **Rollback:** the automation never downgrades, so rollback means a `v1.1.1` that reverts Phase B. Phase B also self-disables on any doubt.
- **Signing:** CI only, through OIDC and the `release` environment. Never sign locally.

## 6. Hardware validation — declared experiments

Each experiment below is a template. The lead fills in the bench, card and build hash privately, and the lead **and** the owner approve **before** it runs. The card currently under hardware-fault suspicion must not be used. No experiment installs, removes or swaps a driver, or writes to System32.

**E1 — Probe (read-only with respect to fan state)**
- **Hypothesis:** H3. With the bound copies pre-loaded, ADLX reports manual fan tuning supported for the RDNA1 card. With `--no-preload` it reports unsupported, which reproduces the field failure.
- **Inputs:**
  - an approved RDNA1 card on a bench already in the mismatch state (RDNA1/2 bound, RDNA3/4 in System32);
  - the signed `v1.1.0-rc.1` installer, SHA-256 recorded;
  - the card idle and the fan-control utility not running;
  - no bench job running, with the automation's own GPU sensor polling idle (it also loads ADL), and no other GPU tools.
  - Run as the bench's interactive user, in that user's session: `--probe`, then `--probe --no-preload`. The declaration names how it is launched in that session.
- **Bounds:** no fan-write API reachable (compile-time); each run takes 30 s or less. Take a fan snapshot before and after: tuning states, zero-RPM state and RPM, read-only.
- **Pass:**
  - the pre-load line names both DriverStore copies;
  - ADLX reports manual fan tuning supported;
  - the `--no-preload` run reports unsupported;
  - the probe's module list shows **no mixed driver line**: no `ati*`/`amd*` module is loaded from System32 when the bound package ships a file of the same name.
- **A mixed module list fails E1 even if fan tuning shows as supported.** Something then loads AMD DLLs by bare name at runtime, and a process holding two driver lines can crash inside the driver. In that case Phase B needs redesign before any fan write (E2).
- **Branches:**
  - Both runs unsupported → first check the probe's AMD module list for System32 copies loaded by bare name, and ADLX's out-of-process service (`AMDADLXServ.exe` ships in both packages). If neither explains it → H1-cap → write the Phase C plan.
  - Both supported → mismatch is not causal → re-investigate with the Phase A diagnostics.
- **Cleanup:** confirm the fan snapshot is unchanged. Keep the rc installed for E2, or uninstall it.
- **Also:** run E1 read-only on one RDNA2 and one RDNA3 bench, expecting `not needed` or `preloaded`, with manual tuning supported.

**E2 — Controlled engagement on RDNA1 (writes fan values)**
- **Hypothesis:** the rc regulates the RDNA1 card under load and restores the pre-run curve and zero-RPM state on graceful exit.
- **Inputs:** same card and bench; default settings (Automatic, target 65 °C, min 20 %, max 85 %); a bounded stress of 10 min or less, tool named by the lead; independent temperature telemetry.
  - Start the app **mid-load**, once the core's 60 s mean is above 74 °C. That reproduces the automation's trigger and the startup self-test's 35 % write on a hot card.
- **Abort and recovery:**
  - Abort on any of: edge 90 °C or higher; hotspot 100 °C or higher; edge rising more than 5 °C during the 4 s self-test; fan RPM not rising within 10 s of a commanded increase; a driver timeout or reset; the device dropping off the bus.
  - On abort: stop the load, exit the app from the tray (graceful), and verify the curve.
  - If the app hangs: a human resets fan tuning in AMD Software, so a person must be reachable during E2.
- **Pass:**
  - the self-test passes on ADLX;
  - the commanded % reads back and RPM rises;
  - the core holds inside the policy band and never reaches the critical latch;
  - after the graceful exit, the read-back curve and zero-RPM state equal the pre-run snapshot.
- **Cleanup:** graceful exit; a read-only probe confirms the baseline; record scrubbed evidence in `docs/verification-evidence.md`.

**E3 — Automation-driven regression (writes fan values)**
- **Hypothesis:** with the rc installed, the downstream automation's normal hot-card run gives a "fan regulated" verdict on RDNA1, and still on RDNA2 and RDNA3.
- **Pass:**
  - a regulated verdict on every generation, with no "fan unverified" or "missed engagement";
  - after each run, the automation's stop (a kill) is followed by `--restore-auto` (exit `0`) and then a read-only probe. The fan state is back at the pre-run baseline and the journal is clean.
  - after the kill, the log shows the GPU's state was recognised as app-written, i.e. a decision line `journal: '<name>' dirty entry -> app-written (current=[...] ...; journal=[...] ...; ...)`, followed by `--restore-auto` exit `0`.
  - If the automation does not yet call `--restore-auto`, the operator runs it by hand at that point and records that.
- **Recovery:** if a leftover manual curve is found anyway, a human resets fan tuning in AMD Software before the bench takes another job, and E3 fails.
- **Cleanup:** none needed when the final is tagged on the validated code (§5).

## 7. Out of scope / follow-ups

- **Restoring ADLX driver defaults** (`IADLXManualFanTuning1::GetDefaultFanTuningStates` / `GetDefaultZeroRPMState`) as the most robust restore. It needs binding work in `external/ADLX`; the minimal fix is Phase E.
- **Full ADL Overdrive8 backend:** only if E1 calls for it (Phase C).
- **Mixed driver lines across several AMD GPUs:** stays on v1.0 behaviour. It would need one process per driver line.
- **A restore journal for the ADL backends:** not needed; they restore to the driver's automatic mode (Phase E, item 9).
- **Possible later cleanup:** removing the CustomFan branch for Overdrive version 7 (older cards). This needs its own validation.
- **Downstream automation changes:** passing `--automation`, reading `status.json` for engagement, and running `--restore-auto` after a kill. These belong to the downstream owner, not this repo.

## 8. Implementation plan

### Global Constraints

- Fan bounds are fixed: floor `20`, ceiling `85` (`MinFanPercent`/`MaxFanPercent`); none of these tasks changes them.
- Never write to System32/SysWOW64 or the DriverStore. Never install or remove drivers. No `LoadLibrary` by bare name for `atiadlxx.dll` / `amdadlx64.dll`.
- The only library sources are `%windir%\System32\DriverStore\FileRepository\<pkg>\…` (bound package, signer-pinned) and `%windir%\System32\<name>`.
- AMD signer pin: subject contains `O=Advanced Micro Devices`; issuer contains `CN=Sectigo Public Code Signing CA R36`.
- `LoadLibraryExW` flags for the pre-load: `0x00000900`. Pre-loaded modules are never freed.
- All new P/Invokes carry `[DefaultDllImportSearchPaths(DllImportSearchPath.System32)]`.
- Pure logic goes in `LightweightAmdGpuFanControl.Core` (netstandard2.0) and is tested in `LightweightAmdGpuFanControl.Tests` (net8.0). Win32 adapters go in the app project behind interfaces.
- Public repo: no bench names, serials, hostnames or internal project names in code, tests, commits or PR text.
- Version `1.1.0` (rc tags `v1.1.0-rc.N`).
- Exit codes: `0` ok, `1` error, `2` usage, `3` nothing to do (no controllable GPU / unavailable / nothing to restore), `4` another instance running. The no-args path keeps v1.0 behaviour.
- **Files in the app data folder:** `log.txt`, `probe-<yyyyMMddTHHmmssZ>.txt`, `status.json` (only with `--automation`) and `baseline.json`. `status.json` and `baseline.json` are written atomically.
- **Verification commands** (macOS or Windows):
  - tests: `DOTNET_ROLL_FORWARD=Major dotnet test LightweightAmdGpuFanControl.Tests`;
  - app compile: `dotnet build LightweightAmdGpuFanControl/LightweightAmdGpuFanControl.csproj -p:AdlxBindPath=<any existing file>` (needs `git submodule update --init external/ADLX`). Expected: 0 errors.
  - CI on the PR is the Windows build and installer gate.

### Review Focus

1. **Two AMD GPUs on the same driver line** (two RDNA1 cards on one `oemNN.inf`): pre-load once and control both. Test in Task 3: `Decide_TwoDevicesSameInf_Preloads`.
2. **A Ryzen iGPU (also `VEN_1002`) plus a discrete RDNA1 card on a different INF:** this counts as mixed driver lines, so no pre-load; log it and keep v1.0 behaviour. This is the likeliest public-user case. Test in Task 3: `Decide_IgpuPlusDiscreteDifferentInfs_MixedDriverLines`.
3. **A package with `atiadlxx.dll` but no `amdadlx64.dll`:** refuse and log; never mix lines between ADL and ADLX. Test in Task 3: `Decide_AdlxMissingFromPackage_Unresolved`.
4. **A machine where System32 already matches** (the common single-line user): no pre-load, behaviour identical to v1.0 apart from the full-path System32 load. Test in Task 3: `Decide_VersionsEqual_NotNeeded`.
5. **INF quirks:** `atiadlxx.dll` listed only in the plain `[SourceDisksFiles]` section; inline `;` comments; quotes; `%strkey%` in the disk path. Resolve from the plain section, strip comments and quotes, refuse `%…%` paths. Test in Task 2: `Resolve_PlainSectionOnly_Resolves`, `Resolve_CommentsAndQuotes_Stripped`, `Resolve_PercentTokenInPath_Refused`.
6. **A corrupt or hand-edited `baseline.json`** (truncated JSON, wrong schema, speeds out of range, wrong count): treat it as empty and use the captured state. Never throw and never block control. Tests in Task 8: `Parse_Truncated_EmptyWithError`, `Parse_WrongSchema_EmptyWithError`, `Choose_SpeedOutOfRange_Captured`, `Choose_CountMismatch_Captured`.
7. **Two identical cards** (same name): journal entries must not collide. Test in Task 8: `Journal_SameNameDifferentUniqueId_SeparateEntries`.
8. **A stale `status.json` from a killed run:** the reader must be able to tell it apart. Test in Task 7: `Status_CarriesPidAndStartedUtc`.

---

### Task 1: CI publishes `-` tags as prereleases

**Files:**
- Modify: `.github/workflows/build-windows.yml:128-131`
- Modify: `docs/release-runbook.md` (add the rc/prerelease rule and the equal-file-version cleanup note)

- [ ] **Step 1:** Add the `prerelease` and `make_latest` inputs to the `softprops/action-gh-release@v2` step, using the exact expressions in §5.
- [ ] **Step 2:** Validate: run `actionlint .github/workflows/build-windows.yml` if available; otherwise run a YAML parse (`python3 -I -c "import yaml,sys;yaml.safe_load(open(sys.argv[1]))" .github/workflows/build-windows.yml`). Expected: no errors.
- [ ] **Step 3:** Commit `ci: publish hyphenated (rc) tags as prereleases, never as latest`.

### Task 2: INF package-file resolver (Core, pure)

**Files:**
- Create: `LightweightAmdGpuFanControl.Core/Drivers/InfPackageFile.cs`
- Test: `LightweightAmdGpuFanControl.Tests/InfPackageFileTests.cs`

**Interfaces — Produces:**
- `public sealed class InfPackageFileResult { public string? Path; public string? Reason; }`
- `public static InfPackageFileResult InfPackageFile.Resolve(string storeInfPath, IReadOnlyList<string>? infLines, string fileName)`, which never throws.

- [ ] **Step 1: Write failing tests.** Use package folder `C:\Windows\System32\DriverStore\FileRepository\u0205305.inf_amd64_x\u0205305.inf`.
  - `Resolve_Amd64SectionWins`: `[SourceDisksNames.amd64] 1 = %D%,,,\B026653` plus `[SourceDisksFiles.amd64] atiadlxx.dll = 1`, together with a conflicting plain section. Path ends `\u0205305.inf_amd64_x\B026653\atiadlxx.dll`.
  - `Resolve_PlainSectionOnly_Resolves`.
  - `Resolve_SubdirAppended`: `atiadlxx.dll = 1,sub`. Path ends `\B026653\sub\atiadlxx.dll`.
  - `Resolve_CommentsAndQuotes_Stripped`: `"atiadlxx.dll" = 1 ; c` and `1 = "D",,,"\B026653"`.
  - `Resolve_DotDot_Refused`, `Resolve_Rooted_Refused` (`C:\x`), `Resolve_PercentTokenInPath_Refused` (`%Dir%`), `Resolve_IllegalChar_Refused` (`<`). In each, `Path == null` and `Reason` is not empty.
  - `Resolve_FileNotListed_Reason`, `Resolve_UnknownDiskId_Reason`, `Resolve_NoSourceDisksNames_Reason`.
  - `Resolve_FileNameWithSlash_Refused`, `Resolve_NullLines_Reason`.
  - `Resolve_SectionNamesCaseInsensitive`.
- [ ] **Step 2:** Run `dotnet test LightweightAmdGpuFanControl.Tests --filter InfPackageFileTests`. Expected: FAIL (type missing).
- [ ] **Step 3:** Implement `Resolve`. Parse sections line by line; a key/value split happens at the first `=`. `SourceDisksNames` value field 4 (0-based index 3) is the disk path; `SourceDisksFiles` value field 1 is the disk id and field 2 the optional subdir. Validate each `\`-separated segment. The result is `packageFolder + "\" + segments`.
- [ ] **Step 4:** Run the same filter. Expected: PASS.
- [ ] **Step 5:** Commit `feat(core): resolve a driver package file from its own INF SourceDisks entries`.

### Task 3: Bound-library planner + signer pin + Overdrive routing (Core, pure)

**Files:**
- Create: `LightweightAmdGpuFanControl.Core/Drivers/BoundAdlPlanner.cs`
- Create: `LightweightAmdGpuFanControl.Core/Drivers/AmdSignerPin.cs`
- Create: `LightweightAmdGpuFanControl.Core/Drivers/OverdriveRouting.cs`
- Test: `LightweightAmdGpuFanControl.Tests/BoundAdlPlannerTests.cs`, `AmdSignerPinTests.cs`, `OverdriveRoutingTests.cs`

**Interfaces — Produces:**
- `public enum BoundAdlKind { NoAmdDisplay, MixedDriverLines, Unresolved, NotNeeded, TrustRefused, Preload }`
- `public sealed class BoundAdlFacts { public IReadOnlyList<string> PresentAmdBoundInfs; public string? StoreInfPath; public string DriverStoreRoot; public string? AtiadlxxPath, AdlxPath; public string? SystemAtiadlxxVersion, SystemAdlxVersion, BoundAtiadlxxVersion, BoundAdlxVersion; public bool? AtiadlxxTrusted, AdlxTrusted; public string? Reason; }`
- `public sealed class BoundAdlDecision { public BoundAdlKind Kind; public string Reason; public string? AtiadlxxPath; public string? AdlxPath; }`
- `public static BoundAdlDecision BoundAdlPlanner.Decide(BoundAdlFacts f)`
- `public static bool AmdSignerPin.Matches(string? subject, string? issuer)`
- `public enum OverdriveFamily { Unsupported, Overdrive5, OverdriveN, Overdrive8 }` and `public static OverdriveFamily OverdriveRouting.Classify(int version)`

- [ ] **Step 1: Write failing tests.**
  - `Decide_NoDevices_NoAmdDisplay`.
  - `Decide_TwoDistinctInfs_MixedDriverLines`.
  - `Decide_IgpuPlusDiscreteDifferentInfs_MixedDriverLines`: `oem3.inf` plus `oem21.inf`.
  - `Decide_TwoDevicesSameInf_Preloads`.
  - `Decide_StoreOutsideDriverStore_Unresolved`.
  - `Decide_AtiadlxxMissing_Unresolved`.
  - `Decide_AdlxMissingFromPackage_Unresolved`.
  - `Decide_BoundVersionUnreadable_Unresolved`.
  - `Decide_VersionsEqual_NotNeeded`: both pairs are `32.0.21045.16010`.
  - `Decide_EitherUntrusted_TrustRefused`.
  - `Decide_Mismatch_Trusted_PreloadsBothPaths`: System32 `32.0.32015.7029`, bound `32.0.21045.16010`. Expect `Kind == Preload` and both paths set.
  - `AmdSignerPin`: true for subject `CN=Advanced Micro Devices, O=Advanced Micro Devices, S=California, C=US` with issuer `CN=Sectigo Public Code Signing CA R36, O=Sectigo Limited, C=GB`. False for `O=Advanced Micro Devices Inc` (exact RDN match), for a Microsoft subject, for a different issuer, and for null.
  - `OverdriveRouting`: `Classify(5)==Overdrive5`, `Classify(7)==OverdriveN`, `Classify(8)==Overdrive8`, and `Classify(6)`, `Classify(9)`, `Classify(0)`, `Classify(-1)` all `==Unsupported`.
- [ ] **Step 2:** Run `dotnet test LightweightAmdGpuFanControl.Tests --filter "BoundAdlPlannerTests|AmdSignerPinTests|OverdriveRoutingTests"`. Expected: FAIL.
- [ ] **Step 3:** Implement. Rule order is exactly §3 Phase B steps 2–6. Distinct INFs are compared case-insensitively. The DriverStore-root prefix check is case-insensitive and requires a trailing `\`. `AmdSignerPin` splits the distinguished name on `, ` and matches whole RDNs (`O=Advanced Micro Devices`, `CN=Sectigo Public Code Signing CA R36`).
- [ ] **Step 4:** Run the same filter. Expected: PASS.
- [ ] **Step 5:** Commit `feat(core): bound ADL/ADLX pre-load planner, AMD signer pin, Overdrive routing`.

### Task 4: Win32 adapters + pre-loader wired before backend creation; full-path ADL load; OD routing in the ADL backend

**Files:**
- Create: `LightweightAmdGpuFanControl/Gpu/Native/BoundAdlPreloader.cs`. It holds the static `Run(LogService) -> BoundAdlDecision` and the private Win32 adapters: SetupAPI display enumeration with `InfPath` from `SetupDiOpenDevRegKey(DIREG_DRV)`, `SetupGetInfDriverStoreLocationW`, `WinVerifyTrust` (`WTD_CHOICE_FILE`, `WTD_REVOKE_NONE`) plus `X509Certificate.CreateFromSignedFile`, binary `FileVersionInfo`, the reparse-point check, `LoadLibraryExW(…, 0x900)`, and the `GetModuleHandle`/`GetModuleFileName` check.
- Modify: `LightweightAmdGpuFanControl/Services/FanControlService.cs:71`. Call `BoundAdlPreloader.Run(_logService)` before `FanControlBackendFactory.CreateAll`.
- Modify: `LightweightAmdGpuFanControl/Gpu/Adl/AdlNativeApi.cs:9-38,91-122`. Replace bare-name `LoadLibrary` with: an already-loaded module (`GetModuleHandleEx`, refcounted), else `LoadLibraryExW("%windir%\System32\atiadlxx.dll", 0, 0x800)`. Drop the `atiadlxy.dll` attempt (a 64-bit process only). Keep the first real error.
- Modify: `LightweightAmdGpuFanControl/Gpu/Adl/AdlFanControlBackend.cs:59-89,115-135`. Both sites branch on `OverdriveRouting.Classify(version)`:
  - `OverdriveN` (version 7) keeps v1.0's code, including the CustomFan preference.
  - `Overdrive5` is unchanged.
  - `Overdrive8` logs `ADL adapter {i}: Overdrive8 — no ADL fan backend; ADLX is the supported path.` and returns null (or skips the adapter in `Initialize`).
  - `Unsupported` returns null.

**Interfaces — Consumes:** Task 2 `InfPackageFile.Resolve`; Task 3 `BoundAdlPlanner.Decide`, `AmdSignerPin.Matches`, `OverdriveRouting.Classify`.
**Interfaces — Produces:** `BoundAdlPreloader.Run(LogService log) -> BoundAdlDecision`, idempotent per process, never throws. It always logs one line: `adl-preload: <kind> (system32 <v>/<v>, bound <v>/<v>, <reason>)`. When the decision is not `Preload`, it pre-loads `%windir%\System32\amdadlx64.dll` by full path if that file exists.

- [ ] **Step 1:** Implement the adapters and the wiring above.
- [ ] **Step 2:** Run `dotnet test LightweightAmdGpuFanControl.Tests`. Expected: all PASS. The Windows build is verified by the CI `build` job on the PR (net48 and ADLX native need Windows). Expected: green.
- [ ] **Step 3:** Commit `fix: load the bound AMD driver line's own ADL/ADLX (DriverStore, AMD-signed) before ADLX/ADL start`.

### Task 5: Diagnostics (Phase A)

**Files:**
- Modify: `Adlx/AdlxInitializer.cs:20-35`. Return `bool Initialize(out string? error)`, where `error` is the exception message carrying the `ADLX_RESULT`.
- Modify: `Adlx/AdlxSession.cs:36-56`. Log `ADLX init failed: <error>`.
- Modify: `Gpu/FanControlBackendFactory.cs:33-56`. Log `ADLX: <n> GPU(s)`. Log one line per GPU: `ADLX GPU <i> '<name>': manual fan tuning supported=<bool>`, or `…: <error>`.
- Modify: `Adlx/FanController.cs:52-73`. Add `bool Initialize(GPU gpu, out string? reason)`.
- Modify: `Gpu/Adl/AdlFanControlBackend.cs:56-90`. Log one line per adapter: `ADL adapter <i>: caps rc=<rc> supported=<s> enabled=<e> version=<v> family=<f>`.
- After backend creation in `FanControlService.Start`, log the loaded module path and version of `atiadlxx.dll` and `amdadlx64.dll`.

- [ ] **Step 1:** Implement. Messages must not include user paths other than the loaded module paths.
- [ ] **Step 2:** Run `dotnet test` plus CI build. Expected: PASS / green.
- [ ] **Step 3:** Commit `feat: log why ADLX/ADL found no controllable GPU`.

### Task 6: Launch options + `--probe` read-only mode (Phase D)

**Files:**
- Create: `LightweightAmdGpuFanControl.Core/Cli/LaunchOptions.cs` (the parser plus `ExitCodes`).
- Create: `LightweightAmdGpuFanControl.Core/Diagnostics/ProbeReport.cs`. It holds the pure report model and its formatter.
- Test: `LightweightAmdGpuFanControl.Tests/LaunchOptionsTests.cs`, `ProbeReportTests.cs`
- Create: `LightweightAmdGpuFanControl/Services/CapabilityProbe.cs`. It uses only the read-only `IFanCapabilitySource`, which has no Set, Reset, Restore or ZeroRPM-set member.
- Modify: `LightweightAmdGpuFanControl/Program.cs`. `static int Main(string[] args)` parses the options before any UI. It dispatches `Probe` (Task 6), `RestoreAuto` (Task 8) and `Tray` (with `Automation` from Task 7).

**Interfaces — Produces:**
- `public enum LaunchMode { Tray, Probe, RestoreAuto }`
- `public sealed class LaunchOptions { public LaunchMode Mode; public bool Automation; public bool NoPreload; public IReadOnlyList<string> Ignored; public string? Error; public static LaunchOptions Parse(IReadOnlyList<string>? args); }`
- `public static class ExitCodes { public const int Ok = 0, Error = 1, Usage = 2, NothingToDo = 3, AlreadyRunning = 4; }`
- `ProbeReport` (`PreloadLine`, `AdlxInit`, `JournalLine`, `List<ProbeGpu>`, `List<ProbeAdlAdapter>`, `List<ProbeModule>` with `Name`, `Path`, `Version` for every loaded `ati*` / `amd*` module), with `string Format()` and `int ExitCode`. `ExitCode` is `0` when any `ProbeGpu.ManualFanTuningSupported`, else `3`.

- [ ] **Step 1:** Write failing tests:
  - `Parse_NoArgs_TrayInteractive`;
  - `Parse_Automation_TrayAutomation`;
  - `Parse_Probe_NoPreload`;
  - `Parse_RestoreAuto`;
  - `Parse_CaseInsensitive`;
  - `Parse_UnknownArg_IgnoredAndListed`;
  - `Parse_ProbeAndRestoreAuto_UsageError`;
  - `Parse_NoPreloadWithoutProbe_UsageError`;
  - `Parse_AutomationWithProbe_Accepted`;
  - `Parse_Null_Tray`;
  - `ExitCode_AnySupported_Zero`;
  - `ExitCode_NoneSupported_Three`;
  - `Format_ContainsPreloadLineJournalLineAndEachGpu`;
  - `Format_NoUserProfilePath`: a report built with a module path under `C:\Users\x\` renders it as `%USERPROFILE%\…`.
- [ ] **Step 2:** Run `dotnet test --filter "LaunchOptionsTests|ProbeReportTests"`. Expected: FAIL.
- [ ] **Step 3:** Implement `LaunchOptions`, `ProbeReport`, `CapabilityProbe` and the `Program` dispatch.
  - Usage errors are logged and exit `2`, with no UI.
  - Probe takes the mutex non-blockingly; if another instance holds it, exit `4`.
  - The report is written to `log.txt` and to `probe-<yyyyMMddTHHmmssZ>.txt` in the app data folder.
  - Confirm by reading `external/ADLX/Samples/csharp/ADLXWrapper` that disposing the wrappers the probe touches performs no write. Record that in a code comment.
- [ ] **Step 4:** Run the tests and the app compile. Expected: PASS / 0 errors.
- [ ] **Step 5:** Commit `feat: --probe read-only capability report (no fan writes)`.

### Task 7: `--automation` mode and `status.json` (Phase F)

**Files:**
- Create: `LightweightAmdGpuFanControl.Core/Automation/AutomationStatus.cs`. It holds the model, schema 1, and the camelCase `System.Text.Json` serializer.
- Create: `LightweightAmdGpuFanControl.Core/IO/AtomicFile.cs`
- Test: `LightweightAmdGpuFanControl.Tests/AutomationStatusTests.cs`, `AtomicFileTests.cs`
- Modify: `Program.cs` (with automation: on "already running", exit `4` silently; otherwise run the tray and return the context's exit code).
- Modify: `SystrayApplicationContext.cs` (take `LaunchOptions`; expose `int ExitCode`; with automation, the paths below).
- Modify: `Services/FanControlService.cs` (`Start` returns whether a backend initialised; with automation, no MessageBox; report the self-test outcome).

**Interfaces — Produces:**
- `public sealed class AutomationStatus { public int Schema = 1; public string State; public bool Engaged; public string? Reason; public string? Gpu; public string? Backend; public List<StatusGpu> Gpus; public int Pid; public string StartedUtc; public string UpdatedUtc; public string Version; public string ToJson(); }` and `public sealed class StatusGpu { public string Name; public string Backend; }`.
- Factory methods take `(int pid, DateTime startedUtc, DateTime nowUtc, string version)`:
  - `AutomationStatus.Starting(…)`
  - `.Engaged(…, IReadOnlyList<StatusGpu> gpus)`
  - `.Unavailable(…, string reason)`
  - `.Stopped(…)`
- `public static class StatusReasons { public const string NoBackend = "no-backend", SelfTestFailed = "self-test-failed", Exit = "exit"; }`
- `LaunchOptions` also carries `public string? StatusFile` and `public string? StatusFileError`. `--status-file` consumes the next argument unless that is missing or starts with `--`; then it records a warning, which is not a usage error.
- `public static string? StatusFilePath.Validate(string? path, Func<string,bool> directoryExists, Func<string,bool> isReparsePoint, out string? error)`. It returns the validated path, or null with an error. The app falls back to the default path on null.
- `public static bool AtomicFile.TryWriteAllText(string path, string contents, out string? error)`. It writes `path + ".tmp"` in the same folder, then replaces `path` (`File.Replace` when `path` exists, `File.Move` otherwise). It never throws.

- [ ] **Step 1:** Write failing tests:
  - `Starting_StateStartingNotEngaged`;
  - `Engaged_FirstGpuAndAllGpus`;
  - `Unavailable_ReasonSet`;
  - `Stopped_ReasonExit`;
  - `ToJson_CamelCaseSchema1`: it parses back with `JsonDocument` and has keys `schema`, `state`, `engaged`, `reason`, `gpu`, `backend`, `gpus`, `pid`, `startedUtc`, `updatedUtc`, `version`;
  - `Status_CarriesPidAndStartedUtc`;
  - `Utc_Iso8601Z`;
  - `AtomicFile_CreatesNew`;
  - `AtomicFile_ReplacesExisting`;
  - `AtomicFile_NoTmpLeftBehind`;
  - `AtomicFile_MissingFolder_FalseWithError`;
  - status file: `StatusFile_Valid_Returned`, `StatusFile_Relative_Rejected`, `StatusFile_Unc_Rejected`, `StatusFile_DotDot_Rejected`, `StatusFile_ParentMissing_Rejected`, `StatusFile_ReparseParent_Rejected`, `StatusFile_ReparseFile_Rejected`, `StatusFile_IllegalChar_Rejected`, `StatusFile_Null_Rejected`;
  - in `LaunchOptionsTests`: `Parse_StatusFile_Captured`, `Parse_StatusFileMissingValue_WarningNotError`, `Parse_StatusFileFollowedByFlag_NotConsumed`, `Parse_NoStatusFile_Null`.
- [ ] **Step 2:** Run `dotnet test --filter "AutomationStatusTests|AtomicFileTests"`. Expected: FAIL.
- [ ] **Step 3:** Implement, then wire the app per Phase F:
  - write `starting` after the mutex;
  - on no backend, write `unavailable`/`no-backend`, dispose the tray icon, return `3` without `Application.Run`;
  - when the self-test fails everywhere, write `unavailable`/`self-test-failed`, then do a graceful `Exit()` with code `3` on the UI thread;
  - when any channel passes, write `engaged`;
  - on a graceful exit, write `stopped`.
  - A write failure is logged and never stops control.
  - Without automation, nothing in this list runs.
- [ ] **Step 4:** Run the tests and the app compile. Expected: PASS / 0 errors.
- [ ] **Step 5:** Commit `feat: --automation mode with status.json (no modal UI for unattended runs)`.

### Task 8: Restore journal and `--restore-auto` (Phase E)

**Files:**
- Create: `LightweightAmdGpuFanControl.Core/Recovery/BaselineJournal.cs`, containing:
  - `GpuIdentity`, `FanBaseline`, `BaselineEntry` and `BaselineJournal`;
  - `BaselinePlanner` and `BaselineChoice`;
  - `RestoreAutoResult`.
- Test: `LightweightAmdGpuFanControl.Tests/BaselineJournalTests.cs`
- Create: `LightweightAmdGpuFanControl/Services/BaselineJournalSession.cs`. It is the file-backed shared journal: lock, `AtomicFile` save, and `.bad` quarantine.
- Create: `LightweightAmdGpuFanControl/Services/RestoreAutoRunner.cs`
- Modify:
  - `Adlx/FanController.cs`: `Initialize(GPU, BaselineJournalSession?)`, baseline-aware restore at both `Reset()` sites, and mark dirty before the first write.
  - `Gpu/AdlxFanControlBackend.cs` and `Gpu/FanControlBackendFactory.cs`: pass the session through, then prune after channel creation.
  - `installer/LightweightAmdGpuFanControl.iss`: two `[UninstallRun]` entries.

**Interfaces — Produces:**
- `public sealed record GpuIdentity(string Name, int UniqueId, int StateCount) { public string Key { get; } }`. Key = `$"{Name}|{UniqueId}"`. StateCount is validated by `Choose`.
- `public sealed record FanBaseline(IReadOnlyList<int> Speeds, bool ZeroRpmSupported, bool ZeroRpm)`
- `public sealed class BaselineEntry { public string Key, Name; public int UniqueId, StateCount; public int[] Speeds; public bool ZeroRpmSupported, ZeroRpm, Dirty; public string Utc; }`
- `BaselineJournal` members:
  - `public int Schema = 1`
  - `public List<BaselineEntry> Entries`
  - `public static BaselineJournal Parse(string? json, out string? error)`, which never throws; on error it returns an empty journal
  - `public string ToJson()`
  - `public BaselineEntry? Find(GpuIdentity id)`
  - `public void MarkDirty(GpuIdentity id, FanBaseline b, DateTime utc)`
  - `public bool MarkClean(GpuIdentity id)`
  - `public IReadOnlyList<BaselineEntry> PruneAbsent(IEnumerable<GpuIdentity> present)`, which removes and returns entries whose key is not present
  - `public bool HasDirty`
- `public enum BaselineSource { Captured, Journal }`; `public sealed class BaselineChoice { public FanBaseline Baseline; public BaselineSource Source; public string Reason; }`; `public static BaselineChoice BaselinePlanner.Choose(GpuIdentity id, FanBaseline captured, BaselineEntry? entry)`
- `public static int RestoreAutoResult.ExitCode(int restored, int failed)`: `failed > 0 → 1`, `restored > 0 → 0`, else `3`.

- [ ] **Step 1:** Write failing tests:
  - `Choose_NoEntry_Captured`;
  - `Choose_CleanEntry_Captured`;
  - `Choose_DirtyValid_Journal`;
  - `Choose_SpeedOutOfRange_Captured`;
  - `Choose_CountMismatch_Captured`;
  - `Choose_ZeroRpmCapabilityMismatch_Captured`;
  - `Journal_MarkDirtyThenFind`;
  - `Journal_MarkCleanKeepsEntryNotDirty`;
  - `Journal_SameNameDifferentUniqueId_SeparateEntries`;
  - `Journal_PruneAbsent_RemovesOnlyAbsent`;
  - `Journal_RoundTrip`;
  - `Parse_Null_Empty`;
  - `Parse_Truncated_EmptyWithError`;
  - `Parse_WrongSchema_EmptyWithError`;
  - `ExitCode_RestoredZero_FailedOne_NothingThree`.
- [ ] **Step 2:** Run `dotnet test --filter BaselineJournalTests`. Expected: FAIL.
- [ ] **Step 3:** Implement the Core types, then the app wiring per Phase E items 1–8.
  - `FanController` keeps calling `Reset()` when `Source == Captured`.
  - The journal path is `SetZeroRPM` (if supported), then `SetFanTuningStates(int[])`.
  - `--restore-auto` exits `3` before loading any AMD library when `HasDirty` is false.
- [ ] **Step 4:** Run the tests and the app compile. Expected: PASS / 0 errors.
- [ ] **Step 5:** Commit `feat: journal the pre-app fan state and restore it after an unclean stop (--restore-auto)`.

### Task 9: Version, docs, changelog

**Files:**
- Modify: `VERSION` → `1.1.0`, the same for the rc tags and the final tag (§5)
- Modify: `LightweightAmdGpuFanControl/LightweightAmdGpuFanControl.csproj:16`
- Modify: the AboutForm fallback version string
- Modify: `CHANGELOG.md`, `[1.1.0]` entry:
  - **Fixed:** RDNA1 under mixed driver lines; the fan state left behind after an unclean stop or uninstall.
  - **Added:** `--probe`, `--automation` with `status.json`, `--restore-auto`.
  - **Changed:** library loading; Overdrive8 no longer reaches the ADL fan path.
  - **Security:** no DLL search for ADL/ADLX.
- Modify: `docs/AMD_COMPATIBILITY.md` (backend order, the bound-line rule, Overdrive8, the probe)
- Modify: `README.md` and `LightweightAmdGpuFanControl/Resources/Help.md` (the CLI table, troubleshooting with `--probe`)
- Modify: `docs/agent-handoff.md`, `docs/verification-evidence.md`

- [ ] **Step 1:** Edit the files. `rg -n "1\.0\.0" --glob '!CHANGELOG.md'` must show no stale product-version hits.
- [ ] **Step 2:** Run `gitleaks detect --no-banner` if available. Expected: no leaks.
- [ ] **Step 3:** Commit `docs: 1.1.0 RDNA1 support, probe mode, library loading`.

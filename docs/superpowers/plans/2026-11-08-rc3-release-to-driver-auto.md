# 1.1.0-rc.3: Release Fans to the Driver's Automatic Control — Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Status:** APPROVED (2026-11-08) with amendments 5b, 5c. No hardware step runs until its declared experiment (§6) has been approved.

**Goal:** Every restore path (graceful exit, `--stop`, `--restore-auto`, and recovery after a crash or kill) must return a GPU whose pre-app fan state was the driver's automatic control to that automatic control. A user's own pre-app manual curve must still come back as that manual curve.

**Architecture:** Read ADLX `IADLXGPUTuningServices::IsAtFactory` before the first fan write and journal it per GPU. On restore, a pure Core planner chooses one action:
- `FactoryReset` (ADLX `ResetToFactory`) when the pre-app state was at factory;
- `ReapplyPoints` (today's `SetFanTuningStates` of the captured or journaled points) when it was not;
- `ReapplyPoints` plus a loud "mode unknown" record when the pre-app state is unknown.

The probe prints each GPU's tuning-at-factory state.

**Tech Stack:** .NET Framework 4.8 WinForms app; `netstandard2.0` Core library with `net8.0` xUnit tests; ADLX SDK 1.4 SWIG binding (`external/ADLX`, the Rem0o/ADLX fork at `240b1f3`).

**Spec:** This document (sections 1–5 are the design; section 7 is the task plan).

## Global Constraints

- Fan control is safety-critical. Never set a duty that could overheat a card. Always restore the driver's automatic fan control on exit, crash or uninstall. Zero-RPM handling must stay correct.
- Never write to System32/SysWOW64. Never install, remove or swap drivers.
- The repo is public: no bench names, serials, internal hostnames or private plan ids in code, commits or PR text.
- Releases are signed only by CI (OIDC release environment). Never sign locally; never re-run a failed tag.
- Any test on real hardware is a declared experiment, approved by the lead and the owner before it runs.

## Review Focus

1. **A GPU whose non-fan tuning (clocks, VRAM, power) the user changes while the app runs.** `ResetToFactory` would discard those changes at exit. Expected: the change is documented, and the planner never factory-resets a GPU that was not at factory before the app's first write (Task 2 tests).
2. **A dirty journal from rc.1/rc.2 (no factory field) after an upgrade.** Expected: no factory reset on a guess. The points are re-applied, the probe and log say "pre-app mode unknown", and the user is told how to reset (Task 2, Task 5 tests).
3. **A driver whose ADLX lacks `IsAtFactory`/`ResetToFactory`, or returns an error** (older Adrenalin; the RDNA1/2 bound package). Expected: today's behaviour (re-apply points) plus an explicit "release incomplete" record, never a silent success (Task 3 tests).
4. **A crash between `ResetToFactory` and the journal's `MarkClean`.** Expected: the next restore sees a dirty entry with `preAppAtFactory=true` and factory-resets again. That is idempotent (Task 2 test).
5. **Multi-GPU.** `ResetToFactory` is per GPU. Expected: only GPUs the app wrote are reset, each from its own journal entry (Task 3 test).

---

## 1. Problem (evidence)

On an RX 5700 XT test card running rc.2:
- **Before the app ever engaged:** it idled at 52 °C with the fan stopped (0 rpm and 0 % on PMLog and D3DKMT). That is the board's fan stop under the driver's automatic control.
- **The event:** rc.2 engaged ADLX manual fan tuning briefly, was force-killed, and `--restore-auto` then restored from the journal (exit 0).
- **After:** the card idles at 33 °C with the fan at 22 % (about 715 rpm). That is what the captured curve `15,32,49,66,100` gives under manual control. The fan stop is gone.

Code trace at `0cf52803`:
- **Journal restore:** `RestoreAutoRunner` → `FanController.TryRestoreAutomatic` → `ApplyBaseline` (FanController.cs:166-186). The journal branch calls `SetZeroRPM` (only when supported) and then `SetFanTuningStates(journaled speeds)`.
- **Graceful exit:** `StopSignal` → `Exit()` → `FanControlService.Stop()` → backend `Dispose` → `TryRestoreAutomatic` → `ApplyBaseline`. Its non-journal branch calls `ManualFanTuning.Reset()`.
- **The wrapper's `Reset()`** (external/ADLX `Samples/csharp/ADLXWrapper/ManualFanTuning.cs:77-83) is `SetZeroRPM(captured)` (only when supported) plus `SetFanTuningStates(_resetList)`. `_resetList` is the curve read when the wrapper was constructed (:26-51).
- **Net effect: no path in v1.0 or v1.1 releases a GPU back to the driver's automatic fan control.** Every path re-applies captured points as a manual curve.
- **Where it shows:** where ADLX supports zero-RPM (most RDNA2/3/4 cards), restoring the zero-RPM flag hides this at idle, but the GPU stays under manual tuning. Where it doesn't (this RDNA1 card), the idle fan stop is lost.
- **Why the probe misses it:** `speeds=` shows the same points in both modes.

## 2. What ADLX offers (primary sources)

- **`IADLXGPUTuningServices::IsAtFactory(pGPU, isFactory)`:** "Checks if the GPU tuning on a GPU is set to factory settings." Source: `external/ADLX/SDK/Include/IGPUTuning.h:835-855`. It is read-only and per GPU.
- **`IADLXGPUTuningServices::ResetToFactory(pGPU)`:** "resets settings of Auto Tuning, Manual GPU Tuning, Manual Fan Tuning, Manual VRAM Tuning and Manual Power Tuning". Source: `IGPUTuning.h:858-880`.
  - It is **not fan-only**.
  - AMD's own sample uses the pair as "check/restore GPU tuning to factory default" (`Samples/CPP/GPUTuning/GPUAutoTuning/mainGPUAutoTuning.cpp:34-38, 177+`).
- **`IADLXManualFanTuning1`** adds `GetDefaultFanTuningStates` and `GetDefaultZeroRPMState` (`IGPUManualFanTuning.h:823-957`). These are default values, not a release; setting them still leaves manual tuning on. No fan-only release exists in ADLX 1.4.
- **Not yet verified:** the online docs, persistence across a reboot or driver restart, version support on older drivers, and any known issues. These come from the docs research (§2a, filled in from the researcher's cited answers before approval).
- **The wrapper can't reach either call today.** The SWIG binding exposes both (`Bindings/IADLXGPUTuningServices.cs:61-67`), but the wrapper's `NativeInterface` is `internal` (`UnmanagedWrapper.cs:9`), so the app cannot call them.

### 2a. Docs research (cited)

AMD's own pages (gpuopen.com ADLX manual), cited:
- **ResetToFactory:** "Reset the GPU tuning to factory settings." Its remarks list Auto, Manual GPU, Manual Fan, Manual VRAM and Manual Power tuning (<https://gpuopen.com/manuals/adlx/adlx-sdk-references/adlx-interfaces/gpu-tuning/iadlxgputuningservices/resettofactory/>).
  - Preset tuning is not listed.
  - Whether it also touches AMD Software profiles, or states the control mode, is not documented.
- **IsAtFactory:** "Checks if the GPU tuning on a GPU is set to factory settings", with no remarks (<…/iadlxgputuningservices/isatfactory/>).
  - **Whether it reads false when ONLY the fan curve is manual is not documented.** That is the design's key unknown; see H0 and D5.
- **SetFanTuningStates:** its page notes that with one-click auto tuning enabled the call returns `ADLX_RESET_NEEDED`, and `ResetToFactory` is needed (<…/iadlxmanualfantuning/setfantuningstates/>). rc.3 must handle that result explicitly: log it, never retry-loop.
- **No fan-only reset and no fan-mode flag** in `IADLXManualFanTuning`, `IADLXManualFanTuning1` (only `GetDefault*` getters), `IADLXGPUAutoTuning`, `IADLXGPUPresetTuning`, or `IADLXGPUTuningServices1` (it adds only `GetSmartAccessMemory`).
- **AMD's ManualFanTuning samples** (C and C++) don't restore fan state on exit. The GPUAutoTuning sample calls `ResetToFactory` on its restore option and on `ADLX_RESET_NEEDED`.
- **Persistence** across exit, driver restart or reboot, and where the state is stored: **not documented.**
- **Versions:** both methods are "Minimum version 1.0" of `IADLXGPUTuningServices`. No known issues are documented for RDNA1/Navi 10 or older Adrenalin drivers; a search of the GitHub issues found none.

## 3. Design

### 3.1 The release planner (Core, pure)

`LightweightAmdGpuFanControl.Core/Recovery/ReleasePlanner.cs`:

```csharp
public enum ReleaseAction { FactoryReset, ReapplyPoints }
public sealed class ReleasePlan { public ReleaseAction Action; public bool ModeUnknown; public string Why; }
public static class ReleasePlanner { public static ReleasePlan Plan(bool? preAppAtFactory); }
```

| `preAppAtFactory` | Action | Notes |
|---|---|---|
| `true` | `FactoryReset` | The pre-app GPU was entirely at factory, so a factory reset is the exact pre-app state. |
| `false` | `ReapplyPoints` | The user had their own tuning; the points restore their curve, which is what they had. |
| `null` | `ReapplyPoints`, `ModeUnknown = true` | Legacy rc.1/rc.2 entry, or `IsAtFactory` unsupported or failed at capture. |

### 3.2 The journal

`BaselineEntry` gains `bool? PreAppAtFactory` (camelCase `preAppAtFactory`), **inside schema 1**:
- Old journals parse with `null`.
- rc.1/rc.2 ignore the unknown field (System.Text.Json default), so a downgrade still restores points.
- `MarkDirty` records it once, from the capture before the first write. A recovered dirty entry keeps its ORIGINAL value. That is the crash case: on the next start `IsAtFactory` reads false because the fan is still manual, but the entry still says `true`.
- **`BaselinePlanner.Choose` must take the mode into account, not just the points.** Today a dirty entry whose points equal the current state counts as `AlreadyRestored` (BaselineJournal.cs:187) and is dropped. That is exactly the state a points-only restore leaves behind on a factory-mode GPU.
  - In rc.3, an entry counts as already restored only if its points match AND (`PreAppAtFactory != true` OR `IsAtFactory` reads true now).
  - Otherwise it stays a `Journal` choice, its `PreAppAtFactory=true` is carried forward, and the release is `FactoryReset`.
  - This also repairs any card left in that state by rc.1/rc.2 whose entry was written by rc.3. Legacy entries have no field, so they are covered by D3.

### 3.3 FanController (app)

- `CaptureBaseline` calls `IsAtFactory(gpu)` alongside `GetCurrentState()`, before any write. Any ADLX error gives `null`.
- `ApplyBaseline` is replaced by `Release(ReleasePlan plan, FanBaseline points)`:
  - **`FactoryReset`:** `ResetToFactory(gpu)`, then `IsAtFactory(gpu)` must read true. If the reset throws or doesn't confirm, fall back to `ReapplyPoints`, log `[fan] release incomplete: …`, and return false, so the entry stays dirty and a later restore retries.
  - **`ReapplyPoints`:** today's code (`SetZeroRPM` when supported, then `SetFanTuningStates`).
- `TryRestoreAutomatic` marks the journal clean only after an exact release, as today. `FactoryReset` confirmed by `IsAtFactory` counts as exact.
- `ManualFanTuning.Reset()` is no longer called anywhere. A source guard enforces this.

### 3.4 Reaching ResetToFactory / IsAtFactory

| Option | What it takes | Verdict |
|---|---|---|
| **A. A bitworks-io fork of the wrapper submodule** | About 20 lines in `GPUTuningService.cs`: `bool IsAtFactory(GPU)` and `void ResetToFactory(GPU)`, the same pattern as `IsManualFanTuningSupported`. The submodule moves to the fork at a pinned commit. | **Recommended**: smallest, mirrors existing code, reviewable. Cost: we maintain a fork, and the submodule URL change is a supply-chain change to review. |
| **B. App-side SWIG access** | Re-enumerate GPUs through the bindings to get `IADLXGPUTuningServices`/`IADLXGPU` handles outside the wrapper. | Duplicates lifetimes and reference counting; more risk. |
| **C. Reflection on `internal NativeInterface`** | Read the internal field at runtime. | Rejected: brittle, defeats review. |

### 3.5 The probe

Per GPU it prints `tuningAtFactory=yes|no|unsupported|error`, and per journal entry `preAppAtFactory=true|false|unknown`. `ProbeReport` (Core) formats both, and a test pins the format. This is the auto-versus-manual indicator: `speeds=` alone can't show it.

### 3.6 What `ResetToFactory` touches, and why that is acceptable

- **What it touches:** auto, manual GPU, fan, VRAM and power tuning.
- **When the plan uses it:** only when the whole GPU was at factory before the app's first write. The only thing a factory reset can then discard is a non-fan tuning change the user makes in AMD Software while the app is running.
- **On the test benches:** we never set clock, VRAM or power tuning, so the reset returns exactly the pre-app state.
- **For users:** document it (README and CHANGELOG): "on exit, if your GPU was at factory settings when the app started, its tuning is reset to factory; tuning changes made while the app runs are reset too."
- Open decision D2 covers this.

### 3.7 The fleet implication

- **The behaviour:** v1.0 and every 1.1 rc leave a GPU under manual fan tuning after exit, with the captured points. On RDNA2/3/4 cards whose ADLX zero-RPM is supported, the idle fan stop comes back with the zero-RPM flag, so the cards look normal. They are still not on driver auto.
- **Where the state lives:** this is per-adapter driver tuning state, set through ADLX. The app has no firmware or VBIOS write path, and nothing that ships with the card changes.
- **How to undo it:** AMD Software's tuning reset (or a driver factory reset) clears it.
- **Not yet verified:** whether it survives a reboot (§2a).
- **What users are told:** the 1.1.0 release notes carry a "known issue in 1.0.x and rc.1/rc.2" entry with the one-line manual fix.

## 4. Acceptance criteria

1. On a GPU that was at factory before the app ran, every exit path ends with `IsAtFactory == true`. The paths are graceful exit, `--stop`, and `--restore-auto` after a forced kill.
2. A pre-app manual curve set by the user is restored as manual, with the same points.
3. A legacy (null) entry is restored as points, with a "pre-app mode unknown" log line, a probe line and a user-facing hint.
4. The probe distinguishes `tuningAtFactory=yes` from `no`.
5. A `ResetToFactory` failure never reports success, and leaves the entry dirty.
6. No call to `ManualFanTuning.Reset()` remains (source guard).

## 5. Red tests (written first, in `LightweightAmdGpuFanControl.Tests`)

- `ReleasePlannerTests.PreAppAtFactory_PlansFactoryReset`
- `ReleasePlannerTests.UserManualCurve_PlansReapplyPoints_SamePoints`
- `ReleasePlannerTests.LegacyEntry_PlansReapplyPoints_ModeUnknown`
- `BaselineJournalTests.PreAppAtFactory_RoundTrips_AndLegacyParsesNull`
- `BaselineJournalTests.RecoveredDirtyEntry_KeepsOriginalPreAppAtFactory`: the crash-then-restore incident path. Engage with a factory pre-app state, force-kill (the entry stays dirty), restart with `IsAtFactory=false`. The plan must be `FactoryReset`.
- `BaselineJournalTests.PointsEqualButNotAtFactory_IsNotAlreadyRestored`: the bench state after rc.2's restore. The points equal the journal's, `PreAppAtFactory=true`, and `IsAtFactory` reads false now. The choice must be `Journal` with `FactoryReset`, not `AlreadyRestored`.
- `ProbeReportTests.PrintsTuningAtFactory_AndPreAppMode`
- `FanReleaseSourceGuardTests`, over FanController.cs and RestoreAutoRunner.cs:
  - no `ManualFanTuning.Reset()`;
  - the release goes through `ReleasePlanner.Plan`;
  - `ResetToFactory` is always followed by an `IsAtFactory` confirmation;
  - the journal is marked clean only on an exact release.

## 6. Hardware validation (declared experiments, each approved first)

**H0 comes first, before the design is locked (read-only; needs only Tasks 1 and 5 built as an rc.3 probe):** `IsAtFactory` on the test card. Run it in its current state (a manual curve pinned by rc.2's restore; expected `no`) and again after the owner's AMD Software reset (expected `yes`).
- If `IsAtFactory` reads `yes` while the fan curve is manual, it can't serve as the mode indicator, and the design goes back to the lead (D5).
- This could ship as rc.3 with the probe alone, and the release logic as rc.4. Or rc.3 waits for H0 on a CI test build. That is the lead's call (D5).

The remaining steps run against the test card after the owner's AMD Software reset has brought it back to factory:

- **H1 (read-only):** the rc.3 probe shows `tuningAtFactory=yes`, and the idle fan stop is seen on PMLog and D3DKMT.
- **H2 (write):** an rc.3 start at idle, then `engaged`, then a graceful `--stop`. Afterwards: `tuningAtFactory=yes`, and the idle fan stop returns within 3 min.
  - H2 also observes the state right after the start-up self-test (the self-test's release can factory-reset), and across Pause and Resume, including whether the `ManualFanTuning` interfaces still work for writes after a mid-run `ResetToFactory`.
- **H3 (write; the incident path):** engage, then force-kill, then `--restore-auto` (exit 0). Afterwards: `tuningAtFactory=yes`, and the fan stop returns.
- **Then the regulation test under load**, with the lead's timing change.

## 7. Tasks

### Task 1: Wrapper access (option A)

**Files:**
- Modify: `external/ADLX` (the submodule moves to the bitworks-io fork) and `.gitmodules`.
- Fork: `Samples/csharp/ADLXWrapper/GPUTuningService.cs`.

**Interfaces:**
- Produces: `bool GPUTuningService.IsAtFactory(GPU gpu)`, which throws on an ADLX error.
- Produces: `void GPUTuningService.ResetToFactory(GPU gpu)`, which throws on an ADLX error.

- [ ] Fork Rem0o/ADLX at `240b1f3` into bitworks-io. Add both methods, using the `IsManualFanTuningSupported` pattern (`new_boolP` / `boolP_value` / `delete_boolP`, `ThrowIfError`).
- [ ] Point the submodule at the fork commit. CI builds the native bind unchanged.
- [ ] Commit: "build: wrapper exposes IsAtFactory/ResetToFactory (bitworks-io fork)".

### Task 2: ReleasePlanner and journal field (Core)

**Files:**
- Create: `LightweightAmdGpuFanControl.Core/Recovery/ReleasePlanner.cs`.
- Modify: `LightweightAmdGpuFanControl.Core/Recovery/BaselineJournal.cs` (`BaselineEntry.PreAppAtFactory`; `MarkDirty(id, b, preAppAtFactory, utc)`; `FanBaseline` carries it).
- Test: `ReleasePlannerTests.cs`, `BaselineJournalTests.cs`.

**Interfaces:** as in §3.1. `BaselineChoice.Baseline.PreAppAtFactory` is the journaled value for `Journal` choices and the captured value otherwise.

- [ ] Write the §5 planner and journal tests; run them and see them fail.
- [ ] Implement; run them and see them pass. Run the full suite (176 tests at rc.2, plus the new ones).
- [ ] Commit.

### Task 3: FanController capture and release

**Files:**
- Modify: `LightweightAmdGpuFanControl/Adlx/FanController.cs`.
- Test: `FanReleaseSourceGuardTests.cs`.

- [ ] Write the source guard; run it and see it fail (`Reset()` is still present).
- [ ] Add `IsAtFactory` to `CaptureBaseline`. Replace `ApplyBaseline` with `Release(plan, points)` as in §3.3. Log `[fan] release action=… confirmed=…`.
- [ ] Run the guard and the suite and see them pass. Commit.

### Task 4: RestoreAutoRunner

- [ ] Route it through the same planner (its log says `restore-auto: release action=…`). Exit codes are unchanged: 0 restored, 3 nothing to do, 4 running, 1 error. A legacy unknown-mode restore exits 0 and logs the hint.
- [ ] Extend the guard. Commit.

### Task 5: Probe

- [ ] `ProbeReportTests` for the new lines, then the implementation in `CapabilityProbe` (`IsAtFactory` is read-only).
- [ ] Commit.

### Task 5b: `--reset-tuning-to-factory --gpu <uniqueId>` (amendment, approved by the lead 2026-11-08)

Purpose: return one named GPU's tuning to factory on demand. It covers GPUs left under manual tuning by v1.0, rc.1 or rc.2, whose journal entries (legacy, `preAppAtFactory` null) rc.3's own restore treats as points plus a hint (D3).

**Files:**
- Modify: `Core/Cli/LaunchOptions.cs` (new `LaunchMode.ResetTuningToFactory`, plus `--gpu <int>`, required by it and rejected otherwise).
- Create: `Core/Cli/FactoryResetOutcome.cs` (the pure mapping to an exit code).
- Create: `Services/FactoryResetCommand.cs` (app).
- Modify: `Program.cs` (dispatch).
- Modify: the help/usage text and README.
- Tests: `LaunchOptionsTests`, `FactoryResetOutcomeTests`, `FactoryResetSourceGuardTests`.

**Behaviour:**
- **Explicit GPU only.** There is no all-GPUs form.
- **It refuses with:**
  - exit 4 if an instance holds the single-instance lock (the same `SingleInstance.TryAcquire` wait as `--restore-auto`);
  - exit 1 if the GPU with that `uniqueId` isn't found, or `IsSupportedManualFanTuning` is false.
- **ADLX setup** is the same bound-DLL pre-load and ADLX init path as `--restore-auto` and `--probe`.
- **Order:**
  1. Log `IsAtFactory` before. If it is true, exit 3 (already at factory) and write nothing.
  2. Call `ResetToFactory`.
  3. Log `IsAtFactory` after. True gives exit 0; false or an exception gives exit 1.
- **Journal:** only on exit 0, that GPU's dirty LEGACY entry (`preAppAtFactory` null) is marked clean. No other journal change.
- **Help text and README say plainly:** "resets ALL of this GPU's tuning (clock, VRAM, power and fan) to factory settings, not only the fan" (D2).
- **The probe afterwards** reads `tuningAtFactory=yes`.

**Tests:**
- Parsing: `--gpu` is required, and must be an int; the mode is exclusive with every other mode.
- The outcome mapping: before=true → 3; reset ok + after=true → 0; after=false or a throw → 1; lock held → 4.
- Source guard:
  - `ResetToFactory` is preceded by an `IsAtFactory` read and followed by an `IsAtFactory` confirmation;
  - the lock comes before any ADLX call;
  - the only GPU touched is the one matched by `uniqueId`;
  - `MarkClean` runs only after a confirmed reset.

### Task 5c: D6, fail closed on fan-write errors (lead ruling 2026-11-08: option (a), no fork widening)

The pinned SDK (ADLX 1.4) has no `ADLX_RESET_NEEDED` value, and the wrapper reports errors only as message text. So every `SetFanTuningStates` error is treated alike:

- **At start or during the self-test:** refuse that GPU and write nothing more to it. Report it in `status.json` (a per-GPU issue carrying the wrapper's message text) and in the probe (where the probe can observe it).
- **Mid-regulation:**
  - stop regulating that GPU;
  - release it once through `ReleasePlanner` (factory reset confirmed by `IsAtFactory` when `preAppAtFactory=true`, otherwise the points);
  - mark it `degraded` in status;
  - **never retry writes in a loop**. The GPU stays out of regulation for the rest of the run.

Other GPUs are unaffected.

**Tests:**
- Core, a pure per-GPU state machine: `Regulating` → error → `Degraded` (release once); `Starting` or self-test → error → `Refused` (no release write beyond what the planner says for a GPU that was never written); no transition back to `Regulating`; no second release.
- A source guard: the regulation loop checks the per-GPU state before every write, and the error handler calls the release exactly once.

### Task 5d: GpuEnablement stale entries (amendment, approved by the lead 2026-11-08)

A defect present since v1.0.0. `GpuEnablement.Reconcile` counted every saved entry, including GPUs that are no longer detected, when deciding whether another GPU was already enabled. After a card swap, a stale enabled entry for the removed card made the newly detected primary card come up disabled, and the stale entry then dropped out, so the new card stayed disabled with no visible reason.

- **Fix:** only saved entries whose id is among the detected ids count. Saved preferences are still respected exactly, and newly seen non-primary GPUs are still opt-in.
- **Tests (`GpuEnablementTests`):**
  - a removed card's enabled entry no longer blocks the new primary;
  - a saved disabled choice for a detected card is kept;
  - an enabled detected card still keeps a new second card opt-in;
  - two new cards replacing a removed one enable only the primary.

### Task 6: Docs

- [ ] README "how restore works"; CHANGELOG rc.3 entry with the 1.0.x known issue and the manual fix; `docs/agent-handoff.md`.
- [ ] Commit.

### Task 7: Release

- [ ] PR (ask before pushing). Merge on review. Tag `v1.1.0-rc.3` only via CI, with the lead's and owner's OK.
- [ ] Verify the signed release (sha, Authenticode).
- [ ] Then H1–H3, then the regulation test under load.

## 8. Open decisions for the lead

- **D1, wrapper access:** option A (our fork) is recommended.
- **D2, `ResetToFactory` scope:** accept and document (§3.6)? The alternative is to factory-reset only when the user hasn't touched AMD Software during the run, which ADLX can't observe.
- **D3, legacy entries (`null`):** re-apply points plus the "unknown" hint (recommended), or factory-reset them too, which risks discarding a user's own tuning.
- **D4, release-incomplete fallback:** re-apply points and leave the entry dirty (recommended), or leave the GPU as it is.
- **D5, sequencing around the key unknown:** does `IsAtFactory` see a manual fan curve?
  - (a) Ship the wrapper and probe first (rc.3), run H0, then the release logic (rc.4).
  - (b) Build everything and run H0 on a CI test build before tagging rc.3. **Recommended:** one release, provided test builds may run on the bench as a declared step.
  - (c) Build everything now and accept the risk. Not recommended.
- **D6, `ADLX_RESET_NEEDED` from `SetFanTuningStates`** (one-click auto tuning active): refuse to regulate that GPU and report it (recommended), or factory-reset it at start, which discards the user's auto tuning.

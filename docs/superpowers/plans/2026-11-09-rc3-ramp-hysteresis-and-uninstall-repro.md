# rc.3 follow-ups: ramp hysteresis (fan dither) and the silent-uninstall repro — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** (A) stop the fan from hunting ±3 % every poll at a steady load temperature; (B) reproduce, in CI, the silent uninstall that exited 1 on a test bench, and find its cause, before any rc.3 tag.

**Architecture:** (A) is a pure-policy change in `FanControlPolicy` (Core) plus a monotonic timestamp passed in by `FanControlService`. (B) is a new CI job that installs and uninstalls in a non-elevated per-user context; it does not touch product code until a cause is known.

**Tech Stack:** C# (.NET Framework 4.8 app, netstandard Core), xUnit, Inno Setup 6.7.1, GitHub Actions `windows-latest`.

**Spec:** findings from the RDNA1 hardware run of the rc.3 test build (PR #18 head `9d2ff04`, code-identical to `7575d50`): the regulation log and PMLog/D3DKMT streams, and the cleanup's uninstall record. They are summarised under each part.

## Global Constraints

- Fan safety: increases are never delayed; the over-temp emergency and its latch are unchanged; MaxFanPercent 85 stays the ceiling.
- No change to the release, restore or journal paths (`ReleasePlanner`, `--restore-auto`, `--stop`).
- Public repo: no bench names, serials or internal host names in code, commits or PR text.
- Every behaviour change gets a failing test first.

## Review Focus

1. A sensor that wobbles 1 °C around a ramp step must hold the higher duty (the observed case).
2. A real temperature rise must raise the duty on the very next poll, even right after a decrease.
3. A real cool-down must still lower the duty within hysteresis + dwell (no stuck-high fan).
4. Contention mode (6 s polls) and the 24-poll re-assert must not bypass the dwell or re-trigger a decrease.
5. The first poll after start, a resume from pause, or a re-enabled GPU (PreviousFanPercent unset or reset) must take the plain ramp value.

---

## Part A: ramp hysteresis

### Root cause (from code and the hardware run)

- `FanControlPolicy.Decide`, at t ≥ TargetTempC, returns `Min + round((t − Target) × (Max − Min) / 25)`. With the defaults (65 / 20 / 85) that is 2.6 % per °C, so 77 °C → 51 and 78 °C → 54.
- The hysteresis (`HysteresisC`, default 3) exists only in the dead band *below* the target. In the ramp region a 1 °C wobble changes the duty on every 2.5 s poll.
- The hardware log shows exactly that: edge temperature alternating 77/78 °C, the app writing 51↔54 % every 2.5–12 s, for the whole regulation window.
- `releasedForDecrease`, the release-then-reapply on each decrease in `FanController.SetFanPercent`, is not the cause. PMLog's reported fan % is about 12 points above the commanded % already after the first increase (56 → 68–70), before any decrease, and the decreases show no RPM spike. It only turns each dither step into a release-plus-write.

### Task A1: hysteretic decreases in the ramp region

**Files:**
- Modify `LightweightAmdGpuFanControl.Core/Control/PolicyTypes.cs`:
  - `TempReading(double? TempC, long MonotonicMs)`.
  - `PolicyState` gains `long LastChangeMs` and `bool HasPrevious`.
- Modify `LightweightAmdGpuFanControl.Core/Control/FanControlPolicy.cs`: the ramp branch only.
- Test: `LightweightAmdGpuFanControl.Tests/FanControlPolicyTests.cs`.

**Interfaces:**
- Produces `public const int MinDecreaseDwellMs = 10_000;` and `static int Ramp(double t, AppSettings s)` (the existing formula, extracted).
- Consumes the existing `HysteresisC`.

**Rule.** It applies only when t ≥ TargetTempC and there is no emergency and no manual mode; everything else is unchanged:
- `up = Ramp(t)`. If `!HasPrevious` or `up ≥ Previous`, take `up`: increases are immediate and the first decision is plain.
- Otherwise take `down = Ramp(t + HysteresisC)`. Lower the duty to `down` only if `down < Previous` and `MonotonicMs − LastChangeMs ≥ MinDecreaseDwellMs`. Otherwise hold `Previous`.
- In every branch (dead band, minimum, emergency, manual, ramp), a returned percent sets `HasPrevious = true`, and any change of the returned percent sets `LastChangeMs`. `Math.Round`'s banker's rounding never meets a .5 here: integer temperatures × 2.6 give .0/.2/.4/.6/.8.

- [ ] **Step 1: Write the failing tests.** Defaults are T 65, min 20, max 85, H 3; polls every 2,500 ms.
  - `Ramp_OneDegreeWobble_HoldsTheHigherDuty`: 77, 78, 77, 78, 77 → 51, 54, 54, 54, 54.
  - `Ramp_RealCooling_DecreasesAfterHysteresisAndDwell`: 78 at 0 ms (54), then 74 at 2,500 ms gives 54 (dwell), 74 at 12,500 ms gives 51 (Ramp(77)), and 70 at 25,000 ms gives 41 (Ramp(73) = 20 + round(20.8)).
  - `Ramp_Increase_IsImmediate_EvenInsideTheDwell`: 78 → 54, then 74 at +2,500 ms holds 54, then 80 at +3,000 ms gives 59.
  - `Ramp_FirstDecision_IsThePlainRamp`: a fresh state at 77 → 51.
  - `Emergency_LatchAndRelease_Unchanged`, and the existing dead-band tests, unchanged.
- [ ] **Step 2:** run `dotnet test LightweightAmdGpuFanControl.Tests --filter FanControlPolicyTests`. Expected: the new tests FAIL; `TempReading` has no `MonotonicMs` yet.
- [ ] **Step 3:** implement the rule. `Ramp` is the existing formula, clamped.
- [ ] **Step 4:** run the same command. Expected: PASS, and the whole test project green.
- [ ] **Step 5:** commit: `fix: hysteretic fan decreases in the ramp region (no ±3 % hunt on a 1 °C sensor wobble)`.

### Task A2: monotonic time from the service

There is deliberately no log of held decreases: a held decrease writes nothing, and a per-poll log line would spam.

**Files:**
- Modify `LightweightAmdGpuFanControl/Services/FanControlService.cs` (`AdjustChannel`): pass `Stopwatch.GetTimestamp()`-based milliseconds into `TempReading`. Do not use `DateTime.Now`, because a clock step would break the dwell. Wherever `LastAppliedPercent` is reset to -1 (pause, disable, relinquish, write error), also set `channel.State.HasPrevious = false`, so a resumed GPU starts plain.
- Test: a source-guard test in `LightweightAmdGpuFanControl.Tests` (the existing `SourceText` pattern). It asserts that the `TempReading(` construction in `FanControlService.cs` passes a Stopwatch-derived value, and that every `LastAppliedPercent = -1` site is followed by `HasPrevious = false`.

- [ ] **Step 1:** write the guard test (FAIL). **Step 2:** implement. **Step 3:** PASS, whole suite green. **Step 4:** commit.

### Proof on hardware (a bench step declared separately, not part of this plan's code)

The next RDNA1 hardware load test on a PR build containing A1–A2 must show the following over a steady-temperature regulation window:
- at most one decrease per 30 s;
- no 51↔54 alternation in the app log;
- increases following temperature rises on the next poll.

---

## Part B: silent-uninstall repro in CI

### What is known

- On the bench, `unins000.exe /VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG=…` from the rc.3 test build exited 1 after about 2 minutes.
  - It ran in a non-elevated scheduled task in the interactive session.
  - It wrote no log, made no `is-*-uninstall.tmp` copy in TEMP, and ran no [UninstallRun] entry.
- The install history since the last clean uninstall was v1.0.0 → v1.0.1 → test build → the same test build (all over-installs).
- An uninstall of signed rc.2 after a single over-install had worked (exit 0, 2 s).
- Inno is 6.7.1 in both builds, and `installer/` is unchanged since rc.2.
- Signing does not explain it: CI signs the installer after compiling, so every build's embedded uninstaller is unsigned. The signed v1.0.1 installs an unsigned `unins000.exe` too.
- v1.0.x's `.iss` has no `[UninstallRun]`, and its `Main()` takes no arguments. An uninstall log that carries rc.3's `[UninstallRun]` entries under a 1.0.x binary would therefore start the tray app during uninstall and hang (a known residual).

### Task B1: `uninstall-repro` job

**Files:**
- Create `.github/workflows/uninstall-repro.yml`, triggered by `workflow_dispatch` and by `pull_request` on `installer/**` and on the workflow file. It has job `repro` on `windows-latest`.
- Create `tools/uninstall-repro/Run-Scenario.ps1`, the per-scenario driver, run non-elevated.

**Design:**
- Download the PR build's `LightweightAmdGpuFanControl-Setup` artifact; when it's run by dispatch, take a run id input.
- Download v1.0.0 and v1.0.1 by tag, and check each against its published SHA-256 (v1.0.0 `66324233…bfcb`, v1.0.1 `8bebfa41…5888`).
- Create a local standard user with a random password that is never echoed. Run every install and uninstall as a scheduled task under that user (`-LogonType Password -RunLevel Limited`), so the per-user install lands in that user's `%LOCALAPPDATA%`, as on a bench.
- For each step, record:
  - the exit code and wall time;
  - the Inno log (`/LOG=`);
  - the `unins000.dat` size;
  - presence of the app folder and data folder;
  - new `is-*` and `_iu*` items in TEMP;
  - a 1 s poll during the uninstall of child processes, plus top-level window titles and text via `EnumWindows`, to catch a suppressed or hidden dialog.
- Each scenario has a 300 s cap. A hung uninstaller is reported, and killed only at job teardown.
- Everything is uploaded as one artifact.

**Scenarios** (each in a fresh user profile):

| # | History before the uninstall | Expectation |
|---|---|---|
| S1 | test build | exit 0, removed (control) |
| S2 | test build → test build | discriminates the same-version over-install |
| S3 | v1.0.0 → v1.0.1 → test build → test build | the bench's history: the experiment |
| S4 | v1.0.0 → v1.0.1 | the release-only control |
| S5 | test build → v1.0.1 (the cleanup's over-install) | shows the residual: whether a later uninstall starts 1.0.1 and hangs |

- [ ] **Step 1:** write the workflow and the driver. Assert exit 0 and removal for S1 and S4. Report S2, S3 and S5 without asserting (they're the experiment).
- [ ] **Step 2:** run it on the PR, and read the artifact.
- [ ] **Step 3: decide from the evidence.**
  - If S3 or S2 reproduces, the Inno log and window text name the cause. Write a fix plan (e.g. `UninstallLogMode`, `RunOnceId` handling, or an `InitializeUninstall` guard) before any code, with S2/S3 turned into asserting regressions.
  - If nothing reproduces, the cause is environment-specific to the bench (the interactive session, AV, or the profile). Report that, and propose a declared read-mostly bench diagnostic instead.
- [ ] **Step 4:** commit: `ci: uninstall repro across over-install histories (no product change)`.

---

## Order and gates

1. A1 → A2 (commits on the rc.3 branch).
2. B1 in parallel, since it touches different files.
3. A new PR test build carrying A1–A2 is what the next RDNA1 load test runs on.
4. The rc.3 pre-release tag needs: the RDNA1 release-to-driver-auto hardware check passed (done), the A1–A2 fix, a passing RDNA1 load test, and B resolved, or explicitly accepted with evidence.
5. Nothing is pushed or opened as a PR without the user's approval.

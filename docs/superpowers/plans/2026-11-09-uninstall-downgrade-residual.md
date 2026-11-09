# Uninstall after a downgrade hangs: fix plan (for review; no code yet)

**Goal:** an uninstall must never hang, and never leave a fan under app control, after any install history that includes a 1.1 build. That includes histories where an older 1.0.x was installed over it later (a "downgrade"). Existing machines already in that state must be made safe too, not only future installs.

**Status:** plan only, for review. No code until it's approved.

## 1. What happens (evidence)

- The CI uninstall repro reproduces it: scenario S5, test build → v1.0.1 over-install → silent uninstall.
  - The uninstaller runs the 1.1 build's recorded `[UninstallRun]` entry `LightweightAmdGpuFanControl.exe --stop` against the installed **v1.0.1** binary.
  - v1.0.1 ignores the argument and starts the tray app. On a machine with no supported GPU it shows the modal "AMD fan control unavailable".
  - `waituntilterminated` then blocks the uninstall indefinitely.
- **Why it happens:**
  - **Inno Setup appends uninstall logs** across over-installs. The 1.1 build's three `[UninstallRun]` entries (`--stop`, then `taskkill`, then `--restore-auto`, each with a RunOnceId) stay in `unins000.dat` after v1.0.1 is installed over it.
  - **The uninstaller binary and its `[Code]` come from the LAST installer** (v1.0.1). So nothing in the 1.1 installer's `[Code]` can guard this. Only the recorded entries, and the files they execute, carry over.
  - **v1.0.1's `Main()` takes no arguments** (`Program.cs:11`). Every launch is a tray launch:
    - a second launch shows an "Already running" modal (`:14-19`);
    - with no backend, it shows the "unavailable" modal and keeps running (`FanControlService.cs:79-83`).
    - **On a machine WITH a supported GPU,** the same launch would start regulating the fan while the uninstaller waits on it.
  - **v1.0.1 restores the driver's automatic fan control on a normal exit** (`SystrayApplicationContext.cs:248-256`, plus ProcessExit and the exception handlers). It has **no journal**, so a hard kill (`taskkill /F`) leaves the fan on the app's last curve until the next launch or a driver reset.
- **The installers themselves are fine during an over-install.** In both directions, they only `taskkill` a running instance in `PrepareToInstall`. The residual is at uninstall only.

## 2. Options considered

| Option | Fixes future 1.1 → 1.0.x downgrades | Fixes machines already in the state | Cost and risk |
|---|---|---|---|
| A. Guard in the 1.1 uninstaller's `[Code]` (`InitializeUninstall`) | **No**: the last installer's uninstaller runs, not 1.1's | No | — |
| B. `[UninstallRun]` `Check:` | No: it's evaluated when the entry is recorded (install time), not at uninstall | No | — |
| C. A separate helper exe that runs `--stop` / `--restore-auto` only if the main exe is ≥ 1.1 | Yes, while the helper file survives the downgrade's over-install (Inno doesn't delete unknown files) | **No**: it's not on existing machines | A new binary, signing and tests; doesn't help today's machines |
| **D. A v1.0.2 hotfix with safe argument handling** | Yes, for downgrades to ≥ 1.0.2 | **Yes**, through the normal over-install of the latest release | A small change on the 1.0 line; a signed release |
| E. Do nothing, keep the "over-install only" rule | No | No | Leaves a hang, and on machines with a supported GPU an app left regulating during uninstall |

## 3. Recommendation: D (v1.0.2), plus two 1.1 hardening items

### D. v1.0.2 = v1.0.1 plus safe argument handling (branch `release/1.0.x` from tag `v1.0.1`)

- `Main(string[] args)`:
  - **`--stop`:** a graceful stop, as in 1.1.
    - Backport `StopSignal` (the named event `Local\LightweightAmdGpuFanControl.Stop`, which the tray listens for and answers with its normal `Exit()`, which restores the fans) and `StopCommand`.
    - Exit codes: 0 stopped; 3 not running; 1 timeout or error. No tray, no modal.
  - **`--restore-auto`, `--probe`, `--reset-tuning-to-factory`, or any other argument starting with `--`:** exit 3 at once. No mutex, no tray, no modal.
    - v1.0.x has no journal; its own exit path is the restore.
  - **No arguments:** exactly v1.0.1's behaviour.
- **With D, the recorded 1.1 entries run safely against 1.0.2:**
  1. `--stop` gracefully stops a running 1.0.2, which restores its fans;
  2. `taskkill` finds nothing;
  3. `--restore-auto` exits 3.
  - The uninstall completes.
- **D must NOT** turn the recorded `--stop` into a no-op while a 1.0.2 tray is running. The following `taskkill /F` would then kill it with no restore and leave the fan on its curve. That's why `--stop` has to be the real graceful stop, not "exit 3".

### H1. 1.1 `PrepareToInstall`: a graceful stop before the kill (upgrade path)

- **Today:** an over-install `taskkill`s a running instance, with no restore. The new app then captures its pre-app baseline from a GPU that is NOT at factory (the killed app's curve). On release it re-applies those points instead of a factory reset, as already seen after an earlier test build's pinned curve came back.
- **Change:** if the installed exe is ≥ 1.1 (`GetVersionNumbersString` on `{app}\…exe`, which is the new installer's own `[Code]`, so it does run), run `--stop` and wait for it, THEN `taskkill` as a fallback.
- For a running 1.0.x there's no graceful path. Keep the kill, and document the factory-reset step (`--reset-tuning-to-factory`) for that rare upgrade-while-running case.

### H2. 1.1 `[UninstallRun]`: add `skipifdoesntexist`

`skipifdoesntexist` on the two exe entries, so an uninstall after the exe was removed or renamed doesn't error. It's harmless and future-proofing; it doesn't address the downgrade by itself.

## 4. Existing machines (the bench path)

- **A machine in the S5 state** (any 1.1 test build, then v1.0.1 over-installed) is unsafe to uninstall today. Keep the "over-install only, never uninstall" rule there until it's fixed.
- **Fix path:**
  1. Release v1.0.2 as a signed release (CI signing, as for 1.0.1).
  2. Over-install v1.0.2. That is what the bench automation already does with `releases/latest`; on a test bench, it's a declared step.
  3. Verify with a declared, read-only check: version 1.0.2.0 and the recorded entries unchanged.
  - **Or:** over-install a 1.1.x release. The 1.1 exe handles its own entries.
- **Until v1.0.2 is `releases/latest`,** the bench rule stands.
- Downgrades to v1.0.0 or v1.0.1 after any 1.1 build remain unsafe to uninstall. Document that in the release notes ("don't downgrade below 1.0.2").

## 5. Tests and proof

- **v1.0.2 unit tests:** `--stop` with nothing running → 3, and no mutex taken; `--stop` with a running instance → the stop event is set and 0 is returned when it exits; `--restore-auto`, `--probe` and an unknown `--x` → 3 with no tray; no arguments → the tray path, as before. Plus a source guard that no `MessageBox` is reachable before the argument dispatch.
- **CI repro**, after the harness fix lands and runs cleanly:
  - **S5 becomes asserting with v1.0.2:** test → v1.0.2 → uninstall exits 0, everything removed, no modal window seen.
  - **S5-1.0.1** (test → v1.0.1 → uninstall) stays reported only. It's the documented residual.
  - **New S6:** test → v1.0.2 with the tray running (the scenario starts it) → uninstall. The tray exits gracefully through `--stop` (log line), nothing is left running, and the uninstall exits 0.
- **H1:** an Inno `[Code]` test isn't practical. Instead, a CI scenario: 1.1 installed and running → over-install 1.1 → the log shows `stop: stopped` before any taskkill.

## 6. Open questions for review

1. Is a v1.0.2 release acceptable on the 1.0 line: a new `release/1.0.x` branch from `v1.0.1`, with signed CI release from that branch?
2. Must B be resolved before the rc.3 tag, or can rc.3 be tagged with B documented, given that D lands on the 1.0 line independently?
3. Should H1 go into rc.3, or into 1.1.1?

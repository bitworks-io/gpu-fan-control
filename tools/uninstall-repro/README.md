# Uninstall repro harness

CI harness (`.github/workflows/uninstall-repro.yml`) that installs and silently uninstalls the Windows
installer as a non-elevated throwaway standard user, across several install histories, and records what
happens. It changes no product code.

## Scenarios

| Scenario | History before the uninstall | Mode |
|---|---|---|
| S1 | test build | asserted (control) |
| S4 | v1.0.0, v1.0.1 | asserted (control) |
| S5b | test build, v1.0.2 (downgrade to the hotfix) | asserted |
| S6 | test build, v1.0.2, with the 1.0.2 tray running | asserted |
| S2 | test build, test build | reported only |
| S3 | v1.0.0, v1.0.1, test build, test build | reported only |
| S5 | test build, v1.0.1 | reported only |

Asserted scenarios fail the job on any anomaly; reported ones always exit 0 and write the outcome to
`result.json` and the step summary. For S5b and S6 the assertions cover the three recorded
`[UninstallRun]` exit codes from the Inno log, the app's `stop:` log line, the
`Unsupported arguments for 1.0.x: --restore-auto; exit 3` line, exit 0 within 120 s, the app folder gone,
and no fan-control process left. S6 also requires the tray's owner and session to match the uninstaller's.

**Order matters.** S5 hangs on purpose and leaves its own user's app process running until teardown, which
would change what the 1.1 uninstaller's `taskkill /IM` reports in a later scenario. So S5b and S6 run right
after the controls, count only the app process of their own user (by its installed exe path), and refuse to
judge (a precondition failure) if another scenario's app process is still running.

## Self-test

`Test-Harness.ps1` tests the pure functions (result building, Inno log parsing, stop-line classification,
the S5b/S6 rules) with no installs, users or tasks. It runs first in CI under Windows PowerShell 5.1 and
locally with `pwsh -NoProfile -File tools/uninstall-repro/Test-Harness.ps1`.

## Where the installers come from

- Test build: built in the job (as `build-windows.yml`, minus tests and signing). A manual dispatch with
  `build_run_id` downloads that run's installer artifact instead.
- v1.0.0 / v1.0.1: downloaded from the releases and checked against pinned SHA-256 values.
- v1.0.2: built in the job from the commit pinned in `V102_SHA` (it has no release yet).

## Data folder may be left behind in S5b and S6 (not an uninstaller bug)

For S5b and S6 the harness holds `log.txt` open (ReadWrite|Delete sharing) across the uninstall, to capture
the app's `--stop` and `--restore-auto` log lines before `[UninstallDelete]` removes them. So the data
folder `%LOCALAPPDATA%\Bitworks\LightweightAmdGpuFanControl` may be left behind in those two scenarios.
That is a harness artifact. `dataDirExists` is recorded in `result.json` (with any Inno log line about a
failed delete, as `dataDirDeleteLines`) but is not asserted.

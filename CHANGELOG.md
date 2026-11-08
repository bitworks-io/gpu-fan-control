# Changelog

All notable changes to **Lightweight AMD GPU Fan Control** are documented here.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/);
this project uses [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.1.0] — Unreleased

RDNA1 (RX 5700 / 5600 series) support, read-only diagnostics, an unattended
mode, and a journal that restores the pre-app fan state after an unclean stop.
Not validated on hardware yet.

### Fixed

- **rc.3: the fan is released to the driver's automatic control on exit.** Until rc.2 (and in 1.0.x)
  every exit path re-applied the fan curve captured at start as a manual curve, so the GPU stayed
  under manual fan tuning after the app closed. On a card whose fan stops at idle under automatic
  control, the fan stop did not come back. Before its first fan write the app now records whether
  the GPU's tuning was at factory settings, and on exit (graceful exit, `--stop`, `--restore-auto`
  after a kill) a GPU that was at factory settings is reset to factory and confirmed. A GPU that
  had its own tuning, or whose earlier mode is unknown, gets its saved fan points back. Not
  validated on hardware yet.
  - **What the factory reset covers:** ADLX `ResetToFactory` resets auto, GPU, fan, VRAM and power
    tuning together, not just the fan. The app uses it only when the whole GPU was at factory
    settings before the app's first fan write. Non-fan tuning (clocks, VRAM, power) that you change
    in AMD Software while the app runs is reset too.
  - **When a release can factory-reset a GPU.** Only for a GPU that was at factory settings before the app's
    first write to it (or whose recovered journal entry says so), and at each of these moments: the end of the
    start-up self-test (on every channel the test ran on), Pause, a GPU disabled in settings, the sensor-failure
    relinquish, a release after a fan-write error, `RestoreAll` after an unhandled exception, and exit,
    `--stop` and `--restore-auto`. Each of those resets non-fan tuning (clocks, VRAM, power) changed since the
    app's first write. After a crash, non-fan tuning you changed before the next start can be reset too, because
    that start's release factory-resets the GPU the crashed run had written to.
  - **Entries from earlier builds:** a `baseline.json` entry written by 1.0.x or rc.1/rc.2 has no
    factory flag. It is restored by re-applying its fan points, the log and `--probe` say
    "pre-app mode unknown", and nothing is factory-reset on a guess.
  - **If the release is not confirmed:** the fan points are re-applied, the log says
    `[fan] release incomplete`, `status.json` lists `release-incomplete: <GPU>` under
    `releaseIssues`, and the journal entry stays dirty so `--restore-auto` can try again.
  - **Known issue in 1.0.x and rc.1/rc.2:** after the app exits, a GPU can be left under manual fan
    tuning with the curve it had at start. To undo it, reset GPU tuning for that card in AMD Software.
- **rc.3: a GPU installed in place of a removed one could come up with fan control disabled** (the removed card's saved setting blocked it); fixed. If a card is already affected, enable it in Preferences: a saved off setting is kept as a choice.
- **rc.3: a failed fan write now fails closed.** Before, a fan-write error during regulation was
  logged and the same write was tried again every poll. Now an error at start or in the start-up
  self-test refuses that GPU (nothing more is written to it), and an error during regulation stops
  regulating that GPU, releases it right then (factory reset confirmed by IsAtFactory when it was at
  factory before the app wrote, otherwise its saved fan points), and never retries; the normal release at
  exit still runs afterwards. Other GPUs are
  not affected. `status.json` lists the GPU under `gpuIssues` as `refused` or `degraded` with the
  driver wrapper's message, and `--probe` prints the last run's issues as `last run:` lines. The ADLX
  SDK in use has no distinct "reset needed" result (for example when one-click auto tuning is on), so
  every fan-write error is treated the same way. Not validated on hardware yet.
- **rc.3: `--reset-tuning-to-factory --gpu <uniqueId>`** returns one named GPU to factory settings on
  demand, for a card left under manual tuning by 1.0.x or rc.1/rc.2. It resets **ALL of this GPU's
  tuning (clock, VRAM, power and fan) to factory settings, not only the fan**. There is no all-GPUs form.
  Exit `0` reset and confirmed by IsAtFactory, `3` already at factory (nothing written), `4` app
  running, `1` GPU not found, no manual fan tuning, `IsAtFactory` unreadable before the reset, or not
  confirmed. A repeated `--gpu` is a usage error (exit `2`). After a confirmed reset the GPU's
  dirty legacy journal entry is cleared. Not validated on hardware yet.
- **RDNA1 cards (RX 5700 / 5600 series) on machines whose System32 ADL/ADLX comes from a
  different AMD driver line than the one bound to the card.** ADLX opened but reported no GPU
  with manual fan tuning, and the ADL fallback found no fan interface. The app now loads the
  bound package's own `atiadlxx.dll` / `amdadlx64.dll` (see Changed).
- **Fan state left behind after a forced stop, crash or uninstall.** The pre-app fan state is
  journaled to `baseline.json`; the next start (or `--restore-auto`) returns to it instead of
  re-applying the killed run's manual curve. ADLX only.
- **rc.2: the bound-package lookup now accepts a `.\` disk path.** The RDNA1/2 driver package
  lists its library folder as `.\B026653`. rc.1 refused any `.` path segment, so the pre-load
  never ran and the app behaved like 1.0.0. `..`, UNC paths, drive letters and `%tokens%` are
  still refused.

### Added

- `--probe [--no-preload]`: read-only capability report written to `log.txt` and
  `probe-<yyyyMMddTHHmmssZ>.txt`. No window, no fan writes. Exit `0` controllable GPU,
  `3` none, `4` app running, `1` error. rc.3 adds `tuningAtFactory=yes|no|unsupported|error` per
  GPU and `preAppAtFactory=true|false|unknown` per journal entry.
- `--automation [--status-file <fullpath>]`: no modal dialogs; writes `status.json`
  (`starting`, `engaged`, `unavailable`, `stopped`) with `pid` and `startedUtc`. Exit `3` when
  unavailable, `4` when another instance is running.
- `--restore-auto`: one-shot restore of the journaled pre-app fan state. Exit `0` restored,
  `3` nothing to restore, `4` app running, `1` error.
- Start-up diagnostics in `log.txt`: ADLX init result, GPU count, per-GPU manual-tuning result,
  ADL Overdrive caps and version, and the path and version of each loaded `ati*` / `amd*` module.
- `--stop`: asks a running instance to exit gracefully and waits up to 20 s. Exit `0` stopped,
  `3` not running, `1` timeout or an older running build. It never kills the process.
- Status reason `no-enabled-gpu` (a GPU passed the self-test but all passing GPUs are disabled).
- Start-up log warning when ATI/AMD modules from a different driver line are loaded.
- Usage errors exit `2`: any two of `--probe`, `--restore-auto` and `--stop`, or `--no-preload` without `--probe`.
  Unknown arguments are logged and ignored.

### Changed

- **ADL/ADLX loading.** When the bound driver package ships `atiadlxx.dll` and `amdadlx64.dll`
  that differ from System32 and are AMD-signed, both are loaded from the package by full path
  before ADLX/ADL start. If anything is uncertain, v1.0 behaviour is kept. Nothing loads by
  DLL search.
- **Overdrive8 adapters no longer use the ADL fan path.** Only Overdrive 7 reaches OverdriveN
  (unchanged, including CustomFan) and Overdrive 5 reaches Overdrive5. Overdrive 8 is ADLX-only.
- **The uninstaller restores the pre-app fan state** (`--stop`, then a `taskkill` fallback, then
  `--restore-auto`) before removing files.
- **The journal is only applied to a state the app wrote.** A dirty entry whose current fan state
  is not one flat duty in 20-85 % with Zero RPM off (stale), or already equals the journaled state,
  is cleared instead of applied. A journal restore the driver rejects leaves the entry dirty.
  Tray start never prunes a dirty entry. `--restore-auto` and `--automation` wait up to 15 s for
  the single-instance mutex.

### Security

- No DLL search-path loading for `atiadlxx.dll` / `amdadlx64.dll`: when no bound pre-load
  applies, the System32 copies are loaded by full path.
- DriverStore copies must pass `WinVerifyTrust` and match the AMD signer pin (subject
  `O=Advanced Micro Devices`, issuer `CN=Sectigo Public Code Signing CA R36`) before they load.

## [1.0.0] — 2026-07-13

First public release. A per-user Windows systray utility (.NET Framework 4.8
WinForms) that drives AMD Radeon GPU fans toward a **core/edge temperature
target** (default 65 °C), as a lightweight alternative to the AMD Adrenalin
fan-curve UI. Publisher: Bitworks (bitworks.io).

> **Released.** Validated on real hardware (Radeon RX 7900 XTX). The installer is
> code-signed via Azure Artifact Signing. Superseded the `v1.0.0-rc.1`…`rc.8` prereleases.

### Added

- **Automatic fan curve** — ramps fan speed from Min% → Max% across
  `target … target + 25 °C`, with a hysteresis dead-band to avoid oscillation.
- **Over-temp safety** — emergency latch to the ceiling (85%) at or above the
  critical temperature, with hysteretic un-latch on cooldown; overrides manual mode.
- **Sensor-loss safety** — after 3 consecutive failed reads, relinquishes control
  back to the driver's automatic fan management.
- **Hard fan bounds** (manufacturer guidance) — floor **20%** (no Zero-RPM / passive
  idle), ceiling **85%** (not user-raisable to 100%).
- **Multi-GPU support** — primary GPU controlled by default; additional GPUs are
  opt-in. Full multi-GPU on the modern **ADLX** path; the legacy **ADL** fallback
  controls the **primary GPU only**.
- **Manual fixed-speed mode** and an **Automatic mode** toggle from the tray.
- **Pause / Resume** (tray) — hands all GPUs back to driver automatic.
- **Restore-to-automatic on exit** — on every graceful shutdown path (ProcessExit,
  UnhandledException, ThreadException, SessionEnding) and on Stop/Dispose, every
  controlled GPU is returned to the driver's automatic fan control.
- **Preferences window** — core-temp target, min/max fan, start-with-Windows,
  per-GPU enable checklist, live status readout, and a feedback link.
- **About dialog** with app version and a feedback/feature-request link to the
  Bitworks contact form.
- **Start with Windows** (per-user `HKCU\...\Run` entry, opt-in).
- **Windows installer** (Inno Setup) that bundles the native ADLX binding
  (`ADLXCSharpBind.dll`, `ADLXWrapper.dll`); installs per-user, no elevation required.

### Changed

- **Runtime: migrated to .NET Framework 4.8** — the app now targets .NET Framework 4.8, which
  is in-box on Windows 10 1903+ and Windows 11. End users need **no separate runtime download
  and no administrator rights**; the installer stays small since no runtime is bundled.

### Fixed (during Phase 5 hardware validation, Radeon RX 7900 XTX)

- **Fan now ramps back DOWN (both Automatic and Manual).** The AMD driver latches the manual fan
  setpoint at its highest value and ignores a lower fan curve until manual control is released — so
  lowering the target (min in Auto, fixed speed in Manual) left the fan stuck high; only exiting the
  app, which releases control, dropped it. The app now briefly **releases and re-applies whenever the
  target decreases**, forcing the driver to re-evaluate downward. (Also fixed a secondary settings
  clamp that could ratchet the manual setpoint up; covered by a regression test.)
- **Fan apply uses the percent-native ADLX fan curve.** Removes a unit mismatch where the
  target-fan-speed path was fed a percentage-range value where an RPM value was expected.
- **Preferences and About dialogs no longer clip buttons/text on high-DPI displays.** Rebuilt on
  auto-sizing layouts sized after DPI scaling (in `OnShown`); DPI mode made consistent with the manifest.

### Added (post-rc.1 hardware validation)

- **Preferences: fan-control mode + Apply.** The Automatic/Manual mode (and manual fan speed) is now
  shown and editable in Preferences, matching the tray. Buttons follow Windows convention —
  **OK** applies and closes, **Cancel** discards and closes, **Apply** applies and keeps the window open.
- **"Start with Windows" reflects the real startup state** (the installer's Run key), not just settings.
- **Adaptive GPU-monitor contention mitigation.** Detects co-running GPU monitors
  (GPU-Z, MSI Afterburner, HWiNFO, AIDA64, …), reduces sensor polling while they run, stops redundant
  fan writes, and warns once — to limit the AMD-driver sensor-bus contention that can cause crashes /
  "green screen" when several GPU tools poll at once.
- **Code-signing pipeline prepared.** CI has gated Azure Artifact Signing steps (passwordless
  OIDC via `azure/login` + `azure/artifact-signing-action`), a no-op until the signing secrets
  are configured. Onboarding steps in `docs/signing-setup.md`.

### Fixed (post-rc.7 bench test)

- **Installer can now replace a running copy.** The app is a windowless systray process that
  Windows Restart Manager couldn't reliably close, so installing over a running instance hung.
  The installer now disables Restart Manager (`CloseApplications=no`) and terminates the old
  instance itself before copying files; if it closed a running copy, it **relaunches the app**
  afterward so fan control resumes.
- **Preferences: OK now applies and closes.** The dialog is modeless, where a button's
  `DialogResult` does not auto-close the window — OK applied settings but left the pane open.
  OK now applies then closes; Cancel closes without saving; Apply applies and stays open.

### Security / Operational notes

- **Unsigned build.** Windows SmartScreen will warn on first run. Code-signing is wired
  as gated **Azure Artifact Signing** CI steps (`azure/login` OIDC +
  `azure/artifact-signing-action`) that stay a no-op until the `AZURE_*` repo secrets are
  set — no signing account provisioned yet. See `docs/signing-setup.md`.
- Runs as `asInvoker` (no elevation); installer `PrivilegesRequired=lowest`. The
  no-admin fan-set assumption is validated by Phase 5.
- No network calls except user-initiated browser opens to bitworks.io. No telemetry,
  no secrets.

[1.1.0]: https://github.com/bitworks-io/gpu-fan-control/compare/v1.0.0...main
[1.0.0]: https://github.com/bitworks-io/gpu-fan-control/releases/tag/v1.0.0

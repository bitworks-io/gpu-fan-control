# Changelog

All notable changes to **Lightweight AMD GPU Fan Control** are documented here.
Format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/);
this project uses [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [1.0.0] — Unreleased (pending Phase 5 hardware sign-off)

First public release. A per-user Windows systray utility (.NET Framework 4.8
WinForms) that drives AMD Radeon GPU fans toward a **core/edge temperature
target** (default 65 °C), as a lightweight alternative to the AMD Adrenalin
fan-curve UI. Publisher: Bitworks (bitworks.io).

> **Not yet released.** All automated and CI checks are green, but the on-hardware
> acceptance gate (Phase 5, real AMD Windows PC) is not yet signed off. Do not cut
> the `v1.0.0` tag until Phase 5 passes — see [docs/release-runbook.md](docs/release-runbook.md).

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

[1.0.0]: https://github.com/bitworks-io/gpu-fan-control/releases/tag/v1.0.0

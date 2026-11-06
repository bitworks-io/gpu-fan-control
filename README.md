# Lightweight AMD GPU Fan Control

**Publisher:** [Bitworks](https://bitworks.io)

A lightweight Windows systray utility that monitors AMD Radeon GPU temperature
and dynamically adjusts fan speed to hold a configurable core-temperature
target — a simpler, always-on alternative to the AMD Adrenalin fan-curve UI.

<!-- TODO: add screenshot of the Preferences window / tray icon -->

> ## ⚠️ Safety / Disclaimer
>
> This software directly controls your GPU's fan hardware. It enforces a
> hard fan-speed **floor of 20%** and **ceiling of 85%**, and restores your
> GPU to the driver's automatic fan control whenever the app exits cleanly.
> Even so, **use it at your own risk**: monitor your GPU temperatures while
> you get familiar with it, and if anything looks wrong, open AMD Software:
> Adrenalin Edition and switch fan tuning back to **Automatic**. The
> software is provided **without warranty of any kind**, per the terms of
> the [MIT License](LICENSE).

## Features

- **Systray icon** — runs quietly in the background with a tray icon and
  context menu.
- **Automatic fan curve** — ramps fan speed from a floor to a ceiling as GPU
  core temperature rises toward and past your target, with a hysteresis
  dead-band to avoid oscillation.
- **Configurable target temperature** — default 65°C.
- **Hard safety bounds** — fan speed is always kept between **20% (floor)**
  and **85% (ceiling)**, regardless of settings.
- **Over-temp safety latch** — jumps straight to the ceiling at/above a
  critical temperature, with hysteretic recovery on cooldown.
- **Sensor-loss safety** — after repeated failed temperature reads, hands
  control back to the driver's automatic fan management.
- **Multi-GPU support** — the primary GPU is controlled by default;
  additional GPUs are opt-in per system.
- **Manual fixed-speed mode**, plus an Automatic mode toggle and
  Pause/Resume from the tray.
- **Restore-to-automatic on exit** — every graceful shutdown path hands
  control back to the driver's automatic fan management.
- **RDNA1 (RX 5700 / 5600 series) support, pending hardware validation** — when the
  bound AMD driver package's ADL/ADLX differs from System32's, the app loads the
  bound package's own AMD-signed copies. Read-only `--probe` and unattended
  `--automation` modes are included; see [Command-line options](#command-line-options).
- **Start with Windows** — optional, per-user (no admin required).
- **Preferences window** — target temp, min/max fan, per-GPU enablement,
  live status readout.

## Requirements

- Windows 10 (version 1903 or later) or Windows 11, x64
- An AMD Radeon GPU
- **AMD Software: Adrenalin Edition** installed, with manual fan tuning
  available for your GPU

No additional runtime is required. The app targets **.NET Framework 4.8**,
which ships in-box on Windows 10 1903+ and Windows 11 — there's nothing extra
to download, and **no administrator rights are needed** to install or run it.

## Installation

1. Download `LightweightAmdGpuFanControl-Setup.exe` from the
   [latest release](../../releases/latest).
2. Run the installer. It installs per-user and does **not** require
   administrator rights.
3. **SmartScreen notice:** this build is not yet code-signed, so Windows
   SmartScreen may show "Windows protected your PC." Click **More info →
   Run anyway** to proceed. Signing is a planned fast-follow.

## First Run / Usage

After installation, the app runs in your system tray. Right-click the tray
icon for:

- **Preferences** — set your core-temp target, min/max fan bounds, and
  enable/disable control per GPU (for multi-GPU systems).
- **Automatic / Manual** — toggle between the automatic curve and a fixed
  manual fan speed.
- **Pause / Resume** — temporarily hand all GPUs back to the driver's
  automatic fan control without exiting the app.
- **Help** — opens troubleshooting steps if fan control isn't working.
- **About** — app version and a feedback link.
- **Exit** — closes the app and restores automatic fan control on every
  controlled GPU.

## Building From Source

Building is Windows-only (the app targets `net48`, a Windows-only .NET
Framework target).

### Prerequisites

1. **Visual Studio 2022 Build Tools** — C++ desktop and C# workloads
2. **SWIG 4.0.2** — [swigwin](https://www.swig.org/download.html), added to `PATH`
3. **Inno Setup 6** — for building the installer

### Steps

```powershell
git submodule update --init --recursive
.\build.ps1
```

`build.ps1` builds the ADLX C# bindings, publishes the app, and produces the
Inno Setup installer under `output\`. See `docs/WINDOWS_BUILD.md` for
Windows VM / Apple Silicon / CI build guidance, and
`docs/AMD_COMPATIBILITY.md` for GPU-generation and startup-test notes.

## Command-line options

Arguments are case-insensitive. Unknown arguments are logged and ignored.

| Invocation | Behaviour | Exit codes |
|---|---|---|
| *(no arguments)* | Interactive tray app | `0` |
| `--automation [--status-file <fullpath>]` | Tray app with no modal dialogs; writes `status.json` | `0` graceful exit · `3` unavailable · `4` already running |
| `--probe [--no-preload]` | Read-only capability report; no window, no fan writes | `0` controllable GPU · `3` none · `4` app running · `1` error |
| `--restore-auto` | One-shot restore of the journaled pre-app fan state | `0` restored · `3` nothing to restore · `4` app running · `1` error |
| `--stop` | Asks a running instance to exit gracefully (restores every backend) and waits up to 20 s; never kills | `0` stopped · `3` not running · `1` timeout or older running build |

`--probe` exits `0` only when ADLX manual fan tuning is supported; ADL adapters are
reported but not counted.

**Callers: check the version first.** Check that the exe's FileVersion is 1.1.0 or newer
before passing `--stop`, `--restore-auto` or `--probe`. An older build ignores unknown
arguments and starts the tray app, including its "already running" dialog.

Exit `2` is a usage error (no UI): any two of `--probe`, `--restore-auto` and `--stop`, or
`--no-preload` without `--probe`. `--automation` combined with `--probe` or `--restore-auto` is accepted.

Files are written to `%LOCALAPPDATA%\Bitworks\LightweightAmdGpuFanControl`:
`log.txt`, `probe-<yyyyMMddTHHmmssZ>.txt` (`--probe`), `status.json` (`--automation`)
and `baseline.json`.

### `status.json`

Written atomically, camelCase, schema `1`; readers must ignore unknown fields.

| Field | Meaning |
|---|---|
| `schema` | `1` |
| `state` | `starting`, `engaged`, `unavailable` or `stopped` |
| `engaged` | `true` only when `state` is `engaged` |
| `reason` | `null`, `no-backend`, `self-test-failed`, `no-enabled-gpu` or `exit` |
| `gpu`, `backend` | First GPU that passed the start-up self-test (`null` unless engaged) |
| `gpus` | Every GPU that passed, as `{ "name", "backend" }` |
| `pid`, `startedUtc` | The writing process |
| `updatedUtc` | Time of this write |
| `version` | App version |

Sequence: `starting` once the single-instance mutex is held; `unavailable` with
`no-backend` (no backend initialised) or `self-test-failed` (self-test failed on every
channel) or `no-enabled-gpu` (a GPU passed but every passing GPU is disabled in settings),
then exit `3`; `engaged` when the self-test passes on at least one channel;
`stopped` with `exit` on a graceful exit. When another instance is running the app
exits `4` and leaves `status.json` untouched. `--automation` waits up to 15 s for the
single-instance mutex before exiting `4`, so a just-stopped instance can finish exiting.
Once `stopped` or `unavailable` is written, later writes are ignored.

**Readers must key off `state` and `engaged`, not `reason`.** `reason` is explanatory text
and new values may be added.

A forced kill leaves the last state on disk. **A reader must match `pid` and
`startedUtc` against the process it launched** before trusting the file.

`--status-file <fullpath>` writes the status file there instead of the default.
The path must start with a drive letter (no UNC, no relative path, no `.` or `..`
segment, no illegal characters), its folder must exist, and neither the folder nor the
file may be a reparse point. A missing or invalid value is logged and the default
path is used; it never stops the app.

### `--restore-auto`

After a forced stop, crash or power loss the driver can keep the app's manual fan
curve. While controlling a GPU through ADLX the app records the pre-app per-state
fan speeds and Zero RPM state in `baseline.json` and marks the entry dirty before
its first fan write. `--restore-auto` puts every GPU with a matching dirty entry back on
that recorded state and marks the entry clean. It waits up to 15 s for the app's
single-instance mutex, then exits `4` if the app is still running; it exits `3` without
loading any AMD library when no entry is dirty. A dirty entry is restored only when the
GPU's current state looks like one this app wrote (one flat duty in the 20-85 % range,
Zero RPM off); otherwise the entry is stale and is just cleared. The installer runs
`--stop`, then a `taskkill` fallback, then `--restore-auto` on uninstall. ADL fallback adapters are not journaled. A corrupt `baseline.json` is set
aside as `baseline.json.bad` and ignored; it never blocks fan control.

## Troubleshooting: "Fan Control Not Working?"

1. Open **AMD Software: Adrenalin Edition** → **Performance** → **Tuning**.
2. Under your GPU's Tuning Control, enable **Manual Tuning, Custom**.
3. Set **Fan Tuning: ON** and **Zero RPM: OFF**.
4. Click **Apply Changes**, then reopen Lightweight AMD GPU Fan Control's
   Preferences.
5. If it's still not working, check the log at
   `%LOCALAPPDATA%\Bitworks\LightweightAmdGpuFanControl\log.txt` for errors.
6. When reporting a problem, run `LightweightAmdGpuFanControl.exe --probe` and attach the
   `probe-*.txt` from `%LOCALAPPDATA%\Bitworks\LightweightAmdGpuFanControl`.

## Reporting Issues

- Bugs and problems: please open a
  [GitHub issue](https://github.com/bitworks-io/gpu-fan-control/issues)
  on this repository.
- Feature requests are welcome via GitHub issues, or through the
  [Bitworks contact form](https://bitworks.io/contact-us/).
- For security vulnerabilities, please see [SECURITY.md](SECURITY.md) instead
  of opening a public issue.

## Changelog

See [CHANGELOG.md](CHANGELOG.md) for release history.

## License

Licensed under the [MIT License](LICENSE). Copyright (c) 2026 Bitworks.

## Third-Party

This project uses the AMD ADLX SDK (included as a git submodule at
`external/ADLX`) and an ADLX C# wrapper. See [NOTICE](NOTICE) for full
third-party attributions and licensing terms.

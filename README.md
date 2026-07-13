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

## Troubleshooting: "Fan Control Not Working?"

1. Open **AMD Software: Adrenalin Edition** → **Performance** → **Tuning**.
2. Under your GPU's Tuning Control, enable **Manual Tuning, Custom**.
3. Set **Fan Tuning: ON** and **Zero RPM: OFF**.
4. Click **Apply Changes**, then reopen Lightweight AMD GPU Fan Control's
   Preferences.
5. If it's still not working, check the log at
   `%LOCALAPPDATA%\Bitworks\LightweightAmdGpuFanControl\log.txt` for errors.

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

# Windows Build Guide

This app can be developed from macOS, but the release build should be produced on Windows because it depends on WinForms, ADLX C# bindings, Visual Studio C++ tooling, and Inno Setup.

## Recommended Build Options

### Option A: GitHub Actions

Use the workflow in `.github/workflows/build-windows.yml`. It runs on a Windows x64 runner, restores submodules, builds the ADLX C# wrapper, publishes the app, builds the Inno Setup installer, and uploads artifacts.

This is the easiest repeatable build path from an Apple Silicon Mac. It does not hardware-test fan control.

### Option B: Windows PC or Windows VM

Install:

1. Visual Studio 2022 or Build Tools with:
   - `.NET desktop development`
   - `Desktop development with C++`
2. .NET 8 SDK
3. SWIG 4.x in `PATH`
4. Inno Setup 6
5. Git

Then run from the repository root:

```powershell
.\build.ps1
```

To skip installer creation:

```powershell
.\build.ps1 -SkipInstaller
```

To try prerequisite installation with `winget`:

```powershell
.\build.ps1 -InstallPrerequisites
```

Visual Studio Build Tools may still need manual workload selection after this step.

## Apple M4 Notes

A true x86/x64 Windows VM is not practical on Apple Silicon. QEMU/UTM can emulate x86, but the Visual Studio and native binding build will be very slow.

Practical local option:

1. Install Windows 11 ARM in Parallels Desktop or VMware Fusion.
2. Install Visual Studio 2022 and the prerequisites above.
3. Build `win-x64`; Windows ARM can run many x64 build tools through emulation.

Hardware validation still needs a real AMD Radeon GPU in a Windows PC. A Mac VM will not expose an RX 470/Polaris or RX 9000-series GPU to AMD ADL/ADLX for fan-control testing.

## Hardware Test Matrix

At minimum, validate:

- RX 470 or RX 480 (Polaris, ADL OverdriveN fallback)
- RX 580/590 (Polaris refresh)
- RX 5000/6000/7000 (ADLX)
- RX 9000-series / 9070-class card (ADLX/current driver)

Startup test logs are written to:

```text
%LOCALAPPDATA%\Bitworks\LightweightAmdGpuFanControl\log.txt
```

# Lightweight AMD GPU Fan Control

**Author:** Bitworks

A Windows systray utility that monitors AMD GPU temperature and dynamically adjusts fan speed (20–85%) to maintain a configurable target temperature.

## Features

- **Systray icon** – Runs in background with system tray icon
- **Start Menu shortcut** – Created by the installer
- **Start with Windows** – Optional preference (registry Run key)
- **Target temperature** – Configurable (default 65°C)
- **Default active mode** – Targets 65°C, disables Zero RPM when supported, keeps fans at a minimum 20%, and ramps earlier than stock GPU BIOS behavior
- **Fan curve** – Min 20%, max 85%, ramps to keep GPU under target temp
- **Hybrid AMD support** – Uses ADLX first for modern Radeon cards and ADL Overdrive fallback for Polaris/legacy cards
- **Startup test** – Verifies fan control at 35% before enabling, with percent/RPM telemetry handling
- **Help** – Integrated troubleshooting steps when control fails

## Requirements

- Windows 10/11 x64
- AMD Radeon GPU
  - RX 5000/6000/7000/9000-series: ADLX backend
  - RX 470/480/500-series Polaris: ADL OverdriveN fallback
  - Older cards: best-effort ADL Overdrive5 fallback when the driver exposes fan APIs
- AMD Software: Adrenalin Edition (latest drivers)
- .NET 8 runtime (included if built self-contained)

## Building (Windows)

### Prerequisites

1. **Visual Studio 2022** – With C++ desktop and C# workloads
2. **SWIG 4.0.2** – [swigwin](https://www.swig.org/download.html), add to PATH
3. **.NET 8 SDK**

### Build Steps

The easiest repeatable build is GitHub Actions (`.github/workflows/build-windows.yml`) on a Windows x64 runner. For local Windows builds:

1. **Build ADLX C# bindings** (one-time, requires SWIG):
   ```cmd
   cd external\ADLX\Samples\csharp
   msbuild csharp.sln -p:Configuration=Release -p:Platform=x64
   ```
   Or open `csharp.sln` in Visual Studio and build.

2. **Build and publish the app**:
   ```cmd
   cd <workspace root>
   dotnet publish LightweightAmdGpuFanControl\LightweightAmdGpuFanControl.csproj -c Release -r win-x64
   ```

3. **Create installer** (Inno Setup 6):
   ```cmd
   iscc installer\LightweightAmdGpuFanControl.iss
   ```
   Output: `output\LightweightAmdGpuFanControl-Setup.exe`

Or run the automated build script:

```powershell
.\build.ps1
```

See `docs/WINDOWS_BUILD.md` for Windows VM, Apple Silicon, and CI build guidance. See `docs/AMD_COMPATIBILITY.md` for GPU-generation and startup-test edge cases.

## Running

Run `LightweightAmdGpuFanControl.exe`. Right-click the systray icon for:

- **Preferences** – Set target temp, Start with Windows
- **Help / Fan control not working** – Opens troubleshooting guide
- **Exit**

## Fan Control Not Working?

1. In AMD Adrenalin: **Performance** → **Tuning**
2. GPU → Tuning Control: enable **Manual Tuning, Custom**
3. Fan Tuning: **ON**, Zero RPM: **OFF**
4. Apply Changes

## Compatibility Notes

ADLX is AMD's modern API and is used first. ADLX may not support older GPUs such as RX 470/480, so the app falls back to ADL Overdrive APIs when available. Because older ADL paths can report fan telemetry as either percentage or RPM, startup validation checks control state, percent telemetry, and RPM response before enabling the control loop.

Building on an Apple M4 should use either GitHub Actions or Windows 11 ARM in Parallels/VMware. Final fan-control testing requires a real Windows PC with an AMD Radeon GPU.

## License

Uses AMD ADLX SDK (see `external/ADLX`). Application code by Bitworks.

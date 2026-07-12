# Lightweight AMD GPU Fan Control — v1.0 Release Readiness Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Ship a working, safe, multi-GPU, testable v1.0 installer of the AMD GPU fan-control tray app that keeps each enabled GPU's core temperature near a 65 °C target.

**Architecture:** Extract the fan-control math into a pure, testable `FanControlPolicy` in a new `net8.0` core library; give backends a `RestoreAutomaticFanControl()` capability and per-GPU identity; run one policy per enabled GPU; add a safety lifecycle (restore-to-auto on exit/crash), a live-readout/pause/manual/About UI, and fix the broken installer packaging + CI so the ADLX native DLL actually ships.

**Tech Stack:** C# / .NET 8 (`net8.0-windows` app + `net8.0` core lib), WinForms, ADLX (SWIG C# wrapper + native `ADLXCSharpBind.dll`) & legacy ADL, xUnit, GitHub Actions (windows-latest), Inno Setup.

**Source of truth:** [docs/superpowers/specs/2026-07-03-fan-control-release-design.md](../specs/2026-07-03-fan-control-release-design.md). Read it before starting.

**Test execution note:** The app is Windows-only. Unit tests target the `net8.0` core library and run on any OS; the full build + hardware self-test run on the Windows CI runner. `dotnet test` commands below assume the Windows CI runner or a Windows dev box for the app, but core-lib tests also run on macOS/Linux.

---

## File Structure (decomposition)

**New:**
- `LightweightAmdGpuFanControl.Core/` — `net8.0` class library (no WinForms). Holds pure logic:
  - `Models/AppSettings.cs` (moved), `Models/GpuConfig.cs`, `Control/FanControlPolicy.cs`, `Control/PolicyState.cs`, `Control/FanDecision.cs`, `Control/TempReading.cs`, `Services/SettingsService.cs` (moved), `AppLinks.cs`.
- `LightweightAmdGpuFanControl.Tests/` — `net8.0` xUnit project referencing Core.
- `LightweightAmdGpuFanControl/app.manifest` — `asInvoker`.
- `LightweightAmdGpuFanControl/Forms/AboutForm.cs` — About dialog.
- `VERSION` (repo root) — single-source version string.

**Modified:**
- `LightweightAmdGpuFanControl/Gpu/IFanControlBackend.cs` — add `RestoreAutomaticFanControl()`, `GpuId`.
- `LightweightAmdGpuFanControl/Gpu/AdlxFanControlBackend.cs`, `Gpu/Adl/AdlFanControlBackend.cs`, `Gpu/Adl/AdlNativeApi.cs`, `Adlx/FanController.cs` — restore-to-auto, configurable bounds, per-GPU.
- `LightweightAmdGpuFanControl/Gpu/FanControlBackendFactory.cs` — `CreateAll()` (multi-GPU, single family).
- `LightweightAmdGpuFanControl/Services/FanControlService.cs` — use policy, per-GPU channels, lifecycle.
- `LightweightAmdGpuFanControl/Services/FanControlTestService.cs` — restore prior state, run off UI thread.
- `LightweightAmdGpuFanControl/Forms/PreferencesForm.cs` — min/max, GPU list, live readout, feedback link.
- `LightweightAmdGpuFanControl/SystrayApplicationContext.cs` — pause/manual/About menu, tooltip, lifecycle hooks.
- `LightweightAmdGpuFanControl/LightweightAmdGpuFanControl.csproj` — Core ref, B1 fix, manifest, version.
- `LightweightAmdGpuFanControl.sln` — add Core + Tests.
- `installer/LightweightAmdGpuFanControl.iss`, `.github/workflows/build-windows.yml`, `build.ps1` — packaging, verify, versioning, release, signing hook.

---

## Phase 0 — Setup

### Task 0: Feature branch
- [ ] **Step 1:** Create the branch (repo currently on `main`).

```bash
git checkout -b feature/v1.0-release-readiness
```

- [ ] **Step 2:** Confirm clean-ish start.

```bash
git status
```
Expected: on `feature/v1.0-release-readiness`; pre-existing untracked docs remain (do not revert them).

---

## Phase 1 — Build release-blockers (independent; highest priority)

**Outcome:** CI produces an installer that actually contains `ADLXCSharpBind.dll`, verified automatically, versioned, released on tag, with a signing hook and an `asInvoker` manifest. No app-behavior changes.

### Task 1.1: Reproduce the B1 defect (baseline evidence)
**Files:** none (investigation).

- [ ] **Step 1:** Confirm the current shipped output is missing the native DLL.

```bash
gh auth switch --hostname github.com --user bitworks-io
RUN=$(gh run list --workflow build-windows.yml -L 1 --json databaseId --jq '.[0].databaseId')
gh run download "$RUN" -n LightweightAmdGpuFanControl-publish -D /tmp/b1check
ls /tmp/b1check
```
Expected: `ADLXWrapper.dll` present, **`ADLXCSharpBind.dll` absent** — the bug. (This is the before-state the fix must flip.)

### Task 1.2: Locate the real ADLXCSharpBind.dll output path
**Files:** none (investigation on a Windows build, or inspect the vcxproj).

- [ ] **Step 1:** Determine where `msbuild csharp.sln` writes `ADLXCSharpBind.dll` and its CRT linkage.

Inspect `external/ADLX/Samples/csharp/ADLXCSharpBind/ADLXCSharpBind/ADLXCSharpBind.vcxproj` for `<OutDir>` and `<RuntimeLibrary>`. Record: (a) exact output path relative to repo root for `Configuration=Release;Platform=x64`, (b) whether `RuntimeLibrary` is `MultiThreadedDLL` (needs VC++ redist) or `MultiThreaded` (static `/MT`, self-contained).
Expected: a concrete path like `external/ADLX/Samples/csharp/x64/Release/ADLXCSharpBind.dll`. If `RuntimeLibrary` is `MultiThreadedDLL`, add Task 1.6.

### Task 1.3: Fix packaging so the native DLL reaches `publish\` + hard-fail if missing
**Files:** Modify `LightweightAmdGpuFanControl/LightweightAmdGpuFanControl.csproj:24-30`.

- [ ] **Step 1:** Replace the silent `CopyAdlxNativeLibs` target with a `Content` item (so publish carries it) **and** a separate explicit error guard. Use the path confirmed in Task 1.2 (shown here as the expected default).

```xml
  <!-- Ship the native ADLX SWIG binding. Build the ADLX C++ project (csharp.sln) first. -->
  <PropertyGroup>
    <AdlxBindPath>$(MSBuildThisFileDirectory)..\external\ADLX\Samples\csharp\x64\$(Configuration)\ADLXCSharpBind.dll</AdlxBindPath>
  </PropertyGroup>

  <ItemGroup>
    <Content Include="$(AdlxBindPath)" Condition="Exists('$(AdlxBindPath)')">
      <Link>ADLXCSharpBind.dll</Link>
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
      <CopyToPublishDirectory>PreserveNewest</CopyToPublishDirectory>
    </Content>
  </ItemGroup>

  <!-- Hard fail: the Content item above silently includes nothing if the path is wrong. This makes that impossible. -->
  <Target Name="EnsureAdlxNativeBinding" BeforeTargets="Build;Publish">
    <Error Condition="!Exists('$(AdlxBindPath)')"
           Text="ADLXCSharpBind.dll not found at $(AdlxBindPath). Build external\ADLX\Samples\csharp\csharp.sln (x64/$(Configuration)) before building the app." />
  </Target>
```

- [ ] **Step 2:** (Windows) Verify a local publish now contains the DLL.

```bash
pwsh -File build.ps1 -Configuration Release
ls LightweightAmdGpuFanControl/bin/Release/net8.0-windows/win-x64/publish/ | grep -i ADLXCSharpBind
```
Expected: `ADLXCSharpBind.dll` listed. If the `<Error>` fires instead, fix `AdlxBindPath` to the Task 1.2 path.

- [ ] **Step 3:** Commit.

```bash
git add LightweightAmdGpuFanControl/LightweightAmdGpuFanControl.csproj
git commit -m "fix(build): ship ADLXCSharpBind.dll in publish output and hard-fail if missing"
```

### Task 1.4: CI verification step (B2)
**Files:** Modify `.github/workflows/build-windows.yml` (after the "Build installer" step, before uploads).

- [ ] **Step 1:** Add a step asserting both DLLs are in the publish output; fail otherwise.

```yaml
      - name: Verify ADLX runtime binaries are present
        shell: pwsh
        run: |
          $pub = "LightweightAmdGpuFanControl/bin/Release/net8.0-windows/win-x64/publish"
          $required = @("ADLXCSharpBind.dll","ADLXWrapper.dll","LightweightAmdGpuFanControl.exe")
          $missing = $required | Where-Object { -not (Test-Path (Join-Path $pub $_)) }
          if ($missing) { Write-Error "Missing required binaries: $($missing -join ', ')"; exit 1 }
          Write-Host "All required ADLX binaries present."
```

- [ ] **Step 2:** Commit.

```bash
git add .github/workflows/build-windows.yml
git commit -m "ci: assert ADLX native + managed DLLs are present in publish output"
```

### Task 1.5: Single-source version + GitHub Release on tag (B4)
**Files:** Create `VERSION`; modify `.csproj:16`, `installer/LightweightAmdGpuFanControl.iss:5`, `build.ps1`, `.github/workflows/build-windows.yml`.

- [ ] **Step 1:** Create `VERSION` at repo root containing exactly `1.0.0`.
- [ ] **Step 2:** In `build.ps1`, read it and pass to both builds. After `$Root` is set (line 20), add:

```powershell
$Version = (Get-Content "$Root\VERSION" -Raw).Trim()
Write-Host "Building version $Version" -ForegroundColor Cyan
```
Change the publish call (line 78) to inject the version:
```powershell
& dotnet publish "$Root\LightweightAmdGpuFanControl\LightweightAmdGpuFanControl.csproj" -c $Configuration -r win-x64 --self-contained false -v m -p:Version=$Version
```
Change the iscc call (line 88) to pass the version:
```powershell
& iscc "/DMyAppVersion=$Version" "$Root\installer\LightweightAmdGpuFanControl.iss"
```
- [ ] **Step 3:** In the `.iss`, make version overridable — replace line 5:
```
#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
```
- [ ] **Step 4:** Add a release job to the workflow (runs only on tag push `v*`), attaching the installer:
```yaml
  release:
    needs: build
    if: startsWith(github.ref, 'refs/tags/v')
    runs-on: ubuntu-latest
    permissions:
      contents: write
    steps:
      - uses: actions/download-artifact@v4
        with:
          name: LightweightAmdGpuFanControl-Setup
          path: ./release
      - uses: softprops/action-gh-release@v2
        with:
          files: ./release/LightweightAmdGpuFanControl-Setup.exe
```
- [ ] **Step 5:** Commit.
```bash
git add VERSION build.ps1 installer/LightweightAmdGpuFanControl.iss .github/workflows/build-windows.yml
git commit -m "build: single-source version + GitHub Release on tag"
```

### Task 1.6 (conditional): VC++ runtime for ADLXCSharpBind.dll
**Only if Task 1.2 found `RuntimeLibrary = MultiThreadedDLL`.**
- [ ] **Step 1:** Prefer static CRT: set `<RuntimeLibrary>MultiThreaded</RuntimeLibrary>` in the vcxproj (Release|x64) so no redist is needed. If that's not acceptable, instead add the VC++ redistributable as an installer prerequisite in the `.iss`. Document the choice in `docs/WINDOWS_BUILD.md`.
- [ ] **Step 2:** Commit with message `build: statically link CRT in ADLXCSharpBind to avoid VC++ redist dependency`.

### Task 1.7: asInvoker manifest (B5)
**Files:** Create `LightweightAmdGpuFanControl/app.manifest`; modify `.csproj`.

- [ ] **Step 1:** Create `app.manifest` with a standard `asInvoker` requestedExecutionLevel (level `asInvoker`, uiAccess `false`) plus the Windows 10/11 `supportedOS` GUIDs.
- [ ] **Step 2:** In `.csproj` PropertyGroup add `<ApplicationManifest>app.manifest</ApplicationManifest>`.
- [ ] **Step 3:** Commit `build: add explicit asInvoker application manifest`.

> **Hardware-gated note (do not skip at release):** On a real AMD PC, confirm fan-set works un-elevated. If it needs admin, change the manifest to `requireAdministrator` and set `PrivilegesRequired=admin` in the `.iss`. Track in the Phase 5 verification checklist.

### Task 1.8: Signing hook (B3)
**Files:** Modify `.github/workflows/build-windows.yml`; `README.md`.
- [ ] **Step 1:** Add a signing step guarded by a secret so it no-ops until a cert exists:
```yaml
      - name: Sign installer
        if: ${{ env.SIGN_CERT_BASE64 != '' }}
        env:
          SIGN_CERT_BASE64: ${{ secrets.SIGN_CERT_BASE64 }}
          SIGN_CERT_PASSWORD: ${{ secrets.SIGN_CERT_PASSWORD }}
        shell: pwsh
        run: |
          # Decode cert, run signtool on output\LightweightAmdGpuFanControl-Setup.exe.
          Write-Host "Signing enabled (cert secret present)."
```
- [ ] **Step 2:** Add a README note: v1.0 is unsigned; Windows SmartScreen may warn ("More info → Run anyway"). Commit `ci: add gated code-signing step (no-op until cert secret present)`.

### Task 1.9: Re-verify the build proof (closes B1)
- [ ] **Step 1:** Push the branch, let CI run, re-download the publish artifact and confirm the fix.
```bash
git push -u origin feature/v1.0-release-readiness
RUN=$(gh run list --branch feature/v1.0-release-readiness --workflow build-windows.yml -L 1 --json databaseId --jq '.[0].databaseId')
gh run watch "$RUN"
gh run download "$RUN" -n LightweightAmdGpuFanControl-publish -D /tmp/b1after
ls /tmp/b1after | grep -i ADLXCSharpBind
```
Expected: `ADLXCSharpBind.dll` **present**, and the "Verify ADLX runtime binaries" step green. **Phase 1 acceptance:** installer now contains the native binding.

---

## Phase 2 — Core library, control policy & safety

**Outcome:** Pure, unit-tested control logic; backends can restore automatic control; the app restores auto on exit/crash and never leaves the fan pinned. Single-GPU behavior preserved.

### Task 2.1: Create the core library and test project
**Files:** Create `LightweightAmdGpuFanControl.Core/LightweightAmdGpuFanControl.Core.csproj` (`<TargetFramework>net8.0</TargetFramework>`, `<Nullable>enable</Nullable>`); `LightweightAmdGpuFanControl.Tests/LightweightAmdGpuFanControl.Tests.csproj` (`net8.0`, xUnit, ProjectReference to Core); add both to `LightweightAmdGpuFanControl.sln`.
- [ ] **Step 1:** Create Core csproj (empty class lib).
- [ ] **Step 2:** Create Tests csproj referencing xUnit (`xunit`, `xunit.runner.visualstudio`, `Microsoft.NET.Test.Sdk`) + `<ProjectReference>` to Core.
- [ ] **Step 3:** `dotnet sln add` both; add `<ProjectReference>` to Core from the WinForms `.csproj`.
- [ ] **Step 4:** Verify empty solution builds.
```bash
dotnet build LightweightAmdGpuFanControl.sln
```
Expected: build succeeds. Commit `chore: add Core class library and xUnit test project`.

### Task 2.2: Move settings model into Core + expand it
**Files:** Move `LightweightAmdGpuFanControl/Models/AppSettings.cs` → `LightweightAmdGpuFanControl.Core/Models/AppSettings.cs`; create `Models/GpuConfig.cs`; update namespaces (keep `LightweightAmdGpuFanControl.Models` namespace so app `using`s are unchanged).

- [ ] **Step 1:** Write failing test `Tests/AppSettingsTests.cs`:
```csharp
using LightweightAmdGpuFanControl.Models;
using Xunit;

public class AppSettingsTests
{
    [Fact]
    public void Defaults_match_spec()
    {
        var s = new AppSettings();
        Assert.Equal(65, s.TargetTempC);
        Assert.Equal(20, s.MinFanPercent);
        Assert.Equal(85, s.MaxFanPercent);
        Assert.Equal(3, s.HysteresisC);
        Assert.Equal(90, s.CriticalTempC);
        Assert.Equal(FanMode.Auto, s.Mode);
        Assert.NotNull(s.Gpus);
    }
}
```
- [ ] **Step 2:** `dotnet test --filter AppSettingsTests` → FAIL (members missing).
- [ ] **Step 3:** Implement the expanded `AppSettings` (per spec §5.1) with consts for bounds, `enum FanMode { Auto, Manual }`, `int ManualFanPercent`, `List<GpuConfig> Gpus = new()`. Create `GpuConfig { string GpuId; bool Enabled; }`.
- [ ] **Step 4:** `dotnet test --filter AppSettingsTests` → PASS. Commit `feat(core): expand AppSettings for curve, mode, and per-GPU config`.

### Task 2.3: SettingsService validation + old-schema migration
**Files:** Move `SettingsService.cs` → Core; extend `Validate`.

- [ ] **Step 1:** Failing tests `Tests/SettingsServiceTests.cs` (use a temp file path injected via a new constructor overload `SettingsService(string path)`):
```csharp
[Fact] public void Clamps_out_of_range_fields() { /* target=200 -> 90, max=99 -> 85, min=5 -> 20 */ }
[Fact] public void Enforces_min_less_than_max() { /* min=80,max=40 -> min<max holds */ }
[Fact] public void Old_schema_json_deserializes_with_new_defaults()
{
    // write {"TargetTempC":72,"StartWithWindows":true}
    // load -> TargetTempC 72, MinFanPercent 20, MaxFanPercent 85, Gpus not null
}
[Fact] public void Corrupt_json_returns_defaults() { }
```
Write the exact bodies (temp file per test, assert clamped values).
- [ ] **Step 2:** `dotnet test --filter SettingsServiceTests` → FAIL.
- [ ] **Step 3:** Add the `SettingsService(string path)` ctor; extend `Validate` to clamp every field (spec §5.1 ranges) and enforce `MinFanPercent < MaxFanPercent` (raise max to `min+5`, capped at 85). System.Text.Json already fills missing props with defaults — the migration test passes once defaults exist.
- [ ] **Step 4:** `dotnet test --filter SettingsServiceTests` → PASS. Commit `feat(core): validate all settings fields and migrate old-schema settings.json`.

### Task 2.4: FanControlPolicy (the heart) — TDD
**Files:** Create `Core/Control/TempReading.cs`, `Control/FanDecision.cs`, `Control/PolicyState.cs`, `Control/FanControlPolicy.cs`; `Tests/FanControlPolicyTests.cs`.

Types:
```csharp
public readonly record struct TempReading(double? TempC);
public enum FanAction { SetPercent, RelinquishToAuto }
public readonly record struct FanDecision(FanAction Action, int Percent);
public sealed class PolicyState { public int PreviousFanPercent; public int ConsecutiveReadFailures; public bool EmergencyLatched; }
```
`FanControlPolicy` constants: `RampTempRange = 25`, `ReadFailureLimit = 3`. Method:
```csharp
public FanDecision Decide(TempReading reading, AppSettings s, PolicyState state);
```
Rules (spec §4.5, §6.3):
- Manual mode (`s.Mode==Manual`): base = `Clamp(s.ManualFanPercent, s.MinFanPercent, s.MaxFanPercent)`, then apply emergency/failsafe below.
- Read failure (`reading.TempC == null`): increment counter; if `>= ReadFailureLimit` return `RelinquishToAuto`; else hold `PreviousFanPercent` (SetPercent). On success reset counter to 0.
- Emergency: `temp >= CriticalTempC` → latch, return SetPercent `MaxFanPercent`. Unlatch when `temp < CriticalTempC - HysteresisC`.
- Auto ramp: `temp < TargetTempC - HysteresisC` → `MinFanPercent`. `temp >= TargetTempC` → `Min + (temp-Target)*(Max-Min)/RampTempRange`, clamped `Min..Max`. Between `Target-Hyst` and `Target` → hold `PreviousFanPercent` clamped to `Min..Max` (hysteresis dead-band). Always update `PreviousFanPercent` to the returned SetPercent value.

- [ ] **Step 1:** Write `FanControlPolicyTests.cs` with these cases (real asserts):
```csharp
// below target-hyst -> min
// exactly at target -> min (ramp start)
// at target+25 -> max
// at target+12.5 -> ~ (min+max)/2 (allow ±1)
// in dead-band holds previous
// temp null x2 holds previous; x3 -> RelinquishToAuto
// temp>=critical -> SetPercent(max) and latched; stays latched at critical-1; unlatches below critical-hyst
// manual mode returns manual percent clamped; but critical still forces max
// max is 85, never exceeds even at target+100
```
- [ ] **Step 2:** `dotnet test --filter FanControlPolicyTests` → FAIL (policy not implemented).
- [ ] **Step 3:** Implement `FanControlPolicy.Decide` exactly per the rules above.
- [ ] **Step 4:** `dotnet test --filter FanControlPolicyTests` → PASS. Commit `feat(core): pure FanControlPolicy with ramp, hysteresis, emergency, sensor-loss`.

### Task 2.5: Backend restore-to-auto + configurable bounds + GpuId
**Files:** Modify `Gpu/IFanControlBackend.cs`, `Adlx/FanController.cs`, `Gpu/AdlxFanControlBackend.cs`, `Gpu/Adl/AdlFanControlBackend.cs`, `Gpu/Adl/AdlNativeApi.cs`.

- [ ] **Step 1:** `IFanControlBackend`: add `string GpuId { get; }` and `void RestoreAutomaticFanControl();`. Change `SetFanPercent(int percent)` callers to pass an already-policy-clamped value; keep a defensive absolute clamp `20..85` inside backends (constants stay as safety floor/ceiling, NOT as the policy range).
- [ ] **Step 2:** `FanController`: add `public void RestoreAutomatic()` → `try { _manualFanTuning?.Reset(); } catch { }` (uses the wrapper `Reset()`, [ManualFanTuning.cs:77-83](../../../external/ADLX/Samples/csharp/ADLXWrapper/ManualFanTuning.cs)).
- [ ] **Step 3:** `AdlxFanControlBackend`: implement `RestoreAutomaticFanControl()` → `_fanController?.RestoreAutomatic()`. Implement `GpuId` from the most stable ADLX GPU field (e.g. unique id/name+index — inspect `ADLXWrapper.GPU`; fall back to `AdapterName`).
- [ ] **Step 4:** `AdlNativeApi`: add optional delegate `ADL2_Overdrive5_FanSpeedToDefault_Set(IntPtr context, int adapterIndex, int thermalControllerIndex)` (required:false) and expose it.
- [ ] **Step 5:** `AdlFanControlBackend`: implement `RestoreAutomaticFanControl()` — OverdriveN: read current `ADLODNFanControl`, set `iMode = 0` (ODNControlType_Default), call `OverdriveNFanControlSet`/`CustomFanSet`; OverdriveN unavailable path OD5: call `Overdrive5FanSpeedToDefault?.Invoke(...)`. All best-effort/try-catch. Add `GpuId` = `$"ADL-{_adapterIndex}"`.
- [ ] **Step 6:** Build (Windows). No unit test (hardware); covered by fake in Task 2.6/3. Commit `feat(gpu): restore automatic fan control on ADLX and ADL backends`.

### Task 2.6: FanControlService rewrite to use policy + lifecycle
**Files:** Rewrite `Services/FanControlService.cs`; modify `SystrayApplicationContext.cs` (lifecycle hooks); modify `Services/FanControlTestService.cs`.

- [ ] **Step 1:** `FanControlService`: hold a list of channels `{ IFanControlBackend backend, PolicyState state }` (single-GPU for now: the one backend). Each poll: `temp = backend.GetTemperatureC()`; `decision = _policy.Decide(new TempReading(temp), settings, state)`; if `SetPercent` → `backend.SetFanPercent(decision.Percent)`; if `RelinquishToAuto` → `backend.RestoreAutomaticFanControl()`. Add `public void RestoreAll()` iterating channels → `RestoreAutomaticFanControl()`. Keep the 2.5 s timer.
- [ ] **Step 2:** `FanControlTestService.RunTest`: capture `before = GetFanTelemetry()` / current state, run the 35 % probe, then **restore**: call a passed-in `Action restore` (the backend's `RestoreAutomaticFanControl`) instead of leaving 35 %. Signature becomes `RunTest(IFanControlBackend backend, LogService log)` and it calls `backend.RestoreAutomaticFanControl()` at the end (pass/fail both). Run the whole test on a background thread (`Task.Run`) so `Start` doesn't block the UI thread; surface the balloon via `notifyIcon` on completion.
- [ ] **Step 3:** `SystrayApplicationContext`: register lifecycle handlers that call `_fanControlService.RestoreAll()`:
  - `Application.ApplicationExit`, `AppDomain.CurrentDomain.ProcessExit`, `AppDomain.CurrentDomain.UnhandledException`, `Application.ThreadException`, `Microsoft.Win32.SystemEvents.SessionEnding`. In `Exit()`, call `RestoreAll()` before disposing. Make `RestoreAll` idempotent.
- [ ] **Step 4:** Build (Windows) + run app on a real AMD box: set a low target, confirm fan ramps; exit, confirm the card returns to driver auto (Adrenalin shows automatic). Commit `feat(service): policy-driven control loop with restore-to-auto lifecycle`.

**Phase 2 acceptance:** `dotnet test` green for Core; on hardware, exiting the app restores automatic fan control and the self-test no longer leaves the fan pinned.

---

## Phase 3 — Multi-GPU (single family, shared init)

**Outcome:** All supported GPUs enumerated under one backend family; primary controlled by default, others opt-in; shared curve per enabled GPU.

### Task 3.1: Enumerate all GPUs under one family
**Files:** Modify `Gpu/FanControlBackendFactory.cs`, `Adlx/GpuMonitor.cs`, `Adlx/FanController.cs`, `Gpu/AdlxFanControlBackend.cs`, `Gpu/Adl/AdlFanControlBackend.cs`.

- [ ] **Step 1:** Add `FanControlBackendFactory.CreateAll(LogService)` → `IReadOnlyList<IFanControlBackend>`: try ADLX — if it initializes, enumerate **all** ADLX GPUs from one `AdlxInitializer`/`SystemServices` and build one `AdlxFanControlBackend` per GPU that passes `IsManualFanTuningSupported`. Only if ADLX init fails, enumerate ADL adapters into per-adapter `AdlFanControlBackend`s. Never mix families. (Refactor `AdlxFanControlBackend` to accept an externally-owned `SystemServices` + a specific `GPU` rather than each creating its own initializer, so one init is shared.)
- [ ] **Step 2:** Ensure exactly one `AdlxInitializer` is disposed once (owned by the factory result / a small `IFanControlSystem` wrapper). Build. Commit `feat(gpu): enumerate all supported GPUs under a single backend family`.

### Task 3.2: Per-GPU channels + default-on rule (TDD on the pure logic)
**Files:** Create `Core/Control/GpuEnablement.cs` (pure helper); `Tests/GpuEnablementTests.cs`; modify `Services/FanControlService.cs`.

- [ ] **Step 1:** Failing tests for the pure reconcile function `IReadOnlyList<GpuConfig> Reconcile(IReadOnlyList<string> detectedIds, AppSettings settings)` (spec §5.3):
```csharp
// no saved config, single detected id -> that id Enabled=true
// no saved config, two detected ids -> first Enabled=true, second false
// saved config disables primary -> respected (not re-enabled)
// detected id not in saved list, another already enabled -> new one defaults disabled
```
- [ ] **Step 2:** `dotnet test --filter GpuEnablementTests` → FAIL.
- [ ] **Step 3:** Implement `GpuEnablement.Reconcile` exactly per §5.3 (primary = index 0 of `detectedIds`; enable primary iff no other GPU currently enabled).
- [ ] **Step 4:** PASS. Then wire `FanControlService` to build one channel (backend + `PolicyState`) per **enabled** GPU using `Reconcile`, and persist the reconciled list back via `SettingsService.Save`. Poll loop iterates enabled channels; `RestoreAll` iterates all engaged channels. Commit `feat: per-GPU control channels with primary-on-by-default enablement`.

**Phase 3 acceptance:** `GpuEnablementTests` green; on a single-GPU box behavior is unchanged; on multi-GPU only enabled GPUs are driven.

---

## Phase 4 — UI: readout, pause/manual, feedback & About

**Outcome:** Users see live temp/fan, can configure min/max + which GPUs, pause or set a manual speed, and reach the bitworks.io contact form from Preferences and About.

### Task 4.1: AppLinks + About dialog
**Files:** Create `Core/AppLinks.cs`; `LightweightAmdGpuFanControl/Forms/AboutForm.cs`; modify `SystrayApplicationContext.cs`.

- [ ] **Step 1:** `Core/AppLinks.cs`:
```csharp
namespace LightweightAmdGpuFanControl;
public static class AppLinks
{
    public const string ContactFormUrl = "https://bitworks.io/contact-us/";
    public const string WebsiteUrl = "https://bitworks.io";
    public static void Open(string url)
    {
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo { FileName = url, UseShellExecute = true }); }
        catch { }
    }
}
```
- [ ] **Step 2:** `AboutForm`: fixed-dialog, non-resizable. Shows: product name; version from `Assembly.GetExecutingAssembly().GetName().Version`; "Publisher: Bitworks"; one-line description; encouraging copy ("Found a bug or want a feature? We'd love to hear from you."); a prominent button **"Send feedback / request a feature"** → `AppLinks.Open(AppLinks.ContactFormUrl)`; a LinkLabel to `AppLinks.WebsiteUrl`. Close button.
- [ ] **Step 3:** `SystrayApplicationContext.CreateContextMenu`: insert `"About…"` (opens `AboutForm`, single-instance like Preferences) before Exit; final order: Preferences · Pause/Resume · Manual fixed speed… · Help · About… · Exit.
- [ ] **Step 4:** Build (Windows); open About; click feedback → browser opens the contact form. Commit `feat(ui): About dialog with feedback link to bitworks.io contact form`.

### Task 4.2: Preferences — min/max, GPU list, feedback link, live readout
**Files:** Modify `Forms/PreferencesForm.cs`.

- [ ] **Step 1:** Add controls (exact): relabel target to `"Core GPU temperature (°C):"`; `NumericUpDown _minFan` (Min `AppSettings.MinFanFloor`=20, Max 70) and `_maxFan` (Min 40, Max 85); a `CheckedListBox _gpuList` populated from the detected GPUs (display `AdapterName`, tag `GpuId`, checked = enabled); a live-status `Label _status` updated by a `System.Windows.Forms.Timer` (1 s) reading the primary backend's `GetTemperatureC()`/`GetFanTelemetry()`; a `LinkLabel` "Send feedback / request a feature" → `AppLinks.Open(AppLinks.ContactFormUrl)` with the encouraging line; an "About…" button → `AboutForm`. Grow the form; group fan controls vs. feedback visually.
- [ ] **Step 2:** `OkButton_Click`: persist `TargetTempC`, `MinFanPercent`, `MaxFanPercent`, and the `Gpus` enabled flags from `_gpuList`; enforce `min<max` (show a message + block close on violation). Dispose the status timer on `FormClosed`.
- [ ] **Step 3:** Pass the running backends/service into `PreferencesForm` (constructor) so the GPU list and live readout reflect reality; changing enabled GPUs re-reconciles channels in `FanControlService` (call a `service.ApplyGpuConfig(settings)` method). Build + manual check on hardware. Commit `feat(ui): configurable min/max, GPU selection, live readout, feedback link`.

### Task 4.3: Tray pause/manual + tooltip
**Files:** Modify `SystrayApplicationContext.cs`, `Services/FanControlService.cs`.

- [ ] **Step 1:** Add menu items **Pause/Resume** (toggles `settings.Mode`/a paused flag; paused → `service` relinquishes to auto and stops writing) and **"Manual fixed speed…"** (a tiny prompt/NumericUpDown dialog; sets `Mode=Manual`, `ManualFanPercent`, persists). Update the checkmark/label to reflect current mode.
- [ ] **Step 2:** A 2.5 s tray tooltip update (reuse the poll or a light timer): `_notifyIcon.Text = $"AMD Fan: {temp:0}°C, {fan}%"` (truncate to 63 chars). Build + manual check. Commit `feat(ui): tray pause/resume, manual speed, and live tooltip`.

**Phase 4 acceptance:** feedback link opens `https://bitworks.io/contact-us/` from both Preferences and About; live readout updates; pause and manual work; min/max and GPU enablement persist and take effect.

---

## Phase 5 — Release verification (hardware-gated, do not skip)

Not code — the acceptance gate. Perform on a real Windows PC with a supported AMD Radeon GPU.

- [ ] Install the CI-built installer on a clean machine; confirm it launches with **no** missing-DLL error and the ADLX backend initializes (log shows "Selected ADLX backend").
- [ ] Set target 65 °C; load the GPU; confirm the fan ramps and holds core temp near target (±, given the ramp) and never exceeds 85 %.
- [ ] Trigger over-temp (lower `CriticalTempC` temporarily or heavy load) → fan jumps to 85 %; recovers with hysteresis.
- [ ] Exit the app → Adrenalin shows fan back on **automatic**. Repeat with Task Manager "End task" → confirm auto is reclaimed on next launch (documented residual risk).
- [ ] Confirm fan-set works **without** admin (validates the asInvoker assumption). If not, apply the Task 1.7 elevation fallback.
- [ ] Multi-GPU box (if available): only enabled GPUs are driven; enabling a second GPU takes effect.
- [ ] Feedback buttons open `https://bitworks.io/contact-us/`.
- [ ] Update `docs/implementation-review.md` / `docs/agent-handoff.md`: B1 fixed (with artifact evidence), elevation result, and any residual risks.

---

## Self-Review (author's check against the spec)

- **Spec coverage:** §3 restore-to-auto → Task 2.5; §4.1 policy → 2.4; §4.2 backend iface → 2.5; §4.3 multi-GPU → 3.1/3.2; §4.4 lifecycle → 2.6; §4.5 failure semantics → 2.4; §5 settings+migration → 2.2/2.3; §6.1–6.3 UI → 4.2/4.3; §6.4 feedback+About → 4.1/4.2; §7.1 B1 → 1.3; §7.2 B2 → 1.4; §7.3 B4 → 1.5; §7.4 B3 → 1.8; §7.5 B5 → 1.7 (+ hardware gate in Phase 5); §8 tests → 2.2/2.3/2.4/3.2; §3 CRT caveat → 1.2/1.6. **No gaps.**
- **Placeholder scan:** WinForms layout is specified by exact controls/handlers rather than transcribed line-by-line (deliberate, per plan preamble — executing builder has file context); all logic-bearing tasks carry concrete code. No "TBD/handle edge cases" left.
- **Type consistency:** `Decide`, `FanDecision`, `FanAction.{SetPercent,RelinquishToAuto}`, `PolicyState`, `RestoreAutomaticFanControl`, `RestoreAll`, `CreateAll`, `Reconcile`, `AppLinks.ContactFormUrl` used consistently across tasks.

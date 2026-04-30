<#
.SYNOPSIS
Builds Lightweight AMD GPU Fan Control on Windows.

.DESCRIPTION
This script is intended for a clean Windows 10/11 x64 build machine, a Windows
11 ARM VM running x64 tools under emulation, or GitHub Actions. Hardware fan
testing still requires a real Windows PC with a supported AMD Radeon GPU.
#>

param(
    [switch]$InstallPrerequisites,
    [switch]$SkipAdlxBindings,
    [switch]$SkipInstaller,
    [ValidateSet("Release", "Debug")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$Root = Split-Path -Parent $MyInvocation.MyCommand.Path

function Assert-Command {
    param(
        [Parameter(Mandatory = $true)][string]$Name,
        [Parameter(Mandatory = $true)][string]$InstallHint
    )

    if (-not (Get-Command $Name -ErrorAction SilentlyContinue)) {
        throw "$Name was not found. $InstallHint"
    }
}

function Install-WithWinget {
    param([Parameter(Mandatory = $true)][string]$Id)

    $winget = Get-Command winget -ErrorAction SilentlyContinue
    if (-not $winget) {
        throw "winget is not available. Install prerequisites manually; see docs\WINDOWS_BUILD.md."
    }

    & winget install --id $Id -e --source winget --accept-package-agreements --accept-source-agreements
    if ($LASTEXITCODE -ne 0) {
        throw "winget install failed for $Id"
    }
}

if ($InstallPrerequisites) {
    Write-Host "Installing prerequisites with winget where available..." -ForegroundColor Cyan
    Install-WithWinget "Microsoft.DotNet.SDK.8"
    Install-WithWinget "JRSoftware.InnoSetup"
    Install-WithWinget "SWIG.SWIG"
    Write-Host "Install Visual Studio 2022 Build Tools manually with '.NET desktop development' and 'Desktop development with C++' if msbuild is still missing." -ForegroundColor Yellow
}

Assert-Command "dotnet" "Install .NET 8 SDK."
if (-not $SkipAdlxBindings) {
    Assert-Command "msbuild" "Install Visual Studio 2022 or Build Tools with C++ desktop workload."
    Assert-Command "swig" "Install SWIG 4.x and add it to PATH."
}

Write-Host "Restoring submodules..." -ForegroundColor Cyan
& git submodule update --init --recursive
if ($LASTEXITCODE -ne 0) { throw "Submodule restore failed" }

if (-not $SkipAdlxBindings) {
    Write-Host "Step 1: Building ADLX C# bindings..." -ForegroundColor Cyan
    Push-Location "$Root\external\ADLX\Samples\csharp"
    try {
        & msbuild csharp.sln -p:Configuration=$Configuration -p:Platform=x64 -v:m
        if ($LASTEXITCODE -ne 0) { throw "ADLX build failed" }
    }
    finally {
        Pop-Location
    }
}

Write-Host "Step 2: Publishing application..." -ForegroundColor Cyan
& dotnet publish "$Root\LightweightAmdGpuFanControl\LightweightAmdGpuFanControl.csproj" -c $Configuration -r win-x64 --self-contained false -v m
if ($LASTEXITCODE -ne 0) { throw "Publish failed" }

if (-not $SkipInstaller) {
    Write-Host "Step 3: Creating installer..." -ForegroundColor Cyan
    $iscc = Get-Command iscc -ErrorAction SilentlyContinue
    if (-not $iscc) {
        throw "Inno Setup (iscc) was not found. Install Inno Setup 6 or rerun with -SkipInstaller."
    }

    & iscc "$Root\installer\LightweightAmdGpuFanControl.iss"
    if ($LASTEXITCODE -ne 0) { throw "Installer build failed" }
    Write-Host "Installer: $Root\output\LightweightAmdGpuFanControl-Setup.exe" -ForegroundColor Green
}

Write-Host "Build complete." -ForegroundColor Green

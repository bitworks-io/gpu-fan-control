<#
.SYNOPSIS
Self-test for Run-Scenario.ps1's result building. No installs, users or tasks: it dot-sources the driver
(which defines functions only when dot-sourced) and builds results from in-memory steps.

Regression: run 42675308728 died in every scenario with "Argument types do not match" while building
result.json. Wrapping a System.Collections.Generic.List[object] in @() throws that error, so no scenario
ever wrote a verdict. Runs under Windows PowerShell 5.1 in CI and under pwsh locally. Exit 0 = pass.
#>
$ErrorActionPreference = 'Stop'
. (Join-Path $PSScriptRoot 'Run-Scenario.ps1')

$script:fails = 0
function Check([string]$Name, [bool]$Ok, [string]$Detail = '') {
    if ($Ok) { Write-Host "PASS $Name" } else { Write-Host "FAIL $Name $Detail"; $script:fails++ }
}

function New-Steps([int]$Count) {
    $s = New-Object System.Collections.Generic.List[object]
    for ($i = 1; $i -le $Count; $i++) {
        $s.Add([pscustomobject]@{ kind = 'install'; installer = 'test'; exitCode = 0; outcome = 'success'; windowTexts = @() })
    }
    , $s
}
function New-Failures([string[]]$Items) {
    $f = New-Object System.Collections.Generic.List[string]
    foreach ($x in $Items) { $f.Add($x) }
    , $f
}

$def = @{ Steps = @('test'); Assert = $true; Note = 'control' }
$batch = [pscustomobject]@{ user = 'reproS1'; grantedDirect = $false }
$cases = @(
    @{ name = 'ok, 2 steps';          steps = 2; failures = @();            assert = $true;  verdict = 'OK' }
    @{ name = 'asserted failure';     steps = 1; failures = @('x');         assert = $true;  verdict = 'FAIL' }
    @{ name = 'reported anomaly';     steps = 2; failures = @('x', 'y');    assert = $false; verdict = 'REPORTED-ANOMALY' }
    @{ name = 'no steps (skipped)';   steps = 0; failures = @('install');   assert = $false; verdict = 'REPORTED-ANOMALY' }
)
foreach ($c in $cases) {
    $steps = New-Steps $c.steps
    $failures = New-Failures $c.failures
    try {
        $r = New-ScenarioResult -Scenario 'S1' -Variant '' -Def $def -Assert $c.assert -Batch $batch `
            -Failures $failures -SkipReason '' -Steps $steps
        Check "$($c.name): builds" $true
        Check "$($c.name): verdict" ($r.verdict -eq $c.verdict) "got '$($r.verdict)'"
        Check "$($c.name): steps is an array of $($c.steps)" (($r.steps -is [array]) -and ($r.steps.Count -eq $c.steps)) "got $($r.steps.GetType().FullName) count $($r.steps.Count)"
        Check "$($c.name): failures kept" (@($r.failures).Count -eq $c.failures.Count)
        $json = $r | ConvertTo-Json -Depth 8
        Check "$($c.name): serializes" ($json -match '"verdict"')
    } catch {
        Check "$($c.name): builds" $false $_.Exception.Message
    }
}

# ---------------------------------------------------------------------------------------------
# Get-UninstallRunExits, Get-StopOutcome, Test-ExpectedV102Path (S5b / S6 assertions)
# ---------------------------------------------------------------------------------------------
# Excerpts of real Inno uninstall logs from CI throwaway users (S2 complete; S5 hung in --stop).
$s2Log = @'
2026-11-10 10:46:44.650   Starting the uninstallation process.
2026-11-10 10:46:44.650   Running Exec filename: C:\Users\reproS2\AppData\Local\Programs\Bitworks\Lightweight AMD GPU Fan Control\LightweightAmdGpuFanControl.exe
2026-11-10 10:46:44.650   Running Exec parameters: --stop
2026-11-10 10:46:44.706   Process exit code: 3
2026-11-10 10:46:44.708   Running Exec filename: C:\Windows\system32\cmd.exe
2026-11-10 10:46:44.708   Running Exec parameters: /C taskkill /F /IM LightweightAmdGpuFanControl.exe
2026-11-10 10:46:44.775   Process exit code: 128
2026-11-10 10:46:44.775   Running Exec filename: C:\Users\reproS2\AppData\Local\Programs\Bitworks\Lightweight AMD GPU Fan Control\LightweightAmdGpuFanControl.exe
2026-11-10 10:46:44.775   Running Exec parameters: --restore-auto
2026-11-10 10:46:44.820   Process exit code: 3
2026-11-10 10:46:44.820   Skipping RunOnceId "StopApp" filename: C:\Users\reproS2\AppData\Local\Programs\Bitworks\Lightweight AMD GPU Fan Control\LightweightAmdGpuFanControl.exe
'@
$s5Log = @'
2026-11-10 10:50:23.574   Starting the uninstallation process.
2026-11-10 10:50:23.574   Running Exec filename: C:\Users\reproS5\AppData\Local\Programs\Bitworks\Lightweight AMD GPU Fan Control\LightweightAmdGpuFanControl.exe
2026-11-10 10:50:23.574   Running Exec parameters: --stop
'@

function New-Exit([string]$Params, $Code) { [pscustomobject]@{ params = $Params; exitCode = $Code } }
$kill = '/C taskkill /F /IM LightweightAmdGpuFanControl.exe'
$exitsS5b = @((New-Exit '--stop' 3), (New-Exit $kill 128), (New-Exit '--restore-auto' 3))
$exitsS6 = @((New-Exit '--stop' 1), (New-Exit $kill 0), (New-Exit '--restore-auto' 3))

$e = @(Get-UninstallRunExits -Text $s2Log)
Check 'exits: S2 fixture parses to three entries' ($e.Count -eq 3) "got $($e.Count)"
Check 'exits: S2 fixture exit codes 3/128/3' ((($e | ForEach-Object { $_.exitCode }) -join ',') -eq '3,128,3')
Check 'exits: S2 fixture params in order' ((($e | ForEach-Object { $_.params }) -join '|') -eq "--stop|$kill|--restore-auto")
$e = @(Get-UninstallRunExits -Text $s5Log)
Check 'exits: S5 fixture is one --stop entry' (($e.Count -eq 1) -and ($e[0].params -eq '--stop')) "got $($e.Count)"
Check 'exits: S5 fixture --stop has a null exit code' ($e.Count -eq 1 -and $null -eq $e[0].exitCode)
Check 'exits: empty text gives no entries' (@(Get-UninstallRunExits -Text '').Count -eq 0)

$pfx = '2026-11-10 10:46:44 | '
$stopCases = @(
    @{ line = 'stop: not running'; want = 'not-running' }
    @{ line = 'stop: a running instance has no stop listener (older build)'; want = 'no-listener' }
    @{ line = 'stop: stopped'; want = 'stopped' }
    @{ line = 'stop: timed out waiting for exit'; want = 'timeout' }
    @{ line = 'stop: unexpected error'; want = 'error' }
)
foreach ($c in $stopCases) {
    $got = Get-StopOutcome -Lines @(($pfx + 'tray started'), ($pfx + $c.line))
    Check "stop outcome: '$($c.line)' -> $($c.want)" ($got -eq $c.want) "got '$got'"
}
Check 'stop outcome: no stop line -> absent' ((Get-StopOutcome -Lines @(($pfx + 'tray started'))) -eq 'absent')
Check 'stop outcome: empty log -> absent' ((Get-StopOutcome -Lines @()) -eq 'absent')
Check 'stop outcome: two stop lines -> multiple' ((Get-StopOutcome -Lines @(($pfx + 'stop: stopped'), ($pfx + 'stop: not running'))) -eq 'multiple')
Check 'stop outcome: a line without the timestamp prefix is not a stop line' ((Get-StopOutcome -Lines @('stop: stopped')) -eq 'absent')

$unsup = $pfx + 'Unsupported arguments for 1.0.x: --restore-auto; exit 3'
$logS5b = @(($pfx + 'stop: not running'), $unsup)
$logS6 = @(($pfx + 'stop: a running instance has no stop listener (older build)'), $unsup)
function Invoke-V102([string]$Scenario, $Exits, [string[]]$AppLog, [int]$Code = 0, [double]$Wall = 30, [bool]$AppDir = $false,
    [int]$Left = 0, [string]$TrayOwner = 'HOST\reproS6', [int]$TraySession = 0, [string]$UnOwner = 'host\REPROS6', [int]$UnSession = 0) {
    @(Test-ExpectedV102Path -Scenario $Scenario -Exits $Exits -AppLog $AppLog -UninstallExitCode $Code -WallSeconds $Wall `
            -AppDirExists $AppDir -FanProcsLeft $Left -TrayOwner $TrayOwner -TraySession $TraySession `
            -UninstallOwner $UnOwner -UninstallSession $UnSession)
}
$f = Invoke-V102 'S5b' $exitsS5b $logS5b
Check 'v102: good S5b passes' ($f.Count -eq 0) ($f -join '; ')
$f = Invoke-V102 'S6' $exitsS6 $logS6
Check 'v102: good S6 passes' ($f.Count -eq 0) ($f -join '; ')
foreach ($sc in @('S5b', 'S6')) {
    $ex = $(if ($sc -eq 'S5b') { $exitsS5b } else { $exitsS6 })
    $lg = $(if ($sc -eq 'S5b') { $logS5b } else { $logS6 })
    $f = Invoke-V102 $sc $ex $lg -Code 1
    Check "v102 ${sc}: uninstall exit 1 fails" ($f.Count -gt 0)
    $f = Invoke-V102 $sc $ex $lg -Wall 121
    Check "v102 ${sc}: wall 121 s fails" ($f.Count -gt 0)
    $f = Invoke-V102 $sc $ex $lg -AppDir $true
    Check "v102 ${sc}: app dir left fails" ($f.Count -gt 0)
    $f = Invoke-V102 $sc $ex $lg -Left 1
    Check "v102 ${sc}: leftover fan process fails" ($f.Count -gt 0)
    $f = Invoke-V102 $sc $ex[0..1] $lg
    Check "v102 ${sc}: two exits entries fails" ($f.Count -gt 0)
    $f = Invoke-V102 $sc @($ex[0], $ex[1], (New-Exit '--restore-auto' $null)) $lg
    Check "v102 ${sc}: missing exit code fails" ($f.Count -gt 0)
    $f = Invoke-V102 $sc $ex @(($pfx + 'stop: not running'), ($pfx + 'stop: a running instance has no stop listener (older build)'), $unsup)
    Check "v102 ${sc}: multiple stop lines fails" ($f.Count -gt 0)
    $f = Invoke-V102 $sc $ex @($lg[0])
    Check "v102 ${sc}: missing restore-auto line fails" ($f.Count -gt 0)
    $f = Invoke-V102 $sc $ex @(($pfx + 'stop: unexpected error'), $unsup)
    Check "v102 ${sc}: stop outcome error fails" ($f.Count -gt 0)
    $f = Invoke-V102 $sc $ex @($unsup)
    Check "v102 ${sc}: stop outcome absent fails" ($f.Count -gt 0)
}
$f = Invoke-V102 'S6' $exitsS6 @(($pfx + 'stop: unexpected error'), $unsup)
Check 'v102 S6: exits 1/0/3 but outcome error (access denied) fails' ($f.Count -gt 0)
$f = Invoke-V102 'S6' $exitsS6 $logS5b
Check 'v102 S6: outcome not-running (wrong for S6) fails' ($f.Count -gt 0)
$f = Invoke-V102 'S5b' $exitsS5b $logS6
Check 'v102 S5b: outcome no-listener (wrong for S5b) fails' ($f.Count -gt 0)
$f = Invoke-V102 'S6' $exitsS5b $logS6
Check 'v102 S6: S5b exit codes (3/128/3) fail' ($f.Count -gt 0)
$f = Invoke-V102 'S6' $exitsS6 $logS6 -UnOwner 'HOST\someoneelse'
Check 'v102 S6: owner mismatch fails' ($f.Count -gt 0)
$f = Invoke-V102 'S6' $exitsS6 $logS6 -UnSession 2
Check 'v102 S6: session mismatch fails' ($f.Count -gt 0)
$f = Invoke-V102 'S6' $exitsS6 $logS6 -TrayOwner '' -UnOwner ''
Check 'v102 S6: empty owners fail' ($f.Count -gt 0)
$f = Invoke-V102 'S5b' $exitsS5b $logS5b -TrayOwner '' -UnOwner 'x' -TraySession 0 -UnSession 5
Check 'v102 S5b: owner/session are not checked' ($f.Count -eq 0) ($f -join '; ')

# Scenario table: S5b and S6 exist, are asserted, use the v102 installer; S5 stays reported.
Check 'table: S5b asserted with test,v102' (($ScenarioTable.S5b.Assert -eq $true) -and (($ScenarioTable.S5b.Steps -join ',') -eq 'test,v102'))
Check 'table: S6 asserted, tray started' (($ScenarioTable.S6.Assert -eq $true) -and ($ScenarioTable.S6.StartTray -eq $true) -and (($ScenarioTable.S6.Steps -join ',') -eq 'test,v102'))
Check 'table: S5 stays reported' ($ScenarioTable.S5.Assert -eq $false)

# Uninstall log lines about a failed data-folder delete are captured as text.
$delLog = @'
2026-11-10 10:46:45.000   Deleting directory: C:\Users\reproS6\AppData\Local\Bitworks\LightweightAmdGpuFanControl
2026-11-10 10:46:45.001   Failed to delete directory (32). Will not retry.
2026-11-10 10:46:45.002   Some unrelated line
'@
$dl = @(Get-DataDirDeleteLines -Text $delLog)
Check 'data dir delete lines: failure captured' (($dl.Count -ge 1) -and (($dl -join "`n") -match 'Failed to delete'))
Check 'data dir delete lines: none in S2 fixture' (@(Get-DataDirDeleteLines -Text $s2Log).Count -eq 0)

if ($script:fails) { Write-Host "$script:fails check(s) failed"; exit 1 }
Write-Host 'all harness self-tests passed'
exit 0

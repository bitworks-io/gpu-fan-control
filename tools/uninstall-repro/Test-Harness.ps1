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
if ($script:fails) { Write-Host "$script:fails check(s) failed"; exit 1 }
Write-Host 'all harness self-tests passed'
exit 0

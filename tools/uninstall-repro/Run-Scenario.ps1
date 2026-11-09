<#
.SYNOPSIS
Per-scenario driver for the silent-uninstall repro (see .github/workflows/uninstall-repro.yml).

.DESCRIPTION
Modes:
  Scenario  (default) Create a fresh local standard user, run the scenario's installs and the final
            silent uninstall as that user (non-elevated scheduled task), capture evidence, write
            result.json and a GitHub step summary section. Run from the elevated job context.
  Wrapper   Internal. Runs inside the scheduled task as the standard user: starts one installer or
            uninstaller, waits for it, and writes its exit code and wall time to a result file. Also
            polls window text from inside the task's own window station/desktop, which is the only
            place a suppressed dialog of a task-launched process is visible.
  Cleanup   Kill any process owned by a repro user, unregister the repro tasks and remove the users.

Everything here must run under Windows PowerShell 5.1 (the wrapper is launched with powershell.exe).
Scenarios S1 and S4 are asserted; S2, S3 and S5 are reported only (and any -GrantBatchLogon run is reported only).
#>
[CmdletBinding()]
param(
    [ValidateSet('Scenario', 'Wrapper', 'Cleanup')]
    [string]$Mode = 'Scenario',
    [ValidateSet('S1', 'S2', 'S3', 'S4', 'S5')]
    [string]$Scenario,
    [string]$TestInstaller,
    [string]$V100Installer,
    [string]$V101Installer,
    [string]$WorkRoot = 'C:\repro-work',
    [int]$TimeoutSec = 300,
    [string]$ParamFile,
    # Opt-in diagnostic variant: grant SeBatchLogonRight to this scenario's repro user only (throwaway runner),
    # via secedit /configure. Default is diagnose only (the right is reported, never changed). A grant run
    # uses its own user and output folder and is never asserted.
    [switch]$GrantBatchLogon
)

$ErrorActionPreference = 'Stop'

$AppFolderName = 'Lightweight AMD GPU Fan Control'
$DataRelPath = 'Bitworks\LightweightAmdGpuFanControl'
$UserPrefix = 'repro'

# Scenario table: installer history before the uninstall (keys: test, v100, v101) and whether it asserts.
$ScenarioTable = @{
    S1 = @{ Steps = @('test'); Assert = $true; Note = 'control: test build only' }
    S2 = @{ Steps = @('test', 'test'); Assert = $false; Note = 'same-version over-install' }
    S3 = @{ Steps = @('v100', 'v101', 'test', 'test'); Assert = $false; Note = 'the bench history (the experiment)' }
    S4 = @{ Steps = @('v100', 'v101'); Assert = $true; Note = 'release-only control' }
    S5 = @{ Steps = @('test', 'v101'); Assert = $false; Note = 'cleanup over-install: test build then v1.0.1' }
}

# ---------------------------------------------------------------------------------------------
# Window text capture (EnumWindows / EnumChildWindows + WM_GETTEXT with a timeout)
# ---------------------------------------------------------------------------------------------
function Initialize-WindowApi {
    if ('ReproWin' -as [type]) { return }
    Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class ReproWin
{
    delegate bool EnumProc(IntPtr hWnd, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassName(IntPtr hWnd, StringBuilder sb, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern IntPtr SendMessageTimeout(IntPtr hWnd, uint msg, IntPtr wParam, StringBuilder lParam,
        uint flags, uint timeoutMs, out IntPtr result);

    const uint WM_GETTEXT = 0x000D;
    const uint SMTO_ABORTIFHUNG = 0x0002;

    static string Text(IntPtr h)
    {
        StringBuilder sb = new StringBuilder(1024);
        IntPtr res;
        // WM_GETTEXT (not GetWindowText) so the text of another process's controls is returned.
        SendMessageTimeout(h, WM_GETTEXT, (IntPtr)sb.Capacity, sb, SMTO_ABORTIFHUNG, 300, out res);
        return sb.ToString();
    }

    static string Cls(IntPtr h)
    {
        StringBuilder sb = new StringBuilder(256);
        GetClassName(h, sb, sb.Capacity);
        return sb.ToString();
    }

    // Each row: pid, "top"|"child", visible("1"/"0"), class, text
    public static List<string[]> Snapshot()
    {
        List<string[]> rows = new List<string[]>();
        EnumWindows(delegate (IntPtr top, IntPtr l1)
        {
            uint pid;
            GetWindowThreadProcessId(top, out pid);
            rows.Add(new string[] { pid.ToString(), "top", IsWindowVisible(top) ? "1" : "0", Cls(top), Text(top) });
            EnumChildWindows(top, delegate (IntPtr child, IntPtr l2)
            {
                string t = Text(child);
                if (t.Length > 0)
                    rows.Add(new string[] { pid.ToString(), "child", IsWindowVisible(child) ? "1" : "0", Cls(child), t });
                return true;
            }, IntPtr.Zero);
            return true;
        }, IntPtr.Zero);
        return rows;
    }
}
'@
}

function Get-WindowTexts {
    <# Top-level window titles and child control text. -ProcessIds limits to those pids (null = every
       window on the calling desktop). Windows with an empty title and no text children are dropped. #>
    [CmdletBinding()]
    param([int[]]$ProcessIds)
    Initialize-WindowApi
    $rows = [ReproWin]::Snapshot()
    $out = New-Object System.Collections.Generic.List[object]
    foreach ($r in $rows) {
        $wpid = [int]$r[0]
        if ($ProcessIds -and ($ProcessIds -notcontains $wpid)) { continue }
        if ($r[1] -eq 'top' -and [string]::IsNullOrEmpty($r[4]) -and $r[3] -notlike '#32770') { continue }
        $out.Add([pscustomobject]@{ pid = $wpid; kind = $r[1]; visible = ($r[2] -eq '1'); class = $r[3]; text = $r[4] })
    }
    # Plain enumeration (no leading comma): callers wrap in @(), and a unary comma here nested the rows one
    # level deeper in every poll row (windows: [[...]]).
    $out.ToArray()
}

# ---------------------------------------------------------------------------------------------
# Wrapper mode: runs as the standard user inside the scheduled task
# ---------------------------------------------------------------------------------------------
function Invoke-WrapperMode {
    param([Parameter(Mandatory)][string]$ParamFile)
    $p = $null
    try {
        $p = Get-Content -Raw -LiteralPath $ParamFile | ConvertFrom-Json
        $sw = [Diagnostics.Stopwatch]::StartNew()
        $id = [Security.Principal.WindowsIdentity]::GetCurrent()
        $isAdmin = ([Security.Principal.WindowsPrincipal]$id).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
        $ctx = [ordered]@{
            status = 'started'; user = $id.Name; isAdminRole = $isAdmin; wrapperPid = $PID
            localAppData = $env:LOCALAPPDATA; temp = $env:TEMP
        }
        ($ctx | ConvertTo-Json) | Set-Content -LiteralPath ($p.ResultFile + '.started') -Encoding UTF8

        $proc = Start-Process -FilePath $p.Exe -ArgumentList $p.ArgString -PassThru
        $null = $proc.Handle   # keep the handle so ExitCode is readable
        $lastSig = ''
        while (-not $proc.WaitForExit(1000)) {
            try {
                $w = @(Get-WindowTexts | Where-Object { $_.text -or $_.kind -eq 'top' })
                $sig = ($w | ForEach-Object { '{0}|{1}|{2}|{3}' -f $_.pid, $_.kind, $_.class, $_.text }) -join "`n"
                if ($sig -ne $lastSig) {
                    $lastSig = $sig
                    $line = [ordered]@{ t = [math]::Round($sw.Elapsed.TotalSeconds, 1); windows = $w }
                    ($line | ConvertTo-Json -Compress -Depth 5) | Add-Content -LiteralPath $p.WindowFile -Encoding UTF8
                }
            } catch {
                ("poll error: " + $_.Exception.Message) | Add-Content -LiteralPath ($p.WindowFile + '.err') -Encoding UTF8
            }
        }
        $code = $proc.ExitCode
        if ($null -eq $code) { $code = -1 }
        $ctx.status = 'completed'
        $ctx.exitCode = $code
        $ctx.wallSeconds = [math]::Round($sw.Elapsed.TotalSeconds, 1)
        ($ctx | ConvertTo-Json) | Set-Content -LiteralPath $p.ResultFile -Encoding UTF8
    } catch {
        # A throw (for example Start-Process failing) is never silent: write an error result the driver reads.
        $e = $_
        $errBase = $(if ($p -and $p.ResultFile) { $p.ResultFile } else { $ParamFile })
        try {
            if ($p -and $p.ResultFile) {
                ([ordered]@{ status = 'wrapper-error'; message = $e.Exception.Message; wrapperPid = $PID } | ConvertTo-Json) |
                    Set-Content -LiteralPath $p.ResultFile -Encoding UTF8
            }
        } catch { }
        try { ($e | Out-String) | Set-Content -LiteralPath ($errBase + '.err') -Encoding UTF8 } catch { }
        exit 1
    }
    exit $code
}

# ---------------------------------------------------------------------------------------------
# Driver helpers (elevated job context)
# ---------------------------------------------------------------------------------------------
function Write-Json {
    param($Object, [string]$Path)
    ($Object | ConvertTo-Json -Depth 8) | Set-Content -LiteralPath $Path -Encoding UTF8
}

function Initialize-WorkRoot {
    New-Item -ItemType Directory -Force -Path $WorkRoot, "$WorkRoot\inst", "$WorkRoot\out" | Out-Null
    # Every repro user needs to read the installers/script and write logs and result files here.
    & icacls.exe $WorkRoot /grant '*S-1-5-32-545:(OI)(CI)M' | Out-Null
    Copy-Item -LiteralPath $PSCommandPath -Destination "$WorkRoot\Run-Scenario.ps1" -Force
}

function New-ReproPassword {
    $sets = @('ABCDEFGHJKLMNPQRSTUVWXYZ', 'abcdefghijkmnopqrstuvwxyz', '23456789', '+-=_')
    # No '%', quotes or backslashes: the set must pass through ::add-mask:: and command lines unchanged.
    $rng = New-Object System.Security.Cryptography.RNGCryptoServiceProvider
    function Pick([string]$chars) {
        $b = New-Object byte[] 4
        $rng.GetBytes($b)
        return $chars[[int]([BitConverter]::ToUInt32($b, 0) % $chars.Length)]
    }
    $chars = New-Object System.Collections.Generic.List[char]
    foreach ($s in $sets) { $chars.Add((Pick $s)) }
    $all = $sets -join ''
    while ($chars.Count -lt 28) { $chars.Add((Pick $all)) }
    # Shuffle (Fisher-Yates with RNG bytes).
    for ($i = $chars.Count - 1; $i -gt 0; $i--) {
        $b = New-Object byte[] 4
        $rng.GetBytes($b)
        $j = [int]([BitConverter]::ToUInt32($b, 0) % ($i + 1))
        $t = $chars[$i]; $chars[$i] = $chars[$j]; $chars[$j] = $t
    }
    return (-join $chars)
}

function New-ReproUser {
    <# Creates a local standard user (member of Users only) with a random password that is masked in the
       job log and never printed. Returns the name and a PSCredential. #>
    param([Parameter(Mandatory)][string]$Name)
    $plain = New-ReproPassword
    Write-Host "::add-mask::$plain"
    $secure = ConvertTo-SecureString -String $plain -AsPlainText -Force
    $plain = $null
    if (Get-LocalUser -Name $Name -ErrorAction SilentlyContinue) { Remove-LocalUser -Name $Name }
    New-LocalUser -Name $Name -Password $secure -PasswordNeverExpires -UserMayNotChangePassword -AccountNeverExpires `
        -Description 'uninstall repro (temporary)' | Out-Null
    Add-LocalGroupMember -SID 'S-1-5-32-545' -Member $Name -ErrorAction SilentlyContinue
    $cred = New-Object System.Management.Automation.PSCredential("$env:COMPUTERNAME\$Name", $secure)
    return [pscustomobject]@{ Name = $Name; Credential = $cred }
}

function Export-UserRights {
    <# Read-only. secedit /export of the user-rights area to a temp INF, parsed to a hashtable
       right name -> entries (SID strings without the leading '*', or account names). $null if unavailable. #>
    $ErrorActionPreference = 'Continue'
    $tmp = Join-Path $env:TEMP ('repro-rights-{0}.inf' -f [guid]::NewGuid().ToString('N'))
    try {
        $null = & secedit.exe /export /cfg $tmp /areas USER_RIGHTS 2>&1
        if (-not (Test-Path -LiteralPath $tmp)) { return $null }
        $rights = @{}
        foreach ($line in (Get-Content -LiteralPath $tmp)) {
            if ($line -match '^\s*(Se\w+)\s*=\s*(.*)$') {
                $rights[$Matches[1]] = @($Matches[2].Split(',') | ForEach-Object { $_.Trim().TrimStart('*') } | Where-Object { $_ })
            }
        }
        return $rights
    } catch {
        return $null
    } finally {
        Remove-Item -LiteralPath $tmp -Force -ErrorAction SilentlyContinue
    }
}

function Get-BatchLogonReport {
    <# Read-only: does the user hold SeBatchLogonRight (directly, or through the well-known groups the user
       belongs to: Users, Everyone, Authenticated Users) and is it denied (SeDenyBatchLogonRight)? #>
    param([Parameter(Mandatory)][string]$UserName)
    $r = [ordered]@{ user = $UserName; grantedDirect = $null; grantedViaGroup = $null; denied = $null; entries = ''; note = '' }
    try {
        $sid = (Get-LocalUser -Name $UserName -ErrorAction Stop).SID.Value
        $rights = Export-UserRights
        if ($null -eq $rights) { $r.note = 'secedit export unavailable'; return [pscustomobject]$r }
        $grant = @($rights['SeBatchLogonRight'] | Where-Object { $_ })
        $deny = @($rights['SeDenyBatchLogonRight'] | Where-Object { $_ })
        $groups = @('S-1-5-32-545', 'S-1-1-0', 'S-1-5-11')
        $r.grantedDirect = (($grant -contains $sid) -or ($grant -contains $UserName))
        $r.grantedViaGroup = (@($grant | Where-Object { $groups -contains $_ }).Count -gt 0)
        $r.denied = (($deny -contains $sid) -or ($deny -contains $UserName) -or (@($deny | Where-Object { $groups -contains $_ }).Count -gt 0))
        $r.entries = ($grant -join ',')
    } catch {
        $r.note = 'report failed: ' + $_.Exception.Message
    }
    [pscustomobject]$r
}

function Format-BatchLogonReport {
    param($Report)
    if (-not $Report) { return 'SeBatchLogonRight: not checked' }
    $s = 'SeBatchLogonRight for {0}: direct={1} via-group={2} denied={3}; right holders: {4}' -f $Report.user, $Report.grantedDirect, $Report.grantedViaGroup, $Report.denied, $Report.entries
    if ($Report.note) { $s += ' (' + $Report.note + ')' }
    $s
}

function Grant-BatchLogonRight {
    <# Opt-in (-GrantBatchLogon). Adds the repro user's SID to SeBatchLogonRight via secedit /configure with a
       temp INF that lists the existing holders plus this one, so no other holder is dropped and no other right
       is touched. Throwaway runner only. #>
    param([Parameter(Mandatory)][string]$UserName)
    $ErrorActionPreference = 'Continue'
    $sid = (Get-LocalUser -Name $UserName -ErrorAction Stop).SID.Value
    $rights = Export-UserRights
    if ($null -eq $rights) { throw 'cannot read the current user rights (secedit export failed)' }
    $cur = @($rights['SeBatchLogonRight'] | Where-Object { $_ })
    if ($cur -contains $sid) { return 'SeBatchLogonRight already held by the user' }
    $entries = @(($cur | ForEach-Object { if ($_ -match '^S-1-') { '*' + $_ } else { $_ } }) + ('*' + $sid))
    $base = Join-Path $env:TEMP ('repro-grant-{0}' -f [guid]::NewGuid().ToString('N'))
    $inf = "$base.inf"; $sdb = "$base.sdb"
    try {
        @('[Unicode]', 'Unicode=yes', '[Version]', 'signature="$CHICAGO$"', 'Revision=1', '[Privilege Rights]',
            ('SeBatchLogonRight = ' + ($entries -join ','))) | Set-Content -LiteralPath $inf -Encoding Unicode
        $out = (& secedit.exe /configure /db $sdb /cfg $inf /areas USER_RIGHTS /quiet 2>&1 | Out-String).Trim()
        if ($LASTEXITCODE -ne 0) { throw "secedit /configure exit $LASTEXITCODE : $out" }
    } finally {
        Remove-Item -LiteralPath $inf, $sdb, "$base.jfm", "$base.log" -Force -ErrorAction SilentlyContinue
    }
    'granted SeBatchLogonRight to ' + $UserName
}

function Get-TaskLaunchDiag {
    <# Read-only, best effort: Task Scheduler operational log entries for the task and Security log logon
       events (4624/4625) for the user since $Since. Every source is skipped with a one-line reason if the
       runner does not allow it or the log is disabled. Returns lines of text. #>
    param([string]$TaskName, [string]$UserName, [datetime]$Since)
    $lines = New-Object System.Collections.Generic.List[string]
    try {
        $ln = 'Microsoft-Windows-TaskScheduler/Operational'
        if (-not (Get-WinEvent -ListLog $ln -ErrorAction Stop).IsEnabled) {
            $lines.Add('TaskScheduler/Operational log is disabled: no task events recorded')
        } else {
            $ev = @(Get-WinEvent -FilterHashtable @{ LogName = $ln; StartTime = $Since } -ErrorAction Stop |
                    Where-Object { $_.Message -like "*$TaskName*" } | Sort-Object TimeCreated | Select-Object -First 15)
            if ($ev.Count -eq 0) { $lines.Add('TaskScheduler/Operational: no events for this task') }
            foreach ($e in $ev) { $lines.Add(('TaskScheduler {0:HH:mm:ss} id {1}: {2}' -f $e.TimeCreated, $e.Id, (($e.Message -split "\r?\n")[0]))) }
        }
    } catch {
        $lines.Add('TaskScheduler/Operational: ' + $(if ($_.Exception.Message -match 'No events were found') { 'no events for this task' } else { 'skipped (' + $_.Exception.Message + ')' }))
    }
    try {
        $pat = 'Account Name:\s+' + [regex]::Escape($UserName) + '\b'
        $ev = @(Get-WinEvent -FilterHashtable @{ LogName = 'Security'; Id = 4624, 4625; StartTime = $Since } -ErrorAction Stop |
                Where-Object { $_.Message -match $pat } | Sort-Object TimeCreated | Select-Object -First 15)
        if ($ev.Count -eq 0) { $lines.Add('Security: no 4624/4625 logon events for the user (auditing may be off)') }
        foreach ($e in $ev) {
            $m = $e.Message
            $lt = $(if ($m -match 'Logon Type:\s+(\d+)') { $Matches[1] } else { '?' })
            $st = $(if ($m -match '\sStatus:\s+(0x[0-9A-Fa-f]+)') { $Matches[1] } else { '' })
            $ss = $(if ($m -match 'Sub Status:\s+(0x[0-9A-Fa-f]+)') { $Matches[1] } else { '' })
            $lines.Add(('Security {0:HH:mm:ss} id {1} logon type {2} status {3} substatus {4}' -f $e.TimeCreated, $e.Id, $lt, $st, $ss))
        }
    } catch {
        $lines.Add('Security: ' + $(if ($_.Exception.Message -match 'No events were found') { 'no 4624/4625 logon events for the user (auditing may be off)' } else { 'skipped (' + $_.Exception.Message + ')' }))
    }
    return $lines.ToArray()
}

function Invoke-AsUser {
    <# Starts one installer/uninstaller as the standard user via the wrapper. Preferred method: a scheduled
       task registered with the user's password (logon type Password, run level Limited). If registration
       or start fails, or the wrapper never reports started within 120 s (first-logon profile creation can
       be slow), falls back to Start-Process -Credential, but only after the task is stopped and no process
       of the user is running the target. Each method uses its own result file, so two targets can never
       write the same one. Returns a handle for Watch-Step. #>
    param(
        [Parameter(Mandatory)]$User,
        [Parameter(Mandatory)][string]$Tag,
        [Parameter(Mandatory)][string]$Exe,
        [Parameter(Mandatory)][string]$ArgString,
        [Parameter(Mandatory)][string]$StepDir
    )
    New-Item -ItemType Directory -Force -Path $StepDir | Out-Null

    function New-Attempt {
        param([string]$Method)
        $resultFile = Join-Path $StepDir "$Tag.$Method.result.json"
        $windowFile = Join-Path $StepDir "$Tag.$Method.windows-in-task.jsonl"
        $paramFile = Join-Path $StepDir "$Tag.$Method.params.json"
        Write-Json ([ordered]@{ Exe = $Exe; ArgString = $ArgString; ResultFile = $resultFile; WindowFile = $windowFile }) $paramFile
        $psArgs = '-NoProfile -NonInteractive -ExecutionPolicy Bypass -File "{0}\Run-Scenario.ps1" -Mode Wrapper -ParamFile "{1}"' -f $WorkRoot, $paramFile
        @{ ResultFile = $resultFile; WindowFile = $windowFile; ParamFile = $paramFile; PsArgs = $psArgs }
    }

    $handle = [ordered]@{
        Tag = $Tag; Method = ''; TaskName = ''; Process = $null; Exe = $Exe; StartError = ''
        ResultFile = ''; WindowFile = ''; StartedFile = ''; ErrFiles = @(); Notes = @()
    }

    $taskName = "repro-$($User.Name)-$Tag"
    $taskT0 = (Get-Date).AddSeconds(-5)
    try {
        $a = New-Attempt 'task'
        $handle.ResultFile = $a.ResultFile; $handle.WindowFile = $a.WindowFile
        $handle.StartedFile = $a.ResultFile + '.started'
        $handle.ErrFiles = @(($a.ResultFile + '.err'), ($a.ParamFile + '.err'))
        $action = New-ScheduledTaskAction -Execute 'powershell.exe' -Argument $a.PsArgs
        $settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
            -ExecutionTimeLimit (New-TimeSpan -Hours 3)
        $pw = $User.Credential.GetNetworkCredential().Password
        # -User/-Password selects logon type Password; -RunLevel Limited = standard (non-elevated) token.
        Register-ScheduledTask -TaskName $taskName -Action $action -Settings $settings `
            -User $User.Credential.UserName -Password $pw -RunLevel Limited -Force | Out-Null
        $pw = $null
        Start-ScheduledTask -TaskName $taskName
        $handle.Method = 'ScheduledTask'
        $handle.TaskName = $taskName
        # The wrapper writes <result>.started immediately; the first logon also creates the profile, so allow 120 s.
        # While waiting, record the task's State and LastTaskResult (on change). If the task is no longer
        # Running/Queued and carries a failure HRESULT (or never wrote the started file in 20 s), it cannot
        # start: stop waiting and fall back now instead of after the full 120 s.
        $deadline = (Get-Date).AddSeconds(120)
        $wsw = [Diagnostics.Stopwatch]::StartNew()
        $lastSig = ''
        $cannotStart = ''
        while (-not (Test-Path -LiteralPath $handle.StartedFile) -and (Get-Date) -lt $deadline) {
            $ti = Get-ScheduledTaskInfo -TaskName $taskName -ErrorAction SilentlyContinue
            $ts = [string](Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue).State
            if ($ti) {
                $lr = [uint32]$ti.LastTaskResult
                $sig = '{0}|{1}' -f $ts, $lr
                if ($sig -ne $lastSig) {
                    $lastSig = $sig
                    $handle.Notes += ('task t+{0:N1}s state {1} LastTaskResult 0x{2:X8}' -f $wsw.Elapsed.TotalSeconds, $ts, $lr)
                }
                $idle = ($ts -ne 'Running' -and $ts -ne 'Queued')
                if ($idle -and $lr -ge 2147483648 -and $wsw.Elapsed.TotalSeconds -ge 3) { $cannotStart = ('LastTaskResult 0x{0:X8} with the task not running' -f $lr); break }
                if ($idle -and $wsw.Elapsed.TotalSeconds -ge 20) { $cannotStart = ('task state {0} (LastTaskResult 0x{1:X8}) and no wrapper after 20 s' -f $ts, $lr); break }
            }
            Start-Sleep -Milliseconds 500
        }
        if (-not (Test-Path -LiteralPath $handle.StartedFile)) {
            $info = Get-ScheduledTaskInfo -TaskName $taskName
            if ($cannotStart) { $handle.Notes += ('task cannot start: ' + $cannotStart + '; falling back now') }
            else { $handle.Notes += ('task did not start the wrapper in 120 s (LastTaskResult 0x{0:X8})' -f [uint32]$info.LastTaskResult) }
            $diag = @(Get-TaskLaunchDiag -TaskName $taskName -UserName $User.Name -Since $taskT0)
            foreach ($d in $diag) { Write-Host "  diag [$Tag] $d"; $handle.Notes += ('diag: ' + $d) }
            try { $diag | Set-Content -LiteralPath (Join-Path $StepDir "$Tag.task.diag.txt") -Encoding UTF8 } catch { }
            Stop-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
            for ($i = 0; $i -lt 10; $i++) {
                if ([string](Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue).State -ne 'Running') { break }
                Start-Sleep -Seconds 1
            }
            Start-Sleep -Seconds 2
            $running = @(Get-TargetProcesses -UserName $User.Name -Exe $Exe)
            if ($running.Count -gt 0) {
                # The task's wrapper did start a target after all. Never run a second one: keep watching this attempt.
                $handle.Notes += ('target still running after task stop (pids {0}); not falling back' -f (($running | ForEach-Object { $_.pid }) -join ','))
                return [pscustomobject]$handle
            }
            $handle.Notes += 'no target process of the user is running; falling back'
            throw 'wrapper not started'
        }
        return [pscustomobject]$handle
    } catch {
        $handle.Notes += ('scheduled-task path failed: ' + $_.Exception.Message)
        Unregister-ScheduledTask -TaskName $taskName -Confirm:$false -ErrorAction SilentlyContinue
        $handle.TaskName = ''
    }

    # Fallback: CreateProcessWithLogon via Start-Process -Credential (still a standard, non-elevated token).
    $a = New-Attempt 'cred'
    $handle.ResultFile = $a.ResultFile; $handle.WindowFile = $a.WindowFile
    $handle.StartedFile = $a.ResultFile + '.started'
    $handle.ErrFiles = @(($a.ResultFile + '.err'), ($a.ParamFile + '.err'))
    $handle.Method = 'StartProcessCredential'
    try {
        $handle.Process = Start-Process -FilePath 'powershell.exe' -ArgumentList $a.PsArgs -Credential $User.Credential `
            -LoadUserProfile -WorkingDirectory $WorkRoot -PassThru -WindowStyle Hidden
        $null = $handle.Process.Handle   # keep the handle so ExitCode is readable
    } catch {
        $handle.StartError = $_.Exception.Message
    }
    return [pscustomobject]$handle
}

function Get-UserProcesses {
    param([Parameter(Mandatory)][string]$UserName)
    $owned = @{}
    Get-Process -IncludeUserName -ErrorAction SilentlyContinue |
        Where-Object { $_.UserName -and ($_.UserName.Split('\')[-1] -eq $UserName) } |
        ForEach-Object { $owned[[int]$_.Id] = $true }
    if ($owned.Count -eq 0) { return @() }
    @(Get-CimInstance Win32_Process | Where-Object { $owned.ContainsKey([int]$_.ProcessId) } | ForEach-Object {
            [pscustomobject]@{ name = $_.Name; pid = [int]$_.ProcessId; ppid = [int]$_.ParentProcessId; cmd = $_.CommandLine }
        })
}

function Get-TempItems {
    param([string]$TempDir)
    if (-not $TempDir -or -not (Test-Path -LiteralPath $TempDir)) { return @() }
    @(Get-ChildItem -LiteralPath $TempDir -Force -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -like 'is-*' -or $_.Name -like '_iu*' } |
            ForEach-Object {
                $size = if ($_.PSIsContainer) { -1 } else { $_.Length }
                [pscustomobject]@{ name = $_.Name; dir = [bool]$_.PSIsContainer; size = $size }
            })
}

function Get-PostState {
    param([string]$LocalAppData)
    $app = Join-Path $LocalAppData "Programs\Bitworks\$AppFolderName"
    $data = Join-Path $LocalAppData $DataRelPath
    $dat = Join-Path $app 'unins000.dat'
    [ordered]@{
        appDir = $app
        appDirExists = (Test-Path -LiteralPath $app)
        dataDirExists = (Test-Path -LiteralPath $data)
        uninsDatBytes = $(if (Test-Path -LiteralPath $dat) { (Get-Item -LiteralPath $dat).Length } else { -1 })
        uninsExeExists = (Test-Path -LiteralPath (Join-Path $app 'unins000.exe'))
        appFiles = @($(if (Test-Path -LiteralPath $app) { Get-ChildItem -LiteralPath $app -Force | ForEach-Object Name }))
    }
}

function Get-TargetProcesses {
    <# Processes of the user whose command line carries the target exe path (installer or uninstaller and its
       self-copies/second phase), excluding the shells that host the wrapper. #>
    param([Parameter(Mandatory)][string]$UserName, [Parameter(Mandatory)][string]$Exe)
    @(Get-UserProcesses $UserName | Where-Object {
            $_.cmd -and $_.name -notmatch '^(powershell|pwsh|cmd|conhost)\.exe$' -and
            $_.cmd.IndexOf($Exe, [StringComparison]::OrdinalIgnoreCase) -ge 0
        })
}

function Get-ErrText {
    param([string[]]$Files)
    $parts = @()
    foreach ($f in $Files) {
        if ($f -and (Test-Path -LiteralPath $f)) {
            $t = (Get-Content -Raw -LiteralPath $f -ErrorAction SilentlyContinue)
            if ($t) { $parts += $t.Trim() }
        }
    }
    $s = ($parts -join ' | ')
    if ($s.Length -gt 1500) { $s = $s.Substring(0, 1500) + '...' }
    return $s
}

function Watch-Step {
    <# Waits (up to TimeoutSec) for the wrapper's result file and classifies the end of the step:
         completed       result file written by the wrapper (Outcome 'success' if the target exited 0,
                         otherwise 'exit-<code>')
         wrapper-failed  the wrapper is gone (task no longer Running / wrapper process exited / wrapper wrote a
                         wrapper-error result / could not be started) and left no result; Detail carries
                         LastTaskResult, the wrapper exit code and any .err content
         hung            a target process (installer or uninstaller) was still running at the cap
       With -Capture, polls once a second from this elevated context: the user's processes (name/pid/parent/
       command line), top-level window titles and control text for those pids, and new is-* / _iu* items in the
       user's TEMP. Poll rows are written only when something changed. #>
    param(
        [Parameter(Mandatory)]$Handle,
        [Parameter(Mandatory)][string]$UserName,
        [int]$TimeoutSec = 300,
        [switch]$Capture,
        [string]$TempDir,
        [string]$PollFile
    )
    $sw = [Diagnostics.Stopwatch]::StartNew()
    $baseline = @{}
    foreach ($i in (Get-TempItems $TempDir)) { $baseline[$i.name] = $true }
    $everNew = @{}
    $lastSig = ''
    $done = $false
    $wrapperGone = $false
    while ($sw.Elapsed.TotalSeconds -lt $TimeoutSec) {
        if (Test-Path -LiteralPath $Handle.ResultFile) { $done = $true; break }
        if ($Handle.StartError) { $wrapperGone = $true; break }
        $gone = $false
        if ($Handle.Process) {
            $gone = $Handle.Process.HasExited
        } elseif ($Handle.TaskName) {
            $st = [string](Get-ScheduledTask -TaskName $Handle.TaskName -ErrorAction SilentlyContinue).State
            $gone = ($st -and ($st -ne 'Running') -and ($st -ne 'Queued') -and ($st -ne 'Unknown'))
        }
        if ($gone) {
            Start-Sleep -Milliseconds 500
            if (Test-Path -LiteralPath $Handle.ResultFile) { $done = $true; break }
            # Wrapper gone and no result: only a still-running target keeps the step alive.
            if (@(Get-TargetProcesses -UserName $UserName -Exe $Handle.Exe).Count -eq 0) { $wrapperGone = $true; break }
        }
        if ($Capture) {
            try {
                $procs = @(Get-UserProcesses $UserName)
                $wins = @()
                if ($procs.Count -gt 0) { $wins = @(Get-WindowTexts -ProcessIds ($procs | ForEach-Object { $_.pid })) }
                $newTemp = @(Get-TempItems $TempDir | Where-Object { -not $baseline.ContainsKey($_.name) })
                foreach ($n in $newTemp) { $everNew[$n.name] = $n }
                $sig = ((($procs | ForEach-Object { "$($_.pid)|$($_.cmd)" }) + ($wins | ForEach-Object { "$($_.pid)|$($_.kind)|$($_.text)" }) +
                        ($newTemp | ForEach-Object { "$($_.name)|$($_.size)" })) -join "`n")
                if ($sig -ne $lastSig) {
                    $lastSig = $sig
                    $row = [ordered]@{ t = [math]::Round($sw.Elapsed.TotalSeconds, 1); processes = $procs; windows = $wins; newTemp = $newTemp }
                    ($row | ConvertTo-Json -Compress -Depth 6) | Add-Content -LiteralPath $PollFile -Encoding UTF8
                }
            } catch {
                ("poll error: " + $_.Exception.Message) | Add-Content -LiteralPath ($PollFile + '.err') -Encoding UTF8
            }
        }
        Start-Sleep -Seconds 1
    }

    # Task Scheduler's LastTaskResult (task path); give it a moment to publish after the wrapper exits.
    $taskResult = $null
    if ($Handle.TaskName) {
        for ($i = 0; $i -lt 10; $i++) {
            $info = Get-ScheduledTaskInfo -TaskName $Handle.TaskName -ErrorAction SilentlyContinue
            $state = [string](Get-ScheduledTask -TaskName $Handle.TaskName -ErrorAction SilentlyContinue).State
            if (-not $done -or $state -ne 'Running') { break }
            Start-Sleep -Seconds 1
        }
        if ($info) { $taskResult = [uint32]$info.LastTaskResult }
    }
    $wrapperExit = $null
    if ($Handle.Process -and $Handle.Process.HasExited) { try { $wrapperExit = [int]$Handle.Process.ExitCode } catch { } }

    $result = $null
    $kind = ''
    $why = ''
    if ($done) {
        for ($i = 0; $i -lt 3 -and -not $result; $i++) {
            try { $result = Get-Content -Raw -LiteralPath $Handle.ResultFile | ConvertFrom-Json } catch { Start-Sleep -Milliseconds 500 }
        }
        if (-not $result) { $kind = 'wrapper-failed'; $why = 'result file unreadable' }
        elseif ($result.status -eq 'wrapper-error') { $kind = 'wrapper-failed'; $why = 'wrapper error: ' + $result.message }
        elseif ($null -eq $result.exitCode) { $kind = 'wrapper-failed'; $why = 'result file has no exitCode' }
        else { $kind = 'completed' }
    } elseif ($wrapperGone) {
        $kind = 'wrapper-failed'
        $why = $(if ($Handle.StartError) { 'wrapper could not be started: ' + $Handle.StartError } else { 'wrapper ended without a result file' })
    } elseif (@(Get-TargetProcesses -UserName $UserName -Exe $Handle.Exe).Count -gt 0) {
        $kind = 'hung'
        $why = "target still running at the $TimeoutSec s cap"
    } else {
        $kind = 'wrapper-failed'
        $why = "no result at the $TimeoutSec s cap and no target process seen"
    }

    $detail = ''
    if ($kind -eq 'wrapper-failed') {
        $bits = @($why)
        if ($null -ne $taskResult) { $bits += ('LastTaskResult 0x{0:X8}' -f $taskResult) }
        if ($null -ne $wrapperExit) { $bits += ('wrapper exit code {0}' -f $wrapperExit) }
        $et = Get-ErrText (@($Handle.ErrFiles) + @($Handle.WindowFile + '.err'))
        if ($et) { $bits += ('err: ' + $et) }
        $detail = $bits -join '; '
    } elseif ($kind -eq 'hung') {
        $detail = $why
    }
    $exitCode = $(if ($kind -eq 'completed') { [int]$result.exitCode } else { $null })
    $outcome = $(if ($kind -ne 'completed') { $kind } elseif ($exitCode -eq 0) { 'success' } else { "exit-$exitCode" })
    [pscustomobject]@{
        Done = $done; Kind = $kind; Outcome = $outcome; Detail = $detail; Hung = ($kind -eq 'hung')
        WaitedSeconds = [math]::Round($sw.Elapsed.TotalSeconds, 1)
        ExitCode = $exitCode
        WallSeconds = $(if ($kind -eq 'completed') { $result.wallSeconds } else { $null })
        LastTaskResult = $taskResult; WrapperExitCode = $wrapperExit
        NewTempItems = @($everNew.Values)
    }
}

function Watch-Uninstall {
    param($Handle, [string]$UserName, [int]$TimeoutSec = 300, [string]$TempDir, [string]$PollFile)
    Watch-Step -Handle $Handle -UserName $UserName -TimeoutSec $TimeoutSec -Capture -TempDir $TempDir -PollFile $PollFile
}

function Wait-UninstallSettled {
    <# The uninstaller's first phase exits right after launching its second phase (_unins.tmp), which deletes
       the files, so the wrapper reports before the app folder is gone. Wait (bounded) until no uninstaller
       process of the user remains before reading the post state. Returns seconds waited. #>
    param([Parameter(Mandatory)][string]$UserName, [int]$MaxSeconds = 60)
    $sw = [Diagnostics.Stopwatch]::StartNew()
    while ($sw.Elapsed.TotalSeconds -lt $MaxSeconds) {
        if (@(Get-UserProcesses $UserName | Where-Object { $_.name -match '^(unins\d+\.exe|_unins\.tmp)$' }).Count -eq 0) { break }
        Start-Sleep -Milliseconds 500
    }
    [math]::Round($sw.Elapsed.TotalSeconds, 1)
}

function ConvertTo-MdCell {
    param($Text)
    if ($null -eq $Text) { return '' }
    (([string]$Text) -replace '\|', '\|' -replace '[\r\n]+', ' ').Trim()
}

# ---------------------------------------------------------------------------------------------
# Scenario mode
# ---------------------------------------------------------------------------------------------
function Invoke-Scenario {
    param([Parameter(Mandatory)][string]$Scenario)
    $def = $ScenarioTable[$Scenario]
    $installers = @{ test = $TestInstaller; v100 = $V100Installer; v101 = $V101Installer }
    foreach ($k in $def.Steps) {
        if (-not $installers[$k] -or -not (Test-Path -LiteralPath $installers[$k])) { throw "Installer for '$k' not found: $($installers[$k])" }
    }
    Initialize-WorkRoot
    # A grant run uses its own user and output folder so it can never read the plain run's result files.
    $variant = $(if ($GrantBatchLogon) { 'grant' } else { '' })
    $assert = $def.Assert -and -not $GrantBatchLogon
    $outDir = Join-Path "$WorkRoot\out" ($Scenario + $(if ($variant) { "-$variant" } else { '' }))
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null

    # Stage installers where every repro user can read them.
    $staged = @{}
    foreach ($k in ($def.Steps | Select-Object -Unique)) {
        $dest = "$WorkRoot\inst\$k.exe"
        if (-not (Test-Path -LiteralPath $dest)) { Copy-Item -LiteralPath $installers[$k] -Destination $dest }
        $staged[$k] = $dest
    }

    $user = New-ReproUser -Name ("$UserPrefix$Scenario" + $(if ($variant) { 'g' } else { '' }))
    $steps = New-Object System.Collections.Generic.List[object]
    $failures = New-Object System.Collections.Generic.List[string]
    if ($GrantBatchLogon) {
        try { Write-Host ("[$Scenario] " + (Grant-BatchLogonRight -UserName $user.Name)) }
        catch { $failures.Add('batch-logon grant failed: ' + $_.Exception.Message); Write-Host "[$Scenario] grant failed: $($_.Exception.Message)" }
    }
    $batch = Get-BatchLogonReport -UserName $user.Name
    Write-Host ("[$Scenario] " + (Format-BatchLogonReport $batch))
    $localAppData = "C:\Users\$($user.Name)\AppData\Local"
    $tempDir = "$localAppData\Temp"
    $stop = $false
    $skipReason = ''
    $n = 0

    foreach ($key in $def.Steps) {
        if ($stop) { break }
        $n++
        $tag = 'step{0}-install-{1}' -f $n, $key
        $log = Join-Path $outDir "$tag.inno.log"
        $argString = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP- /LOG="{0}"' -f $log
        Write-Host "[$Scenario] $tag"
        $h = Invoke-AsUser -User $user -Tag $tag -Exe $staged[$key] -ArgString $argString -StepDir $outDir
        $w = Watch-Step -Handle $h -UserName $user.Name -TimeoutSec $TimeoutSec
        if (Test-Path -LiteralPath $h.StartedFile) {
            $ctx = Get-Content -Raw -LiteralPath $h.StartedFile | ConvertFrom-Json
            $localAppData = $ctx.localAppData; $tempDir = $ctx.temp
        }
        $post = Get-PostState $localAppData
        $steps.Add([pscustomobject]@{
                kind = 'install'; installer = $key; method = $h.Method; notes = $h.Notes; exitCode = $w.ExitCode
                outcome = $w.Outcome; detail = $w.Detail; wrapperExitCode = $w.WrapperExitCode
                lastTaskResult = $w.LastTaskResult; wallSeconds = $(if ($w.Kind -eq 'completed') { $w.WallSeconds } else { $w.WaitedSeconds }); log = (Split-Path $log -Leaf)
                logExists = (Test-Path -LiteralPath $log); post = $post; newTemp = @(); windowTexts = @()
            })
        if ($h.TaskName) { Unregister-ScheduledTask -TaskName $h.TaskName -Confirm:$false -ErrorAction SilentlyContinue }
        if ($w.Outcome -ne 'success') {
            # Any non-success install is recorded and ends the scenario: the uninstall would not test the intended history.
            $failures.Add("install '$key' (step $n) outcome=$($w.Outcome)$(if ($w.Detail) { ' (' + $w.Detail + ')' })")
            $skipReason = "uninstall skipped: install '$key' (step $n) ended with outcome '$($w.Outcome)', not success"
            $stop = $true
        }
    }

    # The uninstall, exactly as the bench ran it.
    $uninst = $null
    if (-not $stop) {
        $n++
        $tag = 'step{0}-uninstall' -f $n
        $log = Join-Path $outDir "$tag.inno.log"
        $pollFile = Join-Path $outDir "$tag.poll.jsonl"
        $unins = Join-Path $localAppData "Programs\Bitworks\$AppFolderName\unins000.exe"
        if (-not (Test-Path -LiteralPath $unins)) {
            $failures.Add("unins000.exe missing at $unins")
            $steps.Add([pscustomobject]@{ kind = 'uninstall'; installer = ''; exitCode = $null; outcome = 'not-run'; detail = 'unins000.exe missing'; skipped = 'unins000.exe missing'; post = (Get-PostState $localAppData) })
        } else {
            $argString = '/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /LOG="{0}"' -f $log
            Write-Host "[$Scenario] $tag"
            $h = Invoke-AsUser -User $user -Tag $tag -Exe $unins -ArgString $argString -StepDir $outDir
            $w = Watch-Uninstall -Handle $h -UserName $user.Name -TimeoutSec $TimeoutSec -TempDir $tempDir -PollFile $pollFile
            if ($w.Kind -eq 'completed') {
                $settled = Wait-UninstallSettled -UserName $user.Name
                if ($settled -ge 1) { $h.Notes += ('waited {0} s for the uninstaller second phase to exit' -f $settled) }
            }
            $post = Get-PostState $localAppData
            # Window rows from the poll and the in-task files. Each row's windows may be nested one level
            # (older rows), so flatten and stringify every field explicitly.
            $seen = New-Object System.Collections.Generic.List[string]
            foreach ($f in @($pollFile, $h.WindowFile)) {
                if (Test-Path -LiteralPath $f) {
                    foreach ($line in (Get-Content -LiteralPath $f)) {
                        try { $o = $line | ConvertFrom-Json } catch { continue }
                        foreach ($grp in @($o.windows)) {
                            foreach ($win in @($grp)) {
                                $cls = [string]$win.class
                                $txt = [string]$win.text
                                if ($txt -and $cls -ne 'ConsoleWindowClass') { $seen.Add('[' + $cls + '] ' + $txt) }
                            }
                        }
                    }
                }
            }
            $texts = @($seen | Select-Object -Unique)
            $uninst = [pscustomobject]@{
                kind = 'uninstall'; installer = ''; method = $h.Method; notes = $h.Notes; exitCode = $w.ExitCode
                outcome = $w.Outcome; detail = $w.Detail; wrapperExitCode = $w.WrapperExitCode
                lastTaskResult = $w.LastTaskResult; wallSeconds = $(if ($w.Kind -eq 'completed') { $w.WallSeconds } else { $w.WaitedSeconds })
                log = (Split-Path $log -Leaf); logExists = (Test-Path -LiteralPath $log)
                post = $post; newTemp = $w.NewTempItems; windowTexts = $texts
            }
            $steps.Add($uninst)
            if ($h.TaskName) {
                # A hung task is left running on purpose; Cleanup mode kills it at the end of the job.
                if (-not $w.Hung) { Unregister-ScheduledTask -TaskName $h.TaskName -Confirm:$false -ErrorAction SilentlyContinue }
            }
            if ($w.Outcome -ne 'success') { $failures.Add("uninstall outcome=$($w.Outcome)$(if ($w.Detail) { ' (' + $w.Detail + ')' })") }
            if ($w.Kind -eq 'completed' -and $post.appDirExists) { $failures.Add('app folder still present after uninstall') }
        }
    }

    $assertFailed = $assert -and ($failures.Count -gt 0)
    $result = [ordered]@{
        scenario = $Scenario; variant = $variant; note = $def.Note; asserted = $assert; history = $def.Steps
        batchLogon = $batch
        verdict = $(if ($failures.Count -eq 0) { 'OK' } elseif ($assert) { 'FAIL' } else { 'REPORTED-ANOMALY' })
        failures = @($failures); skipReason = $skipReason; steps = @($steps)
    }
    Write-Json $result (Join-Path $outDir 'result.json')
    Write-ScenarioSummary $result
    if ($assertFailed) {
        Write-Host "::error::Scenario $Scenario (asserted) failed: $($failures -join '; ')"
        exit 1
    }
    exit 0
}

function Write-ScenarioSummary {
    param($Result)
    $md = New-Object System.Collections.Generic.List[string]
    $mode = if ($Result.asserted) { 'asserted' } else { 'reported only' }
    $vtag = $(if ($Result.variant) { " + $($Result.variant) SeBatchLogonRight" } else { '' })
    $md.Add("### $($Result.scenario)${vtag}: $($Result.note) ($mode) - $($Result.verdict)")
    $md.Add('')
    $md.Add("- $(ConvertTo-MdCell (Format-BatchLogonReport $Result.batchLogon))")
    $md.Add('')
    $md.Add("History: $($Result.history -join ' -> ') -> uninstall")
    $md.Add('')
    $md.Add('| Step | Via | Exit | TaskResult | Wall s | Outcome | Log | unins000.dat B | App dir | Data dir | New TEMP is-*/_iu* |')
    $md.Add('|---|---|---|---|---|---|---|---|---|---|---|')
    $i = 0
    foreach ($s in $Result.steps) {
        $i++
        $name = if ($s.kind -eq 'install') { "$i install $($s.installer)" } else { "$i uninstall" }
        $tr = if ($null -ne $s.lastTaskResult) { '0x{0:X8}' -f [uint32]$s.lastTaskResult } else { '' }
        $temp = ConvertTo-MdCell (($s.newTemp | ForEach-Object { $_.name }) -join ', ')
        $md.Add(('| {0} | {1} | {2} | {3} | {4} | {5} | {6} | {7} | {8} | {9} | {10} |' -f $name, (ConvertTo-MdCell $s.method),
                $s.exitCode, $tr, $s.wallSeconds, (ConvertTo-MdCell $s.outcome), $(if ($s.logExists) { $s.log } else { 'none' }),
                $s.post.uninsDatBytes, $s.post.appDirExists, $s.post.dataDirExists, $temp))
    }
    $md.Add('')
    foreach ($s in $Result.steps) {
        foreach ($note in @($s.notes)) { if ($note) { $md.Add("- note: $(ConvertTo-MdCell $note)") } }
        if ($s.detail) { $md.Add("- $($s.kind) outcome $($s.outcome): $(ConvertTo-MdCell $s.detail)") }
        if ($s.kind -eq 'uninstall' -and $s.windowTexts) {
            $md.Add('- window text seen during uninstall:')
            foreach ($t in ($s.windowTexts | Select-Object -First 15)) { $md.Add("  - ``$(ConvertTo-MdCell $t)``") }
        }
    }
    if ($Result.skipReason) { $md.Add("- $(ConvertTo-MdCell $Result.skipReason)") }
    foreach ($f in $Result.failures) { $md.Add("- anomaly: $(ConvertTo-MdCell $f)") }
    $md.Add('')
    $text = $md -join "`n"
    Write-Host $text
    if ($env:GITHUB_STEP_SUMMARY) { Add-Content -LiteralPath $env:GITHUB_STEP_SUMMARY -Value $text -Encoding UTF8 }
}

# ---------------------------------------------------------------------------------------------
# Cleanup mode (end of job)
# ---------------------------------------------------------------------------------------------
function Invoke-Cleanup {
    $names = @(Get-LocalUser -ErrorAction SilentlyContinue | Where-Object { $_.Name -like "$UserPrefix*" } | ForEach-Object Name)
    foreach ($name in $names) {
        foreach ($p in @(Get-UserProcesses $name)) {
            Write-Host "killing $($p.name) pid $($p.pid) owned by $name"
            Stop-Process -Id $p.pid -Force -ErrorAction SilentlyContinue
        }
    }
    Get-ScheduledTask -TaskName 'repro-*' -ErrorAction SilentlyContinue | ForEach-Object {
        Stop-ScheduledTask -InputObject $_ -ErrorAction SilentlyContinue
        Unregister-ScheduledTask -InputObject $_ -Confirm:$false -ErrorAction SilentlyContinue
    }
    Start-Sleep -Seconds 2
    foreach ($name in $names) { Remove-LocalUser -Name $name -ErrorAction SilentlyContinue }
    Write-Host "cleanup done; users removed: $($names -join ', ')"
}

# ---------------------------------------------------------------------------------------------
if ($MyInvocation.InvocationName -ne '.') {
    switch ($Mode) {
        'Wrapper' { Invoke-WrapperMode -ParamFile $ParamFile }
        'Cleanup' { Invoke-Cleanup }
        default {
            if (-not $Scenario) { throw '-Scenario is required' }
            try {
                Invoke-Scenario -Scenario $Scenario
            } catch {
                # Name the exact failing statement and stack, so a driver bug is never just "Invoke-Scenario : <message>".
                Write-Host ('::error::driver exception ' + $_.Exception.GetType().FullName + ': ' + $_.Exception.Message)
                Write-Host ($_.InvocationInfo.PositionMessage)
                Write-Host ($_.ScriptStackTrace)
                throw
            }
        }
    }
}

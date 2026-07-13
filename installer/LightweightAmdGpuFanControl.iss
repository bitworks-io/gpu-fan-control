; Lightweight AMD GPU Fan Control - Inno Setup Script
; Author: Bitworks

#define MyAppName "Lightweight AMD GPU Fan Control"
#ifndef MyAppVersion
  #define MyAppVersion "1.0.0"
#endif
#define MyAppPublisher "Bitworks"
#define MyAppExeName "LightweightAmdGpuFanControl.exe"

[Setup]
AppId={{A1B2C3D4-E5F6-7890-ABCD-EF1234567890}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
DefaultDirName={autopf}\Bitworks\{#MyAppName}
DefaultGroupName={#MyAppName}
OutputDir=..\output
OutputBaseFilename=LightweightAmdGpuFanControl-Setup
SetupIconFile=..\LightweightAmdGpuFanControl\Assets\app.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=lowest
; Windows 10 (1903+) or Windows 11 — matches the in-box .NET Framework 4.8 requirement.
MinVersion=10.0
; The app is a windowless systray process; Restart Manager can't reliably close it and the install
; hung when a previous copy was running. Disable RM's close attempt and terminate the old instance
; ourselves in [Code] PrepareToInstall (then relaunch it — see [Run]).
CloseApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "startup"; Description: "Start with Windows"; GroupDescription: "Startup:"; Flags: unchecked

[Files]
; Build with: dotnet publish -c Release (net48; from solution root) — output in bin\Release\net48\publish\
Source: "..\LightweightAmdGpuFanControl\bin\Release\net48\publish\LightweightAmdGpuFanControl.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LightweightAmdGpuFanControl\bin\Release\net48\publish\LightweightAmdGpuFanControl.exe.config"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LightweightAmdGpuFanControl\bin\Release\net48\publish\*.dll"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs
; net48 publish produces no .json config files (runtimeconfig/deps are .NET Core concepts).
Source: "..\LightweightAmdGpuFanControl\bin\Release\net48\publish\*.pdb"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "LightweightAmdGpuFanControl"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: startup

[Run]
; Fresh install: optional "Launch" checkbox on the finished page.
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent; Check: IsFreshInstall
; Upgrade over a running instance we just force-closed: relaunch unconditionally so fan control
; resumes (the forced close skipped the app's fan-restore; leaving the GPU unmanaged is not an option).
Filename: "{app}\{#MyAppExeName}"; Flags: nowait; Check: ClosedRunningInstance

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\Bitworks\LightweightAmdGpuFanControl"

[Code]
var
  // Set true in PrepareToInstall when we force-closed a previously-running instance, so [Run]
  // knows to relaunch it (a fresh install just offers the optional "Launch" checkbox instead).
  GClosedRunningInstance: Boolean;

// The app targets .NET Framework 4.8, which ships in-box on Windows 10 1903+ and Windows 11
// but is absent from older Windows 10 builds (which carry 4.7.x). Detect it via the standard
// NDP\v4\Full 'Release' DWORD (528040 = 4.8) and guide the user rather than let the app fail
// to launch with a cryptic error.
function IsDotNet48OrLater(): Boolean;
var
  Release: Cardinal;
begin
  Result := False;
  if RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', Release) then
    Result := (Release >= 528040);
end;

function InitializeSetup(): Boolean;
var
  ErrorCode: Integer;
begin
  Result := True;
  if not IsDotNet48OrLater() then
  begin
    if MsgBox('This application requires Microsoft .NET Framework 4.8, which was not detected.'#13#10#13#10 +
              'It is included with Windows 10 (version 1903 and later) and Windows 11.'#13#10 +
              'On older Windows 10 builds you can install it for free from Microsoft.'#13#10#13#10 +
              'Open the .NET Framework 4.8 download page now? (Setup will then close.)',
              mbInformation, MB_YESNO) = IDYES then
      ShellExec('open', 'https://dotnet.microsoft.com/download/dotnet-framework/net48', '', '', SW_SHOW, ewNoWait, ErrorCode);
    Result := False;
  end;
end;

// [Run] Check helpers: relaunch the app after an upgrade that closed a running copy; otherwise
// (fresh install) just show the optional "Launch" checkbox on the finished page.
function ClosedRunningInstance(): Boolean;
begin
  Result := GClosedRunningInstance;
end;

function IsFreshInstall(): Boolean;
begin
  Result := not GClosedRunningInstance;
end;

// Close a previously-running instance so its locked EXE/DLLs can be replaced. The app is a
// windowless systray process that Restart Manager can't reliably close (CloseApplications=no),
// so terminate it directly. taskkill exit code 0 means an instance was actually running and
// killed — record that so [Run] relaunches it (the forced close skipped the app's fan-restore).
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  ResultCode: Integer;
begin
  Result := '';
  if Exec(ExpandConstant('{cmd}'), '/C taskkill /F /IM {#MyAppExeName}', '',
          SW_HIDE, ewWaitUntilTerminated, ResultCode) then
  begin
    if ResultCode = 0 then
    begin
      GClosedRunningInstance := True;
      // Give Windows a moment to release the file handles before we overwrite the EXE/DLLs.
      Sleep(1500);
    end;
  end;
end;

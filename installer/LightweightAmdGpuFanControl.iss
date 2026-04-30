; Lightweight AMD GPU Fan Control - Inno Setup Script
; Author: Bitworks

#define MyAppName "Lightweight AMD GPU Fan Control"
#define MyAppVersion "1.0.0"
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
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64
ArchitecturesInstallIn64BitMode=x64
PrivilegesRequired=lowest

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "startup"; Description: "Start with Windows"; GroupDescription: "Startup:"; Flags: unchecked

[Files]
; Build with: dotnet publish -c Release -r win-x64 (from solution root)
Source: "..\LightweightAmdGpuFanControl\bin\Release\net8.0-windows\win-x64\publish\LightweightAmdGpuFanControl.exe"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LightweightAmdGpuFanControl\bin\Release\net8.0-windows\win-x64\publish\*.dll"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs
Source: "..\LightweightAmdGpuFanControl\bin\Release\net8.0-windows\win-x64\publish\*.json"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\LightweightAmdGpuFanControl\bin\Release\net8.0-windows\win-x64\publish\*.pdb"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"

[Registry]
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "LightweightAmdGpuFanControl"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: startup

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{localappdata}\Bitworks\LightweightAmdGpuFanControl"

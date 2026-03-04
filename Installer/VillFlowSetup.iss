; VillFlow Installer Script — Inno Setup 6
; Requires Inno Setup 6: https://jrsoftware.org/isdl.php
;
; Build Steps:
; 1. dotnet publish VillFlow.App -c Release -r win-x64 --self-contained
; 2. Open this .iss file in Inno Setup Compiler and click Build
;    OR run: iscc VillFlowSetup.iss

#define MyAppName "VillFlow"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "VillFlow"
#define MyAppExeName "VillFlow.App.exe"
#define MyAppURL "https://github.com/SreekarGpalli/VillFlowSTT"

; Path to the published output (relative to this .iss file)
#define PublishDir "..\VillFlow.App\bin\Release\net8.0-windows\win-x64\publish"

[Setup]
AppId={{E7A3C1F9-B2D4-4E5F-9A6B-8C7D0E1F2A3B}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
DefaultDirName={autopf}\{#MyAppName}
DefaultGroupName={#MyAppName}
; No Start Menu group needed — app runs from system tray
DisableProgramGroupPage=yes
; Output settings
OutputDir=Output
OutputBaseFilename=VillFlowSetup-{#MyAppVersion}
Compression=lzma2/ultra64
SolidCompression=yes
; Visual
WizardStyle=modern
; Minimum Windows 10
MinVersion=10.0
; 64-bit only
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Run as admin for Program Files install
PrivilegesRequired=admin
; Allow user to change install dir
AllowNoIcons=yes
; Uninstall settings
UninstallDisplayName={#MyAppName}
; Show license
LicenseFile=..\LICENSE

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked
Name: "startup"; Description: "Start VillFlow when Windows starts"; GroupDescription: "Startup:"

[Files]
; Copy ALL files from the publish directory
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Registry]
; Auto-start with Windows (current user only)
Root: HKCU; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: string; ValueName: "{#MyAppName}"; ValueData: """{app}\{#MyAppExeName}"""; Flags: uninsdeletevalue; Tasks: startup

[Run]
; Launch after install
Filename: "{app}\{#MyAppExeName}"; Description: "Launch VillFlow"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Clean up app data on uninstall
Type: files; Name: "{localappdata}\VillFlow\*"
Type: dirifempty; Name: "{localappdata}\VillFlow"

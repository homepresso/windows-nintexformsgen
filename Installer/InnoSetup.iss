; ============================================================
; Nintex Forms Generator - Inno Setup Script
; ============================================================
; This produces a single self-extracting EXE installer.
;
; Prerequisites:
;   1. Install Inno Setup from https://jrsoftware.org/isdl.php
;   2. Build the project in Release mode first:
;      dotnet build FormGenerator\FormGenerator.csproj -c Release
;   3. Open this .iss file in Inno Setup Compiler and click Build
;      OR run from command line:
;      iscc.exe Installer\InnoSetup.iss
; ============================================================

#define MyAppName "Nintex Forms Generator"
#define MyAppVersion "1.0.0"
#define MyAppPublisher "andyhayes.ai"
#define MyAppURL "https://andyhayes.ai"
#define MyAppExeName "NintexFormsGenerator.exe"
#define BuildOutput "..\FormGenerator\bin\Release\net48"

[Setup]
; NOTE: AppId uniquely identifies this application. Do not change between versions.
AppId={{E4A3B8C1-5D72-4F9A-B6E1-8C3D5A7F2E90}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppVerName={#MyAppName} {#MyAppVersion}
AppPublisher={#MyAppPublisher}
AppPublisherURL={#MyAppURL}
AppSupportURL={#MyAppURL}
AppUpdatesURL={#MyAppURL}
DefaultDirName={autopf}\andyhayes.ai\Forms Generator
DefaultGroupName={#MyAppName}
AllowNoIcons=yes
; Output settings
OutputDir=..\output
OutputBaseFilename=NintexFormsGenerator-{#MyAppVersion}-Setup
; Compression
Compression=lzma2/ultra64
SolidCompression=yes
; Installer look and feel
WizardStyle=modern
SetupIconFile=..\FormGenerator\Resources\app.ico
UninstallDisplayIcon={app}\NintexFormsGenerator.exe
; Require admin for Program Files installation
PrivilegesRequired=admin
; Minimum Windows version (Windows 10)
MinVersion=10.0
; Architecture
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
; Show license if present
; LicenseFile=..\LICENSE.txt

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
; Main application files - recursively include everything from build output
Source: "{#BuildOutput}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
; NOTE: Don't use "Flags: ignoreversion" on any shared system files

[Icons]
; Start Menu shortcut
Name: "{group}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\NintexFormsGenerator.exe"
; Start Menu uninstall shortcut
Name: "{group}\Uninstall {#MyAppName}"; Filename: "{uninstallexe}"
; Desktop shortcut (optional)
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; IconFilename: "{app}\NintexFormsGenerator.exe"; Tasks: desktopicon

[Run]
; Launch after install (optional checkbox)
Filename: "{app}\{#MyAppExeName}"; Description: "{cm:LaunchProgram,{#StringChange(MyAppName, '&', '&&')}}"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
; Clean up any log files or generated content on uninstall
Type: filesandordirs; Name: "{app}\Logs"
Type: filesandordirs; Name: "{app}\Output"

[Code]
// Check if .NET Framework 4.8 is installed
function IsNetFx48Installed(): Boolean;
var
  release: Cardinal;
begin
  Result := False;
  if RegQueryDWordValue(HKLM, 'SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full', 'Release', release) then
  begin
    // 4.8 = 528040 on Windows 10 May 2019 Update+, 528049 otherwise
    Result := (release >= 528040);
  end;
end;

function InitializeSetup(): Boolean;
begin
  Result := True;
  if not IsNetFx48Installed() then
  begin
    MsgBox('This application requires .NET Framework 4.8 or later.' + Chr(13) + Chr(10) +
           Chr(13) + Chr(10) +
           '.NET Framework 4.8 is included with Windows 10 (version 1903+) and Windows 11.' + Chr(13) + Chr(10) +
           'Please update Windows or install .NET Framework 4.8 from:' + Chr(13) + Chr(10) +
           'https://dotnet.microsoft.com/download/dotnet-framework/net48' + Chr(13) + Chr(10) +
           Chr(13) + Chr(10) +
           'Setup will now exit.',
           mbError, MB_OK);
    Result := False;
  end;
end;

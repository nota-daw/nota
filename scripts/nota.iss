; SPDX-License-Identifier: AGPL-3.0-only
; Inno Setup script for the Nota Windows installer. Driven by scripts/package-win.ps1,
; which passes /DAppVersion=<semver> and /DPubDir=<self-contained publish dir>.

#define AppName "Nota"
#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef Arch
  #define Arch "x64"
#endif
#ifndef PubDir
  #define PubDir "..\dist\publish-" + Arch
#endif

[Setup]
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Nota
AppId={{9E5D3F2A-1C4B-4E6A-9B77-4E0A5D6C7B81}
DefaultDirName={autopf}\Nota
DefaultGroupName=Nota
DisableProgramGroupPage=yes
UninstallDisplayIcon={app}\Nota.App.exe
OutputDir=..\dist
OutputBaseFilename=Nota-Setup-{#AppVersion}-{#Arch}
Compression=lzma2
SolidCompression=yes
#if Arch == "arm64"
ArchitecturesAllowed=arm64
ArchitecturesInstallIn64BitMode=arm64
#else
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
#endif
WizardStyle=modern

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional icons:"

[Files]
Source: "{#PubDir}\*"; DestDir: "{app}"; Flags: recursesubdirs createallsubdirs ignoreversion

[Icons]
Name: "{group}\Nota"; Filename: "{app}\Nota.App.exe"
Name: "{commondesktop}\Nota"; Filename: "{app}\Nota.App.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Nota.App.exe"; Description: "Launch Nota"; Flags: nowait postinstall skipifsilent

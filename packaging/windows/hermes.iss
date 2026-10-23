; Inno Setup script for the Windows installer.
; Built by .github/workflows/release.yml:
;   iscc /DAppVersion=1.2.3 /DArch=x64 /DSourceDir=<publish dir> /DIconFile=<hermes.ico> /DOutputDir=<dir> hermes.iss

#ifndef AppVersion
  #define AppVersion "0.0.0"
#endif
#ifndef Arch
  #define Arch "x64"
#endif
; Inno Setup has no "arm64compatible" identifier, so arm64 installs only on arm64 Windows.
#if Arch == "arm64"
  #define ArchAllowed "arm64"
#else
  #define ArchAllowed "x64compatible"
#endif

[Setup]
; Keep AppId stable across releases: Windows uses it to find the installed version to upgrade.
AppId={{6B0E4F2C-3D7A-4E8B-9C1F-5A2D8E6B4C10}
AppName=Hermes
AppVersion={#AppVersion}
AppVerName=Hermes {#AppVersion}
AppPublisher=hunterlan
AppPublisherURL=https://github.com/hunterlan/Hermes
AppSupportURL=https://github.com/hunterlan/Hermes/issues
DefaultDirName={autopf}\Hermes
DefaultGroupName=Hermes
DisableProgramGroupPage=yes
; Installs per user without UAC by default; the dialog offers an all-users install.
PrivilegesRequired=lowest
PrivilegesRequiredOverridesAllowed=dialog
ArchitecturesAllowed={#ArchAllowed}
ArchitecturesInstallIn64BitMode={#ArchAllowed}
OutputDir={#OutputDir}
OutputBaseFilename=Hermes-{#AppVersion}-win-{#Arch}-setup
SetupIconFile={#IconFile}
UninstallDisplayIcon={app}\Hermes.exe
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
CloseApplications=yes

[Tasks]
Name: "desktopicon"; Description: "{cm:CreateDesktopIcon}"; GroupDescription: "{cm:AdditionalIcons}"; Flags: unchecked

[Files]
Source: "{#SourceDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{autoprograms}\Hermes"; Filename: "{app}\Hermes.exe"
Name: "{autodesktop}\Hermes"; Filename: "{app}\Hermes.exe"; Tasks: desktopicon

[Run]
Filename: "{app}\Hermes.exe"; Description: "{cm:LaunchProgram,Hermes}"; Flags: nowait postinstall skipifsilent

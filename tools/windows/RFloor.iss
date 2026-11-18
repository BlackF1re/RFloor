#define MyAppName "RFloor"
#ifndef AppVersion
  #define AppVersion "0.1.0"
#endif
#ifndef PublishDir
  #error PublishDir is required
#endif
#ifndef OutputDir
  #error OutputDir is required
#endif
#ifndef AppArch
  #define AppArch "x64"
#endif
#if AppArch == "x64"
  #define InstallArch "x64compatible"
#else
  #define InstallArch "arm64"
#endif
[Setup]
AppId={{322E0E93-ABBB-45B4-ADFD-D3227CB9AC76}
AppName={#MyAppName}
AppVersion={#AppVersion}
DefaultDirName={autopf}\RFloor
DefaultGroupName=RFloor
OutputDir={#OutputDir}
OutputBaseFilename=RFloor-{#AppVersion}-windows-{#AppArch}-setup
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
PrivilegesRequired=lowest
ArchitecturesAllowed={#InstallArch}
ArchitecturesInstallIn64BitMode={#InstallArch}
SetupIconFile=..\..\app\RFloor\Assets\RFloor.ico
UninstallDisplayIcon={app}\RFloor.Desktop.exe
[Files]
Source: "{#PublishDir}\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs
[Icons]
Name: "{autoprograms}\RFloor"; Filename: "{app}\RFloor.Desktop.exe"
[Run]
Filename: "{app}\RFloor.Desktop.exe"; Description: "Launch RFloor"; Flags: nowait postinstall skipifsilent

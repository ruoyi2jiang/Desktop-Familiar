#define MyAppName "Desktop Familiar"
#define MyAppVersion "0.10.0"
#define MyAppPublisher "Desktop Familiar"
#define MyAppExeName "DesktopFamiliar.exe"
#ifndef MyAppSourceRoot
  #define MyAppSourceRoot "..\Artifacts\InstallerStaging\DesktopFamiliar-standalone-win-x64"
#endif
#define MyAppSourceExe MyAppSourceRoot + "\DesktopFamiliar.exe"

[Setup]
AppId={{92AE6C24-0462-4D93-B029-5F9DF31B4B56}
AppName={#MyAppName}
AppVersion={#MyAppVersion}
AppPublisher={#MyAppPublisher}
VersionInfoVersion=0.10.0.0
VersionInfoCompany={#MyAppPublisher}
VersionInfoDescription={#MyAppName} installer
VersionInfoProductName={#MyAppName}
VersionInfoProductVersion={#MyAppVersion}
DefaultDirName={localappdata}\Programs\Desktop Familiar
DefaultGroupName={#MyAppName}
DisableProgramGroupPage=yes
PrivilegesRequired=lowest
UsedUserAreasWarning=no
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\Artifacts\Installer
OutputBaseFilename=Desktop-Familiar-Setup-{#MyAppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
SetupIconFile=..\Assets\AppIcon.ico
UninstallDisplayIcon={app}\{#MyAppExeName}
CloseApplications=yes
RestartApplications=no
SetupLogging=yes
AppMutex=Local\DesktopFamiliar-84D01F52-E8F4-4D45-9D86-D25CC33290BA

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"; Flags: unchecked

[Files]
Source: "{#MyAppSourceExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#MyAppSourceRoot}\Resources\Strings\en.json"; DestDir: "{app}\Resources\Strings"; Flags: ignoreversion
Source: "{#MyAppSourceRoot}\Resources\Licenses\SharpGLTF.txt"; DestDir: "{app}\Resources\Licenses"; Flags: ignoreversion
Source: "{#MyAppSourceRoot}\Resources\Licenses\NAudio.txt"; DestDir: "{app}\Resources\Licenses"; Flags: ignoreversion
Source: "{#MyAppSourceRoot}\Resources\Licenses\System.Speech.txt"; DestDir: "{app}\Resources\Licenses"; Flags: ignoreversion

[InstallDelete]
Type: files; Name: "{app}\DesktopFamiliar.exe"

[Icons]
Name: "{autoprograms}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"
Name: "{autodesktop}\{#MyAppName}"; Filename: "{app}\{#MyAppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#MyAppExeName}"; Description: "Launch {#MyAppName}"; Flags: nowait postinstall skipifsilent

#include "Startup.iss"

#define AppName "Tapo Desktop (TC70)"
#define AppVersion "0.1.0"
#define AppExe "TapoDesktop.exe"

[Setup]
AppId={{B2BF425B-0599-432D-9C8B-0E9C0C22A578}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Community prototype (unaffiliated with TP-Link)
DefaultDirName={autopf}\TapoDesktop
DefaultGroupName={#AppName}
OutputDir=..\dist
OutputBaseFilename=TapoDesktop-TC70-Setup-0.1.0
Compression=lzma2
SolidCompression=yes
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=lowest
UninstallDisplayIcon={app}\{#AppExe}

[Files]
Source: "..\publish\*"; DestDir: "{app}"; Flags: ignoreversion recursesubdirs createallsubdirs

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{autodesktop}\{#AppName}"; Filename: "{app}\{#AppExe}"; Tasks: desktopicon

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Run]
Filename: "{app}\{#AppExe}"; Description: "Launch {#AppName}"; Flags: nowait postinstall skipifsilent

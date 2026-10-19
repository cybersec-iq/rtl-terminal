#define AppName "Rtl Terminal"
#ifndef AppVersion
  #error AppVersion must be supplied by build-release.ps1 or the release workflow
#endif
#define AppPublisher "AryanIQ"
#define AppExeName "RtlTerminal.exe"
#define PublishDirectory "..\publish\win-x64"

[Setup]
AppId={{472E1923-0EA9-44D5-97D5-A45A2D7DDB96}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppPublisher}
DefaultDirName={autopf}\AryanIQ\Rtl Terminal
DefaultGroupName=Rtl Terminal
UninstallDisplayName=Rtl Terminal
UninstallDisplayIcon={app}\{#AppExeName}
OutputDir=..\release
OutputBaseFilename=RtlTerminal-Setup-{#AppVersion}-x64
SetupIconFile=..\RtlTerminal.ico
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
PrivilegesRequired=admin
MinVersion=10.0.19041
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "english"; MessagesFile: "compiler:Default.isl"

[Tasks]
Name: "desktopicon"; Description: "Create a desktop shortcut"; GroupDescription: "Additional shortcuts:"

[Files]
Source: "{#PublishDirectory}\{#AppExeName}"; DestDir: "{app}"; Flags: ignoreversion
Source: "{#PublishDirectory}\ConPty\*"; DestDir: "{app}\ConPty"; Flags: ignoreversion recursesubdirs createallsubdirs
Source: "{#PublishDirectory}\AgentEngine\xagent.js"; DestDir: "{app}\AgentEngine"; Flags: ignoreversion
Source: "{#PublishDirectory}\AgentEngine\codex-account-status.js"; DestDir: "{app}\AgentEngine"; Flags: ignoreversion
Source: "{#PublishDirectory}\AgentEngine\route-ui.js"; DestDir: "{app}\AgentEngine"; Flags: ignoreversion
Source: "{#PublishDirectory}\AgentEngine\route-hub.js"; DestDir: "{app}\AgentEngine"; Flags: ignoreversion
Source: "{#PublishDirectory}\AgentEngine\generic-broker.js"; DestDir: "{app}\AgentEngine"; Flags: ignoreversion
Source: "{#PublishDirectory}\AgentEngine\registry.json"; DestDir: "{app}\AgentEngine"; Flags: ignoreversion

[Icons]
Name: "{group}\Rtl Terminal"; Filename: "{app}\{#AppExeName}"
Name: "{group}\Uninstall Rtl Terminal"; Filename: "{uninstallexe}"
Name: "{autodesktop}\Rtl Terminal"; Filename: "{app}\{#AppExeName}"; Tasks: desktopicon

[Run]
Filename: "{app}\{#AppExeName}"; Description: "Launch Rtl Terminal"; Flags: nowait postinstall skipifsilent

[UninstallDelete]
Type: filesandordirs; Name: "{app}"

[UninstallRun]
Filename: "{sys}\reg.exe"; Parameters: "delete ""HKCU\Software\Classes\Directory\shell\AryanIQRtlTerminal"" /f"; Flags: runhidden
Filename: "{sys}\reg.exe"; Parameters: "delete ""HKCU\Software\Classes\Directory\Background\shell\AryanIQRtlTerminal"" /f"; Flags: runhidden

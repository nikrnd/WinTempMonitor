; Installer WinTemp - compilare con Inno Setup 6 (build.ps1 lo fa in automatico)

#define AppName "WinTemp"
#define AppVersion "1.1.0"
#define AppExe "WinTemp.exe"

[Setup]
AppId={{6A1C1E2B-3F0D-4D7A-9B7E-57E1A0C4D2F1}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher={#AppName}
DefaultDirName={autopf}\{#AppName}
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
OutputDir=..\dist
OutputBaseFilename=WinTemp-Setup-{#AppVersion}
Compression=lzma2
SolidCompression=yes
WizardStyle=modern
UninstallDisplayIcon={app}\{#AppExe}
CloseApplications=force

[Languages]
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"

[Tasks]
Name: "autostart"; Description: "Avvia WinTemp all'accesso a Windows"; GroupDescription: "Opzioni:"
Name: "pawnio"; Description: "Installa il driver PawnIO (necessario per le temperature della CPU e della scheda madre)"; GroupDescription: "Opzioni:"; Check: not PawnIOInstalled

[Files]
Source: "..\src\WinTemp\bin\publish\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "redist\PawnIO_setup.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall; Tasks: pawnio

[Icons]
Name: "{group}\{#AppName}"; Filename: "{app}\{#AppExe}"
Name: "{group}\Disinstalla {#AppName}"; Filename: "{uninstallexe}"

[Run]
Filename: "{tmp}\PawnIO_setup.exe"; Parameters: "-install -silent"; StatusMsg: "Installazione del driver PawnIO..."; Flags: waituntilterminated; Tasks: pawnio
Filename: "{sys}\schtasks.exe"; Parameters: "/Create /F /TN ""{#AppName}"" /SC ONLOGON /RL HIGHEST /DELAY 0000:15 /TR ""\""{app}\{#AppExe}\"""""; Flags: runhidden waituntilterminated; Tasks: autostart
Filename: "{app}\{#AppExe}"; Description: "Avvia {#AppName} ora"; Flags: postinstall nowait skipifsilent shellexec

[UninstallRun]
Filename: "{sys}\taskkill.exe"; Parameters: "/F /IM {#AppExe}"; Flags: runhidden; RunOnceId: "KillApp"
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /F /TN ""{#AppName}"""; Flags: runhidden; RunOnceId: "DelTask"

[Code]
function PawnIOInstalled: Boolean;
begin
  Result := RegKeyExists(HKLM, 'SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\PawnIO');
end;

function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Code: Integer;
begin
  // chiude un'eventuale istanza in esecuzione prima di sovrascrivere l'exe
  Exec(ExpandConstant('{sys}\taskkill.exe'), '/F /IM {#AppExe}', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Result := '';
end;

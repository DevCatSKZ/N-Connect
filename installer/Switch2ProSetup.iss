; Installer für den Switch 2 Pro Controller Bluetooth-Treiber.
; Bauen: siehe .github/workflows/switch2-pro-windows.yml (Inno Setup 6).
; Erwartet:  ..\out\publish\win-x64\Switch2ProBridge.exe  (dotnet publish, self-contained)
;            redist\ViGEmBus_Setup.msi  ODER  redist\ViGEmBus_Setup.exe  (offizieller ViGEmBus-Installer)

#define AppName "Switch 2 Pro Controller Treiber"
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#define AppExe "Switch2ProBridge.exe"
#if FileExists(AddBackslash(SourcePath) + "redist\ViGEmBus_Setup.msi")
  #define ViGEmFile "ViGEmBus_Setup.msi"
#elif FileExists(AddBackslash(SourcePath) + "redist\ViGEmBus_Setup.exe")
  #define ViGEmFile "ViGEmBus_Setup.exe"
#else
  #error "ViGEmBus-Installer fehlt: installer\redist\ViGEmBus_Setup.msi oder .exe ablegen"
#endif

[Setup]
AppId={{6F3C2B9E-52A1-4C8B-9E44-2D5F1B7A9C31}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=Switch2 Pro Bridge
DefaultDirName={autopf}\Switch2ProBridge
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
OutputDir=..\out
OutputBaseFilename=Switch2ProController-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; Adminrechte für ViGEmBus (Kernel-Treiber) und Programme-Ordner.
PrivilegesRequired=admin
ArchitecturesAllowed=x64compatible
ArchitecturesInstallIn64BitMode=x64compatible
MinVersion=10.0.19041
UninstallDisplayIcon={app}\{#AppExe}
CloseApplications=yes
RestartApplications=no

[Languages]
Name: "german"; MessagesFile: "compiler:Languages\German.isl"
Name: "english"; MessagesFile: "compiler:Default.isl"

[CustomMessages]
german.TaskAutostart=Automatisch mit Windows starten (empfohlen)
english.TaskAutostart=Start automatically with Windows (recommended)
german.InstallingViGEm=Installiere ViGEmBus (virtueller Controller-Treiber) …
english.InstallingViGEm=Installing ViGEmBus (virtual controller driver) …
german.LaunchNow=Jetzt starten
english.LaunchNow=Launch now
german.ViGEmFailed=ViGEmBus konnte nicht installiert werden. Ohne diesen Treiber kann kein virtueller Controller erzeugt werden. Sie können ViGEmBus später manuell installieren: https://github.com/nefarius/ViGEmBus/releases
english.ViGEmFailed=ViGEmBus could not be installed. Without it no virtual controller can be created. You can install it manually later: https://github.com/nefarius/ViGEmBus/releases

[Tasks]
Name: "autostart"; Description: "{cm:TaskAutostart}"

[Files]
Source: "..\out\publish\win-x64\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; DestName: "Anleitung.md"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "redist\{#ViGEmFile}"; DestDir: "{tmp}"; Flags: deleteafterinstall; Check: not ViGEmInstalled

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"

[Run]
; ViGEmBus still mitinstallieren (nur wenn noch nicht vorhanden).
#if ViGEmFile == "ViGEmBus_Setup.msi"
Filename: "msiexec.exe"; Parameters: "/i ""{tmp}\{#ViGEmFile}"" /qn /norestart"; StatusMsg: "{cm:InstallingViGEm}"; Flags: waituntilterminated; Check: not ViGEmInstalled; AfterInstall: CheckViGEmResult
#else
; Advanced-Installer-Bootstrapper: /exenoui = ohne Oberfläche, /qn = MSI still.
Filename: "{tmp}\{#ViGEmFile}"; Parameters: "/exenoui /qn /norestart"; StatusMsg: "{cm:InstallingViGEm}"; Flags: waituntilterminated; Check: not ViGEmInstalled; AfterInstall: CheckViGEmResult
#endif
; Programm im Kontext des angemeldeten Benutzers starten (trägt dort auch den Autostart ein).
Filename: "{app}\{#AppExe}"; Parameters: "{code:LaunchArgs}"; Description: "{cm:LaunchNow}"; Flags: nowait postinstall runasoriginaluser

[UninstallRun]
Filename: "{cmd}"; Parameters: "/C taskkill /IM {#AppExe} /F"; Flags: runhidden; RunOnceId: "KillBridge"
Filename: "{cmd}"; Parameters: "/C reg delete HKCU\Software\Microsoft\Windows\CurrentVersion\Run /v Switch2ProBridge /f"; Flags: runhidden; RunOnceId: "RemoveAutostart"

[Code]
// ViGEmBus registriert seinen Gerätetreiber-Dienst als "ViGEmBus".
function ViGEmInstalled: Boolean;
begin
  Result := RegKeyExists(HKLM, 'SYSTEM\CurrentControlSet\Services\ViGEmBus');
end;

procedure CheckViGEmResult;
begin
  if not ViGEmInstalled then
    MsgBox(CustomMessage('ViGEmFailed'), mbError, MB_OK);
end;

function LaunchArgs(Param: String): String;
begin
  if WizardIsTaskSelected('autostart') then
    Result := '--autostart'
  else
    Result := '';
end;

// Laufende Instanz vor dem Update beenden, sonst ist die EXE gesperrt.
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Code: Integer;
begin
  Exec(ExpandConstant('{cmd}'), '/C taskkill /IM {#AppExe} /F', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Result := '';
end;

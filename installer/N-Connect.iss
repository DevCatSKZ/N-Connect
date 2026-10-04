; Installer „N-Connect“ – Nintendo-Controller unter Windows (Switch 1/2, NSO, Wii, Wii U).
; Bauen: siehe .github/workflows/build.yml (Inno Setup 6).
; Erwartet:  ..\out\publish\win-x64\N-Connect.exe  (dotnet publish, self-contained)
;            redist\ViGEmBus_Setup.msi  ODER  redist\ViGEmBus_Setup.exe  (offizieller ViGEmBus-Installer)

#define AppName "N-Connect"
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#define AppExe "N-Connect.exe"
#if FileExists(AddBackslash(SourcePath) + "redist\ViGEmBus_Setup.msi")
  #define ViGEmFile "ViGEmBus_Setup.msi"
#elif FileExists(AddBackslash(SourcePath) + "redist\ViGEmBus_Setup.exe")
  #define ViGEmFile "ViGEmBus_Setup.exe"
#else
  #error "ViGEmBus-Installer fehlt: installer\redist\ViGEmBus_Setup.msi oder .exe ablegen"
#endif
; Optional: HidHide (versteckt per USB angeschlossene Controller vor Spielen, damit sie nicht doppelt erscheinen).
#if FileExists(AddBackslash(SourcePath) + "redist\HidHide_Setup.exe")
  #define WithHidHide
#endif

[Setup]
AppId={{6F3C2B9E-52A1-4C8B-9E44-2D5F1B7A9C31}
AppName={#AppName}
AppVersion={#AppVersion}
AppPublisher=devcatskz
DefaultDirName={autopf}\N-Connect
DefaultGroupName={#AppName}
DisableProgramGroupPage=yes
DisableDirPage=auto
OutputDir=..\out
OutputBaseFilename=N-Connect-Setup-{#AppVersion}
Compression=lzma2/max
SolidCompression=yes
WizardStyle=modern
; Logo und Banner (erzeugt mit „N-Connect.exe --render-brand installer\art“).
SetupIconFile=art\N-Connect.ico
WizardImageFile=art\wizard-100.bmp,art\wizard-150.bmp,art\wizard-200.bmp
WizardSmallImageFile=art\wizard-small-100.bmp,art\wizard-small-150.bmp,art\wizard-small-200.bmp
WizardImageStretch=no
WizardImageBackColor=$120D0C
; Adminrechte für ViGEmBus (Kernel-Treiber) und den Programme-Ordner.
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
german.InstallingViGEm=Installiere ViGEmBus (virtueller Controller-Treiber) …
english.InstallingViGEm=Installing ViGEmBus (virtual controller driver) …
german.LaunchNow=Jetzt starten
english.LaunchNow=Launch now
german.ViGEmFailed=ViGEmBus konnte nicht installiert werden. Ohne diesen Treiber kann kein virtueller Controller erzeugt werden. Sie können ViGEmBus später manuell installieren: https://github.com/nefarius/ViGEmBus/releases
english.ViGEmFailed=ViGEmBus could not be installed. Without it no virtual controller can be created. You can install it manually later: https://github.com/nefarius/ViGEmBus/releases

german.TaskHidHide=HidHide installieren – verhindert doppelte Controller bei USB-Kabel (Neustart nötig)
english.TaskHidHide=Install HidHide – prevents duplicate controllers when using a USB cable (restart required)
german.InstallingHidHide=Installiere HidHide …
english.InstallingHidHide=Installing HidHide …

[Tasks]
#ifdef WithHidHide
Name: "hidhide"; Description: "{cm:TaskHidHide}"; Check: not HidHideInstalled
#endif

[Files]
Source: "..\out\publish\win-x64\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; DestName: "Anleitung.md"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "redist\{#ViGEmFile}"; DestDir: "{tmp}"; Flags: deleteafterinstall; Check: not ViGEmInstalled
#ifdef WithHidHide
Source: "redist\HidHide_Setup.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall; Tasks: hidhide
#endif

[InstallDelete]
; Reste der Vorversion („Nintendo Controller für Windows“, Switch2ProBridge.exe) beim Update entfernen.
Type: files; Name: "{app}\Switch2ProBridge.exe"
Type: files; Name: "{autoprograms}\Nintendo Controller für Windows.lnk"

[Registry]
Root: HKLM; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "Switch2ProBridge"; Flags: deletevalue
; Autostart richtet N-Connect beim ersten Start selbst ein (für den Benutzer, in der App abschaltbar).
; Einträge älterer Versionen für alle Benutzer entfernen – sie ließen sich in der App nicht abschalten.
; Wird beim Deinstallieren entfernt.
Root: HKLM; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "N-Connect"; Flags: deletevalue

[Icons]
Name: "{autoprograms}\{#AppName}"; Filename: "{app}\{#AppExe}"

[Run]
; ViGEmBus still mitinstallieren (nur wenn nicht schon mindestens Version 1.17 vorhanden ist).
#if ViGEmFile == "ViGEmBus_Setup.msi"
Filename: "msiexec.exe"; Parameters: "/i ""{tmp}\{#ViGEmFile}"" /qn /norestart"; StatusMsg: "{cm:InstallingViGEm}"; Flags: waituntilterminated; Check: not ViGEmInstalled; AfterInstall: CheckViGEmResult
#else
; Advanced-Installer-Bootstrapper: /exenoui = ohne Oberfläche, /qn = MSI still.
Filename: "{tmp}\{#ViGEmFile}"; Parameters: "/exenoui /qn /norestart"; StatusMsg: "{cm:InstallingViGEm}"; Flags: waituntilterminated; Check: not ViGEmInstalled; AfterInstall: CheckViGEmResult
#endif
#ifdef WithHidHide
; HidHide still installieren (gleicher Installer-Typ wie ViGEmBus). Neustart verlangt Windows ggf. selbst.
Filename: "{tmp}\HidHide_Setup.exe"; Parameters: "/exenoui /qn /norestart"; StatusMsg: "{cm:InstallingHidHide}"; Flags: waituntilterminated; Tasks: hidhide
#endif
; Programm im Kontext des angemeldeten Benutzers starten (zeigt beim ersten Start die Kurzanleitung).
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchNow}"; Flags: nowait postinstall runasoriginaluser

[UninstallRun]
Filename: "{cmd}"; Parameters: "/C taskkill /IM {#AppExe} /F"; Flags: runhidden; RunOnceId: "KillBridge"
; Autostart, den die App selbst für den angemeldeten Benutzer gesetzt hat (Einstellungsfenster).
Filename: "{cmd}"; Parameters: "/C reg delete HKCU\Software\Microsoft\Windows\CurrentVersion\Run /v N-Connect /f"; Flags: runhidden; RunOnceId: "RemoveAutostart"

[Code]
// Vergleicht zwei Versionsangaben „a.b.c.d“ (−1, 0, 1).
function CompareVersion(A, B: String): Integer;
var
  PA, PB, NA, NB: Integer;
begin
  Result := 0;
  while (Result = 0) and ((A <> '') or (B <> '')) do
  begin
    PA := Pos('.', A);
    if PA = 0 then PA := Length(A) + 1;
    PB := Pos('.', B);
    if PB = 0 then PB := Length(B) + 1;
    NA := StrToIntDef(Copy(A, 1, PA - 1), 0);
    NB := StrToIntDef(Copy(B, 1, PB - 1), 0);
    Delete(A, 1, PA);
    Delete(B, 1, PB);
    if NA < NB then
      Result := -1
    else if NA > NB then
      Result := 1;
  end;
end;

// ViGEmBus gilt als vorhanden, wenn der Treiber mindestens Version 1.17 hat – sonst wird er aktualisiert.
function ViGEmInstalled: Boolean;
var
  Version: String;
begin
  Result := GetVersionNumbersString(ExpandConstant('{sys}\drivers\ViGEmBus.sys'), Version)
            and (CompareVersion(Version, '1.17.0.0') >= 0);
end;

// HidHide gilt als vorhanden, wenn sein Treiberdienst eingetragen ist.
function HidHideInstalled: Boolean;
begin
  Result := RegKeyExists(HKLM, 'SYSTEM\CurrentControlSet\Services\HidHide');
end;

// Nach der HidHide-Installation ist ein Neustart nötig, damit der Filtertreiber aktiv wird.
function NeedRestart: Boolean;
begin
  Result := WizardIsTaskSelected('hidhide') and HidHideInstalled;
end;

procedure CheckViGEmResult;
begin
  if not ViGEmInstalled then
    MsgBox(CustomMessage('ViGEmFailed'), mbError, MB_OK);
end;

// Laufende Instanz vor dem Update beenden, sonst ist die EXE gesperrt.
function PrepareToInstall(var NeedsRestart: Boolean): String;
var
  Code: Integer;
begin
  Exec(ExpandConstant('{cmd}'), '/C taskkill /IM {#AppExe} /F', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Exec(ExpandConstant('{cmd}'), '/C taskkill /IM Switch2ProBridge.exe /F', '', SW_HIDE, ewWaitUntilTerminated, Code);
  Result := '';
end;

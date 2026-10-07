; Installer „N-Connect“ – Nintendo-Controller unter Windows (Switch 1/2, NSO, Wii, Wii U).
; Bauen: siehe .github/workflows/build.yml (Inno Setup 6).
; Erwartet:  ..\out\publish\win-x64\N-Connect.exe  (dotnet publish, self-contained)
;            redist\ViGEmBus_Setup.msi  ODER  redist\ViGEmBus_Setup.exe  (offizieller ViGEmBus-Installer)

#define AppName "N-Connect"
#ifndef AppVersion
  #define AppVersion "1.0.0"
#endif
#define AppExe "N-Connect.exe"
; Geplante Aufgabe, über die N-Connect Controller ohne UAC-Abfrage per HidHide versteckt (Name wie HidHide.TaskName).
#define HidHideTask "N-Connect HidHide"
#if FileExists(AddBackslash(SourcePath) + "redist\ViGEmBus_Setup.msi")
  #define ViGEmFile "ViGEmBus_Setup.msi"
#elif FileExists(AddBackslash(SourcePath) + "redist\ViGEmBus_Setup.exe")
  #define ViGEmFile "ViGEmBus_Setup.exe"
#else
  #error "ViGEmBus-Installer fehlt: installer\redist\ViGEmBus_Setup.msi oder .exe ablegen"
#endif
; HidHide (versteckt Original-Controller vor Steam und Spielen, damit sie nicht doppelt erscheinen) – wird immer
; mitinstalliert, wenn redist\HidHide_Setup.exe beim Bauen vorliegt.
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
; Weitere Sprachen nur aufnehmen, wenn die ISL-Datei der installierten Inno-Version sie mitbringt.
#define InnoLangDir GetEnv("ProgramFiles(x86)") + "\Inno Setup 6\Languages"
#if FileExists(InnoLangDir + "\Spanish.isl")
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
#endif
#if FileExists(InnoLangDir + "\French.isl")
Name: "french"; MessagesFile: "compiler:Languages\French.isl"
#endif
#if FileExists(InnoLangDir + "\Italian.isl")
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"
#endif
#if FileExists(InnoLangDir + "\Portuguese.isl")
Name: "portuguese"; MessagesFile: "compiler:Languages\Portuguese.isl"
#endif
#if FileExists(InnoLangDir + "\BrazilianPortuguese.isl")
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
#endif
#if FileExists(InnoLangDir + "\Dutch.isl")
Name: "dutch"; MessagesFile: "compiler:Languages\Dutch.isl"
#endif
#if FileExists(InnoLangDir + "\Polish.isl")
Name: "polish"; MessagesFile: "compiler:Languages\Polish.isl"
#endif
#if FileExists(InnoLangDir + "\Russian.isl")
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
#endif
#if FileExists(InnoLangDir + "\Japanese.isl")
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"
#endif
#if FileExists(InnoLangDir + "\ChineseSimplified.isl")
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
#endif
#if FileExists(InnoLangDir + "\Korean.isl")
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"
#endif

[CustomMessages]
german.InstallingViGEm=Installiere ViGEmBus (virtueller Controller-Treiber) …
english.InstallingViGEm=Installing ViGEmBus (virtual controller driver) …
german.LaunchNow=Jetzt starten
english.LaunchNow=Launch now
german.ViGEmFailed=ViGEmBus konnte nicht installiert werden. Ohne diesen Treiber kann kein virtueller Controller erzeugt werden. Sie können ViGEmBus später manuell installieren: https://github.com/nefarius/ViGEmBus/releases
english.ViGEmFailed=ViGEmBus could not be installed. Without it no virtual controller can be created. You can install it manually later: https://github.com/nefarius/ViGEmBus/releases

german.InstallingHidHide=Installiere HidHide (verhindert doppelte Controller in Steam und Spielen) …
english.InstallingHidHide=Installing HidHide (prevents duplicate controllers in Steam and games) …
german.ConfiguringHidHide=Gebe N-Connect in HidHide frei …
english.ConfiguringHidHide=Allowing N-Connect in HidHide …

; Seite „Darstellung“ (Programm-Theme): Standard ist Dunkel.
german.ThemeCaption=Darstellung
english.ThemeCaption=Appearance
german.ThemeDescription=Wie soll N-Connect aussehen?
english.ThemeDescription=How should N-Connect look?
german.ThemeSubCaption=Wählen Sie das Farbschema des Programms:
english.ThemeSubCaption=Choose the color scheme of the application:
german.ThemeDark=Dunkel (empfohlen)
english.ThemeDark=Dark (recommended)
german.ThemeLight=Hell
english.ThemeLight=Light
german.ThemeSystem=Wie Windows
english.ThemeSystem=Same as Windows

; Rückfall auf Englisch für Sprachen ohne eigene Formulierung (Einträge ohne Sprachpräfix gelten für alle).
InstallingViGEm=Installing ViGEmBus (virtual controller driver) …
LaunchNow=Launch now
ViGEmFailed=ViGEmBus could not be installed. Without it no virtual controller can be created. You can install it manually later: https://github.com/nefarius/ViGEmBus/releases
InstallingHidHide=Installing HidHide (prevents duplicate controllers in Steam and games) …
ConfiguringHidHide=Allowing N-Connect in HidHide …
ThemeCaption=Appearance
ThemeDescription=How should N-Connect look?
ThemeSubCaption=Choose the color scheme of the application:
ThemeDark=Dark (recommended)
ThemeLight=Light
ThemeSystem=Same as Windows

[Files]
Source: "..\out\publish\win-x64\{#AppExe}"; DestDir: "{app}"; Flags: ignoreversion
Source: "..\README.md"; DestDir: "{app}"; DestName: "Anleitung.md"; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "redist\{#ViGEmFile}"; DestDir: "{tmp}"; Flags: deleteafterinstall; Check: not ViGEmInstalled
#ifdef WithHidHide
Source: "redist\HidHide_Setup.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall; Check: HidHideMissing
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
; HidHide immer still mitinstallieren, wenn es fehlt (gleicher Installer-Typ wie ViGEmBus). Neustart fragt das Setup am Ende.
Filename: "{tmp}\HidHide_Setup.exe"; Parameters: "/exenoui /qn /norestart"; StatusMsg: "{cm:InstallingHidHide}"; Flags: waituntilterminated; Check: HidHideMissing
#endif
; N-Connect gleich als erlaubtes Programm eintragen und Verstecken einschalten (Fehlschlag, z. B. vor dem Neustart, ist
; unkritisch – die App holt das beim ersten Verstecken nach).
Filename: "{code:HidHideCli}"; Parameters: "--app-reg ""{app}\{#AppExe}"" --cloak-on"; StatusMsg: "{cm:ConfiguringHidHide}"; Flags: runhidden waituntilterminated skipifdoesntexist; Check: HidHideInstalled
; Programm im Kontext des angemeldeten Benutzers starten (zeigt beim ersten Start die Kurzanleitung).
Filename: "{app}\{#AppExe}"; Description: "{cm:LaunchNow}"; Flags: nowait postinstall runasoriginaluser

[UninstallRun]
Filename: "{cmd}"; Parameters: "/C taskkill /IM {#AppExe} /F"; Flags: runhidden; RunOnceId: "KillBridge"
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /TN ""{#HidHideTask}"" /F"; Flags: runhidden; RunOnceId: "HidHideTask"
; Freigabe in HidHide wieder austragen (HidHide selbst bleibt, andere Programme nutzen es evtl. auch).
Filename: "{code:HidHideCli}"; Parameters: "--app-unreg ""{app}\{#AppExe}"""; Flags: runhidden skipifdoesntexist; RunOnceId: "HidHideUnreg"
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

var
  HidHideWasMissing: Boolean;
  ThemePage: TInputOptionWizardPage;

function InitializeSetup: Boolean;
begin
  HidHideWasMissing := not HidHideInstalled;
  Result := True;
end;

// Eigene Seite „Darstellung“: das gewählte Programm-Theme landet in der Registry (HKCU\Software\N-Connect\Theme)
// und wird von der App beim ersten Start gelesen. Standard: Dunkel.
procedure InitializeWizard;
begin
  ThemePage := CreateInputOptionPage(wpSelectTasks,
    CustomMessage('ThemeCaption'), CustomMessage('ThemeDescription'),
    CustomMessage('ThemeSubCaption'), True, False);
  ThemePage.Add(CustomMessage('ThemeDark'));
  ThemePage.Add(CustomMessage('ThemeLight'));
  ThemePage.Add(CustomMessage('ThemeSystem'));
  ThemePage.SelectedValueIndex := 0;
end;

function ThemeRegValue: String;
begin
  case ThemePage.SelectedValueIndex of
    1: Result := 'light';
    2: Result := 'system';
  else
    Result := 'dark';
  end;
end;

function HidHideMissing: Boolean;
begin
  Result := not HidHideInstalled;
end;

// Pfad zu HidHideCLI.exe (Installationsordner aus der Registry, sonst Standardordner).
function HidHideCli(Param: String): String;
var
  Dir: String;
begin
  if not RegQueryStringValue(HKLM64, 'SOFTWARE\Nefarius Software Solutions e.U.\HidHide', 'Path', Dir) then
    Dir := ExpandConstant('{autopf}\Nefarius Software Solutions\HidHide');
  Result := AddBackslash(Dir) + 'x64\HidHideCLI.exe';
end;

// Nach der HidHide-Installation ist ein Neustart nötig, damit der Filtertreiber aktiv wird.
function NeedRestart: Boolean;
begin
  Result := HidHideWasMissing and HidHideInstalled;
end;

// Aufgabe „N-Connect HidHide“: startet „N-Connect.exe --hidhide-helper "<IDs>"“ als SYSTEM. Angemeldete Benutzer
// dürfen sie nur lesen und starten (GRGX), nicht ändern – so versteckt die App neue Controller ohne UAC-Abfrage.
// Der Helfer versteckt nur angeschlossene Nintendo-Controller/Kabel-Pads und gibt nur sich selbst frei.
procedure RegisterHidHideTask;
var
  Service, Folder: Variant;
  Xml: String;
begin
  Xml :=
    '<Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">' +
    '<RegistrationInfo><Author>N-Connect</Author><Description>N-Connect: Controller per HidHide vor Spielen verstecken (ohne Adminabfrage).</Description></RegistrationInfo>' +
    '<Principals><Principal id="Author"><UserId>S-1-5-18</UserId><RunLevel>HighestAvailable</RunLevel></Principal></Principals>' +
    '<Settings><MultipleInstancesPolicy>Queue</MultipleInstancesPolicy><DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>' +
    '<StopIfGoingOnBatteries>false</StopIfGoingOnBatteries><AllowStartOnDemand>true</AllowStartOnDemand><Enabled>true</Enabled>' +
    '<Hidden>true</Hidden><ExecutionTimeLimit>PT1M</ExecutionTimeLimit></Settings>' +
    '<Actions Context="Author"><Exec><Command>' + ExpandConstant('{app}\{#AppExe}') + '</Command>' +
    '<Arguments>--hidhide-helper "$(Arg0)"</Arguments></Exec></Actions></Task>';
  try
    Service := CreateOleObject('Schedule.Service');
    Service.Connect;
    Folder := Service.GetFolder('\');
    // 6 = anlegen oder ersetzen, 5 = Dienstkonto (SYSTEM)
    Folder.RegisterTask('{#HidHideTask}', Xml, 6, 'SYSTEM', Null, 5, 'D:(A;;FA;;;SY)(A;;FA;;;BA)(A;;GRGX;;;AU)');
  except
    Log('Aufgabe {#HidHideTask} nicht angelegt: ' + GetExceptionMessage);
  end;
end;

// Installersprache -> Sprachcode der App (HKCU\Software\N-Connect\Language, wird beim ersten Start gelesen).
function InstallerLangCode: String;
var
  L: String;
begin
  L := ExpandConstant('{language}');
  if L = 'german' then Result := 'de'
  else if L = 'spanish' then Result := 'es'
  else if L = 'french' then Result := 'fr'
  else if L = 'italian' then Result := 'it'
  else if (L = 'portuguese') or (L = 'brazilianportuguese') then Result := 'pt'
  else if L = 'dutch' then Result := 'nl'
  else if L = 'polish' then Result := 'pl'
  else if L = 'russian' then Result := 'ru'
  else if L = 'japanese' then Result := 'ja'
  else if L = 'chinesesimplified' then Result := 'zh'
  else if L = 'korean' then Result := 'ko'
  else Result := 'en';
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
  begin
    RegisterHidHideTask;
    RegWriteStringValue(HKCU, 'Software\N-Connect', 'Language', InstallerLangCode);
    RegWriteStringValue(HKCU, 'Software\N-Connect', 'Theme', ThemeRegValue);
  end;
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

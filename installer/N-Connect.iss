; Installer „N-Connect“ – Controller unter Windows (Switch 1/2, NSO, Wii, Wii U, PlayStation, Xbox).
; Bauen: siehe .github/workflows/build.yml (Inno Setup 6).
; Erwartet:  ..\out\publish\win-x64\N-Connect.exe  (dotnet publish, self-contained)
;            redist\ViGEmBus_Setup.msi  ODER  redist\ViGEmBus_Setup.exe  (offizieller ViGEmBus-Installer)
;            redist\HidHide_Setup.exe  (optional, offizieller HidHide-Installer)
;            ..\README.md, ..\README.en.md, ..\THIRD-PARTY-NOTICES.md
; Sprache und Darstellung schreibt das Setup nicht selbst in Benutzereinstellungen: Es übergibt sie beim Start nach der
; Installation an die App (siehe SetupArgs) und merkt sie im eigenen Deinstallationsschlüssel (RegisterPreviousData);
; die App übernimmt sie nur, solange in ihren Einstellungen noch nichts gewählt ist.

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
; Englisch zuerst: Ohne passende Sprache (z. B. Schwedisch, oder Chinesisch ohne ISL-Datei) schlägt das Setup den
; ersten Eintrag vor – wie die App, die dann unter „Automatisch“ Englisch zeigt (siehe ChosenLangCode).
Name: "english"; MessagesFile: "compiler:Default.isl"
Name: "german"; MessagesFile: "compiler:Languages\German.isl"
; Weitere Sprachen nur aufnehmen, wenn die ISL-Datei der Inno-Version, die gerade baut, sie mitbringt. SetupLangs
; sammelt dazu die Sprachcodes der App (siehe InstallerLangCode), damit das Setup weiß, was es angeboten hat.
#define InnoLangDir AddBackslash(CompilerPath) + "Languages"
#define SetupLangs "en de"
#if FileExists(InnoLangDir + "\Spanish.isl")
Name: "spanish"; MessagesFile: "compiler:Languages\Spanish.isl"
  #define SetupLangs SetupLangs + " es"
#endif
#if FileExists(InnoLangDir + "\French.isl")
Name: "french"; MessagesFile: "compiler:Languages\French.isl"
  #define SetupLangs SetupLangs + " fr"
#endif
#if FileExists(InnoLangDir + "\Italian.isl")
Name: "italian"; MessagesFile: "compiler:Languages\Italian.isl"
  #define SetupLangs SetupLangs + " it"
#endif
#if FileExists(InnoLangDir + "\Portuguese.isl")
Name: "portuguese"; MessagesFile: "compiler:Languages\Portuguese.isl"
  #define SetupLangs SetupLangs + " pt"
#endif
#if FileExists(InnoLangDir + "\BrazilianPortuguese.isl")
Name: "brazilianportuguese"; MessagesFile: "compiler:Languages\BrazilianPortuguese.isl"
  #define SetupLangs SetupLangs + " pt"
#endif
#if FileExists(InnoLangDir + "\Dutch.isl")
Name: "dutch"; MessagesFile: "compiler:Languages\Dutch.isl"
  #define SetupLangs SetupLangs + " nl"
#endif
#if FileExists(InnoLangDir + "\Polish.isl")
Name: "polish"; MessagesFile: "compiler:Languages\Polish.isl"
  #define SetupLangs SetupLangs + " pl"
#endif
#if FileExists(InnoLangDir + "\Russian.isl")
Name: "russian"; MessagesFile: "compiler:Languages\Russian.isl"
  #define SetupLangs SetupLangs + " ru"
#endif
#if FileExists(InnoLangDir + "\Japanese.isl")
Name: "japanese"; MessagesFile: "compiler:Languages\Japanese.isl"
  #define SetupLangs SetupLangs + " ja"
#endif
#if FileExists(InnoLangDir + "\ChineseSimplified.isl")
Name: "chinesesimplified"; MessagesFile: "compiler:Languages\ChineseSimplified.isl"
  #define SetupLangs SetupLangs + " zh"
#endif
#if FileExists(InnoLangDir + "\Korean.isl")
Name: "korean"; MessagesFile: "compiler:Languages\Korean.isl"
  #define SetupLangs SetupLangs + " ko"
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

; Seite „Darstellung“ (Programm-Theme): Standard ist Dunkel bzw. die Wahl der letzten Installation.
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
; Anleitung in der Sprache des Setups: Deutsch, sonst Englisch (README.en.md).
Source: "..\README.md"; DestDir: "{app}"; DestName: "Anleitung.md"; Languages: german; Flags: ignoreversion
Source: "..\README.en.md"; DestDir: "{app}"; DestName: "README.md"; Languages: not german; Flags: ignoreversion
Source: "..\THIRD-PARTY-NOTICES.md"; DestDir: "{app}"; Flags: ignoreversion
Source: "redist\{#ViGEmFile}"; DestDir: "{tmp}"; Flags: deleteafterinstall; Check: not ViGEmInstalled
#ifdef WithHidHide
Source: "redist\HidHide_Setup.exe"; DestDir: "{tmp}"; Flags: deleteafterinstall; Check: HidHideMissing
#endif

[InstallDelete]
; Reste der Vorversion („Nintendo Controller für Windows“, Switch2ProBridge.exe) beim Update entfernen.
Type: files; Name: "{app}\Switch2ProBridge.exe"
Type: files; Name: "{autoprograms}\Nintendo Controller für Windows.lnk"
; Anleitung der anderen Sprache aus einer früheren Installation.
Type: files; Name: "{app}\README.md"; Languages: german
Type: files; Name: "{app}\Anleitung.md"; Languages: not german

[Registry]
Root: HKLM; Subkey: "Software\Microsoft\Windows\CurrentVersion\Run"; ValueType: none; ValueName: "Switch2ProBridge"; Flags: deletevalue
; Autostart richtet N-Connect beim ersten Start selbst ein (für den Benutzer, HKCU\…\Run, in der App abschaltbar;
; beim Deinstallieren entfernt RemoveAutostartEntries ihn). Einträge älterer Versionen für alle Benutzer entfernen –
; sie ließen sich in der App nicht abschalten.
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
; Programm im Kontext des angemeldeten Benutzers starten (zeigt beim ersten Start die Kurzanleitung) und ihm dabei
; Sprache und Darstellung des Setups mitgeben (beim stillen Update nicht, siehe SetupArgs). Läuft auch still, damit
; N-Connect nach dem Ein-Klick-Update wieder startet.
Filename: "{app}\{#AppExe}"; Parameters: "{code:SetupArgs}"; Description: "{cm:LaunchNow}"; Flags: nowait postinstall runasoriginaluser

[UninstallRun]
Filename: "{cmd}"; Parameters: "/C taskkill /IM {#AppExe} /F"; Flags: runhidden; RunOnceId: "KillBridge"
Filename: "{sys}\schtasks.exe"; Parameters: "/Delete /TN ""{#HidHideTask}"" /F"; Flags: runhidden; RunOnceId: "HidHideTask"
; Freigabe in HidHide wieder austragen (HidHide selbst bleibt, andere Programme nutzen es evtl. auch).
Filename: "{code:HidHideCli}"; Parameters: "--app-unreg ""{app}\{#AppExe}"""; Flags: runhidden skipifdoesntexist; RunOnceId: "HidHideUnreg"
; Den Autostart der Benutzer entfernt nicht ein „reg delete HKCU …“ (das träfe im Admin-Kontext ggf. das falsche
; Konto), sondern RemoveAutostartEntries beim Deinstallieren (CurUninstallStepChanged). Installationen bis 1.0.21
; haben den alten Eintrag (RunOnceId „RemoveAutostart“) noch im Deinstallationsprotokoll – er läuft dann zusätzlich, ist
; aber harmlos.

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

// Eigene Seite „Darstellung“: Die Wahl bekommt die App beim Start nach der Installation als „--setup-theme=…“
// (SetupArgs) und übernimmt sie nur, solange in ihren Einstellungen noch keine Darstellung steht. Vorausgewählt ist
// die Wahl der letzten Installation (GetPreviousData, im Deinstallationsschlüssel), sonst Dunkel. Installationen bis
// 1.0.21 hatten sie nur nach HKCU\Software\N-Connect geschrieben – dort nur lesen, damit ein Durchklicken beim Update
// die damalige Wahl nicht durch „Dunkel“ ersetzt.
procedure InitializeWizard;
var
  Previous: String;
begin
  ThemePage := CreateInputOptionPage(wpSelectTasks,
    CustomMessage('ThemeCaption'), CustomMessage('ThemeDescription'),
    CustomMessage('ThemeSubCaption'), True, False);
  ThemePage.Add(CustomMessage('ThemeDark'));
  ThemePage.Add(CustomMessage('ThemeLight'));
  ThemePage.Add(CustomMessage('ThemeSystem'));
  Previous := GetPreviousData('Theme', '');
  if Previous = '' then
    if not RegQueryStringValue(HKCU, 'Software\N-Connect', 'Theme', Previous) then
      Previous := 'dark';
  if Previous = 'light' then
    ThemePage.SelectedValueIndex := 1
  else if Previous = 'system' then
    ThemePage.SelectedValueIndex := 2
  else
    ThemePage.SelectedValueIndex := 0;
end;

function ThemeValue: String;
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
// Der Helfer versteckt nur angeschlossene, echte Nintendo-/Sony-Controller und Kabel-Pads (keine virtuellen Pads)
// und gibt nur sich selbst frei. Fehlt die Aufgabe (Registrierung gescheitert, portable Variante), fragt die App per UAC.
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

// Installersprache -> Sprachcode der App (für „--setup-lang=…“, siehe SetupArgs).
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

// Anzeigesprache von Windows als Sprachcode der App – dieselbe Wahl wie Tr.WindowsLanguage unter „Automatisch“
// (Hauptsprache der LANGID; ohne eigene Tabelle Englisch).
function WindowsLangCode: String;
begin
  case GetUILanguage and $3FF of
    $07: Result := 'de';
    $0A: Result := 'es';
    $0C: Result := 'fr';
    $10: Result := 'it';
    $16: Result := 'pt';
    $13: Result := 'nl';
    $15: Result := 'pl';
    $19: Result := 'ru';
    $11: Result := 'ja';
    $04: Result := 'zh';
    $12: Result := 'ko';
  else
    Result := 'en';
  end;
end;

// Sprachcode, wenn die Setup-Sprache bewusst gewählt wurde, sonst '' (die App bleibt „wie Windows“). Das Setup schlägt
// die Windows-Sprache vor, wenn es sie anbietet (Traditionelles Chinesisch nie als Vereinfachtes), sonst Englisch
// (erster Eintrag unter [Languages]). Nur wenn dieser Vorschlag die Sprache der App unter „Automatisch“ war, ist eine
// andere Setup-Sprache eine bewusste Wahl. Bietet das Setup die Windows-Sprache nicht an (z. B. Chinesisch, ISL-Datei
// fehlt beim Bauen), lief es zwangsläufig in einer anderen Sprache – dann nichts übergeben.
function ChosenLangCode: String;
var
  WinLang: String;
  Lang: Integer;
begin
  WinLang := WindowsLangCode;
  Lang := GetUILanguage;
  Result := InstallerLangCode;
  if (Result = WinLang) or (Pos(' ' + WinLang + ' ', ' {#SetupLangs} ') = 0)
     or ((WinLang = 'zh') and ((Lang = $0404) or (Lang = $0C04) or (Lang = $1404) or (Lang = $7C04))) then
    Result := '';
end;

// Argumente für den Start nach der Installation ([Run], runasoriginaluser): Darstellung und – nur bei bewusster Wahl,
// siehe ChosenLangCode – Sprache des Setups. Die App übernimmt sie nur, solange in settings.json noch nichts gewählt
// ist (InstallerChoices; „Automatisch“ und „Dunkel“ speichert sie ausdrücklich). Das Setup selbst schreibt nichts nach
// HKCU – es läuft mit Adminrechten, bei UAC über ein anderes Konto wäre das das falsche Profil. Beim stillen Update
// (Ein-Klick-Update der App, winget) keine Argumente: Dort gab es keine Wahl, die App behält ihre Einstellungen.
function SetupArgs(Param: String): String;
begin
  if WizardSilent then
    Result := ''
  else
  begin
    Result := '--setup-theme=' + ThemeValue;
    if ChosenLangCode <> '' then
      Result := '--setup-lang=' + ChosenLangCode + ' ' + Result;
  end;
end;

// Wahl für die nächste Installation merken (auch beim stillen Update, dann unverändert; Sprache leer, wenn nicht bewusst
// gewählt). Im Deinstallationsschlüssel (HKLM, „Inno Setup CodeFile: Theme/Language“) liest sie auch die App beim
// allerersten Start ohne Argumente (InstallerChoices) – etwa wenn „Jetzt starten“ abgewählt war oder ein weiteres
// Benutzerkonto N-Connect startet.
procedure RegisterPreviousData(PreviousDataKey: Integer);
begin
  SetPreviousData(PreviousDataKey, 'Theme', ThemeValue);
  SetPreviousData(PreviousDataKey, 'Language', ChosenLangCode);
end;

procedure CurStepChanged(CurStep: TSetupStep);
begin
  if CurStep = ssPostInstall then
    RegisterHidHideTask;
end;

// Autostart (HKCU\…\Run, Wert „N-Connect“) setzt die App selbst für den angemeldeten Benutzer. Die Deinstallation läuft
// mit Adminrechten – ihr HKCU ist nach einer UAC-Abfrage mit anderem Konto nicht das des Benutzers. Deshalb in allen
// geladenen Benutzerprofilen (HKEY_USERS = alle angemeldeten Benutzer) nur Einträge entfernen, die auf diese
// Installation zeigen; dazu dort Reste älterer Installer (Software\N-Connect: Language, Theme). Profile nicht
// angemeldeter Benutzer behalten einen toten Eintrag, den Windows beim Anmelden übergeht.
procedure RemoveAutostartEntries;
var
  Users: TArrayOfString;
  I: Integer;
  Key, Command, Exe: String;
begin
  Exe := Lowercase(ExpandConstant('{app}\{#AppExe}'));
  if not RegGetSubkeyNames(HKU, '', Users) then
    Exit;
  for I := 0 to GetArrayLength(Users) - 1 do
  begin
    Key := Users[I] + '\Software\Microsoft\Windows\CurrentVersion\Run';
    if RegQueryStringValue(HKU, Key, 'N-Connect', Command) and (Pos(Exe, Lowercase(Command)) > 0) then
    begin
      if RegDeleteValue(HKU, Key, 'N-Connect') then
        Log('Autostart entfernt: HKU\' + Key)
      else
        Log('Autostart nicht entfernt: HKU\' + Key);
    end;
    Key := Users[I] + '\Software\N-Connect';
    if RegKeyExists(HKU, Key) then
    begin
      RegDeleteValue(HKU, Key, 'Language');
      RegDeleteValue(HKU, Key, 'Theme');
      RegDeleteKeyIfEmpty(HKU, Key);
    end;
  end;
end;

procedure CurUninstallStepChanged(CurUninstallStep: TUninstallStep);
begin
  if CurUninstallStep = usUninstall then
    RemoveAutostartEntries;
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

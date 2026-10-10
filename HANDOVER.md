# Übergabe: N-Connect

> **Laufende Arbeit (04.10.2026): Funktion „Switch-Identität“** – Joy-Con an Switch und PC ohne neues Koppeln,
> gemeinsam mit Bluepick_RCM. Einstieg: [docs/UEBERGABE-SWITCH-IDENTITAET.md](docs/UEBERGABE-SWITCH-IDENTITAET.md),
> Details und Protokoll: [docs/SWITCH-IDENTITAET.md](docs/SWITCH-IDENTITAET.md).
> Zugehörige Dateien: `src/Switch2Pro.Protocol/SwitchBtSave.cs`, `tools/Switch2Pro.BtIdentityProbe/`.

Stand: 06.10.2026. Entwickelt im Repo `sfm` (Branch `ccr-ad005d0f-jon8df`, Ordner `switch2-pro-windows/`);
veröffentlicht als eigenes Repo **DevCatSKZ/N-Connect** (öffentlich) per `git subtree split`.
Was das Programm kann und wie man es baut: siehe [README.md](README.md). Vollständige Entwicklerdokumentation
(Funktionen und Verhalten, Architektur, Protokolle, Portierung): [docs/](docs/README.md).

## Arbeitsablauf nach jeder Änderung

```powershell
cd switch2-pro-windows
dotnet build -c Release                      # Warnungen gelten als Fehler
dotnet test -c Release --no-build            # aktuell 227 Tests, alle grün
N-Connect.exe --render <Ordner>              # alle Controller-Grafiken prüfen
N-Connect.exe --render-ui <Ordner> --demo-all --wide   # alle Seiten/Karten prüfen (auch --demo, --demo-retro, --compact, --light)
dotnet publish src\Switch2Pro.Bridge -c Release -r win-x64 --self-contained -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o out\publish\win-x64
ISCC.exe installer\N-Connect.iss             # Installer nach out\
cd ..; git subtree split --prefix=switch2-pro-windows -b n-connect-export
cd ..\N-Connect; git pull --ff-only ..\sfm n-connect-export
```

Commits als **devcatskz** (devcatskz@gmail.com), ohne „Co-Authored-By“-Zeile. Antworten an den Nutzer auf
**Deutsch**. Nach jeder Änderung: laufende App beenden, neu veröffentlichen, Installer bauen, App neu starten
(`out\publish\win-x64\N-Connect.exe`), dann Subtree nach `N-Connect` übernehmen.

Hinweise zur Umgebung:
- Inno Setup liegt nicht im System, sondern im Scratchpad der vorigen Sitzung (`…\scratchpad\inno\ISCC.exe`); ggf.
  neu entpacken/installieren. Das Skript `check.ps1` dort baut und rendert alle Seiten (`-Variants`, `-Extra --en`).
- Im Ordner `N-Connect` liegen **fremde, nicht committete** Dateien eines anderen Vorhabens („Switch-Identität“:
  `docs/SWITCH-IDENTITAET.md`, `docs/UEBERGABE-SWITCH-IDENTITAET.md`, `src/Switch2Pro.Protocol/SwitchBtSave.cs`,
  `tools/Switch2Pro.BtIdentityProbe/`) und eine lokale Änderung an `HANDOVER.md` (Hinweis oben). Nicht anfassen,
  nicht committen. Beim Übernehmen: `git stash push -- HANDOVER.md; git pull --ff-only ..\sfm n-connect-export;
  git stash pop` (vom Nutzer so freigegeben).
- Das Protokoll `%LOCALAPPDATA%\N-Connect\bridge.log` ist die wichtigste Quelle bei Hardwareproblemen. Die
  Prüfhilfen (`--render-ui --demo-all`) schreiben in dasselbe Protokoll („Demo-Modus …“) – nicht verwechseln.

## Release-Ablauf (fortlaufend, jedes Update)

1. In `sfm` entwickeln, Tests grün (`dotnet test -c Release`).
2. **Version hochzählen** in `src/Switch2Pro.Bridge/Switch2Pro.Bridge.csproj` (`<Version>`) —
   sie steuert die lokale Anzeige; CI überschreibt sie beim Veröffentlichen per Tag.
3. Commit + Push auf den Arbeitsbranch, `git subtree split --prefix=switch2-pro-windows -b sync-out`,
   in `N-Connect` fetchen + cherry-picken, `main` pushen.
4. **Tag setzen**: `git tag v<x.y.z> && git push origin v<x.y.z>` → CI baut Installer
   `N-Connect-Setup-<x.y.z>.exe` und legt das Release automatisch an.
5. Screenshots mit dem neuen Build neu rendern (`--render-ui --demo-all`) → `gh-pages` pushen;
   die Seite holt sich Versionsnummer und Download-Link selbst per GitHub-API.

## Aktueller Stand (10.10.2026, Nacht): v1.0.17 veröffentlicht

- **Veröffentlicht:** v1.0.16 (Desktop-Modus, Testseite, Einblendung, Diagnose, neue Vorlagen) und **v1.0.17**
  (Desktop-Widget). Beide CI-Läufe grün, inkl. der neuen Oberflächenprüfung auf GitHub. winget-Manifeste auf
  **1.0.17** (Prüfsummen aus dem Release nachgerechnet, `winget validate` ok – nur die YAML-Dateien prüfen, die
  `README.md` im Ordner lässt `winget validate` sonst scheitern).
- **Desktop-Widget** (`ControllerWidget`, Standard aus, *Allgemein → Desktop-Widget*): Kärtchen im aktiven
  Farbschema mit Spieler, Name, Verbindung · Ausgabe, Akku; verschiebbar, Rechtsklick-Menü, Doppelklick öffnet
  N-Connect. Details: docs/FUNKTIONEN.md Abschnitt 11a. Gesteuert über `TrayApp.SyncWidget`.
- **Prüfhilfe** rendert zusätzlich den Reiter „Test“ (`ui_cardN_6_*`), die Einblendung (`ui_einblendung_*`) und das
  Widget (`ui_widget_*`, `_kompakt`, `_leer`).
- **Echtes Widget in der Windows-11-Widgetleiste** geprüft und zurückgestellt: braucht Paket-Identität (MSIX bzw.
  Sparse Package), und die geht nur mit einem vertrauenswürdig signierten Paket → erst mit Code-Signatur-Zertifikat.
- **Nächstes Vorhaben (angefragt):** ESP32 mit denselben Kopplungsdaten wie der PC-Bluetooth-Stick, damit
  Controller ohne neues SYNC am ESP32 laufen – siehe „Offen“.

## Zuletzt erledigt (10.10.2026, abends): Desktop-Modus, Testseite, Einblendung, Diagnose, CI

- **Desktop-Modus** (Standard aus), **Testseite** (Reiter „Test“), **Einblendung beim Verbinden** (abschaltbar),
  **Diagnose exportieren**, neue Tastenvorlagen – Details in docs/FUNKTIONEN.md Abschnitt 11a.
- **Einstellungen sicher speichern** (Zwischendatei + `settings.json.bak`, Wiederherstellung beim Laden).
- **CI**: rendert bei jedem Build die Oberfläche (Artefakt „UI-Bilder“, blockiert nichts) und **signiert** App und
  Installer, sobald die Secrets `SIGNING_CERT_PFX` (PFX als Base64: `[Convert]::ToBase64String([IO.File]::ReadAllBytes("cert.pfx"))`)
  und `SIGNING_CERT_PASSWORD` im Repo N-Connect hinterlegt sind (Settings → Secrets → Actions). Günstige Wege zu einem
  Zertifikat: SignPath.io (kostenlos für Open Source, eigener Ablauf mit deren Action) oder Microsoft Trusted Signing
  (~10 $/Monat; braucht statt PFX die Action `azure/trusted-signing-action`).
- **winget**: Manifeste gültig (inzwischen 1.0.17); Einreichen = PR in microsoft/winget-pkgs (siehe winget/README.md).

## Zuletzt erledigt (10.10.2026, später): Windows-Design, Farbschemata, neues Icon, Skalierung, Prüfhilfe

- **Windows-Design als Standard** (Fluent-Farben, einfarbiger Akzent, kein Leuchten) plus Farbschemata Neon, Aurora,
  Sunset, Joy-Con (`Theme.Schemes`, `Settings.ColorScheme`, `SchemePicker`). Kontraste nachgerechnet.
- **Neues App-Icon** als Vektor (`Branding.DrawLogo`), ICO/PNG/Installer-Grafiken per `--render-brand` neu erzeugt.
- **Skalierung für alle Nutzer**: App skaliert selbst (`UiScale.Px`, `AutoScaleMode.None` überall), Schrift nach
  Windows-11-Typografie; geprüft bei 100/125/150/200 %, hell/dunkel, Deutsch und Russisch.
- **Prüfhilfe stört nicht mehr**: `--render-ui/--render/--dump-ui` starten sich auf einem unsichtbaren Desktop neu
  (`HiddenDesktop`), Fehler dort landen in `%TEMP%\N-Connect-Pruefhilfe-Fehler.txt`. Neue Schalter: `--scale=1.5`
  (simuliert 150 %), `--scheme=aurora` usw. **Trotzdem vor jedem Lauf beim Nutzer fragen.**

## Zuletzt erledigt (10.10.2026): Fehlerkorrekturen, Neon-Design, Statusleiste, Kompakt-Ansicht

- **Fehler behoben**: doppelte Akku-Warnung (`ControllerManager.CheckLowBattery` entfernt – `Player.CheckBattery`
  warnt schon bei 15/5 %, übersetzt); portables Update-Skript scheiterte an Umlauten/`%` im Pfad (jetzt UTF-8 +
  `chcp 65001`, `%` maskiert, Kopieren bis zu 10× versucht, Selbstlöschen ohne Fehlermeldung); „Gedrückt“ zeigte
  rohe Enum-Namen (`SRLeft`, Nintendo-Namen bei PlayStation/Xbox) – jetzt Namen des jeweiligen Controllers.
- **Neon-Design** (Farben aus Logo): Navy-Paletten, Verlauf `Theme.Accent` → `Theme.Accent2`, `Theme.Glow`,
  Schriftzug `Wordmark` statt Text (Logo bleibt bewusst weg), Titelleiste ohne Mica in Fensterfarbe.
- **Animationen** (`Anim.cs`): Hover, Schalter, Segmente, Akkubalken, Karten-Aufleuchten beim Verbinden und Glühen
  bei Tastendruck. Aus bei abgeschalteten Windows-Animationen und bei `--render…`.
- **Statusleiste** (`StatusBand.cs`) und **Kompakt-Ansicht** (`Settings.CompactCards`, Umschalter in der Leiste,
  bis 4 Spalten); Reihenfolge-Chips kleiner und gleichmäßig auf Zeilen verteilt; Gyro-Balken lesbar.
- Neue Texte in allen 11 Übersetzungstabellen (Block „Status-Kopfzeile, Ansicht“). Render-Schalter `--compact`.

## Zuletzt erledigt (07.10.2026, Nacht II): MIT-Lizenz, Paar-Knopf in der Karte, v1.0.7

- **LICENSE**: MIT (Copyright devcatskz) im Repo; winget-Manifeste auf `License: MIT` +
  `LicenseUrl` umgestellt und auf Version 1.0.6 mit den echten SHA-256 der Assets aktualisiert.
- **Joy-Con Paar/Trennen direkt in der Karte** (`ControllerOverview.Card`): neuer Kopf-Knopf
  `_pairToggle` (Glyph.Swap), nur sichtbar wenn alle Links des Spielers Joy-Con sind – klickt wie
  der Knopf auf der Joy-Con-Seite (SplitPair/PairWithAnySingle), Tooltip je nach Zustand, Enabled =
  IsPair || HasPartner. Neue Texte in allen 11 Übersetzungstabellen ergänzt.

## Zuletzt erledigt (07.10.2026, Nacht): Portable-Updater, Akku-Warnung, winget, v1.0.6

- **Updater kennt jetzt Portable** (`UpdateCheck.FindInstaller` + `TrayApp.InstallUpdateAsync`):
  im portablen Betrieb wird `N-Connect-Portable-….zip` geladen statt der Setup-EXE. Nach dem
  Download entpackt `PreparePortableUpdate` die neue EXE in den Temp-Ordner und startet ein kleines
  cmd-Skript, das nach Prozess-Ende die EXE neben der alten ersetzt und N-Connect neu startet –
  ganz ohne Adminrechte (der Hinweis-Dialog zeigt das entsprechend an).
- **Akku-Warnung** (`ControllerManager.CheckLowBattery`, läuft im 10-s-Takt): einmal je Verbindung
  „Akku fast leer (x %)“-Hinweis ab ≤15 %, nicht beim Laden. Meldungen bei Verbinden/Trennen sowie
  das Tray-Untermenü je Spieler (Vibrieren, Platz, Ausgabeart, Trennen) und Profilwechsel samt
  „aktiv bei Programm“ gab es bereits – ist jetzt in der Doku vermerkt.
- **winget-Manifeste** unter `winget/` (Setup + portable ZIP, echte SHA-256 von v1.0.5, AppId aus
  dem Inno-Skript) plus `winget/README.md` mit Einreich-Anleitung (wingetcreate/PR zu winget-pkgs).
  License-Feld steht auf „Proprietary“, solange das Repo keine LICENSE hat.

## Zuletzt erledigt (07.10.2026, spät): Portable-Variante, Release v1.0.5

- **Portable-Modus** (`Paths` in `Log.cs`): Liegt neben `N-Connect.exe` eine `portable.txt` oder ein
  Ordner `data`, wandern `settings.json`, `bridge.log` und Sicherungen in `data\` statt nach
  %APPDATA%/%LOCALAPPDATA%. `Settings.Save` legt den Ordner selbst an. Getestet: Render-Lauf aus
  einem Testordner schrieb `data\bridge.log` korrekt neben die EXE.
- **CI**: Der Workflow packt jetzt `N-Connect-Portable-<Version>.zip` (EXE + `portable.txt` mit
  Hinweistext) und hängt sie ans Release. **Einschränkung**: ViGEmBus/HidHide sind Kerneltreiber und
  müssen pro PC installiert bleiben — ohne sie startet die App, erzeugt aber keine virtuellen
  Controller (zeigt den bekannten „ViGEmBus fehlt“-Hinweis mit Download-Link).
- Linux-Port wurde besprochen und verworfen: App hängt komplett an Windows (WinForms, WinRT-BLE,
  ViGEmBus, XInput); unter Linux laufen Switch-Controller nativ per `hid-nintendo` + Steam Input.

## Zuletzt erledigt (07.10.2026, Abend): Geist-Xbox-Fix, Seite mit 4 dunklen Shots, v1.0.4

- **Geist-Xbox-Controller** (`VirtualPads.cs`): XInput-Platz wurde nur einmal sofort nach
  `Connect()` gelesen — oft noch nicht vergeben → eigene ViGEm-Pads tauchten als zusätzliche
  Xbox-Controller auf. Jetzt: sofort + bis 4 s nachlesen + beim ersten Rumble-Feedback markieren.
- **Reihenfolge-Chips einheitlich breit** (`PlayerOrderBar.Arrange`): alle Chips bekommen die
  Breite des breitesten, statt je nach Namenslänge.
- **Lokale Version**: `Switch2Pro.Bridge.csproj` trägt `<Version>` (zuletzt 1.0.3 → Anzeige
  „Version 1.0.3" auch bei Dev-Builds).
- **Pages**: nur noch 4 Screenshots, alle dunkel — Übersicht ganz breit, Tastenbelegung +
  Sticks & Vibration gleich breit daneben, Controller-Einstellungen mittig; `uebersicht-hell.png`
  entfernt.
- GitHub-Push lief zeitweise mit „Internal Server Error" — Retries gingen durch, nichts verloren.

## Zuletzt erledigt (07.10.2026): 12 Sprachen, Logo, Installer-Auswahl, Release v1.0.2

- **Zwölf Sprachen**: `Tr` arbeitet jetzt mit Sprachcodes (`Supported`-Liste in `Tr.cs`), je Sprache
  eine Tabelle `Tr.Lang<cc>.cs` (ES, FR, IT, PT, NL, PL, RU, JA, ZH, KO). Fallback: Sprache → Englisch →
  Deutsch. Sprachauswahl in *Allgemein*; `Tr.SetLanguage`/`--lang=xx` für Render-Prüfung.
- **Sprachquelle beim Start**: `Tr.Init` liest zuerst `HKCU\Software\N-Connect\Language` (schreibt der
  Installer), sonst Windows-Sprache, sonst Englisch. `Theme.Init` liest analog `...\Theme`
  („dark"/„light"/„system") – ohne beides gilt Dunkel.
- **Installer**: Inno-Sprachen per `FileExists`-Guard eingebunden (ISL muss auf dem Runner vorhanden
  sein); neue eigene Seite „Darstellung“ (Dunkel/Hell/Wie Windows, Standard Dunkel);
  `CurStepChanged(ssPostInstall)` schreibt Language + Theme nach HKCU.
- **Neues Logo**: `src/Switch2Pro.Bridge/N-Connect.png` (eingebettete Ressource), `Branding` zeichnet
  daraus Fenster-/Tray-/Installer-Grafiken; `--render-brand` neu gelaufen (Ico, Wizard-BMPs).
- **Tray-Menü im Dark Mode**: eigener `ToolStripRenderer` in `Theme.cs` zeichnet Text, Pfeile,
  Häkchen und Trennlinien in den Theme-Farben (vorher schwarze Schrift auf dunklem Grund).
- **Bluetooth-Rune** neu als Vektor gezeichnet (Buttons „Controller suchen …“ u. a.).
- **Light Mode**: `TextMuted` dunkler (#484850) – Sekundärtexte gut lesbar.
- **README zweisprachig**: `README.md` (Deutsch) + `README.en.md` mit Umschalt-Links.
- **GitHub Pages** (`gh-pages` im Repo `N-Connect`): komplett neu – Logo-Farbschema (Navy + Neon-Blau/
  Violett), Sprachwahl mit denselben 12 Sprachen (Browser-Sprache als Standard, `localStorage`),
  neue Screenshots, Download-Knopf holt das neueste Release-Asset per GitHub-API.
- **Release v1.0.2** über den Build-Workflow (Tag `v1.0.2` pushen → Installer + Release automatisch).
- Render-Prüfung aller 12 Sprachen (`--render-ui … --lang=xx`): keine abgeschnittenen Texte.

## Zuletzt erledigt (06.10.2026): Darstellung/DPI-Skalierung korrigiert

Nutzer-Meldung „alles klein und gequetscht, Menü-Texte abgeschnitten" (Commit `c78cea5`,
216/216 Tests grün, DE+EN-Render geprüft):

- **Ursache 1 – fehlende DPI-Basis**: `AutoScaleMode.Dpi` ohne `AutoScaleDimensions` skaliert auf
  Displays mit 125/150 % nicht; Punkt-Schriften rendern dort größer, während Pixel-Layouts gleich
  bleiben → abgeschnittene/verquetschte Texte. Jetzt `AutoScaleDimensions = new SizeF(96F, 96F)`
  in **allen** Fenstern und Dialogen (Settings, Welcome, Pair, PairingData, StickCalibration,
  GyroSetup, IrCamera, KeyCapture, Inline-Dialoge in `Dialogs.cs`, `Theme.Message`).
- **Ursache 2 – feste Breiten zu knapp**: Navi-Schiene 250→286 px, Einträge 258×44 px („Sticks &
  Vibration" passte nie); `ScrollPage.MaxContentWidth` 1000→1240; Fenster 1280×880 (min. 1060×660);
  Karten min. 700 px; Reihenfolge-Chips Text bis 430 px.
- **Controller-Karten aufgeräumt**: „Vibrieren"/„Trennen" sind jetzt immer Symbolknöpfe (Tooltips
  erklären sie), sonst fraßen drei Textknöpfe ~350 px der Titelzeile. Titel fällt bei Platzmangel von
  „Spieler n · Name" auf den reinen Controllernamen zurück. Info-Zeilen messen die Label-Breite
  statt fixer Spalte (Label und Wert klebten aneinander); Report-Rate steht nur noch im
  Details-Tab (kompakte Karte: nur Transport).

Wichtig bei künftigen UI-Beschwerden: immer beides prüfen – echte Engstelle im Layout **und**
DPI-Skalierung. `--render-ui` zeigt nur 100 %; hohe DPI-Skalierung muss am Nutzer-PC beurteilt werden.

## Zuletzt erledigt (05.10.2026): Controller-Grafiken überarbeitet

Komplette Grafik-Überarbeitung in `InputView.cs`, `InputView.Photo.cs`, `InputView.Outlines.cs`,
`InputView.Retro.cs`, `InputView.JoyCon.cs` (Commits `ffaa98b` … `ef22786`, alle mit `--render` visuell
geprüft, 216/216 Tests grün):

- **Schultertasten neu** (`EdgeShoulder`/`Shoulder`): gewölbte Keil-Segmente, die sich der Gehäusekante
  anschmiegen und **nebeneinander mit sichtbarem Spalt** liegen – nichts überlappt mehr. Reihenfolge wie am
  Original: GC `ZL|L` bzw. `R|Z`, Sony `L1|L2` / `R2|R1`, Xbox `LB|LT` / `RT|RB`, N64 schmales `ZR` innen.
  Trigger-Kappen höher und hinter den Bumper-Kappen; analoge Füllung bleibt.
- **DualShock 4 / DualSense komplett neu** (`PaintPlayStation`): echte Sony-Anatomie – Kreuz links oben,
  Symboltasten rechts oben, beide Sticks symmetrisch unten, Touchpad-Trapez mit Verlauf mittig (klickbar =
  Aufnahme). Eigene Umrisse `PhotoOutlines.DualShock`/`DualSense`: schlanke angewinkelte Griffe mit
  Einschnürung, tiefe V-Kerbe in der Mitte. SHARE/CREATE und OPTIONS als schmale Pills an den
  Touchpad-Ecken **ohne Aufschrift** (Wort-Labels klebten je nach Fenstergröße an der Lichtleiste).
- **Lichtleiste zeigt die Farbe vom Spiel** (`InputView.LightbarTint` ← `Player.Lightbar`): DualSense als
  U-förmige Leuchtstreifen um das Touchpad (`LightbarEdge`), DS4 als Streifen in der Oberkanten-Mulde
  (frei vom Touchpad).
- **Paletten originalgetreu**: DualSense weiß (weißes Gehäuse/Touchpad/Tasten, graue Symbole, schwarze
  Sticks), DS4 schwarz mit Farbsymbolen (△○✕□), Xbox farbige Buchstaben (A grün, B rot, X blau, Y gelb).
- **Neutral ohne Markenzeichen** (Nutzerwunsch): „Nintendo“ (NES), „SEGA“ (Mega Drive), „Wii“/„MotionPlus“
  (Wii-Fernbedienung) entfernt; PS- und Xbox-Logo durch neutrales Haus-Symbol `⌂` ersetzt (`XboxHome`).
  Übrig bleiben nur Funktionsnamen: L/R, ZL/ZR, L1/L2/R1/R2, LB/LT/RB/RT, A/B/X/Y, SELECT/START/HOME, MODE.
- **Wii Classic**: Sticks rund statt achteckig. **N64**: Z als Kapsel am Mittelgriff, ZR klein.
- **Koppeln erweitert (`2ff4999`)**: Fenster „Controller koppeln“ und AutoPair decken jetzt auch
  **PlayStation** (klassisch, Name „Wireless Controller“, `PairJustWorks` – auch im Hintergrund) und
  **Xbox über Bluetooth** (BLE: `WatchXbox`/`PairXbox` im Fenster, WinRT-Kopplung, `IsPaired`-Test) ab.
  Beide mit Hardware noch ungeprüft.

Rendern/Prüfen wie immer: `N-Connect.exe --render <Ordner>`. Bei Überlappungs-Meldungen vom Nutzer:
Textgrößen sind fest in pt, Positionen skalieren mit `PhotoFrame` – bei kleinen Karten kann Text an Kanten
kleben; ggf. Aufschrift weglassen statt verschieben.

## Zuletzt erledigt (04.10.2026, Nachmittag)

- Kopplungsdaten-Fenster und Passwort-Dialog im Windows-11-Stil (`PairingDataForm`, `Report`, `Footer`, `TextField`).
- Joy-Con 1 per SYNC zuverlässig (neu) koppeln – Details unten unter „Selbst koppeln“.
- Logo ohne pixeligen hellen Rand (`LogoView`, Fenstersymbol aus eingebetteter ICO, ICO mit Bitmap-Einträgen);
  Installer-Grafiken neu erzeugt (`--render-brand installer\art`, ICO auch nach `src\Switch2Pro.Bridge\` kopieren).
- Grafiken: Pfeile als gleich große Dreiecke (drehen am quer gehaltenen Joy-Con mit), SL/SR quer waagerecht,
  Joy-Con-Ansichten in `--render`.
- Eingabefelder `TextField` (Profil-Programme, Umbenennen, Passwort).
- Akzentfarbe festes sattes Blau (dunkel FF008CDC, hell FF0078D4) statt Windows-Akzentfarbe
  (war beim Nutzer pink, nicht gewünscht; `Branding.Blue` FF00B4F0 wirkte zu cyan; `Theme.Init`).
- Joy-Con-Paar trennt **nur noch** quer gehalten + SL/SR (Nutzer hatte per „SL + SR 1 s“ versehentlich getrennt; der
  Joy-Con lief dann einzeln mit gedrehter Belegung = „im Spiel falsch gemappt“).
- Spieler-Reihenfolge: Leiste `PlayerOrderBar` über den Karten (‹ › tauschen); Spieler rücken nach Trennen/
  Zusammenfassen lückenlos auf (`CompactPlayers`), Menüs bieten nur belegte Plätze an.

## Danach umgesetzt (Prüfung „Bugs, Referenzen, Benutzerfreundlichkeit“)

- **Steam sah Switch-1/NSO-Controller doppelt** (Steam/SDL unterstützen sie selbst; HidHide griff nur bei USB und
  von Hand, `Switch1HidLink` hatte keine `HidInstanceId`). Jetzt: automatisches Verstecken (FUNKTIONEN 2.4a).
  Beim Nutzer war HidHide **nicht installiert** – sehr wahrscheinlich die Ursache für „Joy-Con 1 im Spiel falsch“.
- **Ausgabeart je Controller** (FUNKTIONEN 3a).
- **Joy-Con 1 im Ladegriff per USB** (057E:200E, Gerätetyp 0x01/0x02/0x03) – mit echter Hardware **nicht** geprüft.
- **Gyro-Extras nach JoyShockMapper** (Flick-Stick, Gyro-Beschleunigung, „Gyro anhalten“) und **Rückkanal vom Spiel**
  (Xbox-Platz → Spieler-LED, DS4-Lichtleiste → HOME-LED/Karte): FUNKTIONEN 7.1/7.2. Mit echter Hardware und Spielen
  **nicht** geprüft (Flick-Stick braucht ein Spiel mit Maussteuerung; Werte der Testdrehung je Spiel).

## Offen

**Braucht den Nutzer (eigene Konten, nach außen sichtbar – nicht eigenmächtig erledigen):**
- **winget einreichen:** PR an `microsoft/winget-pkgs` unter dem Konto des Nutzers, Dateien aus `winget/`
  nach `manifests/d/DevCatSKZ/N-Connect/<Version>/` (Anleitung `winget/README.md`).
- **Code-Signatur:** Zertifikat besorgen (SignPath.io kostenlos für Open Source oder Microsoft Trusted Signing) und
  als Secrets `SIGNING_CERT_PFX`/`SIGNING_CERT_PASSWORD` im Repo N-Connect hinterlegen – CI signiert dann App und
  Installer (entfernt mit der Zeit die SmartScreen-Warnung). Danach möglich: echtes Windows-11-Widget.
- **Hardware-Tester** für HORI/PowerA/PDP-Kabelpads, DS4/DualSense, Xbox über Bluetooth (Aufruf steht im README).

**ESP32 als zweiter „Bluetooth-Stick“** (Nutzerwunsch 10.10.2026) – **Export fertig**, Firmware offen:
- Nutzer-Hardware: **ESP32-S3 N16R8 DevKitC-1** (USB 303A:1001 + CH343, eigenes Projekt `C:\Coding\ESP32 Pokemon\…`,
  ESP-IDF v6.1 unter `C:\Coding\esp-idf`). **Am ESP nichts ändern/flashen, serielle Schnittstelle nicht öffnen**
  (Nutzer arbeitet daran).
- Ziel: Switch-2-Controller am PC mit Stick **und** an einem anderen PC mit dem ESP32, ohne neues SYNC.
- Erkenntnis: Switch-2-Controller verbinden sich unverschlüsselt, werben nach SYNC nur für die Host-Adresse und lehnen
  fremde Hosts ab → ESP32 muss die **Adresse des PC-Sticks übernehmen** (`esp_iface_mac_addr_set(…, ESP_MAC_BT)` vor
  dem BT-Start; in IDF 6.1 vorhanden). Keine Schlüssel, kein Adminrecht nötig. Klassische Controller (Joy-Con 1, Pro 1,
  DS4/DS5, Wii) gehen mit dem S3 nicht (kein BR/EDR); Xbox-BLE bräuchte LTK/IRK – beides nicht umgesetzt.
- Umgesetzt: *Kopplungsdaten → Für ESP32 exportieren* (`Esp32Export`, ZIP mit JSON, `nconnect_pairing.h`,
  LIESMICH), `Settings.ControllerKinds` (Art je Adresse beim Verbinden), 8 Tests, Doku **docs/ESP32.md**
  (Prinzip, Format, Firmware-Leitfaden NimBLE, Fehlersuche).
- Offen: ESP32-Firmware (Weg A „Funkadapter für N-Connect“ per USB-CDC – braucht neue Verbindungsart in N-Connect –
  oder Weg B „eigenständiges USB-Gamepad“), Test mit echter Hardware.

**Technisch offen (bisheriger Stand):**
- **Flackernde Tests:** einmal 3 Fehlschläge, danach 7 Läufe hintereinander grün – Ursache nicht untersucht.
- **Joy-Con-1-Belegung im Spiel:** Nutzer meldete „funktionieren nicht korrekt im Spiel“; Ursache war sehr
  wahrscheinlich das versehentliche Trennen (s. o.). Rückmeldung des Nutzers, ob als Paar noch etwas falsch ist,
  steht aus. Tastenbits (`Switch1.cs`) und Paar-Zusammenführung (`Mapping.Merge`) wurden geprüft und sind korrekt.
- **Spieler-Reihenfolge/Aufrücken** mit echter Hardware und Steam noch nicht geprüft (nur Demo-Darstellung).
- **Akkuanzeige Switch 2:** Kennlinie (`InputReports.BatteryPercentFromMillivolts`) und Ladekorrektur
  (`BatteryEstimator`) sind am Pro Controller 2 gemessen (Ruhespannung 3704 mV ≈ 26 %, beim Laden +20 mV,
  Lade-Byte 0x21: `2C`/`3C`/`34` = lädt, `20` = Kabel steckt/Ladepause, `00` = kein Kabel). Für Joy-Con 2 und
  GameCube-Controller sind die Werte noch geschätzt; das Protokoll schreibt alle 30 s Spannung und Rohbytes
  0x1C–0x2F mit („Akku …“), gemessene Ladeanstiege landen in `battery.json`. Über einen kompletten Ladevorgang
  ist die Anzeige noch nicht beobachtet.
- **Selbst koppeln (Switch 1, NSO, Wii, PlayStation; Xbox nur im Koppelfenster)** ist gebaut (`ControllerPairing`,
  Hintergrundsuche in `TrayApp.AutoPairLoopAsync`, Einstellung `AutoPair`). Wii mit echter Hardware bestätigt (auch
  automatisch im Hintergrund). **Neu (05.10.):** Sony-Pads werben im Kopplungsmodus als „Wireless Controller“ und
  laufen über `PairJustWorks` (klassisch, auch im Hintergrund); Xbox-Controller werben per BLE („Xbox …“) und werden
  nur im Fenster „Controller koppeln“ über einen LE-Watcher + WinRT-Kopplung gekoppelt (`WatchXbox`/`PairXbox` –
  nur ungekoppelte, `IsPaired`-Test). Beide noch **nicht** mit Hardware geprüft. **Joy-Con 1 per SYNC
  bestätigt (04.10.2026, auch nach Zwischenstopp an der Switch)**: Switch 1/NSO werden über WinRT
  (`DeviceInformationCustomPairing`, Anfrage sofort bestätigt) gekoppelt – die Win32-Rückfrage kam bei Joy-Con zu
  spät (Fehler 1244/258). Bekannte Controller gelten als „im Kopplungsmodus“, wenn Windows sie während des Suchlaufs
  oder in den letzten 8 s gesehen hat – außer sie waren in den letzten 30 s verbunden (`NoteDisconnected`), sonst
  würde ein eben ausgeschalteter Controller neu gekoppelt. Nach Fehlschlag 90 s Pause im Hintergrund. Übersprungene
  bekannte Controller stehen im Protokoll („… nicht im Kopplungsmodus erkannt“). Koppeln dauert ~20 s; der erste
  HID-Öffnungsversuch danach scheitert manchmal („keine Antwort auf Unterbefehl 02“), der nächste nach ~6 s klappt.
  Neukoppeln abgesichert (FUNKTIONEN 2.3): Bestätigung (zweite Suche) nur im Hintergrund – im Fenster wird sofort
  entfernt und **bis zu ~8 s** neu gesucht, sonst zehrten die Suchläufe das kurze Wii-Kopplungsfenster (~20 s) vor dem
  Entfernen auf (genau das zerstörte am 04.10. eine funktionierende Wii-Kopplung: entfernt, dann „nicht wiedergefunden“,
  veralteter Versuch mit Fehler 259). Ohne Neufund wird nicht mehr mit veralteten Daten gekoppelt (war eh
  aussichtslos); beim nächsten SYNC zählt der Controller als neu. Mit Hardware noch **nicht** geprüft – zu testen:
  Wii einmal über die rote SYNC-Taste koppeln, dann aus/ein und nur eine normale Taste drücken (kein SYNC) – sie muss
  sich von selbst verbinden. Tut sie das nicht, liegt es nicht an N-Connect (Kopplung ist dann permanent gespeichert),
  sondern eher am Barrot-Stick oder an der Fernbedienung.
- **„Controller suchen …“ auf der Controller-Seite** (04.10., Nutzerwunsch): Knopf `GlyphButton` im Leerzustand
  (zentriert unter dem Hinweistext) und in einer Leiste am unteren Rand, sobald ≥ 1 Controller verbunden ist
  (`ControllerOverview`, Aktion `pair` = `TrayApp.ShowWiiPairing`; in der Prüfhilfe `--render-ui` als No-Op übergeben).
- **Xbox- und PlayStation-Controller mitverwalten** (04.10., Nutzerwunsch „perfekte Integration“, FUNKTIONEN 2.5):
  - **Xbox nativ über XInput** (`XInputLink`, `XInput.cs` dynamisch aus xinput1_4/9_1_0/1_3, Ordinale 100/108,
    ~125 Hz, 3 Fehlversuche bis „getrennt“): USB/Bluetooth/Adapter sind für XInput gleich – Karte zeigt „XInput“.
    `Link.Native/NativeSlot`: **kein** ViGEm-Pad (sonst doppelt), „Erscheint als“ entfällt, „Im Spiel: Xbox · Platz n ·
    nativ“. Eigene ViGEm-Slots werden per `XInput.SetVirtualSlot` markiert und nie als echt gemeldet.
  - **Sony über HID** (`PlayStationHidLink`, Protokoll `PlayStationPad`): DS4 (05C4/09CC/0BA0) und DS5/Edge
    (0CE6/0DF2), USB + Bluetooth, CRC32 (BT-Eingang + Ausgang mit Seed 0xA2), DS5-BT-Wechsel in den erweiterten
    Modus durch ersten Effekt-Bericht, Vibration/Lichtleiste/Spieler-LEDs/Mikro-LED, Seriennummer/Firmware per
    Feature-Bericht (DS4 0x12, DS5 0x09/0x20). Eigener virtueller DS4 wird an der Gerätehierarchie übersprungen.
    Sony steht auf der HidHide-Whitelist (inkl. Aufgaben-Helfer) – native Xbox nie.
  - `ControllerKind` neu: `DualShock4`, `DualSense`, `XboxController`; Standardbelegung positionsgetreu/Xbox
    (`DefaultTarget`), analoge Trigger laufen bei allen Arten durch (`Mapping.Normalize`), Touchpad-Klick → Aufnahme.
    Beschriftungen/Grafik in `InputView` + `Profiles.ControllerButtons`, Tuning (Sticks/Vibration/Gyro bei Sony),
    Demo-Links und Render-/UI-Prüfungen um Xbox/DS4/DS5 erweitert. Tests: 177 → **216**.
  - **Mit echter Hardware noch nicht geprüft** (wichtigste offene Prüfung!): DS4/DS5 über USB und Bluetooth
    (CRC-Annahme, DS5-Erweiterungsmodus, Rumble/Lichtleiste), Xbox über Bluetooth und Wireless-Adapter
    (XInput-Slot-Verhalten beim An-/Abstecken, Akkuarten), dass ViGEm-Slots nie als nativ auftauchen, Sony-Verstecken
    in Steam, XInput-Platz nach Wechseln.
- **Am 04.10.2026 gebaut, mit echter Hardware noch nicht (vollständig) geprüft:** Spielerplatz/Namen (Kartentitel),
  Stick-Kalibrierung (`StickCalibrationForm`, nur mit simulierten Controllern gesehen), Gyro-Assistent
  (`GyroSetupForm`), Untermenüs im Infobereich, Ein-Klick-Update (`UpdateCheck.DownloadAsync` – braucht ein erstes
  GitHub-Release), Kabel-Pads HORI/PowerA/PDP (`WiredPadLink`, Kennungen aus öffentlichen Listen), Nachbauten
  (Speicher nicht lesbar, leere Adresse), Joy-Con trennen (quer + SL/SR, Lageerkennung `Player.IsHeldSideways`
  bei Joy-Con 1 nicht geprüft), Wii „nur bei Änderung senden“
  mit Statusabfrage als Lebenszeichen, robustere Wii-Erweiterungserkennung.
- **Passive BLE-Suche** geprüft (Werkzeug im Scratchpad, N-Connect beendet): in 90 s keine Nintendo-Werbung empfangen,
  243 andere – nicht eindeutig (evtl. keine Taste gedrückt), daher **nicht** übernommen; die Suche bleibt aktiv.
- **Viele Controller auf einem Bluetooth-Stick** (gemessen 04.10.2026, Barrot BT 5.4, USB 33FA:0010): Ab drei bis vier
  Controllern (z. B. Wii-Fernbedienung + Joy-Con-2-Paar + Pro Controller 2) bekommt jeder nur ~10–11 Berichte/s,
  die Wii reagiert spürbar verzögert, ein weiterer Controller braucht ~9 s zum Verbinden. Vom Nutzer vorerst so gelassen.
  **Nicht wiederholen:** Ab drei Bluetooth-Controllern die Switch-2-Controller auf „ausgeglichen“ umzuschalten
  (Commit 24c4938, zurückgenommen) brachte keine höhere Rate, und weil schon ein Verbindungsversuch mitzählte,
  verhandelten bei jedem Versuch alle Verbindungen neu – der neue Controller scheiterte dann mit „Unreachable“.
  Umgesetzt: Wii sendet nur bei Änderung (Lebenszeichen per Statusabfrage). Verlässlichste Lösung:
  stärkerer Adapter (Intel AX200/AX210, Realtek RTL8761B). **Bestätigt:** Mit einem Realtek-Bluetooth-5.3-Stick
  (USB 0BDA:A725) sind die Probleme beim Nutzer weg.
  Achtung: Nach Herstellerbefehlen an den Stick (fremdes Werkzeug `tools/Switch2Pro.BtIdentityProbe`, nicht Teil von
  N-Connect) verband sich kein dritter Switch-2-Controller mehr – Ab- und Anstecken des Sticks hat das behoben.
- Angeboten, noch nicht entschieden: Switch-1-Pro-Controller aus dem verschlüsselten Spielstand `8000000000000050.bin` lesen; eigene Controller-Bilder
  (Nano-Banana-Prompts wurden geliefert) statt der gezeichneten Grafiken.

## Bewusst nicht umgesetzt

- **Bluetooth-Adresse des PC-Adapters auf die der Switch setzen** (damit Controller ohne neues SYNC an PC und
  Switch funktionieren): abgelehnt – Änderung der Adapter-Hardwareadresse per Herstellerbefehl.
- **Link-Keys direkt in die Windows-Registry (BTHPORT) schreiben:** nur mit SYSTEM-Rechten, undokumentiert, riskant.
  Die Kopplungsdaten-Funktion liest, zeigt, sichert und überträgt die Daten, verändert aber weder Adapter noch
  Windows-Kopplungen.

## Aufbau (Kurzüberblick)

```
src/Switch2Pro.Protocol   plattformunabhängig, getestet
  InputReports, Commands, Calibration, Rumble   Switch 2 (BLE/USB)
  Switch1, Wii, Mapping, Dsu, Settings          Switch 1, Wii, Belegung, Cemuhook, Einstellungen
  PlayStationPad, XboxPad                        DS4/DualSense (USB+BT, CRC, Effekte), XInput-Zustand
  BatteryEstimator                              Akku aus Spannung (Glättung, Ladekorrektur, keine Sprünge)
  SwitchPairingData, PairingTransfer            Bluepick-/hekate-Kopplungsdaten, .ncpair-Datei
src/Switch2Pro.Bridge     Windows-App (.NET 8 WinForms)
  Links/                  eine Verbindung je Controller (Switch2Ble/Usb, Switch1Hid, WiimoteHid, WiredPad,
                          PlayStationHid, XInput nativ, Demo)
  ControllerManager, Player, VirtualPads        Suche, Spielerplätze (lückenlos), ViGEm-Ausgabe
  PlayerOrderBar          Leiste „Spieler-Reihenfolge“ über den Karten
  BatteryTracker          Akkuschätzung je Seriennummer (überlebt Wechsel Bluetooth ↔ USB)
  Theme, Ui               Windows-11-Optik (Dunkel/Hell, Marken-Blau als Akzent, eigene Steuerelemente inkl. TextField)
  Branding                Logo (gezeichnet), LogoView, Icon/Installer-Grafiken (--render-brand)
  SettingsForm, ControllerOverview, MappingEditor, TuningEditor   Fenster, Karten, Einstellungen je Controller
  InputView*.cs           Controller-Grafiken; Umrisse aus Produktfotos (InputView.Outlines.cs, Werkzeug
                          zum Erzeugen lag im Scratchpad: Konturverfolgung + Douglas-Peucker)
  SwitchCardWatcher, PairingDataForm   SD-Karte, Kopplungsdaten
  ControllerPairing, PairForm          selbst koppeln (Switch 1, NSO, Wii), Fenster „Controller koppeln“
  StickCalibrationForm, GyroSetupForm  Stick-Kalibrierung, Gyro-Assistent
  Links/WiredPadLink                   Kabel-Pads HORI/PowerA/PDP (Protokoll: WiredSwitchPad)
  UpdateCheck                          Update-Prüfung und Ein-Klick-Update
installer/N-Connect.iss   Inno Setup 6 (ViGEmBus, HidHide immer mit, N-Connect dort freigegeben); den Autostart richtet die App selbst ein
                          (standardmäßig an, Einstellung „Mit Windows starten“)
```

Wichtige Gestaltungsregeln der Grafiken: Alle Controller nutzen dieselbe Bühne (`PhotoFrame`), Schultertasten
sind gewölbte Segmente **auf** der Gehäuse-Oberkante (`EdgeShoulder`, nebeneinander mit Spalt, kein Überlappen),
GL/GR sitzen auf den Griffen (`GripButton`), Joy-Con haben schwarze Tasten unabhängig von der Gehäusefarbe
(`_darkButtons`). Der Switch-1-Pro-Controller nutzt die Pro-2-Form, aber ohne C-Taste und GL/GR. Grafiken bleiben
**neutral**: keine Hersteller-/Plattformnamen oder -logos (kein Nintendo/SEGA/Wii/PS/Xbox-Schriftzug), Home-Tasten
tragen `⌂`; nur Funktionsnamen (L/R, ZL/ZR, A/B/X/Y, SELECT/START …) sind erlaubt. Beschriftungen laufen über
`InputView.Caption` (dreht bei gedrehtem Controller zurück, Pfeile ausgenommen) – Textgrößen sind fest in pt,
Positionen skalieren mit `PhotoFrame`, daher knappe Text-Abstände bei kleiner Kartenansicht vermeiden. Jede
Grafikänderung mit `--render` (Sammelbild aller Controller) prüfen.

Gestaltungsregeln der Oberfläche: nur Bausteine aus `Ui.cs` (keine Standard-`Button`/`TextBox` mit Rahmen; Textfelder
in `TextField` hüllen), Farben nur aus `Theme`, jeder sichtbare Text auf Deutsch im Code und mit englischem Eintrag in
`Tr.Texts.cs` (Texte mit Platzhaltern als Muster in `Tr.cs`); prüfen mit `--dump-ui <Datei> --en`.

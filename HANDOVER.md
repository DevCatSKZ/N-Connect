# Übergabe: N-Connect

Stand: 05.10.2026. Entwickelt im Repo `sfm` (Branch `ccr-ad005d0f-jon8df`, Ordner `switch2-pro-windows/`);
veröffentlicht als eigenes Repo **DevCatSKZ/N-Connect** (öffentlich) per `git subtree split`.
Was das Programm kann und wie man es baut: siehe [README.md](README.md). Vollständige Entwicklerdokumentation
(Funktionen und Verhalten, Architektur, Protokolle, Portierung): [docs/](docs/README.md).

## Arbeitsablauf nach jeder Änderung

```powershell
cd switch2-pro-windows
dotnet build -c Release                      # Warnungen gelten als Fehler
dotnet test -c Release --no-build            # aktuell 216 Tests, alle grün
N-Connect.exe --render <Ordner>              # alle Controller-Grafiken prüfen
N-Connect.exe --render-ui <Ordner> --demo-all --wide   # alle Seiten/Karten prüfen (auch --demo, --demo-retro)
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

- **Push nach GitHub blockiert:** Das Token (Konto `mrskittelz`, Scopes `gist, read:org, repo`) braucht die
  Berechtigung `workflow`, weil `.github/workflows/build.yml` mit hochgeladen wird. `gh auth refresh -h github.com
  -s workflow` zeigt einen Einmal-Code für https://github.com/login/device – das muss der **Nutzer** im Browser
  bestätigen (vom Agenten gestartet, Code `4479-589A`, nicht bestätigt; ggf. neu starten). Danach im Ordner
  `N-Connect`: `git push -u origin main` (Remote-Repo ist noch leer), dann Release anlegen, z. B.
  `gh release create v1.0.0 ..\sfm\switch2-pro-windows\out\N-Connect-Setup-1.0.0.exe` (oder Tag pushen – die
  Workflow-Datei baut bei `v*` selbst ein Release). Erst dann funktioniert das Ein-Klick-Update.
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
- **Selbst koppeln (Switch 1, NSO, Wii)** ist gebaut (`ControllerPairing`, Hintergrundsuche in `TrayApp.AutoPairLoopAsync`,
  Einstellung `AutoPair`). Wii mit echter Hardware bestätigt (auch automatisch im Hintergrund). **Joy-Con 1 per SYNC
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

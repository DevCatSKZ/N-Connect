# Übergabe: N-Connect

Stand: 04.10.2026. Entwickelt im Repo `sfm` (Branch `ccr-ad005d0f-jon8df`, Ordner `switch2-pro-windows/`);
veröffentlicht als eigenes Repo **DevCatSKZ/N-Connect** (öffentlich) per `git subtree split`.
Was das Programm kann und wie man es baut: siehe [README.md](README.md). Vollständige Entwicklerdokumentation
(Funktionen und Verhalten, Architektur, Protokolle, Portierung): [docs/](docs/README.md).

## Arbeitsablauf nach jeder Änderung

```powershell
cd switch2-pro-windows
dotnet build -c Release                      # Warnungen gelten als Fehler
dotnet test -c Release --no-build            # aktuell 166 Tests, alle grün
N-Connect.exe --render <Ordner>              # alle Controller-Grafiken prüfen
N-Connect.exe --render-ui <Ordner> --demo-all --wide   # alle Seiten/Karten prüfen (auch --demo, --demo-retro)
dotnet publish src\Switch2Pro.Bridge -c Release -r win-x64 --self-contained -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o out\publish\win-x64
ISCC.exe installer\N-Connect.iss             # Installer nach out\
cd ..; git subtree split --prefix=switch2-pro-windows -b n-connect-export
cd ..\N-Connect; git pull --ff-only ..\sfm n-connect-export
```

Commits als **devcatskz** (devcatskz@gmail.com), ohne „Co-Authored-By“-Zeile.

## Offen

- **Push nach GitHub blockiert:** Das Token braucht die Berechtigung `workflow`
  (`gh auth refresh -h github.com -s workflow`), dann `git push` im Ordner `N-Connect`.
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
  bekannte Controller stehen im Protokoll („… nicht im Kopplungsmodus erkannt“).
- **Am 04.10.2026 gebaut, mit echter Hardware noch nicht (vollständig) geprüft:** Spielerplatz/Namen (Kartentitel),
  Stick-Kalibrierung (`StickCalibrationForm`, nur mit simulierten Controllern gesehen), Gyro-Assistent
  (`GyroSetupForm`), Untermenüs im Infobereich, Ein-Klick-Update (`UpdateCheck.DownloadAsync` – braucht ein erstes
  GitHub-Release), Kabel-Pads HORI/PowerA/PDP (`WiredPadLink`, Kennungen aus öffentlichen Listen), Nachbauten
  (Speicher nicht lesbar, leere Adresse), Joy-Con automatisch trennen (quer + SL/SR), Wii „nur bei Änderung senden“
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
- Angeboten, noch nicht entschieden: Kopplungsdaten-Fenster (`PairingDataForm`) im Windows-11-Stil;
  Switch-1-Pro-Controller aus dem verschlüsselten Spielstand `8000000000000050.bin` lesen; eigene Controller-Bilder
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
  BatteryEstimator                              Akku aus Spannung (Glättung, Ladekorrektur, keine Sprünge)
  SwitchPairingData, PairingTransfer            Bluepick-/hekate-Kopplungsdaten, .ncpair-Datei
src/Switch2Pro.Bridge     Windows-App (.NET 8 WinForms)
  Links/                  eine Verbindung je Controller (Switch2Ble/Usb, Switch1Hid, WiimoteHid, Demo)
  ControllerManager, Player, VirtualPads        Suche, Spielerplätze, ViGEm-Ausgabe
  BatteryTracker          Akkuschätzung je Seriennummer (überlebt Wechsel Bluetooth ↔ USB)
  Theme, Ui               Windows-11-Optik (Akzentpalette, Dunkel/Hell, eigene Steuerelemente)
  SettingsForm, ControllerOverview, MappingEditor, TuningEditor   Fenster, Karten, Einstellungen je Controller
  InputView*.cs           Controller-Grafiken; Umrisse aus Produktfotos (InputView.Outlines.cs, Werkzeug
                          zum Erzeugen lag im Scratchpad: Konturverfolgung + Douglas-Peucker)
  SwitchCardWatcher, PairingDataForm   SD-Karte, Kopplungsdaten
  ControllerPairing, PairForm          selbst koppeln (Switch 1, NSO, Wii), Fenster „Controller koppeln“
  StickCalibrationForm, GyroSetupForm  Stick-Kalibrierung, Gyro-Assistent
  Links/WiredPadLink                   Kabel-Pads HORI/PowerA/PDP (Protokoll: WiredSwitchPad)
  UpdateCheck                          Update-Prüfung und Ein-Klick-Update
installer/N-Connect.iss   Inno Setup 6 (ViGEmBus, optional HidHide); den Autostart richtet die App selbst ein
                          (standardmäßig an, Einstellung „Mit Windows starten“)
```

Wichtige Gestaltungsregeln der Grafiken: Alle Controller nutzen dieselbe Bühne (`PhotoFrame`), Schultertasten
liegen hinter der Gehäuse-Oberkante (`Shoulder(..., tucked: true)`), GL/GR sitzen auf den Griffen (`GripButton`),
Joy-Con haben schwarze Tasten unabhängig von der Gehäusefarbe (`_darkButtons`). Der Switch-1-Pro-Controller nutzt
die Pro-2-Form, aber ohne C-Taste und GL/GR.

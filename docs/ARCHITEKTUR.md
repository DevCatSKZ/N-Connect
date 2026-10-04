# Architektur

## 1. Projekte

```
src/Switch2Pro.Protocol     .NET-Bibliothek ohne Windows-Abhängigkeit – das Wissen über die Controller
src/Switch2Pro.Bridge       Windows-App (WinForms, .NET 8, net8.0-windows10.0.22621): Verbindungen, Ausgabe, Oberfläche
tests/Switch2Pro.Protocol.Tests   xUnit-Tests für das Protokoll-Projekt (166 Tests)
installer/N-Connect.iss     Inno Setup 6 (installiert ViGEmBus, optional HidHide)
.github/workflows/build.yml Build, Tests, Installer; Tag „v*“ → GitHub-Release mit N-Connect-Setup-*.exe
```

**Regel für Portierungen:** Alles, was Bytes zerlegt, Werte umrechnet oder entscheidet, gehört ins
Protokoll-Projekt (plattformunabhängig, getestet). Die Bridge enthält nur Betriebssystem-Anbindung und Oberfläche.

### Protokoll-Projekt (plattformunabhängig)

| Datei | Inhalt |
|---|---|
| `ControllerState.cs` | `ProButtons` (Tastenbits), `ControllerKind`, `Motion`, `ControllerState` (Rohzustand) |
| `Gatt.cs`, `Advertisement.cs`, `Commands.cs`, `InputReports.cs`, `Pairing.cs`, `Rumble.cs`, `Calibration.cs` | Switch 2 (BLE/USB) |
| `Switch1.cs`, `Nfc.cs`, `IrCamera.cs` | Switch 1 / NSO (HID-Berichte, Unterbefehle, IMU, Rumble, amiibo, Ring-Con, IR-Kamera) |
| `Wii.cs` | Wii-Fernbedienung, Erweiterungen, MotionPlus, IR-Zeiger, Wii U Pro |
| `WiredSwitchPad.cs` | Kabel-Pads HORI/PowerA/PDP |
| `Mapping.cs` | Normalisieren, Paar zusammenführen, Belegung auswerten, Sticks/Trigger, Gyro→Stick, DS4-Bericht |
| `Profiles.cs`, `Macro.cs` | Tastenaktionen, Tastenlisten/-namen je Controller, Makros |
| `Settings.cs` | Alle Einstellungen (JSON), Aufräumen beim Laden |
| `BatteryEstimator.cs`, `StickCalibrator.cs` | Akku aus Spannung, geführte Stick-Kalibrierung |
| `Dsu.cs` | Cemuhook/DSU-Pakete |
| `SwitchPairingData.cs`, `PairingTransfer.cs` | Kopplungsdaten von der SD-Karte, `.ncpair`-Datei |

### Bridge (Windows)

| Bereich | Dateien |
|---|---|
| Verbindungen (`Links/`) | `IControllerLink`, `Switch2BleLink`, `Switch2UsbLink`, `Switch1HidLink`, `WiimoteHidLink`, `WiredPadLink`, `DemoLink` |
| Geräte-Zugriff | `Usb/UsbNative` (SetupAPI), `Usb/UsbChannels` (`WinUsbChannel`, `HidChannel`), WinRT-Bluetooth |
| Steuerung | `ControllerManager` (Suche, Verbinden, Spieler), `Player` (ein virtueller Controller), `ControllerPairing` (Classic-Kopplung) |
| Ausgabe | `VirtualPads` (ViGEm Xbox 360 / DS4), `WindowsInput` (Tastatur/Maus per SendInput), `SmoothMouse`, `JoyConMouse`, `DsuServer`, `HidHide` |
| Zusatz | `BatteryTracker`, `UpdateCheck`, `SwitchCardWatcher`, `Log`, `Autostart` (in `Dialogs.cs`) |
| Oberfläche | `TrayApp` (Infobereich, Lebenszyklus), `SettingsForm`, `ControllerOverview` (Karten), `InputView*` (Grafiken), `MappingEditor`, `TuningEditor`, Dialoge, `Theme`, `Ui`, `Tr` (Übersetzung) |

---

## 2. Datenfluss einer Eingabe

```
Funk/Kabel ──► Link (Thread des Links)
                 │  Bericht zerlegen (Protocol: InputReports / Switch1 / WiiParser / WiredSwitchPad)
                 │  Switch 2: Akku schätzen (BatteryTracker → BatteryEstimator)
                 ▼
            ControllerState (roh: Tasten, 12-Bit-Sticks, Motion in Switch-2-Achsen, Akku)
                 │  Ereignis StateReceived
                 ▼
            Player.OnState
                 │  Joy-Con 2: Mausmodus (JoyConMouse) – nimmt Maustasten heraus
                 │  Kalibrierung: Link-Werte + eigene Stick-Kalibrierung + Gyro-Nullpunkt (Player.CalibrationFor)
                 │  Mapping.Normalize (1 Link) bzw. Mapping.Merge (Joy-Con-Paar)
                 │  Gesten: Paar trennen (SL/SR quer bzw. SL+SR 1 s) / zusammenfügen (L+R)
                 ▼
            PadInput (einheitlich: Pro-Schema, Sticks −1…1, Trigger 0…1, Motion ohne Nullpunktfehler)
                 │  Mapping.Evaluate: Profil, Shift, Turbo, Makros, Totzone/Kennlinie, Trigger-Schwelle
                 ▼
            Output (GamepadState im Xbox-Schema + Hotkeys + Sonderaktionen + Makros)
                 │  Makros abspielen, Sonderaktionen (Maus, Gyro-Maus), Wii-Zeiger, Gyro → rechter Stick
                 ▼
            IVirtualPad.Update (ViGEm: Xbox 360 oder DS4-Bericht mit Bewegungsdaten)
            WindowsInput (Hotkeys/Maus) · DsuServer.Publish (UDP) · Akku-Warnung · Changed (Oberfläche)
```

Rückweg: Spiel → ViGEm-Vibration (`IVirtualPad.Rumble`) → `Player.OnGameRumble` (× Stärke je Controller-Art) →
`IControllerLink.SetRumble` → jede Verbindung wiederholt die Vibration selbst in ihrem Takt (Switch 2 BLE ~12 ms).

Die Oberfläche liest den Zustand selbst (60-mal pro Sekunde, `SettingsForm._liveTimer`) über
`ControllerOverview.LiveInput` – dieselbe Kette bis `Mapping.ToGamepad`, damit Anzeige und Spiel übereinstimmen.

---

## 3. Abläufe

### 3.1 Start (`TrayApp`)
1. Einstellungen laden (kaputte Datei → Standardwerte, Datei bleibt), Sprache und Darstellung setzen,
   Autostart einmalig einrichten.
2. ViGEmBus verbinden (`PadFactory.TryCreate`); fehlt er: Hinweis, keine Controller.
3. `ControllerManager.StartAsync`: Bluetooth-Adapter prüfen (alle 5 s bei Problemen), BLE-Suche starten,
   HID-/USB-Suchlauf alle 2 s, Inaktivitätsprüfung alle 10 s.
4. DSU-Server (UDP 26760), Hintergrund-Kopplung, Profilerkennung (1 s), Update-Prüfung (nach 10 s).
5. Fenster zeigen (nicht bei `--autostart`); Einstellungsdatei überwachen (Änderungen von außen übernehmen).

### 3.2 Verbinden (`ControllerManager`)
- **BLE**: Werbung → Art + Host-Adresse prüfen (siehe FUNKTIONEN 2.1) → `Switch2BleLink.ConnectAsync`
  (Gerät öffnen, GATT-Sitzung mit `MaintainConnection`, schnelles Verbindungsintervall anfordern, Dienst und
  Merkmale suchen, Antwortkanal abonnieren, Feature-Maske setzen/einschalten, Eingabekanal abonnieren,
  Watchdog und Vibrations-Schleife starten, Kalibrierung/Gerätedaten lesen) → `Attach` → nach SYNC
  `PairWithHostAsync`.
- **HID** (Classic/USB-HID): Gerätepfade aller HID-Schnittstellen → Art aus VID/PID (`Switch1Devices`,
  `WiredSwitchPad`) → passender Link → `Attach`. Fehlschläge: still alle 5 s neu (gekoppelte, aber
  ausgeschaltete Controller bleiben in Windows sichtbar).
- **USB (Switch 2)**: WinUSB-Schnittstelle (Interface 1) + HID (Interface 0) → `Switch2UsbLink.OpenAsync`.
- **Attach**: Ist derselbe Controller schon per USB da → Bluetooth-Verbindung verwerfen. Sonst Joy-Con-Partner
  suchen (Paar) oder neuen Spieler auf dem gemerkten bzw. ersten freien Platz anlegen (virtueller Controller).

### 3.3 Spielerverwaltung
- `Player` besitzt den virtuellen Controller; `Index` 0–7 bestimmt LEDs und DSU-Slot.
- Umsortieren (`MovePlayer`): Indizes tauschen → alle virtuellen Controller abbauen → in neuer Reihenfolge anlegen
  (Windows vergibt XInput-Plätze nach Anlegereihenfolge) → Plätze je Controller speichern.
- Paar trennen/zusammenfügen: `Split`/`Merge` verschieben Links zwischen Spielern; der kleinere Index bleibt.

### 3.4 Kopplung Classic (`ControllerPairing`)
Siehe FUNKTIONEN 2.3 und PROTOKOLLE 6. Läuft nie parallel (Semaphore) und im Hintergrund nur unter den
dort genannten Bedingungen (`TrayApp.CanScanInBackground`).

---

## 4. Nebenläufigkeit

- Jeder Link liest auf eigenem Thread (WinRT-Ereignisse bzw. `Task.Run`-Leseschleife); `Player.OnState` läuft
  dort. Mehrere Links eines Paars: Sperre `_gate` im Player; Ausgabe an den virtuellen Controller unter `_output`.
- **Einstellungen** werden von Link-Threads gelesen und von der Oberfläche geändert: Listen und Wörterbücher
  werden **nie verändert, sondern ersetzt** (Kopie schreiben, Referenz tauschen) – siehe Kommentar in `Settings`.
- Oberfläche nur im UI-Thread (`SynchronizationContext.Post`). Hintergrundaufgaben mit `Forget(...)`
  (Fehler werden protokolliert statt zu verschwinden).

## 5. Einstellungen und Dateien

| Datei | Inhalt |
|---|---|
| `%APPDATA%\N-Connect\settings.json` | `Settings` als JSON (Enums als Text), siehe `Settings.cs` |
| `%LOCALAPPDATA%\N-Connect\bridge.log` | Protokoll |
| `%LOCALAPPDATA%\N-Connect\battery.json` | Gemessener Spannungssprung beim Laden je Seriennummer |
| `*.ncprofile.json` | Exportiertes Profil (`NamedProfile`) |
| `*.ncpair` | Kopplungsdaten PC → PC (`PairingTransfer`) |

Schlüssel „je Controller“ ist die **Adresse** (`IControllerLink.Address`): Bluetooth-Adresse `AA:BB:…`, bei USB
`USB:<Seriennummer>` bzw. `USB:<HID-Instanz>`, bei Nachbauten ohne Adresse `HID:<Instanz>`. Daran hängen Namen,
Spielerplätze, Stick- und Gyro-Kalibrierung, Einzel-/Hochkant-Joy-Con.

## 6. Oberfläche

WinForms mit eigenen, selbst gezeichneten Steuerelementen im Windows-11-Stil (`Ui.cs`: `StackPanel`,
`SettingsGroup`, `SettingRow`, `ToggleSwitch`, `Slider`, `Segmented`, `GlyphButton`, `PivotTabs`, `NavItem`,
`ScrollPage`; Symbole aus „Segoe MDL2 Assets“). Farben zentral in `Theme` (Paletten dunkel/hell, Akzentfarbe aus
der Windows-Akzentpalette). Controller-Grafiken (`InputView*`) sind gezeichnet: Umrisse aus Produktfotos
extrahiert (`InputView.Outlines.cs`, Punktlisten in Foto-Pixeln, `PhotoFrame` rechnet auf die Bühne um).
Übersetzung: `Tr.T(deutscher Text)` mit Tabelle (`Tr.Texts.cs`) und Mustern für Texte mit Platzhaltern (`Tr.cs`).

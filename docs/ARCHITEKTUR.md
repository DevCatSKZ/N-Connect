# Architektur

Stand: 1.0.22 (Windows), 0.9.1 (Android), 0.4.4 (Kodi). Alle .NET-Projekte zielen auf .NET 10.

## 1. Projekte und Ordner

```
src/Switch2Pro.Protocol           Bibliothek ohne Betriebssystem-Abhängigkeit (net10.0): das Wissen über die Controller,
                                  Belegung, Einstellungen, DSU – genutzt von Windows, Android und Linux
src/Switch2Pro.Bridge             Windows-App „N-Connect“ (WinForms, net10.0-windows10.0.22621.0): Verbindungen,
                                  virtuelle Controller über ViGEmBus, Oberfläche
android/NConnect.Android          N-Connect für Android (net10.0-android, minSdk 26) – siehe ANDROID.md
android/NConnect.Android.Tests    xUnit-Tests für die Android-freien Teile (net10.0, ohne Android-Workload)
android/i18n                      Übersetzungstabellen der Android-App und generate.py
linux/NConnect.Linux              Linux-Dienst „nconnect“ (net10.0, BlueZ über D-Bus, uinput) – siehe LINUX-KODI.md
linux/kodi                        Kodi-Add-ons (service.nconnect, plugin.nconnect.status, script.nconnect.bundle,
                                  repository.devcatskz), Übersetzungen, Index-Werkzeug, Python-Tests
linux/build-kodi-addon.ps1        baut den Dienst für x64/arm64/arm und packt die Kodi-ZIPs
tests/Switch2Pro.Protocol.Tests   xUnit-Tests der Bibliothek (net10.0)
tests/Switch2Pro.Bridge.Tests     xUnit-Tests der Windows-App (net10.0-windows): Gerätekette, Kodi-Statusdatei,
                                  Sprache/Darstellung, alte Einstellungsfelder, Übersetzungen, Lizenzhinweise, Versionen
tests/NConnect.Linux.Tests        xUnit-Tests des Linux-Dienstes (net10.0, laufen auch unter Windows)
tools/Switch2Pro.BleProbe         Diagnose: Switch-2-Controller per BLE suchen, verbinden, Berichte ausgeben
tools/Switch2Pro.UsbProbe         Diagnose: Switch-2-Controller am USB-Kabel (bindet Usb/UsbNative.cs der App ein)
tools/Switch2Pro.BtIdentityProbe  Diagnose zum ruhenden Vorhaben „Switch-Identität“ (Herstellerbefehle nur mit Freigabe)
installer/N-Connect.iss           Inno Setup 6: installiert ViGEmBus und HidHide, gibt N-Connect in HidHide frei, legt die
                                  Aufgabe „N-Connect HidHide“ an (Verstecken ohne UAC), übergibt Sprache/Darstellung
.github/workflows/build.yml       Windows: alle Projekte der Projektmappe bauen und testen, Oberfläche rendern, Setup und
                                  Portable-ZIP; Job „Android kompilieren“; Tag „v*“ → GitHub-Release
.github/workflows/kodi.yml        Linux-Dienst und Kodi-Add-ons: Tests, ZIPs als Artefakt, bei Tags am Release
winget/                           Manifeste für den Windows Package Manager
firmware/nrf52840/                verworfene Funkadapter-Firmware, nur noch als BLE-Referenz
docs/                             Entwicklerdoku (Übersicht: README.md), docs/archiv/ für verworfene Wege
```

`Switch2Pro.sln` enthält alle Projekte außer der Android-App selbst; `dotnet build`/`dotnet test` der Projektmappe
braucht deshalb keine Android-Workload.

**Regel:** Alles, was Bytes zerlegt, Werte umrechnet oder entscheidet, gehört in die Bibliothek
(plattformunabhängig, getestet). Die Apps enthalten nur die Anbindung ans Betriebssystem und die Oberfläche.

### Bibliothek `Switch2Pro.Protocol`

| Datei | Inhalt |
|---|---|
| `ControllerState.cs` | `ProButtons` (Tastenbits), `ControllerKind`, `Motion`, `ControllerState` (Rohzustand) |
| `Gatt.cs`, `Advertisement.cs`, `Commands.cs`, `InputReports.cs`, `Pairing.cs`, `Rumble.cs`, `Calibration.cs` | Switch 2 (BLE/USB): GATT-Merkmale, Werbung, Befehle, Bericht 0x05/0x09, Nintendo-Kopplung (0x15), Vibration, Kalibrierung |
| `Switch1.cs`, `Nfc.cs`, `IrCamera.cs` | Switch 1 / NSO (HID-Berichte, Unterbefehle, IMU, Rumble, amiibo, Ring-Con, IR-Kamera) |
| `Wii.cs` | Wii-Fernbedienung, Erweiterungen, MotionPlus, IR-Zeiger, Wii U Pro |
| `WiredSwitchPad.cs` | Kabel-Pads HORI/PowerA/PDP |
| `PlayStationPad.cs` | DualShock 4 / DualSense (Edge): USB- und BT-Berichte, CRC32, Effekt-Berichte, Seriennummer/Firmware |
| `XboxPad.cs` | XInput-Zustand → `ControllerState`, Akku, Modellname aus VID/PID |
| `XboxBtReport.cs` | Virtueller Xbox-One-S-Controller (045E:02FD) als HID-Gerät: Deskriptor, Bericht, UHID-Befehle – Ausgabe unter Android |
| `Mapping.cs` | Normalisieren, Paar zusammenführen, Belegung auswerten, Sticks/Trigger, Gyro → Stick, DS4-Bericht |
| `FlickStick.cs` | Flick-Stick nach JoyShockMapper |
| `Profiles.cs`, `Macro.cs` | Tastenaktionen, Tastenlisten und -namen je Controller, Makros |
| `Settings.cs` | Alle Einstellungen (JSON), Aufräumen beim Laden, Sicherung (`.bak`, unlesbare Datei als `.unlesbar-<Zeit>`), toleranter Enum-Konverter |
| `ColorSchemes.cs` | Die fünf Farbschemata, gemeinsame Quelle für Windows und Android |
| `BatteryEstimator.cs`, `StickCalibrator.cs` | Akku aus Spannung, geführte Stick-Kalibrierung |
| `Dsu.cs`, `DsuServer.cs` | Cemuhook/DSU-Pakete und der UDP-Server (Port 26760, nur lokal oder im Heimnetz) für alle drei Apps |
| `SwitchPairingData.cs`, `PairingTransfer.cs` | Kopplungsdaten von der Switch-SD-Karte, `.ncpair`-Datei |
| `SwitchBtSave.cs` | Bluetooth-Speicher der Switch (`8000000000000050.bin`) – ruhendes Vorhaben „Switch-Identität“ |
| `WindowsBtDevice.cs` | In Windows gekoppelte Geräte und Art aus dem Namen; seit dem Entfernen des ESP32-Exports nur noch von Tests genutzt |

Entfernt in 1.0.22: `AdapterProtocol.cs` und `Esp32Export.cs` (verworfener Funkadapter).

### Windows-App `Switch2Pro.Bridge`

| Bereich | Dateien |
|---|---|
| Verbindungen (`Links/`) | `IControllerLink`, `Switch2BleLink`, `Switch2UsbLink`, `Switch1HidLink`, `WiimoteHidLink`, `WiredPadLink`, `PlayStationHidLink` (DS4/DS5 über USB/BT), `XInputLink` (nativer Xbox), `DemoLink` |
| Geräte-Zugriff | `Usb/UsbNative` (SetupAPI, Gerätekette), `Usb/DeviceTree` (echtes oder virtuelles Gerät, erkennt eigene ViGEm-Pads), `Usb/UsbChannels` (`WinUsbChannel`, `HidChannel`), `XInput` (xinput1_4/9_1_0/1_3 dynamisch, Ordinale 100/108, Markierung eigener Slots), WinRT-Bluetooth |
| Steuerung | `ControllerManager` (Suche, Verbinden, Spieler, Bluetooth-Zustand), `Player` (ein virtueller Controller), `ControllerPairing` (Classic-Kopplung), `DesktopMode` (`DesktopControl`, Desktop-Modus) |
| Ausgabe | `VirtualPads` (ViGEm Xbox 360 / DS4, `PadFactory`), `WindowsInput` (Tastatur/Maus per SendInput), `SmoothMouse`, `JoyConMouse`, `HidHide`, `HidHideUpdate`; DSU über `DsuServer` aus der Bibliothek |
| Kodi | `KodiIntegration` (Repository in Kodi einrichten, herunterladen), `KodiStatusWriter` (status.json für das Add-on) |
| Zusatz | `BatteryTracker`, `UpdateCheck` (auch Vorabversionen `-ci.N`), `SwitchCardWatcher`, `Diagnostics`, `InstallerChoices` (Sprache/Darstellung aus dem Setup), `ToastSenders` (Windows-Gerätemeldungen stummschalten), `Log` (mit `Paths`, portabler Betrieb), `TaskExtensions` (`Forget`), `Autostart` (in `Dialogs.cs`) |
| Oberfläche | `Program` (Einstieg: Prüfhilfen oder App, nur eine Instanz), `TrayApp` (Infobereich, Lebenszyklus), `SettingsForm`, `ControllerOverview` (Karten), `StatusBand`, `PlayerOrderBar`, `ControllerTestView`, `ControllerWidget`, `ConnectOverlay`, `InputView*` (Grafiken), `MappingEditor`, `TuningEditor`, `PairingDataForm`, `PairForm`, `StickCalibrationForm`, `GyroSetupForm`, `IrCameraForm`, `WelcomeForm`, `SchemePicker`, Dialoge, `Theme`, `Ui`, `Anim`, `Branding`, `Tr` (Übersetzung) |
| Prüfhilfen | `RenderCheck` (`--render`, `--render-ui`, `--dump-ui`), `HiddenDesktop` (Prüfhilfen auf unsichtbarem Desktop) |

Entfernt in 1.0.22: `AdapterConnection`, `Links/AdapterLink`, `WindowsBtDevices` (Funkadapter), `DsuServer` (jetzt in
der Bibliothek), die ungenutzten Klassen `NavButton`, `Section` und `LogoView`.

### Android und Linux

Beide nutzen die Bibliothek unverändert über eine Projektreferenz und bringen eigene, kleinere Fassungen von `Player`
und der Verbindung zum Switch-2-Controller mit. Aufbau, Dateien und Datenfluss: [ANDROID.md](ANDROID.md) und
[LINUX-KODI.md](LINUX-KODI.md). Die Kodi-Add-ons sind Python; Windows und Android schreiben dieselbe `status.json`
wie der Linux-Dienst, damit Menü und Widget des Add-ons auf jeder Plattform gleich funktionieren.

---

## 2. Datenfluss einer Eingabe (Windows)

```
Funk/Kabel ──► Link (Thread des Links)
                 │  Bericht zerlegen (Protocol: InputReports / Switch1 / WiiParser / WiredSwitchPad /
                 │  PlayStationPad; XInputLink pollt den XInput-Zustand selbst)
                 │  Switch 2: Akku schätzen (BatteryTracker → BatteryEstimator)
                 ▼
            ControllerState (roh: Tasten, 12-Bit-Sticks, Motion in Switch-2-Achsen, Akku)
                 │  Ereignis StateReceived
                 ▼
            Player.OnState
                 │  Joy-Con 2: Mausmodus (JoyConMouse) – nimmt Maustasten heraus
                 │  Kalibrierung: Link-Werte + eigene Stick-Kalibrierung + Gyro-Nullpunkt (Player.CalibrationFor)
                 │  Mapping.Normalize (1 Link) bzw. Mapping.Merge (Joy-Con-Paar)
                 │  Gesten: Paar trennen (quer gehalten + SL/SR) / zusammenfügen (L+R)
                 ▼
            PadInput (einheitlich: Pro-Schema, Sticks −1…1, Trigger 0…1, Motion ohne Nullpunktfehler)
                 │  Mapping.Evaluate: Profil, Shift, Turbo, Makros, Totzone/Kennlinie, Trigger-Schwelle
                 ▼
            Output (GamepadState im Xbox-Schema + Hotkeys + Sonderaktionen + Makros)
                 │  Makros abspielen, Sonderaktionen (Maus, Gyro-Maus), Wii-Zeiger, Gyro → rechter Stick,
                 │  Flick-Stick, Desktop-Modus
                 ▼
            IVirtualPad.Update (ViGEm: Xbox 360 oder DS4-Bericht mit Bewegungsdaten)
            WindowsInput (Hotkeys/Maus) · DsuServer.Publish (UDP) · Akku-Warnung · Changed (Oberfläche)
```

Rückweg: Spiel → ViGEm-Vibration (`IVirtualPad.Rumble`) → `Player.OnGameRumble` (× Stärke je Controller-Art) →
`IControllerLink.SetRumble` → jede Verbindung wiederholt die Vibration selbst in ihrem Takt (Switch 2 BLE ~12 ms,
Switch 1 über Bericht 0x10 alle 30 ms, Sony über eine eigene Sende-Schleife).

Die Oberfläche liest den Zustand selbst (60-mal pro Sekunde, `SettingsForm._liveTimer`) über
`ControllerOverview.LiveInput` – dieselbe Kette bis `Mapping.ToGamepad`, damit Anzeige und Spiel übereinstimmen.

---

## 3. Abläufe (Windows)

### 3.1 Start (`TrayApp`)
1. Einstellungen laden: unlesbare Datei → Sicherung `settings.json.bak` oder Standardwerte, Hinweis beim Start; die
   unlesbare Datei wird vor dem ersten Speichern als `settings.json.unlesbar-<Zeit>` aufbewahrt. Sprache und
   Darstellung setzen (Setup-Argumente über `InstallerChoices`), Autostart einmalig einrichten.
2. Treiberversionen von ViGEmBus und HidHide ins Protokoll; ViGEmBus verbinden (`PadFactory.TryCreate`). Fehlt er:
   Hinweis, keine Controller.
3. `ControllerManager.StartAsync`: Bluetooth-Adapter prüfen (alle 5 s bei Problemen), BLE-Suche starten,
   HID-/USB-Suchlauf alle 2 s, Inaktivitätsprüfung alle 10 s.
4. DSU-Server (UDP 127.0.0.1:26760; Port belegt → ohne DSU weiter), Hintergrund-Kopplung, `KodiStatusWriter`,
   Profilerkennung (1 s), Update-Prüfung (nach 10 s).
5. Fenster zeigen (nicht bei `--autostart`); Einstellungsdatei überwachen (Änderungen von außen übernehmen; eine
   unlesbare Fassung verwirft das Neuladen und behält die laufenden Werte).

### 3.2 Verbinden (`ControllerManager`)
- **BLE**: Werbung → Art + Host-Adresse prüfen (siehe FUNKTIONEN 2.1) → `Switch2BleLink.ConnectAsync`
  (Gerät öffnen, GATT-Sitzung mit `MaintainConnection`, schnelles Verbindungsintervall anfordern, Dienst und
  Merkmale suchen, Antwortkanal abonnieren, Feature-Maske setzen/einschalten, Eingabekanal abonnieren,
  Watchdog und Vibrations-Schleife starten, Kalibrierung/Gerätedaten lesen) → `Attach` → nach SYNC
  `PairWithHostAsync`.
- **HID** (Classic/USB-HID): Gerätepfade aller HID-Schnittstellen → Art aus VID/PID (`Switch1Devices`,
  `WiredSwitchPad`, `PlayStationPad` für Sony) → passender Link → `Attach`. Fehlschläge: still alle 5 s neu
  (gekoppelte, aber ausgeschaltete Controller bleiben in Windows sichtbar). Virtuelle Geräte – auch die eigenen
  ViGEm-DualShock-4 – erkennt `Usb/DeviceTree` an der ganzen Gerätekette bis zur Wurzel (ViGEmBus-Dienst oder
  -Hardware-ID, `ROOT\`/`SWD\` über der USB-Kette); sie werden weder angehängt noch versteckt noch beschrieben.
- **XInput**: alle vier Plätze im selben Suchlauf; `XInput.IsVirtual` überspringt selbst angelegte ViGEm-Slots;
  verbunden → `XInputLink` (`Native = true`, `Player` legt kein virtuelles Pad an).
- **USB (Switch 2)**: WinUSB-Schnittstelle (Interface 1) + HID (Interface 0) → `Switch2UsbLink.OpenAsync`.
- **Attach**: Ist derselbe Controller schon per USB da → Bluetooth-Verbindung verwerfen. Sonst Joy-Con-Partner
  suchen (Paar) oder neuen Spieler auf dem gemerkten bzw. ersten freien Platz anlegen (virtueller Controller). Sind
  alle acht Plätze belegt, wird derselbe Controller 10 s lang nicht neu versucht; die Meldung kommt je Gerät einmal.

### 3.3 Spielerverwaltung
- `Player` besitzt den virtuellen Controller; `Index` 0–7 bestimmt LEDs und DSU-Slot. Ausnahme native Links
  (Xbox/XInput): kein virtuelles Pad, `GameSlot` = `NativeSlot`; Lichtleiste vom virtuellen DS4 läuft bei Sony
  über `SetLightbarAsync` zurück an den physischen Controller.
- Umsortieren (`MovePlayer`, Ziel höchstens letzter belegter Platz): Indizes tauschen → alle virtuellen Controller
  abbauen → in neuer Reihenfolge anlegen (Windows vergibt XInput-Plätze nach Anlegereihenfolge) → Plätze je
  Controller speichern.
- Lückenlos (`CompactPlayers`, nach Wegfall eines Spielers in `OnLinkLost` und `Merge`): Indizes 0…n−1 neu
  vergeben, virtuelle Controller wie oben neu anlegen, gemerkte Plätze **nicht** überschreiben. `FreeIndex` nimmt
  den gemerkten Platz nur ohne Lücke.
- Paar trennen/zusammenfügen: `Split`/`Merge` verschieben Links zwischen Spielern; der kleinere Index bleibt.

### 3.4 Kopplung Classic (`ControllerPairing`)
Siehe FUNKTIONEN 2.3 und PROTOKOLLE 6. Läuft nie parallel (Semaphore) und im Hintergrund nur unter den
dort genannten Bedingungen (`TrayApp.CanScanInBackground`). Ablauf je Suchlauf (`ScanOnce`): bekannte Geräte
merken (`stLastSeen`, verbundene → `LastConnected`) → kurze Suche → je Nintendo-Gerät: verbunden/kürzlich verbunden/
nach Fehlschlag in Pause → überspringen; bekannt und nicht im SYNC-Modus → überspringen (protokolliert); sonst alte
Kopplung entfernen → Wii: `PairWii` (binäre PIN), sonst `PairJustWorks` (WinRT, Win32 als Ersatz) → HID-Dienst an.

---

## 4. Nebenläufigkeit

- Jeder Link liest auf eigenem Thread (WinRT-Ereignisse bzw. `Task.Run`-Leseschleife); `Player.OnState` läuft
  dort. Mehrere Links eines Paars: Sperre `_gate` im Player; Ausgabe an den virtuellen Controller unter `_output`.
- **Einstellungen** werden von Link-Threads gelesen und von der Oberfläche geändert: Listen und Wörterbücher
  werden **nie verändert, sondern ersetzt** (Kopie schreiben, Referenz tauschen) – siehe Kommentar in `Settings`.
- Oberfläche nur im UI-Thread (`SynchronizationContext.Post`). Hintergrundaufgaben mit `Forget(...)`
  (Fehler werden protokolliert statt zu verschwinden).
- Sende-Schleifen statt blockierender Schreibzugriffe in Rückruf-Threads (Vibration, Sony-Effekte): Der ViGEm-Rückkanal
  darf nie warten.

## 5. Einstellungen und Dateien

| Datei | Inhalt |
|---|---|
| `%APPDATA%\N-Connect\settings.json` | `Settings` als JSON (Enums als Text), siehe `Settings.cs`; daneben `settings.json.bak` und ggf. `settings.json.unlesbar-<Zeit>` |
| `%LOCALAPPDATA%\N-Connect\bridge.log` | Protokoll (ab 1 MB einmal rotiert) |
| `%LOCALAPPDATA%\N-Connect\battery.json` | Gemessener Spannungssprung beim Laden je Seriennummer |
| `<Kodi>\userdata\addon_data\service.nconnect\status.json` | Status für das Kodi-Add-on (nur, wenn es installiert ist) |
| `*.ncprofile.json` | Exportiertes Profil (`NamedProfile`) |
| `*.ncpair` | Kopplungsdaten PC → PC (`PairingTransfer`) |

Portabel (neben der EXE liegt `portable.txt` oder ein Ordner `data`): Einstellungen, Protokoll und Sicherungen liegen
in `data\`.

Schlüssel „je Controller“ ist die **Adresse** (`IControllerLink.Address`): Bluetooth-Adresse `AA:BB:…`, bei USB
`USB:<Seriennummer>` bzw. `USB:<HID-Instanz>`, bei Nachbauten ohne Adresse `HID:<Instanz>`. Daran hängen Namen,
Spielerplätze, Art, Stick- und Gyro-Kalibrierung, Einzel-/Hochkant-Joy-Con. Groß/Klein-Dubletten führt das Laden
zusammen.

## 6. Oberfläche

WinForms mit eigenen, selbst gezeichneten Steuerelementen im Windows-11-Stil (`Ui.cs`: `StackPanel`,
`SettingsGroup`, `SettingRow`, `ToggleSwitch`, `Slider`, `Segmented`, `GlyphButton`, `PivotTabs`, `NavItem`,
`ScrollPage`, `TextField` – umhüllt ein randloses `TextBox`; Symbole aus „Segoe MDL2 Assets“). Farben zentral in
`Theme` (Farbschemata aus `ColorSchemes`, dunkel/hell). Die App skaliert selbst (`UiScale.Px`, alle Fenster
`AutoScaleMode.None`). Controller-Grafiken (`InputView*`) sind gezeichnet: Umrisse aus Produktfotos gewonnen
(`InputView.Outlines.cs`, Punktlisten in Foto-Pixeln, `PhotoFrame` rechnet auf die Bühne um). Übersetzung:
`Tr.T(deutscher Text)` mit Tabellen (`Tr.Texts.cs` Englisch, `Tr.Lang*.cs`) und Mustern für Texte mit Platzhaltern
(`Tr.cs`).

Gestaltungsregeln:
- Nur Bausteine aus `Ui.cs` (keine Standard-`Button`/`TextBox` mit Rahmen; Textfelder in `TextField`), Farben nur aus
  `Theme`. Jeder sichtbare Text auf Deutsch im Code und mit Eintrag in allen Übersetzungstabellen (`TranslationTests`).
- Alle Controller nutzen dieselbe Bühne (`PhotoFrame`). Schultertasten sind gewölbte Segmente auf der
  Gehäuse-Oberkante (`EdgeShoulder`, nebeneinander mit Spalt, nichts überlappt), GL/GR sitzen auf den Griffen
  (`GripButton`), Joy-Con haben schwarze Tasten unabhängig von der Gehäusefarbe.
- Grafiken bleiben **neutral**: keine Hersteller- oder Plattformnamen und -logos, Home-Tasten tragen `⌂`; erlaubt sind
  nur Funktionsnamen (L/R, ZL/ZR, A/B/X/Y, SELECT/START …). Beschriftungen laufen über `InputView.Caption`; Textgrößen
  sind fest in pt, Positionen skalieren mit `PhotoFrame`.
- Prüfen ohne Hardware mit den Prüfhilfen (`--render`, `--render-ui`, `--dump-ui`, siehe FUNKTIONEN 14) – nur nach
  Rückfrage beim Nutzer, sie öffnen Fenster.

## 7. Abhängigkeiten und Risiken

| Abhängigkeit | Wofür | Stand |
|---|---|---|
| ViGEmBus 1.22.0 + Nefarius.ViGEm.Client | virtuelle Xbox-360-/DS4-Controller unter Windows | seit 02.11.2023 archiviert, keine Updates mehr; Bewertung und Alternativen in [HANDOVER.md](../HANDOVER.md#vigembus-ist-archiviert) |
| HidHide 1.5 | Original-Controller vor Spielen verstecken | gepflegt; N-Connect sucht auf Wunsch nach Updates (`HidHideUpdate`) |
| .NET 10 (LTS) | alle Projekte; Windows-App bringt die Laufzeit mit | aktuell |
| Linux.Bluetooth, Tmds.DBus | BlueZ-Anbindung des Linux-Dienstes | Linux.Bluetooth bewusst als Vorabversion 6.0.0-pre3 (5.x hängt an Tmds.DBus 0.20 mit bekannter Sicherheitslücke), Tmds.DBus fest auf 0.95.1 |
| Shizuku | virtueller Controller unter Android ohne Root | per Reflection angesprochen |

Lizenzen aller mitgelieferten Komponenten: [THIRD-PARTY-NOTICES.md](../THIRD-PARTY-NOTICES.md).

# Portierung auf andere Plattformen

Stand: 1.0.22. Neben Windows gibt es zwei Portierungen, beide auf derselben Bibliothek: SKZ Connect für Android
([ANDROID.md](ANDROID.md)) und den Linux-Dienst mit Kodi-Add-on ([LINUX-KODI.md](LINUX-KODI.md)). Dieses Dokument
beschreibt, was sich übernehmen lässt, was plattformabhängig ist und wie die beiden Portierungen es gelöst haben.

## 1. Was sich direkt übernehmen lässt

**`Switch2Pro.Protocol` ist plattformunabhängig** (net10.0, keine Windows-Aufrufe) und läuft unverändert unter
Windows, Android und Linux. Es enthält das gesamte Controller-Wissen: Berichte zerlegen, Befehle bauen, Kalibrierung,
Kopplungsverfahren der Switch 2, Belegung, Makros, Gyro → Stick, Flick-Stick, Akkuschätzung, DS4-Bericht,
DSU-Pakete und -Server, Farbschemata, Einstellungen (JSON) und den virtuellen Xbox-One-S-Controller für UHID. Die
Tests laufen auf jeder Plattform mit `dotnet test`.

Wer in einer anderen Sprache portiert, nutzt [PROTOKOLLE.md](PROTOKOLLE.md) und die Tests als Spezifikation
(Testfälle enthalten echte gemessene Werte).

## 2. Was plattformabhängig ist – und was es anderswo gibt

| Aufgabe in SKZ Connect (Windows) | Linux (umgesetzt im Dienst `nconnect`) | Android (umgesetzt in der App) | macOS (nicht umgesetzt) |
|---|---|---|---|
| **BLE-Suche** – `BluetoothLEAdvertisementWatcher` (aktiv) | BlueZ über D-Bus (`Adapter1.StartDiscovery`, Filter `Transport=le` bzw. `auto`), Herstellerdaten aus `Device1.ManufacturerData` | `BluetoothLeScanner` mit Filter auf die Nintendo-Herstellerdaten | CoreBluetooth `scanForPeripherals` |
| **BLE verbinden ohne Kopplung**, GATT, Benachrichtigungen – `BluetoothLEDevice`, `GattSession.MaintainConnection` | eigener ATT-Client über einen L2CAP-Socket, weil BlueZ an den „Secondary Services“ der Controller hängen bleibt | `BluetoothGatt` | `CBCentralManager.connect`, `setNotifyValue`, `writeValue(.withoutResponse)` |
| **Verbindungsintervall** anfordern (`RequestPreferredConnectionParameters`) | BlueZ wählt 48,75 ms (etwa 20 Eingaben/s); der ATT-Client fordert per `HCI LE Connection Update` 6–9 × 1,25 ms an (raw-HCI-Socket, root), Ergebnis etwa 89/s | `requestConnectionPriority(HIGH)` | nicht steuerbar |
| **Adapter-Adresse** (für die Switch-2-Kopplung 0x15) – `BluetoothAdapter.BluetoothAddress` | `Adapter1.Address` | `BluetoothAdapter.getAddress()` liefert seit Android 6 nur eine Platzhalteradresse ; die eigene Adresse liest die App mit Shizuku/Root oder lässt sie eingeben (ANDROID.md) | IOBluetooth |
| **HID** lesen/schreiben (Classic-Controller, Kabel-Pads) – SetupAPI + `hid.dll` | nicht nötig: Kernel-Treiber (`hid-nintendo`, `hid-wiimote`, `hid-playstation`, `xpad` …) übernehmen diese Controller; SKZ Connect koppelt nur | nicht umgesetzt: Android koppelt Classic-Controller selbst, meist nur Tasten und Sticks (Issue #1) | `IOHIDManager` |
| **USB-Bulk** (Switch 2 am Kabel) – WinUSB | nicht umgesetzt (libusb) | nicht umgesetzt (USB-Host-API) | libusb / IOKit |
| **Classic-Kopplung** (Switch 1/NSO ohne PIN, Wii mit binärer PIN) – `BluetoothApis`/WinRT | BlueZ-Agent „NoInputNoOutput“ mit eigener D-Bus-Verbindung; die Wii-PIN setzt das BlueZ-Modul „wiimote“ | System-Dialog | `IOBluetoothDevicePair` |
| **Virtueller Controller** – ViGEmBus (Xbox 360 / DualShock 4) | `uinput` als „Microsoft X-Box 360 pad“ (045E:028E) mit Vibration über `FF_RUMBLE` | UHID über den Shell-Befehl `hid` mit Shizuku oder Root: Xbox One S (045E:02FD); DualShock 4/DualSense scheitern dort an Feature-Berichten. Ohne beides: Touch-Steuerung über eine Bedienungshilfe | keine öffentliche API (nur DriverKit/`IOHIDUserDevice` mit Sonderberechtigung) – Kodi unter macOS zeigt deshalb nur an |
| **Original vor Spielen verstecken** – HidHide | nicht nötig | nicht nötig | – |
| **Tastatur/Maus senden** – `SendInput` | nicht umgesetzt (`uinput`) | Touch-Steuerung über Bedienungshilfe | `CGEvent` (Bedienungshilfen-Freigabe) |
| **Autostart** – `HKCU\…\Run` | Kodi-Add-on startet den Dienst (oder systemd) | Boot-Empfänger „Mit Android starten“ | LaunchAgent |
| **Pfade** – `%APPDATA%`, `%LOCALAPPDATA%` | Datenordner `--data` (im Add-on `userdata/addon_data/service.nconnect/`) | App-Speicher | `~/Library/Application Support` |
| **Oberfläche** – WinForms, selbst gezeichnet | Kodi-Add-on (Python: Menü, Widget, Einstellungen) | eigene Android-Oberfläche in C# | – |
| **Update** – Inno-Setup-Installer | Kodi-Repository | APK von GitHub mit Update-Prüfung in der App | – |

**Gemeinsam statt doppelt:** Der DSU-Server liegt seit 1.0.22 in der Bibliothek (vorher drei Kopien). `Player` und
die Verbindung zum Switch-2-Controller gibt es dagegen je Plattform in einer eigenen, kleineren Fassung; die Windows-App
wurde nicht aufgeteilt.

## 3. Weg für weitere Portierungen

1. **Bibliothek unverändert nutzen** und nur die Anbindung schreiben: BLE-Suche und GATT, virtueller Controller,
   Speicherort der Einstellungen. Vorlage für einen schlanken Aufbau ist der Linux-Dienst (ohne Oberfläche, Ausgabe
   über einen virtuellen Controller, Status als `status.json`).
2. **Doppelte Logik in die Bibliothek ziehen**, sobald eine dritte Fassung entstünde: Spieler-/Paarverwaltung und die
   Ablaufsteuerung von `Player` (Belegung anwenden, Makros, Gyro-Maus/-Stick, Gesten) hängen nur über
   `IControllerLink`, `IVirtualPad` und die Eingabe-Ausgabe an der Plattform.
3. **Steam Deck / Desktop-Linux:** Der Linux-Dienst läuft grundsätzlich ohne Kodi (`nconnect` ohne `--kodi`), ist dort
   aber ungetestet; `hid-nintendo` und Steam Input decken die Classic-Controller bereits ab.
4. **macOS:** Ohne öffentliche API für virtuelle Gamepads ist nur eine Anzeige oder ein eigener Treiber mit
   Sonderberechtigung möglich.
5. **Windows ohne ViGEmBus:** Fällt der archivierte Treiber aus, wäre ein eigener Treiber über das Virtual HID
   Framework (VHF) der Ersatz – HID-Gamepads wie DualShock 4 oder Xbox One als HID, aber kein Xbox-360-Controller.
   Bewertung in [HANDOVER.md](../HANDOVER.md#vigembus-ist-archiviert).

## 4. Verhalten, das auf jeder Plattform gleich bleiben muss

- Switch-2-Werbung auswerten wie in FUNKTIONEN 2.1 (SYNC vs. Host-Adresse; nur mit eigener Adresse verbinden).
- Nach SYNC die Nintendo-Kopplung (0x15) **nur**, wenn der Nutzer Wiederverbinden per Tastendruck will – sie
  ersetzt die Kopplung mit der Konsole.
- Feature-Maske 0x2F bzw. 0x37 (Joy-Con 2) – andere Bits erzeugen Phantomtasten.
- Watchdogs: BLE 2,5 s, USB 3 s, Switch 1 3 s, Wii über Statusabfrage (3 s Stille + 2,5 s Antwortzeit).
- Einstellungen nie verändern, sondern ersetzen (mehrere Threads lesen); eine unlesbare Einstellungsdatei vor dem
  Überschreiben sichern (`Settings.Save` erledigt das).
- Hintergrundsuche nach Classic-Controllern nie während des Spielens oder eines Verbindungsaufbaus (stört BLE).
- Keine Änderungen an Bluetooth-Adapter-Adressen oder an Kopplungsschlüsseln des Betriebssystems.
- Dieselbe `status.json` für das Kodi-Add-on (Felder `platform`, `bluetooth` = `on`/`off`/`missing`/`no_le`,
  `stopped` beim Beenden; Vertrag in LINUX-KODI.md).

## 5. Testen

- `dotnet test Switch2Pro.sln` führt die Tests der Bibliothek, der Windows-App, des Linux-Dienstes und der
  Android-freien Teile der Android-App aus; die Logik der Kodi-Add-ons prüft `py -3 -m unittest discover -s
  linux/kodi/tests` mit nachgebauten Kodi-Modulen.
- Prüfhilfen nachbauen: simulierte Controller (`DemoLink`) und Rendern aller Ansichten als Bild – so lässt sich die
  Oberfläche ohne Hardware prüfen.
- Hardware-Checkliste je Controller: Verbinden (SYNC/Tastendruck), alle Tasten und Sticks, Gyro-Richtungen (DS4 in
  Steam prüfen), Vibration, LEDs, Akku (mit/ohne Kabel), Trennen/Wiederverbinden, mehrere Controller gleichzeitig
  (Funklast!), Joy-Con-Paar trennen/zusammenfügen, Wii-Erweiterungen wechseln.

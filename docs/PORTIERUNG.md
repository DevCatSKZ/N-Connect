# Portierung auf andere Plattformen

## 1. Was sich direkt übernehmen lässt

**`Switch2Pro.Protocol` ist plattformunabhängig** (reines .NET 8, keine Windows-Aufrufe) und läuft unverändert
unter Linux, macOS und Android (.NET MAUI/Android). Es enthält das gesamte Controller-Wissen: Berichte zerlegen,
Befehle bauen, Kalibrierung, Kopplungsverfahren der Switch 2, Belegung, Makros, Gyro→Stick, Akkuschätzung,
DS4-/DSU-Format, Einstellungen (JSON). Die 169 Tests laufen auf jeder Plattform (`dotnet test`).

Wer in einer anderen Sprache portiert, nutzt [PROTOKOLLE.md](PROTOKOLLE.md) und die Tests als Spezifikation
(Testfälle enthalten echte gemessene Werte).

## 2. Was Windows-spezifisch ist – und was es anderswo gibt

| Aufgabe in N-Connect (Windows) | Linux / Steam Deck | macOS | Android |
|---|---|---|---|
| **BLE-Suche** – `BluetoothLEAdvertisementWatcher` (aktiv) | BlueZ über D-Bus (`org.bluez.Adapter1.StartDiscovery`, `SetDiscoveryFilter` mit `Transport=le`), Herstellerdaten aus `Device1.ManufacturerData` | CoreBluetooth `scanForPeripherals` (Herstellerdaten in `advertisementData`) | `BluetoothLeScanner` |
| **BLE verbinden ohne Kopplung**, GATT, Benachrichtigungen – `BluetoothLEDevice`, `GattSession.MaintainConnection` | BlueZ `Device1.Connect`, `GattCharacteristic1.StartNotify`/`WriteValue` (Typ „command“ = ohne Antwort) | `CBCentralManager.connect`, `setNotifyValue`, `writeValue(.withoutResponse)` | `BluetoothGatt` |
| **Verbindungsintervall** anfordern (`RequestPreferredConnectionParameters`) | über Kernel-Parameter (`/sys/kernel/debug/bluetooth/hci0/conn_{min,max}_interval`, Root) oder Controller wählt selbst | nicht steuerbar | `requestConnectionPriority(HIGH)` |
| **Adapter-Adresse** (für Switch-2-Kopplung) – `BluetoothAdapter.BluetoothAddress` | `Adapter1.Address` | IOBluetooth (`IOBluetoothHostController.addressAsString`) | `BluetoothAdapter.getAddress()` liefert seit Android 6 nur eine Platzhalteradresse – Kopplung nach SYNC dann nicht möglich, nur Verbinden per SYNC |
| **HID** lesen/schreiben (Classic-Controller, Kabel-Pads, USB-Eingaben) – SetupAPI + `hid.dll` + `ReadFile`/`WriteFile` | `hidraw` (`/dev/hidrawN`, `read`/`write`); **Achtung:** Kernel-Treiber `hid-nintendo` und `hid-wiimote` übernehmen diese Controller bereits – entweder deren Eingabegeräte nutzen oder den Treiber lösen | `IOHIDManager` | Switch-1/Pro: vom System als Gamepad unterstützt; Rohzugriff nur per USB-Host-API |
| **USB-Bulk** (Switch 2 am Kabel, Interface 1) – WinUSB | libusb | libusb / IOKit | USB-Host-API |
| **Classic-Kopplung** (Switch 1/NSO ohne PIN, Wii mit binärer PIN) – `BluetoothApis`/`bthprops.cpl` | BlueZ-Agent (`org.bluez.Agent1`, „NoInputNoOutput“ für Just Works). Wii-PIN aus Binärbytes ist mit BlueZ schwierig (PIN als Zeichenkette) – bekannte Umwege: Kopplung über 1+2 ohne PIN bzw. Werkzeuge wie in Dolphin/xwiimote | `IOBluetoothDevicePair` (Wii-PIN ebenfalls heikel) | System-Dialog |
| **Virtueller Controller** – ViGEmBus (Xbox 360 / DualShock 4) | `uinput` (evdev-Gamepad, z. B. Xbox-Layout); für Bewegungsdaten zusätzlich ein Bewegungs-Eingabegerät oder `uhid` mit DS4-Beschreibung | keine öffentliche API; nur über DriverKit/`IOHIDUserDevice` mit Sonderberechtigung | ohne Root nicht möglich (Android unterstützt viele Controller aber selbst) |
| **Original vor Spielen verstecken** – HidHide | nicht nötig: Gerät exklusiv öffnen (`EVIOCGRAB`) bzw. Treiber lösen | – | – |
| **Tastatur/Maus senden** – `SendInput` | `uinput` | `CGEvent` (Bedienungshilfen-Freigabe nötig) | Bedienungshilfen-Dienst |
| **Autostart** – `HKCU\…\Run` | systemd-Benutzerdienst oder XDG-Autostart (`~/.config/autostart`) | LaunchAgent (`~/Library/LaunchAgents`) | – |
| **Pfade** – `%APPDATA%`, `%LOCALAPPDATA%` | `$XDG_CONFIG_HOME`, `$XDG_STATE_HOME` | `~/Library/Application Support`, `~/Library/Logs` | App-Speicher |
| **Oberfläche** – WinForms, selbst gezeichnet (GDI+) | Avalonia (C#, plattformübergreifend, mit SkiaSharp-Zeichnung) | Avalonia | MAUI/Avalonia |
| **Infobereich** – `NotifyIcon` | Avalonia `TrayIcon` (StatusNotifier) | Menüleiste | Benachrichtigung |
| **SD-Karte erkennen** – `WM_DEVICECHANGE` | udev/`udisks2` | `DiskArbitration` | Speicherzugriff |
| **Update** – Inno-Setup-Installer | AppImage/Flatpak/.deb | .dmg/.pkg | Store |

**Plattformunabhängig in der Bridge** (nur leicht anzupassen): DSU-Server (UDP-Sockets), Update-Prüfung
(GitHub-API, `HttpClient`), Akku-Ablage (`BatteryTracker`), Protokoll (`Log`), Logik in `Player`
(Belegung anwenden, Makros, Gyro-Maus/-Stick, Gesten) und `ControllerManager` (Spielerverwaltung, Paarbildung,
Inaktivität) – diese hängen nur über `IControllerLink`, `IVirtualPad` und `WindowsInput` an Windows.

## 3. Empfohlener Weg

1. **Bridge aufteilen** in
   - `Core` (plattformfrei): `Player`, Spieler-/Paarverwaltung aus `ControllerManager`, `BatteryTracker`,
     `JoyConMouse`-Logik, DSU, Update-Prüfung;
   - Plattform-Schnittstellen: `IBleCentral` (Suche, Verbinden, GATT), `IHidDevice`, `IUsbBulk`,
     `IVirtualPadFactory`, `IInputInjector` (Tastatur/Maus), `IClassicPairing`, `IAutostart`, `IPaths`;
   - je Plattform eine Implementierung; Windows = heutiger Code.
2. **Links** (`Switch2BleLink`, `Switch1HidLink`, `WiimoteHidLink`, `WiredPadLink`, `Switch2UsbLink`) gegen die
   Schnittstellen statt gegen WinRT/Win32 schreiben – ihre Abläufe (Start-Sequenzen, Watchdogs, Wiederholungen)
   bleiben gleich.
3. **Oberfläche** nach Avalonia übertragen: `Theme`/`Ui`-Steuerelemente und die Controller-Grafiken
   (`InputView*`, Umrisse als Punktlisten in `InputView.Outlines.cs`) lassen sich 1:1 auf eine Skia-Zeichenfläche
   übertragen; Texte über dieselbe `Tr`-Tabelle.
4. **Linux zuerst** (deckt Steam Deck ab): uinput-Gamepad + BlueZ; prüfen, ob `hid-nintendo`/`hid-wiimote` die
   Classic-Controller schon ausreichend unterstützen (dann nur Switch 2 über BLE selbst behandeln).

## 4. Verhalten, das auf jeder Plattform gleich bleiben muss

- Switch-2-Werbung auswerten wie in FUNKTIONEN 2.1 (SYNC vs. Host-Adresse; nur mit eigener Adresse verbinden).
- Nach SYNC die Nintendo-Kopplung (0x15) **nur**, wenn der Nutzer Wiederverbinden per Tastendruck will – sie
  ersetzt die Kopplung mit der Konsole.
- Feature-Maske 0x2F bzw. 0x37 (Joy-Con 2) – andere Bits erzeugen Phantomtasten.
- Watchdogs: BLE 2,5 s, USB 3 s, Switch 1 3 s, Wii über Statusabfrage (3 s Stille + 2,5 s Antwortzeit).
- Einstellungen nie verändern, sondern ersetzen (mehrere Threads lesen).
- Hintergrundsuche nach Classic-Controllern nie während des Spielens oder eines Verbindungsaufbaus (stört BLE).
- Keine Änderungen an Bluetooth-Adapter-Adressen oder an Kopplungsschlüsseln des Betriebssystems.

## 5. Testen

- `dotnet test` (Protokoll) auf der Zielplattform.
- Prüfhilfen nachbauen: simulierte Controller (`DemoLink`) und Rendern aller Ansichten als Bild – so lässt sich die
  Oberfläche ohne Hardware prüfen.
- Hardware-Checkliste je Controller: Verbinden (SYNC/Tastendruck), alle Tasten und Sticks, Gyro-Richtungen (DS4 in
  Steam prüfen), Vibration, LEDs, Akku (mit/ohne Kabel), Trennen/Wiederverbinden, mehrere Controller gleichzeitig
  (Funklast!), Joy-Con-Paar trennen/zusammenfügen, Wii-Erweiterungen wechseln.

# Übergabe: Switch 2 Pro Controller – Bluetooth-Treiber für Windows

Stand: 03.10.2026. Repository `DevCatSKZ-0815/sfm`, Branch `ccr-ad005d0f-jon8df`, Ordner `switch2-pro-windows/`.
(Das Repo `sfm` ist eigentlich ein Spiel-Projekt. Der Treiber kann später in ein eigenes Repo umziehen.)

## Ziel

Den Nintendo Switch 2 Pro Controller per Bluetooth unter Windows 10/11 voll nutzbar machen:
in Windows, Steam und allen Spielen. Ein Installer soll alles einrichten, die Bedienung soll sehr einfach sein.
Die Belegung soll zwischen Xbox und Switch 2 Pro umschaltbar sein, dazu freie Umbelegung jeder Taste.

## Architektur (bereits umgesetzt)

Der Controller nutzt Bluetooth LE mit **eigenen Nintendo-GATT-Diensten**. Er bietet kein HID-over-GATT und
kein Standard-Pairing (SMP). Deshalb gibt es keinen „echten“ HID-Treiber, und ein eigener Kernel-Treiber
bräuchte eine Microsoft-Signatur. Stattdessen besteht die Lösung aus zwei Teilen:

1. **Switch2ProBridge.exe** (dieses Projekt, Benutzermodus, .NET 8 WinForms, Programm im Infobereich):
   - findet den Controller über seine BLE-Werbung (Hersteller 0x0553, Produkt 0x2069),
   - verbindet sich direkt über die WinRT-Bluetooth-API (**ohne** Windows-Pairing),
   - sendet die Start-Befehle und liest die Stick- und Gyro-Kalibrierung,
   - empfängt die Eingabeberichte und schickt Vibration zurück.
2. **ViGEmBus** (signierter Kernel-Treiber von Nefarius): stellt daraus einen virtuellen
   **Xbox-360-** oder **DualShock-4-Controller** (mit Gyro) bereit.

```
src/Switch2Pro.Protocol/   plattformunabhängig, getestet
  Gatt.cs            UUIDs der GATT-Merkmale
  Advertisement.cs   Erkennung Pro Controller 2 + SYNC-Modus vs. Wiederverbinden
  Commands.cs        Befehlsrahmen, Start-Sequenz, Speicher lesen, LEDs
  InputReports.cs    Bericht 0x05 (Standard, mit IMU) und 0x09 (Ausweichweg)
  Calibration.cs     Stick-Kalibrierung (Format wie SDL), Gyro-Nullpunkt
  Rumble.cs          HD-Rumble-2-Pakete (33 Byte)
  Mapping.cs         Belegung (Xbox/Switch2 + Remap), Totzone, DS4-Rohbericht (63 Byte)
  Settings.cs        Einstellungen (JSON in %APPDATA%\Switch2ProBridge\settings.json)
src/Switch2Pro.Bridge/     nur Windows
  ControllerManager.cs   BLE-Suche, mehrere Controller, Wiederverbinden
  ControllerSession.cs   eine Verbindung: GATT, Init, Kalibrierung, Eingaben, Vibration
  VirtualPads.cs         ViGEm Xbox 360 / DualShock 4
  TrayApp.cs             Infobereich-Symbol und Menü
  SettingsForm.cs        Einstellungsfenster mit Live-Tastentest
  WelcomeForm.cs         Kurzanleitung beim ersten Start
tests/Switch2Pro.Protocol.Tests/   43 xUnit-Tests (alle grün)
installer/Switch2ProSetup.iss      Inno Setup 6, installiert ViGEmBus still mit
../.github/workflows/switch2-pro-windows.yml   baut die Setup.exe auf windows-latest
```

Protokollquellen:
- [ndeadly/switch2_controller_research](https://github.com/ndeadly/switch2_controller_research)
- SDL `src/joystick/hidapi/SDL_hidapi_switch2.c`
- NS2Pro-Bridge-Windows (MIT; die Start-Sequenz und das Vibrationsformat stammen von dort und sind dort
  unter Windows erprobt)

## Status

- ✅ Protokoll-Bibliothek fertig, 43/43 Tests grün.
- ✅ Windows-App kompiliert ohne Warnungen (Warnungen gelten als Fehler). Die Single-File-EXE lässt sich bauen.
- ✅ Installer-Skript und GitHub-Workflow geschrieben; `actionlint` meldet keine Fehler.
- ✅ Eine erste Selbstprüfung ist erledigt; die gefundenen Fehler sind behoben (siehe Git-Log).
- ⚠️ **Noch nie mit echter Hardware getestet.** Die Entwicklung lief in einer Linux-Cloud ohne Bluetooth.
- ⚠️ Ein unabhängiger Code-Review ist fertig (Ergebnis unten unter „Offene Review-Befunde“). Die Befunde
  sind **noch nicht behoben**. → Im neuen Chat zuerst abarbeiten.
- ⚠️ **GitHub Actions starten in diesem Repo nicht.** Alle Läufe (auch die älteren Android-Läufe) enden
  sofort mit `startup_failure`. Das ist ein Repo- bzw. Kontoproblem (Actions-Einstellungen oder Abrechnung),
  kein Fehler im YAML. Auf dem PC lässt sich der Installer lokal bauen (siehe unten).

## Auf dem PC einrichten

Voraussetzungen: Windows 10 2004+ oder Windows 11, Bluetooth-LE-Adapter, Git,
[.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) und für den Installer
[Inno Setup 6](https://jrsoftware.org/isdl.php).

```powershell
git clone -b ccr-ad005d0f-jon8df https://github.com/DevCatSKZ-0815/sfm.git
cd sfm\switch2-pro-windows
dotnet test tests\Switch2Pro.Protocol.Tests
dotnet run --project src\Switch2Pro.Bridge     # direkt starten (Debug)
```

Für ViGEmBus vorher einmal den offiziellen Installer ausführen:
<https://github.com/nefarius/ViGEmBus/releases> (v1.22.0).

So baust du den Installer lokal:

```powershell
dotnet publish src\Switch2Pro.Bridge -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o out\publish\win-x64
# ViGEmBus-Installer als installer\redist\ViGEmBus_Setup.msi (oder .exe) ablegen
& "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe" installer\Switch2ProSetup.iss
# Ergebnis: out\Switch2ProController-Setup-1.0.0.exe
```

## ⚠️ Wichtig beim Testen mit dem Controller

- Den Controller **NICHT** über *Windows-Einstellungen → Bluetooth → Gerät hinzufügen* koppeln.
  Er kennt kein Standard-Pairing und bricht die Verbindung ab. Ist er dort schon eingetragen:
  **Gerät entfernen.** Die App verbindet sich selbst.
- Zum Verbinden die App starten und dann **kurz die SYNC-Taste** oben am Controller drücken.
- Die Switch 2 dabei in den Ruhemodus versetzen, sonst schnappt sie sich den Controller.
- Protokoll: `%LOCALAPPDATA%\Switch2ProBridge\bridge.log`. Es ist die wichtigste Quelle zur Fehlersuche.
- Prüfen: Windows-Gamecontroller-Test (`joy.cpl`), Steam → Einstellungen → Controller, ein Spiel.

## Was als Erstes mit echter Hardware zu prüfen ist (Risiken)

Diese Punkte sind aus Doku und Referenzcode abgeleitet, aber nicht real getestet:

1. **Verbindungsaufbau ohne Pairing** über `BluetoothLEDevice.FromBluetoothAddressAsync`: Klappt er?
   Stimmt der Adresstyp? Kommt ein Windows-Pairing-Dialog? (`ControllerSession.ConnectAsync`)
2. **Start-Sequenz** (`Commands.InitSequence`): Kommen Antworten auf `c765a961`? Die Log-Zeilen
   „keine Antwort auf …“ zeigen das. Ohne Antworten fehlt auch die Kalibrierung.
3. **Eingabebericht 0x05** über `ab7de9be-…-7fd2`: Stimmen Tasten, Sticks und IMU-Offsets
   (Beschleunigung ab 0x30, Gyro ab 0x36)? Zum Prüfen das Einstellungsfenster öffnen; der **Live-Test**
   unten zeigt Tasten und Sticks.
4. **Sendet der Controller im Leerlauf weiter Berichte?** (Wichtig für die Watchdog-Logik.)
5. **Vibration** (`Rumble.cs`, Merkmal `cc483f51`): Ist sie spürbar und nicht zu stark?
   Die drei 5-Byte-Frames sind identisch; die Referenz-Bridge nutzt Frame 2 und 3 als „neutral“.
6. **DualShock-4-Modus**: Gyro-Achsen und Vorzeichen in Steam prüfen (`Mapping.ToDs4Report`);
   Vibration über `AwaitRawOutputReport`.
7. **Wiederverbinden per Tastendruck** (`Advertisement.IsSyncMode`, `Settings.AutoReconnect`):
   Nimmt der Controller nach dem Aufwecken eine Verbindung vom PC an?
8. **Akkuanzeige**: Die Spannungskennlinie und das Lade-Byte 0x21 sind geschätzt (nur Anzeige).
9. **Installer**: Die stillen Schalter für ViGEmBus (`/qn` für MSI, `/exenoui /qn` für EXE) und die
   Autostart-Übergabe per `--autostart` prüfen.

## Wünsche des Nutzers (bisher)

- komplett kompatibel mit Windows, Steam und anderen Programmen, ein Installer, fertig ✅
- Belegung zwischen Xbox und Switch 2 Pro einstellbar ✅ (plus freie Umbelegung)
- Windows-Installer ✅
- sehr benutzerfreundlich ✅ (Einstellungsfenster, Willkommensdialog, deutsche Oberfläche)
- „ein funktionierender Treiber ohne Probleme“ → Dafür fehlen der Hardwaretest und das erneute Review.

## Offene Review-Befunde (noch zu beheben)

Der Review hat geprüft und als korrekt bestätigt:
- Bericht 0x05 und 0x09 (Offsets, Tasten-Bits), Stick-Entpackung und Kalibrierung,
- Gyro-Nullpunkt, Befehlsrahmen und Speicherlesen, Rumble-Paket, Werbung,
- DS4-Bericht (DS4_REPORT_EX) und DS4-Ausgabebericht.

Die Zeilennummern beziehen sich auf Commit `7946aa9`.

1. **DS4-Zeitstempel springt in Schritten von ~15,6 ms** (`VirtualPads.cs`, `Ds4Pad.Update`).
   Grund: `Environment.TickCount64` ist zu grob, der Gyro ruckelt in Steam.
   Fix: `Stopwatch.GetTimestamp()` verwenden, besser noch den µs-Zeitstempel des Controllers
   (Bericht 0x05, Offset 0x2A). *(sicher)*
2. **`GattDeviceService` wird nie freigegeben** (`ControllerSession.StartAsync`/`DisposeAsync`).
   Folge: Der BLE-Link kann offen bleiben. Fix: in einem Feld merken und in `DisposeAsync` vor
   `_session`/`_device` freigeben. *(ziemlich sicher)*
3. **ViGEm-Ziele werden nur getrennt, nie freigegeben** (`VirtualPads.cs`). Folge: Bei jedem
   Neuverbinden und jedem Wechsel der Ausgabeart bleibt ein natives Ziel übrig. Fix: nach `Disconnect()`
   auch `_pad.Dispose()` aufrufen; bei DS4 vorher den Thread `DS4-Ausgabe` per `Join` beenden. *(sicher)*
4. **Beim Beenden wird der ViGEm-Client freigegeben, während Pads evtl. noch arbeiten**
   (`TrayApp.ExitThreadCore`, `ControllerManager.DisposeAsync`). Fix: laufende Connect- und
   Lost-Tasks verfolgen und abwarten; `PadFactory` erst danach freigeben. *(mittel)*
5. **Fehlgeschlagene Start-Befehle werden ignoriert** (`ControllerSession.SendCommandAsync`, Init-Schleife).
   Folge: Eine Sitzung ohne Eingaben bleibt für immer „verbunden“. Fix: jeden Init-Befehl bis zu 3× senden
   (120·n ms Pause), danach abbrechen. Zusätzlich gilt: kommt nach dem Start N Sekunden lang gar keine
   Eingabe, ist die Sitzung verloren. *(mittel)*
6. **Die Start-Sequenz sendet „Pairing abschließen“ (`15 91 01 03`) und „USB initialisieren“
   (`03 91 01 0D … FF`)** (`Commands.InitSequence`). Laut Doku schreibt 0x15/0x03 Pairing-Daten.
   Das Versprechen „Switch-2-Pairing bleibt erhalten“ ist damit **unbewiesen**. Test: ohne diese zwei
   Befehle probieren, oder den Flash bei 0x1FA000 vor und nach dem Verbinden lesen. *(Risiko)*
7. **Settings:** Das erste `Save` in `Settings.Load` liegt außerhalb von try/catch, ein IO-Fehler beendet
   die App. `ReloadSettings` blockiert die Oberfläche mit `Thread.Sleep`. Ist `settings.json` gelöscht,
   ersetzt der Reload alles durch Standardwerte. Fix: try/catch und ein WinForms-Timer zum Entprellen.
   *(mittel)*
8. **Installer-Autostart greift nur, wenn „Jetzt starten“ angehakt bleibt** (`Switch2ProSetup.iss`).
   Fix: `[Registry]`-Eintrag mit `Tasks: autostart`. *(sicher)*
9. **Deinstallation löscht bei Admin-Elevation durch ein anderes Konto den falschen Run-Eintrag.**
   Fix: Die App räumt ihren Eintrag selbst auf, oder das Verhalten wird dokumentiert. *(mittel)*
10. **Der Installer überspringt ViGEmBus, sobald irgendeine Version installiert ist.** Fix: Version von
    `{sys}\drivers\ViGEmBus.sys` auf mindestens 1.17 prüfen. Die stillen Schalter sind korrekt. *(niedrig bis mittel)*
11. **Die Inno-Setup-Version ist im Workflow nicht festgelegt.** Fix: `choco install innosetup --version 6.x.y`. *(niedrig)*

Kleinigkeiten:
- Eine verspätete Antwort auf einen Speicherlesebefehl kann den nächsten Lesebefehl erfüllen;
  Fix: die Adresse mit abgleichen.
- Ein 9. Controller bekommt Spieler 1 doppelt.
- `PadFactory.TryCreate` fängt `DllNotFoundException` und `Win32Exception` nicht ab.

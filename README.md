<img src="installer/art/N-Connect.png" width="96" align="right" alt="">

# N-Connect

Nintendo-Controller am PC nutzen – in **Windows, Steam, Xbox-/Game-Pass-Spielen, Epic, Emulatoren** und allen
anderen Programmen, die Controller unterstützen. Jeder Controller erscheint als **Xbox-360-Controller**
(oder auf Wunsch als **DualShock 4** mit Bewegungssteuerung).

Einmal installieren, Controller verbinden, spielen. Die Oberfläche gibt es auf Deutsch und Englisch.

## Unterstützte Controller

| Controller | Verbindung | Besonderheiten |
|---|---|---|
| **Switch 2 Pro Controller** | Bluetooth (SYNC) oder **USB-Kabel** (bis ~500 Hz) | GL/GR, C-Taste, Gyro, HD-Vibration |
| **Joy-Con 2 (L/R)** | Bluetooth (SYNC) | als Paar oder einzeln, **Mausmodus** (auf den Tisch stellen), Charging Grip mit GL/GR |
| **GameCube-Controller (Switch 2)** | Bluetooth (SYNC) oder USB-Kabel | analoge Trigger |
| **Switch Pro Controller** (Switch 1) | Windows-Bluetooth-Kopplung oder USB | Gyro, Vibration, **amiibo lesen** |
| **Joy-Con (L/R)** (Switch 1) | Windows-Bluetooth-Kopplung | Paar oder einzeln, **amiibo**, **Ring-Con**, **IR-Kamera** (rechter Joy-Con) |
| **NES, SNES, N64, SEGA Mega Drive** (Nintendo Switch Online) | Windows-Bluetooth-Kopplung | eigene Anordnung, N64-C-Tasten = rechter Stick |
| **Wii-Fernbedienung** (auch Plus) | „Wii-Controller koppeln …“ im Programm | mit **Nunchuk** oder **Classic Controller** |
| **Wii U Pro Controller** | „Wii-Controller koppeln …“ im Programm | beide Sticks, Akkuanzeige |

Bis zu 8 Controller gleichzeitig (Spieler 1–8).

## Installation

1. **`N-Connect-Setup-….exe`** herunterladen (GitHub → *Releases* bzw. *Actions* → letzter Lauf → *Artifacts*).
2. Setup starten. Es installiert automatisch:
   - das Programm (läuft unauffällig unten rechts im Infobereich),
   - den signierten Treiber **ViGEmBus** für den virtuellen Controller (falls noch nicht vorhanden),
   - auf Wunsch **HidHide** (verhindert, dass Spiele einen per USB angeschlossenen Controller doppelt sehen),
   - auf Wunsch den Autostart mit Windows.
3. Beim ersten Start erscheint eine Kurzanleitung.

Voraussetzungen: Windows 10 (2004) oder Windows 11, 64 Bit, Bluetooth 4.0+ (für Switch-2-Controller Bluetooth LE).

## Controller verbinden

**Switch 2 (Pro Controller, Joy-Con 2, GameCube):** kurz die **SYNC-Taste** drücken. Nach ein paar Sekunden vibriert
der Controller – fertig. Danach reicht ein **beliebiger Tastendruck** zum Verbinden.
Pro Controller und GameCube-Controller funktionieren auch einfach per **USB-Kabel**.

> ⚠️ Switch-2-Controller **nicht** über *Einstellungen → Bluetooth → Gerät hinzufügen* koppeln – sie nutzen ein
> eigenes Nintendo-Verfahren; die Windows-Kopplung stört die Verbindung.
> Hinweis: Nach dem Verbinden mit dem PC muss der Controller an der Switch 2 einmal neu gekoppelt werden (SYNC an der Konsole).

**Switch 1 und Nintendo-Switch-Online-Controller:** einmalig in Windows unter *Bluetooth → Gerät hinzufügen* koppeln
(SYNC-Taste am Controller halten). Das Programm erkennt sie dann automatisch.

**Wii-Fernbedienung / Wii U Pro Controller:** Rechtsklick auf das Symbol im Infobereich → **„Wii-Controller koppeln …“**,
dann die rote SYNC-Taste (im Batteriefach bzw. auf der Unterseite) drücken.

## Was das Programm kann

- **Übersicht** mit Live-Grafik jedes Controllers (gedrückte Tasten leuchten), Akku, Verbindung, Seriennummer, Firmware.
- Knöpfe je Controller: **Trennen**, **Vibrieren** (welcher ist welcher Spieler?), **Gyro kalibrieren**,
  Joy-Con **trennen/zusammenfügen**, **hochkant/quer**, **amiibo lesen**, **Ring-Con**, **IR-Kamera**,
  **„Doppelt angezeigt? Verstecken“** (HidHide, bei USB).
- **Joy-Con wie an der Switch:** zwei Joy-Con werden automatisch ein Controller; SL + SR eine Sekunde halten trennt,
  L + R gleichzeitig verbindet wieder. Die Wahl wird je Joy-Con gemerkt.
- **Joy-Con-2-Mausmodus:** Joy-Con auf die Schienenkante stellen → Maus (R/L = Linksklick, ZR/ZL = Rechtsklick,
  Stick = Scrollen).
- **Tastenbelegung je Controller-Typ:** jede Taste auf Gamepad-Tasten, **Tastatur-Hotkeys**, **Maustasten**,
  **Gyro-Maus**, **Gyro als rechter Stick**, **Turbo/Dauerfeuer**, **Makros** (Tastenfolgen) oder fertige Vorlagen
  (Bildschirmfoto, Aufnahme, Xbox Game Bar, Lautstärke …).
- **Shift-Ebene:** eine Taste halten → andere Tasten bekommen eine zweite Belegung.
- **Profile je Spiel:** werden automatisch aktiv, wenn das Spiel im Vordergrund ist; exportieren/importieren als Datei.
- **Zielen per Bewegung:** Gyro als rechter Stick (immer, beim Zielen mit ZL oder per Taste) – für Spiele ohne Maus.
- **Feinabstimmung:** Stick-Totzone (auch je Controller-Typ), Stick-Kennlinie, Trigger-Schwelle, Turbo-Geschwindigkeit.
- **Emulatoren:** Bewegungsdaten per **Cemuhook/DSU** (Port 26760), z. B. für Cemu, Yuzu-Nachfolger, Dolphin.
- **Akku-Symbol** im Infobereich, **Warnung** bei niedrigem Akku, **automatisches Trennen** bei Inaktivität (einstellbar).
- **Update-Hinweis**, wenn auf GitHub eine neue Version erscheint.

Alle Einstellungen sind optional und gelten sofort (Klick auf das Symbol im Infobereich → Reiter *Einstellungen*).

## Fehlerbehebung

| Problem | Lösung |
|---|---|
| Switch-2-Controller verbindet sich nicht | SYNC kurz drücken (nicht halten). Ist er in den Windows-Bluetooth-Einstellungen eingetragen → dort **entfernen**. Switch 2 in der Nähe in den Ruhemodus versetzen. |
| Switch-1-/NSO-Controller wird nicht erkannt | In Windows unter Bluetooth koppeln; nach dem Koppeln einmal eine Taste drücken. |
| Wii-Fernbedienung koppelt nicht | Im Programm „Wii-Controller koppeln …“ verwenden (nicht das Windows-Fenster); rote SYNC-Taste drücken, solange gesucht wird. |
| Spiel sieht einen USB-Controller doppelt | Auf der Karte „Doppelt angezeigt? Verstecken“ klicken (HidHide). |
| „ViGEmBus-Treiber fehlt“ | Setup erneut ausführen oder ViGEmBus installieren: <https://github.com/nefarius/ViGEmBus/releases> |
| Etwas anderes | Rechtsklick auf das Symbol → **Protokoll öffnen** und den Inhalt bei einer Fehlermeldung beilegen. |

Protokoll: `%LOCALAPPDATA%\Switch2ProBridge\bridge.log` · Einstellungen: `%APPDATA%\Switch2ProBridge\settings.json`

## Wie es funktioniert (technisch)

Das Programm spricht die Controller im **Benutzermodus** direkt an (Bluetooth LE über WinRT, HID, WinUSB) und gibt
die Eingaben an **ViGEmBus** weiter, einen signierten Kernel-Treiber, der einen virtuellen Xbox-360- bzw.
DualShock-4-Controller bereitstellt. Ein eigener Kernel-Treiber ist dadurch nicht nötig.

```
src/Switch2Pro.Protocol   Protokolle (plattformunabhängig, mit Tests): Switch 2 (BLE/USB), Switch 1 (HID, NFC,
                          IR-Kamera, Ring-Con), Wii, Belegung, Makros, DSU, DS4-Bericht
src/Switch2Pro.Bridge     Windows-App: Verbindungen, ViGEm-Ausgabe, Infobereich, Fenster, Übersetzung
tests/                    xUnit-Tests
installer/                Inno-Setup-Skript (Setup.exe mit ViGEmBus und optional HidHide)
```

Selbst bauen: .NET 8 SDK, dann `dotnet test tests/Switch2Pro.Protocol.Tests` und
`dotnet publish src/Switch2Pro.Bridge -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o out/publish/win-x64`.
Den Installer baut der Workflow `.github/workflows/switch2-pro-windows.yml`.
Prüfhilfen: `N-Connect.exe --render <Ordner>` (alle Grafiken als PNG), `--render-brand <Ordner>` (Logo, Icon, Installer-Bilder), `--demo` / `--demo-retro` (simulierte Controller).

### Quellen und Dank

Protokollinformationen (nur als Dokumentation genutzt, eigene Umsetzung):
[ndeadly/switch2_controller_research](https://github.com/ndeadly/switch2_controller_research),
SDL (`SDL_hidapi_switch2.c`, `SDL_hidapi_switch.c`, zlib), dekuNukem/Nintendo_Switch_Reverse_Engineering,
Linux `hid-nintendo` und `hid-wiimote`, WiiBrew, die Emulatoren yuzu/Citron (NFC, IR-Kamera, Ring-Con) und Dolphin
(Wii-Kopplung), Switch2Connect. Start-Sequenz und Vibrationsformat über Bluetooth: NS2Pro-Bridge-Windows (MIT).
Virtueller Controller: [ViGEmBus](https://github.com/nefarius/ViGEmBus), [HidHide](https://github.com/nefarius/HidHide) von Nefarius.

Entwickelt von **devcatskz**. Inoffizielles Projekt, nicht mit Nintendo verbunden. „Nintendo“, „Switch“, „Wii“, „amiibo“ sind Marken von Nintendo;
„SEGA“ und „Mega Drive“ sind Marken von SEGA.

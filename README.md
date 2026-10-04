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
| **Switch Pro Controller** (Switch 1) | Bluetooth (SYNC, N-Connect koppelt selbst) oder USB | Gyro, Vibration, **amiibo lesen** |
| **Joy-Con (L/R)** (Switch 1) | Bluetooth (SYNC, N-Connect koppelt selbst) | Paar oder einzeln, **amiibo**, **Ring-Con**, **IR-Kamera** (rechter Joy-Con) |
| **NES, SNES, N64, SEGA Mega Drive** (Nintendo Switch Online) | Bluetooth (SYNC, N-Connect koppelt selbst) | eigene Anordnung, N64-C-Tasten = rechter Stick |
| **Wii-Fernbedienung** (auch Plus) | Bluetooth (SYNC, N-Connect koppelt selbst) | mit **Nunchuk** oder **Classic Controller** |
| **Wii U Pro Controller** | Bluetooth (SYNC, N-Connect koppelt selbst) | beide Sticks, Akkuanzeige |
| **Kabel-Pads von HORI, PowerA, PDP** (für Switch) | USB-Kabel | wie Pro Controller, ohne Gyro/Vibration (noch nicht mit echter Hardware geprüft) |
| **Nachbauten im Switch-Modus** (z. B. 8BitDo, „Lic Pro Controller“) | wie Switch Pro Controller | soweit der Nachbau das Protokoll beherrscht |

Bis zu 8 Controller gleichzeitig (Spieler 1–8). Tipp für viele Controller: ein leistungsfähiger Bluetooth-Adapter
(z. B. Intel AX200/AX210 oder Realtek-Bluetooth-5.3-Stick) – einfache Sticks reichen oft nur für 2–3 Controller.

## Installation

1. **`N-Connect-Setup-….exe`** herunterladen (GitHub → *Releases* bzw. *Actions* → letzter Lauf → *Artifacts*).
2. Setup starten. Es installiert automatisch:
   - das Programm (läuft unauffällig unten rechts im Infobereich),
   - den signierten Treiber **ViGEmBus** für den virtuellen Controller (falls noch nicht vorhanden),
   - **HidHide** (standardmäßig angehakt: verhindert, dass Steam und Spiele Switch-1-, NSO- und USB-Controller doppelt sehen).
3. N-Connect startet danach **automatisch mit Windows** im Hintergrund (abschaltbar unter *Allgemein → Mit Windows starten*).
4. Beim ersten Start erscheint eine Kurzanleitung.

Voraussetzungen: Windows 10 (2004) oder Windows 11, 64 Bit, Bluetooth 4.0+ (für Switch-2-Controller Bluetooth LE).

## Controller verbinden

**Switch 2 (Pro Controller, Joy-Con 2, GameCube):** kurz die **SYNC-Taste** drücken. Nach ein paar Sekunden vibriert
der Controller – fertig. Danach reicht ein **beliebiger Tastendruck** zum Verbinden.
Pro Controller und GameCube-Controller funktionieren auch einfach per **USB-Kabel**.

> ⚠️ Switch-2-Controller **nicht** über *Einstellungen → Bluetooth → Gerät hinzufügen* koppeln – sie nutzen ein
> eigenes Nintendo-Verfahren; die Windows-Kopplung stört die Verbindung.
> Hinweis: Nach dem Verbinden mit dem PC muss der Controller an der Switch 2 einmal neu gekoppelt werden (SYNC an der Konsole).

**Switch 1, Nintendo-Switch-Online- und Wii-Controller:** einfach die **SYNC-Taste** drücken (Joy-Con: an der
Schiene, Wii-Fernbedienung: rote Taste im Batteriefach, Wii U Pro: Unterseite). N-Connect koppelt den Controller
**selbst** mit Windows – kein Umweg über die Windows-Bluetooth-Einstellungen. Danach reicht ein Tastendruck.
Die Suche im Hintergrund läuft nur, solange gerade niemand spielt; gezielt suchen: *Allgemein → Controller koppeln …*
(abschaltbar: *Neue Controller automatisch koppeln*).

## Was das Programm kann

- **Übersicht** mit Live-Grafik jedes Controllers (Form und Tastenlage nach Produktfotos, Originalfarben, gedrückte
  Tasten leuchten), Akku, Verbindung, Seriennummer, Firmware. Bei breitem Fenster zwei Spalten, alle Karten gleich hoch.
- **Einstellungen direkt an der Controller-Karte** („Einstellungen“ aufklappen): Tasten, Feineinstellung, Gyro,
  Joy-Con, Extras, Details – jeweils nur für diesen Controller.
- **Taste per Tastendruck belegen:** hinter jeder Taste das Tastatur-Symbol klicken und die gewünschte Taste drücken.
- **Oberfläche im Windows-11-Stil:** dunkel (Standard), hell oder wie Windows; Akzentfarbe aus Windows, Mica-Titelleiste.
- Knöpfe je Controller: **Trennen**, **Vibrieren** (welcher ist welcher Spieler?), **Gyro kalibrieren**,
  Joy-Con **trennen/zusammenfügen**, **hochkant/quer**, **amiibo lesen**, **Ring-Con**, **IR-Kamera**,
  **„Doppelt angezeigt? Verstecken“** (HidHide).
- **Für Steam vorbereitet:** Original-Controller werden automatisch vor Steam und Spielen versteckt (HidHide, einmal
  bestätigen) – Steam sieht nur den virtuellen Xbox-Controller. **Erscheint als** je Controller wählbar (Xbox 360 oder
  DualShock 4 mit Gyro), Standard Xbox 360.
- **Joy-Con im Ladegriff per USB** (Switch 1) werden erkannt.
- **Spieler-Reihenfolge:** Leiste über den Karten – mit ‹ › festlegen, welcher Controller Spieler 1, 2 … ist
  (Spieler 1 = erster Controller für Windows, Steam und Spiele; Lichter ziehen mit, wird je Controller gemerkt).
  Wird ein Controller getrennt, rücken die anderen automatisch auf. Auch über den Kartentitel (dort auch
  umbenennen, z. B. „Lenas Joy-Con“).
- **Sticks kalibrieren** (gegen Drift): geführt Mitte und Rand messen, mit Rundheitsanzeige – nur in N-Connect
  gespeichert, der Controller bleibt unverändert.
- **Gyro-Assistent:** Zielen per Bewegung in drei Schritten einrichten, mit Live-Vorschau.
- **Joy-Con wie an der Switch:** zwei Joy-Con werden automatisch ein Controller; einen Joy-Con quer halten und SL
  oder SR drücken macht ihn zum eigenen Spieler, L + R gleichzeitig verbindet
  wieder. Die Wahl wird je Joy-Con gemerkt.
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
  Switch-2-Controller melden nur die Spannung; N-Connect rechnet sie geglättet in Prozent um – auch beim Laden am
  Kabel (der Spannungsanstieg beim Laden wird je Controller gemessen und herausgerechnet).
- **Kopplungsdaten von der Switch (Switch 1):** Mit dem Payload *Bluepick RCM* auf die SD-Karte gesicherte
  Kopplungsdaten (`switchroot/joycon_mac.ini`) liest N-Connect ein – die SD-Karte im Kartenleser bzw. die
  angeschlossene Switch wird automatisch erkannt (*Allgemein → Kopplungsdaten von der Switch übernehmen*).
- **Kopplungsdaten auf einen anderen PC übertragen:** als `.ncpair`-Datei exportieren (optional mit Passwort,
  Schlüssel nur auf ausdrücklichen Wunsch) und auf dem zweiten PC öffnen. Bluetooth-Adapter und Windows-Kopplungen
  werden dabei nicht verändert.
- **Infobereich-Menü:** je Spieler Vibrieren, Spielerplatz, Trennen; alle Controller trennen; Ausgabeart, Profil u. a.
- **Ein-Klick-Update:** Erscheint auf GitHub eine neue Version, lädt N-Connect den Installer auf Klick herunter
  (geprüft), installiert ihn und startet neu.

Alle Einstellungen sind optional und gelten sofort (Klick auf das Symbol im Infobereich; links die Bereiche
*Controller, Tastenbelegung, Sticks & Vibration, Gyro & Maus, Joy-Con & Wii, Allgemein*).

## Fehlerbehebung

| Problem | Lösung |
|---|---|
| Switch-2-Controller verbindet sich nicht | SYNC kurz drücken (nicht halten). Ist er in den Windows-Bluetooth-Einstellungen eingetragen → dort **entfernen**. Switch 2 in der Nähe in den Ruhemodus versetzen. |
| Switch-1-/NSO-/Wii-Controller wird nicht gekoppelt | *Allgemein → Controller koppeln …* öffnen und SYNC drücken, solange gesucht wird (die Hintergrundsuche pausiert, während jemand spielt). Fehlercodes stehen im Protokoll („Kopplung …“). Nach einem Fehlschlag wartet die Hintergrundsuche 90 s, bevor sie es erneut versucht. |
| Joy-Con ist plötzlich ein eigener Spieler (Tasten/Stick gedreht) | Er wurde vom Paar gelöst (quer halten + SL/SR). L am linken und R am rechten Joy-Con gleichzeitig drücken fügt sie wieder zusammen. |
| Steam/Spiel sieht einen Controller doppelt | „Allgemein → Original-Controller verstecken“ (HidHide) muss an sein; die Windows-Abfrage bestätigen und Steam einmal neu starten. Einzeln: Karte → Extras → „Doppelt angezeigt?“. |
| Akkuanzeige weicht beim Laden ab | Einmal Kabel abziehen und wieder anstecken – N-Connect misst den Spannungsanstieg dann neu. Werte stehen alle 30 s im Protokoll („Akku …“). |
| „ViGEmBus-Treiber fehlt“ | Setup erneut ausführen oder ViGEmBus installieren: <https://github.com/nefarius/ViGEmBus/releases> |
| Etwas anderes | Rechtsklick auf das Symbol → **Protokoll öffnen** und den Inhalt bei einer Fehlermeldung beilegen. |

Protokoll: `%LOCALAPPDATA%\N-Connect\bridge.log` · Einstellungen: `%APPDATA%\N-Connect\settings.json` ·
Akku-Messwerte: `%LOCALAPPDATA%\N-Connect\battery.json` (Ordner der Vorversion `Switch2ProBridge` werden einmalig übernommen)

## Wie es funktioniert (technisch)

Ausführliche Entwicklerdokumentation (alle Funktionen und ihr Verhalten, Architektur, Controller-Protokolle,
Portierung auf andere Plattformen): **[docs/](docs/README.md)**.

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
Den Installer lokal bauen: Inno Setup 6, dann `ISCC.exe installer\N-Connect.iss` (Ergebnis in `out\`).
Prüfhilfen: `N-Connect.exe --render <Ordner>` (alle Controller-Grafiken als PNG), `--render-ui <Ordner>` (alle Seiten
und Karten, hell/dunkel, Deutsch/Englisch; mit `--wide` für breite Fenster), `--render-brand <Ordner>` (Logo, Icon,
Installer-Bilder), `--demo` / `--demo-all` / `--demo-retro` (simulierte Controller).

### Quellen und Dank

Protokollinformationen (nur als Dokumentation genutzt, eigene Umsetzung):
[ndeadly/switch2_controller_research](https://github.com/ndeadly/switch2_controller_research),
SDL (`SDL_hidapi_switch2.c`, `SDL_hidapi_switch.c`, zlib), dekuNukem/Nintendo_Switch_Reverse_Engineering,
Linux `hid-nintendo` und `hid-wiimote`, WiiBrew, die Emulatoren yuzu/Citron (NFC, IR-Kamera, Ring-Con) und Dolphin
(Wii-Kopplung), Switch2Connect. Start-Sequenz und Vibrationsformat über Bluetooth: NS2Pro-Bridge-Windows (MIT).
Virtueller Controller: [ViGEmBus](https://github.com/nefarius/ViGEmBus), [HidHide](https://github.com/nefarius/HidHide) von Nefarius.

Entwickelt von **devcatskz**. Inoffizielles Projekt, nicht mit Nintendo verbunden. „Nintendo“, „Switch“, „Wii“, „amiibo“ sind Marken von Nintendo;
„SEGA“ und „Mega Drive“ sind Marken von SEGA.

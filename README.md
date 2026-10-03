# Switch 2 Pro Controller – Bluetooth-Treiber für Windows

Den **Nintendo Switch 2 Pro Controller** kabellos per Bluetooth am PC nutzen – in **Windows, Steam,
Xbox-/Game-Pass-Spielen, Epic, Emulatoren** und allen anderen Programmen, die Controller unterstützen.

Einmal installieren, SYNC-Taste drücken, spielen.

## Installation

1. **`Switch2ProController-Setup-….exe` herunterladen** (GitHub → *Actions* → „Switch 2 Pro – Windows-Installer“
   → letzter Lauf → *Artifacts*, bzw. unter *Releases*).
2. Setup starten und durchklicken. Es installiert automatisch alles Nötige:
   - das Programm (läuft unauffällig unten rechts im Infobereich),
   - den signierten Treiber **ViGEmBus** für den virtuellen Controller (falls noch nicht vorhanden),
   - auf Wunsch den Autostart mit Windows.
3. Fertig – beim ersten Start erscheint eine kurze Anleitung.

Voraussetzungen: Windows 10 (2004) oder Windows 11, 64 Bit, Bluetooth 4.0+ (Bluetooth LE).
Empfohlen: Windows 11 – dort sind Verbindungen deutlich schneller (geringere Eingabeverzögerung).

## Controller verbinden

1. **Kurz die kleine SYNC-Taste** oben am Controller drücken – die Lichter laufen hin und her.
2. Ein paar Sekunden warten: der Controller vibriert kurz, die Spieler-LED leuchtet. **Fertig.**

> ⚠️ **Nicht** über *Einstellungen → Bluetooth → Gerät hinzufügen* koppeln. Das ist nicht nötig:
> Der Controller benutzt ein eigenes Nintendo-Verfahren, und das Windows-Koppeln würde die Verbindung stören.

Danach reicht nach einer Pause (Controller schläft ein) **ein beliebiger Tastendruck**, um ihn wieder mit
dem PC zu verbinden – solange das Programm läuft. Wer den Controller zwischendurch an der Switch 2 nutzen
will, schaltet in den Einstellungen „Bekannte Controller per Tastendruck verbinden“ aus; dann verbindet
sich der PC nur nach SYNC. Die Kopplung mit deiner Switch 2 bleibt in jedem Fall erhalten.
Steht die Switch 2 eingeschaltet daneben, kann sie sich den Controller schnappen – dann die Konsole in den
Ruhemodus versetzen.

Mehrere Controller gleichzeitig werden unterstützt (Spieler 1–8).

## Einstellungen

**Klick auf das Controller-Symbol** im Infobereich öffnet das Einstellungsfenster
(Rechtsklick zeigt ein Schnellmenü). Alles gilt sofort:

| Einstellung | Möglichkeiten |
|---|---|
| **Windows sieht den Controller als …** | **Xbox-360-Controller** (empfohlen, läuft überall) oder **DualShock 4** (zusätzlich mit **Bewegungssteuerung/Gyro** für Steam und Emulatoren) |
| **Tastenbelegung** | **Xbox-Belegung** – nach Position: die untere Taste (Nintendo B) ist „A“, wie bei Xbox-Controllern<br>**Switch-2-Pro-Belegung** – nach Beschriftung: A bleibt A (rechts), B bleibt B (unten), wie auf der Switch |
| **Einzelne Tasten umbelegen** | Jede Taste – auch **GL/GR** (Rücktasten), **C** und **Aufnahme** – auf jede beliebige Funktion legen oder abschalten |
| **Vibration** | an/aus, Stärke |
| **Stick-Totzone** | gegen „Driften“ abgenutzter Sticks |
| **Wiederverbinden per Tastendruck** | bekannte Controller ohne SYNC verbinden (abschaltbar) |
| **Autostart** | mit Windows starten |

Unten im Fenster zeigt ein **Live-Test**, welche Tasten gerade gedrückt sind – praktisch zum Prüfen der Belegung.

Standardbelegung (Xbox 360):

| Pro Controller 2 | Xbox-Belegung | Switch-2-Pro-Belegung |
|---|---|---|
| B (unten) · A (rechts) · Y (links) · X (oben) | A · B · X · Y | B · A · Y · X |
| L / R · ZL / ZR | LB / RB · LT / RT | LB / RB · LT / RT |
| − / + · HOME | Ansicht / Menü · Xbox-Taste | Ansicht / Menü · Xbox-Taste |
| Sticks, Stick-Klicks, Steuerkreuz | identisch | identisch |
| Aufnahme | Touchpad-Klick (nur DualShock 4) | Touchpad-Klick (nur DualShock 4) |
| GL, GR, C | frei belegbar (Standard: aus) | frei belegbar |

**Tipp für Steam:** Steam erkennt den virtuellen Controller automatisch. Im DualShock-4-Modus kann Steam Input
die Bewegungssteuerung (Gyro) nutzen, z. B. für „Gyro als Maus“.

## Fehlerbehebung

| Problem | Lösung |
|---|---|
| Controller verbindet sich nicht | SYNC kurz drücken (nicht gedrückt halten). Switch 2 in den Ruhemodus. Bluetooth am PC an? Ist der Controller in den Windows-Bluetooth-Einstellungen als Gerät eingetragen → dort **entfernen**. |
| „ViGEmBus-Treiber fehlt“ | Setup erneut ausführen oder ViGEmBus manuell installieren: <https://github.com/nefarius/ViGEmBus/releases> |
| Tasten doppelt / falsch | Einstellungen → Tastenbelegung prüfen; in Steam ggf. „Nintendo-Tastenlayout“ abschalten. |
| Etwas anderes | Rechtsklick auf das Symbol → **Protokoll öffnen** und den Inhalt bei einer Fehlermeldung beilegen. |

Das Protokoll liegt unter `%LOCALAPPDATA%\Switch2ProBridge\bridge.log`, die Einstellungen unter
`%APPDATA%\Switch2ProBridge\settings.json`.

## Wie es funktioniert (technisch)

Der Switch 2 Pro Controller funkt per **Bluetooth LE**, aber nicht nach Standard: kein HID-over-GATT,
kein Standard-Pairing, sondern eigene Nintendo-GATT-Dienste. Deshalb erkennt Windows ihn von sich aus nicht.

Dieses Paket besteht aus zwei Teilen:

1. **Switch2ProBridge** (Benutzermodus-Treiber, dieses Projekt): findet den Controller über seine
   BLE-Werbung (Hersteller 0x0553, Produkt 0x2069), verbindet sich direkt über die Windows-Bluetooth-API,
   schickt die Start-Befehle, liest die Werkskalibrierung der Sticks und des Gyros, empfängt die
   Eingabeberichte und schickt Vibration (HD Rumble 2) zurück.
2. **ViGEmBus** (signierter Kernel-Treiber von Nefarius): stellt daraus einen virtuellen
   **Xbox-360-** oder **DualShock-4-Controller** bereit, den Windows und alle Spiele wie ein echtes,
   per Kabel angeschlossenes Gerät sehen.

Warum kein „echter“ Kernel-Treiber? Windows lädt neue Kernel-Treiber nur mit einer Microsoft-Signatur
(EV-Zertifikat + Attestierung). Der signierte ViGEmBus übernimmt diesen Teil; alles Controller-Spezifische
läuft sicher im Benutzermodus – das Ergebnis ist für Spiele dasselbe.

### Aufbau

```
src/Switch2Pro.Protocol   Protokoll (plattformunabhängig, mit Tests): Erkennung, Befehle,
                          Eingabeberichte, Kalibrierung, Vibration, Belegung, DS4-Bericht
src/Switch2Pro.Bridge     Windows-App: BLE-Verbindung (WinRT), ViGEm-Ausgabe, Infobereich, Einstellungen
tests/                    xUnit-Tests (laufen auch unter Linux/macOS)
installer/                Inno-Setup-Skript (Setup.exe mit ViGEmBus)
```

Selbst bauen: .NET 8 SDK, dann

```
dotnet test tests/Switch2Pro.Protocol.Tests
dotnet publish src/Switch2Pro.Bridge -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -o out/publish/win-x64
```

Den Installer baut der GitHub-Workflow `.github/workflows/switch2-pro-windows.yml` (Windows-Runner, Inno Setup).

### Quellen und Dank

- Protokoll: [ndeadly/switch2_controller_research](https://github.com/ndeadly/switch2_controller_research)
- Stick-/Gyro-Kalibrierung: SDL (`SDL_hidapi_switch2.c`, Beitrag von Valve)
- Start-Sequenz und Vibrationsformat über Bluetooth: NS2Pro-Bridge-Windows, joycon2cpp, Switch2BTLink (MIT)
- Virtueller Controller: [ViGEmBus / ViGEm.Client](https://github.com/nefarius/ViGEmBus) von Nefarius

Inoffizielles Projekt, nicht mit Nintendo verbunden. „Nintendo Switch“ ist eine Marke von Nintendo.

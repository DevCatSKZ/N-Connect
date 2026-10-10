# N-Connect – Entwicklerdokumentation

Vollständige Beschreibung aller Funktionen, des Aufbaus und der Controller-Protokolle von N-Connect – als
Grundlage für Weiterentwicklung und für Portierungen auf andere Plattformen (Linux, macOS, Android, Steam Deck …).

Stand: 04.10.2026 (Version 1.0.x, Windows, .NET 8). Alle Angaben sind aus dem Code abgeleitet; wo etwas nur
geschätzt oder nicht mit echter Hardware geprüft ist, steht das ausdrücklich dabei.

| Dokument | Inhalt |
|---|---|
| [FUNKTIONEN.md](FUNKTIONEN.md) | Jede Funktion aus Nutzersicht **und** ihr genaues Verhalten (Regeln, Grenzwerte, Sonderfälle) |
| [ARCHITEKTUR.md](ARCHITEKTUR.md) | Projekte, Komponenten, Datenfluss vom Funk bis zum virtuellen Controller, Threads, Einstellungen |
| [PROTOKOLLE.md](PROTOKOLLE.md) | Alle Controller-Protokolle bis auf Byte-Ebene: Switch 2 (BLE/USB), Switch 1/NSO, Wii, Kabel-Pads, DSU, DS4 |
| [PORTIERUNG.md](PORTIERUNG.md) | Was plattformunabhängig ist, was Windows-spezifisch ist und was es auf anderen Systemen stattdessen gibt |
| [ESP32.md](ESP32.md) | ESP32-S3 als zweiter „Bluetooth-Stick“: Export der Kopplungsdaten, Prinzip, Firmware-Leitfaden (ESP-IDF/NimBLE) |

Für Nutzer: [../README.md](../README.md). Übergabe-/Arbeitsstand: [../HANDOVER.md](../HANDOVER.md).

## In einem Satz

N-Connect liest Nintendo-Controller (Switch 2, Switch 1, Nintendo Switch Online, Wii, lizenzierte Kabel-Pads) über
Bluetooth LE, Bluetooth Classic HID und USB direkt aus, vereinheitlicht ihre Eingaben, wendet die Belegung des
Nutzers an und gibt sie über einen virtuellen Xbox-360- oder DualShock-4-Controller an Windows und Spiele weiter.

## Begriffe

| Begriff | Bedeutung |
|---|---|
| **Link** | Verbindung zu genau einem physischen Controller (`IControllerLink`) |
| **Player / Spieler** | Ein virtueller Controller; hat 1 Link (oder 2 bei einem Joy-Con-Paar) und eine Spielernummer 0–7 |
| **Rohzustand** | `ControllerState`: Tasten (`ProButtons`), Sticks 12 Bit, Bewegungsdaten in Switch-2-Achsen, Akku |
| **Einheitliche Eingabe** | `PadInput`: kalibriert und ausgerichtet, für alle Controller gleich (Pro-Controller-Schema) |
| **Belegung** | Was jede Taste auslöst (`ButtonAction`): Gamepad-Taste, Tastatur, Maus, Gyro, Shift, Turbo, Makro |
| **Ausgabe** | `GamepadState` im Xbox-Schema → ViGEm (Xbox 360 oder DualShock 4) |
| **SYNC** | Kopplungsmodus eines Controllers (kleine Taste); bei Switch 2 sichtbar an der leeren Host-Adresse in der Werbung |

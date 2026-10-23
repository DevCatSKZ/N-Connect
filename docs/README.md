# SKZ Connect – Entwicklerdokumentation

Beschreibung der Funktionen, des Aufbaus und der Controller-Protokolle von SKZ Connect – Grundlage für die
Weiterentwicklung der Windows-App, der Android-App und des Linux-Dienstes mit Kodi-Add-on.

Stand: 17.10.2026 – Windows 1.0.22, Android 0.9.1 (Beta), Kodi `service.nconnect` 0.4.4; alle .NET-Projekte auf
.NET 10. Die Angaben sind aus dem Code abgeleitet; wo etwas nur geschätzt oder nicht mit echter Hardware geprüft
ist, steht das dabei.

| Dokument | Inhalt |
|---|---|
| [FUNKTIONEN.md](FUNKTIONEN.md) | Jede Funktion der Windows-App aus Nutzersicht und ihr genaues Verhalten (Regeln, Grenzwerte, Sonderfälle) |
| [ARCHITEKTUR.md](ARCHITEKTUR.md) | Projekte und Dateien, Datenfluss vom Funk bis zum virtuellen Controller, Abläufe, Threads, Einstellungen, Abhängigkeiten |
| [PROTOKOLLE.md](PROTOKOLLE.md) | Controller-Protokolle bis auf Byte-Ebene: Switch 2 (BLE/USB), Switch 1/NSO, Wii, Kabel-Pads, DSU, DS4 |
| [PORTIERUNG.md](PORTIERUNG.md) | Was plattformunabhängig ist, was plattformabhängig ist und wie Android und Linux es lösen |
| [ANDROID.md](ANDROID.md) | SKZ Connect für Android: Aufbau, virtueller Xbox-Controller über UHID mit Shizuku oder Root, Touch, Bauen, Tests, Release, offene Punkte |
| [LINUX-KODI.md](LINUX-KODI.md) | Linux-Dienst `nconnect` (BlueZ, uinput) und Kodi-Add-ons: Aufbau, `status.json`-Vertrag, Begleitmodus, Bauen, Tests, kodi-repo |
| [SWITCH-IDENTITAET.md](SWITCH-IDENTITAET.md), [UEBERGABE-SWITCH-IDENTITAET.md](UEBERGABE-SWITCH-IDENTITAET.md) | Ruhendes Vorhaben: Joy-Con an Switch und PC ohne neues Koppeln (mit Bluepick_RCM) |
| [VERLAUF.md](VERLAUF.md) | Entwicklung bis 1.0.22 und Erkenntnisse, die man nicht wiederholen sollte |
| [release-notes/](release-notes/) | Release-Texte je Version (Deutsch und Englisch); die CI übernimmt `v<Version>.md` ins GitHub-Release |
| [archiv/](archiv/) | Verworfene Wege: ESP32/nRF52840 als zweiter „Bluetooth-Stick“ und das USB-Protokoll dazu |

Für Nutzer: [../README.md](../README.md) und die Anleitungen [Windows](anleitung/windows.md),
[Android](anleitung/android.md), [Kodi](anleitung/kodi.md), [Hilfe bei Problemen](anleitung/hilfe.md) (englisch in
[guide/](guide/)). Arbeitsstand und Release-Ablauf: [../HANDOVER.md](../HANDOVER.md). Regeln
für Agenten: [../AGENTS.md](../AGENTS.md). Lizenzen: [../THIRD-PARTY-NOTICES.md](../THIRD-PARTY-NOTICES.md).

## In einem Satz

SKZ Connect liest Controller direkt aus (unter Windows über Bluetooth LE, Bluetooth Classic HID und USB, unter Android
und Linux die Switch-2-Controller über Bluetooth LE), vereinheitlicht ihre Eingaben, wendet die Belegung des Nutzers
an und gibt sie als virtuellen Controller weiter – unter Windows als Xbox 360 oder DualShock 4 (ViGEmBus), unter
Android als Xbox One S (UHID), unter Linux als Xbox 360 (uinput).

## Begriffe

| Begriff | Bedeutung |
|---|---|
| **Link** | Verbindung zu genau einem physischen Controller (`IControllerLink`) |
| **Player / Spieler** | Ein virtueller Controller; hat 1 Link (oder 2 bei einem Joy-Con-Paar) und eine Spielernummer 0–7 |
| **Rohzustand** | `ControllerState`: Tasten (`ProButtons`), Sticks 12 Bit, Bewegungsdaten in Switch-2-Achsen, Akku |
| **Einheitliche Eingabe** | `PadInput`: kalibriert und ausgerichtet, für alle Controller gleich (Pro-Controller-Schema) |
| **Belegung** | Was jede Taste auslöst (`ButtonAction`): Gamepad-Taste, Tastatur, Maus, Gyro, Shift, Turbo, Makro |
| **Ausgabe** | `GamepadState` im Xbox-Schema → virtueller Controller der Plattform |
| **SYNC** | Kopplungsmodus eines Controllers (kleine Taste); bei Switch 2 sichtbar an der leeren Host-Adresse in der Werbung |
| **Begleitmodus** | Kodi-Add-on unter Windows, Android oder macOS: verbindet nichts selbst; unter Windows und Android zeigt es den Status der SKZ-Connect-App an, unter macOS (keine App) nur einen Hinweis |

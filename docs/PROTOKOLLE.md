# Controller-Protokolle

Alles, was N-Connect über die Controller weiß, auf Byte-Ebene. Umgesetzt im plattformunabhängigen Projekt
`Switch2Pro.Protocol` – eine Portierung kann es unverändert übernehmen (C#/.NET) oder danach neu schreiben.
Zahlen hexadezimal, „LE“ = niedrigstes Byte zuerst. Quellen: ndeadly/switch2_controller_research, SDL
(`SDL_hidapi_switch2.c`, `SDL_hidapi_switch.c`), dekuNukem/Nintendo_Switch_Reverse_Engineering, Linux
`hid-nintendo`/`hid-wiimote`, WiiBrew, Switch2Connect, eigene Messungen (gekennzeichnet).

## 0. Gemeinsames Zielformat

Alle Verbindungen liefern einen `ControllerState`:
- **Tasten** als `ProButtons`-Bits im Pro-Controller-Schema (B unten, A rechts, Y links, X oben) plus C, GL, GR,
  SL/SR je Seite, Headset.
- **Sticks** als 12-Bit-Rohwerte 0–4095, **Y wächst nach oben**; Kalibrierung je Achse: Mitte, Ausschlag nach
  oben (`Max`) und unten (`Min`); `Normalize(raw) = (raw−Mitte)/Max bzw. /Min`, auf −1…1 begrenzt.
- **Bewegung** (`Motion`) in den Achsen und Einheiten des Switch 2 Pro Controllers: Beschleunigung 4096 ≙ 1 g,
  Gyro 32767 ≙ 2000 °/s. Alle anderen Controller werden darauf umgerechnet.
- **Akku** in Prozent (−1 = unbekannt), Laden ja/nein, bei Switch 2 zusätzlich die Spannung (mV).

---

## 1. Switch 2 über Bluetooth LE

### 1.1 Erkennen (Werbung)
Herstellerdaten Kennung **0x0553**; Inhalt (ohne die 2 Kennungs-Bytes):
`01 00 03 | 7E xx | PID (LE) | … | Byte 10–15: Host-Adresse (LE)`
- PID: 0x2069 Pro Controller 2, 0x2067 Joy-Con 2 L, 0x2066 Joy-Con 2 R, 0x2073 GameCube-Controller.
- Byte 4 (VID-High) schwankt je Firmware und wird nicht geprüft.
- Host-Adresse **leer = SYNC-Modus**; sonst die Adresse des Hosts, mit dem er gekoppelt ist (Konsole oder PC).
- Kein Standard-Pairing (SMP), kein HID-over-GATT – man verbindet sich direkt.

### 1.2 GATT
| UUID | Zweck |
|---|---|
| `ab7de9be-89fe-49ad-828f-118f09df7fd0` | Dienst |
| `ab7de9be-…-7fd2` | Eingabebericht 0x05 (Benachrichtigung) – alle Controller |
| `7492866c-ec3e-4619-8258-32755ffcc0f8` | Bericht 0x09 (nur Pro, Ausweichweg) |
| `649d4ac9-8eb7-4e6c-af44-1ea54fe5f005` | Befehle (Schreiben ohne Antwort) |
| `c765a961-d9d8-4d36-a20a-5315b111836a` | Antworten (Benachrichtigung) |
| `cc483f51-9258-427d-a939-630c31f72b05` | Vibration Pro/GameCube |
| `289326cb-a471-485d-a8f4-240c14f18241` / `fa19b0fb-cd1f-46a7-84a1-bbb09e00c149` | Vibration Joy-Con 2 L / R |

Verbindungsintervall: der Controller wählt 30 ms (~33 Berichte/s); N-Connect fordert „ThroughputOptimized“
(7,5 ms, ~60 Berichte/s) an und fragt höchstens dreimal nach, wenn der Controller zurückschaltet.

### 1.3 Befehle
Kopf 8 Byte: `Befehl | 91 (Host→Controller) | Transport (01 BLE, 00 USB) | Unterbefehl | 00 | Datenlänge | 00 00`,
dann Daten. Antworten: `Befehl | 01 | … | Unterbefehl …`, Daten ab Byte 8.

| Befehl/Unter | Daten | Zweck |
|---|---|---|
| 0C/02, 0C/04 | Maske, 0, 0, 0 | Feature-Maske setzen / einschalten. Bits: 01 Tasten, 02 Sticks, 04 Bewegung, 10 Maus (Joy-Con), 20 Vibration. N-Connect: Pro/GameCube **0x2F**, Joy-Con 2 **0x37** (andere Bits erzeugen Phantom-ZL/ZR) |
| 02/04 | Länge, 7E, 0, 0, Adresse (LE, 4) | Speicher lesen (≤ 0x4F Byte). Antwort-Daten: Länge, 3×0, Adresse, Daten |
| 09/07 | Maske, 7×0 | Spieler-LEDs; Muster je Spieler 1–8: `1 3 7 F 9 5 D 6` |
| 0A/02 | 03, 0, 0, 0 | eingebautes Vibrationsmuster („Verbunden“-Klick) |
| 08/01 | 20, 0, 0, 0 | Joy-Con 2: Daten des Charging Grip; im Griff, wenn ab Byte 8+4+0x12 `7E 05 68 20` steht |
| 08/02 | 1/0, 0, 0, 0 | Joy-Con 2: GL/GR des Griffs ein/aus |
| 10/01 | – | Firmware-Version |
| 15/… | siehe 1.6 | Kopplung mit einem Host |

Speicheradressen: 0x13000 Gerätedaten (Seriennummer, VID/PID, Farben, 0x40 Byte) · 0x130A8/0x130E8
Werkskalibrierung linker/rechter Stick (9 Byte) · 0x1FC040/0x1FC080 Benutzerkalibrierung (Kennung `B2 A1`, dann
9 Byte) · 0x13040 Gyro-Nullpunkt (ab Offset 4: 3 × float32 rad/s) · 0x13140 GameCube-Trigger-Ruhewerte (2 Byte) ·
0x1FA000 gespeicherte Kopplung.

Stick-Kalibrierung (9 Byte = 6 gepackte 12-Bit-Werte): Mitte X/Y, Max X/Y, Min X/Y. 12-Bit-Paar aus 3 Byte:
`A = b0 | (b1 & 0x0F) << 8`, `B = b1 >> 4 | b2 << 4`. Ungültig: 0/0xFFF oder Ausschlag < 200.

Start (Bluetooth, `Switch2BleLink`): Feature-Maske setzen und einschalten, dann Eingaben abonnieren, danach
Kalibrierung/Gerätedaten lesen. Es werden **keine** Kopplungsdaten geschrieben (außer nach SYNC, 1.6).

### 1.4 Eingabebericht 0x05 (BLE ohne Report-ID; USB mit vorangestellter ID)
| Byte | Inhalt |
|---|---|
| 0–3 | Zähler/Status (nicht ausgewertet) |
| 4 | ZR 80, R 40, SL(R) 20, SR(R) 10, A 08, B 04, X 02, Y 01 |
| 5 | C 40, Aufnahme 20, HOME 10, LS 08, RS 04, + 02, − 01 |
| 6 | ZL 80, L 40, SL(L) 20, SR(L) 10, ◀ 08, ▶ 04, ▲ 02, ▼ 01 |
| 7 | Headset 10, GL 02, GR 01 |
| 0x0A–0x0C | linker Stick (12-Bit-Paar) |
| 0x0D–0x0F | rechter Stick |
| 0x10–0x17 | Joy-Con 2: Maussensor X, Y (16-Bit-Zähler), Rauheit, Abstand – „auf Fläche“: Abstand 1–999 und Rauheit < 4000 |
| 0x1F–0x20 | Akkuspannung mV (LE) |
| 0x21 | Ladezustand: 00 kein Kabel, 20 Kabel/Ladepause, 2C/3C/34 lädt (gemessen) |
| 0x30–0x3B | Beschl. X/Y/Z, Gyro X/Y/Z (int16 LE) |
| 0x3C, 0x3D | GameCube: analoge Trigger L, R (0–255; Ruhewert aus 0x13140) |

Bericht 0x09 (Ausweichweg, Pro): Byte 1 Akku-Stufe (Bit 2–5, 0–9) und Laden (Bit 1), Tasten in Byte 2–4, Sticks ab
5 und 8.

Akku aus Spannung siehe FUNKTIONEN 11 (`InputReports.BatteryPercentFromMillivolts`, `BatteryEstimator`).

### 1.5 Vibration (HD-Rumble 2)
Frame 5 Byte (40 Bit LE): hohe Frequenz (Bit 0–9), hohe Amplitude (10–19), tiefe Frequenz (20–29), tiefe
Amplitude (30–39). N-Connect: hoch 0x187, tief 0x112, Amplitude ≤ 453 (= 29000 ≫ 6 wie SDL).
- Pro/GameCube BLE: 33 Byte, Byte 0 = 0, ab Byte 1 und 0x11 je `50|Zähler` + Frame.
- Joy-Con 2 BLE: 17 Byte, Byte 1 = `50|Zähler`, Frame ab Byte 2.
- USB: HID-Ausgabebericht 0x02 (64 Byte) wie BLE-Paket mit Report-ID statt der 0.
- GameCube USB: Bericht 0x03, Byte 2: 1 an, 0 aus, 2 Stopp (Stärke per Fehlerdiffusion über die Zeit).
- Wiederholt etwa alle 12 ms, solange vibriert wird.

### 1.6 Kopplung mit dem PC (Befehl 0x15, nach SYNC)
1. `15/01`: Daten `00 02` + Host-Adresse (LE) zweimal.
2. `15/04`: `00` + 16 Byte Host-Schlüssel A1 → Antwort enthält B1 (Byte 9–24); **LTK = A1 XOR B1**.
3. `15/02`: `00` + 16 Byte Aufgabe A2 → Antwort B2 muss `AES-128-ECB(LTK umgedreht, A2 umgedreht)` sein.
4. `15/03`: `00` → Controller speichert Adressen und LTK (ersetzt die Kopplung mit der Konsole).
Vorher wird 0x1FA000 gelesen: steht der PC schon drin, wird nichts geschrieben. Aufbau 0x1FA000: Byte 0 Anzahl,
ab 0x08 Einträge à 0x28: Host-Adresse (6, höchstes Byte zuerst), 12×0, LTK (16), 6×0.

## 2. Switch 2 über USB
- Pro Controller 2 (057E:2069) und GameCube (057E:2073). **Interface 1 (WinUSB, Bulk)** für Befehle, **Interface 0
  (HID)** für Eingaben (Bericht 0x05 mit Report-ID, bis ~500/s) und Vibration.
- Befehle wie 1.3 mit Transport 00. Speicher lesen: `02/01` mit `40 00 00 00` + Adresse (LE) → 16 Byte Kopf + 0x40 Byte.
- Start (wie SDL): `07/01`, `0C/02` Maske, `11/01`, `0A/08 …`, `0C/04` Maske, `01/0C`, `01/01`, `08/02 01`,
  `03/0A 05` (Bericht 0x05), `03/0D …` (Ausgabe starten). Siehe `Commands.UsbInitSequence`.

## 3. Switch 1 und Nintendo Switch Online (Bluetooth Classic HID)
- VID 057E; PID 0x2009 Pro Controller, 0x2006 Joy-Con L, 0x2007 Joy-Con R (auch NES), 0x2017 SNES, 0x2019 N64,
  0x201E Mega Drive. Der Gerätetyp aus der Geräteinfo (Byte 2) unterscheidet: 07–0A NES, 0B SNES, 0C N64, 0D MD.
- **Ausgabe 0x01** (49 Byte): ID, Zähler (0–F), 8 Byte Vibration (L, R), Unterbefehl, Daten. **0x10**: nur Vibration.
  Neutrale Vibration je Seite `00 01 40 40`.
- **Antwort 0x21**: Byte 13 Bit 7 = Bestätigung, Byte 14 = Unterbefehl, Daten ab 15.
- Unterbefehle: 02 Geräteinfo (Firmware Byte 0–1, Typ Byte 2, MAC Byte 4–9), 03 Eingabemodus (30 = Vollbericht),
  06 HCI-Zustand (00 = schlafen), 10 SPI lesen (Adresse LE 4 Byte, Länge ≤ 0x1D; Antwort: Adresse, Länge,
  Daten), 30 Spieler-LEDs, 40 IMU ein, 48 Vibration ein. N-Connect: 3 Versuche à 300 ms.
- SPI: 0x603D/0x6046 Werks-Stickkalibrierung L/R, 0x8010/0x801B Benutzer (Kennung `B2 A1`), 0x6020/0x8026 IMU,
  0x6050 Farben (Gehäuse, Tasten, Griff L/R je 3 Byte). Stick-Kalibrierung L: Max, Mitte, Min; R: Mitte, Min, Max.
- **Vollbericht 0x30**: Byte 2 Akku (Stufe Bit 5–7: 0, 2, 4, 6, 8; Bit 4 Laden); Byte 3 rechts (Y 01, X 02, B 04,
  A 08, SR 10, SL 20, R 40, ZR 80); Byte 4 gemeinsam (− 01, + 02, RS 04, LS 08, HOME 10, Aufnahme 20); Byte 5 links
  (▼ 01, ▲ 02, ▶ 04, ◀ 08, SR 10, SL 20, L 40, ZL 80); Byte 6–8 / 9–11 Sticks; ab Byte 13 drei IMU-Proben à 12 Byte
  (verwendet: die neueste, Byte 37–48; mit Ring-Con die zweite).
- **IMU-Umrechnung** auf Switch-2-Achsen: `X₂ = −Y₁, Y₂ = X₁, Z₂ = Z₁`; rechter Joy-Con zusätzlich X und Z
  umgedreht. Beschl. `v·4·4096/(Sens−Origin)`, Gyro `(v−Origin)·936/(Sens−Origin)` °/s.
- **Vibration**: 4 Byte je Seite, Frequenzcodes hoch 0x0074 / tief 0x3D, Amplitudentabellen wie SDL.
- **NSO-Anordnung** (`Mapping.NormalizeClassic`, Rohbit → einheitliche Taste):
  - NES/SNES: wie beschriftet, ohne Sticks und Bewegung.
  - N64: A→B, B→Y, ZL→ZL (Z), linker-Stick-Klick-Bit→ZR, L→L, R→R, +, HOME, Aufnahme, Steuerkreuz unverändert;
    C-Tasten als rechter Stick: Y = C-hoch, ZR = C-runter, X = C-links, − = C-rechts.
  - Mega Drive: A→Y, B→B, R→A (C), Y→X, X→L, L→R, ZR→− (MODE), +, HOME, Aufnahme, Steuerkreuz unverändert; keine Sticks.
- **amiibo/NFC, IR-Kamera, Ring-Con**: über den Zusatzprozessor (MCU), Eingabemodus 0x31, MCU-Berichte 0x11/0x31,
  Unterbefehle 0x21/0x22, Ring-Con über externe Geräte 0x59/0x5A/0x5C – Details in `Nfc.cs`, `IrCamera.cs`,
  `Switch1.cs`.
- USB (Pro Controller am Kabel): vorher `80 02`, `80 03`, `80 02`, `80 04` senden (Handshake, schnell, nur USB).

## 4. Wii-Fernbedienung und Wii U Pro (Bluetooth Classic HID)
- VID 057E, PID 0x0306 (Fernbedienung), 0x0330 (Plus und Wii U Pro). Bit 0 des 2. Bytes jedes Ausgabeberichts =
  Vibration.
- Ausgaben: 11 LEDs (Bit 4–7; Spieler 5–8 als Muster), 12 Berichtsmodus (`[12, 04|Rumble, Modus]`, 04 = dauernd;
  N-Connect sendet **ohne** 04 = nur bei Änderung), 13/1A IR-Kamera an, 15 Status, 16 Register schreiben (≤ 16 Byte),
  17 Register lesen.
- Eingaben: 20 Status (Byte 3 Bit 1 = Erweiterung gesteckt, Byte 6 Akku 0–200), 21 Leseantwort (Byte 3: Größe−1
  und Fehler, Byte 4–5 Adresse low, Daten ab 6), 22 Bestätigung, 30–3D Daten. **Nach jedem Statusbericht muss
  der Berichtsmodus neu gesetzt werden.**
- Modi: 34 Tasten + 19 Byte Erweiterung (Wii U Pro), 35 Tasten + Beschl. + 16 Byte Erweiterung,
  37 Tasten + Beschl. + 10 Byte IR + 6 Byte Erweiterung (mit Zeiger).
- **Erweiterung**: unverschlüsselt initialisieren (`55 → A400F0`, `00 → A400FB`), Kennung bei A400FA (6 Byte):
  `…A4 20 00 00` Nunchuk, `01 01` Classic (auch Pro), `01 20` Wii U Pro, `04 05`/`05 05`/`07 05` MotionPlus allein/
  mit Nunchuk/mit Classic. MotionPlus suchen: `55 → A600F0`, Kennung bei A600FA (`…A6 20 ?? 05`), einschalten
  mit 04/05/07 → A600FE.
- **Kern-Tasten** (Byte 1–2): ◀ 01, ▶ 02, ▼ 04, ▲ 08, + 10 | 2 01, 1 02, B 04, A 08, − 10, HOME 80.
  Beschl. 8 Bit (Ruhe 0x80, 1 g ≈ 26 Schritte).
- **Nunchuk**: Stick Byte 0–1 (8 Bit), C/Z Byte 5 Bit 1/0 (0 = gedrückt). **Classic**: 6-/5-Bit-Sticks, Tasten in
  Byte 4–5 (0 = gedrückt). **MotionPlus**: 14 Bit je Achse (Mitte 8192), „langsam“-Bits → ±440 °/s statt ±2000,
  Durchreichen wechselt sich mit den Erweiterungsdaten ab (Byte 5 Bit 1 unterscheidet).
- **Wii U Pro** (Erweiterungsdaten ≥ 11 Byte): vier 12-Bit-Sticks LX, RX, LY, RY (16 Bit LE), Tasten wie Classic
  in Byte 8–9, Byte 10: Stick-Klicks (0 = gedrückt), Akku Bit 4–6 (0–4), Laden Bit 2 (0 = lädt).
- **IR** (einfaches Format, 10 Byte = 4 Punkte): Kamera-Setup `08 → B00030`, Empfindlichkeit `B00000`/`B0001A`,
  Modus `01 → B00033`, `08 → B00030`. Punkte 10 Bit (1024 × 768), 0x3FF = keiner.
- **Kopplung**: binäre PIN = 6 Byte Bluetooth-Adresse (LE): Adresse des PCs (rote SYNC-Taste, verbindet sich
  später per Tastendruck) oder die eigene (1+2, kein Wiederverbinden).

## 5. Kabel-Pads HORI/PowerA/PDP (USB-HID)
Bekannte VID/PID in `WiredSwitchPad.Known` (z. B. 0F0D:00C1 HORIPAD, 20D6:A711–A716 PowerA, 0E6F:0180–0188 PDP).
Bericht ohne Report-ID, 7 Byte (+ herstellerspezifisch):
`[0]` Y 01, B 02, A 04, X 08, L 10, R 20, ZL 40, ZR 80 · `[1]` − 01, + 02, LS 04, RS 08, HOME 10, Aufnahme 20 ·
`[2]` Steuerkreuz 0–7 ab „oben“ im Uhrzeigersinn, 8/15 = keins · `[3..6]` LX, LY, RX, RY (0–255, Mitte 128,
**Y nach unten**). Kein Gyro, keine Vibration. Mit echter Hardware noch nicht geprüft.

## 6. Bluetooth-Kopplung klassischer Controller (Windows-API)
`BluetoothFindFirstDevice` mit Suche (Inquiry, Dauer `cTimeoutMultiplier` × 1,28 s; N-Connect: 1) liefert auch
bekannte Geräte. **SYNC-Erkennung bei bekannten Geräten:** `stLastSeen` vor und nach der Suche vergleichen – nur
ein Gerät im Kopplungsmodus antwortet und bekommt einen neuen Wert. Switch 1/NSO: `BluetoothAuthenticateDeviceEx`
(MITM nicht nötig, Bonding) mit `BluetoothRegisterForAuthenticationEx`-Rückruf, der per
`BluetoothSendAuthenticationResponseEx` zustimmt (Legacy-PIN wird abgelehnt). Wii: `BluetoothAuthenticateDevice`
mit 6-Zeichen-PIN (Adressbytes). Danach `BluetoothSetServiceState` (HID-Dienst 00001124-…).

## 7. Ausgabe
### 7.1 Xbox 360 (ViGEm)
`GamepadState`: Tasten-Bits wie XInput, Trigger 0–255, Sticks −32768…32767 (Y oben positiv).

### 7.2 DualShock 4 (ViGEm, `DS4_REPORT_EX`, 63 Byte ohne Report-ID)
Byte 0–3 Sticks (0–255, Y umgedreht), 4–5 Steuerkreuz (Hat) + Tasten (□ 4, ✕ 5, ○ 6, △ 7, L1 8, R1 9, L2 10, R2 11,
Share 12, Options 13, L3 14, R3 15), 6 PS (Bit 0) / Touchpad-Klick (Bit 1), 7–8 Trigger, 9–10 Zeitstempel
(5,33 µs), 12–17 Gyro (16 LSB je °/s: X, Z, −Y), 18–23 Beschl. (8192 je g: X, Z, −Y). Ohne Bewegungsdaten:
Beschl. 1 g nach oben. Vibration vom Spiel: USB-Ausgabebericht 0x05 (Byte 4 klein, Byte 5 groß).

### 7.3 Cemuhook/DSU (UDP 127.0.0.1:26760)
Kopf „DSUS“, Version 1001, Länge, CRC-32 (zlib) über das ganze Paket mit genulltem CRC-Feld, Server-ID, Typ:
0x100000 Version, 0x100001 Port-Info (4 Slots), 0x100002 Daten (80 Byte: Slot-Info, Paketnummer, Tasten wie DS4,
Sticks 0–255 Y oben, Druckwerte, Zeitstempel µs, Beschl. in g und Gyro in °/s in DS4-Achsen). Slot = Spielernummer.

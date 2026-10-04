# Funktionen und Verhalten

Jede Funktion mit ihrem genauen Verhalten. Zahlen (Zeiten, Grenzwerte) sind die im Code verwendeten Werte;
Fundstellen in Klammern (`Datei`/`Klasse`), damit sich Details schnell nachschlagen lassen.

---

## 1. Unterstützte Controller

| Controller | Verbindung | Eingaben | Ausgaben an den Controller |
|---|---|---|---|
| Switch 2 Pro Controller | BLE (eigene GATT-Dienste), USB (WinUSB + HID) | Tasten inkl. C, GL/GR, Sticks, Gyro/Beschl., Akku | HD-Rumble, Spieler-LEDs, „Verbunden“-Klick |
| Joy-Con 2 (L/R) | BLE | wie oben + optischer Maussensor, Charging Grip (GL/GR) | HD-Rumble (1 Motor), LEDs |
| GameCube-Controller (Switch 2) | BLE, USB | Tasten, Sticks, **analoge Trigger** | Motor an/aus (USB: über Fehlerdiffusion abgestuft), LEDs |
| Switch Pro Controller (Switch 1) | Bluetooth Classic HID, USB | Tasten, Sticks, Gyro, Akku, NFC | HD-Rumble, LEDs |
| Joy-Con (L/R) (Switch 1) | Bluetooth Classic HID | Tasten, Stick, Gyro, NFC (R), IR-Kamera (R), Ring-Con (R) | HD-Rumble, LEDs |
| NES, SNES, N64, Mega Drive (NSO) | Bluetooth Classic HID (Switch-1-Protokoll) | Tasten, N64-Stick | LEDs |
| Wii-Fernbedienung (+ Plus) | Bluetooth Classic HID | Tasten, Beschl., IR-Zeiger, MotionPlus, Nunchuk, Classic | Vibration, LEDs |
| Wii U Pro Controller | Bluetooth Classic HID (Wii-Protokoll) | Tasten, 2 Sticks, Akku | Vibration, LEDs |
| Kabel-Pads HORI/PowerA/PDP | USB-HID (einfaches Format) | Tasten, Sticks, Steuerkreuz | – |
| Nachbauten im Switch-Modus (8BitDo, „Lic Pro Controller“) | wie Switch Pro Controller | wie Pro Controller, soweit unterstützt | wie Pro Controller |

Bis zu **8 Spieler** gleichzeitig (`ControllerManager.MaxPlayers`). Der 9. Controller wird abgewiesen (Einblendung).

---

## 2. Verbinden und Koppeln

### 2.1 Switch-2-Controller (Bluetooth LE)
- N-Connect sucht dauerhaft **aktiv** nach BLE-Werbung mit Herstellerkennung **0x0553** (Nintendo) und erkennt
  die Art an der Produkt-ID in der Werbung (`Advertisement.Kind`).
- **SYNC** (Host-Adresse in der Werbung leer): Controller wird immer verbunden und danach **mit dem PC gekoppelt**
  (Nintendo-Verfahren, Befehl 0x15 – nur wenn „Per Tastendruck verbinden“ an ist und er nicht schon mit diesem PC
  gekoppelt ist). Danach reicht ein Tastendruck. Achtung: Der Controller merkt sich **genau einen** Host – an der
  Switch 2 muss er danach einmal neu per SYNC gekoppelt werden (einmaliger Hinweis).
- **Tastendruck** (Werbung mit Host-Adresse): verbunden wird nur, wenn die Host-Adresse die des PC-Adapters ist
  (sonst gehört der Controller zu einer Konsole und würde ablehnen).
- Eben von Hand getrennte Controller werden erst wieder verbunden, wenn sie ≥ 3 s nicht mehr geworben haben
  (Controller schläft) und neu gedrückt wird; SYNC verbindet immer.
- Eine Windows-Kopplung („Gerät hinzufügen“) stört und wird automatisch entfernt.
- Nach dem Verbinden: kurzes „Verbunden“-Vibrieren (abschaltbar), Spieler-LEDs wie an der Konsole.
- Die Verbindung gilt als verloren, wenn **2,5 s** keine Eingabe kommt.

### 2.2 Switch-2-Controller per USB
- Pro Controller 2 (PID 0x2069) und GameCube-Controller (0x2073) über USB: WinUSB für Befehle, HID für Eingaben
  (bis ~500 Berichte/s). Steckt ein per Bluetooth verbundener Controller am Kabel, übernimmt USB und die
  Bluetooth-Verbindung wird beendet.
- Getrennt nach **3 s** ohne Eingabe (kurze Pause beim Wechsel Bluetooth → USB wird toleriert).
- Spiele sehen den USB-Controller zusätzlich direkt → **„Doppelt angezeigt? Verstecken“** (HidHide, Abschnitt 9).

### 2.3 Switch 1, NSO, Wii – selbst koppeln
- Diese Controller nutzen Bluetooth Classic. N-Connect koppelt sie **selbst** mit Windows (`ControllerPairing`):
  - Switch 1/NSO: Kopplung ohne PIN („Just Works“) über WinRT (`DeviceInformationCustomPairing.PairAsync`,
    `ConfirmOnly | ConfirmPinMatch`, Anfrage wird sofort bestätigt, höchstens 20 s). Nur wenn WinRT das Gerät gar
    nicht öffnen kann: Win32 (`BluetoothAuthenticateDeviceEx` + `BluetoothRegisterForAuthenticationEx`) – dort kam
    die Rückfrage bei Joy-Con oft erst nach dem Abbruch an (Fehler 1244/258, Antwort danach 1167).
  - Wii: binäre PIN = Adresse des PC-Adapters (dreimal versucht – nur so verbindet sich die Fernbedienung später
    per Tastendruck), sonst eigene Adresse (Kopplung „1+2“, ohne Wiederverbinden; Hinweis im Protokoll).
  - Danach HID-Dienst einschalten; der Controller wird beim nächsten HID-Suchlauf gefunden.
- **Hintergrundsuche** („Neue Controller automatisch koppeln“, Standard an): Suchläufe von 1,3 s – alle 6 s, bzw.
  alle 15 s, wenn ein Switch-2-Controller verbunden ist; **nie**, während jemand spielt (Eingabe in den letzten 20 s)
  oder gerade ein Controller verbunden wird. Bereits gekoppelte Controller (z. B. Joy-Con, die zwischendurch an der
  Switch hingen) werden neu gekoppelt, wenn sie im SYNC-Modus sind. Erkannt wird das daran, dass Windows sie
  **während dieses Suchlaufs** (`stLastSeen` vorher/nachher) oder **in den letzten 8 s** gesehen hat – **außer**
  sie waren in den letzten 30 s verbunden (`ControllerPairing.NoteDisconnected`, aufgerufen beim Trennen; sonst
  würde ein eben ausgeschalteter Controller „gesehen“ und seine Kopplung gelöscht). Ausgeschaltete oder mit der
  Switch verbundene Controller antworten nicht und bleiben unberührt. Übersprungene bekannte Controller stehen
  einmal pro Minute im Protokoll („… nicht im Kopplungsmodus erkannt“).
- Nach einem **Fehlschlag** lässt die Hintergrundsuche den Controller **90 s** in Ruhe (sonst wäre er bei jedem
  Versuch belegt und tauchte auch in „Gerät hinzufügen“ von Windows nicht auf). Das Fenster „Controller koppeln“
  versucht es trotzdem.
- Achtung: Vor dem Neukoppeln wird die alte Windows-Kopplung entfernt (sonst verweigert Windows die neue). Scheitert
  die neue Kopplung (z. B. Wii, Fehler 259), muss der Controller erneut per SYNC gekoppelt werden.
- **„Controller koppeln …“** (Fenster): sucht 60 s lang, 1 s Pause zwischen den Läufen, koppelt auch bekannte
  Controller neu, die gerade sichtbar sind.
- Erkannte Namen: „Joy-Con (L/R)“, „Pro Controller“, „Lic Pro Controller“, „NES/HVC/SNES/N64 Controller“,
  „MD/Gen Control Pad“, „Nintendo RVL-CNT-01…“ (Wii), „Nintendo RVL-WBC-01…“.
- Gekoppelte Classic-Controller werden alle 2 s über die HID-Geräteliste gesucht und verbunden (Switch 1/NSO:
  Gerätetyp aus der Geräteinfo bestimmt NES/SNES/N64/Mega Drive; Wii: Erweiterung aus dem Register).

- **Joy-Con im Ladegriff per USB** (057E:200E): je Joy-Con eine HID-Schnittstelle; Seite aus dem Gerätetyp der
  Geräteinfo (0x01 links, 0x02 rechts, 0x03 Pro). USB-Handshake wie beim Pro Controller (0x80 02/03/02/04).
  Leere Griff-Schnittstellen antworten nicht und werden still alle 5 s erneut versucht.

### 2.4a Originale vor Steam und Spielen verstecken (HidHide)
- Steam und viele Spiele (SDL) unterstützen Switch-1-, NSO-, Switch-2-USB-Controller und Kabel-Pads **selbst** und
  sähen sie doppelt (Original + virtueller Controller) – Eingaben kämen doppelt/vermischt an, Steam schickt dem
  Original außerdem eigene Startbefehle. Wii-Controller sind nicht betroffen.
- Einstellung **„Original-Controller verstecken“** (`HideFromGames`, Standard an): Jeder solche Controller wird nach
  dem Verbinden automatisch per HidHide versteckt (`ControllerManager.QueueHide`): 3 s sammeln (Joy-Con-Paar), dann
  **eine** UAC-Abfrage (`HidHideCLI --app-reg <N-Connect> --dev-hide … --cloak-on`). Erfolg → `HiddenDevices`, Meldung
  (läuft Steam: „Steam einmal neu starten“ – Steam hält sein schon geöffnetes Handle). Abgebrochen → in dieser
  Sitzung nicht erneut fragen; Nachholen per Karte → Extras → „Doppelt angezeigt?“ oder Einstellung aus/ein.
- Bluetooth-Instanz-IDs ändern sich nach jedem Neukoppeln → dann erneut eine Abfrage.
- HidHide fehlt → einmalige Meldung, auf „Allgemein“ Knopf „HidHide installieren …“. Der Installer bringt HidHide
  mit (Aufgabe standardmäßig angehakt; Neustart nötig).

### 2.4 Kabel-Pads und Nachbauten
- HORI/PowerA/PDP-Kabel-Pads werden an Hersteller-/Produktkennung erkannt (`WiredSwitchPad.Known`) und laufen als
  „Switch Pro Controller“ (gleiche Tasten, Grafik, Belegung) unter eigenem Namen – ohne Gyro/Vibration/LEDs.
- Nachbauten im Switch-Modus: Beantworten sie Speicher-Lesebefehle nicht, nutzt N-Connect nach der ersten
  ausbleibenden Antwort Standardwerte (keine weiteren Wartezeiten). Melden sie keine Bluetooth-Adresse
  (00:… bzw. FF:…), dient die Geräte-Instanz als Kennung.

### 2.5 Trennen und Schlafen
- **Trennen** (Karte, Infobereich, „Alle Controller trennen“): Switch 1 wird schlafen gelegt (HCI-Befehl, sonst hielte
  Windows die Verbindung; höchstens 1 s gewartet), die anderen werden getrennt. Bluetooth-Controller verbinden sich
  danach erst nach Pause und neuem Tastendruck (siehe 2.1), USB-Controller erst nach Ab- und Anstecken des Kabels.
- **Bei Inaktivität trennen** (0 = nie, sonst Minuten): Prüfung alle 10 s; USB-Controller werden nie getrennt.
  „Aktivität“ = Taste, Stick > 0,3, Trigger > 0,3, Drehung > ~20 °/s oder Mausbewegung.

---

## 3. Übersicht (Controller-Seite)

- Je Spieler eine **Karte**: Live-Grafik (Form/Tasten nach Produktfotos, Originalfarben aus dem Controller,
  gedrückte Tasten leuchten, Sticks bewegen sich, analoge Trigger füllen sich), Akku, Verbindung (Art +
  Berichte/s), Griff/Maus (Joy-Con 2), gedrückte Tasten. Zwei Spalten bei breitem Fenster, zugeklappte Karten
  gleich hoch.
- **Spieler-Reihenfolge** (`PlayerOrderBar`, ab zwei Spielern über den Karten): je Spieler ein Chip (Nummer, Name,
  ‹ ›). Spieler 1 ist hervorgehoben – das ist für Windows, Steam und Spiele der erste Controller. ‹ › tauscht mit
  dem Nachbarn (`ControllerManager.MovePlayer`).
- **Titel anklicken** → Menü: einen belegten Spielerplatz wählen (= tauschen) und Controller umbenennen
  (bei einem Paar je Joy-Con).
- Knöpfe: **Vibrieren** (welcher ist welcher?), **Trennen**, **Einstellungen** (klappt Reiter für genau diesen
  Controller auf): Tasten, Feineinstellung, Gyro, Joy-Con, Extras, Details.
- Schwache Verbindung (< 20 Berichte/s dauerhaft nach 20 s) → einmalige Einblendung mit Tipp (nicht bei Wii, da sie
  absichtlich nur bei Änderung sendet).

## 3a. Ausgabeart (Xbox 360 / DualShock 4)
- Allgemein (`Settings.OutputMode`, Standard **Xbox 360** – läuft mit fast allen Spielen und Steam) und **je
  Controller** (`Settings.ControllerOutputs`, Adresse → Art; Karte → Einstellungen → Tasten → „Erscheint als“: Wie
  allgemein / Xbox 360 / DualShock 4; ebenso im Infobereich). DualShock 4 bringt Gyro nach Steam/Emulatoren.
- Paar: gilt die erste eigene Einstellung eines der beiden Joy-Con (`Player.DesiredOutput`).
- Änderung → `ControllerManager.ApplyOutputMode`: passt ein virtueller Controller nicht mehr, werden **alle** in
  Spielerreihenfolge neu angelegt (Xbox-Plätze bleiben in Reihenfolge). Auch nach Verbinden/Zusammenfassen geprüft.

## 4. Spielerplätze und Namen
- **Platz**: Leiste „Spieler-Reihenfolge“, Klick auf den Kartentitel oder Infobereich → Spieler → Spielerplatz.
  Angeboten werden nur belegte Plätze (= tauschen). LEDs und DSU-Slot folgen; die virtuellen Controller werden in der
  neuen Reihenfolge **neu angelegt** (Windows vergibt Xbox-Plätze nach Anlegereihenfolge; Spiele sehen kurz ein
  Trennen/Verbinden). Der Platz wird je Controller gemerkt (`Settings.PlayerSlots`).
- **Immer lückenlos** (`ControllerManager.CompactPlayers`): Fällt ein Spieler weg (getrennt, Joy-Con zum Paar
  zusammengefasst), rücken die übrigen auf – Spieler 2 wird Spieler 1 usw. (Meldung „… ist jetzt Spieler 1“). Die
  gemerkten Plätze bleiben dabei unverändert. Beim Verbinden bekommt ein Controller seinen gemerkten Platz nur, wenn
  er frei ist **und** keine Lücke entsteht (`FreeIndex`: Platz ≤ Anzahl Spieler), sonst den ersten freien.
- **Name** (max. 40 Zeichen, leer = Standardname): erscheint in Karte, Infobereich-Menü und Tooltip. Paar: Namen
  beider Joy-Con, bei nur einem Namen „Name (Joy-Con-Paar)“.

---

## 5. Belegung (Tasten)

### 5.1 Standard
- **Xbox-Belegung** (nach Position): untere Taste = A usw. **Switch-Belegung** (nach Beschriftung): A bleibt A.
- Standardziele: L/R → LB/RB, ZL/ZR → LT/RT, − → Back, + → Start, HOME → Guide, Aufnahme → Touchpad-Klick
  (nur DualShock 4), C/GL/GR/SL/SR/Headset → nichts.
- GameCube: immer nach Position; Z (ZR) = RB, ZL = LB, L/R digital = nichts (analog über Trigger), C = Back.
- N64/Mega Drive: Tasten sind schon nach Position angeordnet (siehe Protokolle).

### 5.2 Aktionen je Taste (`ButtonAction`, als Text gespeichert)
| Text | Wirkung |
|---|---|
| `A`, `LB`, `LT`, `Back`, `Up`, … `Touchpad` | Taste des virtuellen Gamepads |
| `None` | Taste aus |
| `Key:F5`, `Key:Ctrl+Shift+S` | Tastatur-Hotkey (gehalten, solange die Taste gehalten wird) |
| `MouseLeft` / `MouseRight` / `MouseMiddle` | Maustaste |
| `GyroMouse` / `GyroMouseToggle` | Gyro bewegt die Maus (halten / ein-aus) |
| `GyroStick` / `GyroStickToggle` | Gyro steuert den rechten Stick (halten / ein-aus) |
| `Shift` | Shift-Ebene, solange gehalten: andere Tasten bekommen ihre zweite Belegung |
| `Turbo:A`, `Turbo:Key:Space` | Dauerfeuer im Takt `TurboRate` (Standard 12 Wechsel/s, beginnt mit „an“) |
| `Macro:A 80, Pause 40, A 80` | Makro: bei jedem neuen Drücken einmal abgespielt |

- **Makro-Format**: Schritte durch Komma; je Schritt Tasten (mit `+` gleichzeitig) oder `Key:…` oder `Pause`/`Warten`,
  dann Dauer in ms (Standard 60, max. 5000), max. 64 Schritte.
- **Taste per Tastendruck belegen**: ⌨ neben der Taste klicken, Taste auf der Tastatur drücken.
- **Vorrang**: Shift-Ebene (wenn aktiv) → Profil (benannt oder Standard) je Controller-Art → alte freie Umbelegung
  (`Remap`, nicht beim GameCube) → Standard.
- Belegungen gelten **je Controller-Art**. Ein hochkant gehaltener einzelner Joy-Con nutzt die Belegung
  „Joy-Con-Paar“ (Tasten unter echten Namen), ein quer gehaltener seine eigene (gedrehte Namen).

### 5.3 Profile
- Benannte Profile mit Programmliste (EXE-Namen): automatisch aktiv, solange eines dieser Programme im Vordergrund ist
  (Prüfung jede Sekunde). Fest wählbar im Infobereich („Automatisch“, „Standard“, Profilname).
- Export/Import als `*.ncprofile.json` (ungültige Einträge werden beim Import verworfen).

---

## 6. Sticks, Trigger, Vibration (Feineinstellung)

- **Totzone** radial 0–0,5 (Standard 0,06), weicher Übergang, auf den Kreis begrenzt; je Controller-Art einstellbar.
- **Kennlinie** (Exponent 0,3–3; 1 = linear, > 1 feiner in der Mitte).
- **Analoge Trigger** (GameCube): Totzone 0–0,5 (Standard 0,05), „voll ab“ 0,5–1.
- **Vibration**: an/aus, Stärke 0–1 (Standard 0,8), je Controller-Art. Switch 2: großer Motor → tiefes Band
  (0x112), kleiner → hohes Band (0x187), Amplitude max. 453/1023 (wie SDL, schont die Motoren).
- **Stick-Kalibrierung** (geführt, je Controller und Stick): 1. Stick loslassen, bis 1,5 s ruhig (Streuung ≤ 60
  Rohschritte); 2. am Rand kreisen, bis 24 Richtungen (≥ 700 Rohschritte von der Mitte) erfasst sind. Ergebnis:
  Mitte und Ausschlag je Richtung × 0,95 (Rand sicher erreichbar), mindestens 400 je Richtung. Anzeige der
  Abweichung vom Kreis vorher/nachher. **Nur in N-Connect gespeichert**, der Controller bleibt unverändert;
  „Werkswerte“ entfernt sie. (Nicht für Wii-Sticks.)

## 7. Bewegungssteuerung (Gyro)

- **Gyro kalibrieren**: Controller 2 s ruhig liegen lassen → Nullpunkt je Controller gespeichert.
- **Gyro als rechter Stick** (`GyroStick`): Aus (nur per Taste) / Immer / Beim Zielen (LT > 40/255). Gieren = X,
  Nicken = Y; unter 1,5 °/s nichts (Rauschen); ab da Mindestausschlag `AntiDeadzone` (Standard 12 %), voller
  Ausschlag bei `FullSpeed` °/s (Standard 150); Y umkehrbar; wird zum echten Stick addiert.
- **Gyro-Assistent**: Nullpunkt, Modus, Empfindlichkeit mit Live-Vorschau (Fadenkreuz).
- **Gyro-Maus**: Bildpunkte je Grad (Standard 20), per Taste gehalten oder umgeschaltet.
- **Joy-Con-Paar**: Gyro vom rechten (Standard) oder linken Joy-Con.
- **DualShock 4**: Bewegungsdaten gehen an Spiele/Steam.
- **Cemuhook/DSU** (Standard an): UDP 127.0.0.1:26760 für Emulatoren (Cemu, Dolphin, Yuzu-Nachfolger …).

## 8. Joy-Con-Besonderheiten

- **Paar automatisch**: Linker + rechter Joy-Con werden ein Spieler (abschaltbar), außer einer wurde zuletzt einzeln
  verwendet (gemerkt je Joy-Con).
- **Trennen**: quer gehalten (Schwerkraft überwiegend entlang der Schienenachse, |X| > 2500/4096 g und deutlich
  größer als Y/Z) **und SL oder SR drücken** → sofort eigener Spieler; oder Knopf. „SL + SR 1 s halten“ trennt bewusst **nicht**
  mehr (löste beim Anstecken/Halten im Paar versehentlich aus).
- **Zusammenfügen**: L am linken und R am rechten einzelnen Joy-Con innerhalb von 1 s; oder Knopf.
- **Einzeln quer** (Standard): Stick und Bewegungsdaten gedreht, Tasten nach Lage benannt:

  | Joy-Con L quer | → | Joy-Con R quer | → |
  |---|---|---|---|
  | ◀ / ▼ / ▲ / ▶ | B / A / Y / X (Pro-Namen nach Lage) | A / X / B / Y | B / A / Y / X |
  | SL / SR | L / R | SL / SR | L / R |
  | L / ZL | ZL / ZR | R / ZR | ZL / ZR |
  | − / Aufnahme | + / HOME | + / HOME | + / HOME |
  | Stick-Klick | linker Stick-Klick | Stick-Klick | linker Stick-Klick |

- **Einzeln hochkant** (umschaltbar je Joy-Con): ungedreht, Belegung des Joy-Con-Paars.
- **Joy-Con 2 als Maus** (Standard an): steht er auf der Schienenkante (Sensor unten, Schwerkraft entlang X), ist er
  eine Maus: R/L = Linksklick, ZR/ZL = Rechtsklick, Stick-Klick = Mittelklick, Stick hoch/runter = Scrollen; Bewegung
  geglättet über die Berichtsabstände verteilt; deutlich gekippt (> ~50°) → sofort wieder Controller.
- **Charging Grip (Joy-Con 2)**: erkannt per Befehl (alle ~5 s geprüft), dann GL/GR des Griffs eingeschaltet.

## 9. Wii-Besonderheiten

- **Haltung**: Ohne Nunchuk/Classic quer (Steuerkreuz links): ▲ → links, ▼ → rechts, ◀ → unten, ▶ → oben;
  1 = Y-Position, 2 = B-Position, B (Abzug) = ZR. Mit Nunchuk senkrecht: C = L, Z = ZL, Stick = linker Stick.
- **Classic Controller** (auch Pro): volle Belegung wie Pro Controller.
- **MotionPlus** (Aufsatz oder „Inside“): automatisch eingeschaltet, auch mit Nunchuk/Classic dahinter (Daten
  wechseln sich ab); Nullpunkt wird laufend nachgeführt, wenn die Fernbedienung ~0,5 s ruhig liegt (≤ 3 °/s).
- **Zeiger steuert die Maus** (IR-Kamera, Sensorleiste nötig): Mitte der zwei äußersten Lichtpunkte, gespiegelt.
- **Senden nur bei Änderung** (spart Funkzeit); verbunden bleibt sie über Statusabfragen (nach 3 s Stille Abfrage,
  ohne Antwort binnen 2,5 s = getrennt). Akku alle 30 s abgefragt.
- **Erweiterung erkennen**: Lesen mit 1,5 s Wartezeit, 3 Versuche, Antworten über die Adresse zugeordnet; keine
  Antwort ≠ keine Erweiterung (neuer Versuch nach 2 s, höchstens 5-mal).

## 10. Extras (Switch 1)

- **amiibo lesen** (Pro Controller, rechter Joy-Con): NTAG215, 540 Byte → Datei `.bin`.
- **Ring-Con** (rechter Joy-Con): Zusammendrücken = rechter Trigger, Auseinanderziehen = linker Trigger (analog);
  Ruhelage beim Einschalten gemessen (nicht berühren).
- **IR-Kamera** (rechter Joy-Con): Live-Bild 40 × 30 … 320 × 240 Graustufen.

---

## 11. Akku

- **Anzeige** auf der Karte (mit Spannung bei Switch 2, „⚡ lädt“), im Infobereich-Symbol (niedrigster Stand aller
  Controller, 5-%-Stufen) und im Tooltip/Menü.
- **Warnung** einmal bei 15 % und einmal bei 5 %; wieder scharf ab 25 % oder beim Laden.
- **Switch 2** meldet nur die Spannung (`BatteryEstimator`):
  - Kennlinie Pro/GameCube 3,30 V = 0 % … 4,15 V = 100 % (Stützpunkte 3600/10, 3700/25, 3800/45, 3900/65, 4000/82);
    Joy-Con 2 3,05 V … 3,36 V (geschätzt). Kaufmännisch gerundet.
  - Glättung (Zeitkonstante 4 s), Toleranz ±3 mV gegen Flackern an Prozentgrenzen.
  - **Laden**: Spannung liegt höher als die Ruhespannung. Der Sprung wird beim Anstecken nach 10 s gemessen und
    je Controller gespeichert (`battery.json`), es gilt der größte gemessene Wert, mindestens 20 mV (Joy-Con 2: 10).
    Beim Laden steigt die Anzeige nur, ohne Kabel sinkt sie nur (deutlich höher nur nach Laden/Akkuwechsel).
  - **Abziehen/Ladepause**: 10 s abwarten (Spannung klingt ab), dann den echten Stand übernehmen.
  - Gemessen am Pro Controller 2: Ruhe 3704 mV ≈ 26 %, beim Laden +20 mV; Lade-Byte `2C/3C/34` = lädt, `20` =
    Kabel steckt/Pause, `00` = kein Kabel. Alle 30 s Spannung und Rohbytes im Protokoll.
- **Switch 1/NSO**: Stufe 0–8 → 0–100 %; **Wii**: 0–200 → 0–100 %; **Wii U Pro**: Stufe 0–4.

## 12. Infobereich (Taskleiste)

- Linksklick: Fenster. Rechtsklick: Menü mit je Spieler einem Untermenü (Vibrieren, Spielerplatz, Trennen),
  „Alle Controller trennen“, Ausgabe (Xbox 360 / DualShock 4), Tastenanordnung, Profil, Vibration, Mit Windows
  starten, Einstellungen, Controller koppeln, Kurzanleitung, Protokoll öffnen, Update, Beenden.
- Symbol zeigt den niedrigsten Akkustand; Tooltip je Spieler „P1 Name 57 %“ (max. 127 Zeichen).
- Einblendungen: Verbunden/Getrennt, Kopplung, Akku, schwache Verbindung, Bluetooth-Probleme, Update.

## 13. Kopplungsdaten (Switch 1)

- **Von der Switch-SD-Karte** (Bluepick_RCM/hekate: `switchroot/joycon_mac.ini` bzw. `.bin`, `switch.cal`): wird
  beim Einstecken erkannt (Kartenleser oder hekate „USB Tools“), nur gelesen; Übernahme fragt nach und legt vorher
  eine Sicherung der Einstellungen an.
- **PC → PC**: Datei `*.ncpair` (JSON; optional mit Passwort: AES-256-GCM, Schlüssel per PBKDF2-SHA256). Schlüssel
  nur auf ausdrücklichen Wunsch. Übernahme ergänzt, löscht nichts.
- N-Connect ändert dabei **weder** die Bluetooth-Adresse des Adapters **noch** Windows-Kopplungen.
- Fenster `PairingDataForm` im Windows-11-Stil (Gruppen „Übernehmen“, „Inhalt“, „Weitergeben und sichern“, Fußleiste
  mit Akzent-Knopf „Übernehmen“); Inhalt wird selbst gezeichnet (`Report`: Zeilen mit „:“ am Ende = Zwischenüberschrift,
  „  •  “ = Aufzählung). Passwort-Dialog (`PasswordDialog`) ebenso, Eingabefelder als `TextField`.

## 14. Programm

- **Autostart** (Standard an beim ersten Start, danach Nutzerentscheidung): HKCU\…\Run mit `--autostart`
  (unsichtbar starten). Zweiter Start zeigt das Fenster der laufenden Instanz.
- **Darstellung**: Dunkel (Standard), Hell, Wie Windows; Akzentfarbe aus der Windows-11-Akzentpalette (dunkel:
  „Light 1“ – „Light 2“ wie in Windows 11 wirkte beim Nutzer blass-pastell; hell: „Dark 1“), Schrift darauf nach
  Kontrast (`Theme.OnAccent`); Mica-Titelleiste ab Windows 11.
- **Logo/Icon**: In der Oberfläche direkt in Zielgröße gezeichnet (`LogoView`), Fenstersymbol aus der eingebetteten
  ICO-Datei mit allen Größen (nicht `ExtractAssociatedIcon` – nur 32 px, verkleinert pixelig). Die ICO-Datei
  (`--render-brand`) enthält kleine Größen als 32-Bit-Bitmap, 256 px als PNG; heller Kachelrand erst ab 48 px.
- **Grafiken**: Pfeile (▲▼◀▶) zeichnet `InputView.Caption` als gleich große Dreiecke, die bei gedrehtem Controller
  (quer gehaltener Joy-Con) mitdrehen; alle anderen Beschriftungen bleiben waagerecht lesbar.
- **Sprache**: Deutsch/Englisch, Standard wie Windows (Übersetzungstabelle + Muster für Texte mit Platzhaltern).
- **Update**: Prüft beim Start (nach 10 s) GitHub-Releases (`v1.2.3`). Mit Installer im Release: Ein-Klick-Update –
  Nachfrage, Download nur von GitHub, Größe und SHA-256 (falls angegeben) geprüft, stiller Installer, Neustart.
- **Protokoll**: `%LOCALAPPDATA%\N-Connect\bridge.log` (ab 1 MB einmal rotiert). Einstellungen:
  `%APPDATA%\N-Connect\settings.json` (bei Änderung von außen neu geladen, 300 ms entprellt).
- **Prüfhilfen**: `--demo`, `--demo-all`, `--demo-retro` (simulierte Controller), `--render <Ordner>`
  (Controller-Grafiken, auch Joy-Con 1/2 quer, hochkant, Paar, Grip: `jc1_links_quer.png` …),
  `--render-ui <Ordner> [--wide] [--en] [--light]` (alle Seiten/Karten/Dialoge, auch `ui_kopplungsdaten_*.png`),
  `--render-brand`, `--dump-ui <Datei> --en` (Texte ohne Übersetzung).

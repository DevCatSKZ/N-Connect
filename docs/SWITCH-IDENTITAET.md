# Switch-Identität: Switch-1-Controller am PC ohne neues Koppeln

Projektdokumentation und Arbeitsprotokoll. Gemeinsame Funktion von **N-Connect** (dieses Repo) und
**Bluepick_RCM** (RCM-Payload, `C:\Coding\Bluepick_RCM\Bluepick_RCM Project`).

> Datenschutz: Diese Datei enthält bewusst **keine echten Adressen oder Schlüssel**. Beispiele nutzen
> Platzhalter (`SW:SW:SW:SW:SW:SW` = Switch, `JL:…` = Joy-Con L, `JR:…` = Joy-Con R).

---

## 0. Aktueller Stand (04.10.2026, ca. 09:30)

**Gewählter Weg:** §5d – *die Switch übernimmt die Identität des PCs.* Der Joy-Con wird ganz normal mit Windows
gekoppelt; Bluepick_RCM überträgt diese Kopplung auf die Switch (Atmosphère-Datei `bluetooth_devices.db` +
MissionControl `host_address`). Ergebnis: Joy-Con funktioniert an PC und Switch ohne neues Koppeln, ohne
zusätzliche Hardware, Switch muss beim PC-Spielen nicht laufen, am PC-Adapter wird nichts verändert.

Der ursprüngliche Plan (§1/§2: PC-Adapter nimmt die Switch-Adresse an) ist für den Adapter des Nutzers
**nicht möglich** (Barrot-Chip, §5 Punkt 8) und wurde verworfen. §5b/§5c (eigener Stick, Switch als Brücke)
bleiben als Alternativen dokumentiert.

| Baustein | Stand |
|---|---|
| Switch-Daten per USB/MTP lesen | ✅ funktioniert (nur lesend getestet) |
| Format BT-Save / `bluetooth_devices.db` | ✅ ermittelt und gegengeprüft (§3.1, §5d) |
| Bluepick 1.0.6 „N-Connect-Modus“ | ✅ gebaut, Einträge offline Byte für Byte gegen HOS geprüft · ❌ **noch nicht auf der Switch getestet** |
| Neue `Bluepick_RCM.bin` auf der Switch | ❌ **noch nicht aufgespielt** – auf der SD liegt weiterhin 1.0.5 (siehe Protokoll „Aufspielen“) |
| Backup der alten Version | ✅ `Bluepick_RCM Project\archive\old_deployments\Switch-Backup_2026-10-04\` |
| N-Connect-Seite (MTP-Erkennung, Anzeige des Modus) | ⏳ noch nicht begonnen |
| Pro Controller | ⏳ offen (Schlüssel nur aus der Windows-Registry, §6) |

**Nächster Schritt:** `output\Bluepick_RCM.bin` (1.0.6) auf die SD kopieren (Explorer: alte Datei löschen, neue
einfügen – oder hekate-UMS), Datei per Rücklesen prüfen, dann Hardwaretest nach §5d „Testablauf“.

**Übergabe an den nächsten Agenten:** [UEBERGABE-SWITCH-IDENTITAET.md](UEBERGABE-SWITCH-IDENTITAET.md).
Testdaten von der Switch des Nutzers (echte Schlüssel, nicht veröffentlichen):
`C:\Coding\Bluepick_RCM\Bluepick_RCM Project\archive\testdaten_switch_2026-10-04\`.

**Geänderte Dateien (noch nicht committet):**
- N-Connect (Git, alles neu, untracked): `docs/SWITCH-IDENTITAET.md`, `src/Switch2Pro.Protocol/SwitchBtSave.cs`,
  `tools/Switch2Pro.BtIdentityProbe/` (`Program.cs`, `BtRadio.cs`, `ChipCommands.cs`, `.csproj`).
- Bluepick_RCM (kein Git): `source/keys/keys.c`, `source/keys/keys.h`, `source/main.c`, `Versions.inc` (1.0.6),
  `AGENTS.md`; Build-Ausgabe `output/Bluepick_RCM.bin` (102 357 B).

---

## 1. Ziel

Ein Joy-Con (oder Pro Controller) bleibt mit der Switch gekoppelt und lässt sich trotzdem am PC verwenden,
**ohne ihn neu zu koppeln**. Zurück an der Switch funktioniert er sofort wieder.

So soll es funktionieren:
1. Controller ganz normal an der Switch koppeln.
2. **Bluepick_RCM** exportiert die Kopplungsdaten (Adresse der Switch, Adressen und Schlüssel der Controller)
   auf die SD-Karte.
3. **N-Connect** liest die Daten von der SD-Karte (Kartenleser, hekate-UMS oder die Switch per USB/MTP).
4. In N-Connect lässt sich **„Switch-Identität verwenden“** ein- und ausschalten. Solange sie aktiv ist,
   tritt der Bluetooth-Adapter des PCs mit der Adresse der Switch auf und kennt die Schlüssel. Der Controller
   verbindet sich auf Tastendruck mit dem PC, weil er ihn für seine Switch hält.
5. Beim Ausschalten bzw. Beenden stellt N-Connect den Adapter und Windows wieder her.

Zielgruppe: **alle N-Connect-Nutzer**, also möglichst viele Bluetooth-Adapter.

## 2. Funktionsweise (technisch)

Ein Switch-1-Controller speichert genau einen Host: die Bluetooth-Adresse der Switch und einen 16-Byte-
Kopplungsschlüssel (BR/EDR-Link-Key). Beim Aufwecken ruft er diese Adresse an (Paging) und meldet sich mit
dem Schlüssel an. Der PC muss deshalb zwei Dinge erfüllen:

| Baustein | Was | Wie unter Windows |
|---|---|---|
| A. Adresse | Adapter antwortet unter der Adresse der Switch | Herstellerbefehl an den Chip über `IOCTL_BTH_HCI_VENDOR_COMMAND` (Adminrechte + `SeLoadDriverPrivilege`). Chipabhängig, siehe §4. |
| B. Schlüssel | Windows kennt den Schlüssel des Controllers | `HKLM\SYSTEM\CurrentControlSet\Services\BTHPORT\Parameters\Keys\<Adapter>\<Controller>` (nur mit SYSTEM-Rechten beschreibbar) plus Geräteeintrag. **Noch ungeprüft**, ob Windows eine Verbindung von einem dort nie normal gekoppelten Gerät annimmt. |
| C. Eingaben | Controller-Protokoll | Vorhanden: `Switch1HidLink` (der Controller erscheint als normales HID-Gerät). |

Wichtig im Betrieb: Die Switch muss **aus** sein (nicht im Ruhemodus). Sie hört sonst auf dieselbe Adresse,
und der Controller weckt sie statt mit dem PC zu verbinden.

## 3. Datenquellen (Bluepick_RCM / SD-Karte)

| Datei | Inhalt | Erzeugt von |
|---|---|---|
| `switchroot/joycon_mac.ini` | je Joy-Con: `type`, `mac`, `host`, `ltk` (lesbar) | Bluepick „Dump Joy-Con BT pairing → SD“ / FULL AUTO, hekate |
| `switchroot/joycon_mac.bin` | dasselbe binär (2 × 29 Byte) | dito |
| `switchroot/switch.cal` | `device_bt_mac=` = Adresse der Switch (aus CAL0) | dito |
| `switch/Bluepick_RCM/8000000000000050.bin` | System-Save *BluetoothDevicesSettings*: **alle** gekoppelten Geräte mit Schlüssel (auch Pro Controller) | Bluepick Export / FULL AUTO |

### 3.1 Layout von `8000000000000050.bin` (ermittelt am 04.10.2026)

Das Save ist **innerhalb der Datei nicht verschlüsselt**; verschlüsselt ist nur die NAND-Ebene (BIS), die
Bluepick beim Export bereits entschlüsselt.

- Einträge zu je **0x200 Byte**, Tabelle ab `0x58000` (zweite Kopie ab `0x5C000`, Save-Journal), max. 10 Geräte.
- `+0x70`: Adresse des Controllers, 6 Byte, höchstes Byte zuerst.
- `+0x99`: Kopplungsschlüssel, 16 Byte – **umgekehrte Byte-Reihenfolge** gegenüber `ltk=` in `joycon_mac.ini`
  (das Save speichert ihn wie HCI, niedrigstes Byte zuerst).
- `+0x146`: Gerätename, ASCII, nullterminiert („Joy-Con (L)“, „Joy-Con (R)“, …).
- Die Tabelle enthält auch **ältere Kopplungen** (gleiche Adresse, anderer Schlüssel). Der aktuelle Eintrag
  stand im Test jeweils vorne; die Auswahl muss das berücksichtigen (offen, siehe §6).

Gegenprüfung: Für beide aktuell gekoppelten Joy-Con stimmen Adresse und Schlüssel aus dem Save exakt mit
`joycon_mac.ini` überein (Schlüssel umgedreht). Umgesetzt in `src/Switch2Pro.Protocol/SwitchBtSave.cs`.

## 4. Bluetooth-Adapter: Adresse ändern (Baustein A)

Windows bietet keine allgemeine Funktion zum Ändern der Adapteradresse. Der offizielle Weg für Herstellerbefehle
ist `IOCTL_BTH_HCI_VENDOR_COMMAND` (Code `0x00410050`, Struktur `BTH_VENDOR_SPECIFIC_COMMAND`; Antwort in
`BTH_VENDOR_EVENT_INFO`). Windows leitet den Befehl nur an Adapter mit passender Hersteller-ID weiter.

| Hersteller (SIG-ID) | Befehl zum Setzen der Adresse | Dauer | Status |
|---|---|---|---|
| Broadcom/Cypress (0x000F) | OCF `0x001` Write_BD_ADDR, 6 Byte LE | bis Abstecken | im Werkzeug, ungetestet |
| CSR (0x000A) | OCF `0x000` BCCMD: PSKEY_BDADDR (0x0001) in RAM-Store (0x0008), dann Warmstart (0x4002) | bis Abstecken | im Werkzeug, ungetestet |
| Intel (0x0002) | OCF `0x031` | bis Neustart | geplant |
| MediaTek (0x0046) | OCF `0x01A` | bis Neustart | geplant |
| Qualcomm (0x001D) | OCF `0x00B` / `0x014` (je nach Generation) | – | geplant |
| Marvell (0x0048) | OCF `0x022` | – | geplant |
| Realtek (0x005D) | kein Laufzeitbefehl, nur über Konfigurationsdatei | – | offen |
| **Barrot (0x08E7)** | CSR-BCCMD wird beantwortet, aber PS-Zugriff gesperrt (PERMISSION_DENIED) | – | CSR-Weg geschlossen (Lesen); Schreiben ungetestet; siehe §5 |

Quellen der Befehle: Linux-Treiber (`btbcm.c`, `btintel.c`, `btmtk.c`, `btqca.c`, `btmrvl`) und BlueZ `bccmd`.
Ob Windows die Adresse nach dem Befehl übernimmt (der Stack liest sie beim Start), muss je Chip getestet werden.

## 5. Arbeitsprotokoll

### 04.10.2026 – Bestandsaufnahme und erster Machbarkeitstest

**Umgebung:**

| Gerät | Wert |
|---|---|
| PC-Adapter | „BARROT Bluetooth 5.4 Adapter“, USB `33FA:0010`, Hersteller-ID `0x08E7`, Treiber Barrot 17.55.6.566 (Filtertreiber `brbtusb_54.sys` unter dem Windows-Stack) |
| Switch | Switch V1 per USB, unter Horizon als MTP-Gerät „Nintendo Switch“ (`057E:201D`, DBI-Art); Speicher „microSD card“ |

**Schritte und Ergebnisse:**
1. **Bestehenden Code geprüft.** N-Connect liest `joycon_mac.ini/.bin` und `switch.cal` bereits
   (`SwitchPairingData.cs`, `PairingDataForm.cs`), erkennt Switch-SD-Karten nur als **Laufwerk**
   (`SwitchCardWatcher.cs`) und nutzt die Schlüssel bewusst nicht.
2. **Switch per MTP gelesen** (Windows-Shell, nur lesend): `switchroot/joycon_mac.*`, `switchroot/switch.cal`,
   `switch/Bluepick_RCM/8000000000000050.bin`, `…053.bin` kopiert. → **MTP-Zugriff funktioniert.**
   N-Connect erkennt MTP-Geräte noch nicht (kein Laufwerksbuchstabe).
3. **Kopplungsdaten ausgewertet:** Konsolenadresse in `switch.cal` = Host-Adresse beider Joy-Con. ✓
4. **BT-Save-Layout ermittelt** (§3.1) und gegen `joycon_mac.ini` geprüft. ✓
5. **Barrot-Treiber untersucht:** liest den Registry-Wert `BRExtPatchPath` (externer Firmware-Patch) –
   möglicher, undokumentierter Ansatz. Ein Linux-Projekt (zhuzhuzihan/brtusb) beschreibt eine Barrot-
   Produktvariante als „CSR-BlueCore-kompatibel“ → BCCMD-Weg prüfen.
6. **Prüfwerkzeug gebaut:** `tools/Switch2Pro.BtIdentityProbe` (siehe §7). `info` und `sd` laufen.
   Die LMP-Version liefert `IOCTL_BTH_GET_LOCAL_INFO` ohne Adminrechte nicht (bzw. andere Struktur-Lage) – für
   Herstellerbefehle unkritisch, dort wird `LmpVersion = 0` (alle Versionen) gesendet.

7. **Herstellerbefehle unter Windows (als Admin, nur Leseanfragen):**
   - Erster Versuch mit dem Handle aus `BluetoothFindFirstRadio`: **Fehler 1314** (`ERROR_PRIVILEGE_NOT_HELD`),
     obwohl `SeLoadDriverPrivilege` aktiv war. **Lösung:** Adapter über die Geräteschnittstelle
     `GUID_BTHPORT_DEVICE_INTERFACE` mit `GENERIC_READ | GENERIC_WRITE` öffnen → IOCTL wird angenommen.
   - **Abweichung von der Microsoft-Doku:** Windows liefert im Ausgabepuffer das **rohe HCI-Event**
     (Code, Länge, Parameter), nicht `BTH_VENDOR_EVENT_INFO`.
   - **Muster (`BTH_VENDOR_PATTERN`)**: Offset bezieht sich auf die Event-Parameter. Für CSR-BCCMD
     funktioniert `{Offset 0, Größe 1, 0xC2}`; ohne Muster bzw. mit Offset 2 kommt keine Antwort.
8. **Ergebnis Barrot (`0x08E7`):** antwortet auf **CSR-BCCMD** (OCF `0x000`, Kanal `0xC2`):

   | Anfrage | Ergebnis |
   |---|---|
   | Build-ID (Variable `0x2819`) | OK, **8891** (`0x22BB`) = typische Kennung eines CSR8510 A10 |
   | Chip-Version (`0x281A`) | OK, `0x0007` |
   | Chip-Revision (`0x281B`) | PERMISSION_DENIED |
   | PS-Schlüssel Gerätename (`0x0108`), Quarzfrequenz (`0x01FE`) | PERMISSION_DENIED |
   | PSKEY_BDADDR in allen Speichern (Standard, RAM, PSI, PSF, ROM) | PERMISSION_DENIED |

   **Deutung:** Der Barrot-Chip ahmt nur einzelne CSR-Kennungen nach (damit Treiber ihn als CSR8510 behandeln),
   der CSR-Konfigurationsspeicher (Persistent Store) ist komplett gesperrt. Lesen der Adresse über CSR geht nicht.
9. **Schreibtest vorbereitet, nicht ausgeführt:** `csr-setzen <eigene Adresse> --ja` schreibt die *unveränderte*
   eigene Adresse in den RAM-Speicher (ohne Warmstart), um zu prüfen, ob auch Schreiben gesperrt ist. Die
   UAC-Abfrage wurde abgelehnt; der Befehl wurde nicht gesendet.

10. **Vorfall 08:40 – Bluetooth am PC ausgefallen.** Beim zweiten `csr-lesen`-Lauf wurden zusätzlich Varianten
    ohne Muster bzw. mit Muster-Offset 2 gesendet. Der Chip antwortet auf BCCMD mit einem Herstellerevent statt
    mit *Command Complete*; ohne passendes Muster wartet der Windows-Stack vergeblich. Systemprotokoll:
    `BTHUSB 3` „Ein Befehl, der an den Adapter gesendet wurde, hat das Zeitlimit überschritten“ (08:40:14).
    Danach war Bluetooth unbenutzbar, bis der Adapter neu angemeldet wurde (08:45:05, nach Umstecken).
    Adresse und Chip blieben unverändert. **Konsequenz:** Herstellerbefehle nur noch mit bekannt passendem
    Antwortmuster senden; Varianten „ausprobieren“ ist verboten. Werkzeug entsprechend angepasst (nur Muster @0,
    keine Mehrfachversuche). Abhilfe bei Ausfall: Adapter ab- und wieder anstecken.

**Sicherheitsregel für alle weiteren Tests:** Ein Warmstart mit fremder Adresse trennt **alle** anderen
Bluetooth-Geräte des PCs (hier u. a. Tastatur und Maus) bis zum Abstecken des Adapters. Deshalb verlangt
`csr-setzen` dafür ausdrücklich `--neustart`, und vorher muss eine kabelgebundene Eingabe bereitstehen.

## 5a. Warum keine reine Software-Übersetzung der Adresse („Emulator im Programm“)

Wunsch (04.10.2026): N-Connect soll die Adresse während der Laufzeit nur in Software übersetzen, ohne den
Adapter zu verändern. Das ist mit keinem Standard-Bluetooth-Adapter möglich:

1. **Der Anruf erreicht Windows nie.** Der Joy-Con ruft beim Aufwecken gezielt die Adresse der Switch an
   (Paging). Der Funkcode dieses Anrufs wird aus der Zieladresse berechnet. Ob ein Adapter antwortet, entscheidet
   der **Bluetooth-Chip selbst** (Baseband), bevor irgendetwas beim PC ankommt. Ein Chip mit anderer Adresse
   hört den Anruf schlicht nicht – es gibt nichts, was Software übersetzen könnte.
2. **Die Anmeldung prüft die Adresse mit.** Auch wenn der PC die Verbindung selbst aufbaut, fließt die Adresse
   in die Schlüsselprüfung ein (Legacy: E1 mit BD_ADDR; Secure Simple Pairing: f2/h-Funktionen mit beiden
   Adressen). Mit falscher Adresse schlägt die Anmeldung fehl, obwohl der Schlüssel stimmt.

Folge: Die Adresse muss **im Funkchip** stimmen. Möglich ist das nur auf Chips, deren Adresse sich setzen lässt
(§4) – oder mit einem eigenen, programmierbaren Funkmodul (§5b).

## 5b. Option „Switch-Identitäts-Adapter“ (Hardware-Emulator)

Ein kleines programmierbares Bluetooth-Modul übernimmt die Rolle der Switch und reicht die Eingaben per USB an
N-Connect weiter. Unabhängig vom Bluetooth-Adapter des PCs, damit für alle Nutzer gleich.

- **ESP32 (klassisch, nicht S2/S3/C3)**: kann Bluetooth Classic (BR/EDR) und eine frei wählbare Adresse
  (`esp_base_mac_addr_set`). Kosten ca. 5–10 €.
- Alternative **Raspberry Pi Pico W** (CYW43439, BTstack; Adresse über Broadcom-Herstellerbefehl).
- Ablauf: N-Connect überträgt Konsolenadresse und Schlüssel per USB an das Modul → Modul startet mit der
  Switch-Adresse → Joy-Con verbindet sich → Modul leitet HID-Berichte per USB an N-Connect → bestehende
  Joy-Con-Auswertung (`Switch1.cs`). Ausschalten = Modul trennen/zurücksetzen.
- Vorteile: kein Eingriff in Windows (keine Registry, kein Adminrecht, PC-Adapter bleibt unverändert, Tastatur
  und Maus bleiben verbunden).
- Aufwand: Firmware für das Modul (HID-Host mit gespeichertem Schlüssel) + USB-Anbindung in N-Connect.

## 5c. Lösungswege im Vergleich (Stand 04.10.2026, nach dem Barrot-Ergebnis)

| Weg | Hardware | Für alle Nutzer? | Eingriff in Windows | Bewertung |
|---|---|---|---|---|
| PC-Adapter umadressieren (§4) | vorhandener Adapter | nur bestimmte Chips | Adminrechte, Registry, Neustart des Adapters; trennt andere BT-Geräte | riskant, Barrot nicht möglich |
| **Eigener N-Connect-Stick** (Pico W / ESP32) | ca. 7–10 € | ja, immer gleich | keiner | **empfohlen** |
| Switch als Brücke (Sysmodule) | keine | nur mit CFW | keiner | Switch muss laufen; siehe unten |

**Eigener Stick, bevorzugt Raspberry Pi Pico W / Pico 2 W:**
- Funkchip CYW43439 (Broadcom-Familie): Bluetooth Classic, Adresse per Herstellerbefehl setzbar (in BTstack
  vorgesehen, auf dem Pico W noch zu bestätigen).
- Eigener USB-Anschluss (TinyUSB): Der Stick meldet sich als eigenes Gerät bei N-Connect; optional zusätzlich als
  normaler Gamepad, damit er auch ohne N-Connect funktioniert.
- Firmware-Update per Drag-and-drop (UF2-Datei) – auch für Nutzer ohne Entwicklungswerkzeuge; N-Connect kann die
  Datei mitliefern.
- Firmware auf Basis von **BTstack** (im Pico-SDK enthalten): HID-Host für Bluetooth Classic, eigene
  Schlüsselablage – N-Connect überträgt Konsolenadresse und Schlüssel, der Stick speichert sie.
- Klassischer ESP32 (nicht S2/S3/C3) als Alternative; Flashen dort aber umständlicher (esptool).

**Switch als Brücke (ohne zusätzliche Hardware):** Die Controller bleiben ganz normal mit der eingeschalteten
Switch verbunden. Ein Homebrew-Programm auf der Switch liest die Eingaben und schickt sie an N-Connect, das daraus
wie bisher einen virtuellen Xbox-/DS4-Controller macht. Keine Adresse, keine Schlüssel, kein Eingriff am PC.

- Stufe 1 – **App „N-Connect Bridge“ (NRO)**, im Vordergrund im hbmenu gestartet: liest alle Controller
  (libnx `padUpdate`, inkl. Bewegungssensoren), sendet per WLAN/LAN (UDP) an den PC; Bildschirm zeigt Status.
  Im Vordergrund gibt es keinen Konflikt mit Spielen.
- Stufe 2 – optional **USB statt WLAN** (libnx `usb:ds`, geringere Verzögerung; nicht gleichzeitig mit
  MTP-Programmen wie DBI).
- Stufe 3 – optional **Sysmodule** im Hintergrund (Eingaben lesen ist auch neben Spielen möglich, wie
  Overlay-Lader zeigen); dann sehen allerdings Spiel und PC dieselben Eingaben.
- Rückkanal: Vibration vom PC zur Switch und weiter an den Joy-Con ist über libnx möglich.
- PC-Seite: neue Verbindungsart in N-Connect (z. B. `SwitchBridgeLink` neben `Switch1HidLink`), Suche der
  Switch im Netzwerk, Weitergabe an die vorhandene Ausgabe (ViGEm, DSU).
- Grenzen: Switch muss an sein (nicht im Ruhemodus), CFW nötig, zusätzliche Verzögerung (Joy-Con → Switch → PC,
  per WLAN einige Millisekunden). Funktioniert für alle Controller, die an der Switch hängen – auch Pro Controller,
  ohne Bluepick-Daten.

## 5d. Umgekehrter Weg: Die Switch übernimmt die Identität des PCs (ohne Hardware, Switch darf aus sein)

Idee (04.10.2026): Nicht der PC-Adapter wird umadressiert (geht bei Barrot nicht), sondern die **Switch**. Ihr
Funkchip lässt sich unter Atmosphère steuern, und dafür gibt es ein fertiges Modul.

- **MissionControl** (ndeadly, Atmosphère-Sysmodule) hat in `/config/MissionControl/missioncontrol.ini`,
  Abschnitt `[bluetooth]`, die Option **`host_address`** („Override the bluetooth host adapter address“).
  Laut README gedacht für: *„in conjunction with a link key … use your controller across multiple devices
  without having to re-pair every time you switch“*. Wird nur beim Start gelesen. Hinweis im README: Eine
  Änderung macht bestehende Kopplungen anderer Controller ungültig (einmal neu koppeln).
- **Atmosphère** `system_settings.ini`: `[atmosphere] enable_external_bluetooth_db = u8!0x1` legt die
  Kopplungsdatenbank als Datei auf die SD-Karte: `/atmosphere/bluetooth_devices.db`, gemeinsam für sysMMC und
  alle emuMMCs (ab FW 13: 20 Einträge). → Schlüssel lassen sich **per Datei** eintragen, ohne Save-Signatur.

**Ablauf:**
1. Joy-Con (oder Pro Controller) **ganz normal mit Windows koppeln** (einmalig, SYNC-Taste). Jeder Bluetooth-
   Adapter geht, keine Herstellerbefehle.
2. N-Connect liest Adresse des PC-Adapters und den Kopplungsschlüssel aus Windows
   (`BTHPORT\Parameters\Keys`, nur mit SYSTEM-Rechten lesbar → einmalig mit Adminbestätigung, nur lesend).
3. N-Connect schreibt auf die Switch-SD-Karte (Kartenleser, hekate-UMS oder MTP): `host_address` für
   MissionControl, `enable_external_bluetooth_db` für Atmosphère und den Controller-Eintrag mit Schlüssel in
   `bluetooth_devices.db`.
4. Switch (CFW) neu starten → Sie meldet sich mit der Adresse des PCs, kennt den Schlüssel, der Controller
   verbindet sich auch dort – **ohne neues Koppeln, in beide Richtungen**.

**Vorteile:** keine zusätzliche Hardware; Switch muss beim PC-Spielen nicht laufen; am PC nur eine normale
Windows-Kopplung (kein Eingriff in den Adapter, Tastatur/Maus unberührt); funktioniert mit jedem PC-Adapter.

**Festgelegt mit dem Nutzer (04.10.2026): Bluepick übernimmt das Schreiben auf der Switch-Seite.**

Gefundene Fakten:
- Auf der SD-Karte des Nutzers ist MissionControl installiert (`atmosphere/contents/010000000000BD00`,
  `config/MissionControl/`); `host_address` ist dort vorhanden, aber auskommentiert.
  `enable_external_bluetooth_db` ist nicht gesetzt, `bluetooth_devices.db` existiert nicht.
- Atmosphère-Quelltext (`stratosphere/ams_mitm/source/set_mitm/setsys_mitm_service.cpp`, PR #1787):
  `bluetooth_devices.db` = **`u64` Anzahl + Anzahl × `settings::BluetoothDevicesSettings`**. Liegt die Datei
  vor, liest HOS die Kopplungen daraus (`GetBluetoothDevicesSettings`); jede Änderung schreibt Atmosphère zurück.
- Die Einträge im Save `8000000000000050` haben **dasselbe Format** (libnx `SetSysBluetoothDevicesSettings`,
  0x200 Byte). Korrektur zu §3.1: Ein Eintrag beginnt bei `0x58070` (nicht `0x58000`); relativ dazu:
  Adresse `+0x00`, Klasse `+0x26`, **Schlüssel `+0x29`**, VID/PID `+0x40/+0x42`, Name (`name2`) `+0xD6`.
  Die Offsets 0x70/0x99/0x146 in §3.1 sind dieselben Felder, gemessen ab `0x58000`.
- **Schlüsselquelle ohne Registry:** Ist ein Joy-Con mit dem PC gekoppelt, stehen im Joy-Con-Speicher
  (SPI 0x2000) die **PC-Adresse als Host** und der **PC-Schlüssel**. Bluepicks Joy-Con-Dump liest genau das
  (an den Schienen, im RCM – dabei koppelt HOS nichts neu). Für Pro Controller bleibt nur die Windows-Registry.

Ablauf mit Bluepick („N-Connect-Modus“):
1. Joy-Con in Windows koppeln (SYNC).
2. Joy-Con an die Switch stecken, Bluepick starten → „N-Connect: PC-Kopplung übernehmen“:
   - Joy-Con-Speicher lesen → Host = PC-Adresse, Schlüssel.
   - `/atmosphere/bluetooth_devices.db` schreiben: vorhandene Einträge aus dem Save übernehmen, Einträge der
     Joy-Con mit dem PC-Schlüssel ersetzen bzw. ergänzen (Vorlage: vorhandener Joy-Con-Eintrag gleicher Seite).
   - `config/MissionControl/missioncontrol.ini`: `host_address = <PC-Adresse>`.
   - `atmosphere/config/system_settings.ini`: `enable_external_bluetooth_db = u8!0x1`.
3. Joy-Con abziehen (zu prüfen, ob nötig), CFW starten → Joy-Con verbindet sich drahtlos mit der Switch.

**Konflikt mit bestehender Bluepick-Logik:** FULL AUTO und „Atmosphere fixes“ kommentieren
`enable_external_bluetooth_db` aus und löschen `bluetooth_devices.db`. Im N-Connect-Modus würde das die
Einrichtung zerstören → FULL AUTO muss den Modus erkennen und auslassen.

**Grenzen / zu prüfen:**
- Nur unter CFW (MissionControl). Bei einem Start ohne CFW (OFW/„stock“) hat die Switch ihre echte Adresse.
- Andere an der Switch gekoppelte Controller einmal neu koppeln (Adresse der Switch ändert sich).
- PC und Switch haben dieselbe Adresse: Nur eines von beiden darf gerade auf den Controller warten (Switch aus
  oder im Flugmodus, wenn am PC gespielt wird; sonst gewinnt, wer zuerst antwortet).
- **Joy-Con an die Schienen der Switch stecken** löst dort evtl. ein neues Koppeln aus (neuer Schlüssel) → der
  Schlüssel in Windows wäre dann veraltet. Muss getestet werden; ggf. Rückweg (Schlüssel von der Switch nach
  Windows) nötig.
- Format von `bluetooth_devices.db` noch aus dem Atmosphère-Quelltext zu bestätigen.
- Einträge der Switch-Struktur (§3.1) und Windows-Schlüsselformat (Byte-Reihenfolge) abgleichen.

### 04.10.2026 – Umsetzung in Bluepick_RCM 1.0.6 („N-Connect-Modus“)

Warum über Bluepick und die externe Datenbank: Bluepick kopiert System-Saves nur **als Ganzes**
(Export/Import/Sync). Das bleibt gültig, weil Signatur (CMAC) und Prüfsummen (IVFC) mitkopiert werden. Einen
einzelnen Eintrag im Save zu ändern, würde beides zerstören. Deshalb schreibt Bluepick die PC-Kopplung nicht ins
Save, sondern in Atmosphères **`bluetooth_devices.db`** (unsignierte Datei, gleiches Eintragsformat).

Neue Menüpunkte (Abschnitt „-- N-Connect (PC) --“):
- **„N-Connect: use PC pairing on Switch“** (`bt_nconnect_enable`):
  1. Joy-Con-Dump von den Schienen (vorhandene Routine). Ein Joy-Con gilt als „mit dem PC gekoppelt“, wenn
     Adresse, Host und Schlüssel gesetzt sind und der Host **nicht** die Konsolenadresse aus CAL0 ist.
  2. `sd:/atmosphere/bluetooth_devices.db` = `u64` Anzahl + je Joy-Con ein 0x200-Eintrag. Eine vorhandene
     Datei wird einmalig nach `bluetooth_devices.db.nconnect.bak` verschoben.
  3. `sd:/config/MissionControl/missioncontrol.ini` (bei Bedarf aus `.template` angelegt): `[bluetooth]`
     `host_address=<PC-Adresse>`.
  4. `sd:/atmosphere/config/system_settings.ini`: `[atmosphere] enable_external_bluetooth_db = u8!0x1`.
  5. Status für N-Connect: `sd:/switch/Bluepick_RCM/nconnect.ini` (Adressen, **keine** Schlüssel).
  Voraussetzung: MissionControl installiert (`atmosphere/contents/010000000000BD00/exefs.nsp`).
- **„N-Connect: restore Switch identity“** (`bt_nconnect_disable`): `host_address` auskommentieren, externe
  Datenbank abschalten, `bluetooth_devices.db` nach `.nconnect.bak` verschieben.
- **Schutz:** FULL AUTO, „Disable external BT db override“ und „Delete bluetooth_devices.db“ erkennen den
  aktiven N-Connect-Modus und lassen die externe Datenbank in Ruhe. Die Statusanzeige zeigt den Modus an.

Datenbankeintrag je Joy-Con (Werte wie HOS sie selbst speichert):
`+0x00` Adresse · `+0x26` Klasse `00 25 08` · `+0x29` Schlüssel (umgekehrt zu `joycon_mac.bin`) ·
`+0x39` `01` · `+0x3C` `00 00 10 00` · `+0x44` `08` · `+0x45` `FF` · `+0xC8` `04` · `+0xD6` „Joy-Con (L/R)“ ·
`+0xEA` `68`.

**Offline-Prüfung:** Aus dem aktuellen `joycon_mac.bin` (Joy-Con noch mit der Switch gekoppelt) wurden die
Einträge mit derselben Logik erzeugt und mit den HOS-Einträgen im Save verglichen: zunächst 1 Byte Unterschied
(`+0xEA`: HOS `0x68`), danach ergänzt → **identisch**.

Build: Payload 102 357 B (max. 126 296 B), unkomprimiert 125 769 B (max. 140 288 B). Noch **nicht auf der Switch
getestet**.

**Testablauf (Hardware):**
1. Neue `Bluepick_RCM.bin` auf die SD-Karte (`switch/Bluepick_RCM/`).
2. Joy-Con L und R in Windows koppeln (Einstellungen → Bluetooth → Gerät hinzufügen, SYNC halten).
3. Switch → hekate → Bluepick, Joy-Con an die Schienen → „N-Connect: use PC pairing on Switch“.
4. Joy-Con abziehen, CFW (emuMMC) starten. **PC-Bluetooth während des Tests aus** (sonst nimmt der PC die
   Verbindung an – gleiche Adresse).
5. Taste am Joy-Con → verbindet er sich mit der Switch?
6. Switch aus, PC-Bluetooth an, Taste am Joy-Con → verbindet er sich wieder mit dem PC?
7. Zusätzlich prüfen: Was passiert, wenn der Joy-Con unter HOS an die Schiene gesteckt wird (neue Kopplung?).

Hinweis zu §5d „Ablauf“ Schritt 2/3: Für Joy-Con ist die Windows-Registry nicht nötig – Bluepick liest PC-Adresse
und Schlüssel direkt aus dem Joy-Con. Die Datenbank enthält nur die Joy-Con (frühere Einträge sind nach dem
Adresswechsel ohnehin ungültig), Vorlage sind feste Werte statt eines vorhandenen Eintrags.

### 04.10.2026 – Aufspielen von Bluepick 1.0.6 per MTP (nicht abgeschlossen)

1. **Backup:** Die auf der Switch liegende `switch/Bluepick_RCM/Bluepick_RCM.bin` (96 400 B = 1.0.5) wurde per MTP
   auf den PC gelesen und dauerhaft abgelegt: `Bluepick_RCM Project\archive\old_deployments\Switch-Backup_2026-10-04\`
   (`Bluepick_RCM_switch_aktuell.bin`, `Bluepick_RCM_v1.0.5_vorher.bin` – beide identisch).
2. **Kopieren per Shell-`CopyHere`:** lief ohne Fehler, die Datei auf der Switch blieb aber unverändert (Rücklesen +
   SHA-256-Vergleich: weiterhin 1.0.5). → **MTP überschreibt vorhandene Dateien nicht**; erst löschen, dann kopieren.
3. **Verbindung instabil:** Die Switch verschwand mehrfach aus der MTP-Ansicht und meldete sich zwischendurch in einem
   anderen Modus (Gerät „Nintendo Switch“/Speicher „microSD card“ ↔ Gerät „Switch“/Speicher „1: SD Card“, andere
   Seriennummer im Geräte-Pfad). Der Versuch „löschen + kopieren“ lief ins Leere, weil die Switch schon beim Start
   nicht erreichbar war – **es wurde nichts gelöscht und nichts kopiert**.
4. **Entscheidung:** Keine weiteren automatischen Versuche bei instabiler Verbindung (Gefahr: Datei gelöscht, aber
   nicht neu kopiert). Stattdessen: Nutzer kopiert im Explorer, oder SD per hekate-UMS als Laufwerk; danach
   Prüfung durch Rücklesen.

Für N-Connect relevant (spätere MTP-Erkennung): Gerätenamen und Speichernamen variieren je nach MTP-Programm;
Erkennung muss über VID `057E` / Ordnerstruktur erfolgen, nicht über feste Namen. Überschreiben braucht Löschen.

## 6. Offene Punkte / Risiken

**Für den gewählten Weg (§5d):**
- Hardwaretest steht aus (Testablauf in §5d).
- Rail-Attach unter HOS: Erzeugt das Anstecken eine neue Kopplung (neuer Schlüssel) und macht die PC-Kopplung
  ungültig?
- Joy-Con-Kopplungsdaten merken sich den Host-Typ (Prüfsumme „Host ist PC“ bzw. „Switch“). Ob der Joy-Con sich
  gegenüber einer Switch, die als „PC“ gekoppelt ist, anders verhält, ist ungeprüft.
- Schlüsseltyp `0x04` (wie HOS) – passt das zu einer Windows-Kopplung? (Joy-Con unterstützt kein Secure
  Connections, daher erwartet `0x04`.)
- Start ohne CFW (OFW) nutzt die echte Adresse der Switch → Joy-Con verbindet dort nicht.
- Pro Controller: Schlüssel nur aus der Windows-Registry (`BTHPORT\Parameters\Keys`, SYSTEM-Rechte, nur lesen).
- N-Connect: MTP-Erkennung der Switch, Anzeige von `switch/Bluepick_RCM/nconnect.ini`, Hinweis „Switch aus“.
- MTP: Überschreiben nur nach Löschen; instabile Verbindungen abfangen.

**Nur für die verworfenen bzw. alternativen Wege (§4, §5b, §5c):**
- Barrot: Schreiben in den CSR-Speicher ungetestet (nach dem Lese-Ergebnis vermutlich ebenfalls gesperrt).
- Nimmt Windows eine eingehende HID-Verbindung von einem nur per Registry eingetragenen Gerät an?
- Übernimmt Windows eine geänderte Adapteradresse ohne Neustart des Chips?
- Welcher Eintrag im BT-Save ist der aktuelle, wenn eine Adresse mehrfach vorkommt?

## 7. Werkzeug `Switch2Pro.BtIdentityProbe`

```powershell
dotnet build tools\Switch2Pro.BtIdentityProbe -c Release
$exe = "tools\Switch2Pro.BtIdentityProbe\bin\Release\net8.0-windows\Switch2Pro.BtIdentityProbe.exe"
& $exe info                          # Adapter anzeigen (ändert nichts)
& $exe sd <SD-Stammordner>           # Kopplungsdaten auswerten (Schlüssel maskiert)
# Ab hier: Terminal als Administrator, Herstellerbefehle
& $exe csr-lesen --ja                # CSR-Leseanfrage (ändert nichts, falls CSR)
& $exe csr-setzen <Adresse> --ja     # CSR: Adresse im RAM + Warmstart (verfällt beim Abstecken)
& $exe bcm-setzen <Adresse> --ja     # Broadcom: Write_BD_ADDR (verfällt beim Abstecken)
```

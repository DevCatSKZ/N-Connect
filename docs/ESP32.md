# ESP32 als zweiter „Bluetooth-Stick“ – Controller ohne neues SYNC

Ziel: Deine Switch-2-Controller funktionieren **am PC mit dem Bluetooth-Stick** und **an einem anderen PC mit einem
ESP32-S3**, ohne jedes Mal neu zu koppeln. N-Connect exportiert dafür alles, was der ESP32 braucht
(*Kopplungsdaten → Für ESP32 exportieren*). Diese Seite erklärt das Prinzip, den Export und wie eine
ESP32-Firmware die Controller annimmt.

> Stand 10.10.2026: Der Export in N-Connect ist fertig und getestet. Die ESP32-Seite ist beschrieben, aber noch
> nicht mit echter Hardware erprobt (keine Firmware im Repo).

---

## 1. Prinzip in drei Sätzen

1. Drückst du an einem Switch-2-Controller **SYNC**, koppelt N-Connect ihn mit dem PC und schreibt dabei die
   **Bluetooth-Adresse deines Sticks** in den Controller (Befehl `0x15`, [PROTOKOLLE 1.6](PROTOKOLLE.md)).
2. Danach wirbt der Controller nach jedem Tastendruck nur noch für diese Adresse und nimmt die Verbindung nur von
   ihr an. Ein fremder Host wird abgelehnt (in N-Connect gemessen). Die Verbindung selbst ist **unverschlüsselt**;
   es gibt keinen geheimen Schlüssel.
3. Übernimmt ein ESP32 genau diese Adresse als seine eigene Bluetooth-Adresse, hält der Controller ihn für deinen
   PC und verbindet sich ohne SYNC. Zurück am PC verbindet er sich wieder dort.

## 2. Was geht – und was nicht

| Controller | Mit ESP32-S3 | Warum |
|---|---|---|
| Switch 2 Pro Controller, Joy-Con 2 (L/R), GameCube-Controller (Switch 2) | ✅ | Bluetooth LE, kein Pairing, nur die Host-Adresse zählt |
| Joy-Con 1, Pro Controller 1, NSO-Controller, Wii, DualShock 4, DualSense | ❌ | klassisches Bluetooth (BR/EDR), das der S3 nicht hat. Ein **Ur-ESP32** (ESP32-WROOM-32) könnte es, bräuchte aber die Link-Keys aus der Windows-Registrierung (nur mit SYSTEM-Rechten lesbar) – nicht umgesetzt |
| Xbox-Controller (BLE) | ❌ (vorerst) | verschlüsselte Standardkopplung (LTK/IRK aus Windows nötig) |

Der Export führt alle bekannten Controller auf und markiert, welche übernehmbar sind (`supportedOnEsp32S3`).

## 3. So benutzt du es

1. **Controller einmal mit N-Connect koppeln:** am PC mit dem Stick SYNC drücken (das hast du für deine
   Controller schon getan). Wichtig: Danach **nicht** an einer Konsole oder einem anderen Gerät neu koppeln – sonst
   merkt sich der Controller dessen Adresse.
2. **Exportieren:** N-Connect → *Joy-Con & Wii* → *Kopplungsdaten* → **Für ESP32 exportieren** → ZIP speichern.
   Das Fenster zeigt danach die Adresse und welche Controller übernehmbar sind. Der Export braucht keine
   Administratorrechte und enthält keine Schlüssel.
3. **ESP32 einrichten:** `nconnect_pairing.h` aus dem ZIP in das ESP-IDF-Projekt kopieren und die Firmware bauen
   (Abschnitt 5).
4. **Benutzen:** ESP32 an den anderen PC stecken, am Controller eine Taste drücken → er verbindet sich mit dem ESP32.
   Zurück am ersten PC: Taste drücken → er verbindet sich wieder mit dem Stick.

**Nicht gleichzeitig:** Stick und ESP32 haben dieselbe Adresse. Sind beide in Reichweite und aktiv, verbindet sich
der Controller mit dem, der schneller antwortet. Am ersten PC dann N-Connect beenden oder Bluetooth ausschalten. In
verschiedenen Räumen ist das kein Problem.

**Neuer Controller dazugekommen?** Erst am PC mit SYNC koppeln, dann neu exportieren. Eigentlich braucht der ESP32
nur die Host-Adresse; die Controllertabelle (Namen, Spielerplätze, Kalibrierung) ist eine Zugabe.

## 4. Inhalt des Exports

ZIP `N-Connect-ESP32-<PC>.zip`:

| Datei | Inhalt |
|---|---|
| `nconnect_pairing.h` | C-Header für ESP-IDF: Host-Adresse in beiden Byte-Reihenfolgen, Tabelle der übernehmbaren Controller |
| `nconnect-esp32.json` | alles maschinenlesbar, auch nicht übernehmbare Controller mit Grund |
| `LIESMICH.txt` | Kurzanleitung |

### 4.1 `nconnect-esp32.json` (Format `n-connect-esp32`, Version 1)

```json
{
  "format": "n-connect-esp32",
  "version": 1,
  "created": "2026-10-10T20:15:00Z",
  "sourcePc": "MEIN-PC",
  "hostAddress": "98:B6:E9:01:02:03",
  "controllers": [
    {
      "address": "AA:BB:CC:00:11:22",
      "kind": "Pro2",
      "productId": 8297,
      "name": "Nintendo Switch 2 Pro Controller",
      "supportedOnEsp32S3": true,
      "playerSlot": 0,
      "output": "Xbox360",
      "singleJoyCon": false,
      "uprightJoyCon": false,
      "stickLeft": { "x": { "neutral": 2048, "max": 1400, "min": 1300 }, "y": { "neutral": 2048, "max": 1400, "min": 1300 } },
      "gyroBias": { "x": 1.5, "y": -2, "z": 0.25 }
    },
    { "address": "DD:EE:FF:00:11:22", "kind": "Pro1", "supportedOnEsp32S3": false,
      "note": "Klassisches Bluetooth oder verschlüsselte Kopplung – mit dem ESP32-S3 nicht übernehmbar" }
  ]
}
```

- Adressen immer `AA:BB:CC:DD:EE:FF`, höchstes Byte zuerst (wie Windows sie anzeigt).
- `kind` = Name aus `ControllerKind`; `Unknown`, wenn N-Connect den Controller seit Version 1.0.18 noch nicht
  verbunden gesehen hat (N-Connect merkt sich die Art beim Verbinden in `Settings.ControllerKinds`).
- Felder ohne Wert fehlen. Kalibrierwerte sind 12-Bit-Rohwerte wie in [PROTOKOLLE 0](PROTOKOLLE.md).

### 4.2 `nconnect_pairing.h`

```c
#define NCONNECT_HOST_ADDR_STR "98:B6:E9:01:02:03"
static const uint8_t NCONNECT_HOST_ADDR[6]    = { 0x98, 0xB6, 0xE9, 0x01, 0x02, 0x03 }; // für esp_iface_mac_addr_set
static const uint8_t NCONNECT_HOST_ADDR_LE[6] = { 0x03, 0x02, 0x01, 0xE9, 0xB6, 0x98 }; // wie in der Werbung / NimBLE

typedef struct {
    uint8_t addr[6], addr_le[6];
    uint16_t pid;            // NCONNECT_PID_PRO2 0x2069, _JOYCON2_L 0x2067, _JOYCON2_R 0x2066, _GAMECUBE2 0x2073
    const char *name;
    int8_t player_slot;      // -1 = keiner
    uint8_t output;          // NCONNECT_OUTPUT_DEFAULT / _XBOX360 / _DS4
    uint8_t single_joycon;
    uint8_t has_stick_cal;   // Bit 0 links, Bit 1 rechts
    int16_t stick_cal[2][6]; // Mitte X, Max X, Min X, Mitte Y, Max Y, Min Y
} nconnect_controller_t;

#define NCONNECT_CONTROLLER_COUNT 2
static const nconnect_controller_t NCONNECT_CONTROLLERS[NCONNECT_CONTROLLER_COUNT] = { … };
```

Erzeugt von `Esp32Export.ToHeader` (`src/Switch2Pro.Protocol/Esp32Export.cs`, Tests in `Esp32ExportTests`).

## 5. Leitfaden für die ESP32-Firmware (ESP-IDF 6.x, NimBLE)

Getestet ist hier nur, dass die ESP-IDF-Funktionen existieren (ESP-IDF v6.1). Der Ablauf entspricht dem, was
N-Connect unter Windows tut (`ControllerManager.OnAdvertisement`, `Switch2BleLink`).

### 5.1 Konfiguration
- `CONFIG_BT_ENABLED=y`, `CONFIG_BT_NIMBLE_ENABLED=y`, Rolle **Central** und **Observer** an.
- `CONFIG_BT_NIMBLE_MAX_CONNECTIONS` ≥ Anzahl gleichzeitiger Controller (Joy-Con-Paar = 2).
- ATT-MTU groß genug für den Eingabebericht (≥ 70 Byte; z. B. `CONFIG_BT_NIMBLE_ATT_PREFERRED_MTU=185`).

### 5.2 Adresse übernehmen – vor dem Start von Bluetooth
```c
#include "esp_mac.h"
#include "nconnect_pairing.h"

void app_main(void) {
    // Muss vor nimble_port_init() / esp_bt_controller_init() stehen; der Bluetooth-Controller liest sie per
    // esp_read_mac(…, ESP_MAC_BT) beim Start.
    ESP_ERROR_CHECK(esp_iface_mac_addr_set(NCONNECT_HOST_ADDR, ESP_MAC_BT));
    ESP_ERROR_CHECK(nimble_port_init());
    …
}
```
Beim Start loggt der Controller „Bluetooth MAC: …“. Dort muss die Adresse aus `NCONNECT_HOST_ADDR_STR` stehen. Eigene
Verbindungen mit `BLE_OWN_ADDR_PUBLIC` aufbauen, keine Zufallsadresse.

### 5.3 Werbung erkennen
Passiv scannen (`ble_gap_disc`, `passive = 1`, Duplikate nicht filtern). In den Herstellerdaten (AD-Typ `0xFF`):

```
Kennung 0x0553 (LE: 53 05) | 01 00 03 | 7E xx | PID (LE) | … | Byte 10–15: Host-Adresse (LE)
```
(Byte-Zählung ohne die zwei Kennungs-Bytes, wie in [PROTOKOLLE 1.1](PROTOKOLLE.md).)

- PID bestimmt die Art (`0x2069` Pro 2, `0x2067`/`0x2066` Joy-Con 2 L/R, `0x2073` GameCube).
- **Byte 10–15 == `NCONNECT_HOST_ADDR_LE`** → der Controller sucht „deinen PC“ → verbinden.
- Byte 10–15 alle 0 → SYNC-Modus. Der ESP32 könnte hier selbst koppeln (Befehl 0x15 mit seiner Adresse, PROTOKOLLE
  1.6). Er sollte es aber **nicht** tun, damit die Kopplung mit dem Stick erhalten bleibt.
- Nach dem Trennen wirbt der Controller noch kurz weiter. N-Connect wartet 3 s und verbindet erst nach einer Pause
  und einem neuen Tastendruck (sonst verbindet man ungewollt sofort wieder).

### 5.4 Verbinden und starten
1. `ble_gap_connect` mit der Adresse aus der Werbung (Adresstyp übernehmen), kein Pairing/SMP.
2. MTU tauschen (`ble_gattc_exchange_mtu`), kürzeres Verbindungsintervall anfragen (7,5–15 ms; der Controller
   wählt sonst 30 ms ≈ 33 Berichte/s).
3. Dienst `ab7de9be-89fe-49ad-828f-118f09df7fd0` und die Merkmale aus [PROTOKOLLE 1.2](PROTOKOLLE.md) suchen.
4. Benachrichtigungen für **Antworten** (`c765a961-…`) einschalten (CCCD `0x2902` = `01 00`).
5. Befehle an `649d4ac9-…` (Schreiben ohne Antwort), Kopf `Befehl | 91 | 01 | Unter | 00 | Länge | 00 00` + Daten:
   - `0C 91 01 02 00 04 00 00 | M 00 00 00` (Funktionen setzen), dann dasselbe mit Unterbefehl `04` (einschalten);
     `M` = `0x2F` (Pro 2, GameCube) bzw. `0x37` (Joy-Con 2). Andere Bits erzeugen bei Joy-Con Phantom-ZL/ZR.
   - optional Spieler-LED: `09 91 01 07 00 08 00 00 | Muster + 7×00` (Muster 1–8: `01 03 07 0F 09 05 0D 06`).
6. Benachrichtigungen für den **Eingabebericht** (`ab7de9be-…-7fd2`) einschalten.
7. Kalibrierung lesen (`02 91 01 04 …`, Adressen 0x130A8/0x130E8, Benutzerkalibrierung 0x1FC040/0x1FC080) – oder
   die Werte aus `stick_cal` nehmen, falls `has_stick_cal` gesetzt ist.

### 5.5 Eingaben auswerten
Eingabebericht wie in [PROTOKOLLE 1.4](PROTOKOLLE.md): Tasten in Byte 4–7, Sticks als 12-Bit-Paare ab 0x0A/0x0D
(`A = b0 | (b1 & 0x0F) << 8`, `B = b1 >> 4 | b2 << 4`), Bewegung ab 0x30, Akku ab 0x1F. Vibration: [PROTOKOLLE 1.5](PROTOKOLLE.md).

### 5.6 Wie bekommt der zweite PC die Controller?
Der ESP32-S3 hat einen nativen USB-Anschluss (Buchse „USB“, nicht „UART“). Zwei Wege:

| Weg | Was der ESP32 tut | Am zweiten PC |
|---|---|---|
| **A – ESP32 als Funkadapter für N-Connect** (empfohlen) | reicht die Rohberichte per USB (CDC/serielle Schnittstelle) durch und nimmt Befehle/Vibration entgegen | N-Connect wie gewohnt (Tastenbelegung, Xbox/DS4, Gyro, Profile). Braucht eine **neue Verbindungsart in N-Connect** („ESP32-Adapter“) – noch nicht gebaut |
| **B – ESP32 als eigenständiges Gamepad** | wandelt selbst in ein USB-HID-Gamepad (TinyUSB) um | nichts zu installieren, aber Belegung, Gyro und Xbox-Kompatibilität muss die Firmware selbst leisten (generisches HID-Gamepad ≠ XInput) |

Weg A verhält sich am zweiten PC genauso wie dein Stick am ersten. Weg B kommt ohne Software aus.

## 6. Fehlersuche

| Beobachtung | Ursache / Abhilfe |
|---|---|
| ESP32 sieht Werbung, Verbindung scheitert sofort | Adresse nicht übernommen (Log „Bluetooth MAC“ prüfen) oder eigene Verbindung mit Zufallsadresse aufgebaut |
| Controller verbindet sich mit dem ersten PC statt mit dem ESP32 | beide in Reichweite – N-Connect am ersten PC beenden oder Bluetooth aus |
| Byte 10–15 enthalten eine andere Adresse | Controller wurde inzwischen an einer Konsole/einem anderen Gerät gekoppelt → am PC neu SYNC, neu exportieren |
| Byte 10–15 alle 0 | Controller ist im SYNC-Modus (SYNC-Taste gedrückt) |
| Nur ~33 Berichte/s | Verbindungsintervall nicht verkürzt (5.4 Schritt 2) |
| Joy-Con meldet ZL/ZR, obwohl nicht gedrückt | falsche Funktionsmaske, `0x37` verwenden |

## 7. Sicherheit

Der Export enthält keine Schlüssel, nur Adressen. Die Adresse eines Bluetooth-Adapters ist ohnehin für jeden in
Funkreichweite sichtbar. Trotzdem gilt: Wer die Adresse kennt und einen ESP32 damit betreibt, kann sich mit deinen
Switch-2-Controllern verbinden, wenn sie für deinen PC werben. Das ist eine Eigenschaft der Controller, nicht von
N-Connect.

# USB-Protokoll: Funkadapter ⇄ N-Connect

Für **Weg A** (siehe [ESP32.md §5.6](ESP32.md) / [NRF52840.md](NRF52840.md)): Ein ESP32-S3 oder nRF52840 verbindet
sich per Bluetooth LE mit den Switch-2-Controllern (er gibt sich als der PC-Bluetooth-Adapter aus) und reicht sie über
USB an N-Connect weiter. N-Connect zeigt sie dann wie an einem normalen Stick an – mit voller Belegung, Gyro,
Xbox-/DS4-Ausgabe.

Der Adapter muss die Controller **nicht** selbst verstehen: Er leitet den rohen Switch-2-Eingabebericht 0x05 durch,
den N-Connect ohnehin schon parst (`InputReports.TryParseReport05`). Umgekehrt schickt N-Connect nur einfache
Vibrations- und LED-Befehle; die HD-Rumble-Frames baut der Adapter daraus selbst.

Gemeinsame Umsetzung: `src/Switch2Pro.Protocol/AdapterProtocol.cs` (Rahmen, CRC, Nachrichten), Tests in
`AdapterProtocolTests`. Diese Datei ist die Referenz für die Firmware.

## 1. Transport
USB-CDC (virtueller COM-Port), 8N1, Baudrate egal (USB-CDC ignoriert sie). N-Connect erkennt den Adapter an seiner
Begrüßung (`Hello`), nicht an VID/PID – so geht jeder Nachbau.

## 2. Rahmen (SLIP + CRC16)
SLIP-Rahmung (RFC 1055):

| Byte | Bedeutung |
|---|---|
| `0xC0` | END – Rahmenanfang und -ende |
| `0xDB 0xDC` | ein `0xC0` in den Daten |
| `0xDB 0xDD` | ein `0xDB` in den Daten |

Innerhalb eines Rahmens (vor dem SLIP-Kodieren):

```
Art (1) | Slot (1) | Nutzdaten (0..n) | CRC16 (2, großes Byte zuerst)
```

- **CRC16** = CRC-16/CCITT-FALSE (Polynom `0x1021`, Start `0xFFFF`) über `Art | Slot | Nutzdaten`. Prüfwert für
  „123456789“: `0x29B1`.
- Empfänger verwirft Rahmen mit falscher CRC oder < 4 Byte und synchronisiert sich am nächsten `0xC0` neu.
- **Slot** = Controllerplatz 0–7 am Adapter (bei Nachrichten ohne Bezug 0).

## 3. Nachrichten

### Adapter → PC
| Art | Name | Nutzdaten |
|---|---|---|
| `0x08` | `Hello` | Protokollversion (1) + Firmware-Text (UTF-8). Nach dem Verbinden und auf `Ping`. |
| `0x10` | `Connected` | PID (2, LE) + Controller-Adresse (6, höchstes Byte zuerst). `slot` = Platz. |
| `0x11` | `Disconnected` | – |
| `0x12` | `Input` | roher Bericht 0x05 (wie über BLE, ohne Report-ID). `slot` = Platz. |
| `0x1F` | `Log` | UTF-8-Text (nur Fehlersuche; N-Connect schreibt ihn ins Protokoll). |

PID: `0x2069` Pro Controller 2, `0x2067` Joy-Con 2 L, `0x2066` Joy-Con 2 R, `0x2073` GameCube.

### PC → Adapter
| Art | Name | Nutzdaten |
|---|---|---|
| `0x01` | `SetHost` | Host-Adresse (6, **niedrigstes** Byte zuerst). Adapter setzt sie als eigene öffentliche Adresse und sucht danach. |
| `0x02` | `Ping` | – (Adapter antwortet mit `Hello`) |
| `0x20` | `Rumble` | großer Motor (1), kleiner Motor (1), je 0–255. `slot` = Platz. |
| `0x21` | `PlayerLed` | Muster (1, Bitmaske wie Switch: Spieler 1–8 → `01 03 07 0F 09 05 0D 06`). `slot` = Platz. |

## 4. Ablauf
1. N-Connect öffnet den Port, schickt `Ping`, erwartet `Hello` mit passender Version.
2. N-Connect schickt `SetHost` mit der Adresse, als die der Adapter auftreten soll (aus dem ESP32-/nRF52840-Export).
3. Der Adapter sucht nach Werbung `0x0553`, deren Byte 10–15 diese Adresse ist, verbindet sich (BLE, ohne Pairing),
   startet den Controller (Feature-Maske setzen/einschalten, siehe [PROTOKOLLE §1.3](PROTOKOLLE.md)) und meldet
   `Connected`.
4. Laufend: Adapter → `Input` je Bericht; N-Connect → `Rumble`/`PlayerLed` bei Bedarf.
5. Beim Abbruch meldet der Adapter `Disconnected`; N-Connect entfernt den Spieler.

## 5. N-Connect-Seite
Eine neue Verbindungsart (`IControllerLink` über den COM-Port) macht aus jedem `Connected`/`Input` einen normalen
Spieler. Vibration/LED gehen als `Rumble`/`PlayerLed` zurück. Status: **noch nicht umgesetzt** – das Rahmenprotokoll
(diese Seite) steht und ist getestet; Firmware und die N-Connect-Verbindungsart folgen.

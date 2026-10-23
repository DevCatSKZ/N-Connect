# nRF52840-Firmware (verworfen, nur Referenz)

> **Verworfen am 10.10.2026** – Grund: Der nRF52840 kann kein Bluetooth-Adapter für Windows sein (er beherrscht nur
> Bluetooth LE), der Relais-Weg (Dongle verbindet die Controller und reicht sie per USB an SKZ Connect weiter) blieb in
> den Tests ohne Ergebnis, und jeder Test kostete ein erneutes Koppeln der Controller. SKZ Connect enthält seit 1.0.22
> weder die Gegenstelle noch einen Export der Kopplungsdaten für diese Firmware. Der Code wird nicht weiterentwickelt.

Der Code bleibt als Referenz für den BLE-Ablauf eines Switch-2-Controllers aus Sicht eines eigenen Hosts: Werbung
erkennen (Nintendo-Herstellerdaten, Host-Adresse in Byte 10–15), öffentliche Adresse vor `bt_enable()` setzen,
GATT-Verbindung aufbauen, Bericht 0x05 abonnieren. Der GATT-Teil wurde nie mit einem echten Controller eingefahren
(Stellen mit `PRÜFEN:` in `src/main.c`).

- Controller-Protokoll (GATT, Befehle, Bericht 0x05): [../../docs/PROTOKOLLE.md](../../docs/PROTOKOLLE.md)
- Damaliger Plan und Grenzen: [../../docs/archiv/NRF52840.md](../../docs/archiv/NRF52840.md)
- USB-Protokoll zur früheren Gegenstelle in SKZ Connect: [../../docs/archiv/ADAPTER-PROTOKOLL.md](../../docs/archiv/ADAPTER-PROTOKOLL.md)

## BLE-Referenz aus dem Mitschnitt

Am 10.10.2026 wurde mit dem Dongle als nRF Sniffer for Bluetooth LE (4.1.1) die funktionierende Strecke
Windows ↔ Switch-2-Controller mitgeschnitten. Die Mitschnitte liegen nicht im Repo (sie enthalten Geräteadressen).
Festgehalten:

- Ablauf nach dem Verbinden: `LL_VERSION_IND` → `LL_LENGTH_REQ` (Data Length Extension, 251 Byte) →
  `LL_FEATURE_REQ` → ATT-MTU-Austausch → Dienstsuche.
- Verbindungsparameter beim Aufbau: Intervall 60 ms, Latenz 0, Supervision-Timeout 9,6 s.
- Eigener Dienst `ab7de9be-89fe-49ad-828f-118f09df7fd0` (kein HID-over-GATT), Handle des Eingabeberichts etwa
  `0x000A`.

## Bauen und Flashen

Nur für Versuche mit der Referenz. `src/main.c` erwartet eine Datei `skzconnect_pairing.h` neben sich, die
`SKZCONNECT_HOST_ADDR_LE[6]` definiert (Adresse des PC-Adapters, niedrigstes Byte zuerst). SKZ Connect erzeugt diese
Datei nicht mehr; sie muss von Hand angelegt werden.

Voraussetzungen: nRF Connect SDK (Zephyr) ab v2.6 mit Toolchain (`west`, arm-none-eabi-gcc) und `nrfutil` oder
„nRF Connect for Desktop → Programmer“.

```sh
west build -b nrf52840dongle_nrf52840 .
nrfutil pkg generate --hw-version 52 --sd-req 0x00 \
  --application build/zephyr/zephyr.hex --application-version 1 skzconnect_dfu.zip
nrfutil dfu usb-serial -pkg skzconnect_dfu.zip -p COM<n>
```

Zum Flashen den Dongle anstecken und die seitliche RESET-Taste drücken, bis die rote LED pulsiert (DFU-Modus). Der
Open Bootloader bleibt erhalten; die zuvor installierte Firmware lässt sich ohne SWD-Programmer nicht sichern.

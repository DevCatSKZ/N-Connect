# N-Connect-Firmware für den nRF52840-Dongle (Weg A)

Diese Firmware macht aus einem **Nordic nRF52840-Dongle** (PCA10059) einen Funkadapter: Er gibt sich als dein
PC-Bluetooth-Adapter aus, verbindet die Switch-2-Controller per Bluetooth LE und reicht sie über USB an N-Connect
weiter. N-Connect zeigt sie dann wie an einem normalen Stick an (Einstellung *Erweitert → Funkadapter*).

- Protokoll zu N-Connect: [../../docs/ADAPTER-PROTOKOLL.md](../../docs/ADAPTER-PROTOKOLL.md)
- Controller-Protokoll (GATT, Befehle, Bericht 0x05): [../../docs/PROTOKOLLE.md](../../docs/PROTOKOLLE.md)
- Prinzip und Grenzen: [../../docs/NRF52840.md](../../docs/NRF52840.md)

> **Stand:** v0, **noch nicht auf Hardware getestet.** Rahmenformat, Adressübernahme und das Erkennen der Werbung
> sind 1:1 aus dem getesteten N-Connect-Code übernommen; der GATT-Ablauf (Verbinden, Merkmale finden, abonnieren,
> Befehle schreiben) ist nach der Doku gebaut und muss mit echtem Controller eingefahren werden. `// PRÜFEN:` markiert
> die Stellen, die am ehesten Nacharbeit brauchen.

## Voraussetzungen
- **nRF Connect SDK** (Zephyr), v2.6 oder neuer – inkl. Toolchain (`west`, arm-none-eabi-gcc). Am einfachsten über
  „nRF Connect for Desktop“ → Toolchain Manager.
- **nrfutil** (Nordic) zum Flashen über den USB-Bootloader des Dongles, oder „nRF Connect for Desktop → Programmer“.

## Bauen
1. Deine Kopplungsdaten exportieren: N-Connect → *Kopplungsdaten → Für nRF52840 exportieren*. Die Datei
   **`nconnect_pairing.h`** aus dem ZIP hierher neben `src/main.c` legen (sie liefert `NCONNECT_HOST_ADDR_LE`).
2. Bauen:
   ```sh
   west build -b nrf52840dongle_nrf52840 .
   ```
   Ergebnis: `build/zephyr/zephyr.hex`.

## Flashen (über den USB-Bootloader, ohne Programmer)
Dongle anstecken und die seitliche **RESET**-Taste drücken, bis die rote LED pulsiert (DFU-Modus). Dann:
```sh
nrfutil pkg generate --hw-version 52 --sd-req 0x00 \
  --application build/zephyr/zephyr.hex --application-version 1 nconnect_dfu.zip
nrfutil dfu usb-serial -pkg nconnect_dfu.zip -p COM15
```
(COM-Port anpassen; unter nRF Connect for Desktop → Programmer geht es auch grafisch.)

Der **Open Bootloader bleibt erhalten** – mit RESET kommst du jederzeit zurück in den DFU-Modus und kannst neu
flashen. Die zuvor installierte Firmware lässt sich ohne SWD-Programmer aber **nicht sichern**.

## Benutzen
Dongle an den PC mit N-Connect stecken, dort *Erweitert → Funkadapter* einschalten. Controller einschalten / eine
Taste drücken – sie erscheinen in N-Connect. Der PC mit dem echten Stick sollte dabei nicht gleichzeitig in
Reichweite aktiv sein (gleiche Adresse).

## LED (Vorschlag)
- langsames Pulsieren: sucht Controller · an: mindestens einer verbunden · schnelles Blinken: USB nicht verbunden.

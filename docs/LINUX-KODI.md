# N-Connect für Linux / LibreELEC (Kodi-Add-on)

Stand: 11.10.2026, Version 0.1.0 (in Entwicklung).

**Auf echter Hardware geprüft:** Raspberry Pi 4 mit LibreELEC 12.2.1 (Kodi 21.3, eingebautes Bluetooth).
- Pro Controller 2: SYNC-Kopplung und Wiederverbinden per Tastendruck ohne SYNC funktionieren, ca. 89 Eingaben/s.
- Joy-Con 2 (L) und (R): Kopplung funktioniert; die beiden werden automatisch zu einem Spieler zusammengefasst.
- Kodi erkennt das virtuelle Pad als „Microsoft X-Box 360 pad“.

## Erkenntnisse vom echten Gerät (wichtig)

- **BlueZ-GATT funktioniert mit Switch-2-Controllern nicht.** Nach dem Verbinden fragt BlueZ auch „Secondary
  Services“ (0x2801) ab, und darauf antworten die Controller **nie**. Die Diensteabfrage hängt, `ServicesResolved` wird
  nie true. Lösung: [AttClient.cs](../linux/NConnect.Linux/Ble/AttClient.cs) ist ein eigener ATT-Client über einen
  L2CAP-Socket (Kanal 4). Er hat beim Kernel Vorrang vor bluetoothd, so wie früher gatttool.
- **Verbindungsintervall:** BlueZ wählt 48,75 ms, also nur ca. 20 Eingaben/s. Der ATT-Client schickt deshalb nach dem
  Verbinden `HCI LE Connection Update` (6–9 × 1,25 ms) über einen raw-HCI-Socket (root). Ergebnis: ca. 89/s. Der alte
  debugfs-Weg ist entfernt, denn auf dem Pi-Kernel gibt es diese Dateien gar nicht.
- **Suche nach dem Verbinden neu starten:** Für den eigenen Verbindungsaufbau pausiert der Kernel die Suche und nimmt
  sie nicht wieder auf, während BlueZ weiter `Discovering = true` meldet. Deshalb startet der Daemon die Suche nach
  jedem Verbinden/Trennen neu (`KickScan`) und zusätzlich jede Minute.
- **Joystick-Schnittstelle:** Kodi auf dem Pi nutzt die Schnittstelle „linux“, und dafür hat Kodi die
  Xbox-360-Belegung bereits. Die mitgelieferte udev-Belegung schadet nicht.
- **Texte nach Installation per Dateikopie:** Kopiert man das Add-on von Hand statt über die ZIP-Installation, behält
  Kodi einen leeren Text-Zwischenspeicher, bis Kodi neu startet. Bei der normalen ZIP-Installation tritt das nicht auf.
- **Schriften:** Der Aeon-Nox-Skin kann „→ ✓ ✗“ nicht darstellen. Die Texte nutzen deshalb „>“, „OK“ und „--“.

## Andere Controller (Xbox, PlayStation, Switch 1, 8BitDo …)

[GamepadManager.cs](../linux/NConnect.Linux/Ble/GamepadManager.cs) macht N-Connect zur zentralen Stelle für alle
Controller. Die Kernel-Treiber von LibreELEC (xpad, hid-microsoft, hid-playstation, hid-sony, hid-nintendo, hid-steam)
übernehmen Eingaben, Vibration und Akku. N-Connect übernimmt das Koppeln:

- **Erkennen:** Ein Gamepad im Kopplungsmodus wird an der Geräteklasse (Peripherie/Joystick/Gamepad), am LE-Erscheinungsbild
  0x03C3/0x03C4 oder an einem bekannten Namen erkannt. Die Suche läuft dafür mit `Transport=auto` (LE und klassisch).
- **Koppeln:** Danach wird es gekoppelt, als vertrauenswürdig markiert und verbunden, und es verbindet sich künftig per
  Tastendruck selbst.
- **Agent:** [PairingAgent.cs](../linux/NConnect.Linux/Ble/PairingAgent.cs) hat die Fähigkeit „NoInputNoOutput“ und
  eine **eigene D-Bus-Verbindung**. Die gemeinsame Verbindung erlaubt keine Objekte, und BlueZ fragt den Agenten der
  Verbindung, die `Pair()` aufruft.
- **Keine Konflikte mit LibreELEC:**
  - Der Agent wird nicht als Standard angemeldet.
  - Koppelt gerade jemand über die LibreELEC-Seite („InProgress“/„AlreadyExists“), hält sich N-Connect heraus.
  - Nicht-Gamepads und der Adapter-Strom werden nie angefasst.
- **Ausnahmen:** Wii-Fernbedienungen, denn sie brauchen eine binäre PIN.
- **Menü:** Die Gamepads erscheinen mit Verbindungsstatus und Akku; Aktionen „Trennen“ und „Vergessen“.
- **Einstellung:** „Andere Controller automatisch koppeln“ (`AutoPairGamepads`, Standard: an).

## Wii, DSU-Gyro, Spielerplätze, Akku, Tasten (ab 0.2/0.3)

- **Wii-Fernbedienung / Wii U Pro:** Die feste Wii-PIN (umgedrehte Adapter-Adresse) setzt BlueZ selbst; das Modul
  „wiimote“ ist in LibreELEC enthalten (geprüft). Den Rest erledigt der Kernel-Treiber `hid-wiimote`. N-Connect lässt
  „Nintendo RVL-CNT…“ deshalb einfach durch.
- **DSU/Cemuhook-Server** ([Output/DsuServer.cs](../linux/NConnect.Linux/Output/DsuServer.cs)):
  - Portierung der Windows-Fassung. Port 26760, standardmäßig nur lokal; `DsuNetwork` öffnet ihn fürs Heimnetz.
  - Geprüft auf dem Pi mit einem Test-Client: 89 Pakete/s mit echten Gyro- und Beschleunigungswerten.
- **Feste Spielerplätze:** In `slots.json` im Datenordner steht Adresse → Spieler. Ist der gemerkte Platz frei, wird
  er wieder vergeben.
- **Akku-Warnung:** Das Ereignis `battery-low` kommt einmal bei 20 % und einmal bei 10 %. Nach dem Laden wird zurückgesetzt.
- **Controller-Tasten:** Bei Capture, C, GL und GR sendet der Dienst das Ereignis `button`. Das Add-on führt dann die
  eingestellte Kodi-Aktion aus (`common.BUTTON_ACTIONS`).

## DevCatSKZ Repository und Release

- **Öffentliches Repo:** [github.com/DevCatSKZ/kodi-repo](https://github.com/DevCatSKZ/kodi-repo).
  - Kodi-Add-on `repository.devcatskz` mit dem Anzeigenamen „DevCatSKZ Repository“.
  - Der Index wird mit `tools/build_repo.py` erzeugt; die zwei neuesten Versionen je Add-on bleiben erhalten.
  - In `downloads/` liegen die Einzel-ZIPs je Gerät.
- **Lokal veröffentlichen:** `linux/build-kodi-addon.ps1 -Repo` legt die ZIPs in die Arbeitskopie `..\kodi-repo` (neben
  sfm und N-Connect). Danach in kodi-repo committen und pushen.
- **CI** ([.github/workflows/kodi.yml](../.github/workflows/kodi.yml)):
  - Jeder Push auf `linux/**` erzeugt das Artefakt „Kodi-Plugin“.
  - Ein Tag `v…` hängt die ZIPs ans Release und legt die Plugin-Version ins Repository. Dafür braucht es das Secret
    **`KODI_REPO_TOKEN`**: ein fein abgestufter Token, nur für `DevCatSKZ/kodi-repo`, „Contents: Read and write“.
    Deploy-Schlüssel sind in der Organisation abgeschaltet. Ohne das Secret wird dieser Schritt übersprungen.
- **Version des Plugins:** Sie steht in `linux/kodi/service.nconnect/addon.xml` und ist unabhängig von der
  Windows-Version. Kodi aktualisiert nur, wenn sie steigt, und nur bei Installationen aus dem Repository, nicht bei
  Installationen per ZIP.

## Hintergrundsuche

- **Einstellung „Ständig nach Controllern suchen“** (`BackgroundScan`, Standard: an).
- **Wenn aus:** Gesucht wird nur nach dem Menüpunkt „Jetzt nach Controllern suchen“, dann 2 Minuten lang (Befehl `scan`).
- **Gekoppelte klassische Gamepads** verbinden sich trotzdem selbst. **Switch-2-Controller** brauchen zum Wiederverbinden
  die Suche.

## Sprachen

Es gibt dieselben 12 Sprachen wie unter Windows (de, en, es, fr, it, pt, nl, pl, ru, ja, zh, ko, dazu pt_BR), und Kodi
wählt sie nach seiner eingestellten Sprache aus. Englisch und Deutsch werden von Hand gepflegt. Die übrigen erzeugt
[linux/kodi/i18n/generate.py](../linux/kodi/i18n/generate.py) aus `tr_a.py`/`tr_b.py`; neue Texte kommen in `EXTRA`.
Die Anleitung (`help_*.txt`) gibt es bisher auf Deutsch und Englisch.

## Überblick

```
Switch-2-Controller ──BLE──► BlueZ (bluetoothd) ──D-Bus──► nconnect (Dienst, .NET 10, root)
                                                              │  Switch2Pro.Protocol (wie Windows/Android)
                                                              ▼
                                         /dev/uinput → „Microsoft X-Box 360 pad“ (045E:028E) je Spieler
                                                              ▼
                                         Kodi (peripheral.joystick, udev) · RetroArch · SDL-Spiele
```

| Teil | Ort | Aufgabe |
|---|---|---|
| `nconnect` | [linux/NConnect.Linux/](../linux/NConnect.Linux/) | Programm ohne Oberfläche: Suche, Verbindung, Belegung, virtuelle Controller, Statusdatei |
| Kodi-Add-on `service.nconnect` | [linux/kodi/service.nconnect/](../linux/kodi/service.nconnect/) | Startet/überwacht `nconnect`, Einstellungen, Meldungen, Menü, Diagnose |
| Build-Skript | [linux/build-kodi-addon.ps1](../linux/build-kodi-addon.ps1) | Veröffentlicht linux-x64/arm64/arm und packt die Add-on-ZIPs nach `out/kodi/` |

## Andere Kodi-Plattformen (ab 0.4.0)

Das Add-on ist nicht mehr LibreELEC-only (`<platform>all</platform>`):

- **Batocera / sonstiges Linux:** Dasselbe wie LibreELEC – das Add-on enthält das Programm für x86_64 und
  aarch64 und läuft dort, weil alles als root läuft und BlueZ/uinput vorhanden sind. Auf Batocera selbst koppelt
  bereits das System Bluetooth-Controller; N-Connect ist für Switch-2-Controller gedacht, die das Standard-BlueZ
  nicht schafft. Hinweis: die Bluetooth-Einstellungen liegen dort im Batocera-/Systemmenü, nicht bei
  `service.libreelec.settings` (das Menü prüft, ob das Add-on existiert). **Noch nicht auf echter Hardware geprüft.**
- **Desktop-Linux** (Ubuntu & Co., Kodi läuft als normaler Benutzer): Das Programm braucht root für HCI-Rohzugriff
  und /dev/uinput – als Benutzer gestartet schlägt es fehl (der eingebaute Selbsttest zeigt das an). Für Kodi auf
  Desktop-Linux müsste noch ein systemd-Dienst oder eine udev-Regel ergänzt werden; bis dahin ungetestet.
- **Kodi auf Windows:** Es gibt keinen Linux-Dienst – die installierte N-Connect-App verbindet die Controller
  (als XInput) und [KodiStatusWriter.cs](../src/Switch2Pro.Bridge/KodiStatusWriter.cs) schreibt dieselbe
  `status.json` in Kodis `userdata/addon_data/service.nconnect/`. Das Add-on läuft im „Begleitmodus": es zeigt
  Menü und Widget (verbunden farbig, bekannte/getrennte ausgegraut) und lädt die Anzeige bei Änderungen neu.
  Trennen/Koppeln/Selbsttest/Dienst-Protokoll bleiben der App vorbehalten und werden im Menü nicht angeboten.
  Kodi-Ordner findet [KodiIntegration.cs](../src/Switch2Pro.Bridge/KodiIntegration.cs) (normale Installation
  und Microsoft-Store-Variante).

## Programm `nconnect`

- **Bluetooth:** [Linux.Bluetooth](https://github.com/SuessLabs/Linux.Bluetooth) 6.0.0-pre3 über D-Bus. Bewusst
  nicht 5.x: Das hängt an Tmds.DBus 0.20 mit CVE-2026-39959. Tmds.DBus ist explizit auf 0.95.1 festgelegt.
- **Suche** ([Switch2Scanner.cs](../linux/NConnect.Linux/Ble/Switch2Scanner.cs)): Der Discovery-Filter ist
  `Transport=le, DuplicateData=true`. BlueZ meldet neue Geräte über `DeviceFound`, bekannte über
  Eigenschaftsänderungen (RSSI/ManufacturerData). Beides wird ausgewertet, damit ein gekoppelter Controller nach einem
  Tastendruck sofort erkannt wird. Beendet ein anderes Programm (z. B. Kodis Bluetooth-Seite) die Suche, startet der
  Dienst sie nach spätestens 3 s neu.
- **Gerät** ([Switch2Device.cs](../linux/NConnect.Linux/Ble/Switch2Device.cs)): Das ist eine Portierung der
  Android-Fassung, mit demselben Ablauf (Funktionen, Kalibrierung, Seriennummer, Kopplung 0x15, Wächter, Vibration).
  Die MTU handelt BlueZ selbst aus. Geschrieben wird ohne Antwort per `{"type":"command"}`. Wichtig: Linux.Bluetooth
  ruft beim Anmelden an `Value` selbst `StartNotify` auf, deshalb wird erst auf `Notifying` gewartet.
- **Verbindungsintervall:** Über debugfs (`/sys/kernel/debug/bluetooth/hciX/conn_min/max_interval` = 6/9, also
  7,5–11,25 ms statt 30–50 ms). Ohne debugfs bleibt der Standard; das funktioniert, ist aber träger.
- **Virtueller Controller** ([UInputPad.cs](../linux/NConnect.Linux/Output/UInputPad.cs)):
  - Name, Kennung und Belegung wie beim Kernel-Treiber xpad: „Microsoft X-Box 360 pad“, USB 045E:028E, Version 0x0114.
  - 11 Tasten, Sticks ±32767 (Y nach unten positiv), Trigger 0…255, Steuerkreuz als HAT0X/Y.
  - Spieler werden über `phys` unterschieden (`nconnect/playerN`), weil Kodi die Belegung über den Namen findet.
  - Vibration über `FF_RUMBLE`: Upload/Erase per `UI_BEGIN/END_FF_*`, abgespielt mit `EV_FF`. Die ioctl-Größen sind
    für 64 und 32 Bit berechnet.
- **Einstellungen:** dieselbe `settings.json`/`Settings`-Klasse wie unter Windows und Android. Die Datei wird
  überwacht und bei Änderung neu geladen (auch Belegungen, Profile und Gyro aus Windows).
- **Schnittstelle zum Add-on:**
  - `status.json` (alle 5 s und bei Änderungen): Bluetooth-Zustand, Spieler, Akku, Rate, Seriennummer, Firmware,
    „mit anderem Gerät gekoppelt“.
  - Ereignisse als JSON-Zeilen auf stdout: `connected`, `paired`, `disconnected`, `elsewhere`, `failed`, `bluetooth`,
    `error`.
  - Befehle über stdin: `reload`, `disconnect <n>`, `disconnect-all`, `status`.
- **Weitere Funktionen:** Trennen nach Inaktivität (`InactivityMinutes`), nach einem Trennen von Hand werden Controller
  15 s ignoriert, und fällt der Adapter weg, beginnt eine neue Sitzung.
- **Aufrufe:** `nconnect [--data DIR] [--adapter hci0]`, `--selftest` (JSON-Diagnose), `--test-pad [s]` (Testcontroller
  ohne Bluetooth, gibt Vibration aus), `--version`.
- **Veröffentlichung:** self-contained Single-File, komprimiert, Trim-Modus `partial`. Eigene Bibliotheken und
  Tmds.DBus bleiben vollständig erhalten, weil sie Reflection nutzen. `InvariantGlobalization`, weil LibreELEC kein ICU
  hat. Größe ca. 13 MB je Prozessor (ohne Trimming 38 MB).

## Kodi-Add-on `service.nconnect`

- `addon.xml`: hat zwei Einstiegspunkte, `xbmc.service` (startet mit Kodi) und `xbmc.python.script` (erscheint unter
  Programme → N-Connect).
- `service.py`:
  - Startet das Programm passend zum Prozessor aus `bin/<rid>/nconnect` und setzt das Ausführungsrecht, das beim
    Entpacken verloren geht.
  - Lädt `modprobe uinput`.
  - Überträgt die Kodi-Einstellungen in `settings.json` und lässt dabei fremde Einträge stehen.
  - Zeigt Meldungen beim Verbinden und Trennen (abschaltbar; Fehler erscheinen immer).
  - Startet das Programm nach einem Absturz neu: nach 3/5/10/30/60 s.
  - Beendet es per SIGTERM, wenn Kodi schließt.
  - Befehle aus dem Menü kommen über `NotifyAll(service.nconnect,<befehl>)` → `onNotification`.
- **Kodi-Belegung:** Kodi bringt für die udev-Kennung 045E:028E keine Xbox-360-Belegung mit, sondern nur für Steams
  28DE:11FF. Das Add-on kopiert deshalb [die eigene Belegung](../linux/kodi/service.nconnect/resources/buttonmaps/udev/)
  einmalig nach `addon_data/peripheral.joystick/resources/buttonmaps/xml/udev/`. Eine in Kodi angepasste Belegung wird
  nie überschrieben.
- **Menü** (`default.py`): zeigt
  - die Spieler mit Akku und Rate; wählt man einen aus: Trennen oder Details (Seriennummer, Firmware, Adresse),
  - Hinweise zu „Bluetooth aus“, „kein Adapter“ und „mit anderem Gerät gekoppelt“,
  - „Alle trennen“, Anleitung, Diagnose (`--selftest` mit ✓/✗), Protokoll und Einstellungen.
- **Einstellungen:** Tastenbelegung nach Position (Xbox) oder wie beschriftet, Stick-Totzone, Joy-Con zusammenfassen,
  Gyro-Stick (aus/immer/beim Zielen) samt Tempo und Umkehrung, Vibration samt Stärke, automatisch koppeln,
  Spieler-LEDs, Verbinden-Klick, Trennen bei Nichtbenutzung (Standard: 15 min), Adapter, Meldungen und Hilfe-Knöpfe.
- **Sprachen:** Englisch (`en_gb`, Pflicht als Rückfall) und Deutsch. Die Anleitung liegt in `resources/help_de.txt`
  bzw. `help_en.txt`.

## Bauen

```powershell
powershell -File linux/build-kodi-addon.ps1                 # alle drei Prozessoren
powershell -File linux/build-kodi-addon.ps1 -Rids linux-x64  # nur einer (schneller)
```

Das Ergebnis liegt in `out/kodi/`:

| Datei | Größe | Prozessoren |
|---|---|---|
| `service.nconnect-0.1.0.zip` | ca. 18 MB | universell: x64 + arm64 + arm |
| `service.nconnect-0.1.0-linux-x64.zip` | ca. 6,5 MB | nur x64 |
| `service.nconnect-0.1.0-linux-arm64.zip` | ca. 6,2 MB | nur arm64 |
| `service.nconnect-0.1.0-linux-arm.zip` | ca. 5,4 MB | nur arm |

Installation: Kodi → Add-ons → Aus ZIP-Datei installieren (vorher „Unbekannte Quellen“ erlauben).
Raspberry Pi 4/5 mit LibreELEC 12 = arm64, ältere Pi = arm, PC = x64.

## Test in der VM (ohne Admin-Rechte, ohne Fenster)

- **QEMU:** Den Installer mit 7-Zip nach `vm/qemu/` entpacken; eine Installation ist nicht nötig. WHPX ist auf dem
  Rechner nicht aktiv, deshalb läuft QEMU mit TCG (langsam, Start ca. 2–3 min).
- **Image:** `vm/LibreELEC-Generic-legacy.x86_64-12.2.1.img`, die Kopie `le-test.img` auf 4 GB vergrößert.
- **Boot:**
  - Direkt mit `-kernel boot/KERNEL` (aus der FAT-Partition extrahiert) und
    `-append "boot=UUID=0111-0850 disk=UUID=d0d23ada-… portable ssh console=ttyS0"`. So ist SSH an, ohne das Image zu
    verändern.
  - Netzwerk: `-nic user,hostfwd=tcp:127.0.0.1:2222-:22`. Zugang über plink/pscp mit Passwort `libreelec`.
  - Bildschirm: `-display none` und `-monitor telnet:127.0.0.1:4444`; Bildschirmfotos mit `screendump`.
- **Kodi-Oberfläche in der VM: nicht möglich ohne Host-OpenGL.** Mesa hat dort keinen Software-Renderer, und
  virtio-vga ohne virgl stürzt ab. Dafür bräuchte es ein sichtbares QEMU-Fenster (`-device virtio-vga-gl -display
  sdl,gl=on`). Im Test war Kodi deshalb per `/storage/.config/system.d/kodi.service` stillgelegt.

Geprüft und bestanden:

1. `nconnect --selftest`: uinput ✓, BlueZ über D-Bus ✓, debugfs ✓, Einstellungen ✓. Adapter ✗, weil die VM keinen hat.
2. `--test-pad` und ein evdev-Prüfskript (wie ein Spiel):
   - Name, Kennung, 11 Tasten, 8 Achsen, Wertebereiche, Rauschen und Totzone stimmen; der Kernel legt zusätzlich
     `js0` an.
   - Die Eingaben kommen an.
   - Ein `FF_RUMBLE`-Upload liefert ID 0; Abspielen kommt als Vibration 192/64 an, Stopp als 0/0, Erase beim Schließen.
3. Add-on mit Kodi-Nachbau (xbmc-Module) gegen das echte Programm:
   - `settings.json` wird geschrieben, und eine Einstellungsänderung lädt der Dienst live neu.
   - Die Kodi-Belegung wird installiert.
   - Befehle kommen an.
   - Nach `kill -9` startet das Programm automatisch neu.
   - Das Menü zeigt „startet …“ bzw. „Kein Bluetooth-Adapter“; Diagnose und Hilfe funktionieren auf Deutsch und
     Englisch.
   - Beim Schließen von Kodi endet das Programm sauber (`state: stopped`).

## Offen

- **Test mit echter Hardware:** Raspberry Pi 4/5 mit LibreELEC und echten Controllern. Zu prüfen sind
  Verbindungsaufbau, Kopplung 0x15 und Wiederverbinden per Tastendruck, Latenz, Joy-Con-Paar und Vibration. Auch die
  Frage aus Android gehört dazu: Wirbt der Controller nach der Kopplung für die öffentliche Adapter-Adresse? Unter
  BlueZ ist LE-Privacy normalerweise aus, das sollte also passen.
- **Kodi-Oberfläche testen:** Belegung, Meldungen, Einstellungsdialog und Menü-Optik. Das geht auf echter Hardware oder
  in einer VM mit GL-Fenster.
- **Optional `AcquireNotify`** (Socket statt D-Bus-Eigenschaften) für noch weniger Last bei 8 Controllern.
- **Optional DSU-Server** (Gyro für Dolphin/Cemu) wie unter Windows.
- **Add-on-Repository** (`repository.nconnect`) für automatische Updates. Den Release-Workflow um die Linux-ZIPs
  erweitern.
- **Weitere Sprachen** (die Windows-Version hat 12).

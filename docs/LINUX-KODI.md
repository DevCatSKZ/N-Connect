# N-Connect für Linux / LibreELEC (Kodi-Add-ons)

Stand: 17.10.2026 – `service.nconnect` 0.4.4 (Programm `nconnect` 0.4.4), `plugin.nconnect.status` 1.1.1,
`script.nconnect.bundle` 1.0.2, `repository.devcatskz` 1.0.2.

**Auf echter Hardware geprüft:** Raspberry Pi 4 mit LibreELEC 12.2.1 (Kodi 21.3, eingebautes Bluetooth).
- Pro Controller 2: SYNC-Kopplung und Wiederverbinden per Tastendruck ohne SYNC funktionieren, ca. 89 Eingaben/s.
- Joy-Con 2 (L) und (R): Kopplung funktioniert; die beiden werden automatisch zu einem Spieler zusammengefasst.
- Kodi erkennt das virtuelle Pad als „Microsoft X-Box 360 pad“.

**Nicht auf echter Hardware geprüft:** alle übrigen Geräte (Pi 2/3 mit `linux-arm`, Pi 5, 64-Bit-Boxen, PCs),
Batocera, Desktop-Linux, die Kopplung von Xbox-, PlayStation-, Switch-1- und Wii-Controllern, Vibration im Spiel,
Kodi unter Android (Begleitmodus) und die Änderungen von 0.4.4 (siehe [Offen](#offen)).

## Überblick

```
Switch-2-Controller ──BLE──► Kernel (L2CAP/ATT, eigener Client) ──► nconnect (Dienst, .NET 10, root)
Xbox/PlayStation/Switch 1/Wii ──► BlueZ (bluetoothd, D-Bus) ◄──────── Suche, Koppeln, Status
                                                              │  Switch2Pro.Protocol (wie Windows/Android)
                                                              ▼
                                         /dev/uinput → „Microsoft X-Box 360 pad“ (045E:028E) je Spieler
                                                              ▼
                                         Kodi (peripheral.joystick) · RetroArch · SDL-Spiele · DSU (Port 26760)
```

| Teil | Ort | Aufgabe |
|---|---|---|
| `nconnect` | [linux/NConnect.Linux/](../linux/NConnect.Linux/) | Programm ohne Oberfläche: Suche, Verbindung, Belegung, virtuelle Controller, DSU, status.json |
| `service.nconnect` | [linux/kodi/service.nconnect/](../linux/kodi/service.nconnect/) | Startet/überwacht `nconnect`, Einstellungen, Meldungen, Menü, Widget, Diagnose; auf Windows/Android/macOS Begleitmodus |
| `plugin.nconnect.status` | [linux/kodi/plugin.nconnect.status/](../linux/kodi/plugin.nconnect.status/) | Ältere Widget-Quelle für Skins – leitet nur an `plugin.py` von `service.nconnect` weiter |
| `script.nconnect.bundle` | [linux/kodi/script.nconnect.bundle/](../linux/kodi/script.nconnect.bundle/) | Offline-Installer: bringt die aktuelle `service.nconnect`- und Plugin-ZIP mit, richtet in Aeon Nox Silvo Menüpunkt und Widget ein |
| `repository.devcatskz` | [linux/kodi/repository.devcatskz/](../linux/kodi/repository.devcatskz/) | „DevCatSKZ Repository“ für automatische Updates (Inhalt: github.com/DevCatSKZ/kodi-repo) |
| Index-Werkzeug | [linux/kodi/repo-tools/build_repo.py](../linux/kodi/repo-tools/build_repo.py) | addons.xml, .md5, .sha256, index.html im kodi-repo (wird dorthin kopiert) |
| Sprachen | [linux/kodi/i18n/](../linux/kodi/i18n/) | erzeugt die `strings.po` aller Sprachen (`service.nconnect`, `script.nconnect.bundle`) |
| Tests | [tests/NConnect.Linux.Tests/](../tests/NConnect.Linux.Tests/), [linux/kodi/tests/](../linux/kodi/tests/) | Dienst- und Add-on-Logik ohne Gerät (siehe [Tests](#tests)) |
| Build-Skript | [linux/build-kodi-addon.ps1](../linux/build-kodi-addon.ps1) | Veröffentlicht linux-x64/arm64/arm und packt alle Add-on-ZIPs |
| CI | [.github/workflows/kodi.yml](../.github/workflows/kodi.yml) | Tests und Artefakt bei jedem Lauf; bei Tags Release-Anhang (das kodi-repo pflegt sie nicht) |

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
- **Geteilte WLAN/BT-Antenne (Pi, Mini-PCs):** Während des Verbindungsaufbaus wird die Suche angehalten, sonst stirbt
  der frische Link (HCI 0x3e, errno 38). Bei errno 38 und aktivem WLAN kommt einmal der Hinweis `wifi-hint`.
- **Joystick-Schnittstelle:** Kodi auf dem Pi nutzt die Schnittstelle „linux“, und dafür hat Kodi die
  Xbox-360-Belegung bereits. Die mitgelieferte udev-Belegung schadet nicht.
- **Texte nach Installation per Dateikopie:** Kopiert man das Add-on von Hand statt über die ZIP-Installation, behält
  Kodi einen leeren Text-Zwischenspeicher, bis Kodi neu startet. Bei der normalen ZIP-Installation tritt das nicht auf.
- **Schriften:** Der Aeon-Nox-Skin kann „→ ✓ ✗“ nicht darstellen. Die Texte nutzen deshalb „>“, „OK“ und „--“.

## Programm `nconnect`

- **Bluetooth:** [Linux.Bluetooth](https://github.com/SuessLabs/Linux.Bluetooth) 6.0.0-pre3 über D-Bus. Bewusst
  nicht 5.x: Das hängt an Tmds.DBus 0.20 mit CVE-2026-39959. Tmds.DBus ist explizit auf 0.95.1 festgelegt.
  - Jedes `Device`- und `Adapter`-Objekt der Bibliothek meldet beim Anlegen einen eigenen D-Bus-Beobachter an und gibt
    ihn erst mit `Dispose` frei. Deshalb nutzen Abfragen schlichte Proxys
    ([BlueZ.cs](../linux/NConnect.Linux/Ble/BlueZ.cs)), nicht gebrauchte Adapter-Objekte werden sofort freigegeben,
    das Sitzungs-Adapter-Objekt am Sitzungsende.
- **Suche** ([Switch2Scanner.cs](../linux/NConnect.Linux/Ble/Switch2Scanner.cs)): Discovery-Filter
  `Transport=le` bzw. `auto`, wenn andere Controller automatisch gekoppelt werden sollen, dazu `DuplicateData=true`.
  BlueZ meldet neue Geräte über `InterfacesAdded`, bekannte über Eigenschaftsänderungen (RSSI/ManufacturerData) –
  beides wird ausgewertet, damit ein gekoppelter Controller nach einem Tastendruck sofort erkannt wird. Je Gerät gibt
  es genau einen Beobachter; `InterfacesRemoved` meldet ihn wieder ab. Beendet ein anderes Programm (z. B. Kodis
  Bluetooth-Seite) die Suche, startet der Dienst sie nach spätestens 3 s neu.
- **Gerät** ([Switch2Device.cs](../linux/NConnect.Linux/Ble/Switch2Device.cs)): Portierung der Android-Fassung mit
  demselben Ablauf (Funktionen, Kalibrierung, Seriennummer, Kopplung 0x15, Wächter, Vibration). MTU 512 handelt der
  eigene ATT-Client aus; Befehle und Vibration gehen als „Write Command“ (ohne Antwort). Ein Abbruch während der
  Einrichtung wird nach dem Anlegen des Spielers noch einmal geprüft – kein Geist-Controller.
- **Andere Controller** ([GamepadManager.cs](../linux/NConnect.Linux/Ble/GamepadManager.cs)): Die Kernel-Treiber
  (xpad, hid-microsoft, hid-playstation, hid-sony, hid-nintendo, hid-wiimote, hid-steam) übernehmen Eingaben, Vibration
  und Akku; N-Connect koppelt nur.
  - Erkennen an Geräteklasse (Peripherie/Joystick/Gamepad), LE-Erscheinungsbild 0x03C3/0x03C4 oder bekanntem Namen.
  - Koppeln, als vertrauenswürdig markieren, verbinden – danach verbindet sich das Gamepad per Tastendruck selbst.
  - [PairingAgent.cs](../linux/NConnect.Linux/Ble/PairingAgent.cs): „NoInputNoOutput“ mit eigener D-Bus-Verbindung,
    nicht als Standard-Agent angemeldet. Koppelt gerade die LibreELEC-Seite („InProgress“/„AlreadyExists“), hält sich
    N-Connect heraus. Nicht-Gamepads und der Adapter-Strom werden nie angefasst.
  - Wii-Fernbedienung / Wii U Pro: Die feste Wii-PIN setzt BlueZ selbst (Modul „wiimote“, in LibreELEC enthalten).
- **Virtueller Controller** ([UInputPad.cs](../linux/NConnect.Linux/Output/UInputPad.cs)):
  - Name, Kennung und Belegung wie beim Kernel-Treiber xpad: „Microsoft X-Box 360 pad“, USB 045E:028E, Version 0x0114.
  - 11 Tasten, Sticks ±32767 (Y nach unten positiv), Trigger 0…255, Steuerkreuz als HAT0X/Y.
  - Spieler werden über `phys` unterschieden (`nconnect/playerN`), weil Kodi die Belegung über den Namen findet.
  - Vibration über `FF_RUMBLE`: Upload/Erase per `UI_BEGIN/END_FF_*`, abgespielt mit `EV_FF`; ioctl-Größen für 64 und
    32 Bit. Der Leser wartet mit `poll` und liest wie die Schreiber unter einer Sperre mit Prüfung auf Dispose – nie
    in einen geschlossenen (und womöglich neu vergebenen) fd.
- **ATT-Socket** ([AttClient.cs](../linux/NConnect.Linux/Ble/AttClient.cs)): Gesendet wird unter einer Sperre mit
  Prüfung auf „getrennt“; geschlossen wird der fd erst, wenn Besitzer und Leser fertig sind (Nutzungszähler), und zwar
  unter derselben Sperre. Beim Trennen läuft `shutdown` bewusst ohne die Sperre: Ein Sender, der bei vollem
  Sendepuffer in `write` hängt, kehrt sofort zurück, statt das Trennen bis zum Supervision-Timeout aufzuhalten.
- **DSU/Cemuhook-Server** ([DsuServer.cs](../src/Switch2Pro.Protocol/DsuServer.cs), derselbe wie unter Windows und Android): Port 26760,
  standardmäßig nur lokal; `DsuNetwork` öffnet ihn fürs Heimnetz. Ist der Port beim Start belegt (meist ein gerade
  endendes N-Connect), versucht der Daemon es nach 2, 5, 10, 20 und 30 s erneut.
- **Feste Spielerplätze** (`slots.json`), **bekannte Controller** (`known.json`, für das ausgegraute Widget),
  **Akku-Warnung** (`battery-low` bei 20 % und 10 %), **Trennen nach Inaktivität** (`InactivityMinutes`), nach einem
  Trennen von Hand werden Controller 15 s ignoriert; fällt der Adapter weg, beginnt eine neue Sitzung.
- **Einstellungen:** dieselbe `settings.json`/`Settings`-Klasse wie unter Windows und Android, dazu Linux-eigene
  Schlüssel (`AutoPairGamepads`, `BackgroundScan`, `BatteryWarning`, `DsuNetwork`). Die Datei wird überwacht und bei
  Änderung neu geladen.
- **Aufrufe:** `nconnect [--data DIR] [--adapter hci0] [--kodi]`, `--selftest` (JSON-Diagnose: uinput, BlueZ, Adapter,
  Bluetooth LE, Einstellungen), `--test-pad [s]` (Testcontroller ohne Bluetooth, gibt Vibration aus), `--version`.
  - `--kodi` (setzt das Add-on): Endet Kodi – auch durch einen Absturz –, endet der Daemon mit: bei geschlossenem
    stdin oder wenn sich der Elternprozess ändert (`getppid`, alle 2 s). Ohne `--kodi` (systemd, von Hand) läuft er
    ohne stdin weiter.
- **Veröffentlichung:** self-contained Single-File, komprimiert, Trim-Modus `partial`. Eigene Bibliotheken, Linux.Bluetooth
  und Tmds.DBus bleiben vollständig erhalten, weil sie Reflection nutzen. `InvariantGlobalization`, weil LibreELEC kein
  ICU hat. Größe ca. 13,8 MB (x64), 13,2 MB (arm64), 8,8 MB (arm).

### Schnittstelle zum Add-on

- **Datenordner** (`--data`, im Add-on `userdata/addon_data/service.nconnect/`): `settings.json`, `status.json`,
  `slots.json`, `known.json`, `nconnect.log` (ab 512 KB nach `.1`), `nconnect.pid` (vom Add-on), `cache/` (Entpackordner
  der .NET-Laufzeit).
- **status.json** (alle 5 s und bei Änderungen). Vertrag für alle Schreiber (Linux-Daemon, Windows- und Android-App):
  - `state`: `running` | `starting` | `stopped`. Der Daemon schreibt `starting`, solange Bluetooth noch nicht geprüft ist.
  - `platform`: `linux` | `windows` | `android`. Fehlt das Feld (ältere Schreiber), entscheidet das Add-on über Kodis
    Plattform.
  - `bluetooth`: genau einer von `on` | `off` | `missing` | `no_le`. `no_le` = Adapter ohne Bluetooth LE (BlueZ meldet
    in `Roles` kein `central`): Switch-2-Controller gehen nicht, klassische schon. `off` auch, wenn bluetoothd nicht läuft
    (LibreELEC: Bluetooth in den Einstellungen aus).
  - Außerdem `version`, `adapter`, `scanning`, `connecting`, `players` (mit `devices`: Adresse, Art, Akku, Seriennummer,
    Firmware, Rate), `known`, `elsewhere` (mit anderem Gerät gekoppelt), `gamepads`, `autoPairGamepads`, `backgroundScan`.
- **Ereignisse** als JSON-Zeilen auf stdout: `connecting`, `connected`, `paired`, `disconnected`, `disconnected-all`,
  `failed`, `elsewhere`, `bluetooth` (Zustand wie oben), `error`, `button` (Capture/C/GL/GR), `battery-low`, `wifi-hint`,
  `gamepad-paired`, `gamepad-pair-failed`, `gamepad-connected`, `gamepad-disconnected`.
- **Befehle** über stdin, je Zeile einer: `reload`, `disconnect <Spieler 1…8>`, `disconnect-all`, `status`, `scan`
  (2 Minuten suchen, wenn die Hintergrundsuche aus ist), `gamepad-disconnect <Adresse>`, `gamepad-forget <Adresse>`.
  Jeder Befehl ist einzeln abgesichert – ein Fehler (z. B. ein D-Bus-Fehler beim Trennen) beendet den Befehlskanal nicht.

## Kodi-Add-on `service.nconnect`

- **Einstiegspunkte** (`addon.xml`, `<platform>all</platform>`):
  - `xbmc.service` → `service.py` (startet mit Kodi),
  - `xbmc.python.script` → `default.py` (Programme → N-Connect; Argumente `help`, `selftest`, `log`, `disconnect-all`),
  - `xbmc.python.pluginsource` → `plugin.py` (`plugin://service.nconnect/` als Home-Widget).
- **`service.py`:**
  - Wählt das Programm nach der Bitbreite des Python-Prozesses (`struct.calcsize('P')`), nicht nach dem Kernel: ein
    64-Bit-Kernel mit 32-Bit-System (z. B. Raspberry Pi OS 32 Bit) bekommt `linux-arm`. Scheitert der Start (OSError,
    z. B. „Exec format error“), wird die andere ARM-Variante versucht; passt keine, erscheint Meldung 30022 statt eines
    Absturzes. Das Ausführungsrecht (geht beim Entpacken verloren) wird abgesichert gesetzt.
  - Vor dem Start: `modprobe uinput`; ein N-Connect aus einer früheren, abgestürzten Kodi-Sitzung wird über
    `nconnect.pid` erkannt (Prüfung von `/proc/<pid>/cmdline`) und beendet.
  - Startet `nconnect --data <Datenordner> --kodi [--adapter …]`, überträgt die Kodi-Einstellungen in `settings.json`
    (fremde Einträge bleiben stehen), zeigt Meldungen beim Verbinden und Trennen (abschaltbar; Fehler immer).
  - Neustart nach einem Absturz nach 3/5/10/30/60 s. Ein gewollter Neustart (Menü „Neu starten“, Adapter geändert)
    wartet, bis das alte Programm beendet ist – sonst kämpften kurz zwei Programme um Controller und DSU-Port.
  - Befehle aus Menü und Widget kommen über `NotifyAll(service.nconnect,<befehl>)` → `onNotification`
    (`disconnect-<n>`, `disconnect-all`, `gamepad-disconnect-<adresse>`, `gamepad-forget-<adresse>`, `scan`, `status`,
    `restart`).
  - Lädt Widgets neu, wenn sich Sichtbares ändert (Home-Eigenschaft `NConnect.Reload`, siehe `plugin.py`).
- **Kodi-Belegung:** Kodi bringt für die udev-Kennung 045E:028E keine Xbox-360-Belegung mit, sondern nur für Steams
  28DE:11FF. Das Add-on kopiert deshalb [die eigene Belegung](../linux/kodi/service.nconnect/resources/buttonmaps/udev/)
  einmalig nach `addon_data/peripheral.joystick/resources/buttonmaps/xml/udev/`. Eine in Kodi angepasste Belegung wird
  nie überschrieben.
- **Menü** (`default.py`) und **Widget** (`plugin.py`): Die Controllerliste erscheint **immer** – Spieler mit Akku,
  andere Gamepads, bekannte, gerade nicht verbundene Controller (ausgegraut), „mit anderem Gerät gekoppelt“. Bei
  `bluetooth` = `off`/`missing`/`no_le` steht ein Hinweis davor, passend zum Schreiber der status.json: LibreELEC/Linux
  „LibreELEC-Einstellungen > Bluetooth“, Windows „Bluetooth in den Windows-Einstellungen einschalten“, Android
  „Bluetooth in Android einschalten“, `no_le` „Adapter mit Bluetooth LE (ab 4.0) nötig“. Jeder Klick im Widget öffnet
  das Hauptmenü. Im Menü außerdem Trennen/Details, „Jetzt suchen“, „Alle trennen“, Anleitung, Diagnose (`--selftest`),
  Protokoll und Einstellungen.
- **Einstellungen:** Tastenbelegung nach Position (Xbox) oder wie beschriftet, Stick-Totzone, Joy-Con zusammenfassen,
  Gyro-Stick samt Tempo und Umkehrung, DSU (lokal/Heimnetz), Vibration samt Stärke, Koppeln, Hintergrundsuche, andere
  Controller automatisch koppeln, Spieler-LEDs, Verbinden-Klick, Trennen bei Nichtbenutzung, Adapter, Meldungen,
  Akku-Warnung, Kodi-Aktionen für Capture/C/GL/GR und Hilfe-Knöpfe.
- **Sprachen:** 12 Sprachen wie unter Windows (de, en, es, fr, it, pt, nl, pl, ru, ja, zh, ko; dazu pt_BR mit den
  pt-Texten). Englisch (`en_gb`, Pflicht als Rückfall) und Deutsch werden von Hand gepflegt, die übrigen erzeugt
  [generate.py](../linux/kodi/i18n/generate.py) aus `tr_a.py`/`tr_b.py`; neue Texte kommen in `EXTRA` (und werden
  dann auch in en/de ergänzt). Die Anleitung gibt es in allen 12 Sprachen: `resources/help_<sprache>.txt` (LibreELEC)
  und `resources/help_companion_<sprache>.txt` (Begleitmodus). Die Dialoge des Bundles stehen in
  [tr_bundle.py](../linux/kodi/i18n/tr_bundle.py) (IDs 32000 ff., alle 13 `strings.po` werden daraus erzeugt);
  Zusammenfassung und Beschreibung aller drei Add-ons stehen in ihrer `addon.xml` in allen 12 Sprachen.

## Andere Kodi-Plattformen

- **Batocera / sonstiges Linux:** wie LibreELEC – das Add-on enthält das Programm für x86_64, aarch64 und arm und läuft
  dort, wenn alles als root läuft und BlueZ/uinput vorhanden sind. Auf Batocera koppelt bereits das System
  Bluetooth-Controller; N-Connect ist für Switch-2-Controller gedacht. Die Bluetooth-Einstellungen liegen dort im
  Systemmenü, nicht bei `service.libreelec.settings` (das Menü prüft, ob das Add-on existiert). **Ungetestet.**
- **Desktop-Linux** (Ubuntu & Co., Kodi als normaler Benutzer): Das Programm braucht root für HCI-Rohzugriff und
  /dev/uinput – als Benutzer gestartet schlägt es fehl (der Selbsttest zeigt das an). Dafür fehlen noch ein
  systemd-Dienst bzw. eine udev-Regel. **Ungetestet.**
- **Begleitmodus** (kein Linux-Dienst; `common.has_daemon()` ist falsch – auf Android meldet Python „Linux“, darum wird
  Kodi gefragt): Das Add-on zeigt nur an, was eine N-Connect-App in die status.json schreibt. Trennen, Koppeln,
  Selbsttest und Dienst-Protokoll gibt es dort nicht. Die Einstellungen der Kategorien Controller, Emulatoren,
  Vibration, Verbindung und Kodi sind gesperrt (InfoBool-Bedingung `System.Platform.Linux + !System.Platform.Android`),
  je Kategorie steht ein Hinweis-Knopf „Diese Einstellungen gelten nur unter LibreELEC/Linux“, der die
  Begleitmodus-Anleitung öffnet.
  - **Windows:** Die installierte N-Connect-App verbindet die Controller (als XInput), und
    [KodiStatusWriter.cs](../src/Switch2Pro.Bridge/KodiStatusWriter.cs) schreibt die status.json in Kodis
    `userdata/addon_data/service.nconnect/` (normale Installation und Microsoft-Store-Variante, siehe
    [KodiIntegration.cs](../src/Switch2Pro.Bridge/KodiIntegration.cs)). Wird Kodi erst danach installiert, die App
    einmal neu starten.
  - **Android:** Die App darf nicht in Kodis Datenordner schreiben (Scoped Storage). Sie legt die Datei ab N-Connect
    für Android **0.9.1** über MediaStore in `/sdcard/Documents/N-Connect/status.json` ab (Android 8/9: externer
    App-Ordner); das Add-on liest dort mit
    ([KodiStatusWriter.cs](../android/NConnect.Android/Kodi/KodiStatusWriter.cs)). Ob Kodi ab Android 11 die Datei ohne
    „Zugriff auf alle Dateien“ lesen darf, ist **ungeprüft**.
  - **macOS** (und iOS/tvOS): Es gibt keine N-Connect-App – das Add-on zeigt „Nur Anzeige – N-Connect gibt es für macOS
    nicht“.

## Weitere Add-ons

- **`plugin.nconnect.status`:** frühere eigenständige Widget-Quelle; seit 1.1.0 nur noch eine Weiterleitung an
  `plugin.py` von `service.nconnect`, damit Skins mit dem alten Pfad `plugin://plugin.nconnect.status/` weiter
  funktionieren. Braucht `service.nconnect` ≥ 0.3.4.
- **`script.nconnect.bundle`:** installiert beim ersten Kodi-Start (und auf Aufruf) fehlende Add-ons aus
  `resources/data/<id>-<version>.zip` und trägt in Aeon Nox Silvo den Menüpunkt samt Widget ein. Die ZIPs legt das
  Build-Skript beim Packen hinein – immer die eben gebauten. Bei jeder neuen `service.nconnect`- oder Plugin-Version
  muss auch die Bundle-Version steigen; das Build-Skript bricht sonst beim Ablegen im Repository ab.

## Bauen

```powershell
powershell -File linux/build-kodi-addon.ps1                          # alle drei Prozessoren → out/kodi/
powershell -File linux/build-kodi-addon.ps1 -Rids linux-x64           # nur einer (schneller)
powershell -File linux/build-kodi-addon.ps1 -Out C:\Temp\kodi        # anderer Ausgabeordner
powershell -File linux/build-kodi-addon.ps1 -Repo                     # zusätzlich ins kodi-repo neben N-Connect
powershell -File linux/build-kodi-addon.ps1 -Repo -NoBuild -RepoDir … # nur ablegen, vorhandene ZIPs aus -Out
```

Vor einem neuen Lauf entfernt das Skript im Ausgabeordner nur seine eigenen ZIPs (`N-Connect-Kodi-LibreELEC-*`,
`plugin.nconnect.status-*`, `script.nconnect.bundle-*`, `repository.devcatskz-*`) – andere Dateien in `-Out` bleiben.

Ergebnis (Größen mit allen drei Prozessoren):

| Datei | Größe | Inhalt |
|---|---|---|
| `N-Connect-Kodi-LibreELEC-<V>-alle-Geraete.zip` | ca. 18 MB | `service.nconnect`, universell: x64 + arm64 + arm (empfohlen) |
| `N-Connect-Kodi-LibreELEC-<V>-RPi4-RPi5-arm64.zip` | ca. 6,4 MB | nur arm64 (Pi 4/5 mit LibreELEC 12, 64-Bit-Boxen) |
| `N-Connect-Kodi-LibreELEC-<V>-RPi2-RPi3-arm.zip` | ca. 5,6 MB | nur arm (Pi 2/3, 32 Bit) |
| `N-Connect-Kodi-LibreELEC-<V>-PC-x86_64.zip` | ca. 6,7 MB | nur x64 (PC, LibreELEC Generic) |
| `plugin.nconnect.status-<V>.zip` | < 0,1 MB | Widget-Weiterleitung |
| `script.nconnect.bundle-<V>.zip` | ca. 18 MB | Bundle mit der universellen `service.nconnect`- und der Plugin-ZIP |
| `repository.devcatskz-<V>.zip` | < 0,1 MB | Repository-Add-on |

Die Versionen stehen in `linux/kodi/<id>/addon.xml` (unabhängig von der Windows-Version); die Programmversion wird beim
Bauen aus der `service.nconnect`-Version gesetzt (`-p:Version`, im csproj gleich gehalten). Logo (Installer-Grafik),
Lizenz (`LICENSE.txt`) und die Drittanbieter-Hinweise (`THIRD-PARTY-NOTICES.md` aus dem Repo-Wurzelordner: .NET-Laufzeit,
Linux.Bluetooth, Tmds.DBus …) kommen beim Packen in jede ZIP außer dem Repository-Add-on (das enthält keinen fremden
Code); `__pycache__`/`.pyc` nie.

Installation von Hand: Kodi → Add-ons → Aus ZIP-Datei installieren (vorher „Unbekannte Quellen“ erlauben).
Kodi aktualisiert nur Installationen aus dem Repository und nur, wenn die Version steigt.

## DevCatSKZ Repository und Release

- **Öffentliches Repo:** [github.com/DevCatSKZ/kodi-repo](https://github.com/DevCatSKZ/kodi-repo), Aufbau
  `<id>/<id>-<version>.zip`, `addons.xml`, `addons.xml.md5`, je ZIP `.sha256`, `repository.devcatskz.zip` (fester Name)
  und `downloads/` (ZIPs je Gerät ohne Versionsnummer – darauf verweisen README und die Windows-/Android-App).
- **Index:** `tools/build_repo.py` (Quelle: `linux/kodi/repo-tools/`, wird bei `-Repo` hineinkopiert) erzeugt
  addons.xml, Prüfsummen und `index.html` (Wurzel und je Add-on-Ordner); die zwei neuesten Versionen je Add-on bleiben
  erhalten (`KEEP = 2`).
- **Dateimanager-Quelle:** Das Repo ist zusätzlich über GitHub Pages erreichbar (`https://devcatskz.github.io/kodi-repo/`),
  dort liefert `index.html` die Verzeichnisliste, die Kodis HTTP-Quelle braucht. `raw.githubusercontent.com` liefert
  keine Verzeichnislisten – als Quelle im Dateimanager geht das nicht (das Repository-Add-on selbst lädt von dort).
- **Schutz:** `-Repo` überschreibt nie eine schon abgelegte Version (Hinweis „schon veröffentlicht“, für ein Update die
  Version erhöhen) und bricht ab, wenn `service.nconnect` oder das Plugin neu sind, das Bundle aber nicht. Die
  Download-ZIPs werden nur zusammen mit einer neuen `service.nconnect`-Version ersetzt.
- **CI** ([kodi.yml](../.github/workflows/kodi.yml)):
  - Jeder Push auf `linux/**`, `src/Switch2Pro.Protocol/**`, `tests/NConnect.Linux.Tests/**`,
    `THIRD-PARTY-NOTICES.md` oder die Workflow-Datei führt die [Tests](#tests) aus, prüft, dass `generate.py` keine
    Sprachdatei ändert, und erzeugt das Artefakt „Kodi-Plugin“ (alle ZIPs).
  - Ein Tag `v…` hängt zusätzlich die `N-Connect-Kodi-*.zip` ans GitHub-Release.
  - Ins kodi-repo schreibt die CI bewusst **nicht** (kein automatischer Zugang; Deploy-Keys sind in der Organisation
    DevCatSKZ ohnehin abgeschaltet).
- **Release von Hand** (nach dem Tag, aus einer Arbeitskopie von N-Connect auf dem Release-Stand):
  1. kodi-repo aktuell holen: `git -C <kodi-repo> pull`.
  2. `powershell -File linux/build-kodi-addon.ps1 -Repo -RepoDir <kodi-repo>` – baut alle ZIPs, legt neue Versionen
     samt `downloads/` und `repository.devcatskz.zip` ab und ruft dort das Index-Werkzeug `tools/build_repo.py` auf
     (addons.xml, .md5, .sha256, index.html; von Hand: `python tools/build_repo.py` im kodi-repo). Sollen dort
     byteweise dieselben ZIPs wie am GitHub-Release liegen: das CI-Artefakt „Kodi-Plugin“ in einen Ordner entpacken
     und `-Repo -NoBuild -Out <Ordner> -RepoDir <kodi-repo>` nehmen.
  3. In `<kodi-repo>` mit `git status` prüfen (nur neue Versionen, `addons.xml*`, `index.html`, `downloads/`),
     committen als `devcatskz <devcatskz@gmail.com>` und pushen. Kodi holt die neuen Versionen dann selbst.
  - Liegt eine Version schon im kodi-repo, bleibt sie unverändert („schon veröffentlicht“) – für ein Update die
    Version in der `addon.xml` erhöhen.

## Tests

Alles, was ohne Gerät prüfbar ist; die CI führt beides vor dem Bauen aus (unter Windows – nichts davon braucht
Bluetooth, uinput oder libc).

- **Dienst** ([tests/NConnect.Linux.Tests/](../tests/NConnect.Linux.Tests/), xUnit):
  `dotnet test tests/NConnect.Linux.Tests -c Release`
  - Befehlskanal: Ein fehlgeschlagener Befehl (auch eine D-Bus-Zeitüberschreitung) beendet ihn nicht; echte Befehle
    ohne Adapter und unsinnige Zeilen laufen ins Leere, das Ende von stdin wird erkannt.
  - status.json: `starting` ohne `bluetooth` vor der ersten Prüfung, `platform: linux`, jeder der vier
    Bluetooth-Werte, `stopped` beim Ende; „bluetoothd läuft nicht“ gilt als `off`.
  - Verbindungsabbruch: Lost vor dem Zuordnen zum Spieler (kein Geist-Controller), Lost danach, ohne Abbruch bleibt
    der Controller. Virtuelle Controller legt der Dienst in den Tests nie an (`CreatePad`).
- **Add-ons** ([linux/kodi/tests/](../linux/kodi/tests/), unittest mit nachgebauten Kodi-Modulen in `kodi_stubs.py`):
  `py -3 -m unittest discover -s linux/kodi/tests`
  - Programmwahl nach Bitbreite, Rückfall beim Start, Meldung 30022, Warten beim Beenden, PID-Datei samt
    `/proc/<pid>/cmdline`-Prüfung.
  - Hinweise je Plattform und Bluetooth-Zustand, Widget-Liste, Lesen der status.json (Alter, `starting`, Android-Pfade).
  - Bundle: neueste mitgelieferte ZIP, Installation, Dialoge und Skin-Einträge in Kodis Sprache.
  - Dateien: `addon.xml`/`settings.xml` wohlgeformt, Beschreibungen in allen 12 Sprachen, alle `strings.po` vollständig
    (samt Platzhaltern), jede benutzte String-ID definiert, `generate.py` aktuell, Versionen (csproj =
    `service.nconnect`), Index-Werkzeug `build_repo.py` (KEEP, Prüfsummen, `index.html`).

## Hintergrundsuche

- **Einstellung „Ständig nach Controllern suchen“** (`BackgroundScan`, Standard: an).
- **Wenn aus:** Gesucht wird nur nach dem Menüpunkt „Jetzt nach Controllern suchen“, dann 2 Minuten lang (Befehl `scan`).
- **Gekoppelte klassische Gamepads** verbinden sich trotzdem selbst. **Switch-2-Controller** brauchen zum Wiederverbinden
  die Suche.

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

Damals (0.1/0.2) geprüft und bestanden:

1. `nconnect --selftest`: uinput, BlueZ über D-Bus und Einstellungen OK; Adapter fehlt, weil die VM keinen hat.
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

- **Hardwaretest der Änderungen von 0.4.4** auf dem Pi 4 (ohne Gerät nur gebaut und mit den [Tests](#tests) geprüft):
  `--kodi` (Ende mit Kodi, auch nach `kill -9` von Kodi), PID-Datei und Beenden eines Rests, Neustart über das Menü
  (DSU-Port danach wieder offen), `no_le`-Erkennung über `Roles` (ältere BlueZ ohne `Roles` gelten als LE-fähig),
  poll-Leser des virtuellen Controllers samt Vibration, Trennen während laufender Vibration (ATT-`shutdown` ohne
  Sperre), Speicherverlauf bei langer Laufzeit (vorher/nachher).
- **Ungetestete Geräte und Controller:** Pi 2/3 (`linux-arm`), Pi 5, 64-Bit-Boxen, PCs; Kopplung von Xbox-, PlayStation-,
  Switch-1- und Wii-Controllern, Vibration im Spiel; Klone wie der ShanWan-Pro-Controller (Issue #1, nur klassisches
  Bluetooth) laufen unter Linux vermutlich über `hid-nintendo` – ungeprüft.
- **Batocera** auf echter Hardware prüfen.
- **Desktop-Linux:** systemd-Dienst oder udev-Regel, damit `nconnect` ohne root-Kodi läuft.
- **Kodi unter Android:** prüfen, ob Kodi ab Android 11 `/sdcard/Documents/N-Connect/status.json` lesen darf
  (Scoped Storage), mit N-Connect für Android 0.9.1.
- **Settings im Begleitmodus:** Die Sperre nutzt InfoBool-Bedingungen in `settings.xml`; auf Kodi für Windows/Android
  einmal ansehen, ob Sperre und Hinweis-Knopf wie gedacht erscheinen.
- **Kodi-Repository per Dateimanager:** Weg über GitHub Pages (`https://devcatskz.github.io/kodi-repo/`) einmal in Kodi
  durchspielen.

# SKZ Connect für Kodi

**Deutsch** · [English](../guide/kodi.md) · Stand: Add-on `service.nconnect` 0.4.4

Unter LibreELEC startet das Add-on mit Kodi den Dienst `nconnect`. Er verbindet Switch-2-Controller per Bluetooth LE
und legt für jeden Spieler einen virtuellen Xbox-360-Controller an, den Kodi, RetroArch und Spiele ohne Einrichtung
erkennen. Andere Bluetooth-Controller kann er automatisch koppeln. Unter Kodi für Windows und Android zeigt das Add-on
nur den Stand der dortigen SKZ-Connect-App an (Begleitmodus). Probleme und Lösungen: [Hilfe](hilfe.md#kodi).

## Systeme und Teststand

| System | Betrieb | Stand |
|---|---|---|
| LibreELEC auf Raspberry Pi 4 | selbstständig | geprüft mit LibreELEC 12.2.1 (Kodi 21.3), Pro Controller 2 und Joy-Con 2; die Änderungen aus 0.4.4 noch nicht |
| LibreELEC auf anderen Geräten (Raspberry Pi 2, 3 und 5, PCs, 64-Bit-ARM-Boxen) | selbstständig | ungetestet |
| Batocera | selbstständig | ungetestet |
| Desktop-Linux (Kodi als normaler Benutzer) | – | Der Dienst braucht root-Rechte (Bluetooth, uinput); als normaler Benutzer startet er nicht. |
| Kodi unter Windows | Begleitmodus mit SKZ Connect für Windows | ungetestet |
| Kodi unter Android | Begleitmodus mit SKZ Connect für Android ab 0.9.1 | ungetestet |
| Kodi unter macOS | nur Hinweis, es gibt keine SKZ-Connect-App | – |

Ebenfalls ungetestet: Vibration im Spiel und das Koppeln von Xbox-, PlayStation-, Switch-1- und Wii-Controllern.
Rückmeldungen als [Issue](https://github.com/DevCatSKZ/SKZ-Connect/issues) helfen.

## Voraussetzungen

- Bluetooth mit Bluetooth LE (ab 4.0). Der Raspberry Pi 4 und 5 haben es eingebaut, sonst genügt ein USB-Stick (ab
  Bluetooth 5.0 empfohlen).
- Bluetooth muss eingeschaltet sein: *LibreELEC-Einstellungen › Bluetooth*.
- Das Add-on enthält das Programm für x86_64, ARM64 und ARM (32 Bit) und wählt beim Start das passende.

## Installieren

### Über das DevCatSKZ Repository (mit Updates)

1. In Kodi *Einstellungen › System › Add-ons › Unbekannte Quellen* einschalten.
2. `repository.devcatskz.zip` auf das Gerät bringen, auf einem dieser Wege:
   - [repository.devcatskz.zip](https://github.com/DevCatSKZ/kodi-repo/raw/main/repository.devcatskz.zip) laden und
     per USB-Stick oder Netzwerkfreigabe (unter LibreELEC etwa Samba) auf das Gerät kopieren,
   - in Kodi unter *Einstellungen › Dateimanager › Quelle hinzufügen* die Adresse `https://devcatskz.github.io/kodi-repo/`
     eintragen; die ZIP liegt dort im Ordner `repository.devcatskz`,
   - aus SKZ Connect: unter Windows *Allgemein › Kodi › Repository herunterladen*, unter Android *Hilfe › Kodi:
     DevCatSKZ Repository › Herunterladen*.
3. *Add-ons* › Paket-Symbol › *Aus ZIP-Datei installieren* › die ZIP wählen.
4. *Aus Repository installieren* › *DevCatSKZ Repository* › *SKZ Connect* › *Installieren*.

Läuft Kodi auf demselben Gerät wie SKZ Connect, legt *In Kodi einrichten* (Windows, *Allgemein › Kodi*) bzw. *Direkt
einrichten* (Android, mit Shizuku oder Root) das Repository direkt ab. Dann in Kodi *Add-ons › Meine Add-ons ›
Add-on-Repository › DevCatSKZ Repository › Aktivieren* und weiter mit Schritt 4.

Kodi hält Add-ons aus dem Repository selbst aktuell, sobald eine neue Version erscheint.

### Einzelne ZIP-Datei (ohne Updates)

Am [Release](https://github.com/DevCatSKZ/SKZ-Connect/releases/latest) liegen die ZIP-Dateien mit Versionsnummer, im
[Repository](https://github.com/DevCatSKZ/kodi-repo/tree/main/downloads) unter festem Namen. Installiert werden sie
über *Aus ZIP-Datei installieren*; Kodi aktualisiert solche Installationen nicht.

| Datei | Für |
|---|---|
| `SKZ-Connect-Kodi-LibreELEC-…-alle-Geraete.zip` | alle Geräte (x86_64, ARM64, ARM), empfohlen |
| `SKZ-Connect-Kodi-LibreELEC-…-RPi4-RPi5-arm64.zip` | Raspberry Pi 4 und 5, andere 64-Bit-ARM-Geräte |
| `SKZ-Connect-Kodi-LibreELEC-…-RPi2-RPi3-arm.zip` | Raspberry Pi 2 und 3 (32 Bit) |
| `SKZ-Connect-Kodi-LibreELEC-…-PC-x86_64.zip` | PCs mit LibreELEC Generic |

Das Add-on *SKZ Connect Bundle Installer* (`script.nconnect.bundle`, im Repository) installiert SKZ Connect und das
Widget-Add-on ohne Internet und trägt im Skin Aeon Nox Silvo einen Menüpunkt mit Widget ein; danach Kodi neu starten.

## Controller verbinden

1. Die kleine runde SYNC-Taste drücken, bis die Lämpchen laufen – beim Pro Controller 2 oben neben dem USB-C-Anschluss,
   beim Joy-Con 2 an der flachen Innenseite zwischen SL und SR, beim GameCube-Controller oben.
2. Nach wenigen Sekunden erscheint „Spieler 1: … verbunden“. SKZ Connect koppelt den Controller dabei mit diesem Gerät.

- **Wiederverbinden:** eine beliebige Taste drücken, etwa HOME. Dafür muss *Einstellungen › Verbindung › Ständig nach
  Controllern suchen* an sein (Standard); sonst im Menü *Jetzt nach Controllern suchen* wählen.
- **Joy-Con:** Ein linker und ein rechter Joy-Con werden ein Controller. Für zwei Spieler *Einstellungen › Controller ›
  Zwei Joy-Con zu einem Controller zusammenfassen* ausschalten.
- **Andere Controller** (ungetestet): Ist *Einstellungen › Verbindung › Andere Controller automatisch koppeln* an
  (Standard), genügt der Kopplungsmodus – Xbox: Kopplungstaste oben halten, bis das Logo schnell blinkt;
  PlayStation 4: SHARE + PS halten; PlayStation 5: ERSTELLEN + PS halten; Switch 1: SYNC; Wii: rote SYNC-Taste;
  8BitDo laut Anleitung. Die Eingaben übernehmen die Linux-Treiber.
- **Zurück an die Switch 2:** Ein Controller merkt sich nur ein Gerät. An der Switch 2 den Controller anstecken oder
  unter *Controller › Griff/Reihenfolge ändern* SYNC drücken; für Kodi danach wieder SYNC.

## Bedienung

- **Menü:** *Add-ons › Programm-Add-ons › SKZ Connect* (die C-Taste öffnet es ebenfalls). Es zeigt Spieler mit Akku,
  andere Gamepads und bekannte, gerade nicht verbundene Controller; dazu Trennen, *Jetzt nach Controllern suchen*,
  *Alle trennen*, Anleitung, Diagnose, Protokoll und Einstellungen.
- **Home-Widget:** je nach Skin unter *Skin-Einstellungen › Home › Widgets › Add-ons › SKZ Connect*.
- **Kodi steuern:** Steuerkreuz oder Stick bewegen, untere Taste wählt aus, rechte Taste geht zurück (Belegung nach
  Position). Eigene Belegung: *Kodi-Einstellungen › System › Eingabe › Angeschlossene Controller konfigurieren*.
- **Zusatztasten:** Capture macht ein Bildschirmfoto, C öffnet das SKZ-Connect-Menü; ändern unter *Einstellungen › Kodi ›
  Controller-Tasten in Kodi*, auch für GL/GR des Pro Controller 2.
- **Spielerplätze:** Jeder Controller bekommt beim nächsten Verbinden wieder denselben Platz.
- **Akku:** Warnung bei 20 % und 10 %.
- **Gyro für Emulatoren (DSU):** *Einstellungen › Emulatoren* – Port 26760, im Emulator als Quelle „DSU“ bzw.
  „Cemuhook“ mit `127.0.0.1`. *Auch für andere Geräte im Heimnetz* öffnet den Port fürs Netz.
- Weitere Einstellungen: Tastenbelegung, Stick-Totzone, Gyro-Stick, Vibration und Stärke, Spieler-LEDs, Trennen bei
  Nichtbenutzung, Bluetooth-Adapter, Meldungen.

## Begleitmodus: Kodi unter Windows, Android und macOS

Hier verbindet nicht das Add-on die Controller, sondern die SKZ-Connect-App. Das Add-on zeigt im Menü und im Home-Widget
nur, was die App meldet. Trennen, Koppeln und Diagnose gibt es dort nicht; die Einstellungen für Controller,
Emulatoren, Vibration, Verbindung und Kodi sind gesperrt und tragen einen Hinweis. Eingestellt wird in der App.

- **Windows:** [SKZ Connect für Windows](windows.md) installieren und starten. Kodi sieht die Controller als
  Xbox-Controller; die App schreibt ihren Stand in Kodis Datenordner (auch bei Kodi aus dem Microsoft Store).
- **Android:** [SKZ Connect für Android](android.md) ab 0.9.1 installieren und starten. Die App legt ihren Stand in
  `Documents/SKZ Connect` ab, das Add-on liest dort mit. Zeigt Kodi nichts an, Kodi in den Android-Einstellungen den
  Zugriff auf Dateien erlauben; ob das ab Android 11 genügt, ist ungeprüft.
- **macOS:** Es gibt keine SKZ-Connect-App; das Add-on zeigt nur einen Hinweis.

## Diagnose

Im Menü prüft *Diagnose ausführen*, ob Bluetooth, der Adapter mit Bluetooth LE und der virtuelle Controller (uinput)
bereit sind. *Protokoll anzeigen* zeigt die letzten Einträge; die Datei liegt unter
`userdata/addon_data/service.nconnect/nconnect.log`. Beides hilft beim Melden eines Fehlers als
[Issue](https://github.com/DevCatSKZ/SKZ-Connect/issues).

Technik (Entwicklerdoku): [LINUX-KODI.md](../LINUX-KODI.md).

# Hilfe bei Problemen

**Deutsch** · [English](../guide/troubleshooting.md)

[Windows](#windows) · [Android](#android) · [Kodi](#kodi) · [Portable Version](#portable-version) ·
[Diagnose und Fehler melden](#diagnose-und-fehler-melden)

## Windows

### Der Switch-2-Controller verbindet sich nicht

- Die SYNC-Taste nur kurz drücken, nicht halten.
- Steht der Controller in den Bluetooth-Einstellungen von Windows, dort entfernen. N-Connect entfernt eine solche
  Kopplung zwar selbst, sie stört aber, solange sie besteht.
- Eine Switch 2 in der Nähe in den Ruhemodus versetzen, damit sie den Controller nicht übernimmt.
- Ohne Bluetooth LE am Adapter gehen Switch-2-Controller nur per USB-Kabel (Pro Controller 2, GameCube-Controller).

### Ein Switch-, Wii- oder NSO-Controller wird nicht gefunden

Auf der Controller-Seite *Controller suchen …* bzw. im Menü des Infobereichs *Controller koppeln …* wählen und dann
die SYNC-Taste drücken, solange gesucht wird. Die automatische Suche pausiert, während jemand spielt, und lässt einen
Controller nach einem Fehlversuch 90 Sekunden in Ruhe; das Fenster versucht es trotzdem.

### Steam oder ein Spiel zeigt einen Controller doppelt

*Allgemein › Ausgabe › Original-Controller verstecken* einschalten, eine Windows-Abfrage bestätigen und Steam einmal
neu starten. Für einen einzelnen Controller: Karte › Einstellungen › Extras › *Doppelt angezeigt?*. Fehlt HidHide,
bietet *Allgemein* den Knopf *HidHide installieren …* an.

### Ein Joy-Con ist plötzlich ein eigener Spieler

Er wurde vom Paar gelöst (quer gehalten und SL oder SR gedrückt). L am linken und R am rechten Joy-Con gleichzeitig
drücken, dann sind sie wieder ein Paar.

### Meldung „ViGEmBus-Treiber fehlt“

Ohne diesen Treiber entsteht kein virtueller Controller. Das Setup erneut ausführen; für die Portable-Version
[ViGEmBus](https://github.com/nefarius/ViGEmBus/releases) installieren. ViGEmBus wird seit 2023 nicht mehr
weiterentwickelt. Lädt Windows den Treiber nach einem Update nicht mehr, bitte mit Diagnose melden.

### Verbindung schwach oder viele Controller gleichzeitig

- Liefert ein Controller dauerhaft weniger als 20 Berichte pro Sekunde, blendet N-Connect einen Hinweis ein. Abstand
  zum Adapter verringern; einen USB-Bluetooth-Stick nicht direkt neben USB-3-Geräten betreiben (ein
  Verlängerungskabel hilft).
- Einfache Bluetooth-Sticks schaffen oft nur zwei bis drei Controller gleichzeitig. Für mehr hilft ein
  leistungsfähigerer Adapter, etwa ein Intel AX200/AX210 oder ein Stick mit Bluetooth 5.3.

### Emulatoren bekommen keine Bewegungsdaten

*Allgemein › Erweitert › Gyro für Emulatoren (DSU)* muss an sein; eine Änderung wirkt nach einem Neustart von
N-Connect. Belegt ein anderes Programm den Port 26760 (etwa DS4Windows), läuft N-Connect ohne DSU und vermerkt das im
Protokoll.

### Meldung „Einstellungen wiederhergestellt“ oder „Einstellungen fehlerhaft“

Die Datei `settings.json` war nicht lesbar. N-Connect hat die letzte Sicherung (`settings.json.bak`) bzw. die
Standardwerte geladen und die unlesbare Datei als `settings.json.unlesbar-<Datum>` aufbewahrt. Ein Klick auf die
Meldung zeigt die Datei im Explorer.

### SmartScreen warnt beim Start

Setup und Programm sind nicht code-signiert. Weiter mit „Weitere Informationen“ › „Trotzdem ausführen“. Der Quellcode
liegt vollständig in diesem Repository.

## Android

### Der Controller wird nicht gefunden

Bluetooth einschalten und die SYNC-Taste so lange halten, bis die Lichter laufen. Ist der Controller gerade mit einer
Switch oder einem PC verbunden, dort zuerst Bluetooth ausschalten.

### Ein Switch-1-Controller oder ein Nachbau wird nicht gefunden

Die App unterstützt nur Switch-2-Controller. Ältere Switch-Controller und Nachbauten in den Bluetooth-Einstellungen
von Android koppeln; dann gehen meist nur Tasten und Sticks.

### Verbunden, aber das Spiel reagiert nicht

Auf der Controller-Seite muss oben „Bereit“ stehen. Steht dort, dass Shizuku nicht gestartet ist, Shizuku öffnen und
*Starten* tippen (nach jedem Neustart des Geräts nötig).

### Die Verbindung bricht beim Spielen ab

N-Connect von der Akku-Optimierung ausnehmen: *Einstellungen › Apps › N-Connect › Akku › Nicht eingeschränkt*.

### Doppelte Eingaben

Ist die Touch-Steuerung zusätzlich an und unterstützt das Spiel Controller, die Touch-Steuerung ausschalten.

### Der Controller verbindet sich nicht mehr von selbst

Einmal SYNC drücken, dann merkt er sich dieses Gerät neu. Ohne Shizuku oder Root braucht das Wiederverbinden die
eingetragene Bluetooth-Adresse des Geräts (*Einstellungen › Verbindung*).

### Die Touch-Steuerung lässt sich nicht einschalten

Ab Android 13 sind Bedienungshilfen von Apps außerhalb des Play Store zunächst gesperrt: App-Info von N-Connect ›
Menü (⋮) › „Eingeschränkte Einstellungen zulassen“.

### Kodi zeigt den Stand von N-Connect nicht an

Nach einer Neuinstallation von N-Connect kann noch eine alte `status.json` in `Documents/N-Connect` liegen, die die
App nicht ändern darf; *Hilfe › Kodi* weist dann darauf hin. Die alte Datei mit einem Dateimanager löschen, N-Connect
beenden und wieder öffnen. Außerdem Kodi den Zugriff auf Dateien erlauben.

## Kodi

### Bei SYNC passiert nichts

Im Menü *Diagnose ausführen*: Sie zeigt, ob Bluetooth und der virtuelle Controller bereit sind. Bluetooth muss unter
*LibreELEC-Einstellungen › Bluetooth* eingeschaltet sein, und der Adapter braucht Bluetooth LE.

### Meldung „mit einem anderen Gerät gekoppelt“

SYNC drücken, dann wird der Controller mit diesem Gerät neu gekoppelt.

### Die Verbindung wird sofort wieder abgelehnt (errno 38)

WLAN stört Bluetooth, häufig bei geteilter Antenne wie beim Raspberry Pi. WLAN ausschalten, wenn es nicht gebraucht
wird (*LibreELEC-Einstellungen › Verbindungen*), oder per Kabel ins Netz gehen.

### Eingaben kommen verzögert an

Abstand zum Bluetooth-Adapter verringern; ein USB-Verlängerungskabel für den Stick hilft gegen Störungen durch USB 3.

### Meldung „N-Connect beendet sich immer wieder“

Im Menü *Protokoll anzeigen* und den Fehler mit dem Protokoll melden. Unter Desktop-Linux startet der Dienst ohne
root-Rechte nicht.

### Im Begleitmodus bleibt die Anzeige leer

Die N-Connect-App auf demselben Gerät muss laufen (Android ab 0.9.1). Wurde Kodi erst nach N-Connect installiert,
N-Connect einmal neu starten. Unter macOS gibt es keine App, dort bleibt die Anzeige leer.

## Portable Version

- Auf der [Release-Seite](https://github.com/DevCatSKZ/N-Connect/releases/latest) liegt
  `N-Connect-Portable-<Version>.zip` mit `N-Connect.exe`, `portable.txt`, `LICENSE` und `THIRD-PARTY-NOTICES.md`. In
  einen Ordner entpacken und `N-Connect.exe` starten.
- Einstellungen und Protokoll liegen im Unterordner `data` neben der EXE statt in `%APPDATA%` und `%LOCALAPPDATA%`.
- Die Treiber [ViGEmBus](https://github.com/nefarius/ViGEmBus/releases) und
  [HidHide](https://github.com/nefarius/HidHide/releases) müssen auf jedem PC einmal installiert sein.
- Ohne die geplante Aufgabe des Setups fragt Windows beim Verstecken der Original-Controller nach Adminrechten.
- Ein Update lädt die neue ZIP und tauscht die EXE nach dem Beenden aus.

## Diagnose und Fehler melden

Fehler bitte als [Issue](https://github.com/DevCatSKZ/N-Connect/issues) melden: was passiert ist, welcher Controller,
welches System – und möglichst mit Diagnose.

- **Windows:** *Allgemein › Erweitert › Diagnose exportieren* erstellt eine ZIP-Datei mit Protokoll, Einstellungen und
  Systeminfos (Windows-Version, Treiberversionen von ViGEmBus und HidHide, Controller). Sie enthält keine
  Kopplungsschlüssel, im Protokoll stehen aber Namen und Bluetooth-Adressen der Controller. Das Protokoll allein:
  Infobereich › *Protokoll öffnen*.
- **Android:** *Hilfe › Diagnose › Teilen* (oder *Protokoll anzeigen*). Das Protokoll enthält außer den
  Bluetooth-Adressen der Controller keine persönlichen Daten.
- **Kodi:** Menü › *Diagnose ausführen* und *Protokoll anzeigen*; die Datei liegt unter
  `userdata/addon_data/service.nconnect/nconnect.log`.

Besonders hilfreich sind Rückmeldungen zu Controllern, die noch nicht mit echter Hardware geprüft sind: DualShock 4,
DualSense, Xbox über Bluetooth oder Wireless-Adapter, Kabel-Controller von HORI, PowerA und PDP sowie Joy-Con im
Ladegriff.

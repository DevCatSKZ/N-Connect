# SKZ Connect für Android (Beta)

**Deutsch** · [English](../guide/android.md) · Stand: Version 0.9.1

SKZ Connect für Android verbindet **Switch-2-Controller** (Pro Controller 2, Joy-Con 2 einzeln oder als Paar,
GameCube-Controller) per Bluetooth LE mit Handy oder Tablet. Spiele und Emulatoren sehen einen virtuellen
Xbox-Controller; für Spiele ohne Controller-Unterstützung gibt es eine Touch-Steuerung. Probleme und Lösungen:
[Hilfe](hilfe.md#android).

## Andere Controller

Die App unterstützt nur Switch-2-Controller. Sie sprechen ein eigenes Bluetooth-LE-Protokoll, das Android nicht von
sich aus nutzen kann. Alle anderen Controller koppelt man in den Bluetooth-Einstellungen von Android direkt mit dem
System; SKZ Connect sieht sie nicht.

- **Xbox- und PlayStation-Controller** laufen dort mit den Treibern von Android.
- **Ältere Switch-Controller** (Pro Controller und Joy-Con der Switch 1, Nachbauten des Pro Controllers) nutzen
  Bluetooth Classic. Direkt mit Android gekoppelt liefern sie meist nur Tasten und Sticks; ob Bewegungssteuerung und
  Vibration gehen, hängt vom Gerät ab. Einen eigenen Weg für Bluetooth-Classic-Controller hat die App derzeit nicht.

## Voraussetzungen

- Android 8.0 oder neuer mit Bluetooth LE.
- Für den virtuellen Xbox-Controller [Shizuku](https://shizuku.rikka.app/) (ohne Root) oder Root. Ohne beides bleibt
  nur die Touch-Steuerung.
- Shizuku lässt sich ab Android 11 über kabelloses Debugging im WLAN starten. Unter Android 8 bis 10 startet man
  Shizuku einmal per USB vom PC aus (adb), wie in der Shizuku-App beschrieben.

## Installieren

1. [SKZ-Connect-Android-0.9.1.apk](https://github.com/DevCatSKZ/SKZ-Connect/releases/download/v1.0.22/SKZ-Connect-Android-0.9.1.apk)
   laden und öffnen. Android fragt einmal, ob der Browser bzw. Dateimanager Apps installieren darf: „Zulassen“.
2. Die App kommt nicht aus dem Play Store. Warnt Play Protect vor einer unbekannten App: „Weitere Details“ ›
   „Trotzdem installieren“.
3. SKZ Connect öffnen. Der Assistent führt durch Berechtigungen (Geräte in der Nähe, Benachrichtigungen, Akku nicht
   einschränken), die Wahl des Ausgabewegs, dessen Einrichtung mit Funktionstest und das erste Verbinden. Später erneut:
   *Einstellungen › Allgemein › Einrichtung erneut starten*.

Updates: *Einstellungen › Allgemein › Nach Updates suchen* lädt eine neue Version von GitHub, prüft Größe und
Prüfsumme und startet die Installation. Die Freigabe „Unbekannte Apps installieren“ fragt Android erst dann ab.

## Ausgabeweg einrichten

### Shizuku (ohne Root)

1. Shizuku aus dem Play Store installieren.
2. Entwickleroptionen einschalten: *Einstellungen › Über das Telefon* › siebenmal auf *Build-Nummer* tippen.
3. Mit einem WLAN verbinden und in den Entwickleroptionen *Kabelloses Debugging* einschalten.
4. In Shizuku bei „Über kabelloses Debugging starten“ auf *Kopplung* tippen, den Kopplungscode in der Benachrichtigung
   eingeben, danach *Starten*.
5. Zurück in SKZ Connect: *Zugriff erlauben*.

Nach jedem Neustart des Geräts in Shizuku einmal *Starten* tippen (WLAN nötig); die Kopplung bleibt erhalten. Startet
Shizuku später, übernimmt SKZ Connect es ohne Neustart der App.

### Root

Mit Magisk, KernelSU oder APatch im Assistenten *Root* wählen und den Zugriff im Root-Dialog erlauben. Root gilt auch
nach einem Neustart und ohne WLAN. Der Root-Weg ist mangels gerootetem Testgerät ungetestet.

### Touch-Steuerung

Für Spiele ohne Controller-Unterstützung tippt SKZ Connect die Bildschirmknöpfe; Sticks werden zu ziehenden Fingern.

1. In den Android-Einstellungen unter *Bedienungshilfen* die Bedienungshilfe *SKZ Connect Touch-Steuerung* einschalten.
   Ab Android 13 ist sie für Apps außerhalb des Play Store zunächst gesperrt: App-Info von SKZ Connect › Menü (⋮) ›
   „Eingeschränkte Einstellungen zulassen“.
2. In SKZ Connect die Touch-Steuerung als Weg wählen oder unter *Einstellungen › Ausgabe › Touch-Steuerung zusätzlich*
   einschalten.
3. Im Spiel die Benachrichtigungsleiste herunterziehen und bei SKZ Connect *Touch-Layout bearbeiten* tippen.
4. Mit *Taste hinzufügen* Tasten und Sticks anlegen und auf die Knöpfe des Spiels ziehen; lange drücken entfernt eine
   Taste. *Speichern*.

Die Touch-Steuerung ist auf dem Gerät erst teilweise geprüft.

## Controller verbinden

- **Beim ersten Mal:** Die kleine SYNC-Taste gedrückt halten, bis die Lichter hin- und herlaufen. SKZ Connect findet den
  Controller selbst.
- **Danach:** Eine Taste drücken, der Controller verbindet sich wieder. Dafür braucht SKZ Connect die Bluetooth-Adresse
  des Geräts; mit Shizuku oder Root liest es sie selbst, sonst unter *Einstellungen › Verbindung › Bluetooth-Adresse
  dieses Geräts* eintragen (zu finden unter *Einstellungen › Über das Telefon*).
- **Joy-Con:** beide nacheinander verbinden; sie werden ein Controller (*Einstellungen › Controller › Joy-Con zu einem
  Controller zusammenfassen*).
- Ein Controller merkt sich genau ein Gerät. Soll er wieder an der Switch 2 laufen, dort SYNC drücken – und danach am
  Handy ebenfalls.
- Gemerkte Controller stehen auf der Controller-Seite und lassen sich dort vergessen.

## In Spielen und Emulatoren

- Spiele sehen einen Xbox-Controller namens „SKZ Connect Spieler 1“ usw. In Emulatoren diesen Controller auswählen und
  bei Bedarf die Tasten zuweisen.
- *Einstellungen › Controller*: Tastenanordnung A/B/X/Y (nach Position wie Xbox oder nach Beschriftung wie Switch),
  Vibration und Stärke, Totzonen für Sticks und Trigger.
- *Einstellungen › Bewegungssteuerung › Gyro-Zielen*: Neigen des Controllers steuert den rechten Stick.

## Neu in 0.9.1

Diese Funktionen sind gebaut und per Unit-Test geprüft, liefen aber noch auf keinem Gerät.

- **Profile je Spiel:** *Einstellungen › Profile je Spiel verwalten*. Einmal „Zugriff auf Nutzungsdaten“ erlauben
  (SKZ Connect erkennt daran nur, welche App im Vordergrund ist). *Neues Profil* › Spiel wählen; das Profil startet als
  Kopie der Standardbelegung. *Tastenbelegung bearbeiten*: je Controller jede Taste auf eine Xbox-Taste, keine
  Funktion, Gyro als rechter Stick oder ein Makro legen. *Aktives Profil* wählt ein Profil auch fest.
- **Gyro für Emulatoren (DSU):** *Einstellungen › Bewegungssteuerung › Gyro an Emulatoren senden (DSU)*. Im Emulator
  bei DSU, Cemuhook oder „UDP-Server“ die Adresse `127.0.0.1` und den Port `26760` eintragen; Spieler 1 bis 4 belegen
  die Slots. Der Server ist nur auf diesem Gerät erreichbar. Welche Android-Emulatoren DSU unterstützen, ist nicht
  geprüft.
- **Kachel und Autostart:** In den Schnelleinstellungen die Kachel *SKZ Connect* hinzufügen; ein Tippen startet oder
  beendet die Verbindung. *Einstellungen › Verbindung › Mit Android starten* verbindet nach dem Einschalten des Geräts.
- **Kodi auf demselben Gerät:** Ist Kodi installiert, legt SKZ Connect seinen Stand in `Documents/SKZ-Connect/status.json`
  ab; das Kodi-Add-on ab 0.4.4 zeigt ihn an. Ob Kodi die Datei ab Android 11 lesen darf, ist ungeprüft.
  *Hilfe › Kodi: DevCatSKZ Repository* richtet das Repository ein (*Direkt einrichten* mit Shizuku oder Root) oder lädt
  es herunter. Mehr: [Kodi](kodi.md).
- Akku-Warnung bei 15 % und 5 %, Lizenzen unter *Hilfe › Lizenzen*.

## Berechtigungen

| Berechtigung | Wofür |
|---|---|
| Geräte in der Nähe (Bluetooth) | Controller suchen und verbinden |
| Benachrichtigungen | Statusanzeige, Akku-Warnungen |
| Akku nicht einschränken | Verbindung beim Spielen halten |
| Zugriff auf Nutzungsdaten | nur für Profile je Spiel |
| Internet | nur Update-Prüfung und DevCatSKZ Repository, jeweils auf Tippen |

## Teststand

- Geprüft auf einem Pixel 10a (Android 17) mit Version 0.9.0 und Shizuku: Verbinden, Spielen, virtueller
  Xbox-Controller, Assistent.
- Ungetestet: Root-Weg, Touch-Steuerung vollständig, weitere Geräte (etwa Samsung), Android 8 und 9, alle Neuerungen
  von 0.9.1, das Trigger-Verhalten des GameCube-Controllers.

Rückmeldungen als [Issue](https://github.com/DevCatSKZ/SKZ-Connect/issues), möglichst mit Protokoll: *Hilfe › Diagnose
› Teilen*. Technik (Entwicklerdoku): [ANDROID.md](../ANDROID.md).

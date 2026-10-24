# SKZ Connect für Android

Stand: 17.10.2026 · Version **0.9.1 (Beta)**, versionCode 10 – erscheint als signierte APK `SKZ-Connect-Android-0.9.1.apk`
am nächsten GitHub-Release (die Vorgängerin 0.9.0 hängt an v1.0.20) · Projekt `android/SkzConnect.Android`

SKZ Connect für Android verbindet **Switch-2-Controller** (Pro Controller 2, Joy-Con 2, GameCube-Controller) per
Bluetooth LE mit Android-Geräten und gibt sie als **virtuellen Xbox-Controller** an Spiele, Emulatoren und
Cloud-Gaming weiter – oder als **Touch-Steuerung** für Spiele ohne Controller-Unterstützung.

**Nur Switch-2-Controller.** Sie sprechen kein HID, sondern ein eigenes BLE-GATT-Protokoll, das Android von sich aus
nicht nutzen kann – das ist der Mehrwert der App. Alle anderen Controller verbindet man in den Bluetooth-Einstellungen
von Android direkt mit Android, SKZ Connect sieht sie nicht:

- **Xbox- und PlayStation-Controller** laufen dort nativ (Kernel-Treiber `hid-microsoft`, `hid-playstation`/`hid-sony`).
- **Ältere Switch-Controller** (Pro Controller der Switch 1, Joy-Con der ersten Generation, Nachbauten wie der
  ShanWan-Klon 057e:2009 aus Issue #1) koppeln über Bluetooth Classic ebenfalls direkt mit Android. Dann gehen in der
  Regel nur Tasten und Sticks; ob Bewegungssteuerung und Vibration funktionieren, hängt vom Gerät bzw. Kernel
  (`hid-nintendo`) ab – unter Android 15 waren es laut Issue #1 nur Tasten.
- Ein eigener Bluetooth-Classic-Weg ist **derzeit nicht umgesetzt**: Den HID-Kanal eines klassisch gekoppelten
  Controllers hält der Android-Bluetooth-Stack, eine öffentliche API für HID-Host-Berichte gibt es nicht. Mögliche
  Wege – `/dev/hidraw*` (Root; ob Shizuku-/Shell-Rechte genügen, hängt von den Gerätedateirechten ab) oder nur für die
  Bewegungsdaten `/dev/input/event*`, falls der Kernel `hid-nintendo` bindet – sind gerätespezifisch und **ungeprüft**
  (siehe offene Punkte). Hilfe und Einrichtungs-Assistent sagen das so (vorher hieß es fälschlich, ältere
  Switch-Controller liefen „meist schon direkt“).

## Was geht, was offen ist

| Bereich | Stand |
|---|---|
| Build, Installation auf Pixel 10a (Android 17) | ✅ läuft (0.9.0); 0.9.1 nur gebaut, noch nicht auf einem Gerät |
| Gemeinsame Protokoll-Bibliothek auf dem Gerät | ✅ unverändert wiederverwendet |
| Virtueller Xbox-Controller über UHID (Shizuku) | ✅ von Android als Gamepad erkannt (Test im Assistenten), Tasten/Achsen geprüft |
| Einrichtungs-Assistent, Berechtigungen mit Direktsprüngen | ✅ auf dem Pixel durchgeklickt |
| Farbschemata wie Windows (gemeinsame Quelle) | ✅ |
| Switch-2-Controller verbinden und spielen | ✅ auf dem Pixel 10a geprüft (vom Nutzer bestätigt), keine Hänger beim Minimieren |
| Gekoppelte Controller merken (Liste mit Name, Seriennummer, „Vergessen“) | ✅ `Core/KnownControllers.cs` |
| Wiederverbinden ohne SYNC | ⚙️ eingebaut (braucht die eigene BT-Adresse: Shizuku/Root oder Eingabe) |
| Root-Weg | ⚙️ gebaut, mangels gerootetem Gerät ungetestet |
| Touch-Steuerung + Overlay-Editor | ⚙️ gebaut, auf dem Gerät noch nicht vollständig geprüft |
| Zweites Testgerät Samsung Galaxy A52s (One UI) | ⚙️ verbunden, noch nicht getestet |
| Profile je Spiel (App-Auswahl, eigene Belegung, Makros) | ⚙️ neu in 0.9.1, Logik per Unit-Test geprüft, Oberfläche ungetestet |
| DSU-Server für Emulatoren (127.0.0.1:26760) | ⚙️ neu in 0.9.1, mit konkreten Android-Emulatoren ungetestet |
| Schnelleinstellungen-Kachel, „Mit Android starten“ | ⚙️ neu in 0.9.1, auf dem Gerät ungetestet |
| Update-Prüfung mit APK-Download | ⚙️ neu in 0.9.1, Auswahl der Release-Liste per Unit-Test geprüft |
| Kodi-Status (status.json für das Kodi-Add-on) | ⚙️ neu in 0.9.1, Lesbarkeit für Kodi ab Android 11 ungeprüft |
| Kodi: DevCatSKZ Repository einrichten/herunterladen | ✅ Hilfe › Kodi |
| Übersetzungen | ✅ 12 Sprachen wie Windows (de, en, es, fr, it, pt, nl, pl, ru, ja, zh, ko), generate.py prüft Vollständigkeit |
| Signiertes Release / Verteilung | ✅ 0.9.0 Beta als APK an v1.0.20; 0.9.1 folgt mit dem nächsten Release |

## Aufbau

```
android/SkzConnect.Android/
  SkzConnect.Android.csproj   net10.0-android, minSdk 26, Version 0.9.1 / versionCode 10, verweist auf ../../src/Switch2Pro.Protocol
  AndroidManifest.xml       Berechtigungen, <queries> (Shizuku, Kodi, startbare Apps, Startbildschirme), Provider
  AppPrefs.cs               App-Einstellungen (SharedPreferences) + Settings (geteilte JSON-Klasse)
  Log.cs                    Logcat + Speicher + Datei files/skzconnect.log
  Texts.cs                  gemeinsame Nutzertexte (Android-Begriffe)
  L.cs, L.Texts.cs          Übersetzung (L.T/L.F); L.Texts.cs erzeugt android/i18n/generate.py
  SkzConnectTile.cs           Schnelleinstellungen-Kachel (Dienst starten/beenden)
  Core/ControllerService.cs Vordergrunddienst (connectedDevice), Benachrichtigung, Neuverbinden, Wartungstakt
  Core/Player.cs            Spielerplatz: Paar/einzeln → Belegung → Makros → Gyro-Stick → Ausgabe (+ DSU)
  Core/KnownControllers.cs  gekoppelte Controller (files/known_controllers.json)
  Core/HangWatchdog.cs      meldet Hänger des UI-Threads ≥ 2 s mit Stack ins Protokoll
  Core/BootReceiver.cs      „Mit Android starten“ (BOOT_COMPLETED, MY_PACKAGE_REPLACED)
  Core/UpdateCheck.cs       Update-Prüfung, APK laden/prüfen/installieren, alte APKs aufräumen
  Core/UpdateCheck.Releases.cs  Auswertung der GitHub-Release-Liste (ohne Android-Abhängigkeit, getestet)
  Ble/Switch2Scanner.cs     BLE-Suche, Nintendo-Herstellerdaten 0x0553 (Kopf 01 00 03 7E)
  Ble/Switch2Device.cs      GATT-Verbindung – Portierung von Switch2BleLink (Windows)
  Profiles/GameProfileWatcher.cs       App im Vordergrund (UsageStats) → Profil; Startbildschirme erkennen
  Profiles/GameProfileWatcher.Pick.cs  Auswahl der Vordergrund-App (ohne SKZ-Connect/Startbildschirm, getestet)
  Profiles/ProfileEdit.cs   Profile anlegen (Kopie der Standardbelegung), Tasten/Makros ändern, umbenennen, löschen
  Kodi/KodiRepository.cs    DevCatSKZ Repository herunterladen oder per Shell direkt in Kodi legen
  Kodi/KodiStatusWriter.cs  status.json für das Kodi-Add-on (Documents/SKZ Connect über MediaStore)
  Kodi/KodiStatusWriter.Contract.cs  status.json-Vertrag: "bluetooth", "platform", Änderungsprüfung (getestet)
  Output/PrivilegedShell.cs Root (su) und Shizuku (Reflection) – starten Befehle mit Shell-Rechten
  Output/VirtualPads.cs     UhidPad (hid-Prozess je Spieler), CompositePad
  Output/OutputRouter.cs    wählt Root → Shizuku → Touch, liest einmalig die BT-Adresse
  Touch/                    Bedienungshilfe, Touch-Ausgabe (Gesten), Layout, Overlay-Editor
  UI/                       Assistent, Hauptfenster (Controller · Einstellungen · Hilfe), Baukasten, Design,
                            ProfileDialogs.cs (Profile und Belegungs-Editor), AppPicker.cs (App-Auswahl mit Symbol)
android/SkzConnect.Android.Tests/  xUnit-Tests (net10.0) für die Android-freien Teile, siehe „Tests ohne Gerät“
android/i18n/               Übersetzungstabellen (*.json) und generate.py
```

Neu in der gemeinsamen Bibliothek (`src/Switch2Pro.Protocol`, auch unter Windows getestet):

- `XboxBtReport.cs` – Deskriptor und 17-Byte-Bericht des virtuellen Xbox-One-S-Controllers, `UhidCommand` (JSON für
  den Shell-Befehl `hid`). Tests: `XboxBtReportTests` (gegen die CTS-Referenzberichte).
- `ColorSchemes.cs` – die fünf Farbschemata (Windows, Neon, Aurora, Sunset, Joy-Con) als gemeinsame Quelle; die
  Windows-App (`Theme.cs`) liest sie ebenfalls daraus. Tests: `ColorSchemesTests`.
- `Dsu.cs`, `DsuServer.cs` – Cemuhook-/DSU-Pakete und der UDP-Server auf 127.0.0.1:26760 (Gyro und Tasten für
  Emulatoren), derselbe Server wie unter Windows und Linux. Tests: `DsuServerTests` (echter UDP-Austausch).

### Datenfluss

```
Controller ──BLE (GATT, Bericht 0x05)──▶ Switch2Device ──ControllerState──▶ Player
  Player: Mapping.Merge/Normalize ─▶ Mapping.Evaluate (Belegung, Profil je Spiel) ─▶ Makros ─▶ Gyro-Stick ─▶ GamepadState
  GamepadState ─▶ UhidPad: XboxBtReport.Build ─▶ JSON-Zeile ─▶ stdin von `hid -` (Shell-Rechte) ─▶ /dev/uhid
              ├─▶ TouchPad: gehaltene/ziehende Finger ─▶ AccessibilityService.dispatchGesture
              └─▶ DsuServer (falls an): Tasten + Bewegung ─▶ UDP 127.0.0.1:26760 ─▶ Emulator
Vibration: Spiel ─▶ Kernel (hid-microsoft FF) ─▶ UHID-Ausgabebericht 0x03 ─▶ stdout von `hid` ─▶ Rumble.BuildPacket ─▶ Controller
```

## Berechtigungen

| Berechtigung | Wofür | Wann gefragt |
|---|---|---|
| `BLUETOOTH_SCAN` (neverForLocation), `BLUETOOTH_CONNECT` (ab Android 12); bis Android 11 `BLUETOOTH`, `BLUETOOTH_ADMIN`, `ACCESS_FINE_LOCATION` | Controller suchen und verbinden | Assistent („Geräte in der Nähe“) |
| `POST_NOTIFICATIONS` (ab Android 13) | Statusbenachrichtigung, Akku-Warnungen | Assistent |
| `FOREGROUND_SERVICE`, `FOREGROUND_SERVICE_CONNECTED_DEVICE`, `WAKE_LOCK` | Verbindung beim Spielen halten | automatisch |
| `REQUEST_IGNORE_BATTERY_OPTIMIZATIONS` | Direktdialog „Akku: nicht einschränken“ | Assistent (empfohlen) |
| `INTERNET` | DevCatSKZ Repository, Update-Prüfung | automatisch |
| `RECEIVE_BOOT_COMPLETED` | „Mit Android starten“ | automatisch (wirkt nur, wenn eingeschaltet) |
| `REQUEST_INSTALL_PACKAGES` | geladene Update-APK über den System-Installer einspielen | erst beim Installieren („Unbekannte Apps installieren“) |
| `PACKAGE_USAGE_STATS` | Profile je Spiel: App im Vordergrund erkennen | beim Öffnen von „Profile je Spiel verwalten“ („Zugriff auf Nutzungsdaten“) |

Paketsichtbarkeit (`<queries>`): Shizuku, Kodi, alle startbaren Apps (`MAIN`/`LAUNCHER` und `LEANBACK_LAUNCHER` für die
App-Auswahl) und Startbildschirme (`MAIN`/`HOME`, damit ein Wechsel dorthin das Profil nicht umschaltet).

**Ohne „Geräte in der Nähe“** startet der Dienst nicht: Kachel, Autostart und App prüfen das **vor** dem Start
(Kachel → App öffnen, Autostart → still abbrechen mit Protokolleintrag, App → Berechtigung erfragen). Für den Restfall
(Berechtigung fehlt erst in `OnStartCommand`) fängt `ControllerService.TryStart` Ausnahmen aus
`StartForeground`/`StartScan` ab und beendet den Dienst mit Protokolleintrag (ab Android 12 wirft `startScan`, ab
Android 14 schon `startForeground` mit Typ connectedDevice eine SecurityException). Einen Absturz schließt das nicht
in jedem Fall aus: Endet ein per `startForegroundService` gestarteter Dienst, ohne dass `startForeground` gelungen
ist, beendet Android die App („did not then call startForeground“, `fgRequired`). Deshalb prüft `TryStart` die
Berechtigung erst **nach** `StartAsForeground` – bis Android 13 gelingt `startForeground` auch ohne sie; ob Android ab
14 den abgelehnten Aufruf gelten lässt, ist nicht auf einem Gerät geprüft. Praktisch kaum erreichbar: Zwischen der
Prüfung beim Aufrufer und `OnStartCommand` liegen nur Millisekunden, und entzieht man eine Berechtigung, beendet
Android den App-Prozess ohnehin.

## Ausgabe: virtueller Controller ohne Root

- Android hat den Shell-Befehl **`/system/bin/hid`** (AOSP `frameworks/base/cmds/hid`). Er liest JSON-Ereignisse über
  stdin (`hid -`) und legt per **UHID** ein echtes HID-Gerät an. Der Shell-Nutzer (uid 2000) ist in der Gruppe
  **`uhid`** und darf `/dev/uhid` öffnen – also genügen **Shizuku** (Shell-Rechte) oder Root (`su -c hid -`).
- **Xbox One S Bluetooth (045E:02FD)** funktioniert: Android meldet `GAMEPAD | JOYSTICK | VIBRATOR | BATTERY`.
  DualShock 4 und DualSense **scheitern**: deren Kernel-Treiber fragen beim Erkennen Feature-Reports ab, die auf diesem
  Weg nicht beantwortet werden – es entsteht kein Eingabegerät.
- Ein `hid`-Prozess je Spieler; beendet man den Prozess, verschwindet das Gerät sauber.
- Bericht (17 Byte): `[0]` 0x01 · `[1–8]` LX/LY/RX/RY u16 LE (Mitte 0x7FFF, Y nach unten positiv) · `[9–12]` LT/RT 10 Bit
  · `[13]` Hat (0 neutral, 1 oben … 8 oben links) · `[14]` A 01, B 02, X 08, Y 10, LB 40, RB 80 · `[15]` Start 08,
  Xbox 10, LS 20, RS 40 · `[16]` Zurück 01.
- Gyro: wie unter Windows mit Xbox-Ausgabe über `Mapping.GyroToStick` auf den rechten Stick.

### Shizuku

- Maven-Pakete `dev.rikka.shizuku:api/provider/shared/aidl 13.1.5`, nur paketiert (`Bind="false"`), Aufruf per
  Reflection: `pingBinder`, `checkSelfPermission`, `requestPermission`, privates `newProcess(String[],String[],String)`.
- **Klassenlader:** `Class.forName` muss den Klassenlader der App bekommen
  (`Class.ForName(name, true, Application.Context.ClassLoader)`). Auf .NET-Hintergrund-Threads nimmt Java sonst den
  System-Klassenlader → `ClassNotFoundException`.
- **Paketsichtbarkeit:** Ohne `<queries><package android:name="moe.shizuku.privileged.api"/></queries>` „sieht“ die App
  Shizuku ab Android 11 nicht (wirkte wie „nicht installiert“).
- Die API-Berechtigung `moe.shizuku.manager.permission.API_V23` kommt über das Provider-Paket ins Manifest; dadurch
  liefert Shizuku seine Schnittstelle auch an eine schon laufende App.
- Der Dienst prüft den Ausgabeweg alle 2 s nach (`WatchOutputAsync`), solange keiner aktiv ist – ein später
  gestartetes Shizuku wird ohne App-Neustart übernommen.

### Touch

`TouchService` (Bedienungshilfe, `canPerformGestures`, liest keine Inhalte). Android bricht eine laufende Geste ab,
sobald eine neue kommt – `TouchPad` setzt deshalb alle gehaltenen Finger in **einer** Geste fort
(`StrokeDescription.ContinueStroke`) und schickt nie mehr als eine Geste gleichzeitig. Sticks werden zu ziehenden
Fingern um einen Mittelpunkt. Layout in `files/touch-layout.json` (Anteile der Bildschirmgröße), Editor als
`TYPE_ACCESSIBILITY_OVERLAY` über dem Spiel, erreichbar über die Benachrichtigung.

Android 13+: Bedienungshilfen von Apps außerhalb des Play Store sind zunächst gesperrt („Eingeschränkte Einstellung“)
– der Assistent erklärt den Weg über App-Info → ⋮ → „Eingeschränkte Einstellungen zulassen“.

## Bluetooth-Verbindung

Portierung von `Switch2BleLink`: verbinden (bis 3 Anläufe, Status 133) → `RequestConnectionPriority(High)` →
**MTU 247** (Bericht 0x05 ist ~64 Byte, bei MTU 23 würde er abgeschnitten) → Dienste → Antworten abonnieren →
`SetFeatures`/`EnableFeatures` → Eingaben abonnieren → Kalibrierung, Gerätedaten → im SYNC-Modus **Kopplung mit diesem
Gerät** (Befehl 0x15, Host = eigene BT-Adresse). Android erlaubt nur **eine** GATT-Operation gleichzeitig – alle
Schritte laufen über `Op()` nacheinander. Der Wächter (2,5 s ohne Eingaben = verloren) startet erst **nach** der
Einrichtung.

Eigene BT-Adresse: Normale Apps dürfen sie nicht lesen; mit Shizuku/Root per `settings get secure bluetooth_address`
(am Pixel: `04:C8:B0:F5:B2:27`), sonst Eingabe in den Einstellungen.

Gesucht wird zwei Minuten schnell (LowLatency), danach und sobald ein Controller verbunden ist stromsparend; mit acht
belegten Plätzen ohne wartenden Joy-Con gar nicht. Fehlversuche warten zunehmend lange (5 s bis 60 s).

## Profile je Spiel

Einstellungen › Profile › **Profile je Spiel verwalten** (braucht einmal „Zugriff auf Nutzungsdaten“):

- **Neues Profil:** Spiel aus der App-Auswahl wählen (`UI/AppPicker.cs`: alle startbaren Apps mit Name und Symbol,
  Suchfeld; ohne SKZ Connect selbst und ohne Startbildschirme), Namen bestätigen. Das Profil startet mit einer **festen
  Kopie der Standardbelegung** aller Android-Controller-Arten (Pro Controller 2, Joy-Con-Paar, Joy-Con 2 L/R,
  GameCube) – spätere Änderungen am Standard (z. B. A/B/X/Y) ändern das Profil nicht (`ProfileEdit.CopyOfStandard`).
- **Profil antippen:** Spiel ändern, **Tastenbelegung bearbeiten** (Controller-Art wählen → Taste → Ziel), umbenennen,
  löschen. Ziele: Standard, keine Funktion, jede Xbox-Taste, Touchpad-Klick (nur DSU), Gyro als rechter Stick bzw. Gyro
  anhalten (solange gehalten) und **Makros** (Tastenfolge wie unter Windows, z. B. `A 80, Pause 60, A 80`, Prüfung
  über `MacroScript`; Tastatur-Schritte `Key:` gibt es unter Android nicht und werden abgelehnt). „Alles auf Standard“
  setzt eine Controller-Art zurück. Gespeichert wird in `NamedProfile.Buttons` der gemeinsamen `Settings`.
- **Erkennung** (`GameProfileWatcher.Poll`, alle 5 s im Wartungstakt des Dienstes): letzte ActivityResumed-App der
  letzten 15 s, **ohne SKZ Connect und ohne Startbildschirme** – wer kurz in SKZ Connect oder auf den Startbildschirm
  wechselt, behält sein Profil. „Aktives Profil“ wählt ein Profil auch fest. Umbenennen/Löschen ziehen das fest
  gewählte bzw. erkannte Profil mit; „Belegung zurücksetzen“ lässt Profile und Farbschema stehen.
- Auf Android 8/9 prüft `HasAccess` mit `CheckOpNoThrow` (`UnsafeCheckOpNoThrow` gibt es erst ab API 29 – vorher
  stürzte die Profilverwaltung dort ab). Der Wartungstakt fängt Ausnahmen je Durchlauf ab, Akku-Warnung und
  Profilwechsel laufen also weiter.

## DSU-Server (Gyro für Emulatoren)

Einstellungen › Bewegungssteuerung › **Gyro an Emulatoren senden (DSU)**: Cemuhook-/DSU-Server auf **127.0.0.1:26760**,
nur auf dem Gerät erreichbar. Im Emulator bei DSU/Cemuhook/„UDP-Server“ Adresse `127.0.0.1`, Port `26760` eintragen;
Spieler 1–4 sind die Slots 0–3. Welche Android-Emulatoren einen DSU-Client haben, ist nicht geprüft (Hilfe › Gyro für
Emulatoren nennt deshalb keine Namen).

## Schnelleinstellungen-Kachel und Autostart

- **Kachel** (`SkzConnectTile`): startet/beendet den Dienst. Bewusst **keine aktive Kachel** (ohne ACTIVE_TILE) – Android
  fragt den Zustand bei jedem Öffnen der Schnelleinstellungen ab, nach dem Antippen steht sofort der Zielzustand da,
  zusätzlich meldet der Dienst Start/Ende per `RequestListeningState`. Einfarbiges Symbol `ic_stat_controller`. Ohne
  abgeschlossene Einrichtung oder ohne „Geräte in der Nähe“ öffnet die Kachel die App; lehnt Android den Start des
  Vordergrunddiensts aus dem Hintergrund ab (`ForegroundServiceStartNotAllowedException`), ebenfalls. Ab Android 14 über
  `StartActivityAndCollapse(PendingIntent)`.
- **Mit Android starten** (`BootReceiver`, Einstellungen › Verbindung): nach `BOOT_COMPLETED`/`MY_PACKAGE_REPLACED`,
  nur wenn eingeschaltet, eingerichtet und „Geräte in der Nähe“ erlaubt.

## Update-Prüfung

Einstellungen › Allgemein › **Nach Updates suchen** (`Core/UpdateCheck*.cs`):

- Die APKs hängen an den gemeinsamen Releases, deren Tag die **Windows-Version** ist (v1.0.x). Verglichen wird deshalb
  die Version im **Dateinamen** `SKZ-Connect-Android-<x.y.z>.apk`; Releases ohne APK, Entwürfe und Vorabversionen zählen
  nicht, gewählt wird die höchste APK-Version (vorher wurde der Tag 1.0.21 mit 0.9.0 verglichen und jede Prüfung
  meldete ein Update).
- Download nur von GitHub (auch nach Umleitung), Größe und SHA-256 (`digest` der GitHub-API) werden geprüft; eine schon
  vollständig geladene, geprüfte Datei wird nicht erneut geladen.
- „Unbekannte Apps installieren“ wird erst verlangt, wenn wirklich installiert wird; nach der Freigabe spielt die
  Einstellungsseite die Datei von selbst ein.
- Alte APKs im App-Download-Ordner werden vor einem Download und beim App-Start (alle, die nicht neuer als die
  installierte Version sind) gelöscht.

## Kodi-Status (status.json)

`Kodi/KodiStatusWriter.cs` schreibt – nur wenn Kodi installiert ist – denselben Stand wie SKZ Connect für Windows und der
Linux-Dienst, damit Menü und Home-Widget des Kodi-Add-ons ihn anzeigen:

- Ort: ab Android 10 `/sdcard/Documents/SKZ-Connect/status.json` über MediaStore (ohne Speicher-Berechtigung), auf
  Android 8/9 der externe App-Ordner. Die Datei wird **einmal angelegt und dann mit `"wt"` überschrieben** (vorher
  fünf MediaStore-Aufrufe plus Datei anlegen/löschen bei jedem Schreiben).
- Takt 3 s, geschrieben wird bei Änderungen und spätestens alle 10 s (Herzschlag – das Add-on wertet nur Dateien bis
  15 s Alter aus). Die schwankende Berichtsrate `rate` zählt nicht als Änderung. Die App-Version wird einmal gelesen.
- Vertrag: `"state"` (`running`/`stopped`), `"platform": "android"`, `"bluetooth"`: `on` | `off` | `missing` (kein
  Adapter) | `no_le` (Adapter ohne Bluetooth LE), dazu Spieler, Geräte, bekannte und woanders gekoppelte Controller.
- Beim Beenden wird der Timer angehalten und auf einen laufenden Takt gewartet, dann `"stopped"` geschrieben – ein
  später Takt kann es nicht mehr überschreiben.
- **Neuinstallation:** Eine alte status.json gehört dann der früheren Installation; die App sieht sie nicht und darf
  sie nicht ändern, Android legt die neue unter einem Ausweichnamen an (z. B. `status (1).json`). Die App bleibt dann
  bei dieser Datei und versucht bei jedem Start, wieder `status.json` zu bekommen. Solange sie die Ausweichdatei nutzt
  (`KodiStatusWriter.FallbackName`), steht das im Protokoll und **sichtbar in Hilfe › Kodi** (die Karte ist dann schon
  aufgeklappt): alte `status.json` mit einem Dateimanager löschen, SKZ Connect beenden und wieder öffnen. Das Kodi-Add-on
  liest bisher nur `status.json` und zeigt bis dahin keinen Stand; es sollte auch `status*.json` im Ordner lesen
  (siehe offene Punkte).

## Oberfläche

- **Assistent** (6 Schritte): Willkommen → Berechtigungen (je Zeile ein Knopf mit Direktsprung: Bluetooth- und
  Benachrichtigungs-Dialog, Akku „nicht einschränken“) → Weg wählen (Empfehlung vorausgewählt) → Weg einrichten
  (Shizuku 3 Schritte mit Live-Status / Root / Touch) + Funktionstest → Controller verbinden (live) → Fertig.
  Merkt sich den Schritt (`AppPrefs.WizardStep`) und macht nach einem Neustart dort weiter.
- **Hauptfenster** mit unterer Navigation: *Controller* (Status mit „Beheben“, Spielerkarten mit Akku, Live-Eingabe,
  Vibration testen, Trennen), *Einstellungen* (Ausgabe, Controller, Profile, Bewegungssteuerung, Verbindung,
  Darstellung, Allgemein), *Hilfe* (aufklappbare Anleitungen mit Direktknöpfen – darunter Gyro für Emulatoren (DSU),
  Profile je Spiel, Kachel und Autostart –, Diagnose mit Protokoll teilen).
- **Design:** dieselben Farbschemata und dieselbe Wahl Hell/Dunkel/Wie System wie Windows (`Settings.ColorScheme`,
  `Settings.Theme`); Neon-Schemata mit Verlauf auf Hauptknöpfen. App-Symbol aus dem Website-Logo (512 px).
- Systemseiten-Sprünge zentral in `UI/SystemPages.cs` (immer mit Rückfall).
- Akku-Warnung bei 15 % und 5 % als eigene Benachrichtigung (Kanal „Hinweise“).

## Übersetzung (12 Sprachen)

Wie unter Windows ist die Oberfläche **auf Deutsch geschrieben**; sichtbare Texte laufen durch `L.T("deutscher Text")`
bzw. bei Werten durch Vorlagen `L.F("Spieler {0} · {1}", …)`. Die Sprache folgt der Systemsprache (oder der
App-Sprache aus den Android-Einstellungen), unbekannte Sprachen fallen auf Englisch zurück.

- Die Bausteine in `UI/Ui.cs` (Title, Body, Caption, Section, Rich, Button, Toast …) übersetzen selbst – Texte dort
  einfach deutsch übergeben. Außerhalb davon (Dialoge, Benachrichtigung, Menü, Touch-Editor) `L.T` direkt nutzen.
- **Neue oder geänderte Texte:** in `android/i18n/*.json` eintragen (`{"deutsch": {"en": …, "es": …, …}}`) und
  `python android/i18n/generate.py` ausführen (`--check` schreibt nie und meldet zusätzlich, ob `L.Texts.cs` und die
  `strings.xml` veraltet sind – für die CI). Das Skript schreibt `L.Texts.cs` sowie `Resources/values-xx/strings.xml`
  (Name der Bedienungshilfe, Titel des Assistenten) und **bricht ohne zu schreiben ab**, wenn
  - ein deutscher Text doppelt vorkommt (in einer oder mehreren Dateien – vorher gewann stillschweigend die letzte),
  - eine der 11 Übersetzungen oder ein Platzhalter `{0}` fehlt,
  - ein Text, den der Code an `L.T`, `L.F`, `Ui.*` oder die Hilfsmethoden `Topic`/`Option` übergibt (auch
    zusammengesetzte Literale, `?:`, `??` und `switch`-Ausdrücke), in keiner Tabelle steht – mit Datei und Zeile.
    Eigennamen wie „Pro Controller 2“ stehen in `SAME_EVERYWHERE`.
- `00-windows.json` enthält die aus der Windows-App übernommenen Texte (bei Doppelungen gilt deren Wortwahl), z. B.
  Tastennamen aus `ControllerButtons.Label` und die Ziele des Belegungs-Editors. Texte, die erst zur Laufzeit durch
  `L.T` laufen, prüft `TranslationTests` (Unit-Test).
- Testen ohne Systemsprache zu ändern: `adb shell cmd locale set-app-locales com.devcatskz.skzconnect --locales fr-FR`,
  App neu starten.
- Der Name des virtuellen Controllers ist übersetzt („SKZ Connect Player 1“, „SKZ Connect Joueur 1“ …); für ru/ja/zh/ko
  bewusst englisch, damit Emulatoren keine Probleme mit nicht-lateinischen Gerätenamen haben.

## Release

- Version in `SkzConnect.Android.csproj`: `ApplicationDisplayVersion` (0.9.1) und `ApplicationVersion` (10) – bei jeder
  veröffentlichten APK erhöhen.
- Signatur: `%USERPROFILE%\.skzconnect\skzconnect-release.jks` + `signing.props` (wird von der csproj importiert, falls
  vorhanden). **Nie ins Repository oder in die CI** – deshalb baut die CI keine APK.
- `dotnet publish -c Release` → `bin/Release/net10.0-android/publish/com.devcatskz.skzconnect-Signed.apk`, als
  **`SKZ-Connect-Android-<Version>.apk`** an das GitHub-Release hängen (`gh release upload v1.0.x …`). Der Dateiname ist
  Pflicht: Die Update-Prüfung liest die Version daraus (Releases ohne passend benannte APK sieht sie nicht).
  README/Webseite verlinken die APK mit fester Versionsnummer – Links bei einer neuen APK anpassen.

## Bauen, installieren, testen

```powershell
cd android\SkzConnect.Android
dotnet build -c Debug
# Gerät gezielt (zwei Geräte per Wireless-adb verbunden):
dotnet build -t:Install -c Debug "-p:AdbTarget=-s adb-61291JEA326097-kWTmyn._adb-tls-connect._tcp"   # Pixel 10a
dotnet build -t:Install -c Debug "-p:AdbTarget=-s adb-R5CRA1GQW0Z-ECDAJk._adb-tls-connect._tcp"      # Galaxy A52s
```

- Voraussetzungen: .NET 10 SDK + Workload `android`, JDK 21 (`C:\Program Files\Microsoft\jdk-21.0.10.7-hotspot`, in der
  csproj hinterlegt – JDK 25 ist zu neu), Android-SDK unter `%LOCALAPPDATA%\Android\Sdk`.
- Die CI (`.github/workflows/build.yml`) testet die ganze Projektmappe, darunter `android/SkzConnect.Android.Tests`
  (ohne Android-Workload). Der Job „Android kompilieren“ prüft `python android/i18n/generate.py --check` und baut die
  App als Debug-Build ohne Signatur; das signierte APK entsteht weiterhin nur lokal (siehe oben).
- **Shizuku vom PC starten** (spart die Kopplung per kabellosem Debugging beim Testen):
  `adb shell <APK-Ordner>/lib/arm64/libshizuku.so` (Ordner über `adb shell pm path moe.shizuku.privileged.api`).
- **Protokoll lesen:** `adb shell "run-as com.devcatskz.skzconnect cat files/skzconnect.log"` (Logcat ist auf dem Pixel
  nach wenigen Minuten überschrieben).
- Screenshots: `adb shell screencap -p /sdcard/x.png` + `adb pull` (PowerShell-Umleitung verfälscht Binärdaten).
  **Nur wenn SKZ Connect im Vordergrund ist** – der Nutzer verwendet das Gerät nebenbei.
- `pm clear com.devcatskz.skzconnect` simuliert den Erststart.

### Tests ohne Gerät

`dotnet test android/SkzConnect.Android.Tests` (xUnit, net10.0, braucht kein Android-Workload) kompiliert die
Android-freien Quelldateien direkt mit (`UpdateCheck.Releases.cs`, `ProfileEdit.cs`, `GameProfileWatcher.Pick.cs`,
`KodiStatusWriter.Contract.cs`, `L.Texts.cs`) und prüft:

- Update-Prüfung: Version aus dem APK-Namen, Releases ohne APK/Entwürfe/Vorabversionen/fremde Adressen übersprungen,
  höchste APK gewinnt unabhängig vom Tag, kein erneutes Angebot nach der Installation.
- Profile: Kopie der Standardbelegung für jede Taste aller Android-Controller-Arten, Unabhängigkeit vom späteren
  Standard, geänderte Tasten/keine Funktion/Gyro/Makro wirken in `Mapping.Evaluate`, Makro-Prüfung ohne `Key:`,
  eindeutige Namen, Umbenennen/Löschen mit fest gewähltem und erkanntem Profil, Speichern und Laden.
- Vordergrund-App ohne SKZ Connect und Startbildschirme; status.json-Werte `bluetooth`/`platform`, Änderungsprüfung ohne
  `rate`.
- Übersetzungen: alle Tabellen gleich lang, Tastennamen und Editor-Ziele in allen 11 Sprachen.

Dazu `python android/i18n/generate.py --check` (Abbruch bei fehlenden/doppelten Texten oder veralteter `L.Texts.cs`)
und der Debug-Build mit TreatWarningsAsErrors.

### Teststand 0.9.1

Gebaut (Debug, 0 Warnungen), Unit-Tests grün, **nicht auf einem Gerät geprüft**: App-Auswahl und Belegungs-Editor,
Profilwechsel im Spiel, Kachel (Start aus dem Hintergrund ab Android 12, Anzeige), Autostart nach Neustart, Update mit
echter APK (Freigabe „Unbekannte Apps installieren“, Installation, Aufräumen), DSU mit Android-Emulatoren,
status.json mit Kodi (insbesondere ab Android 11 und nach Neuinstallation), Android 8/9 (Profilverwaltung).

## Offene Punkte

1. Rückmeldungen zur Beta sammeln (verschiedene Hersteller, v. a. Samsung/One UI); die neuen Funktionen aus
   „Teststand 0.9.1“ auf dem Pixel 10a und dem Galaxy A52s durchgehen.
2. Wiederverbinden ohne SYNC über längere Zeit und nach Handy-Neustart prüfen.
3. Touch-Steuerung und Editor auf dem Gerät vollständig testen; Root-Weg testen.
4. Kodi für Android: prüfen, ob Kodi `/sdcard/Documents/SKZ-Connect/status.json` einer anderen App ab Android 11 lesen
   darf (ohne „Zugriff auf alle Dateien“ sieht eine App Nicht-Mediendateien anderer Apps dort normalerweise nicht).
   Das Add-on sollte außerdem die neueste `status*.json` im Ordner lesen (Ausweichname nach einer Neuinstallation,
   z. B. `status (1).json`); bis dahin hilft nur der Hinweis in Hilfe › Kodi.
5. DSU: Android-Emulatoren mit DSU-Client ermitteln und in Hilfe/Doku nennen.
6. Verteilung über F-Droid oder Play Store prüfen.
7. Bluetooth-Classic-Controller (Issue #1, ShanWan-Klon 057e:2009): Issue beantworten (Android 0.9 kann nur
   Switch 2 über BLE), das angehängte Diagnose-ZIP auswerten, auf einem Testgerät `ls -l /dev/hidraw*` und
   `getevent -lp` prüfen bzw. vom Nutzer erfragen und erst dann über einen eigenen Weg (hidraw/evdev) entscheiden.
   Dafür fehlt unter Android auch ein Gegenstück zu HidHide (Original und virtueller Xbox-Controller erschienen sonst
   doppelt).
8. GameCube-Trigger prüfen.

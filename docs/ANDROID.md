# N-Connect für Android

Stand: 11.10.2026 · Version **0.9.0 (Beta)**, veröffentlicht als signierte APK am GitHub-Release v1.0.20 · Projekt
`android/NConnect.Android`

N-Connect für Android verbindet **Switch-2-Controller** (Pro Controller 2, Joy-Con 2, GameCube-Controller) per
Bluetooth LE mit Android-Geräten und gibt sie als **virtuellen Xbox-Controller** an Spiele, Emulatoren und
Cloud-Gaming weiter – oder als **Touch-Steuerung** für Spiele ohne Controller-Unterstützung.

Xbox-, PlayStation- und Switch-1-Controller funktionieren unter Android meist schon nativ (Kernel-Treiber
`hid-microsoft`, `hid-playstation`/`hid-sony`, `hid-nintendo`). Der eigentliche Mehrwert sind die Switch-2-Controller:
Sie sprechen kein HID, sondern ein eigenes BLE-GATT-Protokoll, das Android von sich aus nicht nutzen kann.

## Was geht, was offen ist

| Bereich | Stand |
|---|---|
| Build, Installation auf Pixel 10a (Android 17) | ✅ läuft |
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
| Kodi: DevCatSKZ Repository einrichten/herunterladen | ✅ Hilfe › Kodi |
| Übersetzungen | ✅ 12 Sprachen wie Windows (de, en, es, fr, it, pt, nl, pl, ru, ja, zh, ko), im Emulator geprüft |
| Signiertes Release / Verteilung | ✅ 0.9.0 Beta als APK am GitHub-Release v1.0.20 |

## Aufbau

```
android/NConnect.Android/
  NConnect.Android.csproj   net10.0-android, minSdk 26, verweist auf ../../src/Switch2Pro.Protocol
  AndroidManifest.xml       Berechtigungen, <queries> für Shizuku, Shizuku-Provider
  AppPrefs.cs               App-Einstellungen (SharedPreferences) + Settings (geteilte JSON-Klasse)
  Log.cs                    Logcat + Speicher + Datei files/nconnect.log
  Texts.cs                  gemeinsame Nutzertexte (Android-Begriffe)
  L.cs, L.Texts.cs          Übersetzung (L.T/L.F); L.Texts.cs erzeugt android/i18n/generate.py
  Core/KnownControllers.cs  gekoppelte Controller (files/known_controllers.json)
  Core/HangWatchdog.cs      meldet Hänger des UI-Threads ≥ 2 s mit Stack ins Protokoll
  Kodi/KodiRepository.cs    DevCatSKZ Repository herunterladen oder per Shell direkt in Kodi legen
  Ble/Switch2Scanner.cs     BLE-Suche, Nintendo-Herstellerdaten 0x0553 (Kopf 01 00 03 7E)
  Ble/Switch2Device.cs      GATT-Verbindung – Portierung von Switch2BleLink (Windows)
  Core/Player.cs            Spielerplatz: Paar/einzeln → Belegung → Gyro-Stick → Ausgabe
  Core/ControllerService.cs Vordergrunddienst (connectedDevice), Benachrichtigung, Neuverbinden
  Output/PrivilegedShell.cs Root (su) und Shizuku (Reflection) – starten Befehle mit Shell-Rechten
  Output/VirtualPads.cs     UhidPad (hid-Prozess je Spieler), CompositePad
  Output/OutputRouter.cs    wählt Root → Shizuku → Touch, liest einmalig die BT-Adresse
  Touch/                    Bedienungshilfe, Touch-Ausgabe (Gesten), Layout, Overlay-Editor
  UI/                       Assistent, Hauptfenster (Controller · Einstellungen · Hilfe), Baukasten, Design
```

Neu in der gemeinsamen Bibliothek (`src/Switch2Pro.Protocol`, auch unter Windows getestet):

- `XboxBtReport.cs` – Deskriptor und 17-Byte-Bericht des virtuellen Xbox-One-S-Controllers, `UhidCommand` (JSON für
  den Shell-Befehl `hid`). Tests: `XboxBtReportTests` (gegen die CTS-Referenzberichte).
- `ColorSchemes.cs` – die fünf Farbschemata (Windows, Neon, Aurora, Sunset, Joy-Con) als gemeinsame Quelle; die
  Windows-App (`Theme.cs`) liest sie ebenfalls daraus. Tests: `ColorSchemesTests`.

### Datenfluss

```
Controller ──BLE (GATT, Bericht 0x05)──▶ Switch2Device ──ControllerState──▶ Player
  Player: Mapping.Merge/Normalize ─▶ Mapping.Evaluate (Belegung) ─▶ Gyro-Stick ─▶ GamepadState
  GamepadState ─▶ UhidPad: XboxBtReport.Build ─▶ JSON-Zeile ─▶ stdin von `hid -` (Shell-Rechte) ─▶ /dev/uhid
              └─▶ TouchPad: gehaltene/ziehende Finger ─▶ AccessibilityService.dispatchGesture
Vibration: Spiel ─▶ Kernel (hid-microsoft FF) ─▶ UHID-Ausgabebericht 0x03 ─▶ stdout von `hid` ─▶ Rumble.BuildPacket ─▶ Controller
```

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

## Oberfläche

- **Assistent** (6 Schritte): Willkommen → Berechtigungen (je Zeile ein Knopf mit Direktsprung: Bluetooth- und
  Benachrichtigungs-Dialog, Akku „nicht einschränken“) → Weg wählen (Empfehlung vorausgewählt) → Weg einrichten
  (Shizuku 3 Schritte mit Live-Status / Root / Touch) + Funktionstest → Controller verbinden (live) → Fertig.
  Merkt sich den Schritt (`AppPrefs.WizardStep`) und macht nach einem Neustart dort weiter.
- **Hauptfenster** mit unterer Navigation: *Controller* (Status mit „Beheben“, Spielerkarten mit Akku, Live-Eingabe,
  Vibration testen, Trennen), *Einstellungen* (Ausgabe, Controller, Bewegungssteuerung, Verbindung, Darstellung,
  Allgemein), *Hilfe* (aufklappbare Anleitungen mit Direktknöpfen, Diagnose mit Protokoll teilen).
- **Design:** dieselben Farbschemata und dieselbe Wahl Hell/Dunkel/Wie System wie Windows (`Settings.ColorScheme`,
  `Settings.Theme`); Neon-Schemata mit Verlauf auf Hauptknöpfen. App-Symbol aus dem Website-Logo (512 px).
- Systemseiten-Sprünge zentral in `UI/SystemPages.cs` (immer mit Rückfall).

## Übersetzung (12 Sprachen)

Wie unter Windows ist die Oberfläche **auf Deutsch geschrieben**; sichtbare Texte laufen durch `L.T("deutscher Text")`
bzw. bei Werten durch Vorlagen `L.F("Spieler {0} · {1}", …)`. Die Sprache folgt der Systemsprache (oder der
App-Sprache aus den Android-Einstellungen), unbekannte Sprachen fallen auf Englisch zurück.

- Die Bausteine in `UI/Ui.cs` (Title, Body, Caption, Section, Rich, Button, Toast …) übersetzen selbst – Texte dort
  einfach deutsch übergeben. Außerhalb davon (Dialoge, Benachrichtigung, Menü, Touch-Editor) `L.T` direkt nutzen.
- **Neue oder geänderte Texte:** in `android/i18n/*.json` eintragen (`{"deutsch": {"en": …, "es": …, …}}`) und
  `python android/i18n/generate.py` ausführen. Das Skript prüft, dass alle 11 Sprachen da sind und die Platzhalter
  `{0}` stimmen, und schreibt `L.Texts.cs` sowie `Resources/values-xx/strings.xml` (Name der Bedienungshilfe,
  Titel des Assistenten). `00-windows.json` enthält die aus der Windows-App übernommenen Texte.
- Testen ohne Systemsprache zu ändern: `adb shell cmd locale set-app-locales com.devcatskz.nconnect --locales fr-FR`,
  App neu starten.
- Der Name des virtuellen Controllers ist übersetzt („N-Connect Player 1“, „N-Connect Joueur 1“ …); für ru/ja/zh/ko
  bewusst englisch, damit Emulatoren keine Probleme mit nicht-lateinischen Gerätenamen haben.

## Release

- Version in `NConnect.Android.csproj`: `ApplicationDisplayVersion` (0.9.0) und `ApplicationVersion` (9) – bei jeder
  veröffentlichten APK erhöhen.
- Signatur: `%USERPROFILE%\.nconnect\nconnect-release.jks` + `signing.props` (wird von der csproj importiert, falls
  vorhanden). **Nie ins Repository oder in die CI** – deshalb baut die CI keine APK.
- `dotnet publish -c Release` → `bin/Release/net10.0-android/publish/com.devcatskz.nconnect-Signed.apk`, als
  `N-Connect-Android-<Version>.apk` an das GitHub-Release hängen (`gh release upload v1.0.x …`). README/Webseite
  verlinken die APK mit fester Versionsnummer – Links bei einer neuen APK anpassen.

## Bauen, installieren, testen

```powershell
cd android\NConnect.Android
dotnet build -c Debug
# Gerät gezielt (zwei Geräte per Wireless-adb verbunden):
dotnet build -t:Install -c Debug "-p:AdbTarget=-s adb-61291JEA326097-kWTmyn._adb-tls-connect._tcp"   # Pixel 10a
dotnet build -t:Install -c Debug "-p:AdbTarget=-s adb-R5CRA1GQW0Z-ECDAJk._adb-tls-connect._tcp"      # Galaxy A52s
```

- Voraussetzungen: .NET 10 SDK + Workload `android`, JDK 21 (`C:\Program Files\Microsoft\jdk-21.0.10.7-hotspot`, in der
  csproj hinterlegt – JDK 25 ist zu neu), Android-SDK unter `%LOCALAPPDATA%\Android\Sdk`.
- Die Windows-CI baut nur `Switch2Pro.Protocol.Tests` und `Switch2Pro.Bridge` – das Android-Projekt stört sie nicht.
- **Shizuku vom PC starten** (spart die Kopplung per kabellosem Debugging beim Testen):
  `adb shell <APK-Ordner>/lib/arm64/libshizuku.so` (Ordner über `adb shell pm path moe.shizuku.privileged.api`).
- **Protokoll lesen:** `adb shell "run-as com.devcatskz.nconnect cat files/nconnect.log"` (Logcat ist auf dem Pixel
  nach wenigen Minuten überschrieben).
- Screenshots: `adb shell screencap -p /sdcard/x.png` + `adb pull` (PowerShell-Umleitung verfälscht Binärdaten).
  **Nur wenn N-Connect im Vordergrund ist** – der Nutzer verwendet das Gerät nebenbei.
- `pm clear com.devcatskz.nconnect` simuliert den Erststart.

## Offene Punkte

1. Rückmeldungen zur Beta sammeln (verschiedene Hersteller, v. a. Samsung/One UI).
2. Wiederverbinden ohne SYNC über längere Zeit und nach Handy-Neustart prüfen.
3. Touch-Steuerung und Editor auf dem Gerät vollständig testen; Root-Weg testen.
4. Verteilung über F-Droid oder Play Store prüfen; Hinweis auf neue Versionen in der App.
5. Profile pro Spiel, Makros, weitere Controller-Arten (GameCube-Trigger prüfen).

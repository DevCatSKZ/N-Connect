# SKZ Connect für Windows

**Deutsch** · [English](../guide/windows.md) · Stand: Version 1.0.22

SKZ Connect verbindet Nintendo-, PlayStation- und Xbox-Controller mit Windows und gibt sie als virtuellen Xbox-360- oder
DualShock-4-Controller an Spiele, Steam und Emulatoren weiter. Probleme und Lösungen: [Hilfe](hilfe.md).

## Voraussetzungen

- Windows 10 ab Version 2004 (Build 19041) oder Windows 11, 64 Bit (x64).
- Für Switch-2-Controller einen Bluetooth-Adapter mit Bluetooth LE (ab 4.0). Pro Controller 2 und GameCube-Controller
  gehen auch per USB-Kabel. Switch-1-, Wii- und PlayStation-Controller nutzen klassisches Bluetooth.
- Eine .NET-Installation ist nicht nötig, das Programm bringt seine Laufzeit mit.

## Installieren

1. `SKZ-Connect-Setup-<Version>.exe` von der [Release-Seite](https://github.com/DevCatSKZ/SKZ-Connect/releases/latest)
   laden und ausführen.
2. Setup und Programm sind nicht code-signiert. Windows SmartScreen meldet deshalb „Der Computer wurde durch Windows
   geschützt“: „Weitere Informationen“ › „Trotzdem ausführen“.
3. Fehlen die Treiber, installiert das Setup die offiziellen, signierten Installer mit:
   [ViGEmBus](https://github.com/nefarius/ViGEmBus) 1.22.0 erzeugt die virtuellen Controller,
   [HidHide](https://github.com/nefarius/HidHide) 1.5.230.0 versteckt die Original-Controller vor Spielen. Nach der
   HidHide-Installation fragt das Setup nach einem Neustart.
4. SKZ Connect startet im Infobereich der Taskleiste. Ein Klick auf das Symbol öffnet das Fenster.

Das Setup legt außerdem die geplante Aufgabe „SKZ Connect HidHide“ an, damit das Verstecken ohne Rückfrage klappt (siehe
[Doppelte Controller](#doppelte-controller-vermeiden)). Beim Deinstallieren entfernt es Aufgabe, Autostart und die
Freigabe in HidHide; ViGEmBus und HidHide selbst bleiben installiert, weil andere Programme sie nutzen können.

Ohne Installation: [Portable Version](hilfe.md#portable-version).

## Controller verbinden

| Controller | So geht's |
|---|---|
| Switch 2: Pro Controller 2, Joy-Con 2, GameCube-Controller | Kleine SYNC-Taste kurz drücken. Nach ein paar Sekunden vibriert der Controller. Pro Controller 2 und GameCube-Controller gehen auch per USB-Kabel. |
| Switch 1: Pro Controller, Joy-Con, NES, SNES, N64, Mega Drive | SYNC-Taste drücken, bis die Lichter laufen. SKZ Connect koppelt den Controller selbst. |
| Wii-Fernbedienung | Batteriefach öffnen und die rote SYNC-Taste drücken. |
| Wii U Pro Controller | SYNC-Taste auf der Unterseite drücken. |
| DualShock 4, DualSense | PS + Teilen bzw. PS + Create halten, bis die Lichtleiste schnell blinkt; SKZ Connect koppelt selbst. Alternativ per USB-Kabel oder über die Bluetooth-Einstellungen von Windows. |
| Xbox | Wie gewohnt per USB, Bluetooth oder Xbox-Wireless-Adapter. Über Bluetooth kann auch „Controller koppeln …“ den Controller koppeln (Kopplungstaste oben halten). |
| Kabel-Controller von HORI, PowerA, PDP | USB-Kabel anstecken. |

Danach genügt beim nächsten Mal ein Tastendruck. Bis zu 8 Controller laufen gleichzeitig.

- Switch-2-Controller nicht über „Bluetooth › Gerät hinzufügen“ koppeln. Eine solche Windows-Kopplung stört die
  Verbindung; SKZ Connect entfernt sie selbst.
- Ein Switch-2-Controller merkt sich genau ein Gerät. Soll er wieder an der Switch 2 laufen, dort einmal per SYNC neu
  koppeln; danach am PC wieder SYNC drücken.
- Neue Controller sucht SKZ Connect im Hintergrund, aber nicht, solange jemand spielt. Gezielt sucht „Controller
  suchen …“ auf der Controller-Seite oder „Controller koppeln …“ im Menü des Infobereichs (eine Minute lang).
- PlayStation- und Xbox-Controller über Bluetooth, Joy-Con im Ladegriff und Kabel-Controller sind noch nicht mit echter
  Hardware geprüft. Rückmeldungen bitte als [Issue](https://github.com/DevCatSKZ/SKZ-Connect/issues).

## Fenster und Infobereich

- **Controller-Seite:** je Spieler eine Karte mit Live-Grafik, Akku und Verbindung. Knöpfe: *Vibrieren* (welcher ist
  welcher?), *Trennen* und *Einstellungen* mit den Reitern Tasten, Feineinstellung, Gyro, Joy-Con, Extras, Details und
  Test. Ansicht *Groß* oder *Kompakt*.
- **Spieler-Reihenfolge:** Ab zwei Spielern tauscht die Leiste über den Karten die Plätze. Spieler 1 ist für Windows,
  Steam und Spiele der erste Controller. Ein Klick auf den Kartentitel wählt den Platz oder benennt den Controller um.
- **Infobereich:** Rechtsklick öffnet das Menü mit je Spieler *Vibrieren*, *Spielerplatz*, *Erscheint als* und
  *Trennen*, dazu Ausgabe, Profil, Kurzanleitung und Protokoll.

## Ausgabe: Xbox 360 oder DualShock 4

- *Allgemein › Ausgabe › Controller erscheint als*: **Xbox 360** (Standard, läuft mit fast allen Spielen und Steam)
  oder **DualShock 4** (mit Bewegungsdaten für Steam und Emulatoren). Je Controller: Karte › Einstellungen › Tasten ›
  *Erscheint als*.
- *Tastenanordnung A/B/X/Y*: Xbox (nach Position, die untere Taste ist A) oder Nintendo (nach Beschriftung).
- Xbox-Controller bekommen keinen virtuellen Controller, Spiele sehen sie direkt. SKZ Connect zeigt Akku und
  Spielerplatz und ergänzt Tastatur-, Maus-, Makro- und Gyro-Belegungen.

## Tasten und Profile

- Seite *Tastenbelegung*: Jede Taste kann eine Controller-Taste, ein Tastatur-Kürzel, eine Maustaste, Turbo oder ein
  Makro auslösen, auch fertige Vorlagen wie Bildschirmfoto, Xbox Game Bar, Lautstärke oder Mikrofon stumm. Das
  Tastatur-Symbol neben einer Taste belegt sie per Tastendruck.
- **Shift-Ebene:** Solange eine Taste mit „Shift-Ebene“ gehalten wird, haben die anderen Tasten eine zweite Belegung.
- **Profile je Spiel:** Ein Profil gilt automatisch, solange eines seiner Programme im Vordergrund ist. Profile lassen
  sich als Datei exportieren und importieren.

## Sticks, Trigger, Vibration

- Seite *Sticks & Vibration*: Totzone, Kennlinie, Trigger-Schwellen (GameCube), Vibration und Stärke, Turbo-Tempo;
  eigene Werte je Controller-Art möglich.
- **Sticks kalibrieren:** Karte › Einstellungen › Feineinstellung › *Kalibrieren …*. Gemessen werden Mitte und Rand;
  die Werte speichert nur SKZ Connect, der Controller bleibt unverändert.
- **Testseite:** Reiter *Test* der Karte – Sticks mit Rundheitsanzeige, Trigger, Bewegungssensoren, Tasten,
  Berichtsrate und Vibrationstest.

## Bewegungssteuerung

- Seite *Gyro & Maus*: Gyro als rechter Stick (immer, nur beim Zielen oder per Taste), Gyro-Maus, Gyro-Beschleunigung
  und Flick-Stick. Einrichten mit Vorschau: Karte › Einstellungen › Gyro › *Einrichten …*.
- **Gyro für Emulatoren (DSU):** *Allgemein › Erweitert* (Standard an). Im Emulator, etwa Cemu oder Dolphin, als
  DSU-/Cemuhook-Quelle `127.0.0.1` und Port `26760` eintragen; Spieler 1 bis 4 belegen die Slots. Eine Änderung wirkt
  nach einem Neustart von SKZ-Connect. Belegt ein anderes Programm den Port (etwa DS4Windows), läuft SKZ Connect ohne DSU.

## Joy-Con und Wii

- Ein linker und ein rechter Joy-Con werden automatisch ein Controller. Einzeln: quer halten und SL oder SR drücken.
  Wieder zusammen: L am linken und R am rechten Joy-Con gleichzeitig drücken.
- Einzelne Joy-Con quer oder hochkant (je Joy-Con umschaltbar).
- **Joy-Con 2 als Maus:** auf die Schienenkante legen und wie eine Maus schieben.
- **Switch 1:** amiibo lesen (Pro Controller, rechter Joy-Con; als `.bin`), Ring-Con und IR-Kamera (rechter Joy-Con),
  jeweils unter Karte › Einstellungen › Extras.
- **Wii:** Nunchuk, Classic Controller und MotionPlus werden erkannt; der Zeiger kann die Maus steuern (Sensorleiste
  nötig).
- **Kopplungsdaten** (Seite *Joy-Con & Wii*): Kopplungen von einer Switch mit Bluepick_RCM/hekate übernehmen oder für
  einen anderen PC exportieren, wahlweise mit Passwort.

## Doppelte Controller vermeiden

Steam und viele Spiele kennen Switch-1-, NSO- und USB-Controller, PlayStation-Controller und Kabel-Pads selbst und
sähen sie sonst doppelt.

- *Allgemein › Ausgabe › Original-Controller verstecken* (Standard an) versteckt sie mit HidHide. Mit dem Setup läuft
  das ohne Rückfrage über die geplante Aufgabe, in der Portable-Version fragt Windows einmal nach Adminrechten. Läuft
  Steam schon, Steam einmal neu starten.
- Nachholen für einen Controller: Karte › Einstellungen › Extras › *Doppelt angezeigt?*.
- Xbox-Controller und virtuelle Controller werden nie versteckt.
- *Allgemein › Erweitert › HidHide aktuell halten* meldet neue HidHide-Versionen; installiert wird nur nach Rückfrage.

## Desktop-Modus und Widget

- **Desktop-Modus** (*Allgemein*, Standard aus): Läuft kein Spiel, steuert der Controller Windows – linker Stick Maus,
  rechter Stick Scrollen, A Klick, B Rechtsklick, X Taskansicht, Y Bildschirmtastatur, Steuerkreuz Pfeiltasten,
  LB/RB zurück/vor, Start Enter, Back Esc, HOME Startmenü, RT halten für einen genauen Zeiger. Back + Start eine Sekunde
  halten pausiert ihn; in Vollbildspielen und Programmen mit eigenem Profil ist er aus.
- **Desktop-Widget** (*Allgemein*, Standard aus): Kärtchen auf dem Desktop mit Controllern, Akku und Verbindung.
  Rechtsklick für Optionen, Doppelklick öffnet SKZ-Connect.

## Kodi auf diesem PC

*Allgemein › Kodi › DevCatSKZ Repository in Kodi einrichten* legt das Repository direkt in Kodi ab. Danach in Kodi:
Add-ons › Meine Add-ons › Add-on-Repository › DevCatSKZ Repository › Aktivieren, dann SKZ Connect daraus installieren.
Kodi sieht die Controller auch ohne Add-on als Xbox-Controller; das Add-on zeigt zusätzlich Status und Widget
(Begleitmodus). *Repository herunterladen* speichert die ZIP für ein anderes Gerät. Mehr: [Kodi](kodi.md).

## Programm und Dateien

- **Updates:** SKZ Connect sucht beim Start auf GitHub nach einer neuen Version (abschaltbar unter *Allgemein ›
  Erweitert › Nach Updates suchen*). Nach Rückfrage lädt es das Update nur von GitHub, prüft Größe und – sofern GitHub
  sie angibt – die SHA-256-Prüfsumme und installiert es.
- **Darstellung und Sprache:** Dunkel, Hell oder wie Windows, fünf Farbschemata, 12 Sprachen („Automatisch“ folgt der
  Anzeigesprache von Windows).
- **Mit Windows starten** (*Allgemein › Verbinden*, beim ersten Start an).
- **Dateien:** Einstellungen `%APPDATA%\SKZ-Connect\settings.json`, Protokoll `%LOCALAPPDATA%\SKZ-Connect\bridge.log`;
  in der Portable-Version im Ordner `data` neben der EXE.
- **Lizenzen Dritter:** *Allgemein › Erweitert* öffnet `THIRD-PARTY-NOTICES.md` aus dem Programmordner.
- **Treiber:** ViGEmBus ist seit 2023 archiviert und erhält keine Korrekturen mehr; bestehende Installationen laufen
  weiter. Startprotokoll und Diagnose nennen die installierten Versionen von ViGEmBus und HidHide.

Alle Funktionen mit ihren genauen Werten (Entwicklerdoku): [FUNKTIONEN.md](../FUNKTIONEN.md).

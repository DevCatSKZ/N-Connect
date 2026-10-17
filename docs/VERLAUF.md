# Verlauf

Kurzfassung der Entwicklung bis 1.0.22 und Erkenntnisse, die beim Weiterarbeiten wichtig sind. Was sich je Release
für Nutzer geändert hat, steht in [release-notes/](release-notes/); den aktuellen Arbeitsstand beschreibt
[../HANDOVER.md](../HANDOVER.md).

## Zeitleiste

| Datum | Stand |
|---|---|
| 03.–04.10.2026 | Windows-App im Repo `sfm` (Ordner `switch2-pro-windows/`): Switch 2 über BLE/USB, Switch 1/NSO, Wii/Wii U, Kabel-Pads, Belegung, Makros, Gyro, amiibo. Selbst koppeln (Switch 1, NSO, Wii), Xbox über XInput und DualShock 4/DualSense über HID mitverwaltet, Spieler-Reihenfolge, Stick-Kalibrierung, Gyro-Assistent, Kopplungsdaten-Fenster. Beginn des Vorhabens „Switch-Identität“ (ruht seit 06.10.). |
| 05.10. | Controller-Grafiken neu (Schultertasten, DualShock 4/DualSense, Lichtleiste in Spielfarbe), Grafiken ohne Markenzeichen; Koppeln von PlayStation (klassisch) und Xbox (BLE). |
| 06.10. | Darstellung bei 125/150 % korrigiert (fehlende DPI-Basis, zu knappe feste Breiten). Erste Tags v1.0.0/v1.0.1. |
| 07.10. | 1.0.2: zwölf Sprachen, Logo, Sprach- und Darstellungsauswahl im Setup. 1.0.4: zusätzliche Xbox-Controller durch eigene ViGEm-Pads behoben. 1.0.5: portable Variante. 1.0.6: Update auch für die portable Variante, Akku-Warnung, winget-Manifeste. 1.0.7: MIT-Lizenz, Joy-Con-Paar-Knopf in der Karte. Einen Linux-Port der Windows-App verworfen (WinForms, WinRT, ViGEmBus). |
| 10.10. | Fehlerkorrekturen, Statusleiste, Kompakt-Ansicht, Animationen; Windows-Design als Standard mit vier weiteren Farbschemata, neues Icon, eigene Skalierung (`UiScale`), Prüfhilfen auf unsichtbarem Desktop. 1.0.16: Desktop-Modus, Testseite, Einblendung beim Verbinden, Diagnose-Export, CI rendert die Oberfläche. 1.0.17: Desktop-Widget. 1.0.18: Export für ESP32/nRF52840. Am selben Tag Funkadapter-Weg verworfen, 1.0.19 blendet den Export aus. |
| 11.10. | 1.0.20: N-Connect für Android (Beta 0.9.0, eigener Signaturschlüssel) und Linux-Dienst mit Kodi-Add-on (0.3.0) für LibreELEC, Kodi-Bereich in der Windows-App. |
| 12.10. | Entwicklung direkt in `N-Connect`; `sfm` ist seitdem eingefroren. |
| 12.–15.10. | Android: DSU, Profile je Spiel, Kachel, Autostart, Update-Prüfung, Kodi-Status (erst mit 0.9.1 veröffentlicht). Kodi-Add-on 0.4.0–0.4.3: Widget, Begleitmodus für Kodi unter Windows/Android/macOS, Batocera. 1.0.21 (15.10.): Windows meldet Controller an Kodi auf demselben PC. |
| 17.10. | Analyse aller Teile, danach Überarbeitung in getrennten Strängen: Windows 1.0.22, Android 0.9.1, Kodi 0.4.4 (Einzelheiten in [release-notes/v1.0.22.md](release-notes/v1.0.22.md)). Dazu .NET 10, ein gemeinsamer DSU-Server, vollständige Lizenzhinweise, Funkadapter-Code entfernt, CI mit allen Tests und Android-Compile-Job. |

## Erkenntnisse

### Bluetooth und Koppeln

- **Viele Controller an einem Adapter:** Mit einem Barrot-Stick (BT 5.4, USB 33FA:0010) bekam ab drei bis vier
  Controllern jeder nur noch etwa 10–11 Berichte/s, ein weiterer Controller brauchte etwa 9 s zum Verbinden. Die
  Switch-2-Controller dann auf „ausgeglichen“ umzuschalten (Commit `24c4938`, zurückgenommen) brachte keine höhere
  Rate und ließ neue Verbindungen mit „Unreachable“ scheitern. Nicht wiederholen. Abhilfe ist ein stärkerer Adapter;
  mit einem Realtek-Stick (BT 5.3, USB 0BDA:A725) waren die Probleme beim Nutzer weg.
- **Herstellerbefehle an den Adapter** (Werkzeug `tools/Switch2Pro.BtIdentityProbe`): Danach verband sich kein dritter
  Switch-2-Controller mehr, bis der Stick neu eingesteckt wurde. Am 04.10. ließ ein Befehl ohne passendes
  Antwortmuster den Windows-Bluetooth-Stack hängen. Nur mit ausdrücklicher Freigabe des Nutzers.
- **Joy-Con 1 und Pro Controller 1** koppeln zuverlässig nur über WinRT (`DeviceInformationCustomPairing`, Anfrage
  sofort bestätigen); die Win32-Rückfrage kam zu spät (Fehler 1244/258). Koppeln dauert etwa 20 s, der erste
  HID-Öffnungsversuch danach scheitert manchmal, der nächste nach etwa 6 s klappt.
- **Wii:** Das Kopplungsfenster ist kurz (etwa 20 s). Im Fenster „Controller koppeln“ wird eine alte Kopplung sofort
  entfernt und bis zu 8 s neu gesucht; eine Bestätigungssuche vorher hatte am 04.10. eine funktionierende Kopplung
  zerstört. Ohne Neufund wird nicht mit veralteten Daten gekoppelt.
- **Neu koppeln im Hintergrund:** Ein bekannter Controller gilt nur als „im Kopplungsmodus“, wenn Windows ihn während
  des Suchlaufs oder in den letzten 8 s gesehen hat und er nicht in den letzten 30 s verbunden war – sonst würde ein
  eben ausgeschalteter Controller neu gekoppelt. Nach einem Fehlschlag 90 s Pause.
- **Passive BLE-Suche** wurde geprüft (90 s ohne Nintendo-Werbung) und nicht übernommen; die Suche bleibt aktiv.
- **Switch-2-Controller** merken sich genau einen Host. Jedes SYNC ersetzt die Kopplung mit der Konsole oder einem
  anderen PC – das machte jeden Test des Funkadapters teuer.

### Funkadapter (verworfen am 10.10.2026)

Ziel war, Switch-2-Controller ohne neues SYNC auch an einem zweiten PC zu nutzen: Eine Platine übernimmt die Adresse
des PC-Adapters und reicht die Controller per USB an N-Connect weiter. Gründe für das Aus: Der nRF52840 kann kein
Bluetooth-Adapter für Windows sein (nur Bluetooth LE), der Relais-Weg blieb ergebnislos (der Dongle empfing die
Werbung der Controller nicht, mit nur einem nRF-Gerät ließ sich der eigene Versuch nicht mitschneiden), und jeder Test
kostete ein Neu-Koppeln. Ein ESP32-S3 wurde nie eingesetzt. Export (1.0.18) und Einstellung sind seit 1.0.19
ausgeblendet, der Code ist in 1.0.22 entfernt. Unterlagen: [archiv/](archiv/), Referenz-Firmware
[`firmware/nrf52840/`](../firmware/nrf52840/). Stattdessen: N-Connect auf jedem PC installieren und dort einmal koppeln.

### Bewusst nicht umgesetzt

- Bluetooth-Adresse des PC-Adapters ändern (Herstellerbefehl; mit dem Barrot-Chip des Nutzers ohnehin nicht möglich).
- Kopplungsschlüssel in die Windows-Registry (`BTHPORT`) schreiben oder von dort lesen: nur mit SYSTEM-Rechten,
  undokumentiert. Die Kopplungsdaten-Funktion liest, zeigt, sichert und überträgt Daten, verändert aber weder Adapter
  noch Windows-Kopplungen.
- Echtes Widget in der Windows-11-Widgetleiste: braucht Paket-Identität (MSIX oder Sparse Package) und damit ein
  vertrauenswürdig signiertes Paket – erst mit Code-Signatur möglich.
- Bluetooth-Classic-Controller unter Android (Issue #1): Android koppelt sie selbst; mögliche Wege über hidraw/evdev
  sind ungeprüft.

### Oberfläche

- Bei Beschwerden über abgeschnittene oder gequetschte Texte immer beides prüfen: eine echte Engstelle im Layout und
  die Skalierung. Die App skaliert seit 10.10. selbst (`UiScale.Px`, alle Fenster `AutoScaleMode.None`); die Prüfhilfe
  simuliert andere Stufen mit `--scale=1.5`.
- Textgrößen der Controller-Grafiken sind fest in pt, Positionen skalieren mit `PhotoFrame`. Bei kleinen Karten lieber
  eine Aufschrift weglassen als verschieben.
- Joy-Con-Paare trennen nur noch quer gehalten mit SL/SR: Ein Nutzer hatte mit „SL + SR 1 s“ versehentlich getrennt,
  der Joy-Con lief dann einzeln mit gedrehter Belegung („im Spiel falsch“).
- Steam sah Switch-1- und NSO-Controller doppelt (Steam unterstützt sie selbst, HidHide griff damals nur bei USB und
  von Hand); seitdem versteckt N-Connect sie automatisch über HidHide.
- Akzentfarbe ist bewusst nicht die Windows-Akzentfarbe des Nutzers (war bei ihm pink).

### Android und Linux

- Android: Ein virtueller Xbox-One-S-Controller über den Shell-Befehl `hid` (UHID) funktioniert ohne Root mit
  Shizuku; DualShock 4/DualSense scheitern so, weil der Kernel-Treiber Feature-Berichte abfragt.
- Linux: Die GATT-Verbindung zu Switch-2-Controllern läuft über einen eigenen ATT-Socket, weil BlueZ an den
  „Secondary Services“ hängen bleibt. Einzelheiten in [LINUX-KODI.md](LINUX-KODI.md).
- `kodi-repo` wird von Hand gepflegt: Deploy-Keys sind in der GitHub-Organisation abgeschaltet, ein Token als Secret
  gibt es nicht.

### Tests

- Einmal schlugen drei Tests fehl, danach liefen sie wiederholt grün. Am 17.10. liefen alle Protokoll-Tests 15-mal
  hintereinander grün; der Fehler ließ sich nicht nachstellen.

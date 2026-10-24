# Übergabe: SKZ Connect

Stand: 24.10.2026. Release 1.0.22 ist veröffentlicht; die Umbenennung von N-Connect zu SKZ Connect ist überall
abgeschlossen (Repos, Releases, Add-ons, Webseite, lokale Ordner) und das Icon trägt jetzt die DevCat-Katze
(siehe „Icon und Branding“).

SKZ Connect lässt Nintendo-Switch-2-Controller (Pro Controller 2, Joy-Con 2, GameCube-Controller) und weitere
Controller (Switch 1 und NSO, Wii und Wii U, PlayStation, Xbox, lizenzierte Kabel-Pads) unter Windows als
Xbox-360- oder DualShock-4-Controller erscheinen. Dazu gibt es SKZ Connect für Android (Beta, nur Switch-2-Controller
über Bluetooth LE, virtueller Xbox-Controller per Shizuku oder Root, Touch-Steuerung) und SKZ Connect für
Kodi/LibreELEC (Linux-Dienst und Kodi-Add-on, Begleitmodus unter Kodi für Windows, Android und macOS).

- Regeln für alle Agenten: [AGENTS.md](AGENTS.md)
- Entwicklerdoku (Funktionen, Architektur, Protokolle, Android, Linux/Kodi): [docs/README.md](docs/README.md)
- Bisheriger Verlauf und Erkenntnisse: [docs/VERLAUF.md](docs/VERLAUF.md)
- Release-Notizen: [docs/release-notes/](docs/release-notes/)

## Stände

| Teil | Version im Code | Zuletzt veröffentlicht |
|---|---|---|
| Windows-App | 1.0.22 (`src/Switch2Pro.Bridge/Switch2Pro.Bridge.csproj`) | 1.0.22 (17.10.2026) |
| Android | 0.9.1 Beta, versionCode 10 (`android/SkzConnect.Android/SkzConnect.Android.csproj`) | 0.9.1 (APK an v1.0.22) |
| Kodi `service.skzconnect` mit Dienst `skzconnect` | 0.4.6 (`linux/kodi/service.skzconnect/addon.xml`, `linux/SkzConnect.Linux/SkzConnect.Linux.csproj`) | 0.4.6 |
| Kodi `plugin.skzconnect.status` / `script.skzconnect.bundle` / `repository.devcatskz` | 1.1.3 / 1.0.4 / 1.0.4 | 1.1.3 / 1.0.4 / 1.0.4 |

## Repositories und Ordner

| Ort | Zweck |
|---|---|
| [DevCatSKZ/SKZ-Connect](https://github.com/DevCatSKZ/SKZ-Connect) (öffentlich) | Führendes Repo, Branch `main`. Releases entstehen über Tags `v<Version>`. |
| Branch `gh-pages` im selben Repo | Webseite <https://devcatskz.github.io/SKZ-Connect/>; lokal als Arbeitskopie `..\SKZ-Connect-pages`. |
| [DevCatSKZ/kodi-repo](https://github.com/DevCatSKZ/kodi-repo) (öffentlich) | „DevCatSKZ Repository“ für Kodi (auch über <https://devcatskz.github.io/kodi-repo/>). Wird beim Release von Hand aktualisiert, keine CI-Automatik. Lokal: `..\kodi-repo`. Eigene `AGENTS.md`. |
| `sfm` (lokal `..\sfm`) | Frühere Entwicklungsquelle (Ordner `switch2-pro-windows/`), seit 12.10.2026 eingefroren auf 1.0.20. Nicht mehr benutzen, nichts daraus übernehmen. |
| `..\.wt\<Name>` | Worktrees für parallele Arbeitsstränge (siehe AGENTS.md). |
| `..\vm` | LibreELEC-Abbilder für QEMU-Tests ohne Installation (siehe docs/LINUX-KODI.md). |

## Bauen und testen

Voraussetzungen: .NET 10 SDK. Für die Android-App zusätzlich die Workload `android`, JDK 21 (JDK 25 ist zu neu) und
das Android-SDK unter `%LOCALAPPDATA%\Android\Sdk`. Für das Setup Inno Setup 6, für Übersetzungen und Kodi-Tests
Python 3.

```powershell
dotnet build Switch2Pro.sln -c Release                  # Warnungen gelten als Fehler
dotnet test Switch2Pro.sln -c Release                   # alle .NET-Tests (Protokoll, Windows, Linux, Android ohne Workload)
py -3 -m unittest discover -s linux/kodi/tests          # Logik der Kodi-Add-ons
py -3 android/i18n/generate.py --check                  # Android-Übersetzungen vollständig und aktuell
dotnet build android/SkzConnect.Android -c Debug          # Android-App (braucht die Workload)
dotnet publish src/Switch2Pro.Bridge -c Release -r win-x64 --self-contained -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -p:DebugType=none `
  -o out\publish\win-x64
ISCC.exe installer\SKZ-Connect.iss                        # Setup nach out\ (ViGEmBus-/HidHide-Installer in installer\redist\)
powershell -File linux/build-kodi-addon.ps1             # Kodi-ZIPs nach out\kodi
```

Die Zahl der Tests steht bewusst nirgends fest; maßgeblich ist die Ausgabe von `dotnet test`. Die CI
(`.github/workflows/build.yml`, `kodi.yml`) führt dieselben Schritte aus, baut die Android-App ohne Signatur und
benennt Builds ohne Tag als Vorabversion `<Version>-ci.<Lauf>`.

Wichtigste Quelle bei Hardwareproblemen ist das Protokoll `%LOCALAPPDATA%\SKZ Connect\bridge.log` (Android: Hilfe ›
Diagnose › Teilen; Linux: siehe docs/LINUX-KODI.md). Die Prüfhilfen `--render`, `--render-ui` und `--dump-ui` öffnen
Fenster und schreiben in dasselbe Protokoll – nur nach Rückfrage beim Nutzer starten.

## Icon und Branding

Einzige Quelle für das Logo ist `src/Switch2Pro.Bridge/Branding.cs` (`DrawLogo` zeichnet es als Vektor – deshalb
bitte keine Pixeldateien von Hand nachbearbeiten). Fenster, Infobereich und die UI zeichnen das Logo zur Laufzeit
direkt daraus; die eingebettete ICO ist nur die Rückfallebene.

Nach einer Änderung am Logo alle Dateien neu erzeugen und verteilen:

```powershell
dotnet build src/Switch2Pro.Bridge -c Release
src\Switch2Pro.Bridge\bin\Release\net10.0-windows*\SKZ-Connect.exe --render-brand installer\art  # kein Fenster
copy installer\art\SKZ-Connect.png installer\art\SKZ-Connect.ico src\Switch2Pro.Bridge\
py -3 android\make_icons.py                                     # Android-Mipmaps aus src\...\SKZ-Connect.png
copy installer\art\SKZ-Connect.png ..\kodi-repo\service.skzconnect\icon.png       # ebenso
copy installer\art\SKZ-Connect.png ..\kodi-repo\plugin.skzconnect.status\icon.png # plugin… und
copy installer\art\SKZ-Connect.png ..\kodi-repo\script.skzconnect.bundle\icon.png # script…
copy installer\art\SKZ-Connect.png ..\SKZ-Connect-pages\logo.png                  # Webseite
```

`--render-brand` schreibt `SKZ-Connect.png`/`.ico` und die sechs `wizard-*.bmp` (Inno Setup wählt je Skalierung).
README und `addon.xml` der Add-ons verweisen bereits auf diese Dateien; Kodi-ZIPs ziehen das Icon beim Bauen aus
`installer/art` mit (`build-kodi-addon.ps1`). Die Add-on-ZIPs im kodi-repo behalten ihr altes Icon bis zum nächsten
Add-on-Build – die Ordner-`icon.png` (Repo-Ansicht) sofort mitziehen.

## Release-Ablauf

Jeder Schritt, der etwas nach außen sichtbar macht (Push, Tag, Release, kodi-repo, Webseite, winget), nur mit
Freigabe des Nutzers.

1. **Versionen erhöhen:** Windows `<Version>` in `src/Switch2Pro.Bridge/Switch2Pro.Bridge.csproj` (muss zum Tag
   passen, sonst warnt die CI). Bei einer neuen APK `ApplicationDisplayVersion` und `ApplicationVersion` in
   `android/SkzConnect.Android/SkzConnect.Android.csproj`. Bei Kodi-Änderungen `version` und `news` in
   `linux/kodi/<id>/addon.xml` und die Version in `linux/SkzConnect.Linux/SkzConnect.Linux.csproj`; ist
   `service.skzconnect` oder das Plugin neu, auch `script.skzconnect.bundle` erhöhen.
2. **Release-Text** `docs/release-notes/v<Version>.md`: erst Deutsch, dann Englisch, Download-Tabelle, je Plattform
   „Neu“ und „Behoben“. Die CI übernimmt ihn als Beschreibung des Releases.
3. **Prüfen:** Bauen und alle Tests wie oben, Commit auf `main`, Push.
4. **Tag:** `git tag v<Version>` und `git push origin v<Version>`. `build.yml` baut Setup und Portable-ZIP und legt
   das Release an; `kodi.yml` hängt die `SKZ-Connect-Kodi-*.zip` an.
5. **APK (nur lokal):** `dotnet publish android/SkzConnect.Android -c Release` signiert mit dem Schlüssel aus
   `%USERPROFILE%\.skzconnect` (über `signing.props`); prüfen mit `apksigner verify --print-certs`. Die Datei
   `bin/Release/net10.0-android/publish/com.devcatskz.nconnect-Signed.apk` als `SKZ-Connect-Android-<Version>.apk`
   anhängen (`gh release upload v<Version> …`) – der Name ist Pflicht, die Update-Prüfung der App liest die Version
   daraus. Übergangsweise zusätzlich eine Kopie als `N-Connect-Android-<Version>.apk` anhängen: App-Versionen bis
   0.9.1 suchen nur nach dem alten Namen. SHA-256 und Signatur der APK in die Release-Beschreibung aufnehmen
   (wie bei 1.0.20).
6. **kodi-repo von Hand:** `git -C ..\kodi-repo status` (keine offenen Altlasten), `git -C ..\kodi-repo pull`, dann
   `powershell -File linux/build-kodi-addon.ps1 -Repo -RepoDir ..\kodi-repo`. Für byte-gleiche ZIPs wie am Release das
   CI-Artefakt „Kodi-Plugin“ entpacken und `-Repo -NoBuild -Out <Ordner> -RepoDir ..\kodi-repo` nehmen. Das Skript ruft
   im kodi-repo das Index-Werkzeug `tools/build_repo.py` auf (`addons.xml`, Prüfsummen, `index.html`); schon
   veröffentlichte Versionen überschreibt es nie. Danach `git status` prüfen, als devcatskz committen, pushen.
7. **Webseite** (`gh-pages`): Versionsnummer und Download-Links holt die Seite selbst über die GitHub-API; feste
   APK-Links in README und Webseite anpassen. Neue Screenshots nur nach Rückfrage (die Prüfhilfe öffnet Fenster).
8. **winget:** Manifeste in `winget/` mit den Prüfsummen der fertigen Release-Dateien aktualisieren und bei
   `microsoft/winget-pkgs` einreichen, mit dem Konto des Nutzers ([winget/README.md](winget/README.md)).
9. **Nachziehen:** Tabelle „Stände“ oben, bei größeren Schritten [docs/VERLAUF.md](docs/VERLAUF.md).

## Bekannte Grenzen und offene Punkte

### Umbenannte Kennungen (seit 23.10.)

Alle „nconnect"-Kennungen wurden auf „skzconnect" umgestellt: Kodi-Add-ons (`service.skzconnect`,
`plugin.skzconnect.status`, `script.skzconnect.bundle`), .NET-Projekte (`SkzConnect.*`), das
Linux-Binärprogramm `skzconnect`, der Datenordner `~/.config/skzconnect` (wird einmalig aus
`~/.config/nconnect` übernommen) und die Android-Einstellungsdatei (wird einmalig migriert).
Auf GitHub steht kein „nconnect" mehr in Pfaden, Repos, Releases (Titel+Texte) oder der Webseite.

Bewusst **behalten** (brechen sonst bestehende Installationen):

- `applicationId com.devcatskz.nconnect` samt Provider-Authorities – die Paketkennung der
  installierten Android-App; ändern bedeutet: keine Updates, Datenverlust.
- Update-Check liest weiter die alten Asset-Namen `N-Connect-*` (Windows + Android); `build.yml`
  legt Release-Dateien unter beiden Namen ab. Kann entfallen, sobald keine Installation < 1.0.23
  mehr im Umlauf ist.
- `PairingTransfer.FormatId`/`FileExtension` („n-connect-pairings"/“.ncpair") – Wire- und
  Dateiformat; Exporte und Geräte alter Versionen müssen kompatibel bleiben.
- Legacy-Zeichenfolgen für Migration: Registry `N-Connect`, Autostart-Eintrag und Datenordner
  der Vorversion (Dialogs.cs, Log.cs).
- `service.nconnect` als alte Add-on-Kennung: `KodiIntegration.FindDataDirs` schreibt
  die status.json auch in `addon_data/service.nconnect`, solange das alte Add-on noch
  installiert ist. Alter Kodi-Installationen bekommen keine Add-on-Updates mehr – Umstieg
  über das neue Bundle (`script.skzconnect.bundle`) oder das LibreELEC-Zip.
- Signatur-Alias im Android-Keystore heißt intern `nconnect` (liegt nur lokal unter
  `%USERPROFILE%\.skzconnect`, nicht im Repository).


### Code-Signatur fehlt

Es gibt keine Repo-Secrets. Setup und EXE sind unsigniert, Windows zeigt die SmartScreen-Warnung. `build.yml`
signiert App und Setup automatisch, sobald `SIGNING_CERT_PFX` (PFX als Base64) und `SIGNING_CERT_PASSWORD` im Repo
hinterlegt sind. Mögliche Wege: SignPath (kostenlos für Open-Source-Projekte, eigener Antrag und eigene Action) oder
Microsoft Trusted Signing (kostenpflichtig, statt PFX eine eigene Action); Bedingungen vor der Wahl prüfen. Die
Code-Signatur ist auch Voraussetzung für ein echtes Windows-11-Widget und für jeden eigenen Treiber.

### ViGEmBus ist archiviert

ViGEmBus, der Kerneltreiber für die virtuellen Controller unter Windows, ist seit dem 02.11.2023 archiviert (Ende
des Projekts nach einem Markenstreit, keine Updates mehr). Laut Abschluss-Erklärung des Autors laufen bestehende
Installationen weiter. SKZ Connect installiert das letzte signierte Release 1.22.0 (die CI prüft die Signatur);
Startprotokoll und Diagnose nennen die installierte Treiberversion. HidHide wird weiter gepflegt.

- **Risiko:** keine Fehler- oder Sicherheitskorrekturen mehr. Lädt Windows den Treiber nach einem Update oder einer
  geänderten Treiberrichtlinie nicht mehr, erzeugt SKZ Connect unter Windows keine virtuellen Controller; Android und
  Kodi sind nicht betroffen.
- **Nachfolger und Alternativen** (Websuche 17.10.2026):
  - Nefarius VirtualPad, das Folgeprojekt des ViGEm-Autors, ist kommerziell und nur für Geschäftspartner – für
    SKZ Connect nicht nutzbar.
  - Ein eigener Treiber über das Virtual HID Framework (VHF, Teil von Windows, User-Mode) kann HID-Gamepads anlegen,
    etwa einen DualShock 4 oder einen Xbox-One-/Series-Controller als HID, aber keinen Xbox-360-Controller (XUSB). Ob
    XInput-Spiele einen solchen Xbox-HID-Controller zuverlässig sehen, ist ungeprüft. Ein Treiberpaket muss signiert
    sein.
  - libvirtualhid (LizardByte, für Sunshine) geht diesen VHF-Weg fertig, der Treiber steht aber unter einer
    Source-Available-Lizenz und braucht für Gamepads einen Lizenzschlüssel – derzeit keine Option.
- **Bewertung:** Vorerst bei ViGEmBus 1.22.0 bleiben und Fehlerberichte nach Windows-Updates beobachten. Fällt der
  Treiber aus, ist ein eigener VHF-Treiber der realistische Ersatz, mit Verlust der Xbox-360-Ausgabe; Voraussetzung
  ist die Code-Signatur.

### Weitere Punkte

- **Switch-Identität ruht** seit 06.10.2026 (Joy-Con an Switch und PC ohne neues Koppeln, mit Bluepick_RCM; kein
  Hardwaretest). Unterlagen: [docs/UEBERGABE-SWITCH-IDENTITAET.md](docs/UEBERGABE-SWITCH-IDENTITAET.md). Nur nach
  Rücksprache fortsetzen.
- **Android unterstützt kein Bluetooth Classic** (Issue #1): Switch-1-Controller und Nachbauten laufen direkt über
  Android, meist nur mit Tasten und Sticks. Issue #1 ist noch unbeantwortet; die Windows-Seite (Geister-DualShock-4,
  HidHide) ist in 1.0.22 behoben, für die Vibration des Klons fehlt ein neues Diagnose-ZIP.
- **Kodi:** Auf echter Hardware geprüft ist nur der Raspberry Pi 4 mit LibreELEC 12.2.1. Alles andere ist ungetestet:
  Pi 2/3/5, andere Boxen, PC, Batocera, das Koppeln von Xbox-, PlayStation-, Switch-1- und Wii-Controllern, die
  Darstellung des Begleitmodus unter Kodi für Windows und Android, das Lesen der Android-Statusdatei durch Kodi ab
  Android 11. Nach einer Neuinstallation der Android-App kann die Statusdatei unter einem Ausweichnamen landen, den
  das Add-on nicht liest (die App weist in Hilfe › Kodi darauf hin).
- **Lizenzfragen ungeklärt:** `src/Switch2Pro.Protocol/Nfc.cs` und `IrCamera.cs` folgen im Ablauf und in den
  Konstanten den Emulatoren yuzu/Citron (GPL). THIRD-PARTY-NOTICES.md führt das als reine Protokollquelle; ob
  tatsächlich kein Code übernommen wurde, ist nicht geprüft. Die Controller-Umrisse (`InputView.Outlines.cs`) wurden
  aus Produktfotos von Wikimedia Commons gewonnen; welche Fotos und welche Lizenzen, ist nicht festgehalten.
- **Mit echter Hardware ungeprüft (Windows):** DualShock 4/DualSense über USB und Bluetooth, Xbox über Bluetooth und
  Wireless-Adapter, Kabel-Pads von HORI/PowerA/PDP, Joy-Con 1 im Ladegriff per USB, Gyro-Extras und Rückkanal in
  Spielen, Spieler-Reihenfolge mit Steam. Aus 1.0.22 zusätzlich: DualShock-4-Ausgabe ohne Geister-Spieler,
  Switch-1-Vibration, Verstecken per UAC in der portablen Variante. Akkuwerte von Joy-Con 2 und GameCube-Controller
  sind geschätzt.
- **Mit echtem Gerät ungeprüft (Android 0.9.1):** Root- und Touch-Weg vollständig, Samsung-Geräte, Kachel ab
  Android 12, Autostart, echtes Update, DSU mit Emulatoren. Liste: [docs/ANDROID.md](docs/ANDROID.md).
- **winget:** Manifest `DevCatSKZ.SKZ-Connect` 1.0.22 ist bei `microsoft/winget-pkgs` eingereicht
  (PR #460289, Ersteinreichung als neues Paket – CLA-Klick ggf. nur durch den Kontoinhaber).
- **Webseite:** Screenshots zeigen noch den Stand 1.0.14.
- **Toter Code:** `src/Switch2Pro.Protocol/WindowsBtDevice.cs` (`WindowsBtDevice`, `BtDeviceNames`) nutzen seit dem
  Entfernen des ESP32-Exports nur noch Tests.
- **kodi-repo-Historie:** Ältere Commits enthalten Einrichtungsnotizen und einen Playlist-Dateinamen. Ob die Historie
  bereinigt wird, entscheidet der Nutzer.

# Übergabe: Funktion „Switch-Identität“ (N-Connect + Bluepick_RCM)

Stand: 04.10.2026, ca. 09:40. Für den nächsten Agenten, der an dieser Funktion weiterarbeitet.

**Zuerst lesen:** [SWITCH-IDENTITAET.md](SWITCH-IDENTITAET.md) – §0 (aktueller Stand), §5d (gewählter Weg),
§5 (Arbeitsprotokoll inkl. Vorfall), §6 (offene Punkte). Diese Übergabe fasst nur zusammen und sagt, was als
Nächstes zu tun ist.

---

## 1. Worum es geht (in drei Sätzen)

Der Nutzer will Switch-1-Joy-Con (später auch Pro Controller) abwechselnd an der Switch und am PC nutzen, **ohne
neu zu koppeln**. Gewählte Lösung: Der Joy-Con wird normal mit Windows gekoppelt; **Bluepick_RCM** (RCM-Payload)
liest PC-Adresse und Schlüssel aus dem Joy-Con und richtet die Switch so ein, dass sie sich als dieser PC ausgibt
(Atmosphère-Datei `bluetooth_devices.db` + MissionControl `host_address`). **N-Connect** (Windows-App) soll das
später erkennen, anzeigen und unterstützen.

## 2. Projekte und Pfade

| Was | Pfad | Versionskontrolle |
|---|---|---|
| N-Connect (maßgebliche Kopie) | `C:\Coding\Switch 2 Pro Controler Bluetooth Treiber\N-Connect` | Git, Branch `main` |
| N-Connect (alte Doppelkopie, **nicht** benutzen) | `…\Switch 2 Pro Controler Bluetooth Treiber\sfm\switch2-pro-windows` | eigenes Git |
| Bluepick_RCM | `C:\Coding\Bluepick_RCM\Bluepick_RCM Project` | **kein Git** |
| Bluepick-Agentendoku | `Bluepick_RCM Project\AGENTS.md` (Menü-Indizes, Rezepte, Fallstricke) | – |
| Neue Payload 1.0.6 | `Bluepick_RCM Project\output\Bluepick_RCM.bin` (102 357 B) | – |
| Backup alte Payload 1.0.5 von der Switch | `Bluepick_RCM Project\archive\old_deployments\Switch-Backup_2026-10-04\` | – |
| Testdaten von der Switch des Nutzers | `Bluepick_RCM Project\archive\testdaten_switch_2026-10-04\` | – |

**Achtung, parallele Arbeit am N-Connect-Repo:** Während dieser Sitzung hat eine andere Sitzung weiter
committet (04.10.2026 11:48–12:37, zuletzt `898100a`; u. a. `bb48a21` „Kopplungsdaten im Windows-11-Stil,
Joy-Con-1-Kopplung über WinRT“ – betrifft `PairingDataForm.cs`). Laut `HANDOVER.md` wird N-Connect als
**öffentliches** Repo `DevCatSKZ/N-Connect` veröffentlicht. Die Dateien dieser Funktion sind noch untracked;
vor einem Commit mit dem Nutzer klären, ob sie veröffentlicht werden sollen, und `git status` prüfen. Die
Entwicklerdoku-Übersicht `docs/README.md` (von der anderen Sitzung) verlinkt diese Dateien noch nicht.

**Achtung Testdaten:** enthalten echte Bluetooth-Adressen und Kopplungsschlüssel des Nutzers. Nie ins
N-Connect-Repo kopieren, nie in Doku oder Chat im Klartext ausgeben (Schlüssel maskieren, wie das Prüfwerkzeug
es tut). Die Doku verwendet Platzhalter.

## 3. Was fertig ist

**Bluepick_RCM 1.0.6** (gebaut, offline geprüft, **nicht auf der Switch getestet**):
- `source/keys/keys.c`, Abschnitt „N-Connect: the Switch takes over the PC's Bluetooth identity“:
  `bt_nconnect_enable`, `bt_nconnect_disable`, Hilfsfunktionen `_nc_*` (INI setzen, Datenbank schreiben,
  `_nc_active()`).
- Schutz: `bt_full_auto`, `bt_atmo_disable`, `bt_atmo_purge_db` lassen die externe Datenbank in Ruhe, solange
  `_nc_active()` gilt.
- `source/main.c`: neue Menüzeilen 23–25; Grey-out-Indizes jetzt hekate **32**, RCM **35** (in AGENTS.md §4
  nachgeführt). `source/keys/keys.h`: Deklarationen. `Versions.inc`: 1.0.6.
- Offline-Prüfung: Ein aus `joycon_mac.bin` erzeugter Datenbankeintrag ist Byte für Byte identisch mit dem
  Eintrag, den HOS selbst im Save speichert (Testdaten, Joy-Con damals mit der Switch gekoppelt).

**N-Connect** (alles neu, noch nicht committet, untracked):
- `src/Switch2Pro.Protocol/SwitchBtSave.cs` – liest Geräteeinträge aus `8000000000000050.bin`.
  Achtung: nutzt noch die alten Offsets ab Tabellenanfang `0x58000`-Raster (0x70/0x99/0x146); richtig
  verstanden ist die Struktur ab `0x58070` (Adresse +0x00, Schlüssel +0x29, Name +0xD6). Ergebnis ist
  gleich, die Doku im Code sollte aber angeglichen werden.
- `tools/Switch2Pro.BtIdentityProbe/` – Prüfwerkzeug für Herstellerbefehle an den PC-Bluetooth-Chip
  (`info`, `sd`, `csr-lesen`, `csr-setzen`, `bcm-setzen`). Gehört zum **verworfenen** Weg (PC-Adapter
  umadressieren); behalten als Diagnose, aber siehe Regel 3 unten.
- `docs/SWITCH-IDENTITAET.md`, diese Datei.

## 4. Sofort als Nächstes

1. **Payload aufspielen.** `output\Bluepick_RCM.bin` nach `switch/Bluepick_RCM/Bluepick_RCM.bin` auf die SD.
   - Per MTP (Switch unter Horizon mit DBI o. ä.): **alte Datei erst löschen**, dann kopieren – `CopyHere`
     überschreibt nicht und meldet trotzdem keinen Fehler.
   - Die MTP-Verbindung war instabil (Gerät verschwindet, wechselt zwischen „Nintendo Switch“/„microSD card“ und
     „Switch“/„1: SD Card“). Nicht automatisch löschen+kopieren, wenn die Verbindung wackelt. Besser: Nutzer
     kopiert im Explorer, oder SD per hekate „USB Tools → SD Card“ als Laufwerk.
   - Danach **zurücklesen und SHA-256 mit `output\Bluepick_RCM.bin` vergleichen.**
2. **Hardwaretest** nach SWITCH-IDENTITAET.md §5d „Testablauf“ – macht der Nutzer an der Hardware, Agent
   begleitet. Wichtig: Während die Switch-Seite getestet wird, muss das PC-Bluetooth aus sein (gleiche Adresse);
   der Nutzer hat BT-Tastatur und -Maus am selben Adapter → vorher kabelgebundene Eingabe bereitlegen lassen.
3. Ergebnis in SWITCH-IDENTITAET.md §0 und §5 (Protokoll) eintragen.

## 5. Danach (Reihenfolge nach Absprache mit dem Nutzer)

- Testbefund „Joy-Con unter HOS an die Schiene stecken“ auswerten (neue Kopplung? → ggf. Rückweg nötig).
- N-Connect: Switch per MTP erkennen (VID `057E`, nicht über Namen), `switch/Bluepick_RCM/nconnect.ini`
  anzeigen, Hinweis „Switch aus, wenn am PC gespielt wird“. Vorhandene Bausteine: `SwitchCardWatcher.cs`
  (nur Laufwerke), `SwitchPairingData.cs`, `PairingDataForm.cs` (dort stehen Texte, die den neuen Weg noch
  ausschließen – Stand Commit `898100a` Zeilen 287–288 und 360 – anpassen).
- Pro Controller: Schlüssel aus Windows lesen (`HKLM\SYSTEM\CurrentControlSet\Services\BTHPORT\Parameters\Keys`,
  nur SYSTEM, nur lesend) und an Bluepick übergeben (z. B. Datei auf der SD, die Bluepick in die Datenbank
  übernimmt). Byte-Reihenfolge des Windows-Schlüssels erst prüfen.
- `SwitchBtSave.cs` auf die Struktur ab `0x58070` umstellen, Tests in `tests/Switch2Pro.Protocol.Tests`.
- Commits (N-Connect) erst nach Rücksprache.

## 6. Bauen

```powershell
# Bluepick_RCM (devkitARM)
cd "C:\Coding\Bluepick_RCM\Bluepick_RCM Project"
c:\devkitPro\msys2\usr\bin\make.exe -j4
# Erwartet: Payload < 126 296 B, unkomprimiert < 140 288 B. Warnung „_derive_keys defined but not used“ ist alt.

# N-Connect (.NET 8, SDK 9/10 installiert)
cd "C:\Coding\Switch 2 Pro Controler Bluetooth Treiber\N-Connect"
dotnet build tools\Switch2Pro.BtIdentityProbe -c Release
dotnet test tests\Switch2Pro.Protocol.Tests
```

## 7. Regeln aus dieser Sitzung (vom Nutzer bzw. aus Fehlern)

1. **Sprache:** Deutsch, einfach und verständlich.
2. **Alles dokumentieren:** Der Nutzer will zu jedem Schritt eine Dokumentation → SWITCH-IDENTITAET.md
   (Protokoll, §0) fortlaufend pflegen; Bluepick-Änderungen auch in `AGENTS.md`.
3. **Keine Herstellerbefehle an den PC-Bluetooth-Adapter ohne ausdrückliche Freigabe.** Vorfall am 04.10.:
   Ein Befehl ohne passendes Antwortmuster ließ den Windows-Stack in eine Zeitüberschreitung laufen, Bluetooth
   fiel aus (Tastatur/Maus des Nutzers sind BT), bis der Adapter umgesteckt wurde. Adapter des Nutzers:
   Barrot `0x08E7` – der Weg „PC-Adapter umadressieren“ ist dort ohnehin zu.
4. **Vor dem Überschreiben auf der Switch sichern** (wie mit dem Payload-Backup geschehen).
5. **Keine Schlüssel/Adressen in Repo oder Doku.**
6. Bluepick-UI-Texte sind Englisch (wie der Rest des Payloads), Doku Deutsch.

## 8. Nützliche Schnipsel

Switch per MTP lesen (Windows-Shell, nur lesend):
```powershell
$sh = New-Object -ComObject Shell.Application
$dev = $sh.NameSpace(17).Items() | Where-Object { $_.Name -match 'Switch' } | Select -First 1
$sd  = $dev.GetFolder.Items() | Where-Object { $_.Name -match 'SD Card$|microSD card' -and $_.Name -notmatch 'install' } | Select -First 1
$bp  = $sd.GetFolder.ParseName('switch').GetFolder.ParseName('Bluepick_RCM').GetFolder
$sh.NameSpace('<Zielordner>').CopyHere($bp.ParseName('Bluepick_RCM.bin'), 0x14)
```

Befehl mit Adminrechten starten (UAC beim Nutzer) und Ausgabe lesen:
```powershell
Start-Process powershell.exe -Verb RunAs -Wait -WindowStyle Hidden -ArgumentList '-NoProfile','-Command',"& '<exe>' <args> *> '<ausgabe.txt>'"
```

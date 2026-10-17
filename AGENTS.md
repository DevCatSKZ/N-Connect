# Regeln für Agenten

Gilt für alle Agenten, die in diesem Repo arbeiten. Stand und Abläufe: [HANDOVER.md](HANDOVER.md), Entwicklerdoku:
[docs/README.md](docs/README.md).

## Identität und Commits

- Autor und Committer ist immer `devcatskz <devcatskz@gmail.com>` (Repo-Konfiguration prüfen: `git config user.name`,
  `git config user.email`). Keine anderen Namen oder Adressen, auch nicht die des Windows-Benutzerkontos.
- Keine Zeilen `Co-Authored-By: …` und keine Hinweise wie „Generated with …“ in Commit-Nachrichten, Pull Requests
  oder Release-Texten – gleich, welcher Agent oder welches Werkzeug arbeitet.
- Commit-Nachrichten auf Deutsch, knapp: erste Zeile, was sich ändert; darunter bei Bedarf, warum.
- Nichts pushen, taggen, veröffentlichen oder auf GitHub schreiben (Releases, Issues, Pull Requests, kodi-repo,
  Webseite, winget) ohne ausdrückliche Freigabe des Nutzers. Die Historie nicht umschreiben.

## Umgang mit dem Nutzer

- Antworten an den Nutzer auf Deutsch, einfach und verständlich.
- Vor jedem Lauf von `N-Connect.exe --render`, `--render-ui` oder `--dump-ui` fragen: Die Prüfhilfen öffnen Fenster,
  Fenster und Maus springen, der Nutzer arbeitet parallel am PC.
- Keine Herstellerbefehle an den Bluetooth-Adapter des PCs und nichts an seinen Mikrocontrollern (ESP32-S3,
  nRF52840) flashen oder über die serielle Schnittstelle ansprechen.
- Android-Geräte per adb nur bedienen, wenn N-Connect im Vordergrund ist.

## Code und Qualität

- Warnungen gelten als Fehler (`TreatWarningsAsErrors` in `Directory.Build.props`). Warnungen beheben, nicht
  unterdrücken.
- Vor jedem Commit: `dotnet build Switch2Pro.sln -c Release` und `dotnet test Switch2Pro.sln -c Release`; bei
  Änderungen an Android bzw. Kodi zusätzlich die Schritte aus HANDOVER.md („Bauen und testen“).
- Logik, die Bytes zerlegt, Werte umrechnet oder entscheidet, gehört nach `src/Switch2Pro.Protocol` und bekommt
  Tests. Die Apps enthalten nur Anbindung ans Betriebssystem und Oberfläche ([docs/ARCHITEKTUR.md](docs/ARCHITEKTUR.md)).
- Sichtbare Texte stehen auf Deutsch im Code und werden in allen 12 Sprachen gepflegt: Windows über `Tr.Texts.cs`
  und `Tr.Lang*.cs` (geprüft von `TranslationTests`), Android über `android/i18n/*.json` und `generate.py`, Kodi über
  `linux/kodi/i18n/generate.py`.

## Worktree-Arbeitsweise

- Parallele Arbeitsstränge laufen in eigenen Worktrees neben dem Repo-Ordner (`..\.wt\<Name>`), je Strang ein Branch:
  `git worktree add ..\.wt\<Name> -b <Branch> <Basis>`.
- Jeder Strang schreibt nur in seinem vereinbarten Bereich und committet in seinem Worktree. Andere Worktrees nur
  lesen.
- Zusammengeführt wird in einen Release-Branch (zuletzt `release/1.0.22`), danach nach `main`. Konflikte löst, wer
  zusammenführt; er prüft danach Build und Tests.
- Pfade mit Leerzeichen immer in Anführungszeichen. Der Projektordner heißt
  `C:\Coding\Switch 2 Pro Controler Bluetooth Treiber`.

## Release

Der Ablauf Schritt für Schritt steht in [HANDOVER.md](HANDOVER.md#release-ablauf). Kurz: Versionen erhöhen,
Release-Text unter `docs/release-notes/v<Version>.md`, Tests grün, Tag `v<Version>` (die CI baut und legt das Release
an), APK lokal signieren und anhängen, kodi-repo von Hand mit `linux/build-kodi-addon.ps1 -Repo`, Webseite,
winget-Manifeste. Jeder Schritt nach außen nur mit Freigabe.

## Geheimnisse und private Daten

- Signaturschlüssel nie ins Repo, in Logs oder in die CI: Der Android-Schlüssel liegt nur unter
  `%USERPROFILE%\.nconnect` (`nconnect-release.jks`, `signing.props`); ein Code-Signatur-Zertifikat gehört nur als
  Secret in die Repo-Einstellungen. `.gitignore` schließt `*.pfx`, `*.p12`, `*.jks`, `*.keystore`, `signing.props`,
  `*.apk` und `*.aab` aus – trotzdem vor jedem Commit `git status` prüfen.
- Keine echten Bluetooth-Adressen, Kopplungsschlüssel, IP-Adressen, Passwörter oder Benutzernamen in Code, Doku,
  Commits oder Testdaten. Beispiele mit Platzhaltern; Werkzeuge maskieren Schlüssel.

## Öffentliche Texte

README, Webseite, kodi-repo und Release-Notizen: sachlich und knapp, ohne Emojis, Werbe-Badges und Füllwörter. Nur
Funktionen nennen, die es gibt; „stabil“ oder „geprüft“ nur für tatsächlich Geprüftes. Lange Anleitungen gehören
nach `docs/`. Deutsch und Englisch inhaltsgleich.

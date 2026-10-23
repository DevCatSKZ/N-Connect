# winget-Manifeste

Die vier YAML-Dateien beschreiben SKZ Connect für den Windows Package Manager (Paket `DevCatSKZ.SKZ-Connect`, Setup und
portable ZIP). `winget install DevCatSKZ.SKZ-Connect` funktioniert erst, wenn die Manifeste bei
[microsoft/winget-pkgs](https://github.com/microsoft/winget-pkgs) eingereicht und dort angenommen sind (Stand
17.10.2026: noch nicht eingereicht). Eingereicht wird je Version unter
`manifests/d/DevCatSKZ/SKZ-Connect/<Version>/`, mit dem GitHub-Konto des Nutzers.

## Bei jedem Release aktualisieren

Erst nach dem Release, denn die Prüfsummen gehören zu den fertigen Dateien am GitHub-Release.

- `PackageVersion` in allen vier Dateien, `ReleaseDate` und `ReleaseNotesUrl` auf den neuen Tag,
- `InstallerUrl` auf `SKZ-Connect-Setup-<Version>.exe` und `SKZ-Connect-Portable-<Version>.zip` des Releases,
- `InstallerSha256` aus den heruntergeladenen Release-Dateien neu rechnen:

```powershell
Get-FileHash .\SKZ-Connect-Setup-<Version>.exe, .\SKZ-Connect-Portable-<Version>.zip -Algorithm SHA256
```

Prüfen: `winget validate --manifest <Ordner>` mit einem Ordner, der nur die vier YAML-Dateien enthält – diese
`README.md` im selben Ordner lässt die Prüfung scheitern.

## Einreichen

Mit [wingetcreate](https://github.com/microsoft/winget-create), ebenfalls nur mit den vier YAML-Dateien:

```powershell
wingetcreate submit <Ordner mit den YAML-Dateien>
```

Alternativ von Hand: Fork von `microsoft/winget-pkgs`, die Dateien nach `manifests/d/DevCatSKZ/SKZ-Connect/<Version>/`
legen und einen Pull Request öffnen. Die Validierung dort läuft automatisch, die Moderation dauert meist einige Tage.
Ist das Paket einmal aufgenommen, erzeugt `wingetcreate update DevCatSKZ.SKZ-Connect -v <Version> -u <Setup-URL>
<ZIP-URL>` die Manifeste für spätere Versionen.

## Hinweise

- `License: MIT` passt zur `LICENSE` im Repo.
- Der ZIP-Eintrag installiert die portable EXE. ViGEmBus und HidHide bringt nur das Setup mit; ohne sie erzeugt die
  portable Variante keine virtuellen Controller.
- Setup und EXE sind nicht signiert (siehe [HANDOVER.md](../HANDOVER.md)); Windows zeigt beim Start die
  SmartScreen-Warnung.

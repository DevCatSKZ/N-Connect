# winget-Manifeste

Diese Dateien beschreiben N-Connect für den Windows Package Manager (`winget install DevCatSKZ.N-Connect`).
Sie gehören – einmal je Version – nach <https://github.com/microsoft/winget-pkgs> unter
`manifests/d/DevCatSKZ/N-Connect/<Version>/`.

## Einreichen

Am einfachsten mit `wingetcreate` (https://github.com/microsoft/winget-create):

```
wingetcreate submit .\winget\   # forkt winget-pkgs, legt den Pull Request an
```

oder per Hand: Fork von `microsoft/winget-pkgs`, die vier Dateien nach
`manifests/d/DevCatSKZ/N-Connect/1.0.17/` kopieren, PR öffnen. Die Moderation dort
prüft die Manifeste automatisch (Validation-Pipeline) – dauert meist wenige Tage.

## Vor jedem Release aktualisieren

- `PackageVersion` (alle Dateien) und `ReleaseDate`/`ReleaseNotesUrl` an den Tag anpassen,
- `InstallerUrl` auf die neuen Assets zeigen lassen,
- `InstallerSha256` neu rechnen:

```powershell
Get-FileHash .\N-Connect-Setup-1.0.17.exe; Get-FileHash .\N-Connect-Portable-1.0.17.zip
```

Alternativ die Manifeste frisch erzeugen lassen:

```
wingetcreate update DevCatSKZ.N-Connect -v 1.0.17 -u <Setup-URL> <Zip-URL>
```

## Hinweise

- `License: MIT` – passt zur `LICENSE` im Repo (MIT seit Oktober 2026).
- Der ZIP-Eintrag installiert die portable EXE (`winget install … --installer-type zip` bzw. als
  Alternative zum Setup). ViGEmBus/HidHide kommen aus dem Inno-Setup – das ZIP enthält sie nicht.

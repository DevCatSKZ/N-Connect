# Baut N-Connect für Linux (x64, arm64, arm) und packt die Kodi-Add-ons (service.nconnect, plugin.nconnect.status,
# script.nconnect.bundle, repository.devcatskz). Ergebnis in out/kodi/ (oder -Out) – Dateinamen sagen, wofür:
#   N-Connect-Kodi-LibreELEC-<Version>-alle-Geraete.zip        universell: alle Prozessoren (empfohlen)
#   N-Connect-Kodi-LibreELEC-<Version>-RPi4-RPi5-arm64.zip     Raspberry Pi 4/5 und 64-Bit-Boxen (Amlogic, Rockchip …)
#   N-Connect-Kodi-LibreELEC-<Version>-RPi2-RPi3-arm.zip       Raspberry Pi 2/3 (32 Bit)
#   N-Connect-Kodi-LibreELEC-<Version>-PC-x86_64.zip           PCs/Mini-PCs (LibreELEC Generic)
#   plugin.nconnect.status-<Version>.zip                       Widget-Quelle für Skins (leitet an service.nconnect weiter)
#   script.nconnect.bundle-<Version>.zip                       Offline-Installer mit der aktuellen service.nconnect-
#                                                              und Plugin-ZIP (resources/data)
#   repository.devcatskz-<Version>.zip                         „DevCatSKZ Repository“ (automatische Updates)
# Aufruf: powershell -File linux/build-kodi-addon.ps1 [-Rids linux-x64,linux-arm64] [-Out <Ordner>]
#        powershell -File linux/build-kodi-addon.ps1 -Repo [-RepoDir <Ordner>]
#            zusätzlich in eine Arbeitskopie von DevCatSKZ/kodi-repo legen und den Index neu bauen
#            (Standard: kodi-repo neben dem N-Connect-Ordner); committen/pushen danach von Hand bzw. in der CI
#        powershell -File linux/build-kodi-addon.ps1 -Repo -NoBuild -RepoDir <Ordner>
#            nur ins Repository legen – mit den schon gebauten ZIPs aus -Out (CI: dieselben ZIPs wie am Release)
# Schutz: Eine Add-on-Version, die im Repository schon liegt, wird nie überschrieben (für ein Update die Version in
# linux/kodi/<id>/addon.xml erhöhen). Ist service.nconnect oder das Plugin neu, muss auch das Bundle eine neue
# Version haben – sonst bricht -Repo ab, bevor etwas kopiert wird.
param(
    [string[]]$Rids = @('linux-x64', 'linux-arm64', 'linux-arm'),
    [string]$Out,
    [switch]$Repo,
    [string]$RepoDir,
    [switch]$NoBuild
)
$ErrorActionPreference = 'Stop'
$Rids = @($Rids | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $PSScriptRoot 'NConnect.Linux\NConnect.Linux.csproj'
$kodiSrc = Join-Path $PSScriptRoot 'kodi'
$icon = Join-Path $root 'installer\art\N-Connect.png'
$license = Join-Path $root 'LICENSE'
$out = if ($Out) { $ExecutionContext.SessionState.Path.GetUnresolvedProviderPathFromPSPath($Out) } else { Join-Path $root 'out\kodi' }
$stage = Join-Path $out 'stage'

function Get-AddonXml([string]$id) {
    [xml]$x = Get-Content (Join-Path $kodiSrc "$id\addon.xml") -Encoding UTF8
    return $x
}
$version = (Get-AddonXml 'service.nconnect').addon.version
$pluginVersion = (Get-AddonXml 'plugin.nconnect.status').addon.version
$bundleVersion = (Get-AddonXml 'script.nconnect.bundle').addon.version
$repoVersion = (Get-AddonXml 'repository.devcatskz').addon.version
Write-Host "N-Connect für Kodi $version (Status-Plugin $pluginVersion, Bundle $bundleVersion, Repository $repoVersion)"

$deviceNames = @{ 'linux-arm64' = 'RPi4-RPi5-arm64'; 'linux-arm' = 'RPi2-RPi3-arm'; 'linux-x64' = 'PC-x86_64' }
$universalZip = Join-Path $out "N-Connect-Kodi-LibreELEC-$version-alle-Geraete.zip"
$pluginZip = Join-Path $out "plugin.nconnect.status-$pluginVersion.zip"
$bundleZip = Join-Path $out "script.nconnect.bundle-$bundleVersion.zip"
$repoZip = Join-Path $out "repository.devcatskz-$repoVersion.zip"

# Dateien eines Add-ons aus linux/kodi/<id> (ohne __pycache__/.pyc), dazu das Logo aus dem Installer an der Stelle,
# die <assets><icon> der addon.xml nennt (damit alles gleich aussieht), und die Lizenz.
function Get-AddonFiles([string]$id, [switch]$NoLicense) {
    $src = Join-Path $kodiSrc $id
    $files = @()
    Get-ChildItem $src -Recurse -File | Where-Object {
        $_.FullName -notmatch '\\__pycache__\\' -and $_.Extension -ne '.pyc' -and $_.FullName -notmatch '\\resources\\data\\'
    } | ForEach-Object {
        $rel = $_.FullName.Substring($src.Length + 1).Replace('\', '/')
        $files += [pscustomobject]@{ Source = $_.FullName; Path = "$id/$rel"; Mode = 0 }
    }
    $iconPath = (Get-AddonXml $id).addon.extension | Where-Object { $_.point -eq 'xbmc.addon.metadata' } |
        ForEach-Object { $_.assets.icon }
    if ($iconPath) { $files += [pscustomobject]@{ Source = $icon; Path = "$id/$iconPath"; Mode = 0 } }
    if (-not $NoLicense) { $files += [pscustomobject]@{ Source = $license; Path = "$id/LICENSE.txt"; Mode = 0 } }
    return $files
}

function New-Zip([string]$zipPath, $entries) {
    $zip = [System.IO.Compression.ZipFile]::Open($zipPath, 'Create')
    try {
        foreach ($e in $entries) {
            $entry = [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $e.Source, $e.Path, 'Optimal')
            # rwxr-xr-x (Unix) – Kodi übernimmt das nicht immer, der Dienst setzt es zusätzlich selbst.
            if ($e.Mode) { $entry.ExternalAttributes = ($e.Mode -shl 16) }
        }
    }
    finally {
        $zip.Dispose()
    }
    $mb = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)
    Write-Host "  $(Split-Path -Leaf $zipPath) ($mb MB)"
}

function New-ServiceZip([string]$zipPath, [string[]]$ridsInZip) {
    $entries = @(Get-AddonFiles 'service.nconnect')
    foreach ($rid in $ridsInZip) {
        $entries += [pscustomobject]@{ Source = (Join-Path $stage "bin\$rid\nconnect"); Path = "service.nconnect/bin/$rid/nconnect"; Mode = 0x81ED }
    }
    New-Zip $zipPath $entries
}

if (-not $NoBuild) {
    # Nur eigene Ergebnisse entfernen (auch bei -Out auf einen vorhandenen Ordner)
    New-Item -ItemType Directory -Force $out | Out-Null
    if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
    Get-ChildItem $out -File -Filter '*.zip' | Remove-Item -Force
    New-Item -ItemType Directory -Force $stage | Out-Null

    # 1. Programm je Prozessor veröffentlichen
    foreach ($rid in $Rids) {
        Write-Host "  dotnet publish $rid"
        $dest = Join-Path $stage "bin\$rid"
        & dotnet publish $project -c Release -r $rid -o $dest -p:Version=$version --nologo -v quiet
        if ($LASTEXITCODE -ne 0) { throw "publish $rid fehlgeschlagen" }
        Get-ChildItem $dest -Exclude 'nconnect' | Remove-Item -Recurse -Force
    }

    # 2. service.nconnect: universell und je Gerät
    New-ServiceZip $universalZip $Rids
    foreach ($rid in $Rids) {
        New-ServiceZip (Join-Path $out "N-Connect-Kodi-LibreELEC-$version-$($deviceNames[$rid]).zip") @($rid)
    }

    # 3. Status-Plugin (Widget-Quelle älterer Skin-Einrichtungen)
    New-Zip $pluginZip (Get-AddonFiles 'plugin.nconnect.status')

    # 4. Bundle mit den eben gebauten, aktuellen ZIPs von service.nconnect (universell) und Plugin
    $bundle = @(Get-AddonFiles 'script.nconnect.bundle')
    $bundle += [pscustomobject]@{ Source = $universalZip; Path = "script.nconnect.bundle/resources/data/service.nconnect-$version.zip"; Mode = 0 }
    $bundle += [pscustomobject]@{ Source = $pluginZip; Path = "script.nconnect.bundle/resources/data/plugin.nconnect.status-$pluginVersion.zip"; Mode = 0 }
    New-Zip $bundleZip $bundle

    # 5. Repository-Add-on „DevCatSKZ Repository“ (für alle Kodi-Plugins von DevCatSKZ, gehostet in DevCatSKZ/kodi-repo)
    New-Zip $repoZip (Get-AddonFiles 'repository.devcatskz' -NoLicense)
    Remove-Item $stage -Recurse -Force
}
else {
    foreach ($z in @($universalZip, $pluginZip, $bundleZip, $repoZip)) {
        if (-not (Test-Path $z)) { throw "-NoBuild: $z fehlt – erst ohne -NoBuild bauen (gleiches -Out)" }
    }
}

# 6. Optional: ZIPs in eine Arbeitskopie von DevCatSKZ/kodi-repo legen und den Index neu bauen (danach committen/pushen).
if ($Repo) {
    if (-not $RepoDir) { $RepoDir = Join-Path (Split-Path -Parent $root) 'kodi-repo' }
    if (-not (Test-Path $RepoDir)) { throw "Arbeitskopie von DevCatSKZ/kodi-repo fehlt: $RepoDir (mit -RepoDir angeben)" }
    $RepoDir = (Resolve-Path $RepoDir).Path
    $items = @(
        [pscustomobject]@{ Id = 'service.nconnect'; Version = $version; Zip = $universalZip },
        [pscustomobject]@{ Id = 'plugin.nconnect.status'; Version = $pluginVersion; Zip = $pluginZip },
        [pscustomobject]@{ Id = 'script.nconnect.bundle'; Version = $bundleVersion; Zip = $bundleZip },
        [pscustomobject]@{ Id = 'repository.devcatskz'; Version = $repoVersion; Zip = $repoZip })
    $new = @{}
    foreach ($i in $items) {
        $i | Add-Member Target (Join-Path $RepoDir "$($i.Id)\$($i.Id)-$($i.Version).zip")
        $new[$i.Id] = -not (Test-Path $i.Target)
    }
    # Das Bundle enthält service.nconnect und das Plugin. Sind die neu, das Bundle aber nicht, bliebe im Repository ein
    # Bundle mit veralteten ZIPs liegen (so hing es bis 1.0.1 bei service.nconnect 0.4.2).
    if (($new['service.nconnect'] -or $new['plugin.nconnect.status']) -and -not $new['script.nconnect.bundle']) {
        throw ("script.nconnect.bundle $bundleVersion liegt schon im Repository, enthält aber nicht service.nconnect $version " +
               "bzw. plugin.nconnect.status $pluginVersion – Version in linux/kodi/script.nconnect.bundle/addon.xml erhöhen.")
    }
    foreach ($i in $items) {
        if (-not $new[$i.Id]) {
            Write-Host "  schon veröffentlicht, bleibt unverändert: $($i.Id) $($i.Version) (für ein Update die Version in linux/kodi/$($i.Id)/addon.xml erhöhen)"
            continue
        }
        New-Item -ItemType Directory -Force (Split-Path -Parent $i.Target) | Out-Null
        Copy-Item $i.Zip $i.Target
        Write-Host "  neu im Repository: $($i.Id) $($i.Version)"
    }
    # Zum direkten Herunterladen (Links in der README und in der Windows-/Android-App): Repository-ZIP unter festem
    # Namen, Plugin-ZIPs je Gerät – nur zusammen mit einer neuen Version, damit Download und Repository gleich sind.
    $fixedRepoZip = Join-Path $RepoDir 'repository.devcatskz.zip'
    if ($new['repository.devcatskz'] -or -not (Test-Path $fixedRepoZip)) { Copy-Item $repoZip $fixedRepoZip -Force }
    if ($new['service.nconnect']) {
        $downloads = Join-Path $RepoDir 'downloads'
        if (Test-Path $downloads) { Get-ChildItem $downloads -Filter 'N-Connect-Kodi-LibreELEC-*.zip' | Remove-Item -Force }
        New-Item -ItemType Directory -Force $downloads | Out-Null
        foreach ($z in Get-ChildItem $out -Filter "N-Connect-Kodi-LibreELEC-$version-*.zip") {
            # fester Name ohne Version, damit die Links gleich bleiben
            Copy-Item $z.FullName (Join-Path $downloads ($z.Name -replace "-$([regex]::Escape($version))-", '-')) -Force
        }
    }
    New-Item -ItemType Directory -Force (Join-Path $RepoDir 'tools') | Out-Null
    Copy-Item (Join-Path $kodiSrc 'repo-tools\build_repo.py') (Join-Path $RepoDir 'tools\build_repo.py') -Force
    & python (Join-Path $RepoDir 'tools\build_repo.py') $RepoDir
    if ($LASTEXITCODE -ne 0) { throw 'build_repo.py fehlgeschlagen' }
    Write-Host "  Repository aktualisiert: $RepoDir"
}
Write-Host "Fertig: $out"

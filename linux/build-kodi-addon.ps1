# Baut N-Connect für Linux (x64, arm64, arm) und packt das Kodi-Add-on service.nconnect.
# Ergebnis in out/kodi/ – Dateinamen sagen, für welches System/Gerät:
#   N-Connect-Kodi-LibreELEC-<Version>-alle-Geraete.zip        universell: alle Prozessoren (empfohlen)
#   N-Connect-Kodi-LibreELEC-<Version>-RPi4-RPi5-arm64.zip     Raspberry Pi 4/5 und 64-Bit-Boxen (Amlogic, Rockchip …)
#   N-Connect-Kodi-LibreELEC-<Version>-RPi2-RPi3-arm.zip       Raspberry Pi 2/3 (32 Bit)
#   N-Connect-Kodi-LibreELEC-<Version>-PC-x86_64.zip           PCs/Mini-PCs (LibreELEC Generic)
#   repository.devcatskz-<Version>.zip                         „DevCatSKZ Kodi-Repo“ (automatische Updates)
# Aufruf: powershell -File linux/build-kodi-addon.ps1 [-Rids linux-x64,linux-arm64]
#        powershell -File linux/build-kodi-addon.ps1 -Repo   zusätzlich in die Arbeitskopie von DevCatSKZ/kodi-repo (-RepoDir, Standard: ..\kodi-repo)
param(
    [string[]]$Rids = @('linux-x64', 'linux-arm64', 'linux-arm'),
    [switch]$Repo,
    [string]$RepoDir
)
$ErrorActionPreference = 'Stop'
$Rids = @($Rids | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
Add-Type -AssemblyName System.IO.Compression, System.IO.Compression.FileSystem

$root = Split-Path -Parent $PSScriptRoot
$project = Join-Path $PSScriptRoot 'NConnect.Linux\NConnect.Linux.csproj'
$addonSrc = Join-Path $PSScriptRoot 'kodi\service.nconnect'
$out = Join-Path $root 'out\kodi'
$stage = Join-Path $out 'stage'

[xml]$addonXml = Get-Content (Join-Path $addonSrc 'addon.xml') -Encoding UTF8
$version = $addonXml.addon.version
Write-Host "N-Connect für Kodi $version"

if (Test-Path $out) { Remove-Item $out -Recurse -Force }
New-Item -ItemType Directory -Force $stage | Out-Null

# 1. Programm je Prozessor veröffentlichen
foreach ($rid in $Rids) {
    Write-Host "  dotnet publish $rid"
    $dest = Join-Path $stage "bin\$rid"
    & dotnet publish $project -c Release -r $rid -o $dest -p:Version=$version --nologo -v quiet
    if ($LASTEXITCODE -ne 0) { throw "publish $rid fehlgeschlagen" }
    Get-ChildItem $dest -Exclude 'nconnect' | Remove-Item -Force
}

# 2. Add-on-Dateien (Logo aus dem Installer, damit alles gleich aussieht)
function Get-AddonFiles {
    $files = @()
    Get-ChildItem $addonSrc -Recurse -File | Where-Object { $_.FullName -notmatch '\\__pycache__\\' } | ForEach-Object {
        $rel = $_.FullName.Substring($addonSrc.Length + 1).Replace('\', '/')
        $files += [pscustomobject]@{ Source = $_.FullName; Path = $rel }
    }
    $files += [pscustomobject]@{ Source = (Join-Path $root 'installer\art\N-Connect.png'); Path = 'resources/icon.png' }
    $files += [pscustomobject]@{ Source = (Join-Path $root 'LICENSE'); Path = 'LICENSE.txt' }
    return $files
}

function New-AddonZip([string]$zipPath, [string[]]$ridsInZip) {
    $zip = [System.IO.Compression.ZipFile]::Open($zipPath, 'Create')
    try {
        foreach ($f in Get-AddonFiles) {
            [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $f.Source, "service.nconnect/$($f.Path)", 'Optimal')
        }
        foreach ($rid in $ridsInZip) {
            $entry = [System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
                $zip, (Join-Path $stage "bin\$rid\nconnect"), "service.nconnect/bin/$rid/nconnect", 'Optimal')
            # rwxr-xr-x (Unix) – Kodi übernimmt das nicht immer, der Dienst setzt es zusätzlich selbst.
            $entry.ExternalAttributes = (0x81ED -shl 16)
        }
    }
    finally {
        $zip.Dispose()
    }
    $mb = [math]::Round((Get-Item $zipPath).Length / 1MB, 1)
    Write-Host "  $(Split-Path -Leaf $zipPath) ($mb MB)"
}

$deviceNames = @{ 'linux-arm64' = 'RPi4-RPi5-arm64'; 'linux-arm' = 'RPi2-RPi3-arm'; 'linux-x64' = 'PC-x86_64' }
$universalZip = Join-Path $out "N-Connect-Kodi-LibreELEC-$version-alle-Geraete.zip"
New-AddonZip $universalZip $Rids
foreach ($rid in $Rids) {
    New-AddonZip (Join-Path $out "N-Connect-Kodi-LibreELEC-$version-$($deviceNames[$rid]).zip") @($rid)
}
Remove-Item $stage -Recurse -Force

# 3. Repository-Add-on „DevCatSKZ Repository“ (für alle Kodi-Plugins von DevCatSKZ, gehostet in DevCatSKZ/kodi-repo)
$repoSrc = Join-Path $PSScriptRoot 'kodi\repository.devcatskz'
[xml]$repoXml = Get-Content (Join-Path $repoSrc 'addon.xml') -Encoding UTF8
$repoVersion = $repoXml.addon.version
$icon = Join-Path $root 'installer\art\N-Connect.png'
$repoZip = Join-Path $out "repository.devcatskz-$repoVersion.zip"
$zip = [System.IO.Compression.ZipFile]::Open($repoZip, 'Create')
try {
    [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, (Join-Path $repoSrc 'addon.xml'), 'repository.devcatskz/addon.xml', 'Optimal')
    [void][System.IO.Compression.ZipFileExtensions]::CreateEntryFromFile($zip, $icon, 'repository.devcatskz/icon.png', 'Optimal')
}
finally {
    $zip.Dispose()
}
Write-Host "  $(Split-Path -Leaf $repoZip)"

# 4. Optional: ZIPs in eine Arbeitskopie von DevCatSKZ/kodi-repo legen und den Index neu bauen (danach committen/pushen).
if ($Repo) {
    if (-not $RepoDir) { $RepoDir = Join-Path (Split-Path -Parent (Split-Path -Parent $root)) 'kodi-repo' }
    if (-not (Test-Path $RepoDir)) { throw "Arbeitskopie von DevCatSKZ/kodi-repo fehlt: $RepoDir" }
    foreach ($item in @(@('service.nconnect', $version, $universalZip),
                        @('repository.devcatskz', $repoVersion, $repoZip))) {
        $dir = Join-Path $RepoDir $item[0]
        New-Item -ItemType Directory -Force $dir | Out-Null
        Copy-Item $item[2] (Join-Path $dir "$($item[0])-$($item[1]).zip") -Force
    }
    # Zum direkten Herunterladen (Links in der README): Repository-ZIP unter festem Namen, Plugin-ZIPs je Gerät
    Copy-Item $repoZip (Join-Path $RepoDir 'repository.devcatskz.zip') -Force
    $downloads = Join-Path $RepoDir 'downloads'
    if (Test-Path $downloads) { Get-ChildItem $downloads -Filter 'N-Connect-Kodi-LibreELEC-*.zip' | Remove-Item -Force }
    New-Item -ItemType Directory -Force $downloads | Out-Null
    foreach ($z in Get-ChildItem $out -Filter 'N-Connect-Kodi-LibreELEC-*.zip') {
        # fester Name ohne Version, damit die Links in der README gleich bleiben
        Copy-Item $z.FullName (Join-Path $downloads ($z.Name -replace "-$([regex]::Escape($version))-", '-')) -Force
    }
    New-Item -ItemType Directory -Force (Join-Path $RepoDir 'tools') | Out-Null
    Copy-Item (Join-Path $PSScriptRoot 'kodi\repo-tools\build_repo.py') (Join-Path $RepoDir 'tools\build_repo.py') -Force
    & python (Join-Path $RepoDir 'tools\build_repo.py') $RepoDir
    if ($LASTEXITCODE -ne 0) { throw 'build_repo.py fehlgeschlagen' }
    Write-Host "  Repository aktualisiert: $RepoDir"
}
Write-Host "Fertig: $out"

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $root 'dist'
$extensions = Join-Path $env:APPDATA 'Playnite\Extensions'

if (-not (Test-Path $dist)) { throw "Nothing to install: $dist does not exist. Run tools\build.ps1 first." }
if (-not (Test-Path $extensions)) { throw "Playnite extensions folder not found: $extensions" }

$installed = 0
foreach ($folder in Get-ChildItem $dist -Directory)
{
    if ($folder.Name -in @('pext', 'repo')) { continue }
    if (-not (Test-Path (Join-Path $folder.FullName 'extension.yaml'))) { continue }
    $target = Join-Path $extensions $folder.Name
    if (Test-Path $target) { Remove-Item $target -Recurse -Force }
    Copy-Item $folder.FullName $target -Recurse -Force
    Write-Host ("Installed " + $folder.Name) -ForegroundColor Green
    $installed++
}

Write-Host ""
Write-Host ("Installed " + $installed + " extension(s) into " + $extensions)
Write-Host "Restart Playnite so the new extensions are loaded." -ForegroundColor Yellow

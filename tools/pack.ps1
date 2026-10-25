$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem

$root = Split-Path -Parent $PSScriptRoot
$dist = Join-Path $root 'dist'
$pext = Join-Path $dist 'pext'
if (-not (Test-Path $pext)) { New-Item -ItemType Directory -Path $pext | Out-Null }

$count = 0
foreach ($folder in Get-ChildItem $dist -Directory)
{
    if ($folder.Name -in @('pext', 'repo')) { continue }
    if (-not (Test-Path (Join-Path $folder.FullName 'extension.yaml'))) { continue }
    $target = Join-Path $pext ($folder.Name + '.pext')
    if (Test-Path $target) { Remove-Item $target -Force }
    [System.IO.Compression.ZipFile]::CreateFromDirectory($folder.FullName, $target)
    Write-Host ("Packed " + (Split-Path -Leaf $target)) -ForegroundColor Green
    $count++
}

Write-Host ""
Write-Host ("Packed " + $count + " add-on file(s) into " + $pext)
Write-Host "In Playnite: Main menu > Add-ons > Install add-on from file (or drag the .pext onto the window)." -ForegroundColor Yellow

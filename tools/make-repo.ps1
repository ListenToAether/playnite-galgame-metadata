# Builds dist\repo\playnite-galgame-metadata.zip - the folder tree to upload to GitHub.
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$root = Split-Path -Parent $PSScriptRoot
$workspace = Split-Path -Parent $root

# Documentation sources live in the workspace research folder.
$research = Join-Path $workspace 'research'

$stage = Join-Path $env:TEMP 'galrepo\playnite-galgame-metadata'
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Path $stage -Force | Out-Null

Copy-Item (Join-Path $root 'src') (Join-Path $stage 'src') -Recurse -Force
Copy-Item (Join-Path $root 'README.md') $stage -Force
Copy-Item (Join-Path $root 'LICENSE') $stage -Force
Copy-Item (Join-Path $root '.gitignore') $stage -Force

$tools = Join-Path $stage 'tools'
New-Item -ItemType Directory -Path $tools -Force | Out-Null
foreach ($name in @('build.ps1', 'install.ps1', 'pack.ps1', 'build-harness.ps1', 'build-probe.ps1', 'build-ui.ps1', 'test-settings-view.ps1', 'Harness.cs', 'Probe.cs', 'UiSmoke.cs', 'make-repo.ps1')) {
    $source = Join-Path $root ('tools\' + $name)
    if (Test-Path $source) { Copy-Item $source $tools -Force }
}

$docs = Join-Path $stage 'docs'
New-Item -ItemType Directory -Path $docs -Force | Out-Null
# The repo docs folder is authoritative; it already holds the API notes and screenshots.
$localDocs = Join-Path $root 'docs'
if (Test-Path $localDocs) {
    Get-ChildItem $localDocs -File | ForEach-Object { Copy-Item $_.FullName $docs -Force }
}
foreach ($name in @('shionlib.md', 'touchgal.md')) {
    $source = Join-Path $research $name
    if ((Test-Path $source) -and -not (Test-Path (Join-Path $docs $name))) { Copy-Item $source $docs -Force }
}
foreach ($shotName in @('picker-dark.png', 'picker-dark-hikarinagi.png', 'picker-dark-kungal.png')) {
    $fromTools = Join-Path $root ('tools\ui\' + $shotName)
    if ((Test-Path $fromTools) -and -not (Test-Path (Join-Path $docs $shotName))) { Copy-Item $fromTools (Join-Path $docs $shotName) -Force }
}

$releases = Join-Path $stage 'releases'
New-Item -ItemType Directory -Path $releases -Force | Out-Null
Get-ChildItem (Join-Path $root 'dist\pext') -Filter *.pext -ErrorAction SilentlyContinue |
    ForEach-Object { Copy-Item $_.FullName $releases -Force }

$target = Join-Path $root 'dist\repo\playnite-galgame-metadata.zip'
New-Item -ItemType Directory -Path (Split-Path -Parent $target) -Force | Out-Null
if (Test-Path $target) { Remove-Item $target -Force }
[System.IO.Compression.ZipFile]::CreateFromDirectory((Split-Path -Parent $stage), $target, [System.IO.Compression.CompressionLevel]::Optimal, $false)

$zip = [System.IO.Compression.ZipFile]::OpenRead($target)
try {
    'zipped {0} entries -> {1} ({2:N0} bytes)' -f $zip.Entries.Count, $target, (Get-Item $target).Length
    $zip.Entries | Where-Object { $_.FullName -notmatch '/$' } | Select-Object -ExpandProperty FullName | Sort-Object
}
finally {
    $zip.Dispose()
}

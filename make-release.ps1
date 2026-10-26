# Creates the v0.8 GitHub Release for the playnite-galgame-metadata repo and uploads the .pext assets.
# Usage:
#   powershell -ExecutionPolicy Bypass -File .\tools\make-release.ps1 -Token ghp_xxx
# Optional:
#   -Tag v0.8   -NotesFile docs\release-notes-v0.8.md
param(
    [string]$Token = $env:GH_TOKEN,
    [string]$Owner = 'zxc34731116797',
    [string]$Repo = 'playnite-galgame-metadata',
    [string]$Tag = 'v0.8',
    [string]$Title = 'v0.8 — 完善手动选择、描述渲染与窗口样式',
    [string]$NotesFile,
    [string]$SpecFile
)

$ErrorActionPreference = 'Stop'
if ([string]::IsNullOrWhiteSpace($Token)) { throw 'No token: pass -Token or set $env:GH_TOKEN.' }
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$root = Split-Path -Parent $PSScriptRoot
if (-not $NotesFile) { $NotesFile = Join-Path $root 'docs\release-notes-v0.8.md' }
if (-not $SpecFile -or -not (Test-Path $SpecFile)) {
    # The spec is only needed by the harness scripts; release creation does not use it.
    $SpecFile = $null
}

$api = "https://api.github.com/repos/$Owner/$Repo"
$headers = @{
    'Authorization'        = "Bearer $Token"
    'Accept'               = 'application/vnd.github+json'
    'X-GitHub-Api-Version' = '2022-11-28'
    'User-Agent'           = 'playnite-galgame-metadata-release-script'
}

function New-Release {
    $body = @{
        tag_name         = $Tag
        target_commitish = 'main'
        name             = $Title
        body             = ''
        draft            = $false
        prerelease       = $false
    }
    if (Test-Path $NotesFile) {
        $body.body = [System.IO.File]::ReadAllText($NotesFile)
    }
    $json = $body | ConvertTo-Json -Depth 5
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($json)
    return Invoke-RestMethod -Method Post -Uri "$api/releases" -Headers $headers -Body $bytes -ContentType 'application/json; charset=utf-8'
}

function Upload-Asset([long]$ReleaseId, [string]$Path) {
    $name = Split-Path $Path -Leaf
    $uri = "https://uploads.github.com/repos/$Owner/$Repo/releases/$ReleaseId/assets?name=$([uri]::EscapeDataString($name))"
    $data = [System.IO.File]::ReadAllBytes($Path)
    return Invoke-RestMethod -Method Post -Uri $uri -Headers $headers -Body $data -ContentType 'application/octet-stream' -TimeoutSec 300
}

# Reuse an existing release for the tag, otherwise create it.
$release = $null
try {
    $release = Invoke-RestMethod -Method Get -Uri "$api/releases/tags/$Tag" -Headers $headers
    Write-Host "Release $Tag already exists (id $($release.id))." -ForegroundColor Yellow
}
catch {
    Write-Host "Creating release $Tag ..." -ForegroundColor Cyan
    $release = New-Release
    Write-Host "Created release $Tag (id $($release.id))." -ForegroundColor Green
}

$assets = @()
if ($release.assets) { $assets = $release.assets | Select-Object -ExpandProperty name }

$pext = Get-ChildItem (Join-Path $root 'dist\pext') -Filter *.pext | Sort-Object Name
if (-not $pext) { throw "No .pext files found in dist\pext - run tools\pack.ps1 first." }

foreach ($file in $pext) {
    if ($assets -contains $file.Name) {
        Write-Host "skip (already attached): $($file.Name)" -ForegroundColor DarkGray
        continue
    }
    Write-Host "uploading $($file.Name) ($([math]::Round($file.Length / 1KB)) KB) ..." -ForegroundColor Cyan
    $asset = Upload-Asset $release.id $file.FullName
    Write-Host "  -> $($asset.browser_download_url)" -ForegroundColor Green
}

Write-Host ''
Write-Host "Release page: $($release.html_url)" -ForegroundColor Green

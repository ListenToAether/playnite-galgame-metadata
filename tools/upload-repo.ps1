param(
    [string]$Token = $env:GH_TOKEN,
    [string]$Owner = 'ListenToAether',
    [string]$Repo = 'playnite-galgame-metadata',
    [string]$Branch = 'main',
    [string]$SourceDir,
    [string]$Message = 'chore: sync latest plugin sources',
    [switch]$DryRun
)

$ErrorActionPreference = 'Stop'
if (-not $Token) { throw 'No token. Pass -Token or set $env:GH_TOKEN.' }
if (-not $SourceDir -or -not (Test-Path $SourceDir)) { throw 'SourceDir is required and must exist.' }
$SourceDir = (Resolve-Path $SourceDir).Path.TrimEnd('\')

$headers = @{ Authorization = "Bearer $Token"; 'User-Agent' = 'galgame-metadata-uploader'; Accept = 'application/vnd.github+json' }
$api = "https://api.github.com/repos/$Owner/$Repo"

function Get-GitBlobSha([string]$path) {
    $content = [IO.File]::ReadAllBytes($path)
    $header = [Text.Encoding]::ASCII.GetBytes("blob $($content.Length)`0")
    $buffer = New-Object byte[] ($header.Length + $content.Length)
    [Array]::Copy($header, 0, $buffer, 0, $header.Length)
    [Array]::Copy($content, 0, $buffer, $header.Length, $content.Length)
    $sha1 = [Security.Cryptography.SHA1]::Create()
    try { $hash = $sha1.ComputeHash($buffer) } finally { $sha1.Dispose() }
    return ([BitConverter]::ToString($hash) -replace '-', '').ToLowerInvariant()
}

Write-Host "Reading remote tree of $Owner/$Repo@$Branch ..."
$ref = Invoke-RestMethod -Uri "$api/git/ref/heads/$Branch" -Headers $headers -TimeoutSec 60
$baseCommitSha = $ref.object.sha
$baseCommit = Invoke-RestMethod -Uri "$api/git/commits/$baseCommitSha" -Headers $headers -TimeoutSec 60
$remoteTree = Invoke-RestMethod -Uri "$api/git/trees/$Branch`?recursive=1" -Headers $headers -TimeoutSec 60
$remoteBlobs = @{}
foreach ($entry in ($remoteTree.tree | Where-Object { $_.type -eq 'blob' })) { $remoteBlobs[$entry.path] = $entry.sha }

$local = @{}
foreach ($file in (Get-ChildItem $SourceDir -Recurse -File)) {
    $rel = $file.FullName.Substring($SourceDir.Length + 1).Replace('\', '/')
    $local[$rel] = Get-GitBlobSha $file.FullName
}

$changed = @($local.Keys | Where-Object { -not $remoteBlobs.ContainsKey($_) -or $remoteBlobs[$_] -ne $local[$_] } | Sort-Object)
$removed = @($remoteBlobs.Keys | Where-Object { -not $local.ContainsKey($_) } | Sort-Object)

Write-Host ("local files: {0}, changed/new: {1}, to delete: {2}" -f $local.Count, $changed.Count, $removed.Count)
foreach ($path in $changed) { Write-Host "  update $path" }
foreach ($path in $removed) { Write-Host "  delete $path" }
if ($DryRun) { Write-Host 'Dry run, nothing uploaded.'; return }
if ($changed.Count -eq 0 -and $removed.Count -eq 0) { Write-Host 'Repository already up to date.'; return }

$entries = New-Object System.Collections.ArrayList
foreach ($path in $changed) {
    $full = Join-Path $SourceDir ($path -replace '/', '\')
    $b64 = [Convert]::ToBase64String([IO.File]::ReadAllBytes($full))
    $body = @{ content = $b64; encoding = 'base64' } | ConvertTo-Json -Compress
    $blob = Invoke-RestMethod -Uri "$api/git/blobs" -Method Post -Headers $headers -Body $body -ContentType 'application/json' -TimeoutSec 120
    [void]$entries.Add(@{ path = $path; mode = '100644'; type = 'blob'; sha = $blob.sha })
    Write-Host "  uploaded $path"
}
foreach ($path in $removed) {
    [void]$entries.Add(@{ path = $path; mode = '100644'; type = 'blob'; sha = $null })
}

$treeBody = @{ base_tree = $baseCommit.tree.sha; tree = $entries.ToArray() } | ConvertTo-Json -Depth 6 -Compress
$newTree = Invoke-RestMethod -Uri "$api/git/trees" -Method Post -Headers $headers -Body $treeBody -ContentType 'application/json' -TimeoutSec 120
$commitBody = @{ message = $Message; tree = $newTree.sha; parents = @($baseCommitSha) } | ConvertTo-Json -Depth 4 -Compress
$newCommit = Invoke-RestMethod -Uri "$api/git/commits" -Method Post -Headers $headers -Body $commitBody -ContentType 'application/json' -TimeoutSec 120
$refBody = @{ sha = $newCommit.sha } | ConvertTo-Json -Compress
$updated = Invoke-RestMethod -Uri "$api/git/refs/heads/$Branch" -Method Patch -Headers $headers -Body $refBody -ContentType 'application/json' -TimeoutSec 60
Write-Host ("Committed {0}: {1}" -f $updated.object.sha.Substring(0, 8), $Message) -ForegroundColor Green

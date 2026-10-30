param(
    [string]$Token = $env:GH_TOKEN,
    [string]$Owner = 'ListenToAether',
    [string]$Repo  = 'playnite-galgame-metadata',
    [string]$Branch = 'main',
    [switch]$DryRun
)

# 清理仓库根目录里被误传上来的散落文件（只删根目录中不在白名单里的文件），
# 外加 docs 目录下三个已废弃的旧文件。不会动 src/、tools/ 等正常目录。
# 所有删除合并成一次提交，仓库历史里只多出一条记录。
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$headers = @{
    'User-Agent' = 'galgame-metadata-cleanup'
    'Accept'     = 'application/vnd.github+json'
}
if ($Token) { $headers['Authorization'] = "Bearer $Token" }

$whitelist = @('.gitignore', 'LICENSE', 'README.md', 'README.en.md')
$obsolete  = @(
    'docs/picker-dark.png',
    'docs/settings-en.png',
    'docs/release-notes-v1.0.0.md'
)
$api = "https://api.github.com/repos/$Owner/$Repo"

Write-Host '正在读取仓库文件列表...' -ForegroundColor Cyan
$tree = Invoke-RestMethod "$api/git/trees/$Branch`?recursive=1" -Headers $headers
$root = @($tree.tree | Where-Object { $_.type -eq 'blob' -and $_.path -notmatch '/' })
$stray = @($root | Where-Object { $whitelist -notcontains $_.path } | Select-Object -ExpandProperty path)
$targets = @($stray) + $obsolete

Write-Host ("仓库根目录共 {0} 个文件，其中 {1} 个是多余散落的。" -f $root.Count, $stray.Count) -ForegroundColor Yellow
Write-Host ("需要删除的文件共 {0} 个：" -f $targets.Count)
foreach ($path in $targets) { Write-Host "  $path" }
Write-Host ''

if ($targets.Count -eq 0) { Write-Host '没有需要删除的文件，仓库已经是干净的。' -ForegroundColor Green; return }
if ($DryRun) { Write-Host '这只是预演（-DryRun），没有删除任何文件。' -ForegroundColor Cyan; return }

if (-not $Token) {
    $secure = Read-Host -Prompt '请粘贴 GitHub 令牌，然后按回车（输入的内容不会显示）' -AsSecureString
    $Token = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($secure))
}
if (-not $Token) { throw '没有拿到令牌，已退出。' }
$headers['Authorization'] = "Bearer $Token"

Write-Host '正在提交删除...' -ForegroundColor Cyan
$ref = Invoke-RestMethod "$api/git/ref/heads/$Branch" -Headers $headers
$headSha = $ref.object.sha
$headCommit = Invoke-RestMethod "$api/git/commits/$headSha" -Headers $headers
$baseTree = $headCommit.tree.sha

$entries = @()
foreach ($path in $targets) {
    $entries += @{ path = $path; mode = '100644'; type = 'blob'; sha = $null }
}
$treeBody = @{ base_tree = $baseTree; tree = $entries } | ConvertTo-Json -Depth 6
$newTree = Invoke-RestMethod "$api/git/trees" -Method Post -Headers $headers -Body $treeBody -ContentType 'application/json'

$commitBody = @{
    message = 'chore: remove stray files from repository root'
    tree    = $newTree.sha
    parents = @($headSha)
} | ConvertTo-Json -Depth 6
$newCommit = Invoke-RestMethod "$api/git/commits" -Method Post -Headers $headers -Body $commitBody -ContentType 'application/json'

$refBody = @{ sha = $newCommit.sha; force = $false } | ConvertTo-Json
Invoke-RestMethod "$api/git/refs/heads/$Branch" -Method Patch -Headers $headers -Body $refBody -ContentType 'application/json' | Out-Null

Write-Host ''
Write-Host ("清理完成：一次提交删除了 {0} 个文件。" -f $targets.Count) -ForegroundColor Green
Write-Host ("提交号：{0}" -f $newCommit.sha)
Write-Host '刷新仓库首页即可看到根目录只剩 4 个文件 + 各个文件夹。'

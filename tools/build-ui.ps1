$ErrorActionPreference = 'Stop'
# Builds a small STA smoke test for the code-only WPF windows (settings view + manual picker).
$base     = Split-Path -Parent $PSScriptRoot
$toolRoot = Join-Path $env:TEMP 'galtool\x'
$csc      = Join-Path $toolRoot 'roslyn2\tasks\net472\csc.exe'
$refs     = Join-Path $toolRoot 'refs\build\.NETFramework\v4.6.2'
$outDir   = Join-Path $base 'tools\ui'
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }

$frameworkRefs = Get-ChildItem $refs -Filter *.dll |
    Where-Object { $_.Name -notin @('System.EnterpriseServices.Wrapper.dll', 'System.EnterpriseServices.Thunk.dll') } |
    Select-Object -ExpandProperty FullName

$sources = @()
$sources += Join-Path $base 'src\Shared\MiniJson.cs'
$sources += Join-Path $base 'src\Shared\Common.cs'
$sources += Join-Path $base 'src\Shared\Sources.cs'
$sources += Join-Path $base 'src\Shared\PlayniteSupport.cs'
$sources += Join-Path $base 'src\Shared\Picker.cs'
$sources += Join-Path $base 'src\Hikarinagi\HikarinagiSource.cs'
$sources += Join-Path $base 'src\Kungal\KungalSource.cs'
$sources += Join-Path $base 'tools\UiSmoke.cs'

$out = Join-Path $outDir 'UiSmoke.exe'
$arguments = @('/nologo', '/target:exe', '/platform:anycpu', '/nostdlib+', '/langversion:7.3', '/optimize+', "/out:$out")
foreach ($reference in $frameworkRefs) { $arguments += "/r:$reference" }
$arguments += '/r:D:\playnite\Playnite.SDK.dll'
$arguments += $sources

$output = & $csc @arguments 2>&1
if ($LASTEXITCODE -ne 0) { Write-Host ($output | Out-String) -ForegroundColor Red; throw 'UI smoke build failed' }
Copy-Item 'D:\playnite\Playnite.SDK.dll' $outDir -Force
Write-Host "UI smoke built: $out" -ForegroundColor Green

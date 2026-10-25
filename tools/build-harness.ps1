$ErrorActionPreference = 'Stop'
$base     = Split-Path -Parent $PSScriptRoot
$toolRoot = Join-Path $env:TEMP 'galtool\x'
$csc      = Join-Path $toolRoot 'roslyn2\tasks\net472\csc.exe'
$refs     = Join-Path $toolRoot 'refs\build\.NETFramework\v4.6.2'
$outDir   = Join-Path $base 'tools\harness'
if (-not (Test-Path $outDir)) { New-Item -ItemType Directory -Path $outDir | Out-Null }

$frameworkRefs = Get-ChildItem $refs -Filter *.dll |
    Where-Object { $_.Name -notin @('System.EnterpriseServices.Wrapper.dll', 'System.EnterpriseServices.Thunk.dll') } |
    Select-Object -ExpandProperty FullName

$sources = @()
$sources += Join-Path $base 'src\Shared\MiniJson.cs'
$sources += Join-Path $base 'src\Shared\Common.cs'
$sources += Join-Path $base 'src\Shared\Sources.cs'
foreach ($site in @('Ymgal','Kungal','Hikarinagi','Shionlib','Touchgal'))
{
    $sources += Get-ChildItem (Join-Path $base "src\$site") -Filter '*Source.cs' | Select-Object -ExpandProperty FullName
}
$sources += Join-Path $base 'tools\Harness.cs'

$out = Join-Path $outDir 'Harness.exe'
$arguments = @('/nologo', '/target:exe', '/platform:anycpu', '/nostdlib+', '/langversion:7.3', '/optimize+', "/out:$out")
foreach ($reference in $frameworkRefs) { $arguments += "/r:$reference" }
$arguments += $sources

& $csc @arguments
if ($LASTEXITCODE -ne 0) { throw 'Harness build failed' }
Copy-Item (Join-Path $env:TEMP 'galtool\x\roslyn2\tasks\net472\csc.exe.config') $outDir -ErrorAction SilentlyContinue
Write-Host "Harness built: $out" -ForegroundColor Green

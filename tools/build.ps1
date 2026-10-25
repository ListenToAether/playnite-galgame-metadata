#requires -Version 5.1
# Builds the five Playnite metadata extensions into .\dist.
# Compiler: Roslyn csc from NuGet (no .NET SDK on this machine), targeting .NET Framework 4.6.2
# with the reference assemblies package, plus Playnite.SDK.dll taken from the local Playnite install.

$ErrorActionPreference = 'Stop'

$root      = Split-Path -Parent $PSScriptRoot
$src       = Join-Path $root 'src'
$dist      = Join-Path $root 'dist'
$toolRoot  = Join-Path $env:TEMP 'galtool\x'
$csc       = Join-Path $toolRoot 'roslyn2\tasks\net472\csc.exe'
$refs      = Join-Path $toolRoot 'refs\build\.NETFramework\v4.6.2'
$playnite  = 'D:\playnite\Playnite.SDK.dll'

foreach ($p in @($csc, $refs, $playnite))
{
    if (-not (Test-Path $p)) { throw "Missing build dependency: $p" }
}

$sites = @(
    @{ Folder = 'Ymgal';      Assembly = 'YmgalMetadata';      Id = '2460c7c5-5f79-4ebe-826d-cba2084e2464'; Name = 'Ymgal Metadata Provider';      Color = '#E4572E'; Letters = 'YM' },
    @{ Folder = 'Kungal';     Assembly = 'KungalMetadata';     Id = '664139ab-88b1-4be5-869e-592b81e21df6'; Name = 'Kungal Metadata Provider';     Color = '#2E86AB'; Letters = 'KG' },
    @{ Folder = 'Hikarinagi'; Assembly = 'HikarinagiMetadata'; Id = '57db3d17-7ad1-4f98-83b3-bcffb08ef8d2'; Name = 'Hikarinagi Metadata Provider'; Color = '#F4A261'; Letters = 'HK' },
    @{ Folder = 'Shionlib';   Assembly = 'ShionlibMetadata';   Id = 'e882fba8-217a-4fcd-a400-b53b3392a734'; Name = 'Shionlib Metadata Provider';   Color = '#6A4C93'; Letters = 'SL' },
    @{ Folder = 'Touchgal';   Assembly = 'TouchgalMetadata';   Id = 'af145bc3-1883-4933-a1d7-d5124de2cccd'; Name = 'TouchGAL Metadata Provider';   Color = '#2A9D8F'; Letters = 'TG' }
)

$sharedSources = Get-ChildItem (Join-Path $src 'Shared') -Filter *.cs | Select-Object -ExpandProperty FullName
$frameworkRefs = Get-ChildItem $refs -Filter *.dll |
    Where-Object { $_.Name -notin @('System.EnterpriseServices.Wrapper.dll', 'System.EnterpriseServices.Thunk.dll') } |
    Select-Object -ExpandProperty FullName

function New-Icon([string]$path, [string]$color, [string]$letters)
{
    if (Test-Path $path) { return }
    Add-Type -AssemblyName System.Drawing
    $bmp = New-Object System.Drawing.Bitmap 128, 128
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::FromArgb(255, 30, 30, 36))
    $brush = New-Object System.Drawing.SolidBrush ([System.Drawing.ColorTranslator]::FromHtml($color))
    $g.FillEllipse($brush, 6, 6, 116, 116)
    $font = New-Object System.Drawing.Font('Segoe UI', 42, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    $fmt = New-Object System.Drawing.StringFormat
    $fmt.Alignment = [System.Drawing.StringAlignment]::Center
    $fmt.LineAlignment = [System.Drawing.StringAlignment]::Center
    $rect = New-Object System.Drawing.RectangleF 0, 0, 128, 128
    $g.DrawString($letters, $font, [System.Drawing.Brushes]::White, $rect, $fmt)
    $g.Dispose()
    $bmp.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

if (-not (Test-Path $dist)) { New-Item -ItemType Directory -Path $dist | Out-Null }

$failed = @()

foreach ($site in $sites)
{
    $folderName = "$($site.Assembly)_$($site.Id)"
    $outDir     = Join-Path $dist $folderName
    if (Test-Path $outDir) { Remove-Item $outDir -Recurse -Force }
    New-Item -ItemType Directory -Path $outDir | Out-Null

    $dll = Join-Path $outDir "$($site.Assembly).dll"
    $siteSources = Get-ChildItem (Join-Path $src $site.Folder) -Filter *.cs | Select-Object -ExpandProperty FullName

    $arguments = @('/nologo', '/target:library', '/platform:anycpu', '/nostdlib+', '/langversion:7.3',
                   '/optimize+', '/debug:portable', '/deterministic',
                   "/out:$dll")
    foreach ($reference in $frameworkRefs) { $arguments += "/r:$reference" }
    $arguments += "/r:$playnite"
    $arguments += $sharedSources
    $arguments += $siteSources

    Write-Host "Building $($site.Assembly).dll ..." -ForegroundColor Cyan
    $output = & $csc @arguments 2>&1
    if ($LASTEXITCODE -ne 0)
    {
        $failed += $site.Assembly
        Write-Host ($output | Out-String) -ForegroundColor Red
        continue
    }

    if ($output) { Write-Host ($output | Out-String) -ForegroundColor Yellow }

    $yaml = @"
Id: $folderName
Name: $($site.Name)
Author: galgame-metadata
Version: 0.4
Module: $($site.Assembly).dll
Type: MetadataProvider
Icon: icon.png
Links:
  - Name: Data source
    Url: $((@{ Ymgal = 'https://www.ymgal.games/'; Kungal = 'https://www.kungal.com/'; Hikarinagi = 'https://www.hikarinagi.org/'; Shionlib = 'https://shionlib.com/zh'; Touchgal = 'https://www.touchgal.com/' })[$site.Folder])
"@
    [System.IO.File]::WriteAllText((Join-Path $outDir 'extension.yaml'), $yaml, (New-Object System.Text.UTF8Encoding($false)))
    New-Icon (Join-Path $outDir 'icon.png') $site.Color $site.Letters
}

if ($failed.Count -gt 0) { throw "Build failed: $($failed -join ', ')" }
Write-Host 'All five extensions built into dist.' -ForegroundColor Green
Get-ChildItem $dist | ForEach-Object { "  $($_.Name)" }

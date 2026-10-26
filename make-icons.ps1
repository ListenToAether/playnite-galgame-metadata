#requires -Version 5.1
<#
    Downloads each data source's own site icon and turns it into a 128x128 PNG
    used as the Playnite extension icon (assets\icons\<Site>.png).

    Downloaded originals are cached under assets\icons\source so the build stays
    reproducible (and offline works) once the icons were fetched one time.
#>
$ErrorActionPreference = 'Stop'

$root     = Split-Path -Parent $PSScriptRoot
$outDir   = Join-Path $root 'assets\icons'
$cacheDir = Join-Path $outDir 'source'
if (-not (Test-Path $cacheDir)) { New-Item -ItemType Directory -Path $cacheDir -Force | Out-Null }

# Folder = src\<Folder>\ folder name, used for assets\icons\<Folder>.png
$icons = @(
    # Ymgal's favicon is only 50x50, so the sharper ring from the site logo is
    # cropped out of https://www.ymgal.games/static/ym-main-logo.png instead.
    @{ Folder = 'Ymgal';      File = 'ymgal-logo.png'; Url = 'https://www.ymgal.games/static/ym-main-logo.png';
       Crop = @(24, 24, 132, 132); Circle = $true },
    @{ Folder = 'Kungal';     File = 'kungal.png';     Url = 'https://www.kungal.com/android-chrome-192x192.png' },
    @{ Folder = 'Hikarinagi'; File = 'hikarinagi.ico'; Url = 'https://www.hikarinagi.org/favicon.ico' },
    @{ Folder = 'Shionlib';   File = 'shionlib.ico';   Url = 'https://shionlib.com/favicon.ico' },
    # touchgal.com is behind a Cloudflare challenge for direct requests, so its
    # icon is taken from the site's own favicon as indexed by a public proxy.
    @{ Folder = 'Touchgal';   File = 'touchgal.png';   Url = 'https://t2.gstatic.com/faviconV2?client=SOCIAL&type=FAVICON&fallback_opts=TYPE,SIZE,URL&url=https://touchgal.com&size=128' }
)

Add-Type -AssemblyName System.Drawing

function Convert-ToIconPng([string]$source, [string]$target, [int]$size = 128, [int[]]$crop = $null, [bool]$circle = $false)
{
    $bytes = [System.IO.File]::ReadAllBytes($source)
    if ($bytes.Length -lt 8) { throw "Icon source is empty: $source" }

    $isPng = ($bytes[0] -eq 0x89 -and $bytes[1] -eq 0x50)
    if ($isPng)
    {
        $image = [System.Drawing.Image]::FromStream((New-Object System.IO.MemoryStream(, $bytes)))
    }
    else
    {
        $stream = New-Object System.IO.MemoryStream(, $bytes)
        $icon   = New-Object System.Drawing.Icon($stream, (New-Object System.Drawing.Size(256, 256)))
        $image  = $icon.ToBitmap()
        $icon.Dispose()
    }

    if ($crop -and $crop.Count -eq 4)
    {
        $rect = New-Object System.Drawing.Rectangle $crop[0], $crop[1], $crop[2], $crop[3]
        $full = New-Object System.Drawing.Bitmap $image
        $image.Dispose()
        $image = $full.Clone($rect, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $full.Dispose()
    }

    try
    {
        $bitmap = New-Object System.Drawing.Bitmap $size, $size
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        $graphics.Clear([System.Drawing.Color]::Transparent)
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality

        if ($circle)
        {
            $path = New-Object System.Drawing.Drawing2D.GraphicsPath
            $path.AddEllipse(0, 0, $size, $size)
            $graphics.SetClip($path)
            $path.Dispose()
        }

        $scale = [Math]::Min($size / $image.Width, $size / $image.Height)
        $width = [int][Math]::Round($image.Width * $scale)
        $height = [int][Math]::Round($image.Height * $scale)
        $x = [int](($size - $width) / 2)
        $y = [int](($size - $height) / 2)
        $graphics.DrawImage($image, $x, $y, $width, $height)
        $graphics.Dispose()
        $bitmap.Save($target, [System.Drawing.Imaging.ImageFormat]::Png)
        $bitmap.Dispose()
    }
    finally
    {
        $image.Dispose()
    }
}

foreach ($entry in $icons)
{
    $cached = Join-Path $cacheDir $entry.File
    if (-not (Test-Path $cached))
    {
        Write-Host "Downloading $($entry.Url)" -ForegroundColor Cyan
        $client = New-Object System.Net.WebClient
        $client.Headers.Add('User-Agent', 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/131.0.0.0 Safari/537.36')
        [Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
        $client.DownloadFile($entry.Url, $cached)
        $client.Dispose()
    }

    $target = Join-Path $outDir "$($entry.Folder).png"
    Convert-ToIconPng $cached $target 128 $entry.Crop ([bool]$entry.Circle)
    $file = Get-Item $target
    Write-Host ("  {0,-28} {1,7} B" -f $file.Name, $file.Length) -ForegroundColor Green
}

Write-Host "Icons written to $outDir" -ForegroundColor Green

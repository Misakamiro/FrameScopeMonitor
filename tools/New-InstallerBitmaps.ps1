# Generates the FrameScope-branded NSIS bitmaps (24-bit BMP as required by
# MUI2): a 164x314 welcome/finish sidebar and a 150x57 header, both on the
# app's dark navy background with the FrameScope mark and wordmark.
param(
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot)
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$iconPath = Join-Path $RepoRoot 'assets\icon\framescope-icon.png'
$outDir = Join-Path $RepoRoot 'desktop-preview\src-tauri\installer'
New-Item -ItemType Directory -Path $outDir -Force | Out-Null
$icon = [System.Drawing.Image]::FromFile($iconPath)

function New-Canvas([int]$Width, [int]$Height) {
    $bmp = New-Object System.Drawing.Bitmap($Width, $Height, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAlias
    return @($bmp, $g)
}

function Draw-Background([System.Drawing.Graphics]$g, [int]$Width, [int]$Height) {
    $rect = New-Object System.Drawing.Rectangle(0, 0, $Width, $Height)
    $top = [System.Drawing.Color]::FromArgb(255, 4, 7, 12)
    $bottom = [System.Drawing.Color]::FromArgb(255, 13, 26, 38)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, $top, $bottom, 90.0)
    $g.FillRectangle($brush, $rect)
    $brush.Dispose()
}

# --- sidebar (welcome / finish page): 164x314 ---
$bmp, $g = New-Canvas 164 314
Draw-Background $g 164 314
$iconSize = 64
$g.DrawImage($icon, ([int]((164 - $iconSize) / 2)), 84, $iconSize, $iconSize)
$font = New-Object System.Drawing.Font('Segoe UI', 13.5, [System.Drawing.FontStyle]::Bold)
$textBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 232, 237, 244))
$fmt = New-Object System.Drawing.StringFormat
$fmt.Alignment = [System.Drawing.StringAlignment]::Center
$g.DrawString('FrameScope', $font, $textBrush, ([float](164 / 2)), [float]164, $fmt)
$accent = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 45, 212, 191))
$g.FillRectangle($accent, 52, 196, 60, 2)
$subFont = New-Object System.Drawing.Font('Segoe UI', 7.5)
$subBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 140, 156, 172))
$g.DrawString('Performance Monitor', $subFont, $subBrush, ([float](164 / 2)), [float]208, $fmt)
$bmp.Save((Join-Path $outDir 'sidebar.bmp'), [System.Drawing.Imaging.ImageFormat]::Bmp)
$g.Dispose(); $bmp.Dispose(); $font.Dispose(); $textBrush.Dispose(); $subBrush.Dispose(); $subFont.Dispose()

# --- header: 150x57 ---
$bmp, $g = New-Canvas 150 57
Draw-Background $g 150 57
$g.DrawImage($icon, 10, 12, 33, 33)
$font2 = New-Object System.Drawing.Font('Segoe UI', 11, [System.Drawing.FontStyle]::Bold)
$textBrush2 = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 232, 237, 244))
$fmt2 = New-Object System.Drawing.StringFormat
$fmt2.Alignment = [System.Drawing.StringAlignment]::Near
$g.DrawString('FrameScope', $font2, $textBrush2, [float]50, [float]19, $fmt2)
$bmp.Save((Join-Path $outDir 'header.bmp'), [System.Drawing.Imaging.ImageFormat]::Bmp)
$g.Dispose(); $bmp.Dispose(); $font2.Dispose(); $textBrush2.Dispose()

$icon.Dispose()
Write-Host "Installer bitmaps written to $outDir"

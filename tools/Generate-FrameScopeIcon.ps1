$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent (Split-Path -Parent $MyInvocation.MyCommand.Path)
$iconDir = Join-Path $root 'assets\icon'
$pngPath = Join-Path $iconDir 'framescope-icon.png'
$icoPath = Join-Path $iconDir 'framescope-icon.ico'
$sizes = @(16, 24, 32, 48, 64, 128, 256)

Add-Type -AssemblyName System.Drawing

function New-RoundedRectanglePath {
    param(
        [float]$X,
        [float]$Y,
        [float]$Width,
        [float]$Height,
        [float]$Radius
    )

    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $diameter = $Radius * 2
    $path.AddArc($X, $Y, $diameter, $diameter, 180, 90)
    $path.AddArc($X + $Width - $diameter, $Y, $diameter, $diameter, 270, 90)
    $path.AddArc($X + $Width - $diameter, $Y + $Height - $diameter, $diameter, $diameter, 0, 90)
    $path.AddArc($X, $Y + $Height - $diameter, $diameter, $diameter, 90, 90)
    $path.CloseFigure()
    return $path
}

function New-FrameScopeIconBitmap {
    param([int]$Size)

    $bitmap = New-Object System.Drawing.Bitmap($Size, $Size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $graphics.Clear([System.Drawing.Color]::Transparent)

    $scale = $Size / 256.0
    $pad = [Math]::Max(1.0, 12.0 * $scale)
    $corner = [Math]::Max(4.0, 46.0 * $scale)
    $shell = New-RoundedRectanglePath $pad $pad ($Size - 2 * $pad) ($Size - 2 * $pad) $corner

    $backgroundRect = [System.Drawing.RectangleF]::new($pad, $pad, $Size - 2 * $pad, $Size - 2 * $pad)
    $background = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $backgroundRect,
        [System.Drawing.Color]::FromArgb(255, 17, 31, 47),
        [System.Drawing.Color]::FromArgb(255, 9, 92, 120),
        135.0
    )
    $graphics.FillPath($background, $shell)
    $background.Dispose()

    $rimWidth = [Math]::Max(1.0, 7.0 * $scale)
    $rimPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(230, 69, 214, 255), $rimWidth)
    $graphics.DrawPath($rimPen, $shell)
    $rimPen.Dispose()
    $shell.Dispose()

    $panel = New-RoundedRectanglePath (42 * $scale) (58 * $scale) (172 * $scale) (132 * $scale) (18 * $scale)
    $panelBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(120, 5, 13, 24))
    $graphics.FillPath($panelBrush, $panel)
    $panelBrush.Dispose()
    $panelPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(130, 190, 238, 255), [Math]::Max(1.0, 3.0 * $scale))
    $graphics.DrawPath($panelPen, $panel)
    $panelPen.Dispose()
    $panel.Dispose()

    $axisPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(140, 215, 244, 255), [Math]::Max(1.0, 4.0 * $scale))
    $graphics.DrawLine($axisPen, 65 * $scale, 162 * $scale, 185 * $scale, 162 * $scale)
    $graphics.DrawLine($axisPen, 65 * $scale, 162 * $scale, 65 * $scale, 94 * $scale)
    $axisPen.Dispose()

    $linePen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 150, 255, 88), [Math]::Max(2.0, 10.0 * $scale))
    $linePen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $linePen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $linePen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round
    $points = @(
        ([System.Drawing.PointF]::new(72 * $scale, 150 * $scale)),
        ([System.Drawing.PointF]::new(100 * $scale, 128 * $scale)),
        ([System.Drawing.PointF]::new(126 * $scale, 138 * $scale)),
        ([System.Drawing.PointF]::new(158 * $scale, 94 * $scale)),
        ([System.Drawing.PointF]::new(188 * $scale, 111 * $scale))
    )
    if ($Size -ge 24) {
        $graphics.DrawLines($linePen, $points)
    } else {
        $graphics.DrawLine($linePen, 68 * $scale, 151 * $scale, 184 * $scale, 104 * $scale)
    }
    $linePen.Dispose()

    $scanPen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 255, 211, 80), [Math]::Max(2.0, 8.0 * $scale))
    $scanPen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $scanPen.EndCap = [System.Drawing.Drawing2D.LineCap]::Round
    $graphics.DrawLine($scanPen, 150 * $scale, 82 * $scale, 198 * $scale, 49 * $scale)
    $scanPen.Dispose()

    if ($Size -ge 32) {
        $dotBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 255, 211, 80))
        $graphics.FillEllipse($dotBrush, 192 * $scale, 43 * $scale, 18 * $scale, 18 * $scale)
        $dotBrush.Dispose()
    }

    if ($Size -ge 64) {
        $lettersFont = [System.Drawing.Font]::new('Segoe UI', [single](30 * $scale), [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
        $lettersBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(235, 236, 250, 255))
        $format = New-Object System.Drawing.StringFormat
        $format.Alignment = [System.Drawing.StringAlignment]::Center
        $format.LineAlignment = [System.Drawing.StringAlignment]::Center
        $lettersRect = [System.Drawing.RectangleF]::new(55 * $scale, 176 * $scale, 146 * $scale, 46 * $scale)
        $graphics.DrawString('FS', $lettersFont, $lettersBrush, $lettersRect, $format)
        $format.Dispose()
        $lettersBrush.Dispose()
        $lettersFont.Dispose()
    }

    $graphics.Dispose()
    return $bitmap
}

function Write-IcoFile {
    param(
        [string]$Path,
        [int[]]$Sizes
    )

    $frames = @()
    foreach ($size in $Sizes) {
        $bitmap = New-FrameScopeIconBitmap -Size $size
        $stream = New-Object System.IO.MemoryStream
        $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
        $frames += [pscustomobject]@{
            Size = $size
            Bytes = $stream.ToArray()
        }
        $stream.Dispose()
        $bitmap.Dispose()
    }

    $output = [System.IO.File]::Create($Path)
    $writer = New-Object System.IO.BinaryWriter($output)
    try {
        $writer.Write([UInt16]0)
        $writer.Write([UInt16]1)
        $writer.Write([UInt16]$frames.Count)
        $imageOffset = 6 + ($frames.Count * 16)
        foreach ($frame in $frames) {
            $writer.Write([byte]$(if ($frame.Size -eq 256) { 0 } else { $frame.Size }))
            $writer.Write([byte]$(if ($frame.Size -eq 256) { 0 } else { $frame.Size }))
            $writer.Write([byte]0)
            $writer.Write([byte]0)
            $writer.Write([UInt16]1)
            $writer.Write([UInt16]32)
            $writer.Write([UInt32]$frame.Bytes.Length)
            $writer.Write([UInt32]$imageOffset)
            $imageOffset += $frame.Bytes.Length
        }
        foreach ($frame in $frames) {
            $writer.Write($frame.Bytes)
        }
    }
    finally {
        $writer.Dispose()
        $output.Dispose()
    }
}

New-Item -ItemType Directory -Path $iconDir -Force | Out-Null
$png = New-FrameScopeIconBitmap -Size 256
try {
    $png.Save($pngPath, [System.Drawing.Imaging.ImageFormat]::Png)
}
finally {
    $png.Dispose()
}

Write-IcoFile -Path $icoPath -Sizes $sizes
"Generated $pngPath"
"Generated $icoPath"

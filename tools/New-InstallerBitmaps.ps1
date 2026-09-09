
# Generates the FrameScope-branded NSIS bitmaps (24-bit BMP as required by
# MUI2): a 164x314 welcome/finish sidebar and a 150x57 header, plus the
# full-bleed wizard art (welcome + finish) stretched by the BgImage plugin.
param(
    [string]$RepoRoot = (Split-Path -Parent $PSScriptRoot)
)
$ErrorActionPreference = 'Stop'

Add-Type -AssemblyName System.Drawing

$iconPath = Join-Path $RepoRoot 'assets\icon\framescope-icon.png'
$outDir = Join-Path $RepoRoot 'desktop-preview\src-tauri\installer'
New-Item -ItemType Directory -Path $outDir -Force | Out-Null
$icon = [System.Drawing.Image]::FromFile($iconPath)

function New-RoundedRectanglePath {
    param([float]$X, [float]$Y, [float]$Width, [float]$Height, [float]$Radius)
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $Radius * 2
    $path.AddArc($X, $Y, $d, $d, 180, 90)
    $path.AddArc($X + $Width - $d, $Y, $d, $d, 270, 90)
    $path.AddArc($X + $Width - $d, $Y + $Height - $d, $d, $d, 0, 90)
    $path.AddArc($X, $Y + $Height - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

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
$g.DrawImage($icon, [int]((164 - $iconSize) / 2), 84, $iconSize, $iconSize)
$font = New-Object System.Drawing.Font('Microsoft YaHei UI', 13.5, [System.Drawing.FontStyle]::Bold)
$textBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 232, 237, 244))
$fmt = New-Object System.Drawing.StringFormat
$fmt.Alignment = [System.Drawing.StringAlignment]::Center
$g.DrawString('FrameScope', $font, $textBrush, ([float](164 / 2)), [float]164, $fmt)
$accent = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 45, 212, 191))
$g.FillRectangle($accent, 52, 196, 60, 2)
$subFont = New-Object System.Drawing.Font('Microsoft YaHei UI', 7.5)
$subBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 140, 156, 172))
$g.DrawString('Performance Monitor', $subFont, $subBrush, ([float](164 / 2)), [float]208, $fmt)
$bmp.Save((Join-Path $outDir 'sidebar.bmp'), [System.Drawing.Imaging.ImageFormat]::Bmp)
$g.Dispose(); $bmp.Dispose(); $font.Dispose(); $textBrush.Dispose(); $subBrush.Dispose(); $subFont.Dispose()

# --- header: 150x57 ---
$bmp, $g = New-Canvas 150 57
Draw-Background $g 150 57
$g.DrawImage($icon, 10, 12, 33, 33)
$font2 = New-Object System.Drawing.Font('Microsoft YaHei UI', 11, [System.Drawing.FontStyle]::Bold)
$textBrush2 = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 232, 237, 244))
$fmt2 = New-Object System.Drawing.StringFormat
$fmt2.Alignment = [System.Drawing.StringAlignment]::Near
$g.DrawString('FrameScope', $font2, $textBrush2, [float]50, [float]19, $fmt2)
$bmp.Save((Join-Path $outDir 'header.bmp'), [System.Drawing.Imaging.ImageFormat]::Bmp)
$g.Dispose(); $bmp.Dispose(); $font2.Dispose(); $textBrush2.Dispose()

# --- full-bleed wizard art (welcome + finish), 500x330, stretched by BgImage ---
function New-FullBleedArtScaled {
    param([string]$OutPath, [int]$W, [int]$H, [float]$TitleSize, [float]$SubSize, [string]$Headline, [string]$SubLine, [bool]$LightCard = $false)
    $w = $W; $h = $H
    $bmp = New-Object System.Drawing.Bitmap($w, $h, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAlias
    $rect = New-Object System.Drawing.Rectangle(0, 0, $w, $h)
    $c1 = [System.Drawing.Color]::FromArgb(255, 4, 7, 12)
    $c2 = [System.Drawing.Color]::FromArgb(255, 16, 34, 50)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($rect, $c1, $c2, 55.0)
    $g.FillRectangle($brush, $rect)
    $brush.Dispose()
    foreach ($glow in @(@{X = [int]($w * 0.78); Y = [int]($h * 0.22); C = [System.Drawing.Color]::FromArgb(36, 45, 212, 191) },
                        @{X = [int]($w * 0.18); Y = [int]($h * 0.75); C = [System.Drawing.Color]::FromArgb(30, 59, 130, 246) })) {
        $gp = New-Object System.Drawing.Drawing2D.GraphicsPath
        $gp.AddEllipse([int]($glow.X - 190), [int]($glow.Y - 190), 380, 380)
        $pgb = New-Object System.Drawing.Drawing2D.PathGradientBrush($gp)
        $pgb.CenterColor = $glow.C
        $pgb.SurroundColors = @([System.Drawing.Color]::FromArgb(0, 0, 0, 0))
        $g.FillPath($pgb, $gp)
        $pgb.Dispose(); $gp.Dispose()
    }
    # 亮色玻璃卡（复选框宿主，黑色文字可读）
$cardFill = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(216, 233, 238, 244))
$cardPath = New-RoundedRectanglePath -X (40 * $sx) -Y (208 * $sy) -Width (200 * $sx) -Height (96 * $sy) -Radius 12
$g.FillPath($cardFill, $cardPath)
$cardPath.Dispose(); $cardFill.Dispose()
$penGlass = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(60, 255, 255, 255))
    $gp1 = New-RoundedRectanglePath -X (40 * $sx) -Y (208 * $sy) -Width (200 * $sx) -Height (96 * $sy) -Radius 12
    $g.DrawPath($penGlass, $gp1); $gp1.Dispose()
    $gp2 = New-RoundedRectanglePath -X (268 * $sx) -Y (232 * $sy) -Width (192 * $sx) -Height (72 * $sy) -Radius 12
    $g.DrawPath($penGlass, $gp2); $gp2.Dispose()
    $penGlass.Dispose()
    $sx = $w / 500.0; $sy = $h / 330.0; $g.DrawImage($icon, [int](40 * $sx), [int](44 * $sy), [int](56 * $sx), [int](56 * $sy))
    if ($LightCard) {
        # 亮色卡：为完成页复选框提供可读背景（u 坐标 (42,-78)/(42,-60) 恰落于此）
        $lightFill = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(235, 235, 240, 246))
        $lp = New-RoundedRectanglePath -X (44 * $sx) -Y (172 * $sy) -Width (182 * $sx) -Height (48 * $sy) -Radius 8
        $g.FillPath($lightFill, $lp)
        $lp.Dispose(); $lightFill.Dispose()
    }
    $fmtL = New-Object System.Drawing.StringFormat
    $fmtL.Alignment = [System.Drawing.StringAlignment]::Near
    $fontTitle = New-Object System.Drawing.Font('Microsoft YaHei UI', $TitleSize, [System.Drawing.FontStyle]::Bold)
    $tb = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 232, 237, 244))
    $g.DrawString('FrameScope', $fontTitle, $tb, [float](110 * $sx), [float](52 * $sy), $fmtL)
    $accent = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 45, 212, 191))
    $g.FillRectangle($accent, [int](40 * $sx), [int](124 * $sy), [int](46 * $sx), 3)
    $fontSub = New-Object System.Drawing.Font('Microsoft YaHei UI', $SubSize)
    $g.DrawString($Headline, $fontSub, $tb, [float](40 * $sx), [float](152 * $sy), $fmtL)
    $subBrush2 = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 148, 164, 180))
    $g.DrawString($SubLine, $fontSub, $subBrush2, [float](40 * $sx), [float](180 * $sy), $fmtL)
    $fontV = New-Object System.Drawing.Font('Segoe UI', 7.5)
    $fmtR = New-Object System.Drawing.StringFormat
    $fmtR.Alignment = [System.Drawing.StringAlignment]::Far
    $g.DrawString('v1.0.0', $fontV, $subBrush2, [float]($w - 12 * $sx), [float]($h - 26 * $sy), $fmtR)
    $bmp.Save($OutPath, [System.Drawing.Imaging.ImageFormat]::Bmp)
    $g.Dispose(); $bmp.Dispose(); $fontTitle.Dispose(); $fontSub.Dispose(); $fontV.Dispose()
    $tb.Dispose(); $subBrush2.Dispose(); $accent.Dispose(); $fmtL.Dispose(); $fmtR.Dispose()
}

# 多 DPI 尺寸：96/120/144/192（向导页按系统 DPI 选对应位图铺满）
foreach ($dpi in @(96, 120, 144, 192)) {
    $scale = $dpi / 96.0
    $w = [int](500 * $scale); $h = [int](330 * $scale)
    $tSize = [float](20 * $scale); $sSize = [float](10.5 * $scale)
    New-FullBleedArtScaled -OutPath (Join-Path $outDir ("welcome-{0}.bmp" -f $dpi)) -W $w -H $h -TitleSize $tSize -SubSize $sSize -Headline '欢迎使用 FrameScope 安装向导' -SubLine '点击下一步开始安装到您的电脑'
    New-FullBleedArtScaled -OutPath (Join-Path $outDir ("finish-{0}.bmp" -f $dpi)) -W $w -H $h -TitleSize $tSize -SubSize $sSize -Headline '安装完成，祝您游戏愉快' -SubLine '点击完成按钮启动 FrameScope' -LightCard:$true
}
$icon.Dispose()
Write-Output "Installer bitmaps (incl. full-bleed) written to $outDir"

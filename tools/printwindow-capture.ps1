# Capture what a window ACTUALLY presents, via PrintWindow PW_RENDERFULLCONTENT,
# without bringing it to the foreground.
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public class WinShot {
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr dc, uint flags);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [DllImport("user32.dll")] public static extern bool IsWindow(IntPtr h);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
}
"@
Add-Type -AssemblyName System.Drawing

$p = Get-Process FrameScope -ErrorAction SilentlyContinue | Where-Object { $_.MainWindowHandle -ne 0 } | Select-Object -First 1
if (-not $p) { Write-Output 'no window'; exit 1 }
$h = $p.MainWindowHandle
$r = New-Object WinShot+RECT
[void][WinShot]::GetWindowRect($h, [ref]$r)
$w = $r.R - $r.L; $ht = $r.B - $r.T
Write-Output ("window " + $w + "x" + $ht)

$bmp = New-Object System.Drawing.Bitmap($w, $ht)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$dc = $g.GetHdc()
# PW_RENDERFULLCONTENT = 2 (captures DComp/DWM-presented content)
$ok = [WinShot]::PrintWindow($h, $dc, 2)
$g.ReleaseHdc($dc)
$g.Dispose()
Write-Output ("PrintWindow ok: " + $ok)
$bmp.Save("$env:TEMP\fs-pw.png", [System.Drawing.Imaging.ImageFormat]::Png)
$bmp.Dispose()
Write-Output "saved $env:TEMP\fs-pw.png"

$img = [System.Drawing.Bitmap]::FromFile("$env:TEMP\fs-pw.png")
Write-Output ("center px: " + $img.GetPixel([int]($w/2), [int]($ht/2)))
Write-Output ("gpu-card px: " + $img.GetPixel([int]($w*0.45), [int]($ht*0.33)))
$img.Dispose()

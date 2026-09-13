# Forensics: where IS the WebView2 content window, really?
# Uses GetParent (reliable) on every Chrome_WidgetWin window alive.
Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
using System.Text;
public class CW {
  public delegate bool EnumProc(IntPtr h, IntPtr l);
  [DllImport("user32.dll")] public static extern bool EnumWindows(EnumProc cb, IntPtr l);
  [DllImport("user32.dll")] public static extern int GetClassName(IntPtr h, StringBuilder s, int n);
  [DllImport("user32.dll")] public static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
  [DllImport("user32.dll")] public static extern IntPtr GetParent(IntPtr h);
  [DllImport("user32.dll")] public static extern bool IsWindowVisible(IntPtr h);
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int L, T, R, B; }
}
"@

$fs = Get-Process FrameScope -ErrorAction SilentlyContinue
Write-Output ("FrameScope PIDs: " + (($fs | ForEach-Object Id) -join ','))
$fsHwnds = $fs | Where-Object { $_.MainWindowHandle -ne 0 } | ForEach-Object { $_.MainWindowHandle }
Write-Output ("main hwnds: " + ($fsHwnds -join ','))

# webview host process pid for this instance
$wv = Get-CimInstance Win32_Process -Filter "Name='msedgewebview2.exe'" | Where-Object { $_.ParentProcessId -in $fs.Id }
Write-Output ("webview host pids: " + (($wv | ForEach-Object ProcessId) -join ','))

$rows = New-Object System.Collections.ArrayList
$cb = [CW+EnumProc]{ param($h, $l)
  $sb = New-Object System.Text.StringBuilder 128
  [CW]::GetClassName($h, $sb, 128) | Out-Null
  if ($sb.ToString() -like 'Chrome_WidgetWin*') {
    $pid2 = 0
    [CW]::GetWindowThreadProcessId($h, [ref]$pid2) | Out-Null
    $parent = [CW]::GetParent($h)
    $r = New-Object CW+RECT
    [CW]::GetWindowRect($h, [ref]$r) | Out-Null
    [void]$rows.Add([pscustomobject]@{
      Hwnd = $h; Pid = $pid2; Parent = $parent
      Visible = [CW]::IsWindowVisible($h)
      Rect = "$($r.L),$($r.T) $(($r.R-$r.L))x$(($r.B-$r.T))"
      InFS = ($fsHwnds -contains $parent)
    })
  }
  $true
}
[CW]::EnumWindows($cb, [IntPtr]::Zero) | Out-Null

$mine = $rows | Where-Object { $_.Pid -in ($wv.ProcessId) -or $_.Pid -in $fs.Id }
Write-Output ("--- Chrome windows belonging to FrameScope's webview tree ---")
$mine | ForEach-Object {
  $pname = (Get-Process -Id $_.Pid -ErrorAction SilentlyContinue).Name
  Write-Output ("hwnd={0} class-pid={1}({2}) parent={3} visible={4} rect={5} parentIsFSMain={6}" -f $_.Hwnd, $_.Pid, $pname, $_.Parent, $_.Visible, $_.Rect, $_.InFS)
}

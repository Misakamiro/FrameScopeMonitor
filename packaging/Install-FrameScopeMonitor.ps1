$ErrorActionPreference = 'Stop'

$payload = Join-Path $PSScriptRoot 'payload.zip'
if (-not (Test-Path -LiteralPath $payload)) {
    throw "payload.zip not found next to installer."
}

$appDir = Join-Path $env:LOCALAPPDATA 'FrameScopeMonitor'
$desktopLink = Join-Path ([Environment]::GetFolderPath('Desktop')) 'FrameScope Monitor.lnk'
$startMenuLink = Join-Path ([Environment]::GetFolderPath('StartMenu')) 'Programs\FrameScope Monitor.lnk'

Get-Process -Name 'FrameScopeMonitor' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue

New-Item -ItemType Directory -Path $appDir -Force | Out-Null
Expand-Archive -LiteralPath $payload -DestinationPath $appDir -Force

$exe = Join-Path $appDir 'FrameScopeMonitor.exe'
if (-not (Test-Path -LiteralPath $exe)) {
    throw "FrameScopeMonitor.exe was not installed correctly."
}

$shell = New-Object -ComObject WScript.Shell
foreach ($link in @($desktopLink, $startMenuLink)) {
    $shortcut = $shell.CreateShortcut($link)
    $shortcut.TargetPath = $exe
    $shortcut.WorkingDirectory = $appDir
    $shortcut.IconLocation = $exe
    $shortcut.Save()
}

Start-Process -FilePath $exe -WorkingDirectory $appDir
Write-Host "FrameScope Monitor installed to: $appDir"

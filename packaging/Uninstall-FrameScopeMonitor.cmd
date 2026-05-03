@echo off
setlocal
powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "$ErrorActionPreference='SilentlyContinue'; Get-Process -Name FrameScopeMonitor | Stop-Process -Force; Remove-Item -LiteralPath ([Environment]::GetFolderPath('Desktop') + '\FrameScope Monitor.lnk') -Force; Remove-Item -LiteralPath ([Environment]::GetFolderPath('StartMenu') + '\Programs\FrameScope Monitor.lnk') -Force; Remove-Item -LiteralPath (Join-Path $env:LOCALAPPDATA 'FrameScopeMonitor') -Recurse -Force"
exit /b 0

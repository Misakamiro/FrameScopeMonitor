@echo off
setlocal
cd /d "%~dp0"
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%~dp0Remove-GameLiteAutoTrigger.ps1" %*
pause

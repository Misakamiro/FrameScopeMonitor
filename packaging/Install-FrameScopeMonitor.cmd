@echo off
setlocal
set "SCRIPT=%~dp0Install-FrameScopeMonitor.ps1"
if not exist "%SCRIPT%" (
  echo Install script missing: %SCRIPT%
  pause
  exit /b 1
)
powershell.exe -NoProfile -ExecutionPolicy Bypass -File "%SCRIPT%"
if errorlevel 1 (
  echo.
  echo FrameScope Monitor installation failed.
  pause
  exit /b 1
)
exit /b 0

@echo off
setlocal
set "UNINSTALLER=%~dp0FrameScopeUninstaller.exe"
if exist "%UNINSTALLER%" (
    start "" "%UNINSTALLER%" %*
    exit /b 0
)
reg delete "HKCU\Software\Microsoft\Windows\CurrentVersion\Uninstall\FrameScopeMonitor" /f >nul 2>nul
exit /b 1

@echo off
echo FrameScope - restore previous program version, keeping settings and reports.
"%~dp0FrameScope Installer.exe" --restore-previous /S "/D=%~dp0."
if errorlevel 1 (
  echo Recovery did not complete. Keep this directory and its transaction backups.
  pause
)

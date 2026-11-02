@echo off
setlocal
set "UNINSTALLER=%~dp0FrameScope Installer.exe"
if exist "%UNINSTALLER%" (
    start "" "%UNINSTALLER%" --uninstall %*
    exit /b 0
)
echo Transactional FrameScope uninstaller not found. No files or registration were deleted.
exit /b 1

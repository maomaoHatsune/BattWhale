@echo off
rem Downloads the latest BattWhale.exe from GitHub Releases into the current
rem folder and clears the Mark-of-the-Web, so SmartScreen will not stop it.
rem ASCII only: cmd.exe reads this file with the OEM code page.
setlocal
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0tools\fetch-latest.ps1" %*
if errorlevel 1 (
    echo.
    echo download failed - see the message above
    pause
    exit /b 1
)
echo.
pause

@echo off
setlocal
title Install Galgame metadata plugins v0.8
echo.
echo  This will close Playnite, install the 5 metadata providers, then start Playnite again.
echo  Close any game you are playing BEFORE running this.
echo.
pause
echo Closing Playnite...
taskkill /IM Playnite.DesktopApp.exe /F >nul 2>&1
timeout /t 3 /nobreak >nul
echo Installing...
powershell -NoProfile -ExecutionPolicy Bypass -File "%~dp0install.ps1"
if errorlevel 1 goto fail
echo Starting Playnite...
start "" "D:\playnite\Playnite.DesktopApp.exe"
echo.
echo Done. The plugins were updated to v0.8.
pause
exit /b 0
:fail
echo.
echo Install failed. Close Playnite completely and run this again.
pause
exit /b 1

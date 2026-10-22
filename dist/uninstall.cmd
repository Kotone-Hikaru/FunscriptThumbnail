@echo off
rem ===================================================================
rem  Funscript thumbnail - remove the add-on from this PC.
rem
rem  You do NOT normally need this file: the add-on is listed in
rem  Settings > Apps > Installed apps (and in the classic "Programs and
rem  Features"), where an Uninstall button does exactly the same thing.
rem ===================================================================
setlocal
net session >nul 2>&1
if errorlevel 1 (
  echo Requesting administrator rights...
  powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs" >nul 2>&1
  if errorlevel 1 (
    echo.
    echo Could not elevate automatically. Right-click uninstall.cmd
    echo and choose "Run as administrator".
    pause
  )
  exit /b
)
cd /d "%~dp0"
cls
echo This removes the .funscript thumbnail handler, its registry entries, its
echo entry in Settings ^> Apps, and the DLL in C:\ProgramData\FunscriptThumbnail.
echo It does NOT touch your .funscript files.
echo.
choice /c YN /m "Continue"
if errorlevel 2 goto :cancel
FunscriptThumbTool.exe uninstall
echo.
echo Thumbnails already in Explorer's cache stay until that cache is cleared.
echo Run clear-cache.cmd if you want the icons to disappear right away.
pause
exit /b 0
:cancel
echo Cancelled.
pause

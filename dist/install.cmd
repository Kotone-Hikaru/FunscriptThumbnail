@echo off
rem ===================================================================
rem  Funscript thumbnail - install for all users on this PC
rem
rem  Just double-click it. The script asks Windows for administrator
rem  rights itself (UAC), because the handler is registered machine-wide.
rem  You can also right-click -> "Run as administrator"; that works too.
rem ===================================================================
setlocal
net session >nul 2>&1
if errorlevel 1 (
  echo Requesting administrator rights...
  powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs" >nul 2>&1
  if errorlevel 1 (
    echo.
    echo Could not elevate automatically. Right-click install.cmd
    echo and choose "Run as administrator".
    pause
  )
  exit /b
)
cd /d "%~dp0"

rem ---- the only runtime requirement: .NET Framework 4.8 ----------------
rem This add-on is built for .NET Framework 4.8, which ships with Windows 10
rem version 1903+ and Windows 11. Check before doing anything so that an old
rem machine gets a readable message instead of a silent no-op.
set "REL="
for /f "delims=" %%a in ('powershell -NoProfile -Command "try{(Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full' -ErrorAction Stop).Release}catch{0}" 2^>nul') do set "REL=%%a"
if "%REL%"=="" set "REL=0"
if %REL% GEQ 528040 goto :rel_ok
echo.
echo WARNING: .NET Framework 4.8 was not detected on this PC.
echo Windows 10 version 1903+ and Windows 11 include it; older systems need it installed:
echo   https://dotnet.microsoft.com/download/dotnet-framework/net48
echo.
choice /c YN /m "Try to install anyway"
if errorlevel 2 goto :end
:rel_ok

rem ---- sanity-check the payload ---------------------------------------
if not exist "FunscriptThumbTool.exe" goto :missing
if not exist "FunscriptThumbnail.dll" goto :missing

FunscriptThumbTool.exe install
if errorlevel 1 (
  echo.
  echo Installation reported an error. See the messages above.
  echo Check the current state at any time with:  FunscriptThumbTool.exe status
  pause
  exit /b 1
)

echo.
echo Done. The thumbnail handler is registered machine-wide and the DLL lives in
echo C:\ProgramData\FunscriptThumbnail -- readable by every account on this PC,
echo so this one install covers all users. You can now delete this folder.
echo.
echo Open a folder that contains .funscript files to see it.
echo.
echo NOTE: Explorer caches thumbnails by file path. Files it already looked at
echo BEFORE this add-on existed can keep showing the old blank icon.
echo Any of these forces a fresh render:
echo   1) copy a .funscript to a new file name  - new path means no cached entry
echo   2) run clear-cache.cmd                  - wipes the cache, restarts Explorer
echo.
echo To remove the add-on later, use Settings ^> Apps ^> Installed apps
echo ^(it is listed as "Funscript ..."^), or run uninstall.cmd.
echo.
pause
exit /b 0

:missing
echo.
echo ERROR: FunscriptThumbTool.exe / FunscriptThumbnail.dll not found next to this script.
echo Copy the whole folder, not just install.cmd.
pause
exit /b 1

:end
echo Cancelled.
pause

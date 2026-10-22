@echo off
rem Force Explorer to re-render every thumbnail, including .funscript icons that
rem were cached as blank before the plugin was installed.
rem
rem MUST run as administrator: the thumbnail cache files live in YOUR profile, but
rem killing/restarting Explorer reliably needs the elevated session.
rem
rem Only Explorer's own thumbnail/icon caches are deleted. No documents are touched;
rem Windows rebuilds both caches automatically.
net session >nul 2>&1
if errorlevel 1 (
  echo Requesting administrator rights...
  powershell -NoProfile -Command "Start-Process -FilePath '%~f0' -Verb RunAs" >nul 2>&1
  if errorlevel 1 (
    echo.
    echo Could not elevate automatically. Right-click clear-cache.cmd
    echo and choose "Run as administrator".
    pause
  )
  exit /b
)
cls
echo This will:
echo   - close Explorer (taskbar + desktop icons disappear for a moment)
echo   - delete  %%LOCALAPPDATA%%\Microsoft\Windows\Explorer\thumbcache_*.db
echo   - delete  %%LOCALAPPDATA%%\Microsoft\Windows\Explorer\iconcache_*.db
echo   - restart Explorer
echo.
echo Windows rebuilds these caches automatically. No personal files are deleted.
echo.
choice /c YN /m "Continue"
if errorlevel 2 goto :cancel

taskkill /f /im explorer.exe >nul 2>&1
timeout /t 2 /nobreak >nul

set "CACHE=%LOCALAPPDATA%\Microsoft\Windows\Explorer"
del /f /q "%CACHE%\thumbcache_*.db" >nul 2>&1
del /f /q "%CACHE%\iconcache_*.db" >nul 2>&1
del /f /q "%CACHE%\thumbcache_*.db" >nul 2>&1

start "" explorer.exe
echo.
echo Done. Give Explorer a few seconds, then open a folder with .funscript files.
pause
exit /b 0

:cancel
echo Cancelled.
pause

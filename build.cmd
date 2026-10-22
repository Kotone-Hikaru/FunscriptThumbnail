@echo off
setlocal
cd /d "%~dp0"

echo ===============================================
echo  Funscript Thumbnail - build from source
echo ===============================================
echo.

rem ---- locate the .NET SDK -------------------------------------------------
set "DOTNET="
where dotnet >nul 2>&1 && set "DOTNET=dotnet"
if not defined DOTNET if exist "%ProgramFiles%\dotnet\dotnet.exe" set "DOTNET=%ProgramFiles%\dotnet\dotnet.exe"
if not defined DOTNET if exist "%ProgramFiles(x86)%\dotnet\dotnet.exe" set "DOTNET=%ProgramFiles(x86)%\dotnet\dotnet.exe"

if not defined DOTNET (
    echo ERROR: the .NET SDK was not found.
    echo.
    echo   Install the SDK from https://dotnet.microsoft.com/download
    echo   The .NET Framework 4.8 *runtime* alone is not enough to build.
    echo.
    pause
    exit /b 1
)

echo Using SDK: %DOTNET%
%DOTNET% --version
echo.

rem ---- build ---------------------------------------------------------------
%DOTNET% build FunscriptThumbnail.sln -c Release
if errorlevel 1 (
    echo.
    echo BUILD FAILED.
    pause
    exit /b 1
)

rem ---- stage the distributable into dist\ -----------------------------------
if not exist dist mkdir dist
copy /y "src\FunscriptThumbnail\bin\Release\net48\FunscriptThumbnail.dll" dist\ >nul
copy /y "src\FunscriptThumbTool\bin\Release\net48\FunscriptThumbTool.exe" dist\ >nul
if exist "src\FunscriptThumbTool\bin\Release\net48\FunscriptThumbTool.exe.config" (
    copy /y "src\FunscriptThumbTool\bin\Release\net48\FunscriptThumbTool.exe.config" dist\ >nul
)

echo.
echo Build OK. Binaries copied to dist\.
echo The dist\ folder is a complete installer payload: double-click install.cmd.
echo.
pause

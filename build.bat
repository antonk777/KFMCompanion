@echo off
setlocal
cd /d "%~dp0"

where dotnet >nul 2>&1
if errorlevel 1 (
    echo .NET SDK is not installed.
    echo Download: https://dotnet.microsoft.com/download
    echo Need the SDK, not just the Runtime. x86 / WinForms / .NET Framework 4.8 targeting pack.
    pause
    exit /b 1
)

set "CONFIG=Debug"
if /i "%~1"=="release" set "CONFIG=Release"

echo Building KFM Companion (%CONFIG% x86)...
dotnet build SAM.sln -c %CONFIG% -p:Platform=x86 -t:Rebuild
if errorlevel 1 (
    echo.
    echo Build failed.
    pause
    exit /b 1
)

echo.
if /i "%CONFIG%"=="Release" (
    echo OK: "%~dp0upload\KFM Companion.exe"
) else (
    echo OK: "%~dp0bin\KFM Companion.exe"
)
pause

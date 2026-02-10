@echo off
setlocal EnableDelayedExpansion

:: Auto-elevate to Administrator
net session >nul 2>&1
if %errorLevel% neq 0 (
    powershell -Command "Start-Process -FilePath '%~f0' -Verb RunAs"
    exit /b
)

:: ANSI colors
for /f %%a in ('echo prompt $E ^| cmd') do set "ESC=%%a"
set "GREEN=%ESC%[92m"
set "RED=%ESC%[91m"
set "YELLOW=%ESC%[93m"
set "CYAN=%ESC%[96m"
set "GRAY=%ESC%[90m"
set "RESET=%ESC%[0m"

cd /d "%~dp0"

:: Read version from .csproj
for /f "tokens=*" %%a in ('powershell -Command "(Select-String -Path GameRegionGuard.csproj -Pattern '<Version>(.*)</Version>').Matches.Groups[1].Value"') do set "VERSION=%%a"

:: Capture .NET SDK version
for /f %%a in ('dotnet --version 2^>nul') do set "SDKVER=%%a"
if "%SDKVER%"=="" (
    echo.
    echo %RED%  [ERROR]%RESET%   .NET SDK not found. Install it from https://dotnet.microsoft.com/download
    echo.
    echo %GRAY%  Press any key to close...%RESET%
    pause >nul
    exit /b 1
)

:: Capture start time via PowerShell (avoids leading-space parsing issues)
for /f %%a in ('powershell -Command "[int](Get-Date).TimeOfDay.TotalSeconds"') do set "START=%%a"

echo.
echo %CYAN%  [INFO]%RESET%    GameRegionGuard v%VERSION%
echo %CYAN%  [INFO]%RESET%    .NET SDK %SDKVER%
echo %CYAN%  [INFO]%RESET%    Target: win-x64  ^|  Release  ^|  Self-contained  ^|  Single file
echo.
echo %GRAY%  [INFO]    Running dotnet publish...%RESET%
echo.

dotnet publish GameRegionGuard.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false

set "CODE=%errorLevel%"

:: Elapsed time
for /f %%a in ('powershell -Command "[int](Get-Date).TimeOfDay.TotalSeconds"') do set "END=%%a"
set /a "ELAPSED=END-START"

echo.
if %CODE% equ 0 (
    echo %GREEN%  [SUCCESS]%RESET%  Build completed in %ELAPSED%s
    echo %GREEN%  [SUCCESS]%RESET%  Output: bin\Release\GameRegionGuard\GameRegionGuard.exe
) else (
    echo %RED%  [ERROR]%RESET%   Build failed with exit code %CODE%
    echo.
    echo %YELLOW%  [WARNING]%RESET%  If dotnet publish output is unclear, check the following:
    echo %GRAY%             - SDK version mismatch  : dotnet --version%RESET%
    echo %GRAY%             - Missing dependencies  : dotnet restore%RESET%
    echo %GRAY%             - .NET 8 SDK download   : https://dotnet.microsoft.com/download%RESET%
)

echo.
echo %GRAY%  Press any key to close...%RESET%
pause >nul
endlocal
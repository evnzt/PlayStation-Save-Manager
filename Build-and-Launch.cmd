@echo off
setlocal EnableExtensions
cd /d "%~dp0"

set "DOTNET_ROOT=%CD%\.dotnet"
set "DOTNET=%DOTNET_ROOT%\dotnet.exe"
set "PUBLISH=%CD%\Publish"
set "PROJECT=%CD%\PlayStationSaveManager\PlayStationSaveManager.csproj"

if not exist "%PROJECT%" (
  echo ERROR: Project file is missing.
  pause
  exit /b 1
)

if not exist "%DOTNET%" (
  echo Setting up a private .NET 8 build environment...
  powershell.exe -NoProfile -ExecutionPolicy Bypass -Command "try { [Net.ServicePointManager]::SecurityProtocol=[Net.SecurityProtocolType]::Tls12; Invoke-WebRequest -UseBasicParsing 'https://dot.net/v1/dotnet-install.ps1' -OutFile 'dotnet-install.ps1'; & '.\dotnet-install.ps1' -Channel 8.0 -InstallDir '.dotnet' -NoPath; exit $LASTEXITCODE } catch { Write-Host $_.Exception.Message -ForegroundColor Red; exit 1 }"
  if errorlevel 1 goto :error
)

if exist "%PUBLISH%" rmdir /s /q "%PUBLISH%"
mkdir "%PUBLISH%" >nul 2>&1

echo Building PlayStation Save Manager v1.0.0...
"%DOTNET%" publish "%PROJECT%" -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:DebugType=None -p:DebugSymbols=false -o "%PUBLISH%"
if errorlevel 1 goto :error

copy /y "%CD%\Setup-Engine.ps1" "%PUBLISH%\Setup-Engine.ps1" >nul
copy /y "%CD%\README.txt" "%PUBLISH%\README.txt" >nul
copy /y "%CD%\THIRD-PARTY-NOTICES.txt" "%PUBLISH%\THIRD-PARTY-NOTICES.txt" >nul

echo.
echo Build complete.
echo Launching PlayStation Save Manager v1.0.0...
start "" "%PUBLISH%\PlayStation Save Manager.exe"
exit /b 0

:error
echo.
echo Build failed. Copy or screenshot the red error lines above.
pause
exit /b 1

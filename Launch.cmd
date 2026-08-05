@echo off
cd /d "%~dp0"
if exist "Publish\PlayStation Save Manager.exe" (
  start "" "Publish\PlayStation Save Manager.exe"
  exit /b 0
)
call "Build-and-Launch.cmd"

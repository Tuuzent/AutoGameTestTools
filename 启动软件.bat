@echo off
setlocal
cd /d "%~dp0"
if not exist "bin\GameInputLab.exe" (
  powershell -NoProfile -ExecutionPolicy Bypass -File "scripts\build.ps1" -Release
  if errorlevel 1 (
    echo Build failed.
    pause
    exit /b 1
  )
)
start "" "bin\GameInputLab.exe"
endlocal

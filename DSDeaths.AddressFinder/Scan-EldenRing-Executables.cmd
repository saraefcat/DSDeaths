@echo off
setlocal
title DSDeaths Elden Ring Executable Signature Scan

set "SCAN_TARGET=%~1"
if not defined SCAN_TARGET (
  echo Enter one eldenring.exe or a folder containing Elden Ring executables.
  set /p "SCAN_TARGET=EXE or folder: "
)

if not defined SCAN_TARGET (
  echo No path was supplied.
  pause
  exit /b 2
)

"%~dp0DSDeaths.AddressFinder.exe" --check-exe "%SCAN_TARGET%"
set "SCAN_RESULT=%ERRORLEVEL%"
echo.
pause
exit /b %SCAN_RESULT%

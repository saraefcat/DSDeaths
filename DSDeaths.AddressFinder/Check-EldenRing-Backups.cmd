@echo off
setlocal
title DSDeaths Elden Ring Backup Compatibility Check

set "CHECK_TARGET=%~1"
if not defined CHECK_TARGET (
  echo Enter the folder that contains the Elden Ring backups.
  set /p "CHECK_TARGET=Folder: "
)

if not defined CHECK_TARGET (
  echo No folder was supplied.
  pause
  exit /b 2
)

"%~dp0DSDeaths.AddressFinder.exe" --check-exe "%CHECK_TARGET%"
set "CHECK_RESULT=%ERRORLEVEL%"
echo.
pause
exit /b %CHECK_RESULT%

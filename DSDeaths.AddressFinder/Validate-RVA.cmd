@echo off
setlocal
cd /d "%~dp0"
title DSDeaths Elden Ring RVA Restart Validation

echo ============================================================
echo Use only with Easy Anti-Cheat disabled and the game offline.
echo Completely restart Elden Ring before this validation.
echo ============================================================
echo.

set /p "finder_rva=RVA printed by Address Finder (example 0x12345678): "
set /p "finder_expected=Current cumulative death count: "

DSDeaths.AddressFinder.exe --offline --validate-rva %finder_rva% --offset 0x94 --expected %finder_expected%
set "finder_exit=%ERRORLEVEL%"

echo.
echo Validation exit code: %finder_exit%
pause
exit /b %finder_exit%

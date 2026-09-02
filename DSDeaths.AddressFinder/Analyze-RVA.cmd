@echo off
setlocal
cd /d "%~dp0"
title DSDeaths Elden Ring Signature Research

echo ============================================================
echo Use only with Easy Anti-Cheat disabled and the game offline.
echo Use an RVA that already passed complete-restart validation.
echo ============================================================
echo.

set /p "finder_rva=Validated pointer-storage RVA (example 0x12345678): "
set /p "finder_expected=Current cumulative death count: "
set /p "finder_report=Report file name [DSDeaths.SignatureResearch.txt]: "
if not defined finder_report set "finder_report=DSDeaths.SignatureResearch.txt"

DSDeaths.AddressFinder.exe --offline --analyze-rva "%finder_rva%" --offset 0x94 --expected "%finder_expected%" --report "%finder_report%"
set "finder_exit=%ERRORLEVEL%"

echo.
echo Signature research exit code: %finder_exit%
echo Keep the report from each game version for comparison.
pause
exit /b %finder_exit%

@echo off
setlocal
cd /d "%~dp0"
title DSDeaths Elden Ring Address Finder

echo ============================================================
echo Use only with Easy Anti-Cheat disabled and the game offline.
echo Load a character and have its exact cumulative death count ready.
echo ============================================================
echo.
pause

DSDeaths.AddressFinder.exe --offline
set "finder_exit=%ERRORLEVEL%"

echo.
echo Address Finder exit code: %finder_exit%
echo Keep this window open and copy the requested result lines.
pause
exit /b %finder_exit%

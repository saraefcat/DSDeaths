@echo off
setlocal
cd /d "%~dp0"
title DSDeaths Elden Ring 1.17 Address Finder

echo ============================================================
echo Use only with Easy Anti-Cheat disabled and the game offline.
echo Load the character whose cumulative death count is 33504.
echo ============================================================
echo.
pause

DSDeaths.AddressFinder.exe --offline --known 33504
set "finder_exit=%ERRORLEVEL%"

echo.
echo Address Finder exit code: %finder_exit%
echo Keep this window open and copy the requested result lines.
pause
exit /b %finder_exit%

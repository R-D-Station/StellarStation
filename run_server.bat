@echo off
chcp 65001 > nul

echo ========================================
echo StellarStation Server
echo ========================================
echo.

cd /d "%~dp0Server"

dotnet run

pause
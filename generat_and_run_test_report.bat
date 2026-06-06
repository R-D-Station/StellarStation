@echo off
chcp 65001 > nul

echo ========================================
echo StellarStation ServerTests
echo ========================================
echo.

cd /d "%~dp0ServerTests"

dotnet test --collect:"XPlat Code Coverage"

reportgenerator -reports:"TestResults/**/coverage.cobertura.xml" -targetdir:"coveragereport" -reporttypes:Html

start coveragereport/index.html
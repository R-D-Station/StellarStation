@echo off
chcp 65001 > nul

echo ========================================
echo StellarStation Server - Build and Run
echo ========================================
echo.

call "%~dp0build_server.bat"

if %errorlevel% neq 0 (
    echo Build failed, aborting...
    pause
    exit /b %errorlevel%
)

echo Starting server...
echo.
cd /d "%~dp0Server\publish"
Server.exe

pause
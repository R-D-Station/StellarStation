@echo off
chcp 65001 > nul

cd /d "%~dp0Server\publish"

if not exist "Server.exe" (
    echo [ERROR] Server not built! Run build_server.bat first.
    pause
    exit /b 1
)

echo ========================================
echo StellarStation Server (Published)
echo ========================================
echo.

Server.exe

pause
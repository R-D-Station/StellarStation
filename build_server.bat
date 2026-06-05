@echo off
chcp 65001 > nul
setlocal enabledelayedexpansion

echo ========================================
echo StellarStation Server Build Script
echo ========================================
echo.

cd /d "%~dp0Server"

echo [1/4] Cleaning previous build...
dotnet clean > nul 2>&1
if exist "publish" rmdir /s /q "publish"

echo [2/4] Restoring packages...
dotnet restore
if %errorlevel% neq 0 (
    echo [ERROR] Package restore failed!
    pause
    exit /b %errorlevel%
)

echo [3/4] Building project...
dotnet build -c Release --no-restore
if %errorlevel% neq 0 (
    echo [ERROR] Build failed!
    pause
    exit /b %errorlevel%
)

echo [4/4] Publishing...
dotnet publish -c Release -o publish --no-build

echo.
echo ========================================
echo BUILD COMPLETED SUCCESSFULLY!
echo ========================================
echo.
echo Server files: %~dp0Server\publish\
echo.
pause
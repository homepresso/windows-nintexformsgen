@echo off
REM ============================================================
REM  Nintex Forms Generator - Quick Build Script
REM  Double-click this to build the app and create the MSI.
REM ============================================================

echo.
echo  Nintex Forms Generator - Installer Build
echo  =========================================
echo.

REM Check if PowerShell is available
where powershell >nul 2>nul
if %errorlevel% neq 0 (
    echo ERROR: PowerShell is required but not found.
    pause
    exit /b 1
)

REM Run the PowerShell build script
powershell -ExecutionPolicy Bypass -File "%~dp0build-installer.ps1" %*

if %errorlevel% neq 0 (
    echo.
    echo BUILD FAILED - See errors above.
    pause
    exit /b 1
)

echo.
pause

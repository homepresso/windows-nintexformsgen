@echo off
REM ============================================================
REM  Nintex Forms Generator - Build + Inno Setup Installer
REM
REM  Prerequisites:
REM    - Visual Studio 2022 or .NET SDK with net48 targeting pack
REM    - Inno Setup 6 (https://jrsoftware.org/isdl.php)
REM ============================================================

setlocal enabledelayedexpansion

echo.
echo  ============================================
echo   Nintex Forms Generator - Build Installer
echo  ============================================
echo.

REM ── Step 1: Build the application ──
echo [1/3] Building FormGenerator (Release)...
echo.

dotnet build "%~dp0FormGenerator\FormGenerator.csproj" -c Release -verbosity:minimal
if %errorlevel% neq 0 (
    echo.
    echo ERROR: Build failed!
    pause
    exit /b 1
)

echo.
echo  Build succeeded.
echo.

REM ── Step 2: Create output directory ──
echo [2/3] Preparing output directory...
if not exist "%~dp0output" mkdir "%~dp0output"
echo  Done.
echo.

REM ── Step 3: Run Inno Setup ──
echo [3/3] Building installer with Inno Setup...
echo.

REM Try common Inno Setup install locations
set ISCC=
if exist "C:\Program Files (x86)\Inno Setup 6\ISCC.exe" (
    set "ISCC=C:\Program Files (x86)\Inno Setup 6\ISCC.exe"
)
if exist "C:\Program Files\Inno Setup 6\ISCC.exe" (
    set "ISCC=C:\Program Files\Inno Setup 6\ISCC.exe"
)

if "!ISCC!"=="" (
    echo WARNING: Inno Setup not found in default locations.
    echo.
    echo Please either:
    echo   1. Install Inno Setup 6 from https://jrsoftware.org/isdl.php
    echo   2. Open Installer\InnoSetup.iss directly in the Inno Setup Compiler
    echo   3. Set ISCC environment variable to point to ISCC.exe
    echo.
    echo The application was built successfully in:
    echo   FormGenerator\bin\Release\net48\
    echo.
    pause
    exit /b 0
)

"!ISCC!" "%~dp0Installer\InnoSetup.iss"

if %errorlevel% neq 0 (
    echo.
    echo ERROR: Installer build failed!
    pause
    exit /b 1
)

echo.
echo ============================================
echo  SUCCESS! Installer created in:
echo  %~dp0output\
echo ============================================
echo.

dir "%~dp0output\NintexFormsGenerator-*-Setup.exe" 2>nul

echo.
pause

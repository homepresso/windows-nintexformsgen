<#
.SYNOPSIS
    Builds the Nintex Forms Generator application and creates an MSI installer.

.DESCRIPTION
    This script:
    1. Cleans previous build artifacts
    2. Builds the FormGenerator project in Release mode
    3. Generates a file list (HarvestedComponents) from the build output
    4. Builds the WiX installer project to produce the MSI

.PARAMETER Version
    The version number for the build (e.g., "1.0.0"). Defaults to "1.0.0".

.PARAMETER SkipBuild
    If set, skips the application build and only rebuilds the MSI.

.EXAMPLE
    .\build-installer.ps1
    .\build-installer.ps1 -Version "1.2.0"
#>

param(
    [string]$Version = "1.0.0",
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"

# ── Paths ──
$SolutionRoot   = $PSScriptRoot
$ProjectDir     = Join-Path $SolutionRoot "FormGenerator"
$InstallerDir   = Join-Path $SolutionRoot "Installer"
$BuildOutputDir = Join-Path $ProjectDir "bin\Release\net48"
$OutputDir      = Join-Path $SolutionRoot "output"

Write-Host ""
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host "  Nintex Forms Generator - Installer Build" -ForegroundColor Cyan
Write-Host "  Version: $Version" -ForegroundColor Cyan
Write-Host "============================================================" -ForegroundColor Cyan
Write-Host ""

# ── Step 0: Prerequisites check ──
Write-Host "[0/4] Checking prerequisites..." -ForegroundColor Yellow

# Check for MSBuild
$msbuild = $null
$vsWhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (Test-Path $vsWhere) {
    $msbuildPath = & $vsWhere -latest -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" | Select-Object -First 1
    if ($msbuildPath) { $msbuild = $msbuildPath }
}

# Fallback: try dotnet msbuild
$useDotnet = $false
if (-not $msbuild) {
    $dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
    if ($dotnet) {
        $useDotnet = $true
        Write-Host "  Using 'dotnet build' (MSBuild via .NET SDK)" -ForegroundColor Gray
    } else {
        Write-Error "Neither MSBuild nor dotnet CLI found. Install Visual Studio Build Tools or .NET SDK."
    }
} else {
    Write-Host "  Found MSBuild: $msbuild" -ForegroundColor Gray
}

# Check for WiX Toolset
$wixInstalled = $false
try {
    $wixCheck = dotnet tool list --global 2>$null | Select-String "wix"
    if ($wixCheck) {
        $wixInstalled = $true
        Write-Host "  Found WiX Toolset (global tool)" -ForegroundColor Gray
    }
} catch {}

if (-not $wixInstalled) {
    Write-Host "  WiX Toolset not found. Installing..." -ForegroundColor Yellow
    dotnet tool install --global wix --version 4.0.5
    Write-Host "  WiX Toolset installed." -ForegroundColor Green
}

Write-Host ""

# ── Step 1: Build the application ──
if (-not $SkipBuild) {
    Write-Host "[1/4] Building FormGenerator (Release)..." -ForegroundColor Yellow

    if ($useDotnet) {
        dotnet build "$ProjectDir\FormGenerator.csproj" `
            -c Release `
            -p:Version=$Version `
            -p:AssemblyVersion="$Version.0" `
            -p:FileVersion="$Version.0" `
            -verbosity:minimal
    } else {
        & $msbuild "$ProjectDir\FormGenerator.csproj" `
            /p:Configuration=Release `
            /p:Version=$Version `
            /p:AssemblyVersion="$Version.0" `
            /p:FileVersion="$Version.0" `
            /verbosity:minimal `
            /restore
    }

    if ($LASTEXITCODE -ne 0) {
        Write-Error "Build failed with exit code $LASTEXITCODE"
    }

    Write-Host "  Build succeeded." -ForegroundColor Green
} else {
    Write-Host "[1/4] Skipping application build (SkipBuild flag set)." -ForegroundColor Gray
}
Write-Host ""

# ── Step 2: Verify build output ──
Write-Host "[2/4] Verifying build output..." -ForegroundColor Yellow

if (-not (Test-Path $BuildOutputDir)) {
    Write-Error "Build output directory not found: $BuildOutputDir"
}

$exePath = Join-Path $BuildOutputDir "NintexFormsGenerator.exe"
if (-not (Test-Path $exePath)) {
    # Check for old name
    $oldExe = Join-Path $BuildOutputDir "FormGenerator.exe"
    if (Test-Path $oldExe) {
        Write-Host "  Note: EXE is named FormGenerator.exe (will update csproj AssemblyName if needed)" -ForegroundColor Yellow
        $exePath = $oldExe
    } else {
        Write-Error "Could not find built EXE in $BuildOutputDir"
    }
}

$fileCount = (Get-ChildItem $BuildOutputDir -File).Count
Write-Host "  Found $fileCount files in build output." -ForegroundColor Gray
Write-Host ""

# ── Step 3: Generate harvested components WXS ──
Write-Host "[3/4] Harvesting DLLs and supporting files..." -ForegroundColor Yellow

$harvestFile = Join-Path $InstallerDir "HarvestedComponents.wxs"

# Collect all files except the main EXE and config (those are in Package.wxs)
$excludeFiles = @("NintexFormsGenerator.exe", "NintexFormsGenerator.exe.config",
                   "FormGenerator.exe", "FormGenerator.exe.config")

$files = Get-ChildItem $BuildOutputDir -File -Recurse | Where-Object {
    $excludeFiles -notcontains $_.Name
}

# Build the WXS fragment
$xmlLines = @()
$xmlLines += '<?xml version="1.0" encoding="UTF-8"?>'
$xmlLines += '<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">'
$xmlLines += '  <Fragment>'
$xmlLines += '    <ComponentGroup Id="HarvestedComponents" Directory="INSTALLFOLDER">'

$counter = 0
foreach ($file in $files) {
    $counter++
    $relativePath = $file.FullName.Substring($BuildOutputDir.Length).TrimStart('\')
    $safeId = "Harvested_$counter"
    $safeFileId = "HarvestedFile_$counter"

    # Generate a deterministic GUID based on the file path
    $guidBytes = [System.Text.Encoding]::UTF8.GetBytes("AndyHayesFormsGen_$relativePath")
    $hash = [System.Security.Cryptography.SHA256]::Create().ComputeHash($guidBytes)
    $guid = [guid]::new(
        [BitConverter]::ToInt32($hash, 0),
        [BitConverter]::ToInt16($hash, 4),
        [BitConverter]::ToInt16($hash, 6),
        $hash[8], $hash[9], $hash[10], $hash[11],
        $hash[12], $hash[13], $hash[14], $hash[15]
    )

    # Handle subdirectories
    $dirPart = [System.IO.Path]::GetDirectoryName($relativePath)
    if ($dirPart) {
        $xmlLines += "      <Component Id=`"$safeId`" Guid=`"$guid`" Subdirectory=`"$dirPart`">"
    } else {
        $xmlLines += "      <Component Id=`"$safeId`" Guid=`"$guid`">"
    }
    $xmlLines += "        <File Id=`"$safeFileId`" Source=`"`$(var.PublishDir)\$relativePath`" KeyPath=`"yes`" />"
    $xmlLines += "      </Component>"
}

$xmlLines += '    </ComponentGroup>'
$xmlLines += '  </Fragment>'
$xmlLines += '</Wix>'

$xmlLines | Out-File -FilePath $harvestFile -Encoding UTF8
Write-Host "  Harvested $counter files into HarvestedComponents.wxs" -ForegroundColor Gray
Write-Host ""

# ── Step 4: Build the MSI ──
Write-Host "[4/4] Building MSI installer..." -ForegroundColor Yellow

# Ensure output directory exists
if (-not (Test-Path $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir | Out-Null
}

Push-Location $InstallerDir
try {
    if ($useDotnet) {
        dotnet build Installer.wixproj `
            -c Release `
            -p:PublishDir="$BuildOutputDir\" `
            -p:OutputPath="$OutputDir\" `
            -verbosity:minimal
    } else {
        & $msbuild Installer.wixproj `
            /p:Configuration=Release `
            /p:PublishDir="$BuildOutputDir\" `
            /p:OutputPath="$OutputDir\" `
            /verbosity:minimal
    }

    if ($LASTEXITCODE -ne 0) {
        Write-Error "MSI build failed with exit code $LASTEXITCODE"
    }
} finally {
    Pop-Location
}

# Find the MSI
$msi = Get-ChildItem $OutputDir -Filter "*.msi" | Sort-Object LastWriteTime -Descending | Select-Object -First 1

if ($msi) {
    $sizeMB = [math]::Round($msi.Length / 1MB, 1)
    Write-Host ""
    Write-Host "============================================================" -ForegroundColor Green
    Write-Host "  SUCCESS! MSI installer created:" -ForegroundColor Green
    Write-Host "  $($msi.FullName)" -ForegroundColor White
    Write-Host "  Size: ${sizeMB} MB" -ForegroundColor Gray
    Write-Host "============================================================" -ForegroundColor Green
} else {
    Write-Host ""
    Write-Host "  MSI build completed. Check output directory:" -ForegroundColor Yellow
    Write-Host "  $OutputDir" -ForegroundColor White
}

Write-Host ""
Write-Host "Done!" -ForegroundColor Cyan

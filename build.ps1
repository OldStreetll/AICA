<#
.SYNOPSIS
    AICA Project Build Script
.DESCRIPTION
    Builds the AICA solution and reports results.
.PARAMETER Configuration
    Build configuration (Debug or Release). Default: Debug
.PARAMETER Clean
    Clean before build
.PARAMETER Restore
    Restore NuGet packages before build
.EXAMPLE
    .\build.ps1
    .\build.ps1 -Configuration Release
    .\build.ps1 -Clean -Restore
#>

param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    
    [switch]$Clean,
    [switch]$Restore
)

$ErrorActionPreference = "Stop"

# Colors
function Write-ColorOutput($ForegroundColor) {
    $fc = $host.UI.RawUI.ForegroundColor
    $host.UI.RawUI.ForegroundColor = $ForegroundColor
    if ($args) { Write-Output $args }
    $host.UI.RawUI.ForegroundColor = $fc
}

function Write-Success($msg) { Write-Host $msg -ForegroundColor Green }
function Write-Info($msg) { Write-Host $msg -ForegroundColor Cyan }
function Write-Warn($msg) { Write-Host $msg -ForegroundColor Yellow }
function Write-Err($msg) { Write-Host $msg -ForegroundColor Red }

# Header
Write-Host ""
Write-Host "========================================" -ForegroundColor Magenta
Write-Host "  AICA - AI Coding Assistant Builder" -ForegroundColor Magenta
Write-Host "========================================" -ForegroundColor Magenta
Write-Host ""

# Find MSBuild
$msbuildPaths = @(
    "${env:ProgramFiles}\Microsoft Visual Studio\2022\Enterprise\MSBuild\Current\Bin\MSBuild.exe",
    "${env:ProgramFiles}\Microsoft Visual Studio\2022\Professional\MSBuild\Current\Bin\MSBuild.exe",
    "${env:ProgramFiles}\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe",
    "${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022\BuildTools\MSBuild\Current\Bin\MSBuild.exe"
)

$msbuild = $null
foreach ($path in $msbuildPaths) {
    if (Test-Path $path) {
        $msbuild = $path
        break
    }
}

# Try wildcard if not found
if (-not $msbuild) {
    $found = Get-ChildItem "${env:ProgramFiles}\Microsoft Visual Studio\2022\*\MSBuild\Current\Bin\MSBuild.exe" -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($found) { $msbuild = $found.FullName }
}

if (-not $msbuild) {
    Write-Err "ERROR: MSBuild not found. Please install Visual Studio 2022."
    exit 1
}

Write-Info "MSBuild: $msbuild"
Write-Info "Configuration: $Configuration"
Write-Host ""

$solutionPath = Join-Path $PSScriptRoot "AICA.sln"
$startTime = Get-Date

# Clean
if ($Clean) {
    Write-Info "[1/3] Cleaning..."
    & $msbuild $solutionPath /t:Clean /p:Configuration=$Configuration /v:minimal /nologo
    if ($LASTEXITCODE -ne 0) {
        Write-Err "Clean failed!"
        exit 1
    }
    Write-Success "Clean completed."
    Write-Host ""
}

# Restore
if ($Restore -or $Clean) {
    Write-Info "[2/3] Restoring NuGet packages..."
    & $msbuild $solutionPath /t:Restore /p:Configuration=$Configuration /v:minimal /nologo
    if ($LASTEXITCODE -ne 0) {
        Write-Err "Restore failed!"
        exit 1
    }
    Write-Success "Restore completed."
    Write-Host ""
}

# Build
$step = if ($Clean -or $Restore) { "[3/3]" } else { "[1/1]" }
Write-Info "$step Building AICA solution..."
Write-Host ""

$buildOutput = & $msbuild $solutionPath /p:Configuration=$Configuration /v:minimal /nologo 2>&1
$buildExitCode = $LASTEXITCODE

# Parse output
$errors = @()
$warnings = @()

foreach ($line in $buildOutput) {
    $lineStr = $line.ToString()
    if ($lineStr -match ": error ") {
        $errors += $lineStr
    }
    elseif ($lineStr -match ": warning ") {
        $warnings += $lineStr
    }
}

$endTime = Get-Date
$duration = $endTime - $startTime

# Results
Write-Host ""
Write-Host "========================================" -ForegroundColor Magenta
Write-Host "  Build Results" -ForegroundColor Magenta
Write-Host "========================================" -ForegroundColor Magenta
Write-Host ""

if ($buildExitCode -eq 0) {
    Write-Success "BUILD SUCCEEDED"
} else {
    Write-Err "BUILD FAILED"
}

Write-Host ""
Write-Info "Duration: $($duration.TotalSeconds.ToString('F2')) seconds"
Write-Info "Warnings: $($warnings.Count)"
Write-Info "Errors:   $($errors.Count)"

# Show errors
if ($errors.Count -gt 0) {
    Write-Host ""
    Write-Err "Errors:"
    foreach ($err in $errors | Select-Object -First 10) {
        Write-Host "  $err" -ForegroundColor Red
    }
    if ($errors.Count -gt 10) {
        Write-Warn "  ... and $($errors.Count - 10) more errors"
    }
}

# Show output paths
if ($buildExitCode -eq 0) {
    Write-Host ""
    Write-Info "Output:"
    
    $vsixPath = Join-Path $PSScriptRoot "src\AICA.VSIX\bin\$Configuration\AICA.vsix"
    if (Test-Path $vsixPath) {
        $vsixSize = (Get-Item $vsixPath).Length / 1KB
        Write-Host "  VSIX: $vsixPath ($($vsixSize.ToString('F1')) KB)" -ForegroundColor Green
    }
    
    $coreDll = Join-Path $PSScriptRoot "src\AICA.Core\bin\$Configuration\netstandard2.0\AICA.Core.dll"
    if (Test-Path $coreDll) {
        Write-Host "  Core: $coreDll" -ForegroundColor Green
    }
}

Write-Host ""

exit $buildExitCode

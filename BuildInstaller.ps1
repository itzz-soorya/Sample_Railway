# Build and Create Installer Script
# This script builds the Railway Booking application in Release mode and creates an installer

param(
    [string]$Configuration = "Release",
    [string]$Version = "1.0.0"
)

$ErrorActionPreference = "Stop"

Write-Host "=====================================" -ForegroundColor Cyan
Write-Host "Railax Build Script" -ForegroundColor Cyan
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host ""

# Set paths
$ProjectDir = $PSScriptRoot
$ProjectFile = Join-Path $ProjectDir "UserModule.csproj"
$IssFile = Join-Path $ProjectDir "RailwayBooking.iss"
$OutputDir = Join-Path $ProjectDir "Output"

# Check if Inno Setup is installed
$InnoSetupPath = "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe"
if (-not (Test-Path $InnoSetupPath)) {
    $InnoSetupPath = "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    if (-not (Test-Path $InnoSetupPath)) {
        Write-Host "ERROR: Inno Setup 6 is not installed!" -ForegroundColor Red
        Write-Host ""
        Write-Host "Please download and install Inno Setup from:" -ForegroundColor Yellow
        Write-Host "https://jrsoftware.org/isdl.php" -ForegroundColor Yellow
        Write-Host ""
        exit 1
    }
}

Write-Host "Inno Setup found at: $InnoSetupPath" -ForegroundColor Green
Write-Host ""

# Step 1: Clean previous builds
Write-Host "[1/4] Cleaning previous builds..." -ForegroundColor Yellow
if (Test-Path (Join-Path $ProjectDir "bin")) {
    Remove-Item -Path (Join-Path $ProjectDir "bin") -Recurse -Force
}
if (Test-Path (Join-Path $ProjectDir "obj")) {
    Remove-Item -Path (Join-Path $ProjectDir "obj") -Recurse -Force
}
if (Test-Path $OutputDir) {
    Remove-Item -Path $OutputDir -Recurse -Force
}
Write-Host "Cleaned successfully" -ForegroundColor Green
Write-Host ""

# Step 2: Restore NuGet packages
Write-Host "[2/4] Restoring NuGet packages..." -ForegroundColor Yellow
dotnet restore $ProjectFile
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Failed to restore NuGet packages!" -ForegroundColor Red
    exit 1
}
Write-Host "Packages restored successfully" -ForegroundColor Green
Write-Host ""

# Step 3: Build and Publish in Release mode
Write-Host "[3/4] Building and publishing in Release mode..." -ForegroundColor Yellow
Write-Host "    Configuration: $Configuration" -ForegroundColor Gray
Write-Host "    Target: win-x64 (Self-contained)" -ForegroundColor Gray
Write-Host ""

$publishPath = Join-Path $ProjectDir "bin\Release\net8.0-windows\win-x64\publish"
dotnet publish $ProjectFile --configuration $Configuration --runtime win-x64 --self-contained true --output $publishPath /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true /p:EnableCompressionInSingleFile=true /p:PublishReadyToRun=true /p:Version=$Version

if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Build failed!" -ForegroundColor Red
    exit 1
}
Write-Host "Build completed successfully" -ForegroundColor Green
Write-Host ""

# Step 4: Create installer with Inno Setup
Write-Host "[4/4] Creating installer with Inno Setup..." -ForegroundColor Yellow
& $InnoSetupPath $IssFile

if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Installer creation failed!" -ForegroundColor Red
    exit 1
}
Write-Host "Installer created successfully" -ForegroundColor Green
Write-Host ""

# Show results
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host "BUILD COMPLETED SUCCESSFULLY!" -ForegroundColor Green
Write-Host "=====================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Published application:" -ForegroundColor Yellow
Write-Host "  Location: bin\Release\net8.0-windows\win-x64\publish\" -ForegroundColor White
Write-Host ""
Write-Host "Installer:" -ForegroundColor Yellow
Get-ChildItem $OutputDir -Filter "*.exe" | ForEach-Object {
    Write-Host "  File: $($_.Name)" -ForegroundColor White
    Write-Host "  Size: $([math]::Round($_.Length / 1MB, 2)) MB" -ForegroundColor White
    Write-Host "  Path: $($_.FullName)" -ForegroundColor White
}
Write-Host ""
Write-Host "You can now distribute the installer to end users!" -ForegroundColor Green
Write-Host ""

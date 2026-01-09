@echo off
echo ========================================
echo Railax Installer Creation
echo ========================================
echo.

REM Check if Inno Setup is installed
set "ISCC=%ProgramFiles(x86)%\Inno Setup 6\ISCC.exe"
if not exist "%ISCC%" set "ISCC=%ProgramFiles%\Inno Setup 6\ISCC.exe"

if not exist "%ISCC%" (
    echo ERROR: Inno Setup 6 is not installed!
    echo.
    echo Please install Inno Setup 6 first:
    echo 1. Download from: https://jrsoftware.org/isdl.php
    echo 2. Install it
    echo 3. Run this script again
    echo.
    echo Opening download page in browser...
    start https://jrsoftware.org/isdl.php
    pause
    exit /b 1
)

echo Building application...
dotnet publish UserModule.csproj --configuration Release --runtime win-x64 --self-contained true --output ".\bin\Release\net8.0-windows\win-x64\publish" /p:PublishSingleFile=true /p:IncludeNativeLibrariesForSelfExtract=true /p:EnableCompressionInSingleFile=true

if errorlevel 1 (
    echo Build failed!
    pause
    exit /b 1
)

echo.
echo Creating installer with Inno Setup...
"%ISCC%" RailwayBooking.iss

if errorlevel 1 (
    echo Installer creation failed!
    pause
    exit /b 1
)

echo.
echo ========================================
echo SUCCESS! Installer created
echo ========================================
echo.
echo Check the Output folder for the setup file.
echo.
start .\Output
pause

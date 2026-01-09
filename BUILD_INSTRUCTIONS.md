# Railax - Build and Installation Instructions

## Quick Start - Executable is Ready!

✅ **Your executable has been built successfully!**

**Location:** `bin\Release\net8.0-windows\win-x64\publish\Railax.exe`

You can:
1. **Run it directly** - Double-click `Railax.exe` to run the application
2. **Copy and distribute** - Copy the entire `publish` folder to another PC and run it

---

## Create Installation Setup (Optional)

To create a professional installer (.exe setup file), follow these steps:

### Step 1: Install Inno Setup
1. Download Inno Setup 6 from: https://jrsoftware.org/isdl.php
2. Install it (use default settings)

### Step 2: Create Installer
Run one of these methods:

**Method A - Using Batch Script (Easiest):**
- Double-click `CreateInstaller.bat`
- It will build the app and create the installer automatically

**Method B - Using PowerShell Script:**
```powershell
.\BuildInstaller.ps1
```

**Method C - Manual:**
```powershell
# 1. Build the application
dotnet publish UserModule.csproj --configuration Release --runtime win-x64 --self-contained true --output ".\bin\Release\net8.0-windows\win-x64\publish" /p:PublishSingleFile=true

# 2. Create installer (after installing Inno Setup)
"C:\Program Files (x86)\Inno Setup 6\ISCC.exe" RailwayBooking.iss
```

### Step 3: Find Your Installer
After successful creation, find the installer in:
- **Location:** `Output\RailaxSetup_1.0.0.exe`

---

## Distribution Options

### Option 1: Standalone Executable (No Installation)
- Share the `bin\Release\net8.0-windows\win-x64\publish` folder
- Users can run `Railax.exe` directly
- **Size:** ~80 MB
- **Pros:** No installation needed, portable
- **Cons:** Larger folder, no Start menu shortcuts

### Option 2: Installer Setup (Recommended)
- Share the `RailaxSetup_1.0.0.exe` file from Output folder
- Users run the installer
- **Size:** ~40-50 MB (compressed)
- **Pros:** 
  - Professional installation
  - Creates Start menu shortcuts
  - Creates desktop icon
  - Easy uninstall
  - Smaller download size
- **Cons:** Requires installation

---

## System Requirements

- **OS:** Windows 10/11 (64-bit)
- **Framework:** Self-contained (no .NET required)
- **RAM:** 4 GB minimum
- **Disk Space:** 100 MB

---

## Files Explained

- `Railax.exe` - Main application executable
- `CreateInstaller.bat` - Easy installer creation script
- `BuildInstaller.ps1` - PowerShell build script
- `RailwayBooking.iss` - Inno Setup configuration
- `bin\Release\net8.0-windows\win-x64\publish\` - Published application folder
- `Output\` - Created installer will be here

---

## Troubleshooting

**Q: Executable won't run**
- Make sure Windows Defender/Antivirus isn't blocking it
- Right-click > Properties > Unblock

**Q: Build fails**
- Run `dotnet clean` first
- Check if .NET 8 SDK is installed

**Q: Installer creation fails**
- Verify Inno Setup 6 is installed
- Check the path in the error message

---

## Version Information

- **App Name:** Railax
- **Version:** 1.0.0
- **Build Date:** January 9, 2026
- **Platform:** Windows x64

---

Need help? Check the logs in the `Logs` folder.

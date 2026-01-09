# PowerShell Script to Create ICO file from PNG
# This converts the Booking.png to app.ico for use as the EXE icon

$sourcePng = ".\assests\Booking.png"
$outputIco = ".\assests\app.ico"

Write-Host "Creating app icon from Booking.png..." -ForegroundColor Cyan

if (-not (Test-Path $sourcePng)) {
    Write-Host "Error: Source PNG not found at $sourcePng" -ForegroundColor Red
    exit 1
}

try {
    # Load System.Drawing assembly
    Add-Type -AssemblyName System.Drawing

    # Load the source image
    $image = [System.Drawing.Image]::FromFile((Resolve-Path $sourcePng))
    
    # Create icon sizes (16x16, 32x32, 48x48, 256x256)
    $sizes = @(16, 32, 48, 256)
    
    # Create a memory stream for the ICO file
    $memoryStream = New-Object System.IO.MemoryStream
    $writer = New-Object System.IO.BinaryWriter($memoryStream)
    
    # ICO file header
    $writer.Write([Int16]0)      # Reserved (must be 0)
    $writer.Write([Int16]1)      # Image type (1 = icon)
    $writer.Write([Int16]$sizes.Count)  # Number of images
    
    # Store image data for later
    $imageDataList = @()
    
    foreach ($size in $sizes) {
        # Create resized bitmap
        $bitmap = New-Object System.Drawing.Bitmap($size, $size)
        $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
        $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
        $graphics.DrawImage($image, 0, 0, $size, $size)
        $graphics.Dispose()
        
        # Convert to PNG in memory
        $pngStream = New-Object System.IO.MemoryStream
        $bitmap.Save($pngStream, [System.Drawing.Imaging.ImageFormat]::Png)
        $pngData = $pngStream.ToArray()
        $pngStream.Dispose()
        $bitmap.Dispose()
        
        # Store for later writing
        $imageDataList += $pngData
        
        # Write directory entry
        if ($size -eq 256) {
            $writer.Write([Byte]0)  # Width (0 means 256)
            $writer.Write([Byte]0)  # Height (0 means 256)
        } else {
            $writer.Write([Byte]$size)   # Width
            $writer.Write([Byte]$size)   # Height
        }
        $writer.Write([Byte]0)       # Color palette (0 for PNG)
        $writer.Write([Byte]0)       # Reserved
        $writer.Write([Int16]1)      # Color planes
        $writer.Write([Int16]32)     # Bits per pixel
        $writer.Write([Int32]$pngData.Length)  # Image data size
        $writer.Write([Int32]0)      # Offset (will be updated)
    }
    
    # Calculate and update offsets, then write image data
    $offset = 6 + ($sizes.Count * 16)  # Header + directory entries
    $memoryStream.Seek(6 + 12, [System.IO.SeekOrigin]::Begin)  # Seek to first offset field
    
    for ($i = 0; $i -lt $imageDataList.Count; $i++) {
        # Update offset
        $currentPos = $memoryStream.Position
        $memoryStream.Seek($currentPos, [System.IO.SeekOrigin]::Begin)
        $writer.Write([Int32]$offset)
        
        # Move to next offset field (skip 16 bytes ahead minus 4 we just wrote)
        if ($i -lt $imageDataList.Count - 1) {
            $memoryStream.Seek($currentPos + 12, [System.IO.SeekOrigin]::Begin)
        }
        
        $offset += $imageDataList[$i].Length
    }
    
    # Write all image data
    $memoryStream.Seek(0, [System.IO.SeekOrigin]::End)
    foreach ($imageData in $imageDataList) {
        $writer.Write($imageData)
    }
    
    # Save to file
    $icoBytes = $memoryStream.ToArray()
    [System.IO.File]::WriteAllBytes((Join-Path (Get-Location) $outputIco), $icoBytes)
    
    # Cleanup
    $writer.Close()
    $memoryStream.Close()
    $image.Dispose()
    
    Write-Host "✅ Successfully created app.ico!" -ForegroundColor Green
    Write-Host "Icon saved to: $outputIco" -ForegroundColor Green
    Write-Host ""
    Write-Host "Next steps:" -ForegroundColor Yellow
    Write-Host "1. Uncomment the SetupIconFile line in RailwayBooking.iss" -ForegroundColor White
    Write-Host "2. Change it to: SetupIconFile=.\assests\app.ico" -ForegroundColor White
    Write-Host "3. Run BuildInstaller.ps1 to create the installer with the new icon" -ForegroundColor White
}
catch {
    Write-Host "Error creating icon: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

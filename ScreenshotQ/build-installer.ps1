# Convert PNG to ICO and build installer
# Run this script from the ScreenshotQ project folder

$projectRoot = $PSScriptRoot
if (-not $projectRoot) { $projectRoot = Get-Location }

Write-Host "Project root: $projectRoot"

# Step 1: Convert PNG to ICO using .NET
$pngPath = Join-Path $projectRoot "dist\exe\Assets\5172910.png"
$icoPath = Join-Path $projectRoot "dist\exe\Assets\5172910.ico"

if (Test-Path $pngPath) {
    Add-Type -AssemblyName System.Drawing
    $bitmap = [System.Drawing.Bitmap]::new($pngPath)
    
    # Resize to 32x32 for icon
    $icon32 = [System.Drawing.Bitmap]::new($bitmap, [System.Drawing.Size]::new(32, 32))
    $icon16 = [System.Drawing.Bitmap]::new($bitmap, [System.Drawing.Size]::new(16, 16))

    # Write ICO file manually (ICO format)
    $ms = [System.IO.MemoryStream]::new()
    $writer = [System.IO.BinaryWriter]::new($ms)

    # ICO header
    $writer.Write([uint16]0)       # Reserved
    $writer.Write([uint16]1)       # Type: ICO
    $writer.Write([uint16]2)       # Count: 2 images (32x32 and 16x16)

    # Image data - write each image to temp streams first
    $imgStream32 = [System.IO.MemoryStream]::new()
    $imgStream16 = [System.IO.MemoryStream]::new()
    $icon32.Save($imgStream32, [System.Drawing.Imaging.ImageFormat]::Png)
    $icon16.Save($imgStream16, [System.Drawing.Imaging.ImageFormat]::Png)
    $imgBytes32 = $imgStream32.ToArray()
    $imgBytes16 = $imgStream16.ToArray()

    # Directory entries (6 bytes header + 2 * 16 bytes = 38 bytes offset)
    $offset = 6 + 2 * 16  # ICONDIR header + 2 ICONDIRENTRY

    # Entry for 32x32
    $writer.Write([byte]32)              # Width
    $writer.Write([byte]32)              # Height
    $writer.Write([byte]0)               # Color count
    $writer.Write([byte]0)               # Reserved
    $writer.Write([uint16]1)             # Planes
    $writer.Write([uint16]32)            # Bit count
    $writer.Write([uint32]$imgBytes32.Length)  # Size
    $writer.Write([uint32]$offset)       # Offset

    # Entry for 16x16
    $offset += $imgBytes32.Length
    $writer.Write([byte]16)
    $writer.Write([byte]16)
    $writer.Write([byte]0)
    $writer.Write([byte]0)
    $writer.Write([uint16]1)
    $writer.Write([uint16]32)
    $writer.Write([uint32]$imgBytes16.Length)
    $writer.Write([uint32]$offset)

    # Image data
    $writer.Write($imgBytes32)
    $writer.Write($imgBytes16)
    $writer.Flush()

    [System.IO.File]::WriteAllBytes($icoPath, $ms.ToArray())

    $bitmap.Dispose(); $icon32.Dispose(); $icon16.Dispose()
    $ms.Dispose(); $imgStream32.Dispose(); $imgStream16.Dispose()
    Write-Host "ICO created: $icoPath"
} else {
    Write-Warning "PNG not found at $pngPath — installer will be built without icon"
    # Remove SetupIconFile from the .iss if no icon
    $issPath = Join-Path $projectRoot "setup.iss"
    (Get-Content $issPath) | Where-Object { $_ -notmatch "SetupIconFile" } | Set-Content $issPath
}

# Step 2: Build installer with Inno Setup
$iscc = Get-Command iscc -ErrorAction SilentlyContinue
if (-not $iscc) {
    $isccPaths = @(
        "C:\Program Files (x86)\Inno Setup 6\iscc.exe",
        "C:\Program Files\Inno Setup 6\iscc.exe",
        "C:\Program Files (x86)\Inno Setup 5\iscc.exe"
    )
    foreach ($p in $isccPaths) {
        if (Test-Path $p) { $iscc = $p; break }
    }
}

if (-not $iscc) {
    Write-Host ""
    Write-Host "Inno Setup not found!" -ForegroundColor Yellow
    Write-Host "Please install from: https://jrsoftware.org/isdl.php" -ForegroundColor Cyan
    Write-Host "Then run: iscc `"$projectRoot\setup.iss`"" -ForegroundColor Green
    exit 1
}

$issPath = Join-Path $projectRoot "setup.iss"
New-Item -ItemType Directory -Force -Path (Join-Path $projectRoot "dist\installer") | Out-Null

Write-Host ""
Write-Host "Building installer..." -ForegroundColor Cyan
& $iscc $issPath

if ($LASTEXITCODE -eq 0) {
    $outFile = Join-Path $projectRoot "dist\installer\ScreenshotQ-Setup.exe"
    $size = (Get-Item $outFile).Length / 1MB
    Write-Host ""
    Write-Host ("Installer built successfully! ({0:F1} MB)" -f $size) -ForegroundColor Green
    Write-Host "Output: $outFile" -ForegroundColor Green
} else {
    Write-Host "Build failed!" -ForegroundColor Red
}

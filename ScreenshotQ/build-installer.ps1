# Convert PNG to ICO and build installer
# Run this script from the ScreenshotQ project folder

$projectRoot = $PSScriptRoot
if (-not $projectRoot) { $projectRoot = Get-Location }

Write-Host "Project root: $projectRoot"

$sourceExeDir = Join-Path $projectRoot "dist\exe"
$sourceIconPath = Join-Path $projectRoot "Assets\5172910.ico"

if (-not (Test-Path $sourceExeDir)) {
    Write-Host "Release executable output not found: $sourceExeDir" -ForegroundColor Yellow
    Write-Host "Run the publish command first:" -ForegroundColor Yellow
    Write-Host "dotnet publish ScreenshotQ.csproj -c Release -f net8.0-windows7.0 -r win-x64 --self-contained false -o dist\exe" -ForegroundColor Cyan
    exit 1
}

if (-not (Test-Path $sourceIconPath)) {
    Write-Host "Application icon not found: $sourceIconPath" -ForegroundColor Yellow
    exit 1
}

# Step 1: Build installer with Inno Setup
$iscc = Get-Command iscc -ErrorAction SilentlyContinue
if (-not $iscc) {
    $isccPaths = @(
        "C:\Program Files (x86)\Inno Setup 6\iscc.exe",
        "C:\Program Files\Inno Setup 6\iscc.exe",
        "C:\Program Files (x86)\Inno Setup 5\iscc.exe",
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe")
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

param(
    [ValidateSet("patch", "minor", "major")]
    [string]$Bump = "patch",

    [string]$Runtime = "win-x64",

    [switch]$FrameworkDependent,

    [switch]$SkipInstaller
)

$ErrorActionPreference = "Stop"

function Get-IsccPath {
    $iscc = Get-Command iscc -ErrorAction SilentlyContinue
    if ($iscc) {
        return $iscc.Source
    }

    $candidatePaths = @(
        "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
        "C:\Program Files\Inno Setup 6\ISCC.exe",
        "C:\Program Files (x86)\Inno Setup 5\ISCC.exe",
        (Join-Path $env:LOCALAPPDATA "Programs\Inno Setup 6\ISCC.exe")
    )

    foreach ($path in $candidatePaths) {
        if (Test-Path $path) {
            return $path
        }
    }

    return $null
}

function Get-BumpedVersion {
    param(
        [string]$CurrentVersion,
        [string]$BumpType
    )

    if ($CurrentVersion -notmatch '^(\d+)\.(\d+)\.(\d+)$') {
        throw "Version '$CurrentVersion' is not in SemVer format x.y.z"
    }

    $major = [int]$Matches[1]
    $minor = [int]$Matches[2]
    $patch = [int]$Matches[3]

    switch ($BumpType) {
        "major" {
            $major += 1
            $minor = 0
            $patch = 0
        }
        "minor" {
            $minor += 1
            $patch = 0
        }
        default {
            $patch += 1
        }
    }

    return "$major.$minor.$patch"
}

function Set-Or-InsertVersionTag {
    param(
        [string]$Content,
        [string]$TagName,
        [string]$VersionValue
    )

    $pattern = "<${TagName}>[^<]+</${TagName}>"
    if ($Content -match $pattern) {
        return [regex]::Replace($Content, $pattern, "<${TagName}>$VersionValue</${TagName}>", 1)
    }

    $insertion = "    <${TagName}>$VersionValue</${TagName}>"
    return [regex]::Replace($Content, "</PropertyGroup>", "$insertion`r`n  </PropertyGroup>", 1)
}

$projectRoot = $PSScriptRoot
if (-not $projectRoot) {
    $projectRoot = (Get-Location).Path
}

$csprojPath = Join-Path $projectRoot "ScreenshotQ.csproj"
$setupIssPath = Join-Path $projectRoot "setup.iss"

if (-not (Test-Path $csprojPath)) {
    throw "Could not find project file at $csprojPath"
}

$distDir = Join-Path $projectRoot "dist"
$portableDir = Join-Path $distDir "portable"
$exeDir = Join-Path $distDir "exe"
$installerDir = Join-Path $distDir "installer"

New-Item -ItemType Directory -Path $distDir -Force | Out-Null

Write-Host "Project root: $projectRoot"
Write-Host "Bump type: $Bump"
Write-Host "Runtime: $Runtime"

$isSelfContained = -not $FrameworkDependent
Write-Host "Self-contained: $isSelfContained"

$csprojContent = Get-Content -Path $csprojPath -Raw

$currentVersion = "1.0.0"
if ($csprojContent -match '<Version>([^<]+)</Version>') {
    $currentVersion = $Matches[1]
}

$newVersion = Get-BumpedVersion -CurrentVersion $currentVersion -BumpType $Bump

$updatedContent = $csprojContent
$updatedContent = Set-Or-InsertVersionTag -Content $updatedContent -TagName "Version" -VersionValue $newVersion
$updatedContent = Set-Or-InsertVersionTag -Content $updatedContent -TagName "AssemblyVersion" -VersionValue $newVersion
$updatedContent = Set-Or-InsertVersionTag -Content $updatedContent -TagName "FileVersion" -VersionValue $newVersion

Set-Content -Path $csprojPath -Value $updatedContent -Encoding UTF8

Write-Host "Version updated: $currentVersion -> $newVersion" -ForegroundColor Green

if (Test-Path $portableDir) { Remove-Item $portableDir -Recurse -Force }
if (Test-Path $exeDir) { Remove-Item $exeDir -Recurse -Force }

Write-Host ""
Write-Host "Restoring dependencies..." -ForegroundColor Cyan
dotnet restore $csprojPath -r $Runtime
if ($LASTEXITCODE -ne 0) {
    throw "Restore failed"
}

Write-Host ""
Write-Host "Publishing portable build..." -ForegroundColor Cyan
dotnet publish $csprojPath -c Release -f net8.0-windows7.0 -r $Runtime --self-contained $isSelfContained -p:PublishSingleFile=true -o $portableDir
if ($LASTEXITCODE -ne 0) {
    throw "Portable publish failed"
}

$zipName = "ScreenshotQ-portable-v$newVersion.zip"
$zipPath = Join-Path $distDir $zipName
if (Test-Path $zipPath) { Remove-Item $zipPath -Force }

Write-Host "Creating portable zip: $zipName" -ForegroundColor Cyan
Compress-Archive -Path (Join-Path $portableDir "*") -DestinationPath $zipPath -CompressionLevel Optimal

Write-Host ""
Write-Host "Publishing installer source build..." -ForegroundColor Cyan
dotnet publish $csprojPath -c Release -f net8.0-windows7.0 -r $Runtime --self-contained $isSelfContained -p:PublishSingleFile=true -o $exeDir
if ($LASTEXITCODE -ne 0) {
    throw "Installer source publish failed"
}

if (-not $SkipInstaller) {
    if (-not (Test-Path $setupIssPath)) {
        throw "setup.iss not found at $setupIssPath"
    }

    $isccPath = Get-IsccPath
    if (-not $isccPath) {
        throw "Inno Setup (ISCC.exe) not found. Install Inno Setup 6 or run with -SkipInstaller."
    }

    New-Item -ItemType Directory -Path $installerDir -Force | Out-Null

    Write-Host ""
    Write-Host "Building installer exe..." -ForegroundColor Cyan
    & $isccPath "/DAppVersion=$newVersion" $setupIssPath
    if ($LASTEXITCODE -ne 0) {
        throw "Installer build failed"
    }
}

Write-Host ""
Write-Host "Build completed successfully" -ForegroundColor Green
Write-Host "Version: $newVersion"
Write-Host "Portable folder: $portableDir"
Write-Host "Portable zip: $zipPath"
if (-not $SkipInstaller) {
    Write-Host "Installer folder: $installerDir"
}

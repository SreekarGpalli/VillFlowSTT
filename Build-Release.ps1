# Build-Release.ps1
# Publishes VillFlow.App and produces .exe + .msi in the dist folder.
$ErrorActionPreference = "Stop"
$ProjectRoot = $PSScriptRoot

# Create dist folder
$DistDir = Join-Path $ProjectRoot "dist"
if (-not (Test-Path $DistDir)) {
    New-Item -ItemType Directory -Path $DistDir | Out-Null
}

# 1. Publish VillFlow.App (single-file, self-contained)
$PublishDir = Join-Path $ProjectRoot "publish"
Write-Host "Publishing VillFlow.App to $PublishDir ..."
dotnet publish "$ProjectRoot\VillFlow.App\VillFlow.App.csproj" `
    -c Release `
    -o $PublishDir `
    -p:PublishSingleFile=true `
    -p:SelfContained=true `
    -r win-x64

if (-not (Test-Path "$PublishDir\VillFlow.App.exe")) {
    throw "Publish failed: VillFlow.App.exe not found in $PublishDir"
}

# Copy exe to dist
Copy-Item "$PublishDir\VillFlow.App.exe" -Destination "$DistDir\VillFlow.App.exe" -Force
Write-Host "Copied VillFlow.App.exe to $DistDir"

# 2. Build WiX installer
Write-Host "Building MSI installer ..."
$InstallerProj = Join-Path $ProjectRoot "VillFlow.Installer\VillFlow.Installer.wixproj"
dotnet build $InstallerProj -c Release

$MsiPath = Join-Path $ProjectRoot "VillFlow.Installer\bin\Release\VillFlow.msi"
if (-not (Test-Path $MsiPath)) {
    # Try alternative output path
    $MsiAlt = Get-ChildItem -Path (Join-Path $ProjectRoot "VillFlow.Installer") -Recurse -Filter "VillFlow.msi" -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($MsiAlt) {
        $MsiPath = $MsiAlt.FullName
    } else {
        throw "MSI build failed: VillFlow.msi not found"
    }
}

Copy-Item $MsiPath -Destination "$DistDir\VillFlow.msi" -Force
Write-Host "Copied VillFlow.msi to $DistDir"

Write-Host ""
Write-Host "Done. Release artifacts in $DistDir :"
Get-ChildItem $DistDir | ForEach-Object { Write-Host "  - $($_.Name) ($([math]::Round($_.Length/1MB, 2)) MB)" }

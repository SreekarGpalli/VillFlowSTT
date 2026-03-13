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

# 2. Build Inno Setup Installer (.exe)
# Check multiple common install locations for Inno Setup 6
$InnoSetupCompiler = $null
$InnoSearchPaths = @(
    "C:\Program Files (x86)\Inno Setup 6\ISCC.exe",
    "C:\Program Files\Inno Setup 6\ISCC.exe",
    "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
)
foreach ($path in $InnoSearchPaths) {
    if (Test-Path $path) {
        $InnoSetupCompiler = $path
        break
    }
}
# Also check PATH
if (-not $InnoSetupCompiler) {
    $fromPath = Get-Command iscc.exe -ErrorAction SilentlyContinue
    if ($fromPath) { $InnoSetupCompiler = $fromPath.Source }
}
if ($InnoSetupCompiler) {
    Write-Host "Building EXE installer with Inno Setup ..."
    $IssScript = Join-Path $ProjectRoot "Installer\VillFlowSetup.iss"
    
    # Run ISCC, wait for completion
    $process = Start-Process -FilePath $InnoSetupCompiler -ArgumentList "`"$IssScript`"" -Wait -PassThru
    
    if ($process.ExitCode -eq 0) {
        # The .iss file puts its Output in Installer\Output
        $OutputExe = Join-Path $ProjectRoot "Installer\Output\VillFlowSetup-1.0.0.exe"
        if (Test-Path $OutputExe) {
            Copy-Item $OutputExe -Destination "$DistDir\VillFlowSetup-1.0.0.exe" -Force
            Write-Host "Copied VillFlowSetup-1.0.0.exe to $DistDir"
        } else {
            Write-Warning "Inno Setup output not found at: $OutputExe"
        }
    } else {
        Write-Warning "Inno Setup compiler returned exit code $($process.ExitCode)"
    }
} else {
    Write-Warning "Inno Setup 6 is not installed at '$InnoSetupCompiler'. Skipping .exe installer build."
}

# 3. Build WiX installer (.msi)
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

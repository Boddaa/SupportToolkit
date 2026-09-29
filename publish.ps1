param(
    [string]$OutputPath = "$PSScriptRoot\publish"
)

Write-Host "========================================================" -ForegroundColor Cyan
Write-Host "       SupportToolKit - Clean & Full Publish Pipeline   " -ForegroundColor Cyan
Write-Host "========================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Target: Portable Self-Contained Windows x64" -ForegroundColor Yellow
Write-Host "Output: $OutputPath" -ForegroundColor Yellow
Write-Host ""

# Stop any running instances before cleaning/building
Stop-Process -Name "SupportToolKit", "NetworkDiscoveryTool.UI", "TelegramConfigTool" -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 600

# 1. Check if dotnet is available
if (!(Get-Command "dotnet" -ErrorAction SilentlyContinue)) {
    Write-Host "ERROR: .NET SDK not found. Install from https://dotnet.microsoft.com/download" -ForegroundColor Red
    exit 1
}

# 2. Clean previous build artifacts and databases from publish directory
Write-Host "[1/5] Cleaning publish directory..." -ForegroundColor Green
if (Test-Path $OutputPath) {
    Get-ChildItem -Path $OutputPath -Filter "*.db*" | Remove-Item -Force -ErrorAction SilentlyContinue
    Get-ChildItem -Path $OutputPath -Filter "*.log*" | Remove-Item -Force -ErrorAction SilentlyContinue
    Get-ChildItem -Path $OutputPath -Filter "*.zip" | Remove-Item -Force -ErrorAction SilentlyContinue
    Get-ChildItem -Path $OutputPath -Filter "*.pdb" | Remove-Item -Force -ErrorAction SilentlyContinue

    if (Test-Path (Join-Path $OutputPath "logs")) {
        Remove-Item (Join-Path $OutputPath "logs") -Recurse -Force -ErrorAction SilentlyContinue
    }
    if (Test-Path (Join-Path $OutputPath "Exports")) {
        Remove-Item (Join-Path $OutputPath "Exports") -Recurse -Force -ErrorAction SilentlyContinue
    }
} else {
    New-Item -ItemType Directory -Path $OutputPath -Force | Out-Null
}

# 3. Restore all packages
Write-Host "[2/5] Restoring project dependencies..." -ForegroundColor Green
dotnet restore NetworkDiscoveryTool.slnx
if ($LASTEXITCODE -ne 0) { Write-Host "Restore failed!" -ForegroundColor Red; exit 1 }

# 4. Publish Main Application (SupportToolKit.exe)
Write-Host "[3/5] Publishing SupportToolKit (Release, win-x64, single-file)..." -ForegroundColor Green
dotnet publish NetworkDiscoveryTool.UI\NetworkDiscoveryTool.UI.csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none `
    -o "$OutputPath"
if ($LASTEXITCODE -ne 0) { Write-Host "Publish failed!" -ForegroundColor Red; exit 1 }

# 5. Publish Admin Tool (TelegramConfigTool.exe)
Write-Host "[4/5] Publishing TelegramConfigTool (Admin tool)..." -ForegroundColor Green
$adminToolsDir = Join-Path $OutputPath "AdminTools"
dotnet publish NetworkDiscoveryTool.ConfigTool\NetworkDiscoveryTool.ConfigTool.csproj `
    -c Release `
    -r win-x64 `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none `
    -o "$adminToolsDir"
if ($LASTEXITCODE -ne 0) { Write-Host "Admin Tool Publish warning" -ForegroundColor Yellow }

# 6. Copy support files
$uiEnc = Join-Path $PSScriptRoot "NetworkDiscoveryTool.UI\telegram.enc"
if (Test-Path $uiEnc) {
    Copy-Item $uiEnc -Destination (Join-Path $OutputPath "telegram.enc") -Force
}

$installerBat = Join-Path $PSScriptRoot "installer\Install_SupportToolKit.bat"
if (Test-Path $installerBat) {
    Copy-Item $installerBat -Destination (Join-Path $OutputPath "Install_SupportToolKit.bat") -Force
}

$innoIss = Join-Path $PSScriptRoot "installer\SupportToolKit_InnoSetup.iss"
if (Test-Path $innoIss) {
    Copy-Item $innoIss -Destination (Join-Path $OutputPath "SupportToolKit_InnoSetup.iss") -Force
}

# Final sweep of runtime files
Stop-Process -Name "SupportToolKit", "NetworkDiscoveryTool.UI" -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 400
Get-ChildItem -Path $OutputPath -Filter "*.db*" | Remove-Item -Force -ErrorAction SilentlyContinue
Get-ChildItem -Path $OutputPath -Filter "*.log*" | Remove-Item -Force -ErrorAction SilentlyContinue
if (Test-Path (Join-Path $OutputPath "logs")) {
    Remove-Item (Join-Path $OutputPath "logs") -Recurse -Force -ErrorAction SilentlyContinue
}
if (Test-Path (Join-Path $OutputPath "Exports")) {
    Remove-Item (Join-Path $OutputPath "Exports") -Recurse -Force -ErrorAction SilentlyContinue
}

# 7. Create distribution ZIP packages
Write-Host "[5/5] Packaging clean distribution ZIP..." -ForegroundColor Green
$zipFile1 = Join-Path $OutputPath "SupportToolKit.zip"
$zipFile2 = Join-Path $OutputPath "SupportToolKit_v2.5_Portable.zip"

$itemsToZip = Get-ChildItem -Path $OutputPath | Where-Object { 
    $_.Name -ne "logs" -and 
    $_.Name -ne "Exports" -and 
    $_.Extension -ne ".zip" -and 
    $_.Extension -ne ".iss" -and 
    $_.Extension -ne ".pdb" -and
    $_.Extension -ne ".db" -and
    $_.Extension -ne ".wal" -and
    $_.Extension -ne ".shm" -and
    $_.Extension -ne ".log"
}

Compress-Archive -Path $itemsToZip.FullName -DestinationPath $zipFile1 -Force
Copy-Item $zipFile1 $zipFile2 -Force

Write-Host ""
Write-Host "========================================================" -ForegroundColor Cyan
Write-Host "[OK] PUBLISH COMPLETED SUCCESSFULLY!" -ForegroundColor Green
Write-Host "========================================================" -ForegroundColor Cyan
Write-Host "Publish Folder: $OutputPath" -ForegroundColor Yellow
Write-Host ""
Write-Host "Files in publish directory:" -ForegroundColor Yellow
Get-ChildItem "$OutputPath" | Select-Object Name, Length, LastWriteTime | Format-Table -AutoSize

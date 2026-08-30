param(
    [string]$OutputPath = "$PSScriptRoot\publish"
)

Write-Host "=== SupportToolKit - Publish ===" -ForegroundColor Cyan
Write-Host ""
Write-Host "This will build a portable self-contained .exe" -ForegroundColor Yellow
Write-Host "No .NET runtime required on target machine." -ForegroundColor Yellow
Write-Host "Output: $OutputPath" -ForegroundColor Yellow
Write-Host ""

# Stop any running instances before building
Stop-Process -Name "SupportToolKit", "NetworkDiscoveryTool.UI" -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 600

# Check if dotnet is available
if (!(Get-Command "dotnet" -ErrorAction SilentlyContinue)) {
    Write-Host "ERROR: .NET SDK not found. Install from https://dotnet.microsoft.com/download" -ForegroundColor Red
    exit 1
}

# Restore packages
Write-Host "[1/3] Restoring packages..." -ForegroundColor Green
dotnet restore NetworkDiscoveryTool.UI\NetworkDiscoveryTool.UI.csproj
if ($LASTEXITCODE -ne 0) { Write-Host "Restore failed!" -ForegroundColor Red; exit 1 }

# Publish self-contained
Write-Host "[2/3] Publishing (Release, self-contained, single-file)..." -ForegroundColor Green
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

# Clean logs, exports, and old database from publish output for a pristine first-run experience
Stop-Process -Name "SupportToolKit", "NetworkDiscoveryTool.UI" -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 500

if (Test-Path (Join-Path $OutputPath "logs")) {
    Remove-Item (Join-Path $OutputPath "logs") -Recurse -Force -ErrorAction SilentlyContinue
}
if (Test-Path (Join-Path $OutputPath "Exports")) {
    Remove-Item (Join-Path $OutputPath "Exports") -Recurse -Force -ErrorAction SilentlyContinue
}
Get-ChildItem -Path $OutputPath -Filter "*.db*" | Remove-Item -Force -ErrorAction SilentlyContinue
Get-ChildItem -Path $OutputPath -Filter "*.log" | Remove-Item -Force -ErrorAction SilentlyContinue

Write-Host "[3/3] Creating clean distribution ZIP package..." -ForegroundColor Green
$zipFile = Join-Path $OutputPath "SupportToolKit_v2.5_Portable.zip"
if (Test-Path $zipFile) { Remove-Item $zipFile -Force }

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

Compress-Archive -Path $itemsToZip.FullName -DestinationPath $zipFile -Force

Write-Host ""
Write-Host "=== DONE ===" -ForegroundColor Cyan
Write-Host "SupportToolKit published to: $OutputPath" -ForegroundColor Green
Write-Host ""
Write-Host "Files in publish directory:" -ForegroundColor Yellow
Get-ChildItem "$OutputPath" -Name

Write-Host ""
Write-Host "Copy the '$OutputPath\SupportToolKit_v2.5_Portable.zip' to any Windows x64 machine, extract and run SupportToolKit.exe" -ForegroundColor White

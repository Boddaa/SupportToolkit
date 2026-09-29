@echo off
setlocal
title SupportToolKit - One-Click Installer
echo ========================================================
echo        SupportToolKit - Installation Setup
echo ========================================================
echo.

set "TARGET_DIR=%LOCALAPPDATA%\Programs\SupportToolKit"
set "SRC_DIR=%~dp0"

echo [1/3] Creating application directory...
if not exist "%TARGET_DIR%" mkdir "%TARGET_DIR%"

echo [2/3] Copying application files...
if exist "%SRC_DIR%SupportToolKit.exe" (
    copy /Y "%SRC_DIR%SupportToolKit.exe" "%TARGET_DIR%\SupportToolKit.exe" >nul
) else (
    copy /Y "%SRC_DIR%NetworkDiscoveryTool.UI.exe" "%TARGET_DIR%\SupportToolKit.exe" >nul
)

if exist "%SRC_DIR%telegram.enc" (
    copy /Y "%SRC_DIR%telegram.enc" "%TARGET_DIR%\telegram.enc" >nul
)

if exist "%SRC_DIR%appsettings.json" (
    copy /Y "%SRC_DIR%appsettings.json" "%TARGET_DIR%\appsettings.json" >nul
)

echo [3/3] Creating Desktop and Start Menu Shortcuts...
powershell -NoProfile -ExecutionPolicy Bypass -Command "$ws = New-Object -ComObject WScript.Shell; $d = [Environment]::GetFolderPath('Desktop'); $s1 = $ws.CreateShortcut($d + '\SupportToolKit.lnk'); $s1.TargetPath = $env:LOCALAPPDATA + '\Programs\SupportToolKit\SupportToolKit.exe'; $s1.WorkingDirectory = $env:LOCALAPPDATA + '\Programs\SupportToolKit'; $s1.Save(); $sm = [Environment]::GetFolderPath('Programs'); $smDir = $sm + '\SupportToolKit'; if (!(Test-Path $smDir)) { New-Item -ItemType Directory -Path $smDir | Out-Null }; $s2 = $ws.CreateShortcut($smDir + '\SupportToolKit.lnk'); $s2.TargetPath = $env:LOCALAPPDATA + '\Programs\SupportToolKit\SupportToolKit.exe'; $s2.WorkingDirectory = $env:LOCALAPPDATA + '\Programs\SupportToolKit'; $s2.Save()"

echo.
echo ========================================================
echo   Installation Completed Successfully!
echo   Shortcut created on your Desktop: 'SupportToolKit'
echo ========================================================
echo.
pause

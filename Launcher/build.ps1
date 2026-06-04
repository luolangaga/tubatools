$ErrorActionPreference = 'Stop'

$Arch = if ($args[0]) { $args[0] } else { 'x64' }
$LauncherDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$ProjectDir = Split-Path -Parent $LauncherDir

$ZigExe = $null
$wingetLink = Join-Path $env:LOCALAPPDATA 'Microsoft\WinGet\Links\zig.exe'
if (Test-Path -LiteralPath $wingetLink) { $ZigExe = $wingetLink }
elseif (Get-Command zig -ErrorAction SilentlyContinue) { $ZigExe = 'zig' }
else { Write-Host 'ERROR: zig not found. Install via: winget install zig.zig' -ForegroundColor Red; exit 1 }

$Target = switch ($Arch) {
    'x64'   { 'x86_64-windows-gnu' }
    'x86'   { 'x86-windows-gnu' }
    'arm64' { 'aarch64-windows-gnu' }
    default { Write-Host "Unknown arch: $Arch"; exit 1 }
}

$rcContent = @"
#include <windows.h>

IDI_APP ICON "..\\Assets\\AppIcon.ico"

VS_VERSION_INFO VERSIONINFO
FILEVERSION 1,0,0,0
PRODUCTVERSION 1,0,0,0
FILEFLAGSMASK 0x3fL
FILEFLAGS 0
FILEOS VOS_NT_WINDOWS32
FILETYPE VFT_APP
FILESUBTYPE VFT2_UNKNOWN
BEGIN
    BLOCK "StringFileInfo"
    BEGIN
        BLOCK "080404B0"
        BEGIN
            VALUE "CompanyName", "TubaWinUi3"
            VALUE "FileDescription", "\u56FE\u5427\u5DE5\u5177\u7BB1WinUI3"
            VALUE "FileVersion", "1.0.0.0"
            VALUE "InternalName", "TubaWinUi3"
            VALUE "OriginalFilename", "\u56FE\u5427\u5DE5\u5177\u7BB1WinUI3.exe"
            VALUE "ProductName", "\u56FE\u5427\u5DE5\u5177\u7BB1WinUI3"
            VALUE "ProductVersion", "1.0.0.0"
        END
    END
    BLOCK "VarFileInfo"
    BEGIN
        VALUE "Translation", 0x0804, 1200
    END
END
"@

$rcFile = Join-Path $LauncherDir 'launcher.rc'
[System.IO.File]::WriteAllText($rcFile, $rcContent, [System.Text.UTF8Encoding]::new($true))

$Vcvarsall = Get-ChildItem -LiteralPath 'C:\Program Files\Microsoft Visual Studio' -Recurse -Filter 'vcvarsall.bat' -ErrorAction SilentlyContinue |
    Sort-Object FullName -Descending | Select-Object -First 1

$resFile = $null
if ($Vcvarsall) {
    $rcScript = @"
@echo off
call "$($Vcvarsall.FullName)" x64 >nul 2>&1
cd /d "$LauncherDir"
rc /nologo launcher.rc
if errorlevel 1 exit /b 1
"@
    $rcBat = Join-Path $env:TEMP 'build_launcher_rc.bat'
    Set-Content -LiteralPath $rcBat -Value $rcScript -Encoding ASCII
    Write-Host "Compiling resources (MSVC rc.exe)..." -ForegroundColor Cyan
    cmd /c $rcBat
    if ($LASTEXITCODE -eq 0) {
        $resFile = Join-Path $LauncherDir 'launcher.res'
    } else {
        Write-Host 'WARNING: rc.exe failed, building without icon/resources' -ForegroundColor Yellow
    }
}

$binDir = Join-Path $LauncherDir 'bin'
if (-not (Test-Path -LiteralPath $binDir)) { New-Item -ItemType Directory -Path $binDir -Force | Out-Null }

$outExe = Join-Path $binDir "图吧工具箱WinUI3_$Arch.exe"

$zigArgs = @(
    'cc',
    '-target', $Target,
    '-Wl,--subsystem,windows',
    '-municode',
    '-O2',
    '-D_UNICODE', '-DUNICODE', '-DNDEBUG',
    '-lshlwapi', '-luser32', '-lshell32',
    (Join-Path $LauncherDir 'launcher.c')
)
if ($resFile) { $zigArgs += $resFile }
$zigArgs += '-o', $outExe

Write-Host "Compiling launcher for $Arch ($Target)..." -ForegroundColor Cyan
& $ZigExe $zigArgs
if ($LASTEXITCODE -ne 0) { Write-Host 'ERROR: Build failed' -ForegroundColor Red; exit 1 }

Remove-Item -LiteralPath (Join-Path $LauncherDir 'launcher.res') -Force -ErrorAction SilentlyContinue
Remove-Item -LiteralPath (Join-Path $binDir "*.pdb") -Force -ErrorAction SilentlyContinue

$size = [math]::Round((Get-Item -LiteralPath $outExe).Length / 1KB, 1)
Write-Host "OK: $outExe ($size KB)" -ForegroundColor Green

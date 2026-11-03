$ErrorActionPreference = 'Stop'

$RepoRoot     = 'C:\Users\luolan\Desktop\tubawinui3'
$ProjectDir   = Join-Path $RepoRoot 'TubaWinUi3.WinUI3'
$ISCC        = 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
$Version     = '1.0.2.0'
$VersionShort = '1.0.2'

function Remove-UnnecessaryFiles {
    param([string]$Root)

    Get-ChildItem -LiteralPath $Root -Filter '*.pdb' -Recurse -Force -ErrorAction SilentlyContinue |
        Remove-Item -Force -ErrorAction SilentlyContinue

    $tools = Join-Path $Root 'Tools'
    if (-not (Test-Path -LiteralPath $tools)) { return }

    $removeNames = @(
        'Dism++x86.exe', 'Dism++ARM64.exe',
        'Speccy.exe', 'HWMonitor_x32.exe', 'hwmonitor_arm64.exe', 'cpuz_x32.exe',
        'HWiNFO32.exe', 'Core Temp x86.exe', 'DiskInfo32S.exe',
        'procexp.exe', 'Ventoy2Disk_ARM.exe', 'Ventoy2Disk_ARM64.exe',
        'VentoyPlugson_X64.exe', 'Rw.ini.bak',
        'Display Driver Uninstaller.pdb'
    )
    Get-ChildItem -LiteralPath $tools -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -in $removeNames } |
        Remove-Item -Force -ErrorAction SilentlyContinue

    $themeRemove = @('ShizukuBackground-300.png', 'Background-300.png', 'ShizukuVoice.dll')
    Get-ChildItem -LiteralPath $tools -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -in $themeRemove } |
        Remove-Item -Force -ErrorAction SilentlyContinue

    Get-ChildItem -LiteralPath $tools -Recurse -Directory -Filter 'x86' -ErrorAction SilentlyContinue |
        Where-Object { $_.Parent.Name -eq 'Config' -and $_.Parent.Parent.Name -like '*Dism*' } |
        Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
    Get-ChildItem -LiteralPath $tools -Recurse -Directory -Filter 'arm64' -ErrorAction SilentlyContinue |
        Where-Object { $_.Parent.Name -eq 'Config' -and $_.Parent.Parent.Name -like '*Dism*' } |
        Remove-Item -Recurse -Force -ErrorAction SilentlyContinue
    Get-ChildItem -LiteralPath $tools -Recurse -Directory -Filter '32-bit' -ErrorAction SilentlyContinue |
        Where-Object { $_.Parent.Name -eq 'LinX' } |
        Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

    Get-ChildItem -LiteralPath $tools -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Directory.Name -eq 'Languages' -and $_.Directory.Parent.Name -like '*DDU*' -and $_.Name -notlike '*Chinese*' -and $_.Name -ne 'English.xml' } |
        Remove-Item -Force -ErrorAction SilentlyContinue
    # Dism++ Config ZIPs are runtime resources, including UI and language packs.
    # Keep them intact; they are not leftover download archives.
    Get-ChildItem -LiteralPath $tools -Recurse -File -Filter '*.x86.dll' -ErrorAction SilentlyContinue |
        Where-Object { $_.Directory.Parent.Name -like '*Plugin*' } |
        Remove-Item -Force -ErrorAction SilentlyContinue
    Get-ChildItem -LiteralPath $tools -Recurse -File -Filter '*.arm64.dll' -ErrorAction SilentlyContinue |
        Where-Object { $_.Directory.Parent.Name -like '*Plugin*' } |
        Remove-Item -Force -ErrorAction SilentlyContinue
}

function Publish-Arch {
    param([string]$Arch)

    Write-Host ''
    Write-Host '========================================' -ForegroundColor Cyan
    Write-Host "  Publishing win-$Arch" -ForegroundColor Cyan
    Write-Host '========================================' -ForegroundColor Cyan

    $outDir = Join-Path $ProjectDir "publish_$Arch"

    if (Test-Path -LiteralPath $outDir) { Remove-Item -LiteralPath $outDir -Recurse -Force }

    dotnet publish $ProjectDir -c Release -r "win-$Arch" --self-contained true -p:Platform=$Arch -p:PublishTrimmed=false -p:PublishReadyToRun=false -p:WindowsAppSDKSelfContained=true -p:WindowsPackageType=None -o $outDir 2>&1 | Select-Object -Last 3

    if (-not (Test-Path -LiteralPath (Join-Path $outDir 'TubaWinUi3.exe'))) {
        Write-Host "  ERROR: Publish failed for $Arch" -ForegroundColor Red
        return $false
    }

    $priFile = Join-Path $ProjectDir "bin\$Arch\Release\net10.0-windows10.0.26100.0\win-$Arch\TubaWinUi3.pri"
    if (Test-Path -LiteralPath $priFile) {
        Copy-Item -LiteralPath $priFile -Destination $outDir -Force
        Write-Host '  Restored TubaWinUi3.pri' -ForegroundColor Green
    }

    Write-Host '  Removing unnecessary files...' -ForegroundColor Yellow
    Remove-UnnecessaryFiles $outDir

    Remove-Item -LiteralPath (Join-Path $outDir 'TubaWinUi3.pdb') -Force -ErrorAction SilentlyContinue

    New-Item -ItemType File -Path (Join-Path $outDir '.installed') -Force | Out-Null

    $files = Get-ChildItem -LiteralPath $outDir -Recurse -File
    $totalSize = [math]::Round(($files | Measure-Object -Property Length -Sum).Sum / 1MB, 1)
    Write-Host "  Done: $($files.Count) files, $totalSize MB" -ForegroundColor Green
    return $true
}

Write-Host 'TubaWinUi3 Setup Builder' -ForegroundColor Magenta
Write-Host "Version: $Version" -ForegroundColor Magenta
Write-Host ''

$ok = Publish-Arch 'x64'
if (-not $ok) {
    Write-Host 'FAILED to publish x64' -ForegroundColor Red
    exit 1
}

$arm64Ok = Publish-Arch 'arm64'
if (-not $arm64Ok) {
    Write-Host 'ARM64 publish failed, building x64-only installer' -ForegroundColor Yellow
}

Write-Host ''
Write-Host '========================================' -ForegroundColor Cyan
Write-Host '  Building Inno Setup installer' -ForegroundColor Cyan
Write-Host '========================================' -ForegroundColor Cyan

Set-Location -LiteralPath $RepoRoot

$issPath = Join-Path $RepoRoot 'installer.iss'
if (-not $arm64Ok) {
    $issContent = [System.IO.File]::ReadAllText($issPath, [System.Text.Encoding]::UTF8)
    $issContent = $issContent.Replace('ArchitecturesAllowed=x64compatible arm64', 'ArchitecturesAllowed=x64compatible')
    $issContent = $issContent.Replace('ArchitecturesInstallIn64BitMode=x64compatible arm64', 'ArchitecturesInstallIn64BitMode=x64compatible')
    $lines = $issContent -split "`r?`n"
    $lines = $lines | Where-Object { $_ -notlike '*publish_arm64*' }
    $issContent = $lines -join "`r`n"
    $tempIss = Join-Path $env:TEMP 'TubaWinUi3_installer.iss'
    [System.IO.File]::WriteAllText($tempIss, $issContent, [System.Text.UTF8Encoding]::new($true))
    $issPath = $tempIss
}

& $ISCC $issPath 2>&1 | Select-Object -Last 15

if ($LASTEXITCODE -ne 0) {
    Write-Host 'Inno Setup compilation FAILED' -ForegroundColor Red
    exit 1
}

Write-Host ''
Write-Host '========================================' -ForegroundColor Green
Write-Host '  BUILD COMPLETE' -ForegroundColor Green
Write-Host '========================================' -ForegroundColor Green

$setupDir = Join-Path $ProjectDir 'SetupOutput'
if (Test-Path -LiteralPath $setupDir) {
    Get-ChildItem -LiteralPath $setupDir -Filter '*.exe' | ForEach-Object {
        $size = [math]::Round($_.Length / 1MB, 1)
        Write-Host "  $($_.Name)  ($size MB)" -ForegroundColor White
        Write-Host "  Path: $($_.FullName)" -ForegroundColor Gray
    }
}

Write-Host ''
Write-Host 'Double-click the setup exe to install.' -ForegroundColor Cyan
Write-Host 'It will create desktop shortcut and Start Menu entry.' -ForegroundColor Cyan

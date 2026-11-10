<#
.SYNOPSIS
    Build TubaWinUi3 MSIX packages for x64 and ARM64, then create .msixbundle for Store submission.

.DESCRIPTION
    1. Build x64 and ARM64 self-contained packages
    2. Remove unnecessary files (.pdb, x86/ARM64 binaries, language packs, theme images)
    3. Create clean AppxManifest.xml with correct Chinese UTF-8 encoding
    4. Package each architecture as .msix
    5. Create .msixbundle containing both packages
    6. Attempt to sign (large packages may fail - Store accepts unsigned)

.NOTES
    SignTool may fail on packages > ~260MB (0x800700C1).
    Microsoft Store accepts UNSIGNED .msix/.msixbundle and re-signs them.
#>

$ErrorActionPreference = 'Continue'

$RepoRoot            = 'C:\Users\luolan\Desktop\tubawinui3'
$ProjectDir          = Join-Path $RepoRoot 'TubaWinUi3.WinUI3'
$MakeAppxPath       = 'C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\makeappx.exe'
$SignToolPath        = 'C:\Program Files (x86)\Windows Kits\10\bin\10.0.26100.0\x64\signtool.exe'

# 签名证书与密码保存在仓库外：%LOCALAPPDATA%\TubaWinUi3-secrets\
# 解析顺序：环境变量 TUBA_STORE_PFX / TUBA_STORE_PFX_PASSWORD → secrets 目录 → 仓库旧路径
$SecretsDir          = Join-Path $env:LOCALAPPDATA 'TubaWinUi3-secrets'
$CertPath            = $env:TUBA_STORE_PFX
if (-not $CertPath) { $CertPath = Join-Path $SecretsDir 'TubaWinUi3_StoreKey.pfx' }
if (-not (Test-Path -LiteralPath $CertPath)) { $CertPath = Join-Path $RepoRoot 'TubaWinUi3_StoreKey.pfx' }

$CertPassword        = $env:TUBA_STORE_PFX_PASSWORD
if (-not $CertPassword) {
    $PfxPasswordFile = Join-Path $SecretsDir 'store-pfx-password.txt'
    if (Test-Path -LiteralPath $PfxPasswordFile) { $CertPassword = (Get-Content -LiteralPath $PfxPasswordFile -Raw).Trim() }
}
if (-not $CertPassword) {
    throw "未找到 Store 证书密码：请设置环境变量 TUBA_STORE_PFX_PASSWORD 或创建 $($SecretsDir)\store-pfx-password.txt"
}

$PackageVersion      = '1.0.1.0'
$PackageName         = 'DA3D64F4.winui3'
$Publisher           = 'CN=CC2339A5-C760-46C3-91D8-130408AF3528'
$PublisherDisplayName = [char]0x7F57 + [char]0x6F9C + [char]0x560E + [char]0x560E
$DisplayName         = [char]0x56FE + [char]0x5427 + [char]0x5DE5 + [char]0x5177 + [char]0x7BB1 + 'winui3'
$Description          = $DisplayName + ' - PC' + [char]0x786C + [char]0x4EF6 + [char]0x68C0 + [char]0x6D4B + [char]0x4E0E + [char]0x7CFB + [char]0x7EDF + [char]0x7EF4 + [char]0x62A4 + [char]0x5DE5 + [char]0x5177 + [char]0x96C6

$OutputDir = Join-Path $ProjectDir 'StoreOutput'
$TempDir   = Join-Path $env:TEMP 'TubaWinUi3_Build'

function Write-CleanManifest {
    param([string]$ManifestPath, [string]$Arch)

    $lines = @(
        '<?xml version="1.0" encoding="utf-8"?>'
        '<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10" xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10" xmlns:uap3="http://schemas.microsoft.com/appx/manifest/uap/windows10/3" xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities" xmlns:desktop="http://schemas.microsoft.com/appx/manifest/desktop/windows10" xmlns:desktop4="http://schemas.microsoft.com/appx/manifest/desktop/windows10/4" xmlns:desktop5="http://schemas.microsoft.com/appx/manifest/desktop/windows10/5" xmlns:com="http://schemas.microsoft.com/appx/manifest/com/windows10" IgnorableNamespaces="uap uap3 rescap desktop desktop4 desktop5 com">'
        ('  <Identity Name="{0}" Publisher="{1}" Version="{2}" ProcessorArchitecture="{3}" />' -f $PackageName, $Publisher, $PackageVersion, $Arch)
        '  <Properties>'
        ('    <DisplayName>{0}</DisplayName>' -f $DisplayName)
        ('    <PublisherDisplayName>{0}</PublisherDisplayName>' -f $PublisherDisplayName)
        '    <Logo>Assets\StoreLogo.png</Logo>'
        '  </Properties>'
        '  <Dependencies>'
        '    <TargetDeviceFamily Name="Windows.Universal" MinVersion="10.0.19041.0" MaxVersionTested="10.0.26226.0" />'
        '    <TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.19041.0" MaxVersionTested="10.0.26100.0" />'
        '    <PackageDependency Name="Microsoft.WindowsAppRuntime.1.8" MinVersion="8000.806.2252.0" Publisher="CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond, S=Washington, C=US" />'
        '  </Dependencies>'
        '  <Resources>'
        '    <Resource Language="zh-CN" />'
        '  </Resources>'
        '  <Applications>'
        '    <Application Id="App" Executable="TubaWinUi3.exe" EntryPoint="Windows.FullTrustApplication">'
        ('      <uap:VisualElements DisplayName="{0}" Description="{1}" BackgroundColor="transparent" Square150x150Logo="Assets\Square150x150Logo.png" Square44x44Logo="Assets\Square44x44Logo.png">' -f $DisplayName, $Description)
        '        <uap:DefaultTile Wide310x150Logo="Assets\Wide310x150Logo.png" />'
        '        <uap:SplashScreen Image="Assets\SplashScreen.png" />'
        '      </uap:VisualElements>'
        '      <Extensions>'
        '        <uap3:Extension Category="windows.appExecutionAlias" Executable="TubaWinUi3.exe" EntryPoint="Windows.FullTrustApplication">'
        '          <uap3:AppExecutionAlias>'
        '            <desktop:ExecutionAlias Alias="TubaWinUi3.exe" />'
        '          </uap3:AppExecutionAlias>'
        '        </uap3:Extension>'
        '        <desktop4:Extension Category="windows.fileExplorerContextMenus">'
        '          <desktop4:FileExplorerContextMenus>'
        '            <desktop4:ItemType Type="*">'
        '              <desktop4:Verb Id="TubaFileLocksmith" Clsid="9C1D4A7E-2B63-4E5F-A08C-6D3B9F1E7A52" />'
        '            </desktop4:ItemType>'
        '            <desktop5:ItemType Type="Directory">'
        '              <desktop5:Verb Id="TubaFileLocksmith" Clsid="9C1D4A7E-2B63-4E5F-A08C-6D3B9F1E7A52" />'
        '            </desktop5:ItemType>'
        '          </desktop4:FileExplorerContextMenus>'
        '        </desktop4:Extension>'
        '        <com:Extension Category="windows.comServer">'
        '          <com:ComServer>'
        '            <com:SurrogateServer DisplayName="TubaWinUi3 File Locksmith">'
        '              <com:Class Id="9C1D4A7E-2B63-4E5F-A08C-6D3B9F1E7A52" Path="TubaWinUi3.ShellExtension.dll" ThreadingModel="STA" />'
        '            </com:SurrogateServer>'
        '          </com:ComServer>'
        '        </com:Extension>'
        '      </Extensions>'
        '    </Application>'
        '  </Applications>'
        '  <Capabilities>'
        '    <rescap:Capability Name="runFullTrust" />'
        '    <DeviceCapability Name="microphone" />'
        '    <DeviceCapability Name="webcam" />'
        '  </Capabilities>'
        '</Package>'
    )

    $content = $lines -join "`r`n"
    [System.IO.File]::WriteAllText($ManifestPath, $content, [System.Text.UTF8Encoding]::new($false))
    Write-Host '  Manifest written successfully' -ForegroundColor Gray
}

function Remove-UnnecessaryFiles {
    param([string]$Root)

    Get-ChildItem -LiteralPath $Root -Filter '*.pdb' -Recurse -Force -ErrorAction SilentlyContinue |
        Remove-Item -Force -ErrorAction SilentlyContinue
    Get-ChildItem -LiteralPath $Root -Filter '*.appxrecipe' -Recurse -Force -ErrorAction SilentlyContinue |
        Remove-Item -Force -ErrorAction SilentlyContinue

    $tools = Join-Path $Root 'Tools'
    if (-not (Test-Path -LiteralPath $tools)) { return }

    # DDU non-Chinese/English language XML
    Get-ChildItem -LiteralPath $tools -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Directory.Name -eq 'Languages' -and $_.Directory.Parent.Name -like '*DDU*' -and $_.Name -notlike '*Chinese*' -and $_.Name -ne 'English.xml' } |
        Remove-Item -Force -ErrorAction SilentlyContinue
    Get-ChildItem -LiteralPath $tools -Recurse -File -Filter 'Display Driver Uninstaller.pdb' -ErrorAction SilentlyContinue |
        Remove-Item -Force -ErrorAction SilentlyContinue

    # x86/ARM64 alternatives (keep x64)
    $removeNames = @('Speccy.exe','HWMonitor_x32.exe','hwmonitor_arm64.exe','cpuz_x32.exe','HWiNFO32.exe','Core Temp x86.exe','DiskInfo32S.exe','procexp.exe','Ventoy2Disk_ARM.exe','Ventoy2Disk_ARM64.exe','VentoyPlugson_X64.exe','Rw.ini.bak')
    Get-ChildItem -LiteralPath $tools -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -in $removeNames } |
        Remove-Item -Force -ErrorAction SilentlyContinue

    # LinX 32-bit dir
    Get-ChildItem -LiteralPath $tools -Recurse -Directory -Filter '32-bit' -ErrorAction SilentlyContinue |
        Where-Object { $_.Parent.Name -eq 'LinX' } |
        Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

    # Big Shizuku theme images
    $themeRemove = @('ShizukuBackground-300.png','Background-300.png','ShizukuVoice.dll')
    Get-ChildItem -LiteralPath $tools -Recurse -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -in $themeRemove } |
        Remove-Item -Force -ErrorAction SilentlyContinue
}

function Build-ArchPackage {
    param([string]$Arch)

    Write-Host ''
    Write-Host '========================================' -ForegroundColor Cyan
    Write-Host "  Building $Arch package" -ForegroundColor Cyan
    Write-Host '========================================' -ForegroundColor Cyan

    $archDir  = Join-Path $TempDir "TubaWinUi3_$Arch"
    $msixFile = Join-Path $OutputDir "TubaWinUi3_${PackageVersion}_${Arch}.msix"

    if (Test-Path -LiteralPath $archDir) { Remove-Item -LiteralPath $archDir -Recurse -Force }

    Write-Host "  Publishing $Arch..." -ForegroundColor Yellow
    dotnet publish $ProjectDir -c Release -r "win-$Arch" --self-contained true -p:Platform=$Arch -p:PublishTrimmed=false -p:PublishReadyToRun=false -p:IncludeShellExtension=true -o $archDir 2>&1 | Select-Object -Last 5

    if (-not (Test-Path -LiteralPath $archDir)) {
        Write-Host "  ERROR: Publish failed for $Arch" -ForegroundColor Red
        return $null
    }

    # Copy Assets from project
    Copy-Item -Path "$ProjectDir\Assets\*" -Destination "$archDir\Assets\" -Recurse -Force

    # Copy CertBlock, Metadata from source (Tools are downloaded at runtime via ToolsBundleService)
    if (Test-Path -LiteralPath "$ProjectDir\CertBlock") {
        Copy-Item -Path "$ProjectDir\CertBlock" -Destination $archDir -Recurse -Force
    }
    if (Test-Path -LiteralPath "$ProjectDir\Metadata") {
        Copy-Item -Path "$ProjectDir\Metadata" -Destination $archDir -Recurse -Force
    }

    # Ensure Metadata/tools.json is present (required for tool metadata in MSIX mode)
    $metadataDir = Join-Path $archDir 'Metadata'
    $toolsJsonPath = Join-Path $metadataDir 'tools.json'
    if (-not (Test-Path -LiteralPath $toolsJsonPath)) {
        if (-not (Test-Path -LiteralPath $metadataDir)) {
            New-Item -ItemType Directory -Path $metadataDir -Force | Out-Null
        }
        Copy-Item -Path "$ProjectDir\Metadata\tools.json" -Destination $toolsJsonPath -Force
        Write-Host '  Restored Metadata/tools.json into package' -ForegroundColor Yellow
    }

    Write-Host '  Removing unnecessary files...' -ForegroundColor Yellow
    Remove-UnnecessaryFiles $archDir

    # Remove Tools directory (MSIX mode downloads tools at runtime via ToolsBundleService)
    $toolsDir = Join-Path $archDir 'Tools'
    if (Test-Path -LiteralPath $toolsDir) {
        Remove-Item -LiteralPath $toolsDir -Recurse -Force
        Write-Host '  Removed Tools directory (downloaded at runtime in MSIX mode)' -ForegroundColor Yellow
    }

    # Remove backend exe (MSIX sandbox cannot launch independent child processes)
    Get-ChildItem -LiteralPath $archDir -Filter 'TubaWinUI3.BackEnd.*' -File -ErrorAction SilentlyContinue |
        Remove-Item -Force -ErrorAction SilentlyContinue
    Write-Host '  Removed TubaWinUI3.BackEnd.* (not supported in MSIX sandbox)' -ForegroundColor Yellow

    Write-Host '  Writing manifest...' -ForegroundColor Yellow
    Write-CleanManifest (Join-Path $archDir 'AppxManifest.xml') $Arch

    # Remove publish artifacts that should not be in the package
    Remove-Item -LiteralPath (Join-Path $archDir 'TubaWinUi3.pdb') -Force -ErrorAction SilentlyContinue
    Remove-Item -LiteralPath (Join-Path $archDir 'TubaWinUi3.build.appxrecipe') -Force -ErrorAction SilentlyContinue

    # Stats
    $files = Get-ChildItem -LiteralPath $archDir -Recurse -File
    $totalSize = [math]::Round(($files | Measure-Object -Property Length -Sum).Sum / 1MB, 1)
    Write-Host "  Package content: $($files.Count) files, $totalSize MB" -ForegroundColor Gray

    Write-Host '  Creating MSIX...' -ForegroundColor Yellow
    $makeappxResult = & $MakeAppxPath pack /d $archDir /p $msixFile /o 2>&1
    Write-Host ($makeappxResult | Select-Object -Last 1)

    $msixSize = [math]::Round((Get-Item -LiteralPath $msixFile).Length / 1MB, 1)
    Write-Host "  MSIX size: $msixSize MB" -ForegroundColor Green

    Write-Host '  Attempting to sign...' -ForegroundColor Yellow
    $signResult = & $SignToolPath sign /fd SHA256 /f $CertPath /p $CertPassword $msixFile 2>&1
    if ($signResult -match 'Successfully signed') {
        Write-Host '  Signing: SUCCESS' -ForegroundColor Green
    } else {
        Write-Host '  Signing: FAILED (OK for Store - Store re-signs)' -ForegroundColor Yellow
    }

    return $msixFile
}

# ============================================================
# Main
# ============================================================
Write-Host 'TubaWinUi3 MSIX Bundle Build Script' -ForegroundColor Magenta
Write-Host "Version: $PackageVersion" -ForegroundColor Magenta
Write-Host ''

if (-not (Test-Path -LiteralPath $OutputDir)) { New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null }
if (-not (Test-Path -LiteralPath $TempDir)) { New-Item -ItemType Directory -Path $TempDir -Force | Out-Null }

$x64Msix = Build-ArchPackage 'x64'
if ($null -eq $x64Msix) {
    Write-Host 'FAILED to build x64 package' -ForegroundColor Red
    exit 1
}

$arm64Msix = Build-ArchPackage 'arm64'
$arm64Available = $null -ne $arm64Msix

if ($arm64Available) {
    Write-Host ''
    Write-Host '========================================' -ForegroundColor Cyan
    Write-Host '  Creating MSIX Bundle' -ForegroundColor Cyan
    Write-Host '========================================' -ForegroundColor Cyan

    $bundleFile = Join-Path $OutputDir "TubaWinUi3_${PackageVersion}.msixbundle"

    $x64Name  = "TubaWinUi3_${PackageVersion}_x64.msix"
    $arm64Name = "TubaWinUi3_${PackageVersion}_arm64.msix"

    $mapping = "[Files]`r`n`"$x64Msix`" `"$x64Name`"`r`n`"$arm64Msix`" `"$arm64Name`"`r`n"
    $mappingPath = Join-Path $TempDir 'bundle_mapping.txt'
    [System.IO.File]::WriteAllText($mappingPath, $mapping, [System.Text.UTF8Encoding]::new($false))

    & $MakeAppxPath bundle /f $mappingPath /p $bundleFile /o 2>&1 | Select-Object -Last 2

    $bundleSize = [math]::Round((Get-Item -LiteralPath $bundleFile).Length / 1MB, 1)
    Write-Host "  Bundle size: $bundleSize MB" -ForegroundColor Green

    Write-Host '  Attempting to sign bundle...' -ForegroundColor Yellow
    $bundleSignResult = & $SignToolPath sign /fd SHA256 /f $CertPath /p $CertPassword $bundleFile 2>&1
    if ($bundleSignResult -match 'Successfully signed') {
        Write-Host '  Bundle signing: SUCCESS' -ForegroundColor Green
    } else {
        Write-Host '  Bundle signing: FAILED (OK for Store - Store re-signs)' -ForegroundColor Yellow
    }
} else {
    Write-Host ''
    Write-Host 'ARM64 build failed - only x64 package available' -ForegroundColor Yellow
}

Write-Host ''
Write-Host '========================================' -ForegroundColor Green
Write-Host '  BUILD COMPLETE' -ForegroundColor Green
Write-Host '========================================' -ForegroundColor Green
Write-Host "Output: $OutputDir" -ForegroundColor White
Write-Host ''

Get-ChildItem -LiteralPath $OutputDir -Filter '*.msix*' | ForEach-Object {
    $size = [math]::Round($_.Length / 1MB, 1)
    Write-Host "  $($_.Name)  ($size MB)" -ForegroundColor White
}

Write-Host ''
Write-Host 'If signing failed, that is OK for Store submission.' -ForegroundColor Yellow
Write-Host 'The Store re-signs all packages during certification.' -ForegroundColor Yellow
Write-Host ''
Write-Host 'Upload the .msixbundle (or .msix if x64-only) to Partner Center.' -ForegroundColor Cyan

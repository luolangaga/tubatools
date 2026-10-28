<#
.SYNOPSIS
    Build TubaWinUi3 MSIX packages for Microsoft Store submission — WITHOUT Tools/ folder.

.DESCRIPTION
    Creates lean MSIX packages that do NOT include the Tools/ directory.
    The Store version downloads the tools bundle from GitHub/GitCode Release on first launch.

    Features:
    - Auto-detects Windows SDK makeappx.exe / signtool.exe paths
    - Auto-reads version from .csproj
    - Supports x86, x64, ARM64 (configurable via -Archs)
    - Self-contained publish (no .NET runtime dependency)
    - Excludes Tools/ via ExcludeToolsFromPublish
    - Restores .pri file from publish output
    - Creates .msixbundle if multiple architectures succeed

.PARAMETER Version
    Override the version read from .csproj (format: X.Y.Z.W — 4-part for MSIX)

.PARAMETER Archs
    Architectures to build. Default: @('x64','arm64')

.PARAMETER FrameworkDependent
    Publish framework-dependent instead of self-contained. Default: self-contained.

.PARAMETER SkipSign
    Skip signing step. Store re-signs anyway.

.EXAMPLE
    .\build-msix-store.ps1
    .\build-msix-store.ps1 -Version 1.0.3.0 -Archs @('x64','arm64','x86')
    .\build-msix-store.ps1 -SkipSign
#>

param(
    [string]$Version = '',
    [string[]]$Archs = @('x64','arm64'),
    [switch]$FrameworkDependent,
    [switch]$SkipSign
)

$ErrorActionPreference = 'Stop'

# ── Project paths ──────────────────────────────────────────────
$ProjectDir   = $PSScriptRoot
$WinUI3Dir    = Join-Path $ProjectDir 'TubaWinUi3.WinUI3'
$CsprojPath   = Join-Path $WinUI3Dir 'TubaWinUi3.csproj'
$OutputDir    = Join-Path $ProjectDir 'StoreOutput'
$TempDir      = Join-Path $env:TEMP 'TubaWinUi3_MSIX_Build'

# ── Package identity (must match Partner Center) ──────────────
$PackageName         = 'DA3D64F4.winui3'
$Publisher           = 'CN=CC2339A5-C760-46C3-91D8-130408AF3528'
$PublisherDisplayName = '罗澜嘎嘎'
$DisplayName         = '图吧工具箱CE'
$Description         = '图吧工具箱CE - PC硬件检测与系统维护工具集'

# ── Signing ────────────────────────────────────────────────────
# 签名证书与密码保存在仓库外：%LOCALAPPDATA%\TubaWinUi3-secrets\
# 解析顺序：环境变量 TUBA_STORE_PFX / TUBA_STORE_PFX_PASSWORD → secrets 目录 → 仓库旧路径
$SecretsDir   = Join-Path $env:LOCALAPPDATA 'TubaWinUi3-secrets'
$CertPath     = $env:TUBA_STORE_PFX
if (-not $CertPath) { $CertPath = Join-Path $SecretsDir 'TubaWinUi3_StoreKey.pfx' }
if (-not (Test-Path -LiteralPath $CertPath)) { $CertPath = Join-Path $ProjectDir 'TubaWinUi3_StoreKey.pfx' }

$CertPassword = $env:TUBA_STORE_PFX_PASSWORD
if (-not $CertPassword) {
    $PfxPasswordFile = Join-Path $SecretsDir 'store-pfx-password.txt'
    if (Test-Path -LiteralPath $PfxPasswordFile) { $CertPassword = (Get-Content -LiteralPath $PfxPasswordFile -Raw).Trim() }
}
# 密码缺失时仅告警不中断：脚本对未找到证书的场景本就跳过签名

# ── Auto-detect SDK tools ──────────────────────────────────────
# 查找顺序：系统 Windows SDK（C:\Program Files (x86)\Windows Kits）→
# NuGet 缓存中的 Microsoft.Windows.SDK.BuildTools 包（dotnet publish 会拉取，
# 自带 makeappx/signtool，用于未安装完整 Windows SDK 的机器）。
function Find-SdkTool {
    param([string]$ToolName)

    # 1) 系统 Windows SDK
    $sdkRoot = 'C:\Program Files (x86)\Windows Kits\10\bin'
    if (Test-Path -LiteralPath $sdkRoot) {
        $latestVersion = Get-ChildItem -LiteralPath $sdkRoot -Directory |
            Where-Object { $_.Name -match '^\d+\.\d+\.\d+\.\d+$' } |
            Sort-Object { [version]$_.Name } -Descending |
            Select-Object -First 1

        if ($null -ne $latestVersion) {
            $toolPath = Join-Path $latestVersion.FullName "x64\$ToolName"
            if (Test-Path -LiteralPath $toolPath) {
                Write-Host "  Using $ToolName from Windows SDK $($latestVersion.Name)" -ForegroundColor Gray
                return $toolPath
            }
        }
    }

    # 2) NuGet 缓存中的 BuildTools 包
    $nugetRoot = Join-Path $env:USERPROFILE '.nuget\packages\microsoft.windows.sdk.buildtools'
    if (Test-Path -LiteralPath $nugetRoot) {
        $latestPkg = Get-ChildItem -LiteralPath $nugetRoot -Directory |
            Sort-Object { [version]$_.Name } -Descending |
            Select-Object -First 1

        if ($null -ne $latestPkg) {
            $toolPath = Get-ChildItem -LiteralPath $latestPkg.FullName -Recurse -Filter $ToolName |
                Where-Object { $_.FullName -match '\\x64\\' } |
                Sort-Object FullName -Descending |
                Select-Object -First 1

            if ($null -ne $toolPath) {
                Write-Host "  Using $ToolName from NuGet BuildTools $($latestPkg.Name)" -ForegroundColor Gray
                return $toolPath.FullName
            }
        }
    }

    throw "$ToolName not found in Windows SDK or NuGet BuildTools"
}

$MakeAppxPath = Find-SdkTool 'makeappx.exe'
$SignToolPath  = Find-SdkTool 'signtool.exe'

# ── Auto-read version from .csproj ────────────────────────────
if (-not $Version) {
    $csproj = [xml](Get-Content -LiteralPath $CsprojPath -Raw)
    $rawVer = ($csproj.Project.PropertyGroup | Where-Object { $_.Version -ne $null } | Select-Object -First 1).Version
    if (-not $rawVer) { throw 'Cannot read Version element from csproj' }

    $parts = $rawVer.Split('.')
    if ($parts.Count -eq 3) {
        $Version = "$rawVer.0"
    } elseif ($parts.Count -eq 4) {
        $Version = $rawVer
    } else {
        throw "Unexpected version format in .csproj: $rawVer"
    }
}

# MSIX 的 Identity 是 4 段（1.8.0.0），MSBuild 的 Version 用 3 段更贴合 CI 发布产物的
# FileVersion/ProductVersion 形态（1.8.0.0 / 1.8.0+<sha>）。
$PublishVersion = ($Version.Split('.')[0..2] -join '.')

$selfContainedFlag = -not $FrameworkDependent.IsPresent

Write-Host ''
Write-Host '╔══════════════════════════════════════════════╗' -ForegroundColor Magenta
Write-Host '║  TubaWinUi3 MSIX Store Build (no Tools)     ║' -ForegroundColor Magenta
Write-Host "║  Version: $Version$((' ' * (28 - $Version.Length)))║" -ForegroundColor Magenta
Write-Host "║  Archs:    $($Archs -join ', ')$((' ' * (28 - ($Archs -join ', ').Length)))║" -ForegroundColor Magenta
Write-Host '╚══════════════════════════════════════════════╝' -ForegroundColor Magenta
Write-Host ''

# ── Manifest writer ────────────────────────────────────────────
function Write-CleanManifest {
    param([string]$ManifestPath, [string]$Arch)

    $lines = @(
        '<?xml version="1.0" encoding="utf-8"?>'
        '<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10" xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10" xmlns:uap3="http://schemas.microsoft.com/appx/manifest/uap/windows10/3" xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities" xmlns:desktop="http://schemas.microsoft.com/appx/manifest/desktop/windows10" xmlns:desktop4="http://schemas.microsoft.com/appx/manifest/desktop/windows10/4" xmlns:desktop5="http://schemas.microsoft.com/appx/manifest/desktop/windows10/5" xmlns:com="http://schemas.microsoft.com/appx/manifest/com/windows10" IgnorableNamespaces="uap uap3 rescap desktop desktop4 desktop5 com">'
        "  <Identity Name=`"$PackageName`" Publisher=`"$Publisher`" Version=`"$Version`" ProcessorArchitecture=`"$Arch`" />"
        '  <Properties>'
        "    <DisplayName>$DisplayName</DisplayName>"
        "    <PublisherDisplayName>$PublisherDisplayName</PublisherDisplayName>"
        '    <Logo>Assets\StoreLogo.png</Logo>'
        '  </Properties>'
        '  <Dependencies>'
        '    <TargetDeviceFamily Name="Windows.Universal" MinVersion="10.0.19041.0" MaxVersionTested="10.0.26226.0" />'
        '    <TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.19041.0" MaxVersionTested="10.0.26100.0" />'
        '  </Dependencies>'
        '  <Resources>'
        '    <Resource Language="zh-CN" />'
        '  </Resources>'
        '  <Applications>'
        '    <Application Id="App" Executable="TubaWinUi3.exe" EntryPoint="Windows.FullTrustApplication">'
        "      <uap:VisualElements DisplayName=`"$DisplayName`" Description=`"$Description`" BackgroundColor=`"transparent`" Square150x150Logo=`"Assets\Square150x150Logo.png`" Square44x44Logo=`"Assets\Square44x44Logo.png`">"
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
        '  </Capabilities>'
        '</Package>'
    )

    $content = $lines -join "`r`n"
    [System.IO.File]::WriteAllText($ManifestPath, $content, [System.Text.UTF8Encoding]::new($false))
    Write-Host '  Manifest written' -ForegroundColor Gray
}

# ── Build one architecture ─────────────────────────────────────
function Build-ArchPackage {
    param([string]$Arch)

    Write-Host ''
    Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Cyan
    Write-Host "  Building $Arch MSIX (no Tools)" -ForegroundColor Cyan
    Write-Host "━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━" -ForegroundColor Cyan

    $archDir  = Join-Path $TempDir "TubaWinUi3_$Arch"
    $msixFile = Join-Path $OutputDir "TubaWinUi3_${Version}_${Arch}.msix"

    if (Test-Path -LiteralPath $archDir) {
        Remove-Item -LiteralPath $archDir -Recurse -Force
    }

    Write-Host "  dotnet publish ($Arch, self-contained=$selfContainedFlag)..." -ForegroundColor Yellow
    dotnet publish $CsprojPath `
        -c Release `
        -r "win-$Arch" `
        --self-contained:$selfContainedFlag `
        -p:Platform=$Arch `
        -p:Version=$PublishVersion `
        -p:PublishTrimmed=false `
        -p:PublishReadyToRun=false `
        -p:ExcludeToolsFromPublish=true `
        -p:IncludeShellExtension=true `
        -o $archDir 2>&1 |
        Select-Object -Last 3 |
        ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray }

    if (-not (Test-Path -LiteralPath $archDir)) {
        Write-Host "  ERROR: Publish failed for $Arch" -ForegroundColor Red
        return $null
    }

    $exePath = Join-Path $archDir 'TubaWinUi3.exe'
    if (-not (Test-Path -LiteralPath $exePath)) {
        Write-Host "  ERROR: TubaWinUi3.exe not found in publish output" -ForegroundColor Red
        return $null
    }

    # 「文件占用查看」Win11 新版右键菜单处理程序：清单 com:SurrogateServer 引用的原生 DLL，
    # 缺失时包内菜单会静默失效，必须在这里拦下。
    $shellExtDll = Join-Path $archDir 'TubaWinUi3.ShellExtension.dll'
    if (-not (Test-Path -LiteralPath $shellExtDll)) {
        Write-Host '  ERROR: TubaWinUi3.ShellExtension.dll not found in publish output (右键菜单处理程序缺失)' -ForegroundColor Red
        return $null
    }

    # 商店包不允许内置内核（Tools 由用户首次启动时下载）。SDK 的默认 Content 项会把
    # 项目目录下所有 png/jpg/ico 当资源，Tools/**/*.png 与残留的 bin-verify/ 会悄悄进包，
    # 曾导致 1.7.2 包从 ~110MB 膨胀到 ~390MB。出现即视为构建污染，宁可失败也不出包。
    foreach ($stray in @('Tools', 'bin-verify')) {
        if (Test-Path -LiteralPath (Join-Path $archDir $stray)) {
            Write-Host "  ERROR: publish output contains $stray/ (商店包不允许内置内核或构建残留)" -ForegroundColor Red
            return $null
        }
    }

    # Assets / Metadata / CertBlock 已由 csproj 的 Content 项（CopyToOutputDirectory=PreserveNewest）
    # 随 publish 一并输出，无需在此重复拷贝。

    $buildPriPattern = Join-Path $WinUI3Dir "bin\$Arch\Release\*\win-$Arch\TubaWinUi3.pri"
    $foundPri = Get-Item $buildPriPattern -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($foundPri) {
        Copy-Item -LiteralPath $foundPri.FullName -Destination $archDir -Force
        Write-Host "  Restored TubaWinUi3.pri from build output" -ForegroundColor Gray
    }

    # 非中英语言的框架 .mui 删除：csproj 的 SatelliteResourceLanguages 只作用于托管卫星程序集，
    # WinAppSDK 的 Microsoft.ui.xaml 等 .mui 不受其影响（实测 172 个 / 85 种语言）；
    # 缺失语言由 Windows 资源加载器回退到中性（英文）资源，不报错。
    $keepLangs = @('zh-CN', 'zh-TW', 'en-us', 'en-GB')
    $removedMui = Get-ChildItem -LiteralPath $archDir -Recurse -Filter '*.mui' -Force -ErrorAction SilentlyContinue |
        Where-Object { $keepLangs -notcontains $_.Directory.Name }
    $removedMuiCount = @($removedMui).Count
    $removedMui | Remove-Item -Force -ErrorAction SilentlyContinue
    if ($removedMuiCount -gt 0) {
        Write-Host "  Removed $removedMuiCount non-zh/en framework .mui files" -ForegroundColor Gray
    }

    Get-ChildItem -LiteralPath $archDir -Filter '*.pdb' -Recurse -Force -ErrorAction SilentlyContinue |
        Remove-Item -Force -ErrorAction SilentlyContinue
    Get-ChildItem -LiteralPath $archDir -Filter '*.appxrecipe' -Recurse -Force -ErrorAction SilentlyContinue |
        Remove-Item -Force -ErrorAction SilentlyContinue

    $priFile = Join-Path $archDir 'TubaWinUi3.pri'
    if (-not (Test-Path -LiteralPath $priFile)) {
        $altPri = Get-ChildItem -LiteralPath $archDir -Filter '*.pri' -ErrorAction SilentlyContinue |
            Select-Object -First 1
        if ($altPri) {
            Write-Host "  Found pri: $($altPri.Name)" -ForegroundColor Gray
        } else {
            Write-Host '  WARNING: No .pri file found in publish output' -ForegroundColor Yellow
        }
    }

    Write-Host '  Writing AppxManifest.xml...' -ForegroundColor Yellow
    Write-CleanManifest (Join-Path $archDir 'AppxManifest.xml') $Arch

    $files = Get-ChildItem -LiteralPath $archDir -Recurse -File
    $totalSize = [math]::Round(($files | Measure-Object -Property Length -Sum).Sum / 1MB, 1)
    Write-Host "  Content: $($files.Count) files, $totalSize MB" -ForegroundColor Gray

    Write-Host '  Creating MSIX...' -ForegroundColor Yellow
    $makeappxOutput = & $MakeAppxPath pack /d $archDir /p $msixFile /o 2>&1
    $makeappxOutput | Select-Object -Last 5 | ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray }

    if (-not (Test-Path -LiteralPath $msixFile)) {
        Write-Host '  ERROR: MSIX creation failed' -ForegroundColor Red
        Write-Host '  makeappx output:' -ForegroundColor Red
        $makeappxOutput | ForEach-Object { Write-Host "    $_" -ForegroundColor Red }
        return $null
    }

    $msixSize = [math]::Round((Get-Item -LiteralPath $msixFile).Length / 1MB, 1)
    Write-Host "  MSIX: $msixSize MB" -ForegroundColor Green

    if (-not $SkipSign -and (Test-Path -LiteralPath $CertPath)) {
        Write-Host '  Signing...' -ForegroundColor Yellow
        $signResult = & $SignToolPath sign /fd SHA256 /f $CertPath /p $CertPassword $msixFile 2>&1
        if ($signResult -match 'Successfully signed') {
            Write-Host '  Signed OK' -ForegroundColor Green
        } else {
            Write-Host '  Signing failed (OK for Store — Store re-signs)' -ForegroundColor Yellow
        }
    } elseif (-not $SkipSign) {
        Write-Host "  Skipping sign (cert not found: $CertPath)" -ForegroundColor Yellow
    }

    return $msixFile
}

# ── Main ───────────────────────────────────────────────────────
if (-not (Test-Path -LiteralPath $OutputDir)) {
    New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null
}
if (-not (Test-Path -LiteralPath $TempDir)) {
    New-Item -ItemType Directory -Path $TempDir -Force | Out-Null
}

$builtMsix = @{}

foreach ($arch in $Archs) {
    $msix = Build-ArchPackage $arch
    if ($msix) {
        $builtMsix[$arch] = $msix
    } else {
        Write-Host "  Skipping $arch due to build failure" -ForegroundColor Red
    }
}

if ($builtMsix.Count -eq 0) {
    Write-Host ''
    Write-Host 'All architectures failed!' -ForegroundColor Red
    exit 1
}

if ($builtMsix.Count -ge 2) {
    Write-Host ''
    Write-Host '━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━' -ForegroundColor Cyan
    Write-Host '  Creating MSIX Bundle' -ForegroundColor Cyan
    Write-Host '━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━' -ForegroundColor Cyan

    $bundleFile = Join-Path $OutputDir "TubaWinUi3_${Version}.msixbundle"

    $mappingLines = @('[Files]')
    foreach ($arch in $builtMsix.Keys) {
        $fileName = "TubaWinUi3_${Version}_${arch}.msix"
        $mappingLines += "`"$($builtMsix[$arch])`" `"$fileName`""
    }
    $mapping = $mappingLines -join "`r`n"
    $mappingPath = Join-Path $TempDir 'bundle_mapping.txt'
    [System.IO.File]::WriteAllText($mappingPath, $mapping, [System.Text.UTF8Encoding]::new($false))

    & $MakeAppxPath bundle /f $mappingPath /p $bundleFile /o 2>&1 |
        Select-Object -Last 2 |
        ForEach-Object { Write-Host "  $_" -ForegroundColor DarkGray }

    if (Test-Path -LiteralPath $bundleFile) {
        $bundleSize = [math]::Round((Get-Item -LiteralPath $bundleFile).Length / 1MB, 1)
        Write-Host "  Bundle: $bundleSize MB" -ForegroundColor Green

        if (-not $SkipSign -and (Test-Path -LiteralPath $CertPath)) {
            Write-Host '  Signing bundle...' -ForegroundColor Yellow
            $bundleSignResult = & $SignToolPath sign /fd SHA256 /f $CertPath /p $CertPassword $bundleFile 2>&1
            if ($bundleSignResult -match 'Successfully signed') {
                Write-Host '  Bundle signed OK' -ForegroundColor Green
            } else {
                Write-Host '  Bundle signing failed (OK for Store)' -ForegroundColor Yellow
            }
        }
    } else {
        Write-Host '  Bundle creation failed' -ForegroundColor Red
    }
}

# ── Summary ────────────────────────────────────────────────────
Write-Host ''
Write-Host '━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━' -ForegroundColor Green
Write-Host '  BUILD COMPLETE' -ForegroundColor Green
Write-Host '━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━' -ForegroundColor Green
Write-Host "  Output: $OutputDir" -ForegroundColor White
Write-Host ''

Get-ChildItem -LiteralPath $OutputDir -Filter '*.msix*' |
    ForEach-Object {
        $size = [math]::Round($_.Length / 1MB, 1)
        Write-Host "  $($_.Name)  ($size MB)" -ForegroundColor White
    }

Write-Host ''
Write-Host '  Tools/ is NOT included — Store version downloads on first launch' -ForegroundColor Cyan
Write-Host '  Upload the .msixbundle to Partner Center' -ForegroundColor Cyan
Write-Host '  Upload Tools.zip to GitHub/GitCode Release for each version!' -ForegroundColor Yellow

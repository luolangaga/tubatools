<#
.SYNOPSIS
    One-command "package & run" in packaged (MSIX) mode — CLI equivalent of VS F5 on a
    single-project MSIX. Builds → registers the build output as a dev package (Developer
    Mode, no signing needed) → seeds the packaged Tools data → launches the app.

.DESCRIPTION
    Why this exists: `dotnet run` only runs the app UNPACKAGED (WindowsPackageType=None).
    MSIX-mode behaviors (read/write of the writable tools.json copy under
    %LOCALAPPDATA%\Packages\<family>\LocalState\TubaWinUi3\Metadata, tool root under
    ...\LocalState\TubaWinUi3\Tools, package identity / AppSettings isolation) can only be
    exercised by an actually-registered package. This script registers the existing build
    output folder in place (like VS "Register AppxManifest" / single-project MSIX deploy),
    using a DISTINCT dev identity 'tubawinui3.dev' so it never touches the Store identity
    'DA3D64F4.winui3'.

    Requirements:
    - Windows Developer Mode ON (Settings → Privacy & security → For developers).
      Verify: reg query "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock" /v AllowDevelopmentWithoutDevLicense
    - The app is NOT expected to write into the install dir; data lives in LocalState.
      Because this registers the writable bin output as the package root, the "install dir
      read-only" ACL of a real Store install is NOT reproduced — only the app's own
      LocalState-first data paths are. For a faithful Store-like package use
      .\build-msix-store.ps1 and install the .msix.

.PARAMETER Config
    Debug (default) or Release.

.PARAMETER Arch
    win-x64 / win-arm64 / win-x86. Defaults to current machine architecture.

.PARAMETER NoBuild
    Skip `dotnet build`; register whatever is already in the output folder.

.PARAMETER SkipSeedTools
    Do not copy the source Tools\ folder into the packaged LocalState. Default seeds once
    (only when LocalState has no Tools yet).

.PARAMETER ForceSeedTools
    Re-copy source Tools over the packaged LocalState Tools (mirror). Implies seeding.

.PARAMETER NoLaunch
    Register only; do not start the app.

.PARAMETER RebuildShellExt
    Rebuild the NativeAOT shell-extension DLL (Win11 context menu handler) into the package root,
    even if a previous one is present.

.EXAMPLE
    .\run-msix.ps1                 # Debug: build → register → launch packaged app
    .\run-msix.ps1 -NoBuild        # fastest iteration on existing output
    .\run-msix.ps1 -NoLaunch       # CI / headless register check
#>
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Config = 'Debug',
    [ValidateSet('x64', 'arm64', 'x86')]
    [string]$Arch = '',
    [switch]$NoBuild,
    [switch]$SkipSeedTools,
    [switch]$ForceSeedTools,
    [switch]$NoLaunch,
    [switch]$RebuildShellExt
)

$ErrorActionPreference = 'Stop'

# ── Paths ──────────────────────────────────────────────────────
$ProjectDir = $PSScriptRoot
$WinUI3Dir  = Join-Path $ProjectDir 'TubaWinUi3.WinUI3'
$CsprojPath = Join-Path $WinUI3Dir 'TubaWinUi3.csproj'

# Dev package identity — never collide with the Store identity 'DA3D64F4.winui3'.
$DevPkgName    = 'tubawinui3.dev'
$DevPublisher  = 'CN=TubaWinUi3Dev'
$DevAppId      = 'App'

# ── Resolve architecture / TFM / version from csproj ──────────
if (-not $Arch) {
    switch ($env:PROCESSOR_ARCHITECTURE) {
        'AMD64' { $Arch = 'x64' }
        'ARM64' { $Arch = 'arm64' }
        'x86'   { $Arch = 'x86' }
        default { $Arch = 'x64' }
    }
}
$rid = "win-$Arch"

$csprojXml = [xml](Get-Content -LiteralPath $CsprojPath -Raw)
$tfm  = ($csprojXml.Project.PropertyGroup | Where-Object { $_.TargetFramework } | Select-Object -First 1).TargetFramework
$ver  = ($csprojXml.Project.PropertyGroup | Where-Object { $_.Version } | Select-Object -First 1).Version
if ($ver.Split('.').Count -eq 3) { $ver = "$ver.0" }   # MSIX needs 4 parts
if (-not $tfm) { throw 'Cannot read TargetFramework from csproj' }

$OutRoot = Join-Path $WinUI3Dir "bin\$Config\$tfm\$rid"
$ManifestPath = Join-Path $OutRoot 'AppxManifest.xml'
$ExePath      = Join-Path $OutRoot 'TubaWinUi3.exe'

Write-Host ''
Write-Host '╔══════════════════════════════════════════════════════╗' -ForegroundColor Magenta
Write-Host '║  TubaWinUi3 — packaged (MSIX dev) run                ║' -ForegroundColor Magenta
Write-Host "║  Config=$Config  RID=$rid  pkg=$DevPkgName" -ForegroundColor Magenta
Write-Host '╚══════════════════════════════════════════════════════╝' -ForegroundColor Magenta
Write-Host ''

# ── 1. Stop any running instance (releases exe/dll locks) ─────
Get-Process -Name 'TubaWinUi3' -ErrorAction SilentlyContinue |
    Stop-Process -Force -ErrorAction SilentlyContinue
Start-Sleep -Milliseconds 400

# ── 2. Build ───────────────────────────────────────────────────
if (-not $NoBuild) {
    Write-Host "  dotnet build -c $Config -r $rid ..." -ForegroundColor Yellow
    & dotnet build $CsprojPath -c $Config -r $rid --nologo 2>&1 | Select-Object -Last 6 |
        ForEach-Object { Write-Host "    $_" -ForegroundColor DarkGray }
    if ($LASTEXITCODE -ne 0) { throw "dotnet build failed (exit $LASTEXITCODE)" }
}

if (-not (Test-Path -LiteralPath $ExePath)) {
    throw "Output not found: $ExePath. Build first, or pass -Config to match an existing output."
}

# ── 3. Sanity: .pri (WinUI resources) must exist next to exe ──
$pri = Get-ChildItem -LiteralPath $OutRoot -Filter '*.pri' -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $pri) {
    Write-Host '  WARNING: no .pri found in output — packaged resource loading may fail' -ForegroundColor Yellow
}

# ── 3.5 Shell extension (Win11 新版右键菜单处理程序, NativeAOT) ──
# 包清单以 com:SurrogateServer 声明该 DLL；dev 注册是原地注册 bin 目录，所以 DLL 必须
# 在包根（= 构建输出目录）。缺失或 -RebuildShellExt 时调用仓库根脚本构建。
$shellExtDll = Join-Path $OutRoot 'TubaWinUi3.ShellExtension.dll'
if ($RebuildShellExt -or -not (Test-Path -LiteralPath $shellExtDll)) {
    Write-Host '  Building shell extension DLL (NativeAOT)...' -ForegroundColor Yellow
    & powershell -NoProfile -ExecutionPolicy Bypass -File (Join-Path $ProjectDir 'build-shell-ext-aot.ps1') -Rid $rid -OutputDir $OutRoot -Configuration $Config
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $shellExtDll)) {
        throw 'Shell extension build failed. NativeAOT 需要 MSVC 链接器（VS2022 Build Tools），详见 build-shell-ext-aot.ps1 输出。'
    }
} else {
    Write-Host '  Shell extension DLL present (use -RebuildShellExt to rebuild)' -ForegroundColor Gray
}

# ── 4. Write dev AppxManifest.xml into the package root ───────
Write-Host '  Writing AppxManifest.xml (dev identity)...' -ForegroundColor Yellow
$lines = @(
    '<?xml version="1.0" encoding="utf-8"?>'
    '<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10" xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10" xmlns:uap3="http://schemas.microsoft.com/appx/manifest/uap/windows10/3" xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities" xmlns:desktop="http://schemas.microsoft.com/appx/manifest/desktop/windows10" xmlns:desktop4="http://schemas.microsoft.com/appx/manifest/desktop/windows10/4" xmlns:desktop5="http://schemas.microsoft.com/appx/manifest/desktop/windows10/5" xmlns:com="http://schemas.microsoft.com/appx/manifest/com/windows10" IgnorableNamespaces="uap uap3 rescap desktop desktop4 desktop5 com">'
    "  <Identity Name=`"$DevPkgName`" Publisher=`"$DevPublisher`" Version=`"$ver`" ProcessorArchitecture=`"$Arch`" />"
    '  <Properties>'
    '    <DisplayName>图吧工具箱CE (Dev)</DisplayName>'
    '    <PublisherDisplayName>TubaDev</PublisherDisplayName>'
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
    "    <Application Id=`"$DevAppId`" Executable=`"TubaWinUi3.exe`" EntryPoint=`"Windows.FullTrustApplication`">"
    '      <uap:VisualElements DisplayName="图吧工具箱CE (Dev)" Description="TubaWinUi3 packaged dev run" BackgroundColor="transparent" Square150x150Logo="Assets\Square150x150Logo.png" Square44x44Logo="Assets\Square44x44Logo.png">'
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

# ── 5. Developer Mode check ────────────────────────────────────
$unlock = Get-ItemProperty -Path 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock' -ErrorAction SilentlyContinue
if (-not $unlock -or $unlock.AllowDevelopmentWithoutDevLicense -ne 1) {
    Write-Host ''
    Write-Host '  ERROR: Windows Developer Mode is OFF.' -ForegroundColor Red
    Write-Host '  Enable it: Settings → Privacy & security → For developers → Developer Mode' -ForegroundColor Red
    Write-Host '  (or run:  reg add "HKLM\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock" /v AllowDevelopmentWithoutDevLicense /t REG_DWORD /d 1 /f)' -ForegroundColor Red
    exit 2
}

# ── 6. Replace previous dev registration ───────────────────────
$old = Get-AppxPackage -Name $DevPkgName -ErrorAction SilentlyContinue
if ($old) {
    Write-Host "  Removing previous dev package $($old.PackageFullName)..." -ForegroundColor Gray
    $old | Remove-AppxPackage -ErrorAction Stop
}

# ── 7. Register (file-based, Developer Mode, no signing) ───────
Write-Host "  Registering package from $OutRoot ..." -ForegroundColor Yellow
try {
    Add-AppxPackage -Register $ManifestPath -ErrorAction Stop
} catch {
    Write-Host "  Registration FAILED: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host '  Check Developer Mode / that no Store package claims the same identity.' -ForegroundColor Red
    exit 3
}
Write-Host '  Registered OK' -ForegroundColor Green

$pkg = Get-AppxPackage -Name $DevPkgName
$family = $pkg.PackageFamilyName
Write-Host "  Family: $family" -ForegroundColor Gray

# ── 8. Seed packaged Tools data into LocalState ────────────────
# Packaged app never reads the install-dir Tools; its tool root is
# LocalFolder(=LocalState)\TubaWinUi3\Tools (ToolsBundleService). Seed once from source
# so the catalog is usable offline during dev.
$srcTools = Join-Path $WinUI3Dir 'Tools'
$localState = Join-Path $env:LOCALAPPDATA "Packages\$family\LocalState"
$dstTools   = Join-Path $localState 'TubaWinUi3\Tools'
$seed = (-not $SkipSeedTools) -and (Test-Path -LiteralPath $srcTools)
if ($seed -and (-not (Test-Path -LiteralPath $dstTools) -or $ForceSeedTools)) {
    Write-Host '  Seeding packaged Tools → LocalState (first run only; -ForceSeedTools to re-mirror)...' -ForegroundColor Yellow
    if (Test-Path -LiteralPath $dstTools) { Remove-Item -LiteralPath $dstTools -Recurse -Force }
    New-Item -ItemType Directory -Path (Split-Path $dstTools) -Force | Out-Null
    robocopy $srcTools $dstTools /E /MT:16 /NFL /NDL /NJH /NJS /R:1 /W:1 | Out-Null
    Write-Host "  Seeded: $dstTools" -ForegroundColor Green
} elseif ($seed) {
    Write-Host '  Packaged Tools already present in LocalState — skip seeding (-ForceSeedTools to refresh)' -ForegroundColor Gray
} elseif (-not $SkipSeedTools) {
    Write-Host '  WARNING: source Tools\ not found; catalog will be empty until the app downloads a Tools bundle' -ForegroundColor Yellow
}

# ── 9. Launch via shell activation (gets real package identity) ─
if ($NoLaunch) {
    Write-Host ''
    Write-Host '  Registered (launch skipped). To start the packaged app:' -ForegroundColor Cyan
    Write-Host "    explorer.exe shell:AppsFolder\$family!$DevAppId" -ForegroundColor White
} else {
    Write-Host '  Launching packaged app...' -ForegroundColor Yellow
    Start-Process "shell:AppsFolder\$family!$DevAppId"
    Start-Sleep -Seconds 3
    $running = Get-Process -Name 'TubaWinUi3' -ErrorAction SilentlyContinue
    if ($running) {
        Write-Host "  Running as PID $($running.Id -join ', ') (packaged identity $family)" -ForegroundColor Green
    } else {
        Write-Host '  Launch issued, but process not detected after 3s (check for crash/activation error)' -ForegroundColor Yellow
    }
}

Write-Host ''
Write-Host '━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━' -ForegroundColor Green
Write-Host '  Packaged dev run ready.' -ForegroundColor Green
Write-Host "  Package  : $DevPkgName ($ver)" -ForegroundColor White
Write-Host "  Family   : $family" -ForegroundColor White
Write-Host "  Data root: $localState\TubaWinUi3" -ForegroundColor White
Write-Host '  Uninstall: Get-AppxPackage -Name tubawinui3.dev | Remove-AppxPackage' -ForegroundColor White
Write-Host '━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━' -ForegroundColor Green
Write-Host ''

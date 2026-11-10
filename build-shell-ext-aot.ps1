# 「文件占用查看」右键菜单处理程序（ShellExtension）NativeAOT 发布辅助脚本。
# 用途：定位 MSVC vcvarsall/vcvars64（VS2022 各版本/目录），在其环境下以
#   IlcUseEnvironmentalTools=true 发布 NativeAOT 原生 DLL（导出 DllGetClassObject），
#   兼容 vswhere 元数据缺失（仅装 BuildTools）的机器与 CI。
# 产物：<OutputDir>\TubaWinUi3.ShellExtension.dll（供 MSIX 清单 com:SurrogateServer 引用）
# 用法：
#   powershell -NoProfile -ExecutionPolicy Bypass -File build-shell-ext-aot.ps1 -Rid win-x64 -OutputDir <dir>
param(
    [Parameter(Mandatory = $true)]
    [string]$Rid,
    [Parameter(Mandatory = $true)]
    [string]$OutputDir,
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$shellExtProj = Join-Path $repoRoot "TubaWinUI3.ShellExtension\TubaWinUi3.ShellExtension.csproj"

# 从 RID 解析目标架构（win-x64 -> x64，win-x86 -> x86，win-arm64 -> arm64）。
$targetArch = if ($Rid -match '^win-(x64|x86|arm64)$') { $Matches[1] } else { 'x64' }

# 清除可能残留的 Platform 环境变量（外层构建泄漏的 Platform 会被 MSBuild 当作属性导入，
# 导致 -r win-arm64 与 x64 平台不匹配 NETSDK1032）。
Remove-Item Env:Platform -ErrorAction SilentlyContinue

# 定位 vcvars：优先 vcvarsall.bat（按目标架构选择本机/交叉工具链；arm64 必须用 arm64 的
# LIB/link.exe），找不到则退回 vcvars64.bat（仅 x64）。覆盖 VS2022 各版本与自定义目录。
# 也允许通过环境变量 VCVARS64_PATH 显式指定。
$vcvarsBases = @()
foreach ($vsEdition in @("BuildTools", "Enterprise", "Professional", "Community")) {
    foreach ($base in @("${env:ProgramFiles(x86)}\Microsoft Visual Studio\2022",
                        "${env:ProgramFiles}\Microsoft Visual Studio\2022")) {
        if ($base -and (Test-Path $base)) {
            $vcvarsBases += (Join-Path $base (Join-Path $vsEdition "VC\Auxiliary\Build"))
        }
    }
}
$vcvarsAll = $vcvarsBases | ForEach-Object { Join-Path $_ "vcvarsall.bat" } | Where-Object { Test-Path $_ } | Select-Object -First 1
$vcvars64 = $vcvarsBases | ForEach-Object { Join-Path $_ "vcvars64.bat" } | Where-Object { Test-Path $_ } | Select-Object -First 1
if ($env:VCVARS64_PATH) {
    if ([IO.Path]::GetFileName($env:VCVARS64_PATH) -eq "vcvarsall.bat") { $vcvarsAll = $env:VCVARS64_PATH }
    else { $vcvars64 = $env:VCVARS64_PATH }
}

# 显式固定 Platform/PlatformTarget 与 RID 一致，分号合并为单个 -p: 参数（防 CI 参数拼接错误）。
$baseSwitches = "-p:Platform=$targetArch;PlatformTarget=$targetArch"
New-Item -ItemType Directory -Force -Path $OutputDir | Out-Null

if ($vcvarsAll) {
    Write-Host "使用 vcvarsall ($targetArch): $vcvarsAll"
    $cmd = "call `"$vcvarsAll`" $targetArch >nul 2>&1 && dotnet publish `"$shellExtProj`" -c $Configuration -r $Rid $baseSwitches -p:IlcUseEnvironmentalTools=true -o `"$OutputDir`""
    cmd /c $cmd
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败 (exit $LASTEXITCODE)" }
}
elseif ($vcvars64) {
    Write-Host "使用 vcvars64: $vcvars64"
    $cmd = "call `"$vcvars64`" >nul 2>&1 && dotnet publish `"$shellExtProj`" -c $Configuration -r $Rid $baseSwitches -p:IlcUseEnvironmentalTools=true -o `"$OutputDir`""
    cmd /c $cmd
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败 (exit $LASTEXITCODE)" }
}
else {
    Write-Host "未找到 vcvars，尝试直接发布（依赖现有环境）"
    & dotnet publish $shellExtProj -c $Configuration -r $Rid $baseSwitches -o $OutputDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish 失败 (exit $LASTEXITCODE)" }
}

$dll = Join-Path $OutputDir "TubaWinUi3.ShellExtension.dll"
if (-not (Test-Path $dll)) { throw "ShellExtension 产物不存在: $dll" }
Write-Host "ShellExtension AOT 产物: $dll ($((Get-Item $dll).Length) bytes)"

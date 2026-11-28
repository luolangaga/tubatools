# 裁剪发布目录里按架构冗余 / 已停用 / 受版权限制的工具文件。
#
# 只处理 <Root>\Tools，不碰应用运行时。可安全重复执行：
#   .\build-prune-tools.ps1 -Root publish_x64, publish_x64_installer -Arch x64
#   .\build-prune-tools.ps1 -Root publish_arm64 -Arch arm64
#
# 规则刻意使用“明确文件路径 + 架构白名单”，不做 *64* / *32* 这类通配，
# 避免误删 ARM64 模拟层仍需的 x64 工具（例如 FurMark、DiskGenius）。

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, ValueFromRemainingArguments = $true)]
    [string[]]$Root,

    [Parameter(Mandatory = $true)]
    [ValidateSet('x64', 'arm64', 'x86')]
    [string]$Arch
)

$ErrorActionPreference = 'Stop'

function Remove-ToolPath {
    param(
        [string]$ToolsRoot,
        [string]$RelativePath
    )

    $path = Join-Path $ToolsRoot $RelativePath
    if (-not (Test-Path -LiteralPath $path)) {
        return [pscustomobject]@{ Bytes = 0L; Items = 0 }
    }

    $item = Get-Item -LiteralPath $path
    $bytes = if ($item.PSIsContainer) {
        [long](Get-ChildItem -LiteralPath $path -Recurse -File -ErrorAction SilentlyContinue |
            Measure-Object -Property Length -Sum).Sum
    } else {
        [long]$item.Length
    }

    Remove-Item -LiteralPath $path -Recurse -Force
    return [pscustomobject]@{ Bytes = $bytes; Items = 1 }
}

$rootsSeen = 0
$totalBytes = 0L
$totalItems = 0

foreach ($item in $Root) {
    $path = [System.IO.Path]::GetFullPath($item)
    if (-not (Test-Path -LiteralPath $path -PathType Container)) {
        Write-Warning "跳过不存在的发布目录: $path"
        continue
    }

    $tools = Join-Path $path 'Tools'
    if (-not (Test-Path -LiteralPath $tools -PathType Container)) {
        Write-Warning "跳过没有 Tools 目录的发布目录: $path"
        continue
    }

    $rootsSeen++
    $bytes = 0L
    $items = 0

    # 已从仓库下架的工具：兼容旧 publish / 旧安装目录的清理。
    $retired = @(
        '综合检测\speccy',
        '硬盘工具\Defraggler',
        '其他工具\天梯图'
    )

    # 与架构无关的冗余：Shizuku 主题大图/语音、DDU 调试符号、procexp alpha。
    $common = @(
        '其他工具\procexp\procexp64a.exe',
        '显卡工具\DDU\Display Driver Uninstaller.pdb',
        '硬盘工具\CrystalDiskInfo\CdiResource\themes\Shizuku\ShizukuBackground-300.png',
        '硬盘工具\CrystalDiskInfo\CdiResource\voice\ShizukuVoice.dll'
    )

    foreach ($relative in $retired + $common) {
        $result = Remove-ToolPath $tools $relative
        $bytes += $result.Bytes
        $items += $result.Items
    }

    # Shizuku 主题的版权图很小，但和背景图同属一个受限制主题，一并清掉。
    $shizukuTheme = Join-Path $tools '硬盘工具\CrystalDiskInfo\CdiResource\themes\Shizuku'
    if (Test-Path -LiteralPath $shizukuTheme -PathType Container) {
        foreach ($file in Get-ChildItem -LiteralPath $shizukuTheme -File -Filter 'ShizukuCopyright-*.png' -ErrorAction SilentlyContinue) {
            $bytes += $file.Length
            $items++
            Remove-Item -LiteralPath $file.FullName -Force
        }
    }

    # DDU 只保留简繁中文和英文；旧规则按目录名 Languages 判断，
    # 实际目录是 settings\Languages，按实际路径修。
    $dduLanguages = Join-Path $tools '显卡工具\DDU\settings\Languages'
    if (Test-Path -LiteralPath $dduLanguages -PathType Container) {
        foreach ($file in Get-ChildItem -LiteralPath $dduLanguages -File -Filter '*.xml' -ErrorAction SilentlyContinue) {
            if ($file.Name -match 'Chinese|English') { continue }
            $bytes += $file.Length
            $items++
            Remove-Item -LiteralPath $file.FullName -Force
        }
    }

    # 按架构剔除本机不可能运行的二进制。ARM64 上刻意保留 x64 工具，
    # 因为 Windows 11 ARM64 的 x64 模拟能运行它们；只清理已有原生 ARM64 版本的重复项。
    $x64Only = @(
        '综合检测\hwinfo\HWiNFO_ARM64.exe',
        '综合检测\HWMonitor\hwmonitor_arm64.exe',
        '处理器工具\CPUZ\cpuz_arm64.exe',
        '处理器工具\CPUZ\cpuz_x32.exe',
        '硬盘工具\CrystalDiskInfo\DiskInfoA64.exe',
        '硬盘工具\CrystalDiskInfo\DiskInfo32.exe',
        '其他工具\procexp\procexp.exe',
        '其他工具\ventoy\altexe\Ventoy2Disk_ARM.exe',
        '其他工具\ventoy\altexe\Ventoy2Disk_ARM64.exe'
    )

    $arm64Only = @(
        '综合检测\hwinfo\HWiNFO64.exe',
        '综合检测\HWMonitor\HWMonitor_x64.exe',
        '处理器工具\CPUZ\cpuz_x64.exe',
        '处理器工具\CPUZ\cpuz_x32.exe',
        '硬盘工具\CrystalDiskInfo\DiskInfo64.exe',
        '硬盘工具\CrystalDiskInfo\DiskInfo32.exe',
        '其他工具\procexp\procexp.exe',
        '其他工具\ventoy\altexe\Ventoy2Disk_X64.exe',
        '其他工具\ventoy\altexe\Ventoy2Disk_ARM.exe'
    )

    $archList = switch ($Arch) {
        'x64' { $x64Only }
        'arm64' { $arm64Only }
        default { @() }
    }

    foreach ($relative in $archList) {
        $result = Remove-ToolPath $tools $relative
        $bytes += $result.Bytes
        $items += $result.Items
    }

    $totalBytes += $bytes
    $totalItems += $items
    Write-Host ("Tools 裁剪 [{0}] {1} -> 删除 {2} 项，释放 {3:N2} MB" -f $Arch, $path, $items, ($bytes / 1MB))
}

if ($rootsSeen -eq 0) {
    throw "没有任何带 Tools 目录的发布根: $($Root -join ', ')"
}

Write-Host ("Tools 裁剪完成 [{0}]: 删除 {1} 项，共释放 {2:N2} MB" -f $Arch, $totalItems, ($totalBytes / 1MB))

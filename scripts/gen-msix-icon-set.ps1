<#
.SYNOPSIS
    从 600×600 的 AppLogo.png 生成完整的 MSIX 应用图标资产（targetsize × 明暗主题变体）。

.DESCRIPTION
    Windows 在任务栏 / 开始菜单「所有应用」/ 搜索结果 / Alt+Tab 等处，按当前缩放比例请求精确像素大小的图标：
    16/20/24/30/32/36/40/48/60/64/72/80/96/256。若缺少对应尺寸的
    `targetsize-N_altform-unplated.png` / `targetsize-N_altform-lightunplated.png`，
    Windows 会退回带底板的 `scale-*` 磁贴资产，在图标后面画一块系统强调色（默认蓝色）方块。

    因此这里为 `Square44x44Logo` 与 `AppList` 两个前缀各生成 14 个尺寸 × 3 个主题变体。
    全部保持透明背景，图案取自已发布的 600px 图稿，逐级高质量缩放。
#>
param(
    [string]$Source = (Join-Path $PSScriptRoot '..\TubaWinUi3.WinUI3\Assets\AppLogo.png'),
    [string]$TargetDir = (Join-Path $PSScriptRoot '..\TubaWinUi3.WinUI3\Assets')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$Sizes = 16, 20, 24, 30, 32, 36, 40, 48, 60, 64, 72, 80, 96, 256
$Prefixes = 'Square44x44Logo', 'AppList'
# 空后缀 = 默认/深色（带底板资产，图案本身仍透明）；unplated = 深色任务栏；lightunplated = 浅色任务栏
$Variants = '', '_altform-unplated', '_altform-lightunplated'

$src = [System.Drawing.Bitmap]::FromFile((Resolve-Path -LiteralPath $Source))
try {
    foreach ($prefix in $Prefixes) {
        foreach ($size in $Sizes) {
            foreach ($variant in $Variants) {
                $out = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
                $g = [System.Drawing.Graphics]::FromImage($out)
                try {
                    $g.Clear([System.Drawing.Color]::Transparent)
                    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
                    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
                    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
                    $g.DrawImage($src, (New-Object System.Drawing.Rectangle 0, 0, $size, $size))
                } finally { $g.Dispose() }

                $path = Join-Path $TargetDir ("{0}.targetsize-{1}{2}.png" -f $prefix, $size, $variant)
                $out.Save($path, [System.Drawing.Imaging.ImageFormat]::Png)
                $out.Dispose()
            }
        }
        Write-Host "generated $($Sizes.Count * $Variants.Count) assets for $prefix"
    }
} finally {
    $src.Dispose()
}

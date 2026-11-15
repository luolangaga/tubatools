<#
.SYNOPSIS
  生成 README 顶部的本地 SVG 徽章（中英双语）。

.DESCRIPTION
  GitHub 会把 README 里的外部图片统一走 camo.githubusercontent.com 中转，
  该域名在国内网络经常超时，导致 shields.io 徽章整行显示为空白。
  因此徽章改为仓库内置 SVG：外观与 shields 的 flat-square 一致，
  但不依赖任何境外图床，镜像站与国内网络都能正常显示。

  动态数值（最新版本号、累计下载量）从 GitHub API 读取，
  发布新版本后重新运行本脚本即可刷新：

      pwsh -File scripts/update-readme-badges.ps1

  也可离线指定数值（不联网）：

      pwsh -File scripts/update-readme-badges.ps1 -ReleaseTag v1.9.1 -Downloads 162556

.PARAMETER Repository
  仓库全名，默认 luolangaga/tubatools。

.PARAMETER ReleaseTag
  最新版本号，省略则从 GitHub API 读取。

.PARAMETER Downloads
  累计下载量，省略则从 GitHub API 读取。

.NOTES
  只写 images/readme/badges/ 下的 SVG，不改动 README 文本。
#>
[CmdletBinding()]
param(
    [string]$Repository = 'luolangaga/tubatools',
    [string]$ReleaseTag,
    [long]$Downloads = -1
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing

$root = Split-Path -Parent $PSScriptRoot
$badgeDir = Join-Path $root 'images\readme\badges'
if (-not (Test-Path -LiteralPath $badgeDir)) {
    New-Item -ItemType Directory -Path $badgeDir | Out-Null
}

# ---------- 动态数值 ----------
function Invoke-GitHubJson([string]$Url) {
    $raw = curl.exe -sS --max-time 20 -H 'User-Agent: tuba-badge-builder' $Url
    if ($LASTEXITCODE -ne 0 -or -not $raw) { throw "请求失败：$Url" }
    return ($raw | Out-String | ConvertFrom-Json)
}

if (-not $ReleaseTag -or $Downloads -lt 0) {
    Write-Host '读取 GitHub API ...'
    $latest = Invoke-GitHubJson "https://api.github.com/repos/$Repository/releases/latest"
    if (-not $ReleaseTag) { $ReleaseTag = $latest.tag_name }
    if ($Downloads -lt 0) {
        $releases = Invoke-GitHubJson "https://api.github.com/repos/$Repository/releases?per_page=100"
        $sum = 0
        foreach ($r in $releases) { foreach ($a in $r.assets) { $sum += $a.download_count } }
        $Downloads = $sum
    }
}
if (-not $ReleaseTag) { $ReleaseTag = 'latest' }
if ($Downloads -lt 0) { $Downloads = 0 }

function Format-DownloadsCn([long]$n) {
    if ($n -ge 10000) { return ('{0:0.0}万' -f ($n / 10000)) }
    return "$n"
}
function Format-DownloadsEn([long]$n) {
    if ($n -ge 1000000) { return ('{0:0.0}M' -f ($n / 1000000)) }
    if ($n -ge 1000) { return ('{0:0}k' -f [Math]::Round($n / 1000, 0) ) }
    return "$n"
}

# ---------- 文本度量（Verdana 11px，与 shields flat-square 一致）----------
$measureFont = New-Object System.Drawing.Font('Verdana', 11, [System.Drawing.FontStyle]::Regular, [System.Drawing.GraphicsUnit]::Pixel)
$measureBmp = New-Object System.Drawing.Bitmap 1, 1
$measureGfx = [System.Drawing.Graphics]::FromImage($measureBmp)
$measureFmt = [System.Drawing.StringFormat]::GenericTypographic

function Get-TextWidth([string]$text) {
    $size = $measureGfx.MeasureString($text, $measureFont, 4000, $measureFmt)
    return [int][Math]::Round($size.Width, 0, [MidpointRounding]::AwayFromZero)
}

function ConvertTo-XmlText([string]$s) {
    return [System.Security.SecurityElement]::Escape($s)
}

# ---------- 徽章几何：高 20，左右内边距各 5（shields flat-square）----------
function New-BadgeSvg {
    param(
        [string]$Label,
        [string]$Value,
        [string]$ValueFill,
        [string]$ValueFillDark,
        [string]$ValueInkDark
    )
    $labelW = (Get-TextWidth $Label) + 10
    $valueW = (Get-TextWidth $Value) + 10
    $w = $labelW + $valueW
    $labelLen = $labelW - 10
    $valueLen = $valueW - 10
    $labelCx = [int](($labelW / 2) * 10)
    $valueCx = [int](($labelW + $valueW / 2) * 10)
    $title = ConvertTo-XmlText "$Label：$Value"

    $font = 'Verdana,Geneva,DejaVu Sans,Microsoft YaHei,PingFang SC,sans-serif'

    return @"
<svg xmlns="http://www.w3.org/2000/svg" width="$w" height="20" viewBox="0 0 $w 20" role="img" aria-label="$title">
  <title>$title</title>
  <style>
    svg { --label-bg:#5b624a; --label-ink:#ffffff; --value-bg:$ValueFill; --value-ink:#333333; }
    @media (prefers-color-scheme: dark) { svg { --value-bg:$ValueFillDark; --value-ink:$ValueInkDark; } }
  </style>
  <g shape-rendering="crispEdges">
    <rect width="$labelW" height="20" fill="var(--label-bg)"/>
    <rect x="$labelW" width="$valueW" height="20" fill="var(--value-bg)"/>
  </g>
  <g fill="var(--label-ink)" text-anchor="middle" font-family="$font" text-rendering="geometricPrecision" font-size="110">
    <text x="$labelCx" y="140" textLength="$($labelLen * 10)" transform="scale(.1)">$(ConvertTo-XmlText $Label)</text>
    <text x="$valueCx" y="140" textLength="$($valueLen * 10)" transform="scale(.1)" fill="var(--value-ink)">$(ConvertTo-XmlText $Value)</text>
  </g>
</svg>
"@
}

# ---------- 徽章清单 ----------
# fill 取自旧版 shields 徽章的配色（labelColor=5b624a），保持视觉不变。
$sage = @{ Light = '#a7bc77'; Dark = '#4a5a35'; Ink = '#eef4e6' }
$sky  = @{ Light = '#a8d6d9'; Dark = '#2f4b4e'; Ink = '#e3f1f2' }
$leaf = @{ Light = '#b8c69a'; Dark = '#455138'; Ink = '#eef4e6' }

$specs = @(
    @{
        File  = 'release-cn.svg'
        Label = '最新版'
        Value = $ReleaseTag
        Color = $sage
    },
    @{
        File  = 'downloads-cn.svg'
        Label = '下载量'
        Value = (Format-DownloadsCn $Downloads)
        Color = $sage
    },
    @{
        File  = 'platform-cn.svg'
        Label = '平台'
        Value = 'Windows 10 1809+'
        Color = $sky
    },
    @{
        File  = 'dotnet-cn.svg'
        Label = '.NET'
        Value = '10'
        Color = $leaf
    },
    @{
        File  = 'toolkit-cn.svg'
        Label = '收录工具'
        Value = '89 款'
        Color = $leaf
    },
    @{
        File  = 'license-cn.svg'
        Label = '许可证'
        Value = 'GPL-3.0'
        Color = $sky
    },
    @{
        File  = 'release-en.svg'
        Label = 'Latest'
        Value = $ReleaseTag
        Color = $sage
    },
    @{
        File  = 'downloads-en.svg'
        Label = 'Downloads'
        Value = (Format-DownloadsEn $Downloads)
        Color = $sage
    },
    @{
        File  = 'platform-en.svg'
        Label = 'Platform'
        Value = 'Windows 10 1809+'
        Color = $sky
    },
    @{
        File  = 'dotnet-en.svg'
        Label = '.NET'
        Value = '10'
        Color = $leaf
    },
    @{
        File  = 'toolkit-en.svg'
        Label = 'Bundled Tools'
        Value = '89 apps'
        Color = $leaf
    },
    @{
        File  = 'license-en.svg'
        Label = 'License'
        Value = 'GPL-3.0'
        Color = $sky
    }
)

$utf8 = New-Object System.Text.UTF8Encoding($false)
foreach ($spec in $specs) {
    $color = $spec.Color
    $svg = New-BadgeSvg -Label $spec.Label -Value $spec.Value -ValueFill $color.Light -ValueFillDark $color.Dark -ValueInkDark $color.Ink
    $path = Join-Path $badgeDir $spec.File
    [System.IO.File]::WriteAllText($path, $svg.Replace("`r`n", "`n") + "`n", $utf8)
    Write-Host ("  {0,-16} {1}：{2}" -f $spec.File, $spec.Label, $spec.Value)
}

$measureGfx.Dispose()
$measureBmp.Dispose()
$measureFont.Dispose()

Write-Host ""
Write-Host "完成：版本 $ReleaseTag，累计下载 $Downloads，输出目录 images/readme/badges/"

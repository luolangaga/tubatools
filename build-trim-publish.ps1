# 裁剪 dotnet publish 输出里的 WinUI MUI 语言资源。
#
# WinUI（Microsoft.ui.xaml.dll / Microsoft.UI.Xaml.Phone.dll）会按系统语言加载
# 同目录的 <lang>\*.dll.mui。应用本身只提供 zh-Hans / zh-Hant / en 三种界面语言，
# 其余 80 多个语言目录纯粹是死重量（每个发布目录约 4 MB）。
#
# 用法：
#   .\build-trim-publish.ps1 -Root publish_x64, publish_x64_installer
#   .\build-trim-publish.ps1 -Root publish_x64 -KeepLanguagePrefixes zh,en
param(
    [Parameter(Mandatory = $true, ValueFromRemainingArguments = $true)]
    [string[]]$Root,

    # 保留的语言前缀（目录名以 "-" 分隔的第一段，例如 zh-Hans -> zh）。
    [string[]]$KeepLanguagePrefixes = @('zh', 'en')
)

$ErrorActionPreference = 'Stop'

# 语言目录形如 en-us / zh-Hans / sr-Cyrl-BA / ca-Es-VALENCIA；
# 只处理确实是 MUI 资源（内含 *.mui）的目录，避免误删 amd64 / Data 之类的目录。
$localePattern = '^[a-z]{2,3}(-[A-Za-z0-9]+)*$'

$totalDirs = 0
$totalBytes = 0
$rootsSeen = 0

foreach ($item in $Root) {
    $path = [System.IO.Path]::GetFullPath($item)
    if (-not (Test-Path -LiteralPath $path -PathType Container)) {
        Write-Warning "跳过不存在的发布目录: $path"
        continue
    }
    $rootsSeen++

    $dirs = Get-ChildItem -LiteralPath $path -Directory -ErrorAction SilentlyContinue |
        Where-Object {
            $_.Name -cmatch $localePattern -and
            (Get-ChildItem -LiteralPath $_.FullName -File -Filter '*.mui' -ErrorAction SilentlyContinue | Select-Object -First 1)
        }

    $removed = 0
    $bytes = 0L
    foreach ($dir in $dirs) {
        $prefix = $dir.Name.Split('-')[0]
        if ($KeepLanguagePrefixes -contains $prefix) { continue }

        $size = (Get-ChildItem -LiteralPath $dir.FullName -Recurse -File -ErrorAction SilentlyContinue |
            Measure-Object -Property Length -Sum).Sum
        if (-not $size) { $size = 0 }

        Remove-Item -LiteralPath $dir.FullName -Recurse -Force
        $removed++
        $bytes += $size
    }

    $totalDirs += $removed
    $totalBytes += $bytes
    Write-Host ("MUI 裁剪: {0} -> 删除 {1} 个语言目录，释放 {2:N2} MB" -f $path, $removed, ($bytes / 1MB))
}

if ($rootsSeen -eq 0) {
    throw "没有任何有效的发布目录: $($Root -join ', ')"
}

Write-Host ("MUI 裁剪完成: 删除 {0} 个目录，共释放 {1:N2} MB" -f $totalDirs, ($totalBytes / 1MB))

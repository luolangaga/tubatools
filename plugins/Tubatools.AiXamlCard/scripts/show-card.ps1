param(
    [string]$Xaml = "",
    [string]$XamlFile = "",
    [string]$Code = "",
    [string]$CodeFile = "",
    [string]$Title = "AI 卡片",
    [string]$Json = "",
    [string]$JsonFile = ""
)

# 把卡片交给图吧工具箱立刻渲染。三种用法：
#   1) 单张内联：  show-card.ps1 -Xaml '<Border .../>' [-Code '...'] [-Title 标题]
#   2) 单张文件：  show-card.ps1 -XamlFile card.xaml [-CodeFile card.cs] [-Title 标题]
#   3) 多张整批：  show-card.ps1 -JsonFile cards.json
#      （JSON 形如：{"title":"总标题","cards":[{"title":"A","xaml":"...","code":"..."}]}）
$ErrorActionPreference = "Stop"

function Find-RequestDirs {
    $dirs = @()
    $data = $env:LOCALAPPDATA
    if (-not $data) { throw "找不到 LOCALAPPDATA。" }
    $dirs += (Join-Path $data "TubaWinUi3\PluginData\tubatools.aixamlcard\requests")
    $packages = Join-Path $data "Packages"
    if (Test-Path $packages) {
        Get-ChildItem $packages -Directory -Filter "tubawinui3*" -ErrorAction SilentlyContinue | ForEach-Object {
            $dirs += (Join-Path $_.FullName "LocalState\TubaWinUi3\PluginData\tubatools.aixamlcard\requests")
        }
    }
    return $dirs
}

function Read-Text([string]$inline, [string]$file, [string]$what) {
    if (-not [string]::IsNullOrWhiteSpace($inline)) { return $inline }
    if ($file -and (Test-Path $file)) { return (Get-Content $file -Raw -Encoding UTF8) }
    if ($file) { throw "找不到${what}文件：$file" }
    return ""
}

# ---- 组请求体 ----
$requestId = [guid]::NewGuid().ToString("N")
if (-not [string]::IsNullOrWhiteSpace($Json) -or ($JsonFile -and (Test-Path $JsonFile))) {
    # 整批模式：原样提交，校验交给插件（它才是契约的唯一权威）。
    $payloadText = Read-Text $Json $JsonFile "JSON"
    if ([string]::IsNullOrWhiteSpace($payloadText)) { throw "JSON 内容是空的。" }
} else {
    $xamlText = Read-Text $Xaml $XamlFile "XAML"
    if ([string]::IsNullOrWhiteSpace($xamlText)) { throw "需要 -Xaml / -XamlFile，或 -Json / -JsonFile。" }
    $codeText = Read-Text $Code $CodeFile "代码"
    $payload = [ordered]@{ id = $requestId; title = $Title; xaml = $xamlText; code = $codeText }
    $payloadText = $payload | ConvertTo-Json -Depth 4
}

$candidates = Find-RequestDirs
$directory = $candidates | Where-Object { Test-Path $_ } | Select-Object -First 1
if (-not $directory) { $directory = $candidates[0] }
New-Item -ItemType Directory -Path $directory -Force | Out-Null

$path = Join-Path $directory "$requestId.json"
$temporary = "$path.tmp"
# 先写临时文件再改名：插件监听 *.json，避免读到半个 JSON。
[System.IO.File]::WriteAllText($temporary, $payloadText, (New-Object System.Text.UTF8Encoding($false)))
Move-Item $temporary $path -Force

"已提交卡片：$Title（$requestId）"

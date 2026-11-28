# 把「带底板」的磁贴图标（Square150/Square71/Square44 基础名 + scale-* 变体、Wide、Store）
# 重做成满版不透明图：底色 #8B5CC8 + 放大到出血的圆角方标，
# 这样系统在图标后面画 BackgroundColor 底板时完全看不见（不会再出现蓝边/紫边）。
param(
    [string]$AssetsDir = (Join-Path $PSScriptRoot '..\TubaWinUi3.WinUI3\Assets'),
    [string]$Source = 'AppLogo.png',
    [string]$PlateColor = '#8B5CC8'
)

Add-Type -AssemblyName System.Drawing

$srcPath = Join-Path $AssetsDir $Source
if (-not (Test-Path $srcPath)) { throw "source missing: $srcPath" }

$plate = [System.Drawing.ColorTranslator]::FromHtml($PlateColor)
$src = [System.Drawing.Bitmap]::FromFile($srcPath)

function New-PlatedTile {
    param([System.Drawing.Bitmap]$art, [int]$w, [int]$h, [System.Drawing.Color]$bg, [string]$out)

    $bmp = New-Object System.Drawing.Bitmap $w, $h, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.Clear($bg)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality

    # 正方形：按高度铺满并放大 22%（把原图的上下留白裁掉，让圆角方标贴近四边）
    if ($w -eq $h) {
        $scale = ($h / [double]$art.Height) * 1.22
        $dw = [int][Math]::Ceiling($art.Width * $scale)
        $dh = [int][Math]::Ceiling($art.Height * $scale)
        $dx = [int](($w - $dw) / 2)
        $dy = [int](($h - $dh) / 2)
        $g.DrawImage($art, $dx, $dy, $dw, $dh)
    }
    else {
        # 宽幅 / 小方块：居中放，高度占 92%
        $scale = ($h / [double]$art.Height) * 0.92
        $dw = [int][Math]::Ceiling($art.Width * $scale)
        $dh = [int][Math]::Ceiling($art.Height * $scale)
        $dx = [int](($w - $dw) / 2)
        $dy = [int](($h - $dh) / 2)
        $g.DrawImage($art, $dx, $dy, $dw, $dh)
    }

    $g.Dispose()
    $bmp.Save($out, [System.Drawing.Imaging.ImageFormat]::Png)
    $bmp.Dispose()
}

$targets = @()
foreach ($name in 'Square44x44Logo', 'Square150x150Logo', 'Square71x71Logo') {
    $targets += @{ Name = "$name.png"; W = @{ 'Square44x44Logo' = 44; 'Square150x150Logo' = 150; 'Square71x71Logo' = 71 }[$name]; H = @{ 'Square44x44Logo' = 44; 'Square150x150Logo' = 150; 'Square71x71Logo' = 71 }[$name] }
    foreach ($scale in 100, 200, 300, 400) {
        $base = @{ 'Square44x44Logo' = 44; 'Square150x150Logo' = 150; 'Square71x71Logo' = 71 }[$name]
        $px = [int][Math]::Round($base * $scale / 100)
        $targets += @{ Name = "$name.scale-$scale.png"; W = $px; H = $px }
    }
}
foreach ($name in 'Wide310x150Logo') {
    $targets += @{ Name = "$name.png"; W = 310; H = 150 }
    foreach ($scale in 100, 200, 300, 400) {
        $targets += @{ Name = "$name.scale-$scale.png"; W = [int](310 * $scale / 100); H = [int](150 * $scale / 100) }
    }
}
$targets += @{ Name = 'StoreLogo.png'; W = 50; H = 50 }
foreach ($scale in 100, 200) { $targets += @{ Name = "StoreLogo.scale-$scale.png"; W = [int](50 * $scale / 100); H = [int](50 * $scale / 100) } }

$count = 0
foreach ($t in $targets) {
    $out = Join-Path $AssetsDir $t.Name
    if (-not (Test-Path $out)) { continue }
    New-PlatedTile -art $src -w $t.W -h $t.H -bg $plate -out $out
    $count++
}
$src.Dispose()
"regenerated $count plated tiles ($PlateColor)"

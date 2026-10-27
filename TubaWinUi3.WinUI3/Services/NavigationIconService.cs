using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace TubaWinUi3.Services;

/// <summary>侧栏专用的彩色填充图标，与内置工具共用 128px 渐变风格。</summary>
internal static class NavigationIconService
{
    internal static string AssetFor(string category)
    {
        var asset = category switch
        {
            "home" => "NavIcons/home",
            "hardware" => "HomeTiles/hardware",
            "builtin" => "NavIcons/toolbox",
            "benchmark" => "BuiltinIcons/performance-benchmark",
            "community" => "NavIcons/community",
            "settings" => "NavIcons/settings",
            _ => null
        };
        if (asset is not null) return $"Assets/{asset}.svg";

        var key = category switch
        {
            var c when c.Contains("处理器") => "cpu",
            var c when c.Contains("显卡") => "gpu",
            var c when c.Contains("显示器") => "display",
            var c when c.Contains("硬盘") => "disk",
            var c when c.Contains("内存") => "memory",
            var c when c.Contains("外设") => "peripherals",
            var c when c.Contains("声卡") => "sound",
            var c when c.Contains("网卡") || c.Contains("网络") => "network",
            var c when c.Contains("综合检测") || c.Contains("硬件") => "diagnostics",
            var c when c.Contains("综合") || c.Contains("实用") || c.Contains("系统") => "toolbox",
            _ => "other"
        };
        if (category.Contains("游戏")) return "Assets/BuiltinIcons/game-monitor.svg";
        if (category.Contains("烤鸡")) return "Assets/BuiltinIcons/stress-test.svg";
        return $"Assets/NavIcons/{key}.svg";
    }

    // 每个导航项需要自己的 IconElement；不缓存 UI 对象，避免刷新后元素重挂载。
    internal static IconElement Create(string category, string fallbackGlyph, bool highContrast)
    {
        var customGlyph = AppSettings.Get($"CategoryGlyph_{category}");
        if (!string.IsNullOrWhiteSpace(customGlyph)) return new FontIcon { Glyph = customGlyph };
        if (highContrast) return new FontIcon { Glyph = fallbackGlyph };
        return new ImageIcon
        {
            Width = 24,
            Height = 24,
            // NavigationView 的 IconBox 会固定缩放到 16px；只改 Width/Height 不会变大。
            // 围绕中心放大绘制，保留原有导航文字、行高和收起状态的图标中心。
            RenderTransformOrigin = new Windows.Foundation.Point(.5, .5),
            RenderTransform = new ScaleTransform { ScaleX = 1.25, ScaleY = 1.25 },
            Source = new SvgImageSource(new Uri("ms-appx:///" + AssetFor(category)))
        };
    }
}

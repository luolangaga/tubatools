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
            "community" => "BuiltinIcons/plugins",
            "ai-assistant" => "BuiltinIcons/ai-assistant",
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
        // 图标工坊的自定义图片优先；高对比度下图片一律让位给字体字形。
        var customImage = highContrast ? null : IconThemeService.GetNavigationCustomImage(category);
        if (customImage is not null && BuiltinIconService.LoadAsset(customImage) is { } customSource)
            return CreateImageIcon(customSource);

        var customGlyph = AppSettings.Get($"CategoryGlyph_{category}");
        if (!string.IsNullOrWhiteSpace(customGlyph)) return new FontIcon { Glyph = customGlyph };
        if (highContrast) return new FontIcon { Glyph = fallbackGlyph };

        switch (IconThemeService.Preset)
        {
            case PluginSdk.PluginIconPreset.FluentGlyph:
                return new FontIcon { Glyph = fallbackGlyph };
            case PluginSdk.PluginIconPreset.ClassicColor:
                var classic = IconStudioCatalog.ClassicNavigationImage(category);
                if (classic is not null && BuiltinIconService.LoadAsset(classic) is { } classicSource)
                    return CreateImageIcon(classicSource);
                return new FontIcon { Glyph = fallbackGlyph };
        }

        return CreateImageIcon(new SvgImageSource(new Uri("ms-appx:///" + AssetFor(category))));
    }

    internal static ImageIcon CreateImageIcon(ImageSource source) => new()
    {
        Width = 24,
        Height = 24,
        // NavigationView 的 IconBox 会固定缩放到 16px；只改 Width/Height 不会变大。
        // 围绕中心放大绘制，保留原有导航文字、行高和收起状态的图标中心。
        RenderTransformOrigin = new Windows.Foundation.Point(.5, .5),
        RenderTransform = new ScaleTransform { ScaleX = 1.25, ScaleY = 1.25 },
        Source = source
    };

    /// <summary>
    /// 为图标菜单复制一份 <see cref="IconElement"/>：同一个 IconElement 不能同时挂在
    /// NavigationView 与自绘图标栏上，按类型复制源而不是重复解析图标工坊/插件定制规则。
    /// 图片源无法复制时退回字体字形（高对比度路径同样依赖这个兜底）。
    /// </summary>
    internal static IconElement Clone(IconElement? icon, string fallbackGlyph)
    {
        switch (icon)
        {
            case FontIcon font:
                var fontClone = new FontIcon
                {
                    Glyph = font.Glyph,
                    FontSize = font.FontSize,
                    FontWeight = font.FontWeight,
                };
                if (font.Foreground is not null) fontClone.Foreground = font.Foreground;
                return fontClone;

            case ImageIcon image when CloneImageSource(image.Source) is { } source:
                var imageClone = new ImageIcon
                {
                    Width = image.Width,
                    Height = image.Height,
                    Source = source,
                    RenderTransformOrigin = image.RenderTransformOrigin,
                };
                if (image.RenderTransform is ScaleTransform scale)
                    imageClone.RenderTransform = new ScaleTransform
                    {
                        ScaleX = scale.ScaleX,
                        ScaleY = scale.ScaleY,
                        CenterX = scale.CenterX,
                        CenterY = scale.CenterY,
                    };
                return imageClone;

            case SymbolIcon symbol:
                return new SymbolIcon(symbol.Symbol);

            case BitmapIcon bitmap when bitmap.UriSource is not null:
                return new BitmapIcon
                {
                    UriSource = bitmap.UriSource,
                    ShowAsMonochrome = bitmap.ShowAsMonochrome,
                };
        }

        return new FontIcon { Glyph = fallbackGlyph };
    }

    /// <summary>图标菜单比 NavigationView 的选中图标更紧凑：统一缩到 22px，文字才有呼吸空间。</summary>
    internal static IconElement ForRail(IconElement? icon, string fallbackGlyph)
    {
        var clone = Clone(icon, fallbackGlyph);
        switch (clone)
        {
            case ImageIcon image:
                image.Width = 22;
                image.Height = 22;
                image.RenderTransform = null;
                image.RenderTransformOrigin = new Windows.Foundation.Point(0, 0);
                break;
            case FontIcon font:
                font.FontSize = 18;
                break;
        }
        return clone;
    }

    private static ImageSource? CloneImageSource(ImageSource? source)
    {
        if (source is SvgImageSource svg && svg.UriSource is not null)
            return new SvgImageSource(svg.UriSource);
        if (source is BitmapImage bitmap && bitmap.UriSource is not null)
            return new BitmapImage(bitmap.UriSource);
        return null;
    }
}

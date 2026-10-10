using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace TubaWinUi3.Services;

/// <summary>
/// 主题感知颜色，供代码构建 UI 的内置工具使用。
/// 值直接取自 WinUI 官方主题资源（自动适配明暗/高对比），不再维护独立调色板。
/// 手动选择浅色/深色（设置页）与系统不一致时，按 ThemeService 的生效主题解析资源字典，
/// 保证 C# 构建的界面与 XAML 部分同一主题。回退值仅在资源缺失（如单元测试宿主）时使用。
/// </summary>
internal static class ThemeColors
{
    // C# 的 Application.Resources 索引不会按窗口 RequestedTheme 解析 ThemeResource。
    // 用原生 XAML 解析器保留资源表达式，再令探针跟随窗口的实际主题。
    // 每个语义画刷只创建一个探针，主题/高对比切换由框架重求值。
    private static readonly Dictionary<string, Microsoft.UI.Xaml.Controls.Border> BrushProbes = new();

    internal static Brush ResolveBrush(string key, Color fallback)
    {
        try
        {
            if (Application.Current is null) return new SolidColorBrush(fallback);
            if (!BrushProbes.TryGetValue(key, out var probe))
            {
                probe = (Microsoft.UI.Xaml.Controls.Border)Microsoft.UI.Xaml.Markup.XamlReader.Load(
                    $"<Border xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Background='{{ThemeResource {key}}}' />");
                BrushProbes[key] = probe;
            }
            probe.RequestedTheme = App.MainWindow?.Content is FrameworkElement root
                ? root.ActualTheme : ThemeService.CurrentElementTheme;
            return probe.Background ?? new SolidColorBrush(fallback);
        }
        catch { return new SolidColorBrush(fallback); }
    }

    private static Color GetColor(string key, Color fallback)
    {
        if (key.EndsWith("Brush", StringComparison.Ordinal) && Application.Current is not null)
            return ResolveBrush(key, fallback) is SolidColorBrush resolved ? resolved.Color : fallback;
        var value = ResolveForEffectiveTheme(key) ?? ResolveByAppTheme(key);
        if (value is Color color) return color;
        if (value is SolidColorBrush brush) return brush.Color;
        return fallback;
    }

    /// <summary>
    /// 手动主题（ThemeService 显式选择）下，按生效主题在资源字典树中查找；
    /// 跟随系统 / 高对比时返回 null，交回框架默认解析（含系统高对比行为）。
    /// </summary>
    private static object? ResolveForEffectiveTheme(string key)
    {
        try
        {
            if (ThemeService.CurrentTheme == AppTheme.Default) return null;
            if (IsSystemHighContrast()) return null;
            var themeName = ThemeService.IsDarkEffective ? "Dark" : "Light";
            var resources = Application.Current?.Resources;
            return resources is null ? null : Lookup(resources, key, themeName);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>框架默认解析（与旧实现一致）：跟随应用主题/系统高对比。</summary>
    private static object? ResolveByAppTheme(string key)
    {
        try
        {
            var res = Application.Current?.Resources;
            if (res is not null && res.ContainsKey(key))
                return res[key];
        }
        catch
        {
            // 测试宿主等无 Application 环境
        }
        return null;
    }

    private static object? Lookup(ResourceDictionary dict, string key, string themeName)
    {
        if (dict.ThemeDictionaries.TryGetValue(themeName, out var themed) &&
            themed is ResourceDictionary themeDict &&
            themeDict.TryGetValue(key, out var themedValue) && themedValue is not null)
        {
            return themedValue;
        }

        if (dict.TryGetValue(key, out var direct) && direct is not null)
            return direct;

        foreach (var merged in dict.MergedDictionaries)
        {
            var value = Lookup(merged, key, themeName);
            if (value is not null) return value;
        }
        return null;
    }

    private static readonly Lazy<Windows.UI.ViewManagement.AccessibilitySettings?> _accessibility =
        new(() =>
        {
            try { return new Windows.UI.ViewManagement.AccessibilitySettings(); }
            catch { return null; }
        });

    internal static bool IsSystemHighContrast()
    {
        try { return _accessibility.Value?.HighContrast == true; }
        catch { return false; }
    }

    public static Color CardBg => GetColor("CardBackgroundFillColorDefaultBrush", Color.FromArgb(255, 249, 249, 249));
    public static Color BorderColor => GetColor("CardStrokeColorDefaultBrush", Color.FromArgb(255, 229, 229, 229));
    public static Color DimText => GetColor("TextFillColorTertiaryBrush", Color.FromArgb(255, 110, 110, 110));
    public static Color SecondaryText => GetColor("TextFillColorSecondaryBrush", Color.FromArgb(255, 70, 70, 70));
    public static Color PrimaryText => GetColor("TextFillColorPrimaryBrush", Color.FromArgb(255, 30, 30, 30));
    public static Color HeaderBg => GetColor("SubtleFillColorSecondaryBrush", Color.FromArgb(255, 245, 245, 245));
    public static Color RowHover => GetColor("SubtleFillColorTertiaryBrush", Color.FromArgb(255, 240, 240, 240));
    public static Color DisabledBg => GetColor("ControlFillColorDisabledBrush", Color.FromArgb(255, 240, 240, 240));
    public static Color KeyDefault => GetColor("ControlFillColorDefaultBrush", Color.FromArgb(255, 230, 230, 230));
    public static Color KeyBorder => GetColor("ControlStrokeColorDefaultBrush", Color.FromArgb(255, 200, 200, 200));
    public static Color KeyText => GetColor("TextFillColorPrimaryBrush", Color.FromArgb(255, 30, 30, 30));
    public static Color KeyboardBg => GetColor("LayerFillColorDefaultBrush", Color.FromArgb(255, 240, 240, 240));
    public static Color SubtleBg => GetColor("SubtleFillColorSecondaryBrush", Color.FromArgb(255, 240, 240, 240));
    public static Color SubtleBgHover => GetColor("SubtleFillColorTertiaryBrush", Color.FromArgb(255, 230, 230, 230));
    public static Color Separator => GetColor("DividerStrokeColorDefaultBrush", Color.FromArgb(255, 220, 220, 220));

    // 语义强调色：跟随系统强调色与语义色，替代此前的固定 Tailwind 调色板
    public static Color AccentBlue => GetColor("SystemAccentColor", Color.FromArgb(255, 96, 165, 250));
    public static Color AccentGreen => GetColor("SystemFillColorSuccessBrush", Color.FromArgb(255, 74, 222, 128));
    public static Color AccentOrange => GetColor("SystemFillColorCautionBrush", Color.FromArgb(255, 251, 191, 36));
    public static Color AccentRed => GetColor("SystemFillColorCriticalBrush", Color.FromArgb(255, 248, 113, 113));
    public static Color Neutral => GetColor("SystemFillColorNeutralBrush", Color.FromArgb(255, 142, 142, 142));

    // 图表系列色（对应 FluentTokens.xaml 的 DataSeries1-4Brush）
    public static Color Series1 => GetColor("DataSeries1Brush", Color.FromArgb(255, 96, 165, 250));
    public static Color Series2 => GetColor("DataSeries2Brush", Color.FromArgb(255, 124, 108, 240));
    public static Color Series3 => GetColor("DataSeries3Brush", Color.FromArgb(255, 74, 222, 128));
    public static Color Series4 => GetColor("DataSeries4Brush", Color.FromArgb(255, 251, 191, 36));

    /// <summary>历史紫色入口：统一折算到系统强调色系（DataSeries2），不再使用固定品牌色。</summary>
    public static Color AccentPurple => Series2;
}

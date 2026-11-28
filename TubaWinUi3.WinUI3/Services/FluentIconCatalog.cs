using Microsoft.UI.Xaml.Data;

namespace TubaWinUi3.Services;

internal static class FluentIconCatalog
{
    internal static string? AssetFor(string glyph) => glyph switch
    {
        "\uE713" => "settings", "\uE8B7" or "\uE8A7" => "folder",
        "\uE896" => "download", "\uE898" => "upload", "\uE8E5" => "import",
        "\uE777" => "palette", "\uE80F" => "home", "\uE774" => "network",
        "\uE790" => "palette", "\uE921" => "speed", "\uE945" => "info",
        "\uE946" => "info", "\uE8BD" => "tools", "\uE8FD" => "help",
        "\uE897" => "save", "\uE74E" => "save", "\uE8C8" => "copy",
        "\uE72C" => "refresh", "\uE710" => "add", "\uE721" => "search",
        "\uE77B" => "account", "\uE72E" => "shield", "\uE7F4" => "shield",
        "\uE701" => "wifi", "\uE787" => "phone", "\uE8A5" => "document",
        "\uE8F1" => "document", "\uE9D9" => "speed", "\uE91B" => "chip",
        "\uE950" => "chat", "\uE99A" => "chat", "\uE7C3" => "storage",
        "\uE7F8" => "storage", "\uE753" => "history", "\uE823" => "history",
        _ => null
    };
}

/// <summary>Title content remains a string: bindings and dialog reuse keep working.</summary>
public sealed class DialogTitleGlyphConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var title = value?.ToString() ?? "";
        return IconStudioCatalog.GlyphForDialog(IconStudioCatalog.DialogCategoryForTitle(title));
    }
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}

/// <summary>对话框标题的自定义/复古图片；默认预设返回空，让 FluentColorIcon 自己加载彩色矢量图。</summary>
public sealed class DialogTitleImageConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var title = value?.ToString() ?? "";
        var id = IconStudioCatalog.DialogCategoryForTitle(title);
        if (IconThemeService.GetDialogCustomImage(id) is { } custom) return custom;
        return IconThemeService.Preset switch
        {
            PluginSdk.PluginIconPreset.ClassicColor => IconStudioCatalog.ClassicDialogImage(id) ?? "",
            PluginSdk.PluginIconPreset.FluentGlyph => "",
            _ => ""
        };
    }
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}

/// <summary>Fluent 字形预设下关闭彩色矢量图，只显示 Segoe Fluent Icons 字形。</summary>
public sealed class DialogTitleGlyphOnlyConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var id = IconStudioCatalog.DialogCategoryForTitle(value?.ToString() ?? "");
        // 单图标自定义优先于预设：Fluent/Classic 只在使用预设时才强制字形。
        if (IconThemeService.GetDialogCustomImage(id) is not null) return false;
        if (IconThemeService.Preset == PluginSdk.PluginIconPreset.FluentGlyph) return true;
        if (IconThemeService.Preset != PluginSdk.PluginIconPreset.ClassicColor) return false;
        return IconStudioCatalog.ClassicDialogImage(id) is null;
    }
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}

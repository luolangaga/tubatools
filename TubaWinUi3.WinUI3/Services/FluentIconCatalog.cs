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
        if (Has("下载", "更新", "安装")) return "\uE896";
        if (Has("网络", "联机", "房间", "加入")) return "\uE774";
        if (Has("手机", "连接")) return "\uE787";
        if (Has("聊天", "AI", "模型", "提供商")) return "\uE950";
        if (Has("存储", "磁盘", "空间")) return "\uE7C3";
        if (Has("配置", "设置", "管理")) return "\uE713";
        if (Has("文件", "目录", "路径")) return "\uE8B7";
        if (Has("导出", "保存")) return "\uE74E";
        if (Has("导入", "添加")) return "\uE710";
        if (Has("安全", "权限", "拦截")) return "\uE72E";
        return "\uE946";
        bool Has(params string[] words) => words.Any(w => title.Contains(w, StringComparison.OrdinalIgnoreCase));
    }
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}

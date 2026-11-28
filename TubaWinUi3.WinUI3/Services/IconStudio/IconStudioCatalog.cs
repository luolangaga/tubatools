using TubaWinUi3.Services.Plugins;

namespace TubaWinUi3.Services;

/// <summary>可定制图标位置的静态目录：默认资产、复古资产、对话框分类。</summary>
internal static class IconStudioCatalog
{
    internal const string BuiltinGroup = "builtin";
    internal const string NavigationGroup = "navigation";
    internal const string DialogGroup = "dialog";

    internal sealed record DialogSlot(string Id, string ChineseTitle, string EnglishTitle, string Glyph, string ClassicRelativePath);

    internal static readonly IReadOnlyList<DialogSlot> DialogSlots =
    [
        new("download", "下载与更新", "Download & update", "\uE896", "Assets/IconStyles/Classic/Dialog/download.svg"),
        new("network", "网络与联机", "Network", "\uE774", "Assets/IconStyles/Classic/Builtin/network-optimize.svg"),
        new("phone", "手机连接", "Phone link", "\uE787", "Assets/IconStyles/Classic/Dialog/phone.svg"),
        new("ai", "AI 与聊天", "AI & chat", "\uE950", "Assets/IconStyles/Classic/Builtin/ai-assistant.svg"),
        new("storage", "存储与空间", "Storage", "\uE7C3", "Assets/IconStyles/Classic/Builtin/disk-health.svg"),
        new("settings", "设置与管理", "Settings", "\uE713", "Assets/IconStyles/Classic/Navigation/settings.svg"),
        new("file", "文件与目录", "Files & folders", "\uE8B7", "Assets/IconStyles/Classic/Dialog/file.svg"),
        new("export", "导出与保存", "Export & save", "\uE74E", "Assets/IconStyles/Classic/Dialog/export.svg"),
        new("import", "导入与添加", "Import & add", "\uE710", "Assets/IconStyles/Classic/Dialog/import.svg"),
        new("security", "安全与权限", "Security", "\uE72E", "Assets/IconStyles/Classic/Builtin/cert-block.svg"),
        new("info", "普通消息", "General message", "\uE946", "Assets/IconStyles/Classic/Dialog/info.svg")
    ];

    internal static DialogSlot? FindDialog(string id) =>
        DialogSlots.FirstOrDefault(s => s.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    /// <summary>标题 → 对话框图标分类。顺序与 <see cref="DialogTitleGlyphConverter"/> 保持一致。</summary>
    internal static string DialogCategoryForTitle(string title)
    {
        if (Has("下载", "更新", "安装", "download", "update", "install")) return "download";
        if (Has("网络", "联机", "房间", "加入", "network", "room", "join")) return "network";
        if (Has("手机", "连接", "phone", "link")) return "phone";
        if (Has("聊天", "AI", "模型", "提供商", "chat", "model", "provider")) return "ai";
        if (Has("存储", "磁盘", "空间", "storage", "disk", "space")) return "storage";
        if (Has("配置", "设置", "管理", "config", "setting", "manage")) return "settings";
        if (Has("文件", "目录", "路径", "file", "folder", "path")) return "file";
        if (Has("导出", "保存", "export", "save")) return "export";
        if (Has("导入", "添加", "import", "add")) return "import";
        if (Has("安全", "权限", "拦截", "security", "permission", "block")) return "security";
        return "info";

        bool Has(params string[] words) => words.Any(w => title.Contains(w, StringComparison.OrdinalIgnoreCase));
    }

    internal static string GlyphForDialog(string id) => FindDialog(id)?.Glyph ?? "\uE946";

    internal static string? DefaultDialogImage(string id)
    {
        var glyph = GlyphForDialog(id);
        var asset = FluentIconCatalog.AssetFor(glyph);
        return asset is null ? null : "ms-appx:///Assets/FluentIcons/" + asset + ".svg";
    }

    internal static string? ClassicDialogImage(string id)
    {
        var slot = FindDialog(id);
        return slot is null ? null : ExistingAppUri(slot.ClassicRelativePath);
    }

    /// <summary>内置工具默认图片；插件工具返回插件包内图标绝对路径。</summary>
    internal static string? DefaultBuiltinImage(string toolId)
    {
        if (string.IsNullOrWhiteSpace(toolId)) return null;
        if (BuiltinToolRegistry.GetById(toolId) is PluginXamlTool { IconPath: { } pluginIcon })
            return pluginIcon;
        return ExistingAppUri("Assets/BuiltinIcons/" + toolId + ".svg");
    }

    /// <summary>复古预设的内置工具图标；没有对应资产时返回 null（界面回退字体字形）。</summary>
    internal static string? ClassicBuiltinImage(string toolId)
    {
        var asset = ClassicBuiltinAssetId(toolId);
        return asset is null ? null : ExistingAppUri("Assets/IconStyles/Classic/Builtin/" + asset + ".svg");
    }

    /// <summary>旧图标名与当前工具 id 的兼容映射；返回 null 表示没有复古图标。</summary>
    internal static string? ClassicBuiltinAssetId(string toolId)
    {
        if (string.IsNullOrWhiteSpace(toolId) || toolId.StartsWith("plugin:", StringComparison.OrdinalIgnoreCase)) return null;
        return toolId.ToLowerInvariant() switch
        {
            "space-cleanup" => "junk-cleaner",
            "plugins" => "community-tools",
            _ => toolId.ToLowerInvariant()
        };
    }

    internal static string? DefaultNavigationImage(string tag)
    {
        if (tag.StartsWith("plugin-page:", StringComparison.OrdinalIgnoreCase))
            return DefaultBuiltinImage(tag[12..]);
        var relative = NavigationIconService.AssetFor(tag);
        return ExistingAppUri(relative);
    }

    /// <summary>复古预设的菜单图标。</summary>
    internal static string? ClassicNavigationImage(string tag)
    {
        var relative = ClassicNavigationRelativePath(tag);
        return relative is null ? null : ExistingAppUri(relative);
    }

    internal static string? ClassicNavigationRelativePath(string tag)
    {
        if (tag.StartsWith("plugin-page:", StringComparison.OrdinalIgnoreCase))
        {
            var id = tag[12..];
            var pluginAsset = ClassicBuiltinAssetId(id);
            return pluginAsset is null ? null : "Assets/IconStyles/Classic/Builtin/" + pluginAsset + ".svg";
        }

        var asset = tag switch
        {
            "home" => "Assets/IconStyles/Classic/Navigation/home.svg",
            "hardware" => "Assets/IconStyles/Classic/Navigation/hardware.svg",
            "builtin" => "Assets/IconStyles/Classic/Navigation/toolbox.svg",
            "benchmark" => "Assets/IconStyles/Classic/Builtin/performance-benchmark.svg",
            "community" => "Assets/IconStyles/Classic/Builtin/community-tools.svg",
            "ai-assistant" => "Assets/IconStyles/Classic/Builtin/ai-assistant.svg",
            "settings" => "Assets/IconStyles/Classic/Navigation/settings.svg",
            var c when c.Contains("处理器") => "Assets/IconStyles/Classic/Builtin/cpu-ranking.svg",
            var c when c.Contains("显卡") => "Assets/IconStyles/Classic/Builtin/gpu-ranking.svg",
            var c when c.Contains("显示器") => "Assets/IconStyles/Classic/Navigation/display.svg",
            var c when c.Contains("硬盘") => "Assets/IconStyles/Classic/Builtin/disk-health.svg",
            var c when c.Contains("内存") => "Assets/IconStyles/Classic/Navigation/memory.svg",
            var c when c.Contains("外设") => "Assets/IconStyles/Classic/Builtin/keyboard-test.svg",
            var c when c.Contains("声卡") => "Assets/IconStyles/Classic/Navigation/sound.svg",
            var c when c.Contains("网卡") || c.Contains("网络") => "Assets/IconStyles/Classic/Builtin/network-optimize.svg",
            var c when c.Contains("综合检测") || c.Contains("硬件") => "Assets/IconStyles/Classic/Builtin/quick-device-check.svg",
            var c when c.Contains("综合") || c.Contains("实用") || c.Contains("系统") => "Assets/IconStyles/Classic/Builtin/community-tools.svg",
            var c when c.Contains("游戏") => "Assets/IconStyles/Classic/Builtin/game-monitor.svg",
            var c when c.Contains("烤鸡") => "Assets/IconStyles/Classic/Builtin/stress-test.svg",
            _ => "Assets/IconStyles/Classic/Navigation/other.svg"
        };
        return asset;
    }

    /// <summary>把相对资产路径变成应用内 ms-appx 地址；文件不存在时返回 null。</summary>
    internal static string? ExistingAppUri(string relativePath)
    {
        var normalized = relativePath.Replace('/', Path.DirectorySeparatorChar);
        var path = Path.Combine(AppContext.BaseDirectory, normalized);
        return File.Exists(path) ? "ms-appx:///" + relativePath.Replace('\\', '/') : null;
    }
}

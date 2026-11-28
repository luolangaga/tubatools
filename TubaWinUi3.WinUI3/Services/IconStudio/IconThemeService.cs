using System.Text.Json;
using Microsoft.UI.Xaml;
using TubaWinUi3.PluginSdk;

namespace TubaWinUi3.Services;

internal sealed record IconStudioNavigationItem(
    string Tag, string Title, string Subtitle, string Glyph, string? DefaultImagePath);

/// <summary>
/// 图标工坊的宿主侧状态：全局预设 + 单个位置的图片覆盖。
/// 设置写在 AppSettings（IconStudio.*）里，界面解析时同步读取，所以插件即使没加载，
/// 已经保存的图标也会照常生效。所有修改方法都应在 UI 线程调用。
/// </summary>
internal static class IconThemeService
{
    private const string PresetKey = "IconStudio.Preset";
    private const string OverridesKey = "IconStudio.Overrides";

    private static readonly object Gate = new();
    private static Dictionary<string, string>? _overrides;
    private static PluginIconPreset _preset = PluginIconPreset.Default;

    /// <summary>预设或某个位置的图片发生变化。宿主用它刷新导航与已打开的页面。</summary>
    public static event Action? Changed;

    public static PluginIconPreset Preset
    {
        get { EnsureLoaded(); lock (Gate) return _preset; }
    }

    public static bool IsFluent => Preset == PluginIconPreset.FluentGlyph;
    public static bool IsClassic => Preset == PluginIconPreset.ClassicColor;

    /// <summary>单个位置的有效自定义图片；文件被删掉时视为没有覆盖。</summary>
    internal static string? GetCustomImage(PluginIconGroup group, string id)
    {
        EnsureLoaded();
        var key = Key(group, id);
        lock (Gate)
        {
            var path = _overrides!.GetValueOrDefault(key);
            return path is not null && File.Exists(path) ? path : null;
        }
    }

    internal static string? GetNavigationCustomImage(string tag)
        => GetCustomImage(PluginIconGroup.Navigation, tag);

    internal static string? GetDialogCustomImage(string dialogId)
        => GetCustomImage(PluginIconGroup.Dialog, dialogId);

    internal static string? GetBuiltinCustomImage(string toolId)
        => GetCustomImage(PluginIconGroup.BuiltinTool, toolId);

    internal static void ValidateSlot(PluginIconGroup group, string id)
    {
        if (FindSlot(group, id) is null)
            throw new KeyNotFoundException($"找不到可定制的图标位置：{GroupName(group)}:{id}");
    }

    public static IReadOnlyList<PluginIconSlot> GetSlots()
    {
        EnsureLoaded();
        var result = new List<PluginIconSlot>();

        foreach (var tool in BuiltinToolRegistry.Tools
                     .OrderBy(t => t.Category, StringComparer.CurrentCultureIgnoreCase)
                     .ThenBy(t => t.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            result.Add(new PluginIconSlot
            {
                Group = PluginIconGroup.BuiltinTool,
                Id = tool.Id,
                Title = tool.Name,
                Subtitle = tool.Category,
                Glyph = tool.Glyph,
                DefaultImagePath = IconStudioCatalog.DefaultBuiltinImage(tool.Id),
                ClassicImagePath = IconStudioCatalog.ClassicBuiltinImage(tool.Id),
                CurrentImagePath = GetCustomImage(PluginIconGroup.BuiltinTool, tool.Id)
            });
        }

        foreach (var item in GetNavigationItems())
        {
            result.Add(new PluginIconSlot
            {
                Group = PluginIconGroup.Navigation,
                Id = item.Tag,
                Title = item.Title,
                Subtitle = item.Subtitle,
                Glyph = item.Glyph,
                DefaultImagePath = item.DefaultImagePath,
                ClassicImagePath = IconStudioCatalog.ClassicNavigationImage(item.Tag),
                CurrentImagePath = GetCustomImage(PluginIconGroup.Navigation, item.Tag)
            });
        }

        var chinese = LocalizationService.CurrentLanguage.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
        foreach (var slot in IconStudioCatalog.DialogSlots)
        {
            result.Add(new PluginIconSlot
            {
                Group = PluginIconGroup.Dialog,
                Id = slot.Id,
                Title = chinese ? slot.ChineseTitle : slot.EnglishTitle,
                Subtitle = chinese ? "消息弹窗标题图标" : "Dialog title icon",
                Glyph = slot.Glyph,
                DefaultImagePath = IconStudioCatalog.DefaultDialogImage(slot.Id),
                ClassicImagePath = IconStudioCatalog.ClassicDialogImage(slot.Id),
                CurrentImagePath = GetCustomImage(PluginIconGroup.Dialog, slot.Id)
            });
        }

        return result;
    }

    public static PluginIconState GetState()
    {
        EnsureLoaded();
        var custom = GetSlots().Count(s => s.IsCustom);
        return new PluginIconState { Preset = Preset, CustomCount = custom };
    }

    public static void SetImage(PluginIconGroup group, string id, string sourcePath)
    {
        var slot = FindSlot(group, id) ?? throw new KeyNotFoundException($"找不到可定制的图标位置：{GroupName(group)}:{id}");
        var imported = IconStudioImageImporter.Import(sourcePath);
        SetImportedImage(group, slot.Id, imported);
    }

    internal static void SetImportedImage(PluginIconGroup group, string id, string importedPath)
    {
        EnsureLoaded();
        ValidateSlot(group, id);
        string json;
        lock (Gate)
        {
            _overrides![Key(group, id)] = importedPath;
            json = JsonSerializer.Serialize(_overrides);
        }
        AppSettings.Set(OverridesKey, json);
        AfterChange(prune: true);
    }

    public static void Reset(PluginIconGroup group, string id)
    {
        EnsureLoaded();
        string json;
        var removed = false;
        lock (Gate)
        {
            removed = _overrides!.Remove(Key(group, id));
            json = JsonSerializer.Serialize(_overrides);
        }
        if (!removed) return;
        if (_overrides!.Count == 0) AppSettings.Remove(OverridesKey);
        else AppSettings.Set(OverridesKey, json);
        AfterChange(prune: true);
    }

    public static void ResetAll()
    {
        EnsureLoaded();
        lock (Gate)
        {
            _overrides!.Clear();
            _preset = PluginIconPreset.Default;
        }
        AppSettings.Remove(OverridesKey);
        AppSettings.Remove(PresetKey);
        AfterChange(prune: true);
    }

    public static void ApplyPreset(PluginIconPreset preset)
    {
        if (!Enum.IsDefined(preset)) throw new ArgumentOutOfRangeException(nameof(preset));
        EnsureLoaded();
        lock (Gate)
        {
            _overrides!.Clear();
            _preset = preset;
        }
        AppSettings.Remove(OverridesKey);
        if (preset == PluginIconPreset.Default) AppSettings.Remove(PresetKey);
        else AppSettings.Set(PresetKey, PresetName(preset));
        AfterChange(prune: true);
    }

    public static IReadOnlyList<IconStudioNavigationItem> GetNavigationItems()
    {
        if (App.MainWindow is MainWindow window)
            return window.GetIconStudioNavigationItems();

        // 单元测试 / 主窗口尚未创建时的稳定回退（不依赖 XAML）。
        var chinese = LocalizationService.CurrentLanguage.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
        return
        [
            new("home", chinese ? "首页" : "Home", chinese ? "主导航" : "Main navigation", "\uE80F", IconStudioCatalog.DefaultNavigationImage("home")),
            new("hardware", chinese ? "硬件信息" : "Hardware", chinese ? "主导航" : "Main navigation", "\uE977", IconStudioCatalog.DefaultNavigationImage("hardware")),
            new("builtin", chinese ? "内置" : "Built-in", chinese ? "主导航" : "Main navigation", "\uE90F", IconStudioCatalog.DefaultNavigationImage("builtin")),
            new("benchmark", chinese ? "性能测试" : "Benchmark", chinese ? "主导航" : "Main navigation", "\uE9D9", IconStudioCatalog.DefaultNavigationImage("benchmark")),
            new("community", chinese ? "插件" : "Plugins", chinese ? "主导航" : "Main navigation", "\uE779", IconStudioCatalog.DefaultNavigationImage("community")),
            new("ai-assistant", chinese ? "AI 助手" : "AI assistant", chinese ? "主导航" : "Main navigation", "\uE99A", IconStudioCatalog.DefaultNavigationImage("ai-assistant")),
            new("settings", chinese ? "设置" : "Settings", chinese ? "页脚" : "Footer", "\uE713", IconStudioCatalog.DefaultNavigationImage("settings"))
        ];
    }

    private static PluginIconSlot? FindSlot(PluginIconGroup group, string id)
    {
        foreach (var slot in GetSlots())
        {
            if (slot.Group == group && slot.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) return slot;
        }
        return null;
    }

    private static void EnsureLoaded()
    {
        lock (Gate)
        {
            if (_overrides is not null) return;
            _preset = ParsePreset(AppSettings.Get(PresetKey));
            _overrides = ParseOverrides(AppSettings.Get(OverridesKey));
        }
    }

    private static Dictionary<string, string> ParseOverrides(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return [];
        try
        {
            var map = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            if (map is null) return [];
            // 手工写入可能出现大小写不同的重复键；逐项归一化时以最后一条为准，不能整体丢弃。
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in map)
            {
                if (string.IsNullOrWhiteSpace(pair.Key) || string.IsNullOrWhiteSpace(pair.Value)) continue;
                result[NormalizeKey(pair.Key)] = pair.Value;
            }
            return result;
        }
        catch
        {
            return [];
        }
    }

    private static PluginIconPreset ParsePreset(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "classic" or "classiccolor" or "classic-color" => PluginIconPreset.ClassicColor,
        "fluent" or "fluentglyph" or "fluent-glyph" => PluginIconPreset.FluentGlyph,
        _ => PluginIconPreset.Default
    };

    private static string PresetName(PluginIconPreset preset) => preset switch
    {
        PluginIconPreset.ClassicColor => "classic",
        PluginIconPreset.FluentGlyph => "fluent",
        _ => "default"
    };

    private static string Key(PluginIconGroup group, string id)
        => NormalizeKey(GroupName(group) + ":" + id.Trim().ToLowerInvariant());

    /// <summary>兼容历史/手工写入的键：作用域名与 id 统一小写，冒号前只保留一层。</summary>
    private static string NormalizeKey(string key)
    {
        var value = key.Trim().ToLowerInvariant();
        return value switch
        {
            _ when value.StartsWith("builtin:", StringComparison.Ordinal) => "builtin:" + value[8..],
            _ when value.StartsWith("navigation:", StringComparison.Ordinal) => "navigation:" + value[11..],
            _ when value.StartsWith("dialog:", StringComparison.Ordinal) => "dialog:" + value[7..],
            _ => value
        };
    }

    private static string GroupName(PluginIconGroup group) => group switch
    {
        PluginIconGroup.BuiltinTool => IconStudioCatalog.BuiltinGroup,
        PluginIconGroup.Navigation => IconStudioCatalog.NavigationGroup,
        PluginIconGroup.Dialog => IconStudioCatalog.DialogGroup,
        _ => throw new ArgumentOutOfRangeException(nameof(group))
    };

    private static void AfterChange(bool prune)
    {
        BuiltinIconService.ResetCache();
        if (prune)
        {
            IReadOnlyCollection<string> used;
            lock (Gate) used = _overrides!.Values.ToArray();
            IconStudioImageImporter.Prune(used);
        }
        RaiseChanged();
    }

    private static void RaiseChanged()
    {
        var handler = Changed;
        if (handler is null) return;
        if (App.MainWindow?.DispatcherQueue is { } dispatcher && !dispatcher.HasThreadAccess)
        {
            dispatcher.TryEnqueue(() => Invoke(handler));
            return;
        }
        Invoke(handler);

        static void Invoke(Action handler)
        {
            foreach (var subscriber in handler.GetInvocationList())
            {
                try { ((Action)subscriber)(); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine("[IconStudio] 刷新订阅者失败：" + ex.Message); }
            }
        }
    }
}

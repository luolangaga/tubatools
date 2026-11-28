namespace TubaWinUi3.PluginSdk;

/// <summary>图标定制作用域：内置工具卡片、侧栏菜单、消息对话框标题图标。</summary>
public enum PluginIconGroup
{
    BuiltinTool,
    Navigation,
    Dialog
}

/// <summary>全局图标预设。</summary>
public enum PluginIconPreset
{
    /// <summary>当前版本的彩色矢量图标。</summary>
    Default,
    /// <summary>远古版本的简易双色线稿图标。</summary>
    ClassicColor,
    /// <summary>最早版本的 Segoe Fluent Icons 字体字形。</summary>
    FluentGlyph
}

/// <summary>
/// 一个可定制的图标位置。<see cref="DefaultImagePath"/> 与 <see cref="ClassicImagePath"/>
/// 可能是 <c>ms-appx:///</c> 地址，也可能是插件包内的绝对路径；<see cref="CurrentImagePath"/>
/// 是导入到工具箱数据目录后的绝对路径。没有图片时按 <see cref="Glyph"/> 回退字体字形。
/// </summary>
public sealed class PluginIconSlot
{
    public required PluginIconGroup Group { get; init; }
    public required string Id { get; init; }
    public required string Title { get; init; }
    public string Subtitle { get; init; } = "";
    public required string Glyph { get; init; }
    public string? DefaultImagePath { get; init; }
    public string? ClassicImagePath { get; init; }
    public string? CurrentImagePath { get; init; }
    public bool IsCustom => !string.IsNullOrWhiteSpace(CurrentImagePath);
}

/// <summary>当前图标预设与自定义图标数量。</summary>
public sealed class PluginIconState
{
    public required PluginIconPreset Preset { get; init; }
    public required int CustomCount { get; init; }
}

/// <summary>
/// SDK 2.3 可选能力：修改内置工具、侧栏菜单与消息对话框的图标。
/// 通过 <see cref="IPluginServices.GetService{T}"/> 获取；宿主不提供时返回 null。
/// 所有方法只能在插件已声明 <c>icons</c> 能力（或 fullTrust）时可用。
/// </summary>
public interface IPluginIconService
{
    /// <summary>枚举当前所有可定制位置，包含默认、复古预设与当前自定义图片。</summary>
    Task<IReadOnlyList<PluginIconSlot>> GetSlotsAsync(CancellationToken ct = default);

    /// <summary>读取当前预设与自定义数量。</summary>
    Task<PluginIconState> GetStateAsync(CancellationToken ct = default);

    /// <summary>
    /// 为单个位置选择自定义图片。图片会复制到工具箱数据目录并做尺寸/格式校验；
    /// 支持 PNG/JPEG/BMP/GIF/ICO/SVG。
    /// </summary>
    Task SetImageAsync(PluginIconGroup group, string id, string sourcePath, CancellationToken ct = default);

    /// <summary>恢复某个位置为当前预设的默认图标。</summary>
    Task ResetAsync(PluginIconGroup group, string id, CancellationToken ct = default);

    /// <summary>清除全部自定义图片并恢复默认预设。</summary>
    Task ResetAllAsync(CancellationToken ct = default);

    /// <summary>切换全局预设；会清除全部自定义图片，使预设完整生效。</summary>
    Task ApplyPresetAsync(PluginIconPreset preset, CancellationToken ct = default);
}

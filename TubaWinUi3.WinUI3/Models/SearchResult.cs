using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Services;

namespace TubaWinUi3.Models;

public sealed class SearchResult
{
    public required string Title { get; init; }
    public required string Subtitle { get; init; }
    public required string Glyph { get; init; }
    public required SearchItemKind Kind { get; init; }
    public required string MatchKey { get; init; }
    public string? IconPath { get; init; }
    public string? Category { get; init; }
    public double Score { get; init; }

    /// <summary>内置工具 id；有它就优先显示彩色矢量图标。</summary>
    public string? BuiltinToolId { get; init; }

    public bool HasIconPath => !string.IsNullOrEmpty(IconPath);

    private ImageSource? _iconSource;
    private bool _iconSourceResolved;

    /// <summary>彩色矢量图标；惰性求值（SvgImageSource 只能在 UI 线程创建）。</summary>
    public ImageSource? IconSource
    {
        get
        {
            if (!_iconSourceResolved)
            {
                _iconSource = BuiltinIconService.Get(BuiltinToolId);
                _iconSourceResolved = true;
            }
            return _iconSource;
        }
    }

    public bool HasIconSource => !HasIconPath && IconSource is not null;

    /// <summary>没有任何图片资源时才回退到字形（快捷操作、设置项走这条）。</summary>
    public bool HasGlyphFallback => !HasIconPath && IconSource is null;

    public string KindText => Kind switch
    {
        SearchItemKind.ExternalTool => LocalizationService.L("Search_KindTool", "工具"),
        SearchItemKind.BuiltinTool => LocalizationService.L("Search_KindBuiltin", "内置"),
        SearchItemKind.Setting => LocalizationService.L("Search_KindSetting", "设置"),
        SearchItemKind.CustomTool => LocalizationService.L("Search_KindCustom", "自定义"),
        SearchItemKind.QuickAction => LocalizationService.L("Search_KindQuick", "快捷"),
        SearchItemKind.CommunityTool => LocalizationService.L("Search_KindCommunity", "社区"),
        _ => ""
    };

    public override string ToString() => Title;
}

public enum SearchItemKind
{
    ExternalTool,
    BuiltinTool,
    Setting,
    CustomTool,
    QuickAction,
    CommunityTool
}

public sealed class SearchNavigationTarget
{
    public string? HighlightToolPath { get; init; }
    public string? HighlightSettingKey { get; init; }
    public string? HighlightBuiltinId { get; init; }
}

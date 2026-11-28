using System.Numerics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Services;
using Windows.Foundation;
using Windows.UI;

namespace TubaWinUi3.Controls;

/// <summary>
/// 商店式竖排图标菜单：图标在上、文字在下；选中项是内缩的白色圆角卡片 + 贴左缘的蓝色竖条。
/// 选中卡片是共享的一层指示器，切换时纵向滑动（对齐 Microsoft Store 的动效），
/// 悬停底色用透明度过渡。只负责显示和报告点击，导航与选中状态由 MainWindow 回写。
/// </summary>
public sealed partial class NavRail : UserControl
{
    public sealed record NavRailItem(string Tag, string Label, IconElement Icon);

    private sealed record Entry(string Tag, Button Button, Border Hover, TextBlock Label, IconElement Icon);

    private readonly List<Entry> _entries = [];
    private string? _selectedTag;

    public event EventHandler<string>? ItemInvoked;

    public NavRail()
    {
        InitializeComponent();

        MenuSelection.TranslationTransition = new Vector3Transition
        {
            Duration = TimeSpan.FromMilliseconds(240),
        };
        MenuSelection.OpacityTransition = new ScalarTransition
        {
            Duration = TimeSpan.FromMilliseconds(120),
        };
        FooterSelection.OpacityTransition = new ScalarTransition
        {
            Duration = TimeSpan.FromMilliseconds(140),
        };
    }

    public void SetItems(IReadOnlyList<NavRailItem> menuItems, NavRailItem? settingsItem, string? selectedTag)
    {
        _selectedTag = selectedTag;
        _entries.Clear();
        MenuPanel.Children.Clear();
        FooterPanel.Children.Clear();

        FooterBorder.Visibility = settingsItem is null ? Visibility.Collapsed : Visibility.Visible;

        foreach (var item in menuItems) AddTile(MenuPanel, item);
        if (settingsItem is not null) AddTile(FooterPanel, settingsItem);

        MenuSelection.Opacity = 0;
        FooterSelection.Opacity = 0;
        ApplySelection(animate: false);
    }

    public void SetSelected(string? tag)
    {
        _selectedTag = tag;
        ApplySelection(animate: true);
    }

    public string? SelectedTag => _selectedTag;

    private void AddTile(Panel host, NavRailItem item)
    {
        var hoverBrush = ThemeColors.ResolveBrush("SubtleFillColorSecondaryBrush", Color.FromArgb(255, 240, 240, 240));
        var secondaryTextBrush = ThemeColors.ResolveBrush("TextFillColorSecondaryBrush", Color.FromArgb(255, 90, 90, 90));

        var content = BuildContent(item, hoverBrush, secondaryTextBrush);
        var button = new Button
        {
            Style = (Style)Resources["NavRailTileStyle"],
            Tag = item.Tag,
            Content = content.Root,
        };

        AutomationProperties.SetName(button, item.Label);
        AutomationProperties.SetAutomationId(button, "NavRail_" + item.Tag);
        ToolTipService.SetToolTip(button, item.Label);
        button.Click += (_, _) =>
        {
            SetSelected(item.Tag);
            ItemInvoked?.Invoke(this, item.Tag);
        };
        button.PointerEntered += (_, _) =>
        {
            if (!string.Equals(_selectedTag, item.Tag, StringComparison.Ordinal))
                content.Hover.Opacity = 1;
        };
        button.PointerExited += (_, _) => content.Hover.Opacity = 0;

        _entries.Add(new Entry(item.Tag, button, content.Hover, content.Label, item.Icon));
        host.Children.Add(button);
    }

    private sealed record TileContent(Grid Root, Border Hover, TextBlock Label);

    private static TileContent BuildContent(NavRailItem item, Brush hoverBrush, Brush secondaryTextBrush)
    {
        // 尺寸对齐 Microsoft Store 左侧栏：卡片比栏宽窄一圈，图标 22、文字 11。
        var root = new Grid { Width = 88, Height = 62 };

        var hover = new Border
        {
            Width = 72,
            Height = 52,
            CornerRadius = new CornerRadius(8),
            Background = hoverBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = 0,
            IsHitTestVisible = false,
            OpacityTransition = new ScalarTransition { Duration = TimeSpan.FromMilliseconds(120) },
        };

        item.Icon.HorizontalAlignment = HorizontalAlignment.Center;
        item.Icon.VerticalAlignment = VerticalAlignment.Center;
        var iconHost = new Grid
        {
            Width = 26,
            Height = 24,
            HorizontalAlignment = HorizontalAlignment.Center,
            Children = { item.Icon },
        };

        var label = new TextBlock
        {
            Text = item.Label,
            FontSize = 11,
            MaxWidth = 68,
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            MaxLines = 2,
            TextTrimming = TextTrimming.CharacterEllipsis,
            Foreground = secondaryTextBrush,
            HorizontalAlignment = HorizontalAlignment.Center,
        };

        var stack = new StackPanel
        {
            Spacing = 3,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { iconHost, label },
        };

        root.Children.Add(hover);
        root.Children.Add(stack);
        return new TileContent(root, hover, label);
    }

    private void ApplySelection(bool animate)
    {
        var primaryTextBrush = ThemeColors.ResolveBrush("TextFillColorPrimaryBrush", Color.FromArgb(255, 26, 26, 26));
        var accentTextBrush = ThemeColors.ResolveBrush("AccentTextFillColorPrimaryBrush", Color.FromArgb(255, 0, 95, 184));
        var secondaryTextBrush = ThemeColors.ResolveBrush("TextFillColorSecondaryBrush", Color.FromArgb(255, 90, 90, 90));

        Entry? active = null;
        foreach (var entry in _entries)
        {
            var isActive = string.Equals(entry.Tag, _selectedTag, StringComparison.Ordinal);
            entry.Hover.Opacity = 0;
            // 商店的选中项文字仍是正文色，只有白卡片和左侧蓝条表达选中；不要整行染成蓝色。
            entry.Label.Foreground = isActive ? primaryTextBrush : secondaryTextBrush;
            if (entry.Icon is FontIcon fontIcon)
                fontIcon.Foreground = isActive ? accentTextBrush : secondaryTextBrush;
            if (isActive) active = entry;
        }

        if (active is null)
        {
            MenuSelection.Opacity = 0;
            FooterSelection.Opacity = 0;
            return;
        }

        if (IsSettingsTag(active.Tag))
        {
            // 设置固定在栏底，不跟随列表滑动：淡出上方指示器、淡入底部卡片。
            MenuSelection.Opacity = 0;
            FooterSelection.Opacity = 1;
            return;
        }

        FooterSelection.Opacity = 0;
        MenuSelection.Opacity = 1;

        var top = active.Button.TransformToVisual(MenuContent).TransformPoint(new Point(0, 0)).Y;
        var translation = new Vector3(0, (float)top + 5, 0);

        if (!animate)
        {
            // 首次构建/语言或图标刷新时不要从顶部飞下来，直接放到目标位置。
            var transition = MenuSelection.TranslationTransition;
            MenuSelection.TranslationTransition = null;
            MenuSelection.Translation = translation;
            MenuSelection.TranslationTransition = transition;
        }
        else
        {
            MenuSelection.Translation = translation;
        }
    }

    private static bool IsSettingsTag(string tag) => string.Equals(tag, "settings", StringComparison.Ordinal);
}

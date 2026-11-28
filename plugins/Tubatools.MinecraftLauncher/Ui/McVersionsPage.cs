using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Documents;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Tubatools.MinecraftLauncher.Services;

namespace Tubatools.MinecraftLauncher.Ui;

/// <summary>版本管理页：扫描结果卡片列表、搜索、加载器筛选与快速启动。</summary>
internal sealed class McVersionsPage : IMcPage
{
    private readonly McLauncherView _shell;
    private readonly McPalette _palette;
    private readonly Grid _root = new();
    private readonly TextBox _search = new() { PlaceholderText = "搜索版本名、MC 版本或加载器", Width = 280 };
    private readonly ComboBox _filter = new() { Width = 130 };
    private readonly TextBlock _count = new();
    private readonly StackPanel _list = new() { Spacing = 10 };
    private readonly ScrollViewer _scroll = new();
    private readonly InfoBar _problems = new() { IsOpen = false, IsClosable = true, Severity = InfoBarSeverity.Warning };
    private static readonly string[] Filters = ["全部版本", "仅正式版", "仅带加载器", "仅原版"];

    public McVersionsPage(McLauncherView shell)
    {
        _shell = shell;
        _palette = shell.Palette;
        foreach (var filter in Filters)
            _filter.Items.Add(new ComboBoxItem { Content = filter });
        _filter.SelectedIndex = 0;
        _search.TextChanged += (_, _) => RefreshList();
        _filter.SelectionChanged += (_, _) => RefreshList();
        _shell.StateChanged += RefreshList;

        BuildLayout();
        RefreshList();
    }

    public FrameworkElement Root => _root;

    public void OnShown() => RefreshList();

    private void BuildLayout()
    {
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _root.Padding = new Thickness(24, 18, 24, 0);

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titles = new StackPanel { Spacing = 2 };
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 9 };
        titleRow.Children.Add(McUi.GlyphIcon("\uE8F1", 26, _palette.Accent));
        titleRow.Children.Add(McUi.Text("版本管理", 21, FontWeights.SemiBold, _palette.Text));
        titles.Children.Add(titleRow);
        titles.Children.Add(McUi.Text("选择要启动的实例；Forge / Fabric 等加载器版本会自动合并原版依赖。", 12,
            FontWeights.Normal, _palette.Secondary, true));
        header.Children.Add(titles);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            VerticalAlignment = VerticalAlignment.Center
        };
        var refresh = McUi.Button("刷新", _palette, icon: "icon_refresh.png");
        refresh.Click += async (_, _) =>
        {
            refresh.IsEnabled = false;
            await _shell.RefreshVersionsAsync(true);
            refresh.IsEnabled = true;
            RefreshList();
        };
        actions.Children.Add(refresh);
        var folder = McUi.Button("打开游戏目录", _palette, icon: "icon_folder.png");
        folder.Click += (_, _) => McLauncherView.OpenFolder(_shell.Settings.GameDirectory);
        actions.Children.Add(folder);
        Grid.SetColumn(actions, 1);
        header.Children.Add(actions);
        _root.Children.Add(header);

        var toolbar = new Grid { Margin = new Thickness(0, 14, 0, 10), ColumnSpacing = 10 };
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        _search.Height = 40;
        _filter.Height = 40;
        toolbar.Children.Add(_search);
        Grid.SetColumn(_filter, 1);
        toolbar.Children.Add(_filter);
        _count.FontSize = 12;
        _count.Foreground = _palette.Secondary;
        _count.VerticalAlignment = VerticalAlignment.Center;
        _count.HorizontalAlignment = HorizontalAlignment.Right;
        Grid.SetColumn(_count, 2);
        toolbar.Children.Add(_count);
        Grid.SetRow(toolbar, 1);
        _root.Children.Add(toolbar);

        _problems.Margin = new Thickness(0, 0, 0, 10);
        _problems.Title = "部分版本无法读取";
        Grid.SetRow(_problems, 2);
        _root.Children.Add(_problems);

        _scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        _scroll.VerticalScrollMode = ScrollMode.Enabled;
        _scroll.HorizontalScrollMode = ScrollMode.Disabled;
        _scroll.Content = _list;
        Grid.SetRow(_scroll, 3);
        _root.Children.Add(_scroll);
    }

    public void RefreshList()
    {
        var search = _search.Text.Trim();
        var filter = _filter.SelectedIndex <= 0 ? 0 : _filter.SelectedIndex;
        var versions = _shell.Versions.AsEnumerable();
        if (search.Length > 0)
            versions = versions.Where(version =>
                version.Id.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                version.VanillaName.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                version.Loaders.Contains(search, StringComparison.OrdinalIgnoreCase));

        versions = filter switch
        {
            1 => versions.Where(version => version.Type == "release"),
            2 => versions.Where(version => version.Loaders.Length > 0),
            3 => versions.Where(version => version.Loaders.Length == 0),
            _ => versions
        };

        var list = versions.ToList();
        _count.Text = $"共 {list.Count} 个版本 · 扫描目录 {_shell.Settings.GameDirectory}";

        _problems.IsOpen = _shell.VersionProblems.Count > 0;
        if (_shell.VersionProblems.Count > 0)
            _problems.Message = string.Join("；", _shell.VersionProblems.Take(5)) +
                                (_shell.VersionProblems.Count > 5 ? $"……另有 {_shell.VersionProblems.Count - 5} 个" : "");

        _list.Children.Clear();
        if (list.Count == 0)
        {
            var empty = new StackPanel { Spacing = 10, Margin = new Thickness(0, 20, 0, 0) };
            empty.Children.Add(McUi.Text(
                _shell.Versions.Count == 0
                    ? "没有找到任何已安装版本。前往下载页安装，或把已有的 .minecraft/versions 文件夹放到当前游戏目录。"
                    : "没有匹配的版本，试试清空搜索或切换筛选。",
                13, FontWeights.Normal, _palette.Secondary, true));
            var download = McUi.Button("前往下载页", _palette, accent: true);
            download.Click += (_, _) => _shell.ShowPage("download");
            empty.Children.Add(download);
            _list.Children.Add(empty);
            return;
        }

        foreach (var version in list)
            _list.Children.Add(BuildVersionCard(version));
    }

    private Border BuildVersionCard(McVersion version)
    {
        var selected = string.Equals(_shell.SelectedVersion?.Id, version.Id, StringComparison.OrdinalIgnoreCase);
        var border = new Border
        {
            Background = selected
                ? new SolidColorBrush(McPalette.Color(0xFF, 0x18, 0x22, 0x1B))
                : _palette.Card,
            BorderBrush = selected ? _palette.Accent : _palette.CardStroke,
            BorderThickness = selected ? new Thickness(3, 0, 0, 0) : new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(16, 12, 12, 12)
        };

        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(McArt.BrandIcon(46));

        var infoButton = new Button
        {
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            BorderThickness = new Thickness(0),
            Padding = new Thickness(0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            VerticalContentAlignment = VerticalAlignment.Center
        };
        var info = new StackPanel { Spacing = 2 };
        var versionName = new TextBlock
        {
            FontSize = 15.5,
            FontWeight = FontWeights.SemiBold,
            Foreground = _palette.Text,
            TextTrimming = TextTrimming.CharacterEllipsis,
            TextWrapping = TextWrapping.NoWrap
        };
        versionName.Inlines.Add(new Run { Text = version.Id });
        if (selected)
            versionName.Inlines.Add(new Run { Text = "   当前", FontSize = 11, Foreground = _palette.Success });
        info.Children.Add(versionName);
        var meta = McUi.Text(version.VersionLabel, 12, FontWeights.Normal, _palette.Secondary);
        meta.TextTrimming = TextTrimming.CharacterEllipsis;
        info.Children.Add(meta);
        var path = McUi.Text(
            $"{version.TypeLabel} · {McUi.FormatDate(version.ReleaseTime)} · {version.Directory}",
            10.5, FontWeights.Normal, _palette.Tertiary);
        path.TextTrimming = TextTrimming.CharacterEllipsis;
        info.Children.Add(path);
        infoButton.Content = info;
        infoButton.Click += (_, _) =>
        {
            _shell.SelectVersion(version);
            _shell.ShowPage("launch");
        };
        Grid.SetColumn(infoButton, 1);
        grid.Children.Add(infoButton);

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 6,
            VerticalAlignment = VerticalAlignment.Center
        };
        if (!version.HasClientJar)
            actions.Children.Add(McUi.Chip("不完整", _palette,
                new SolidColorBrush(McPalette.Color(0x20, 0xE7, 0x4C, 0x3C)), _palette.Danger));

        var launch = McUi.Button("启动", _palette, accent: true, height: 38, icon: "icon_play.png");
        launch.IsEnabled = version.HasClientJar;
        launch.Click += async (_, _) =>
        {
            _shell.SelectVersion(version);
            _shell.ShowPage("launch");
            await _shell.LaunchPage.LaunchAsync();
        };
        actions.Children.Add(launch);

        var settings = McUi.Button("设置", _palette, height: 38, icon: "icon_settings.png");
        settings.Click += async (_, _) =>
        {
            if (_root.XamlRoot is not null)
                await McDialogs.ShowVersionAsync(_root.XamlRoot, _palette, _root.ActualTheme, version);
        };
        actions.Children.Add(settings);

        var folder = McUi.PixelIconButton("icon_folder.png", "打开版本目录", _palette, 28);
        folder.Click += (_, _) => McLauncherView.OpenFolder(version.Directory);
        actions.Children.Add(folder);
        Grid.SetColumn(actions, 2);
        grid.Children.Add(actions);

        border.Child = grid;
        border.PointerEntered += (_, _) =>
        {
            if (!string.Equals(_shell.SelectedVersion?.Id, version.Id, StringComparison.OrdinalIgnoreCase))
                border.BorderBrush = _palette.Accent;
        };
        border.PointerExited += (_, _) =>
        {
            if (!string.Equals(_shell.SelectedVersion?.Id, version.Id, StringComparison.OrdinalIgnoreCase))
                border.BorderBrush = _palette.CardStroke;
        };
        return border;
    }
}

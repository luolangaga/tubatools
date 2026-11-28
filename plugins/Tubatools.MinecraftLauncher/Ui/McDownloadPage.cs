using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.PluginSdk;
using Tubatools.MinecraftLauncher.Services;

namespace Tubatools.MinecraftLauncher.Ui;

/// <summary>
/// 下载与安装页：顶部切换「游戏版本 / 模组 / 光影 / 资源包」。
/// 游戏版本页是一键安装向导（版本 + 加载器 + 安装）和官方版本列表；资源页对接 Modrinth。
/// </summary>
internal sealed class McDownloadPage : IMcPage
{
    private readonly McLauncherView _shell;
    private readonly McPalette _palette;
    private readonly Grid _root = new();
    private readonly ScrollViewer _pageScroll = new();
    private readonly SelectorBar _tabs = new() { Margin = new Thickness(24, 4, 24, 10) };
    private readonly SelectorBarItem _gameTab = new() { Text = "游戏版本", IsSelected = true };
    private readonly SelectorBarItem _modpackTab = new() { Text = "整合包" };
    private readonly SelectorBarItem _modTab = new() { Text = "模组" };
    private readonly SelectorBarItem _shaderTab = new() { Text = "光影" };
    private readonly SelectorBarItem _resourcePackTab = new() { Text = "资源包" };
    private readonly Grid _gamePanel = new();
    private readonly Grid _resourcePanel = new();
    private readonly McResourceBrowser _resourceBrowser;

    private readonly TextBox _search = new() { PlaceholderText = "搜索版本号", Width = 240 };
    private readonly ComboBox _filter = new() { Width = 130 };
    private readonly TextBlock _count = new();
    private readonly StackPanel _list = new() { Spacing = 8 };
    private readonly InfoBar _status = new() { IsOpen = false, IsClosable = true };
    private readonly ProgressBar _loading = new() { IsIndeterminate = true, Height = 3, Visibility = Visibility.Collapsed };

    private readonly ComboBox _wizardGame = new() { Header = "Minecraft 版本", Width = 190 };
    private readonly ComboBox _wizardLoader = new() { Header = "加载器", Width = 200 };
    private readonly ComboBox _wizardLoaderVersion = new()
    {
        Header = "加载器版本",
        Width = 240,
        PlaceholderText = "选择加载器后自动填充"
    };
    private readonly Button _wizardInstall;
    private readonly TextBlock _wizardStatus = new()
    {
        FontSize = 11.5,
        TextWrapping = TextWrapping.Wrap,
        Foreground = new SolidColorBrush(Microsoft.UI.Colors.Transparent)
    };

    private readonly Grid _installCard;
    private readonly TextBlock _installTitle = new();
    private readonly TextBlock _installStage = new();
    private readonly TextBlock _installCount = new();
    private readonly ProgressBar _installBar = new() { Height = 6 };
    private readonly Button _installCancel;

    private McVersionManifest? _manifest;
    private string _manifestMode = "";
    private bool _loadingManifest;
    private CancellationTokenSource? _installCts;
    private CancellationTokenSource? _wizardCts;
    private int _shown;
    private string _resourceType = "";

    private static readonly string[] Filters = ["正式版", "快照", "远古版本", "全部", "已安装"];
    private static readonly (string Tag, string Label)[] LoaderChoices =
    [
        ("vanilla", "原版"),
        ("fabric", "Fabric"),
        ("quilt", "Quilt"),
        ("forge", "Forge"),
        ("neoforge", "NeoForge")
    ];

    private const int PageSize = 80;

    public McDownloadPage(McLauncherView shell)
    {
        _shell = shell;
        _palette = shell.Palette;
        foreach (var filter in Filters)
            _filter.Items.Add(new ComboBoxItem { Content = filter });
        _filter.SelectedIndex = 0;
        _filter.SelectionChanged += (_, _) => ResetAndRefresh();
        _search.TextChanged += (_, _) => ResetAndRefresh();

        _installCancel = McUi.Button("取消安装", _palette, height: 38, glyph: "\uE711");
        _installCancel.Click += (_, _) => _installCts?.Cancel();
        _installCard = BuildInstallCard();
        _installCard.Visibility = Visibility.Collapsed;

        _wizardInstall = McUi.Button("开始安装", _palette, accent: true, height: 46, glyph: "\uE896", fontSize: 14);
        _wizardInstall.VerticalAlignment = VerticalAlignment.Bottom;
        _wizardInstall.Click += async (_, _) => await InstallWizardAsync();
        _wizardGame.SelectionChanged += async (_, _) => await LoadWizardLoaderVersionsAsync();
        _wizardLoader.SelectionChanged += async (_, _) => await LoadWizardLoaderVersionsAsync();

        foreach (var (tag, label) in LoaderChoices)
            _wizardLoader.Items.Add(new ComboBoxItem { Content = label, Tag = tag });
        _wizardLoader.SelectedIndex = 0;

        _resourceBrowser = new McResourceBrowser(shell);
        _resourceBrowser.RequestLoaderSetup = () =>
        {
            _tabs.SelectedItem = _gameTab;
            if (_wizardLoader.SelectedIndex <= 0) _wizardLoader.SelectedIndex = 1;
            SelectTab();
        };
        _resourcePanel.Children.Add(_resourceBrowser.Root);

        BuildLayout();
        _shell.StateChanged += () => _resourceBrowser.RefreshTarget();
    }

    public FrameworkElement Root => _root;

    public void OnShown()
    {
        if (ReferenceEquals(_tabs.SelectedItem, _gameTab))
        {
            if (_manifest is null || !string.Equals(_manifestMode, _shell.Settings.Mirror, StringComparison.Ordinal))
                _ = EnsureManifestAsync(false);
            else
                RefreshList();
        }
        else
        {
            _resourceBrowser.OnShown();
        }
    }

    private void BuildLayout()
    {
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var header = new Grid { Padding = new Thickness(24, 18, 24, 0) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titles = new StackPanel { Spacing = 2 };
        var titleRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 9 };
        titleRow.Children.Add(McUi.GlyphIcon("\uE896", 26, _palette.Accent));
        titleRow.Children.Add(McUi.Text("下载与安装", 21, FontWeights.SemiBold, _palette.Text));
        titles.Children.Add(titleRow);
        titles.Children.Add(McUi.Text(
            "原版、Fabric、Quilt、Forge、NeoForge、模组与光影，全部一键完成。",
            12, FontWeights.Normal, _palette.Secondary, true));
        header.Children.Add(titles);

        var refresh = McUi.Button("刷新", _palette, glyph: "\uE72C", height: 40);
        refresh.VerticalAlignment = VerticalAlignment.Center;
        refresh.Click += async (_, _) =>
        {
            if (ReferenceEquals(_tabs.SelectedItem, _gameTab)) await EnsureManifestAsync(true);
            else _resourceBrowser.Refresh();
        };
        Grid.SetColumn(refresh, 1);
        header.Children.Add(refresh);
        _root.Children.Add(header);

        _tabs.Items.Add(_gameTab);
        _tabs.Items.Add(_modpackTab);
        _tabs.Items.Add(_modTab);
        _tabs.Items.Add(_shaderTab);
        _tabs.Items.Add(_resourcePackTab);
        _tabs.SelectedItem = _gameTab;
        _tabs.SelectionChanged += (_, _) => SelectTab();
        Grid.SetRow(_tabs, 1);
        _root.Children.Add(_tabs);

        BuildGamePanel();
        _pageScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _pageScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        _pageScroll.VerticalScrollMode = ScrollMode.Enabled;
        _pageScroll.HorizontalScrollMode = ScrollMode.Disabled;
        _pageScroll.Content = _gamePanel;
        Grid.SetRow(_pageScroll, 2);
        _root.Children.Add(_pageScroll);
        Grid.SetRow(_resourcePanel, 2);
        _resourcePanel.Visibility = Visibility.Collapsed;
        _root.Children.Add(_resourcePanel);
    }

    private void SelectTab()
    {
        var gameSelected = ReferenceEquals(_tabs.SelectedItem, _gameTab);
        _gamePanel.Visibility = gameSelected ? Visibility.Visible : Visibility.Collapsed;
        _resourcePanel.Visibility = gameSelected ? Visibility.Collapsed : Visibility.Visible;
        var type = ReferenceEquals(_tabs.SelectedItem, _modTab) ? "mod"
            : ReferenceEquals(_tabs.SelectedItem, _shaderTab) ? "shader"
            : ReferenceEquals(_tabs.SelectedItem, _modpackTab) ? "modpack" : "resourcepack";
        if (gameSelected)
        {
            if (_manifest is null) _ = EnsureManifestAsync(false);
            return;
        }

        if (!string.Equals(_resourceType, type, StringComparison.Ordinal))
        {
            _resourceType = type;
            _resourceBrowser.SetType(type);
        }
        else
        {
            _resourceBrowser.OnShown();
        }
    }

    private void BuildGamePanel()
    {
        _gamePanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _gamePanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _gamePanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _gamePanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _gamePanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _gamePanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _gamePanel.Padding = new Thickness(24, 0, 24, 0);
        var toolbar = new Grid { Margin = new Thickness(0, 0, 0, 10), ColumnSpacing = 10 };
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
        _count.TextTrimming = TextTrimming.CharacterEllipsis;
        Grid.SetColumn(_count, 2);
        toolbar.Children.Add(_count);
        _gamePanel.Children.Add(toolbar);

        _loading.Margin = new Thickness(0, 0, 0, 8);
        Grid.SetRow(_loading, 1);
        _gamePanel.Children.Add(_loading);

        _status.Margin = new Thickness(0, 0, 0, 10);
        Grid.SetRow(_status, 2);
        _gamePanel.Children.Add(_status);

        var wizard = BuildWizard();
        wizard.Margin = new Thickness(0, 0, 0, 10);
        Grid.SetRow(wizard, 3);
        _gamePanel.Children.Add(wizard);

        Grid.SetRow(_installCard, 4);
        _gamePanel.Children.Add(_installCard);

        _list.Margin = new Thickness(0, 4, 0, 16);
        Grid.SetRow(_list, 5);
        _gamePanel.Children.Add(_list);
    }

    private FrameworkElement BuildWizard()
    {
        var content = new StackPanel { Spacing = 10 };
        var rowTop = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            VerticalAlignment = VerticalAlignment.Bottom
        };
        rowTop.Children.Add(_wizardGame);
        rowTop.Children.Add(_wizardLoader);
        content.Children.Add(rowTop);
        var rowBottom = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 12,
            VerticalAlignment = VerticalAlignment.Bottom
        };
        rowBottom.Children.Add(_wizardLoaderVersion);
        rowBottom.Children.Add(_wizardInstall);
        content.Children.Add(rowBottom);
        _wizardStatus.Foreground = _palette.Secondary;
        content.Children.Add(_wizardStatus);
        content.Children.Add(McUi.Text(
            "选择版本和加载器后点击一次即可：启动器会自动补齐原版、加载器、依赖与资源文件；已存在的文件会跳过。",
            11.5, FontWeights.Normal, _palette.Tertiary, true));
        return McUi.Card(_palette, content, "一键安装", icon: "icon_download.png", padding: new Thickness(18, 14, 18, 16));
    }

    private Grid BuildInstallCard()
    {
        var content = new Grid { ColumnSpacing = 12 };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var stack = new StackPanel { Spacing = 4 };
        _installTitle.FontSize = 14;
        _installTitle.FontWeight = FontWeights.SemiBold;
        _installTitle.Foreground = _palette.Text;
        stack.Children.Add(_installTitle);
        _installStage.FontSize = 12;
        _installStage.Foreground = _palette.Secondary;
        stack.Children.Add(_installStage);
        _installBar.Margin = new Thickness(0, 2, 0, 2);
        stack.Children.Add(_installBar);
        _installCount.FontSize = 11;
        _installCount.Foreground = _palette.Tertiary;
        stack.Children.Add(_installCount);
        content.Children.Add(stack);
        _installCancel.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(_installCancel, 1);
        content.Children.Add(_installCancel);
        return McUi.Card(_palette, content, padding: new Thickness(16, 12, 16, 12));
    }

    private async Task EnsureManifestAsync(bool force)
    {
        if (_loadingManifest) return;
        _loadingManifest = true;
        _loading.Visibility = Visibility.Visible;
        _status.IsOpen = false;
        try
        {
            _manifest = await McVersionManifestClient.FetchAsync(McHttp.Client, _shell.Settings.Mirror,
                Path.Combine(_shell.DataDirectory, "cache"), force, CancellationToken.None);
            _manifestMode = _shell.Settings.Mirror;
            _shell.AppendLog($"已获取版本清单：{_manifest.Versions.Count} 个版本（{McMirror.DisplayName(_manifestMode)}）");
        }
        catch (Exception ex)
        {
            _status.Title = "获取版本清单失败";
            _status.Message = ex.Message;
            _status.Severity = InfoBarSeverity.Error;
            _status.IsOpen = true;
            _shell.AppendLog("获取版本清单失败：" + ex.Message);
        }
        finally
        {
            _loadingManifest = false;
            _loading.Visibility = Visibility.Collapsed;
            PopulateWizardGames();
            ResetAndRefresh();
        }
    }

    private void PopulateWizardGames()
    {
        if (_manifest is null) return;
        var previous = (_wizardGame.SelectedItem as ComboBoxItem)?.Tag as string;
        _wizardGame.Items.Clear();
        foreach (var version in _manifest.Versions.Where(version => version.Type == "release"))
            _wizardGame.Items.Add(new ComboBoxItem { Content = version.Id, Tag = version.Id });
        if (_wizardGame.Items.Count == 0) return;

        var preferred = previous ?? _shell.SelectedVersion?.VanillaName ?? _manifest.LatestRelease;
        var index = 0;
        for (var i = 0; i < _wizardGame.Items.Count; i++)
            if (_wizardGame.Items[i] is ComboBoxItem { Tag: string id } &&
                string.Equals(id, preferred, StringComparison.OrdinalIgnoreCase))
            {
                index = i;
                break;
            }

        _wizardGame.SelectedIndex = index;
        _ = LoadWizardLoaderVersionsAsync();
    }

    private async Task LoadWizardLoaderVersionsAsync()
    {
        if (_wizardGame.SelectedItem is not ComboBoxItem { Tag: string gameVersion }) return;
        var kind = SelectedLoaderKind();
        try { _wizardCts?.Cancel(); } catch (ObjectDisposedException) { }
        var cts = new CancellationTokenSource();
        _wizardCts = cts;
        _wizardLoaderVersion.Items.Clear();
        _wizardLoaderVersion.IsEnabled = kind != McLoaderKind.Vanilla;
        _wizardInstall.IsEnabled = false;

        if (kind == McLoaderKind.Vanilla)
        {
            _wizardLoaderVersion.PlaceholderText = "无需加载器";
            _wizardStatus.Text = $"将安装 Minecraft {gameVersion} 原版（含客户端、依赖与资源）。";
            _wizardInstall.IsEnabled = true;
            return;
        }

        _wizardStatus.Text = $"正在获取 {McLoaderInstaller.DisplayName(kind)} 支持 Minecraft {gameVersion} 的版本…";
        _wizardLoaderVersion.PlaceholderText = "正在获取…";
        try
        {
            var versions = await McLoaderInstaller.GetVersionsAsync(McHttp.Client, kind, gameVersion, cts.Token);
            if (cts.IsCancellationRequested) return;
            var firstRecommended = -1;
            for (var i = 0; i < Math.Min(versions.Count, 120); i++)
            {
                var version = versions[i];
                _wizardLoaderVersion.Items.Add(new ComboBoxItem
                {
                    Content = version.Version + (version.Recommended ? "（推荐）" : ""),
                    Tag = version.Version
                });
                if (firstRecommended < 0 && version.Recommended) firstRecommended = i;
            }

            if (_wizardLoaderVersion.Items.Count == 0)
            {
                _wizardStatus.Text = $"{McLoaderInstaller.DisplayName(kind)} 没有支持 Minecraft {gameVersion} 的版本，换一个加载器试试。";
                return;
            }

            _wizardLoaderVersion.SelectedIndex = firstRecommended >= 0 ? firstRecommended : 0;
            _wizardLoaderVersion.PlaceholderText = "选择版本";
            _wizardStatus.Text = $"将安装 Minecraft {gameVersion} + {McLoaderInstaller.DisplayName(kind)}，共 {versions.Count} 个可用版本。";
            _wizardInstall.IsEnabled = true;
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _wizardStatus.Text = $"获取加载器版本失败：{ex.Message}";
        }
    }

    private McLoaderKind SelectedLoaderKind()
    {
        var tag = (_wizardLoader.SelectedItem as ComboBoxItem)?.Tag as string ?? "vanilla";
        return tag switch
        {
            "fabric" => McLoaderKind.Fabric,
            "quilt" => McLoaderKind.Quilt,
            "forge" => McLoaderKind.Forge,
            "neoforge" => McLoaderKind.NeoForge,
            _ => McLoaderKind.Vanilla
        };
    }

    private async Task InstallWizardAsync()
    {
        if (_installCts is not null)
        {
            _shell.Notify("另一个安装任务正在进行，请先等待或取消。", InfoBarSeverity.Warning);
            return;
        }

        if (_wizardGame.SelectedItem is not ComboBoxItem { Tag: string gameVersion })
        {
            _shell.Notify("请先选择 Minecraft 版本。", InfoBarSeverity.Warning);
            return;
        }

        var kind = SelectedLoaderKind();
        string? loaderVersion = null;
        if (kind != McLoaderKind.Vanilla)
        {
            if (_wizardLoaderVersion.SelectedItem is not ComboBoxItem { Tag: string selectedLoader } ||
                string.IsNullOrWhiteSpace(selectedLoader))
            {
                _shell.Notify("加载器版本还没有就绪，请稍等或重新选择加载器。", InfoBarSeverity.Warning);
                return;
            }

            loaderVersion = selectedLoader;
        }

        var display = kind == McLoaderKind.Vanilla
            ? "原版 " + gameVersion
            : $"{McLoaderInstaller.DisplayName(kind)} {loaderVersion} · {gameVersion}";
        var requiredJava = 0;
        if (kind is McLoaderKind.Forge or McLoaderKind.NeoForge)
        {
            await _shell.EnsureJavaAsync(true);
            var manifest = _manifest?.Versions.FirstOrDefault(version =>
                string.Equals(version.Id, gameVersion, StringComparison.OrdinalIgnoreCase));
            requiredJava = McJava.RequiredMajor(new System.Text.Json.Nodes.JsonObject(),
                manifest?.ReleaseTime ?? DateTime.Today);
            var (java, _) = McJava.Select(_shell.Javas, requiredJava, _shell.Settings.JavaPath);
            if (java is null)
            {
                _shell.Notify($"安装 {McLoaderInstaller.DisplayName(kind)} 需要 Java，请先在设置页指定 java.exe。",
                    InfoBarSeverity.Error, "缺少 Java");
                _shell.ShowPage("settings");
                return;
            }
        }

        await RunInstallAsync("正在安装 " + display, (progress, token) =>
        {
            if (kind == McLoaderKind.Vanilla)
            {
                var summary = _manifest?.Versions.FirstOrDefault(version =>
                    string.Equals(version.Id, gameVersion, StringComparison.OrdinalIgnoreCase));
                if (summary is null) throw new InvalidDataException("版本清单尚未加载完成。");
                return McInstaller.InstallAsync(McHttp.Client, summary.Id, summary.Url,
                    _shell.Settings.GameDirectory, _shell.Settings.Mirror, McOsInfo.Current, progress,
                    line => _shell.AppendLog(line), token, _shell.Settings.VersionIsolation);
            }

            var (selectedJava, _) = McJava.Select(_shell.Javas, requiredJava, _shell.Settings.JavaPath);
            return McLoaderInstaller.InstallAsync(McHttp.Client, kind, gameVersion, loaderVersion,
                _shell.Settings.GameDirectory, _shell.Settings.Mirror, _shell.Settings.VersionIsolation,
                Path.Combine(_shell.DataDirectory, "cache"), selectedJava, progress,
                line => _shell.AppendLog(line), token);
        });
    }

    private void ResetAndRefresh()
    {
        _shown = PageSize;
        RefreshList();
    }

    public void RefreshList()
    {
        _list.Children.Clear();
        if (_manifest is null)
        {
            if (!_loadingManifest)
                _list.Children.Add(McUi.Text("正在等待版本清单，点击右上角「刷新」重试。", 13,
                    FontWeights.Normal, _palette.Secondary, true));
            return;
        }

        var query = _manifest.Versions.AsEnumerable();
        var filter = _filter.SelectedIndex;
        query = filter switch
        {
            0 => query.Where(version => version.Type == "release"),
            1 => query.Where(version => version.Type == "snapshot"),
            2 => query.Where(version => version.Type is "old_beta" or "old_alpha"),
            4 => query.Where(version => IsInstalled(version.Id)),
            _ => query
        };

        var search = _search.Text.Trim();
        if (search.Length > 0)
            query = query.Where(version => version.Id.Contains(search, StringComparison.OrdinalIgnoreCase));

        var all = query.ToList();
        _count.Text = $"{all.Count} 个版本 · 下载源：{McMirror.DisplayName(_shell.Settings.Mirror)}";
        if (all.Count == 0)
        {
            _list.Children.Add(McUi.Text("没有匹配的版本。", 13, FontWeights.Normal, _palette.Secondary));
            return;
        }

        foreach (var version in all.Take(_shown))
            _list.Children.Add(BuildRow(version));

        if (all.Count > _shown)
        {
            var more = McUi.Button($"加载更多（剩余 {all.Count - _shown}）", _palette);
            more.HorizontalAlignment = HorizontalAlignment.Center;
            more.Margin = new Thickness(0, 8, 0, 12);
            more.Click += (_, _) =>
            {
                _shown += PageSize;
                RefreshList();
            };
            _list.Children.Add(more);
        }
    }

    private Border BuildRow(McVersionSummary version)
    {
        var installed = IsInstalled(version.Id);
        var row = new Border
        {
            Background = _palette.Card,
            BorderBrush = _palette.CardStroke,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(14, 10, 12, 10)
        };
        row.PointerEntered += (_, _) => row.Background = _palette.CardHover;
        row.PointerExited += (_, _) => row.Background = _palette.Card;

        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(120) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var name = new StackPanel { Spacing = 1, VerticalAlignment = VerticalAlignment.Center };
        var line = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 7 };
        line.Children.Add(McUi.Text(version.Id, 14.5, FontWeights.SemiBold, _palette.Text));
        if (installed) line.Children.Add(McUi.Chip("已安装", _palette,
            new SolidColorBrush(McPalette.Color(0x2E, 0x6F, 0xCF, 0x7A)), _palette.Success));
        name.Children.Add(line);
        name.Children.Add(McUi.Text(TypeDescription(version.Type), 11.5, FontWeights.Normal, _palette.Tertiary));
        grid.Children.Add(name);

        var typeChip = McUi.Chip(TypeLabel(version.Type), _palette, TypeBackground(version.Type), TypeForeground(version.Type));
        typeChip.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(typeChip, 1);
        grid.Children.Add(typeChip);

        var date = McUi.Text(version.ReleaseTime.Year < 2000 ? "未知" : version.ReleaseTime.ToString("yyyy-MM-dd"),
            11.5, FontWeights.Normal, _palette.Secondary);
        date.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(date, 2);
        grid.Children.Add(date);

        var install = McUi.Button(installed ? "重新安装" : "安装", _palette, accent: !installed, height: 38,
            glyph: "\uE896");
        install.VerticalAlignment = VerticalAlignment.Center;
        install.IsEnabled = _installCts is null;
        ToolTipService.SetToolTip(install, installed
            ? "重新安装原版文件（加载器请使用上方一键安装）"
            : "安装原版；需要 Fabric / Forge 等加载器时使用上方一键安装");
        install.Click += async (_, _) => await InstallVanillaAsync(version);
        Grid.SetColumn(install, 3);
        grid.Children.Add(install);

        row.Child = grid;
        return row;
    }

    private bool IsInstalled(string id) =>
        Directory.Exists(Path.Combine(_shell.Settings.GameDirectory, "versions", id));

    private async Task InstallVanillaAsync(McVersionSummary version)
    {
        if (_installCts is not null)
        {
            _shell.Notify("另一个安装任务正在进行，请先等待或取消。", InfoBarSeverity.Warning);
            return;
        }

        await RunInstallAsync("正在安装原版 " + version.Id, (progress, token) =>
            McInstaller.InstallAsync(McHttp.Client, version.Id, version.Url,
                _shell.Settings.GameDirectory, _shell.Settings.Mirror, McOsInfo.Current, progress,
                line => _shell.AppendLog(line), token, _shell.Settings.VersionIsolation));
    }

    private async Task RunInstallAsync(string title,
        Func<IProgress<McInstallProgress>, CancellationToken, Task<McInstallResult>> operation)
    {
        _installCts = new CancellationTokenSource();
        _installCard.Visibility = Visibility.Visible;
        _installTitle.Text = title;
        _installStage.Text = "准备中…";
        _installBar.IsIndeterminate = true;
        _installBar.Value = 0;
        _installCount.Text = "";
        _installCancel.IsEnabled = true;
        _wizardInstall.IsEnabled = false;
        RefreshList();

        var token = _installCts.Token;
        var progress = new Progress<McInstallProgress>(value =>
        {
            _installStage.Text = value.Stage;
            _installBar.IsIndeterminate = value.Total <= 0;
            if (value.Total > 0)
                _installBar.Value = Math.Clamp(value.Done * 100.0 / value.Total, 0, 100);
            _installCount.Text = value.Total > 0
                ? $"{value.Done} / {value.Total} 个文件"
                : "";
        });

        try
        {
            var queueStatus = new Progress<string>(stage =>
            {
                _installStage.Text = stage;
                _installBar.IsIndeterminate = true;
            });
            var result = await McUnifiedDownloads.RunTaskAsync(
                title, "Minecraft 版本安装", _shell.Settings.GameDirectory,
                (status, queueToken) => operation(progress, queueToken),
                queueStatus, token);
            await _shell.RefreshVersionsAsync(true);
            var installedId = result.VersionJsonPath is { } path ? Path.GetFileNameWithoutExtension(path) : "";
            var installed = _shell.Versions.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, installedId, StringComparison.OrdinalIgnoreCase));
            if (installed is not null) _shell.SelectVersion(installed);

            _shell.AppendLog($"安装完成：{installedId}，失败 {result.Failures.Count} 个文件");
            if (result.Failures.Count == 0)
            {
                _wizardStatus.Text = $"安装完成：{installedId}。已自动选中该版本，可以回到「启动」页开始游戏。";
                _shell.Notify($"《{installedId}》安装完成。", InfoBarSeverity.Success, "安装成功");
            }
            else
            {
                _shell.Notify($"《{installedId}》已安装，但有 {result.Failures.Count} 个文件被跳过。",
                    InfoBarSeverity.Warning, "安装完成（部分文件跳过）");
                if (_root.XamlRoot is not null)
                    await McDialogs.ShowFailuresAsync(_root.XamlRoot, _palette, _root.ActualTheme,
                        "部分文件下载失败", result.Failures);
            }

            foreach (var note in result.Notes) _shell.AppendLog("[安装] " + note);
        }
        catch (OperationCanceledException)
        {
            _shell.AppendLog("安装已取消");
            _shell.Notify("已取消安装。", InfoBarSeverity.Warning);
        }
        catch (Exception ex)
        {
            _shell.AppendLog("安装失败：" + ex);
            _shell.Notify(ex.Message, InfoBarSeverity.Error, "安装失败");
            if (ex.Message.Contains('\n') && _root.XamlRoot is not null)
                await McDialogs.ShowTextAsync(_root.XamlRoot, _palette, _root.ActualTheme,
                    "安装失败", ex.ToString(), 420);
        }
        finally
        {
            _installCts.Dispose();
            _installCts = null;
            _installCard.Visibility = Visibility.Collapsed;
            _wizardInstall.IsEnabled = true;
            RefreshList();
        }
    }

    private static string TypeLabel(string type) => type switch
    {
        "release" => "正式版",
        "snapshot" => "快照",
        "old_beta" => "Beta",
        "old_alpha" => "Alpha",
        _ => type
    };

    private static string TypeDescription(string type) => type switch
    {
        "release" => "正式发布版本",
        "snapshot" => "开发快照，可能不稳定",
        "old_beta" => "远古 Beta 版本",
        "old_alpha" => "远古 Alpha 版本",
        _ => type
    };

    private Brush TypeBackground(string type) => type switch
    {
        "release" => new SolidColorBrush(McPalette.Color(0x2E, 0x6F, 0xCF, 0x7A)),
        "snapshot" => new SolidColorBrush(McPalette.Color(0x2E, 0xF5, 0xC4, 0x51)),
        _ => new SolidColorBrush(McPalette.Color(0x1F, 0xFF, 0xFF, 0xFF))
    };

    private Brush TypeForeground(string type) => type switch
    {
        "release" => _palette.Success,
        "snapshot" => _palette.Warning,
        _ => _palette.Secondary
    };
}

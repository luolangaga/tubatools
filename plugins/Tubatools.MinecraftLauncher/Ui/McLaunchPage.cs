using System.Diagnostics;
using System.Text;
using Microsoft.UI;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using TubaWinUi3.PluginSdk;
using Tubatools.MinecraftLauncher.Services;

namespace Tubatools.MinecraftLauncher.Ui;

/// <summary>
/// PCL CE 启动页：左侧 300px 面板（账户卡片 / 大号启动按钮 / 选择版本、版本设置），
/// 右侧卡片流（版本信息 / 你知道吗 / 启动日志）。
/// </summary>
internal sealed class McLaunchPage : IMcPage
{
    private readonly McLauncherView _shell;
    private readonly McPalette _palette;
    private readonly Grid _root = new();

    private readonly Grid _readyPanel = new();
    private readonly Grid _launchingPanel = new() { Visibility = Visibility.Collapsed };
    private readonly Button _accountButton;
    private readonly Border _avatarHost = new()
    {
        Width = 50, Height = 50, CornerRadius = new CornerRadius(25), VerticalAlignment = VerticalAlignment.Center
    };
    private readonly TextBlock _accountName = new();
    private readonly TextBlock _accountType = new();
    private readonly Button _launchButton;
    private readonly TextBlock _versionLabel;
    private readonly Button _selectVersionButton;
    private readonly Button _versionSettingsButton;
    private readonly StackPanel _runningPanel = new()
    {
        Orientation = Orientation.Horizontal,
        Spacing = 6,
        HorizontalAlignment = HorizontalAlignment.Center,
        Visibility = Visibility.Collapsed
    };
    private readonly TextBlock _runningText = new() { FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center };

    private readonly ProgressRing _launchRing = new() { IsActive = true, Width = 44, Height = 44 };
    private readonly TextBlock _launchTitle;
    private readonly TextBlock _launchVersion;
    private readonly ProgressBar _launchProgress = new() { Height = 4, Width = 230, IsIndeterminate = true };
    private readonly TextBlock _launchStage;
    private readonly TextBlock _launchDetail;
    private readonly Button _cancelButton;

    private readonly TextBlock _hintText;
    private readonly StackPanel _infoRows = new() { Spacing = 9 };
    private readonly TextBlock _logText = new()
    {
        FontFamily = new FontFamily("Consolas"),
        FontSize = 11.5,
        TextWrapping = TextWrapping.Wrap,
        IsTextSelectionEnabled = true
    };
    private readonly ScrollViewer _logScroll = new()
    {
        Height = 240,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        VerticalScrollMode = ScrollMode.Enabled,
        HorizontalScrollMode = ScrollMode.Disabled
    };
    private readonly List<string> _logLines = [];
    private readonly Grid _infoCard;
    private readonly Grid _quickStartCard;
    private readonly Grid _heroCard;
    private readonly TextBlock _heroTitle = new();
    private readonly TextBlock _heroSubtitle = new();
    private readonly Button _heroButton;
    private readonly ScrollViewer _rightScroll = new();

    private Process? _process;
    private CancellationTokenSource? _launchCts;
    private bool _autoScrollLog = true;
    private bool _showingLauncher;
    private DateTime _launchStarted;

    public McLaunchPage(McLauncherView shell)
    {
        _shell = shell;
        _palette = shell.Palette;

        _accountButton = McUi.CustomButton(BuildAccountContent(), _palette, height: 88,
            padding: new Thickness(16, 0, 16, 0), radius: 4);
        _accountButton.Width = 264;
        _accountButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        _accountButton.Click += async (_, _) => await OpenAccountDialogAsync();

        _launchButton = McUi.Button("启动游戏", _palette, accent: true, height: 60,
            icon: "icon_play.png", fontSize: 19);
        _launchButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        _launchButton.Click += async (_, _) => await LaunchAsync();

        _versionLabel = McUi.Text("未选择版本", 11, FontWeights.Normal, _palette.Tertiary);
        _versionLabel.HorizontalAlignment = HorizontalAlignment.Center;

        _selectVersionButton = McUi.Button("选择版本", _palette, height: 42, icon: "icon_grass.png");
        _selectVersionButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        _selectVersionButton.Click += (_, _) => _shell.ShowPage("versions");

        _versionSettingsButton = McUi.Button("版本设置", _palette, height: 42, icon: "icon_settings.png");
        _versionSettingsButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        _versionSettingsButton.Click += async (_, _) => await ShowVersionSettingsAsync();
        ToolTipService.SetToolTip(_versionSettingsButton, "查看版本信息与本地文件");

        _launchTitle = McUi.Text("正在启动游戏", 20, FontWeights.SemiBold, _palette.Accent);
        _launchTitle.HorizontalAlignment = HorizontalAlignment.Center;
        _launchVersion = McUi.Text("", 13, FontWeights.Normal, _palette.Text);
        _launchVersion.HorizontalAlignment = HorizontalAlignment.Center;
        _launchVersion.TextWrapping = TextWrapping.Wrap;
        _launchStage = McUi.Text("正在检查游戏文件…", 12, FontWeights.Normal, _palette.Secondary);
        _launchStage.HorizontalAlignment = HorizontalAlignment.Center;
        _launchDetail = McUi.Text("离线登录", 11.5, FontWeights.Normal, _palette.Tertiary);
        _launchDetail.HorizontalAlignment = HorizontalAlignment.Center;
        _cancelButton = McUi.Button("取消启动", _palette, height: 40);
        _cancelButton.Click += (_, _) => CancelLaunch();

        _hintText = McUi.Text(McTips.Random(), 13, FontWeights.Normal, _palette.Text, true);
        _logText.Foreground = _palette.ConsoleText;
        _heroButton = McUi.Button("启动游戏", _palette, accent: true, height: 54, icon: "icon_play.png", fontSize: 15);
        _heroButton.MinWidth = 148;
        _heroButton.HorizontalAlignment = HorizontalAlignment.Right;
        _heroButton.VerticalAlignment = VerticalAlignment.Center;
        _heroButton.Click += async (_, _) => await LaunchAsync();
        _heroCard = BuildHeroCard();
        _infoCard = McUi.Card(_palette, _infoRows, "版本信息", icon: "icon_check.png");
        _quickStartCard = BuildQuickStartCard();
        _quickStartCard.Visibility = Visibility.Collapsed;

        BuildLayout();
        _shell.StateChanged += Refresh;
        _shell.LaunchingChanged += Refresh;

        if (_shell.Log.Snapshot.Count > 0)
        {
            foreach (var line in _shell.Log.Snapshot) AppendRaw(line);
        }

        Refresh();
    }

    public FrameworkElement Root => _root;

    public void OnShown() => Refresh();

    private void BuildLayout()
    {
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(300) });
        _root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var left = new Border
        {
            Background = _palette.Panel,
            BorderBrush = _palette.CardStroke,
            BorderThickness = new Thickness(0, 0, 1, 0)
        };
        var leftGrid = new Grid();
        var watermark = McArt.BrandIcon(168);
        watermark.Opacity = 0.055;
        watermark.HorizontalAlignment = HorizontalAlignment.Center;
        watermark.VerticalAlignment = VerticalAlignment.Center;
        leftGrid.Children.Add(watermark);
        leftGrid.Children.Add(BuildReadyPanel());
        leftGrid.Children.Add(BuildLaunchingPanel());
        left.Child = leftGrid;
        _root.Children.Add(left);

        BuildRightPanel();
        Grid.SetColumn(_rightScroll, 1);
        _root.Children.Add(_rightScroll);
    }

    private Grid BuildReadyPanel()
    {
        _readyPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _readyPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _readyPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _readyPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _readyPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _readyPanel.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _readyPanel.RowDefinitions.Add(new RowDefinition { Height = new GridLength(38) });

        var accountHost = new Grid { Margin = new Thickness(18, 18, 18, 0) };
        accountHost.Children.Add(_accountButton);
        _readyPanel.Children.Add(accountHost);

        _launchButton.Margin = new Thickness(18, 0, 18, 0);
        Grid.SetRow(_launchButton, 2);
        _readyPanel.Children.Add(_launchButton);

        _versionLabel.Margin = new Thickness(22, 7, 22, 0);
        Grid.SetRow(_versionLabel, 3);
        _readyPanel.Children.Add(_versionLabel);

        BuildRunningPanel();
        _runningPanel.Margin = new Thickness(18, 8, 18, 0);
        Grid.SetRow(_runningPanel, 4);
        _readyPanel.Children.Add(_runningPanel);

        var buttons = new Grid { Margin = new Thickness(18, 12, 18, 0), ColumnSpacing = 10 };
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        buttons.Children.Add(_selectVersionButton);
        Grid.SetColumn(_versionSettingsButton, 1);
        buttons.Children.Add(_versionSettingsButton);
        Grid.SetRow(buttons, 5);
        _readyPanel.Children.Add(buttons);
        return _readyPanel;
    }

    private void BuildRunningPanel()
    {
        var dot = new Microsoft.UI.Xaml.Shapes.Ellipse
        {
            Width = 7,
            Height = 7,
            Fill = _palette.Success,
            VerticalAlignment = VerticalAlignment.Center
        };
        _runningText.Foreground = _palette.Secondary;
        var end = new HyperlinkButton
        {
            Content = "结束游戏",
            FontSize = 11.5,
            Padding = new Thickness(2, 0, 2, 0),
            Foreground = _palette.Gold
        };
        end.Click += (_, _) => EndGame();
        _runningPanel.Children.Add(dot);
        _runningPanel.Children.Add(_runningText);
        _runningPanel.Children.Add(end);
    }

    private UIElement BuildLaunchingPanel()
    {
        var stack = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            Spacing = 10,
            Margin = new Thickness(18)
        };
        _launchRing.HorizontalAlignment = HorizontalAlignment.Center;
        stack.Children.Add(_launchRing);
        stack.Children.Add(_launchTitle);
        stack.Children.Add(_launchVersion);
        _launchProgress.HorizontalAlignment = HorizontalAlignment.Center;
        _launchProgress.Margin = new Thickness(0, 4, 0, 2);
        stack.Children.Add(_launchProgress);
        stack.Children.Add(_launchStage);
        stack.Children.Add(_launchDetail);
        var cancelHost = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
        _cancelButton.HorizontalAlignment = HorizontalAlignment.Stretch;
        cancelHost.Children.Add(_cancelButton);
        stack.Children.Add(cancelHost);

        _launchingPanel.Children.Add(stack);
        return _launchingPanel;
    }

    private void BuildRightPanel()
    {
        _rightScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _rightScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        _rightScroll.VerticalScrollMode = ScrollMode.Enabled;
        _rightScroll.HorizontalScrollMode = ScrollMode.Disabled;
        _rightScroll.Padding = new Thickness(22, 20, 22, 12);

        var content = new StackPanel
        {
            Spacing = 16,
            MaxWidth = 1040,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        content.Children.Add(_heroCard);
        content.Children.Add(_quickStartCard);

        var hintRefresh = McUi.PixelIconButton("icon_refresh.png", "换一条", _palette, 26);
        hintRefresh.Click += (_, _) => _hintText.Text = McTips.Random();
        var hintContent = new StackPanel { Spacing = 0 };
        hintContent.Children.Add(_hintText);
        content.Children.Add(McUi.Card(_palette, hintContent, "你知道吗", hintRefresh, icon: "icon_grass.png"));

        content.Children.Add(_infoCard);
        content.Children.Add(BuildLogCard());
        _rightScroll.Content = content;
    }

    private UIElement BuildLogCard()
    {
        _logScroll.Content = _logText;
        _logScroll.ViewChanged += (_, _) =>
        {
            if (_logScroll.ScrollableHeight <= 0) return;
            _autoScrollLog = _logScroll.VerticalOffset >= _logScroll.ScrollableHeight - 24;
        };

        var actions = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 3,
            VerticalAlignment = VerticalAlignment.Center
        };
        var copy = McUi.PixelIconButton("icon_copy.png", "复制日志", _palette, 26);
        copy.Click += async (_, _) =>
        {
            try { await _shell.Host.Services.CopyTextAsync(_logText.Text); _shell.Notify("启动日志已复制。"); }
            catch (Exception ex) { _shell.Notify("复制失败：" + ex.Message, InfoBarSeverity.Warning); }
        };
        var clear = McUi.PixelIconButton("icon_trash.png", "清空日志", _palette, 26);
        clear.Click += (_, _) => { _logLines.Clear(); _logText.Text = ""; };
        var script = McUi.PixelIconButton("icon_script.png", "打开启动脚本", _palette, 26);
        script.Click += (_, _) => OpenLaunchScript();
        actions.Children.Add(copy);
        actions.Children.Add(clear);
        actions.Children.Add(script);

        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(new Border
        {
            Background = _palette.ConsoleBackground,
            BorderBrush = _palette.ConsoleBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12, 10, 12, 10),
            Child = _logScroll
        });
        content.Children.Add(McUi.Text("启动日志同时写入插件数据目录的 logs/。", 11,
            FontWeights.Normal, _palette.Tertiary));
        return McUi.Card(_palette, content, "启动日志", actions, icon: "icon_script.png");
    }

    private Grid BuildHeroCard()
    {
        var root = new Grid { Height = 164, CornerRadius = new CornerRadius(4) };
        root.Children.Add(new Border
        {
            Background = McArt.Brush("hero.png"),
            CornerRadius = new CornerRadius(4),
            BorderBrush = _palette.CardStroke,
            BorderThickness = new Thickness(1)
        });
        var shade = new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0),
            EndPoint = new Windows.Foundation.Point(1, 0)
        };
        shade.GradientStops.Add(new GradientStop { Color = McPalette.Color(0x55, 0x05, 0x07, 0x0A), Offset = 0 });
        shade.GradientStops.Add(new GradientStop { Color = McPalette.Color(0xC8, 0x05, 0x07, 0x0A), Offset = 0.58 });
        shade.GradientStops.Add(new GradientStop { Color = McPalette.Color(0xF0, 0x05, 0x07, 0x0A), Offset = 1 });
        root.Children.Add(new Border { Background = shade, CornerRadius = new CornerRadius(4) });

        var content = new Grid { Padding = new Thickness(24, 20, 22, 20), ColumnSpacing = 20 };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var left = new StackPanel { Spacing = 5, VerticalAlignment = VerticalAlignment.Center };
        left.Children.Add(McUi.Text("MINECRAFT · JAVA EDITION", 9.5, FontWeights.SemiBold, _palette.Secondary));
        _heroTitle.FontSize = 27;
        _heroTitle.FontWeight = FontWeights.SemiBold;
        _heroTitle.Foreground = new SolidColorBrush(Microsoft.UI.Colors.White);
        _heroTitle.TextTrimming = TextTrimming.CharacterEllipsis;
        left.Children.Add(_heroTitle);
        _heroSubtitle.FontSize = 12.5;
        _heroSubtitle.Foreground = _palette.Secondary;
        _heroSubtitle.TextWrapping = TextWrapping.Wrap;
        left.Children.Add(_heroSubtitle);
        content.Children.Add(left);
        Grid.SetColumn(_heroButton, 1);
        content.Children.Add(_heroButton);
        root.Children.Add(content);
        return root;
    }

    private Grid BuildQuickStartCard()
    {
        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(McUi.Text("还没有扫描到任何 Minecraft 版本。先去「下载」页安装一个版本，或在游戏目录的 versions 文件夹里放入已安装版本。",
            12.5, FontWeights.Normal, _palette.Secondary, true));
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var download = McUi.Button("前往下载页", _palette, accent: true, icon: "icon_download.png");
        download.Click += (_, _) => _shell.ShowPage("download");
        row.Children.Add(download);
        var folder = McUi.Button("打开游戏目录", _palette, icon: "icon_folder.png");
        folder.Click += (_, _) => McLauncherView.OpenFolder(_shell.Settings.GameDirectory);
        row.Children.Add(folder);
        content.Children.Add(row);
        return McUi.Card(_palette, content, "快速开始", icon: "icon_grass.png");
    }

    private UIElement BuildAccountContent()
    {
        _avatarHost.BorderThickness = new Thickness(0);
        var grid = new Grid { ColumnSpacing = 11 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _avatarHost.VerticalAlignment = VerticalAlignment.Center;
        grid.Children.Add(_avatarHost);

        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        _accountName.FontSize = 15;
        _accountName.FontWeight = FontWeights.SemiBold;
        _accountName.Foreground = _palette.Text;
        _accountName.TextTrimming = TextTrimming.CharacterEllipsis;
        _accountType.FontSize = 11.5;
        _accountType.Foreground = _palette.Secondary;
        _accountType.TextTrimming = TextTrimming.CharacterEllipsis;
        text.Children.Add(_accountName);
        text.Children.Add(_accountType);
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        var chevron = new FontIcon { Glyph = "\uE76C", FontSize = 12, Foreground = _palette.Tertiary };
        chevron.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(chevron, 2);
        grid.Children.Add(chevron);
        return grid;
    }

    public void Refresh()
    {
        var version = _shell.SelectedVersion;
        var account = _shell.Accounts.Selected;
        var launching = _shell.IsLaunching;

        if (!launching && !_showingLauncher)
        {
            _readyPanel.Visibility = Visibility.Visible;
            _launchingPanel.Visibility = Visibility.Collapsed;
        }

        var running = _process is { HasExited: false };
        _runningPanel.Visibility = running ? Visibility.Visible : Visibility.Collapsed;
        if (running) _runningText.Text = $"游戏运行中 · PID {_process!.Id}";

        _heroTitle.Text = version?.Id ?? "未选择版本";
        _heroSubtitle.Text = version is null
            ? "从左侧「选择版本」开始，或在下载页安装新版本。"
            : $"Minecraft {version.VanillaName} · {(version.Loaders.Length == 0 ? "原版" : version.Loaders)} · {version.TypeLabel}" +
              (account is null ? "" : $" · {account.Name}");
        _heroButton.IsEnabled = version is not null && !launching && !running;
        _heroCard.Visibility = Visibility.Visible;

        var avatarName = account?.Name ?? "?";
        _avatarHost.Background = new ImageBrush
        {
            ImageSource = McAvatar.Create(avatarName, 64),
            Stretch = Stretch.UniformToFill
        };
        _accountName.Text = account?.Name ?? "未登录";
        _accountType.Text = account is null
            ? "点击添加微软或离线账户"
            : $"{account.TypeLabel} · {account.Uuid[..Math.Min(8, account.Uuid.Length)]}";

        _launchButton.IsEnabled = version is not null && !launching && !running;
        _versionLabel.Text = version?.Id ?? "未选择版本";
        _versionLabel.Foreground = version is null ? _palette.Tertiary : _palette.Secondary;
        _versionSettingsButton.IsEnabled = version is not null;
        _quickStartCard.Visibility = _shell.Versions.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        BuildInfoRows(version, account);
    }

    private void BuildInfoRows(McVersion? version, McAccount? account)
    {
        _infoRows.Children.Clear();
        if (version is null)
        {
            _infoRows.Children.Add(McUi.Text("从左侧「选择版本」开始，或在下载页安装新版本。", 12.5,
                FontWeights.Normal, _palette.Secondary, true));
            var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 4, 0, 0) };
            var choose = McUi.Button("选择版本", _palette, accent: true, height: 38);
            choose.Click += (_, _) => _shell.ShowPage("versions");
            row.Children.Add(choose);
            var download = McUi.Button("下载版本", _palette, height: 38);
            download.Click += (_, _) => _shell.ShowPage("download");
            row.Children.Add(download);
            _infoRows.Children.Add(row);
            return;
        }

        _infoRows.Children.Add(McUi.KeyValueRow("实例", version.Id, _palette));
        _infoRows.Children.Add(McUi.KeyValueRow("游戏版本", "Minecraft " + version.VanillaName, _palette));
        _infoRows.Children.Add(McUi.KeyValueRow("加载器", version.Loaders.Length == 0 ? "原版" : version.Loaders, _palette));
        _infoRows.Children.Add(McUi.KeyValueRow("版本类型", version.TypeLabel, _palette));
        _infoRows.Children.Add(McUi.KeyValueRow("发布时间", McUi.FormatDate(version.ReleaseTime), _palette));
        _infoRows.Children.Add(McUi.KeyValueRow("完整性",
            version.HasClientJar ? "客户端 JAR 已就绪" : "缺少客户端 JAR，请重新安装", _palette));
        _infoRows.Children.Add(McUi.KeyValueRow("游戏目录",
            McVersionStore.ResolveGameDirectory(version, _shell.Settings), _palette, true));
        var required = McJava.RequiredMajor(version.Json, version.ReleaseTime);
        var (java, note) = McJava.Select(_shell.Javas, required, _shell.Settings.JavaPath);
        _infoRows.Children.Add(McUi.KeyValueRow("Java",
            java is null ? $"未检测到（需要 Java {required}）" : java.Display + (note.Length > 0 ? "（" + note + "）" : ""),
            _palette));
        _infoRows.Children.Add(McUi.KeyValueRow("账户", account is null
            ? "未登录"
            : $"{account.Name}（{account.TypeLabel}）", _palette));
        _infoRows.Children.Add(McUi.KeyValueRow("下载源", McMirror.DisplayName(_shell.Settings.Mirror), _palette));

        if (_shell.VersionProblems.Count > 0)
        {
            var warn = new Border
            {
                Background = new SolidColorBrush(McPalette.Color(0x18, 0xF3, 0x9C, 0x12)),
                CornerRadius = new CornerRadius(4),
                Padding = new Thickness(9, 6, 9, 6),
                Margin = new Thickness(0, 3, 0, 0),
                Child = McUi.Text($"versions 目录里有 {_shell.VersionProblems.Count} 个版本无法读取：" +
                                  string.Join("；", _shell.VersionProblems.Take(3)), 11.5, FontWeights.Normal,
                    _palette.Secondary, true)
            };
            _infoRows.Children.Add(warn);
        }
    }

    private async Task OpenAccountDialogAsync()
    {
        await _shell.ShowAccountDialogAsync();
        Refresh();
    }

    private async Task ShowVersionSettingsAsync()
    {
        if (_shell.SelectedVersion is not { } version) return;
        if (_root.XamlRoot is null) return;
        await McDialogs.ShowVersionAsync(_root.XamlRoot, _palette, _root.ActualTheme, version);
    }

    public async Task LaunchAsync()
    {
        if (_shell.IsLaunching) return;
        if (_process is { HasExited: false })
        {
            _shell.Notify("已经有一个游戏在运行，请先结束它再启动新的实例。", InfoBarSeverity.Warning, "游戏运行中");
            return;
        }

        var version = _shell.SelectedVersion;
        if (version is null)
        {
            _shell.Notify("请先选择一个版本。", InfoBarSeverity.Warning, "无法启动");
            _shell.ShowPage("versions");
            return;
        }

        if (_shell.Accounts.Selected is null)
        {
            if (!await _shell.ShowAccountDialogAsync())
            {
                _shell.Notify("添加一个微软或离线账户后才能启动游戏。", InfoBarSeverity.Warning, "需要账户");
                return;
            }

            Refresh();
        }

        var account = _shell.Accounts.Selected;
        if (account is null)
        {
            _shell.Notify("添加一个微软或离线账户后才能启动游戏。", InfoBarSeverity.Warning, "需要账户");
            return;
        }

        if (account.IsMicrosoft)
        {
            var refreshed = await _shell.EnsureAccountTokenAsync(account);
            if (refreshed is null)
            {
                await _shell.ShowAccountDialogAsync();
                return;
            }

            account = refreshed;
        }
        if (!version.HasClientJar)
        {
            _shell.Notify("该版本缺少客户端 JAR，请先在下载页重新安装。", InfoBarSeverity.Error, "文件不完整");
            return;
        }

        if (_shell.Javas.Count == 0) await _shell.EnsureJavaAsync(true);
        var required = McJava.RequiredMajor(version.Json, version.ReleaseTime);
        var (java, note) = McJava.Select(_shell.Javas, required, _shell.Settings.JavaPath);
        if (java is null)
        {
            _shell.Notify($"没有可用的 Java。需要 Java {required}，请在设置页指定 java.exe。", InfoBarSeverity.Error, "未找到 Java");
            _shell.ShowPage("settings");
            return;
        }

        if (note.Length > 0)
        {
            _shell.AppendLog("[提示] " + note);
            _shell.Notify(note, InfoBarSeverity.Warning, "Java 版本提示");
        }

        _launchCts = new CancellationTokenSource();
        var token = _launchCts.Token;
        _launchStarted = DateTime.Now;
        _shell.SetLaunching(true);
        ShowLaunchingPanel(version, account, java, note);
        var stageProgress = new Progress<string>(stage =>
        {
            _launchStage.Text = "当前步骤：" + stage;
            _shell.AppendLog("[阶段] " + stage);
        });

        try
        {
            var plan = await McGameLauncher.PrepareAsync(version, account, java, _shell.Settings,
                stageProgress, _shell.AppendLog, token);
            token.ThrowIfCancellationRequested();
            _launchStage.Text = "当前步骤：启动游戏进程";
            _launchProgress.IsIndeterminate = true;
            var process = McGameLauncher.Start(plan, line =>
                _shell.Dispatcher.TryEnqueue(() => _shell.AppendLog(line)));
            _process = process;
            _shell.AppendLog($"[启动] 游戏进程已启动，PID {process.Id}");
            _shell.AppendLog($"[启动] 启动脚本：{plan.ScriptPath}");
            _launchRing.IsActive = false;
            _launchProgress.IsIndeterminate = false;
            _launchProgress.Value = 100;
            _launchStage.Text = "游戏已启动，正在等待窗口…";
            _launchTitle.Text = "游戏已启动";
            _shell.Notify($"《{version.Id}》已启动，祝游玩愉快。", InfoBarSeverity.Success, "启动成功");

            var started = DateTime.Now;
            _ = MonitorExitAsync(process, version, started);
            _ = CompleteStartupAsync();
        }
        catch (OperationCanceledException)
        {
            _shell.AppendLog("已取消启动");
            _shell.Notify("已取消启动。", InfoBarSeverity.Warning);
            ResetLaunching();
        }
        catch (Exception ex)
        {
            _shell.AppendLog("启动失败：" + ex);
            _shell.Notify(ex.Message, InfoBarSeverity.Error, "启动失败");
            ResetLaunching();
            if (_root.XamlRoot is not null)
                await McDialogs.ShowTextAsync(_root.XamlRoot, _palette, _root.ActualTheme,
                    "启动失败", ex.ToString(), 420);
        }
    }

    private async Task CompleteStartupAsync()
    {
        await Task.Delay(1200);
        if (_shell.IsLaunching) ResetLaunching();
        Refresh();
    }

    private async Task MonitorExitAsync(Process process, McVersion version, DateTime started)
    {
        try
        {
            await process.WaitForExitAsync();
            var code = process.ExitCode;
            var duration = DateTime.Now - started;
            _shell.AppendLog($"[游戏] 进程已退出，退出代码 {code}，运行时长 {duration.TotalSeconds:F0} 秒");
            if (duration.TotalSeconds < 15 && code != 0)
            {
                _shell.Notify($"游戏很快退出（代码 {code}）。请查看启动日志或游戏目录的 logs。",
                    InfoBarSeverity.Error, "游戏异常退出");
            }
            else
            {
                _shell.Notify("游戏已退出。", InfoBarSeverity.Informational);
            }
        }
        catch (Exception ex)
        {
            _shell.AppendLog("等待游戏进程退出时出错：" + ex.Message);
        }
        finally
        {
            _process = null;
            ResetLaunching();
            _shell.Dispatcher.TryEnqueue(Refresh);
        }
    }

    private void ShowLaunchingPanel(McVersion version, McAccount account, JavaRuntime java, string note)
    {
        _showingLauncher = true;
        _readyPanel.Visibility = Visibility.Collapsed;
        _launchingPanel.Visibility = Visibility.Visible;
        _launchRing.IsActive = true;
        _launchProgress.IsIndeterminate = true;
        _launchProgress.Value = 0;
        _launchTitle.Text = "正在启动游戏";
        _launchVersion.Text = version.Id;
        _launchStage.Text = "当前步骤：检查游戏文件";
        _launchDetail.Text = $"{account.TypeLabel} · {java.Display}" + (note.Length > 0 ? $"（{note}）" : "");
        _shell.AppendLog($"[启动] 使用账户 {account.Name}（{account.TypeLabel}）");
    }

    private void ResetLaunching()
    {
        _showingLauncher = false;
        _readyPanel.Visibility = Visibility.Visible;
        _launchingPanel.Visibility = Visibility.Collapsed;
        _launchRing.IsActive = false;
        _launchCts?.Dispose();
        _launchCts = null;
        _shell.SetLaunching(false);
        Refresh();
    }

    public void CancelLaunch()
    {
        _launchCts?.Cancel();
        try
        {
            if (_process is { HasExited: false })
            {
                _process.Kill(true);
                _shell.AppendLog("已强制结束游戏进程");
            }
        }
        catch (Exception ex)
        {
            _shell.AppendLog("结束进程失败：" + ex.Message);
        }
    }

    private void EndGame()
    {
        try
        {
            if (_process is { HasExited: false } process)
            {
                process.Kill(true);
                _shell.AppendLog("已结束游戏进程");
                _shell.Notify("已结束游戏。", InfoBarSeverity.Informational);
            }
        }
        catch (Exception ex)
        {
            _shell.AppendLog("结束进程失败：" + ex.Message);
            _shell.Notify("结束进程失败：" + ex.Message, InfoBarSeverity.Warning);
        }
    }

    public void AppendLogLine(string line)
    {
        AppendRaw(line);
        if (_autoScrollLog)
            _logScroll.ChangeView(null, _logScroll.ScrollableHeight, null, true);
    }

    private void AppendRaw(string line)
    {
        _logLines.Add(line);
        if (_logLines.Count > 2500)
            _logLines.RemoveRange(0, _logLines.Count - 2000);
        var builder = new StringBuilder();
        foreach (var item in _logLines)
            builder.AppendLine(item);
        _logText.Text = builder.ToString();
    }

    private void OpenLaunchScript()
    {
        try
        {
            var path = Path.Combine(_shell.DataDirectory, "last-launch.bat");
            if (!File.Exists(path))
            {
                _shell.Notify("还没有生成启动脚本，先启动一次游戏。", InfoBarSeverity.Warning);
                return;
            }

            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            _shell.Notify("打开启动脚本失败：" + ex.Message, InfoBarSeverity.Warning);
        }
    }
}

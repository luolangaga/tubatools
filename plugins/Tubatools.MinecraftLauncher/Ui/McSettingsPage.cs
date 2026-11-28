using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Tubatools.MinecraftLauncher.Services;

namespace Tubatools.MinecraftLauncher.Ui;

/// <summary>设置页：游戏目录、Java、内存、启动选项、下载源与账户。</summary>
internal sealed class McSettingsPage : IMcPage
{
    private readonly McLauncherView _shell;
    private readonly McPalette _palette;
    private readonly Grid _root = new();
    private readonly StackPanel _cards = new() { Spacing = 15, MaxWidth = 900, HorizontalAlignment = HorizontalAlignment.Stretch };

    private readonly TextBox _gameDirectory = new() { Header = "游戏目录（.minecraft）" };
    private readonly ComboBox _javaBox = new() { Header = "Java 运行时", HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock _javaInfo = new() { FontSize = 11.5, TextWrapping = TextWrapping.Wrap };
    private readonly Slider _memorySlider = new() { Minimum = 512, Maximum = 32768, StepFrequency = 256 };
    private readonly NumberBox _memoryBox = new()
    {
        Minimum = 512,
        Maximum = 32768,
        SmallChange = 256,
        SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
        Width = 150
    };
    private readonly TextBlock _memoryHint = new() { FontSize = 11.5, TextWrapping = TextWrapping.Wrap };
    private readonly ToggleSwitch _isolation = new()
    {
        Header = "版本隔离",
        OnContent = "开启（推荐）",
        OffContent = "关闭",
        IsOn = true
    };
    private readonly NumberBox _width = new()
    {
        Header = "窗口宽度",
        Minimum = 320,
        Maximum = 3840,
        SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
        Width = 150
    };
    private readonly NumberBox _height = new()
    {
        Header = "窗口高度",
        Minimum = 240,
        Maximum = 2160,
        SpinButtonPlacementMode = NumberBoxSpinButtonPlacementMode.Compact,
        Width = 150
    };
    private readonly ComboBox _mirror = new() { Header = "下载源", Width = 230 };
    private readonly ComboBox _gameLanguage = new() { Header = "游戏语言", Width = 140 };
    private readonly TextBox _jvmArgs = new()
    {
        Header = "附加 JVM 参数",
        PlaceholderText = "-Dfile.encoding=UTF-8",
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        MinHeight = 58
    };
    private readonly TextBox _gameArgs = new()
    {
        Header = "附加游戏参数",
        PlaceholderText = "--fullscreen",
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        MinHeight = 58
    };
    private readonly StackPanel _accounts = new() { Spacing = 6 };
    private readonly TextBlock _saved = new()
    {
        FontSize = 11.5,
        Foreground = new SolidColorBrush(Microsoft.UI.Colors.Transparent)
    };
    private readonly DispatcherQueueTimer _saveTimer;
    private readonly DispatcherQueueTimer _savedTimer;

    private bool _loading;

    public McSettingsPage(McLauncherView shell)
    {
        _shell = shell;
        _palette = shell.Palette;
        _javaInfo.Foreground = _palette.Secondary;
        _memoryHint.Foreground = _palette.Secondary;

        _saveTimer = shell.Dispatcher.CreateTimer();
        _saveTimer.Interval = TimeSpan.FromMilliseconds(600);
        _saveTimer.IsRepeating = false;
        _saveTimer.Tick += (_, _) => CommitArguments();

        _savedTimer = shell.Dispatcher.CreateTimer();
        _savedTimer.Interval = TimeSpan.FromSeconds(2);
        _savedTimer.IsRepeating = false;
        _savedTimer.Tick += (_, _) => _saved.Foreground = new SolidColorBrush(Microsoft.UI.Colors.Transparent);

        BuildLayout();
        WireEvents();
        RefreshAll();
        _ = LoadJavaAsync();
    }

    public FrameworkElement Root => _root;

    public void OnShown()
    {
        RefreshAll();
        if (_shell.Javas.Count == 0) _ = LoadJavaAsync();
    }

    private void BuildLayout()
    {
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _root.Padding = new Thickness(24, 18, 24, 0);

        var header = new Grid();
        var headerRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 9 };
        headerRow.Children.Add(McUi.GlyphIcon("\uE713", 26, _palette.Accent));
        var headerText = new StackPanel
        {
            Spacing = 2,
            Children =
            {
                McUi.Text("设置", 21, FontWeights.SemiBold, _palette.Text),
                McUi.Text("目录、Java、内存与启动参数；修改后自动保存。", 12, FontWeights.Normal, _palette.Secondary, true)
            }
        };
        headerRow.Children.Add(headerText);
        header.Children.Add(headerRow);
        _saved.Text = "已保存";
        _saved.HorizontalAlignment = HorizontalAlignment.Right;
        _saved.VerticalAlignment = VerticalAlignment.Center;
        header.Children.Add(_saved);
        _root.Children.Add(header);

        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            VerticalScrollMode = ScrollMode.Enabled,
            HorizontalScrollMode = ScrollMode.Disabled,
            Padding = new Thickness(0, 14, 0, 12),
            Content = _cards
        };
        Grid.SetRow(scroll, 1);
        _root.Children.Add(scroll);

        _cards.Children.Add(BuildDirectoryCard());
        _cards.Children.Add(BuildJavaCard());
        _cards.Children.Add(BuildMemoryCard());
        _cards.Children.Add(BuildLaunchCard());
        _cards.Children.Add(BuildAccountCard());
        _cards.Children.Add(BuildAboutCard());
    }

    private FrameworkElement BuildDirectoryCard()
    {
        var content = new StackPanel { Spacing = 8 };
        var row = new Grid { ColumnSpacing = 8 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _gameDirectory.Header = null;
        row.Children.Add(_gameDirectory);
        var browse = McUi.Button("浏览…", _palette);
        browse.VerticalAlignment = VerticalAlignment.Bottom;
        browse.Click += async (_, _) =>
        {
            var selected = await _shell.Host.Services.PickFolderAsync("选择 .minecraft 文件夹");
            if (!string.IsNullOrWhiteSpace(selected)) _gameDirectory.Text = selected;
        };
        Grid.SetColumn(browse, 1);
        row.Children.Add(browse);
        var apply = McUi.Button("应用", _palette, accent: true);
        apply.VerticalAlignment = VerticalAlignment.Bottom;
        apply.Click += async (_, _) =>
        {
            var directory = _gameDirectory.Text.Trim();
            if (directory.Length == 0) return;
            await _shell.ApplyGameDirectoryAsync(directory);
            RefreshAll();
            ShowSaved();
        };
        Grid.SetColumn(apply, 2);
        row.Children.Add(apply);
        var open = McUi.Button("打开", _palette);
        open.VerticalAlignment = VerticalAlignment.Bottom;
        open.Click += (_, _) => McLauncherView.OpenFolder(_shell.Settings.GameDirectory);
        Grid.SetColumn(open, 3);
        row.Children.Add(open);
        content.Children.Add(row);
        content.Children.Add(McUi.Text(
            "版本列表从该目录的 versions 文件夹读取；切换目录后会重新扫描。", 11.5,
            FontWeights.Normal, _palette.Tertiary, true));
        return McUi.Card(_palette, content, "游戏目录", icon: "icon_folder.png");
    }

    private FrameworkElement BuildJavaCard()
    {
        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(_javaBox);
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var refresh = McUi.Button("重新检测", _palette, icon: "icon_refresh.png");
        refresh.Click += async (_, _) =>
        {
            refresh.IsEnabled = false;
            await _shell.EnsureJavaAsync(true);
            RefreshJava();
            refresh.IsEnabled = true;
        };
        row.Children.Add(refresh);
        var browse = McUi.Button("选择 java.exe…", _palette);
        browse.Click += async (_, _) =>
        {
            var selected = await _shell.Host.Services.PickOpenFileAsync("选择 java.exe 或 javaw.exe");
            if (string.IsNullOrWhiteSpace(selected)) return;
            _shell.Settings.JavaPath = selected;
            _shell.Settings.Save();
            await _shell.EnsureJavaAsync(true);
            RefreshJava();
            ShowSaved();
        };
        row.Children.Add(browse);
        content.Children.Add(row);
        content.Children.Add(_javaInfo);
        content.Children.Add(McUi.Text(
            "启动器会按 Minecraft 版本要求选择 Java：1.20.5+ 需要 21，1.18+ 需要 17，更老的版本通常用 8。",
            11.5, FontWeights.Normal, _palette.Tertiary, true));
        return McUi.Card(_palette, content, "Java", icon: "icon_java.png");
    }

    private FrameworkElement BuildMemoryCard()
    {
        var content = new StackPanel { Spacing = 8 };
        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _memorySlider.VerticalAlignment = VerticalAlignment.Center;
        row.Children.Add(_memorySlider);
        Grid.SetColumn(_memoryBox, 1);
        row.Children.Add(_memoryBox);
        content.Children.Add(row);
        content.Children.Add(_memoryHint);
        return McUi.Card(_palette, content, "内存", icon: "icon_check.png");
    }

    private FrameworkElement BuildLaunchCard()
    {
        var content = new StackPanel { Spacing = 14 };
        content.Children.Add(_isolation);
        content.Children.Add(McUi.Separator(_palette));

        var sizeRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
        sizeRow.Children.Add(_width);
        sizeRow.Children.Add(_height);
        sizeRow.Children.Add(_mirror);
        PopulateMirror();
        _gameLanguage.Items.Add(new ComboBoxItem { Content = "跟随系统", Tag = "auto" });
        _gameLanguage.Items.Add(new ComboBoxItem { Content = "简体中文", Tag = "zh_cn" });
        _gameLanguage.Items.Add(new ComboBoxItem { Content = "繁體中文", Tag = "zh_tw" });
        _gameLanguage.Items.Add(new ComboBoxItem { Content = "English", Tag = "en_us" });
        _gameLanguage.SelectedIndex = 0;
        sizeRow.Children.Add(_gameLanguage);
        content.Children.Add(sizeRow);
        content.Children.Add(McUi.Text(
            "版本隔离：每个版本使用 versions/<版本名>/ 作为工作目录，存档与模组互不干扰（PCL 默认）。",
            11.5, FontWeights.Normal, _palette.Tertiary, true));
        content.Children.Add(McUi.Separator(_palette));
        content.Children.Add(_jvmArgs);
        content.Children.Add(_gameArgs);
        return McUi.Card(_palette, content, "启动选项", icon: "icon_settings.png");
    }

    private FrameworkElement BuildAccountCard()
    {
        var content = new StackPanel { Spacing = 8 };
        content.Children.Add(_accounts);
        var add = McUi.Button("账户管理（微软正版 / 离线）", _palette, accent: true, height: 40);
        add.HorizontalAlignment = HorizontalAlignment.Left;
        add.Click += async (_, _) =>
        {
            await _shell.ShowAccountDialogAsync();
            RefreshAccounts();
        };
        content.Children.Add(add);

        content.Children.Add(McUi.Separator(_palette));
        var clientId = new TextBox
        {
            Header = "自定义 Microsoft 客户端 ID（可选）",
            PlaceholderText = "留空使用内置公共客户端",
            Text = _shell.Settings.MicrosoftClientId
        };
        content.Children.Add(clientId);
        var clientError = McUi.Text("", 11.5, FontWeights.Normal, _palette.Danger, true);
        clientError.Visibility = Visibility.Collapsed;
        var clientRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        var saveClient = McUi.Button("保存客户端 ID", _palette, height: 38);
        saveClient.Click += (_, _) =>
        {
            var value = clientId.Text.Trim();
            if (value.Length > 0 && !Guid.TryParse(value, out _))
            {
                clientError.Text = "客户端 ID 应为 Azure 应用里的 GUID。";
                clientError.Visibility = Visibility.Visible;
                return;
            }

            clientError.Visibility = Visibility.Collapsed;
            _shell.Settings.MicrosoftClientId = value;
            _shell.Settings.Save();
            ShowSaved();
        };
        clientRow.Children.Add(saveClient);
        var review = new HyperlinkButton
        {
            Content = "Minecraft API 审核表单",
            NavigateUri = new Uri("https://forms.cloud.microsoft/Pages/ResponsePage.aspx?id=v4j5cvGGr0GRqy180BHbR-ajEQ1td1ROpz00KtS8Gd5UNVpPTkVLNFVROVQxNkdRMEtXVjNQQjdXVC4u")
        };
        clientRow.Children.Add(review);
        content.Children.Add(clientRow);
        content.Children.Add(clientError);
        content.Children.Add(McUi.Text(
            "只有通过 Minecraft Java Edition Game Service API 审核的 Azure 应用才能用于正版登录；普通应用会在 Minecraft Services 返回 403。留空时使用内置的已获准公共客户端。",
            11, FontWeights.Normal, _palette.Tertiary, true));
        return McUi.Card(_palette, content, "账户", icon: "icon_user.png");
    }

    private FrameworkElement BuildAboutCard()
    {
        var content = new StackPanel { Spacing = 6 };
        content.Children.Add(McUi.KeyValueRow("插件版本", "1.0.11", _palette));
        content.Children.Add(McUi.KeyValueRow("数据目录", _shell.DataDirectory, _palette, true));
        content.Children.Add(McUi.KeyValueRow("启动日志", _shell.Log.FilePath ?? "（尚未创建）", _palette, true));
        content.Children.Add(McUi.Text(
            "界面布局与启动流程参考 PCL CE（Plain Craft Launcher Community Edition，GPL-3.0）。本插件为按行为重写的实现，支持微软正版设备代码登录与离线账户。",
            11.5, FontWeights.Normal, _palette.Tertiary, true));
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 4, 0, 0) };
        var pcl = new HyperlinkButton
        {
            Content = "PCL CE 项目主页",
            NavigateUri = new Uri("https://github.com/PCL-Community/PCL-CE")
        };
        row.Children.Add(pcl);
        var openData = McUi.Button("打开数据目录", _palette, height: 38);
        openData.Click += (_, _) => McLauncherView.OpenFolder(_shell.DataDirectory);
        row.Children.Add(openData);
        var openLogs = McUi.Button("打开日志目录", _palette, height: 38);
        openLogs.Click += (_, _) => McLauncherView.OpenFolder(Path.Combine(_shell.DataDirectory, "logs"));
        row.Children.Add(openLogs);
        content.Children.Add(row);
        return McUi.Card(_palette, content, "关于", icon: "icon_script.png");
    }

    private void PopulateMirror()
    {
        _mirror.Items.Clear();
        foreach (var (value, label) in new[]
                 {
                     (McMirror.Auto, "自动（镜像优先）"),
                     (McMirror.Official, "官方源"),
                     (McMirror.Bmclapi, "BMCLAPI 镜像")
                 })
            _mirror.Items.Add(new ComboBoxItem { Content = label, Tag = value });
    }

    private void WireEvents()
    {
        _memorySlider.ValueChanged += (_, args) =>
        {
            if (_loading) return;
            _memoryBox.Value = Math.Round(args.NewValue / 256) * 256;
            ApplyMemory((int)_memoryBox.Value);
        };
        _memoryBox.ValueChanged += (_, args) =>
        {
            if (_loading || double.IsNaN(args.NewValue)) return;
            _memorySlider.Value = args.NewValue;
            ApplyMemory((int)args.NewValue);
        };

        _isolation.Toggled += (_, _) =>
        {
            if (_loading) return;
            _shell.Settings.VersionIsolation = _isolation.IsOn;
            SaveNow();
        };
        _width.ValueChanged += (_, args) =>
        {
            if (_loading || double.IsNaN(args.NewValue)) return;
            _shell.Settings.WindowWidth = (int)args.NewValue;
            SaveNow();
        };
        _height.ValueChanged += (_, args) =>
        {
            if (_loading || double.IsNaN(args.NewValue)) return;
            _shell.Settings.WindowHeight = (int)args.NewValue;
            SaveNow();
        };
        _mirror.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            if (_mirror.SelectedItem is ComboBoxItem { Tag: string mode })
            {
                _shell.Settings.Mirror = mode;
                SaveNow();
                _shell.Notify("下载源已切换为：" + McMirror.DisplayName(mode));
            }
        };
        _gameLanguage.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            if (_gameLanguage.SelectedItem is ComboBoxItem { Tag: string language })
            {
                _shell.Settings.GameLanguage = language;
                SaveNow();
            }
        };
        _javaBox.SelectionChanged += (_, _) =>
        {
            if (_loading) return;
            if (_javaBox.SelectedItem is ComboBoxItem { Tag: JavaRuntime runtime })
            {
                _shell.Settings.JavaPath = runtime.Home;
                SaveNow();
                UpdateJavaInfo();
            }
            else if (_javaBox.SelectedIndex == 0)
            {
                _shell.Settings.JavaPath = null;
                SaveNow();
                UpdateJavaInfo();
            }
        };
        _jvmArgs.TextChanged += (_, _) => QueueArgumentsSave();
        _gameArgs.TextChanged += (_, _) => QueueArgumentsSave();
    }

    private void QueueArgumentsSave()
    {
        if (_loading) return;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void CommitArguments()
    {
        _shell.Settings.JvmArgs = _jvmArgs.Text.Trim();
        _shell.Settings.GameArgs = _gameArgs.Text.Trim();
        SaveNow();
    }

    private void ApplyMemory(int value)
    {
        _shell.Settings.MemoryMb = Math.Clamp(value, 512, 32768);
        SaveNow();
        UpdateMemoryHint();
    }

    private void SaveNow()
    {
        _shell.SaveSettings();
        ShowSaved();
    }

    private void ShowSaved()
    {
        _saved.Foreground = _palette.Success;
        _savedTimer.Stop();
        _savedTimer.Start();
    }

    public void RefreshAll()
    {
        var settings = _shell.Settings;
        _loading = true;
        _gameDirectory.Text = settings.GameDirectory;
        _isolation.IsOn = settings.VersionIsolation;
        _memorySlider.Value = settings.MemoryMb;
        _memoryBox.Value = settings.MemoryMb;
        _width.Value = settings.WindowWidth;
        _height.Value = settings.WindowHeight;
        _jvmArgs.Text = settings.JvmArgs;
        _gameArgs.Text = settings.GameArgs;
        for (var i = 0; i < _mirror.Items.Count; i++)
        {
            if (_mirror.Items[i] is ComboBoxItem { Tag: string mode } && mode == settings.Mirror)
            {
                _mirror.SelectedIndex = i;
                break;
            }
        }

        for (var i = 0; i < _gameLanguage.Items.Count; i++)
        {
            if (_gameLanguage.Items[i] is ComboBoxItem { Tag: string language } &&
                language == settings.GameLanguage)
            {
                _gameLanguage.SelectedIndex = i;
                break;
            }
        }

        _loading = false;
        RefreshJava();
        RefreshAccounts();
        UpdateMemoryHint();
    }

    private async Task LoadJavaAsync()
    {
        await _shell.EnsureJavaAsync(true);
        RefreshJava();
    }

    private void RefreshJava()
    {
        _loading = true;
        _javaBox.Items.Clear();
        _javaBox.Items.Add(new ComboBoxItem { Content = "自动选择（推荐）", Tag = null });
        var preferredHome = McJava.ResolveHome(_shell.Settings.JavaPath);
        var selectedIndex = 0;
        for (var i = 0; i < _shell.Javas.Count; i++)
        {
            var runtime = _shell.Javas[i];
            _javaBox.Items.Add(new ComboBoxItem { Content = runtime.Detail, Tag = runtime });
            if (preferredHome is not null &&
                string.Equals(runtime.Home, preferredHome, StringComparison.OrdinalIgnoreCase))
                selectedIndex = i + 1;
        }

        if (preferredHome is not null && selectedIndex == 0)
        {
            _javaBox.Items.Add(new ComboBoxItem
            {
                Content = "手动指定：" + _shell.Settings.JavaPath,
                Tag = null
            });
            selectedIndex = _javaBox.Items.Count - 1;
        }

        _javaBox.SelectedIndex = selectedIndex;
        _loading = false;
        UpdateJavaInfo();
    }

    private void UpdateJavaInfo()
    {
        var requiredHint = _shell.SelectedVersion is { } version
            ? $"当前选中版本需要 Java {McJava.RequiredMajor(version.Json, version.ReleaseTime)}。"
            : "";
        _javaInfo.Text = _shell.Javas.Count == 0
            ? "未检测到 Java。请安装 Java 21 或 JDK，或手动选择 java.exe。" + requiredHint
            : $"已检测到 {_shell.Javas.Count} 个 Java 运行时。" + requiredHint;
    }

    private void UpdateMemoryHint()
    {
        long totalMb = 0;
        try { totalMb = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes / (1024 * 1024); }
        catch { }

        _memoryHint.Text = (totalMb > 0
                               ? $"物理内存约 {totalMb / 1024.0:F1} GB。"
                               : "") +
                           $"当前分配 {_shell.Settings.MemoryMb} MB。原版建议 2048–4096 MB，大型整合包可适当提高，不建议超过物理内存的一半。";
    }

    public void RefreshAccounts()
    {
        _accounts.Children.Clear();
        if (_shell.Accounts.Accounts.Count == 0)
        {
            _accounts.Children.Add(McUi.Text("还没有账户。点击下方「账户管理」登录微软正版或添加离线账户。", 12,
                FontWeights.Normal, _palette.Tertiary, true));
            return;
        }

        foreach (var account in _shell.Accounts.Accounts)
        {
            var selected = string.Equals(_shell.Accounts.Selected?.Name, account.Name, StringComparison.OrdinalIgnoreCase);
            var row = new Grid { ColumnSpacing = 10, Padding = new Thickness(0, 2, 0, 2) };
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var avatar = new Border
            {
                Width = 30,
                Height = 30,
                CornerRadius = new CornerRadius(15),
                Background = new ImageBrush
                {
                    ImageSource = McAvatar.Create(account.Name, 32),
                    Stretch = Stretch.UniformToFill
                },
                VerticalAlignment = VerticalAlignment.Center
            };
            row.Children.Add(avatar);

            var text = new StackPanel { Spacing = 0, VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(McUi.Text(account.Name + (selected ? "（当前）" : "") + "  ·  " + account.TypeLabel, 12.5,
                FontWeights.SemiBold, _palette.Text));
            text.Children.Add(McUi.Text(account.Uuid, 10.5, FontWeights.Normal, _palette.Tertiary));
            Grid.SetColumn(text, 1);
            row.Children.Add(text);

            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
            if (!selected)
            {
                var use = McUi.Button("使用", _palette, height: 36);
                use.Click += (_, _) =>
                {
                    _shell.Accounts.Select(account);
                    _shell.SaveSettings();
                    RefreshAccounts();
                    _shell.LaunchPage.Refresh();
                };
                actions.Children.Add(use);
            }

            var delete = McUi.PixelIconButton("icon_trash.png", "删除账户", _palette, 26);
            delete.Click += (_, _) =>
            {
                _shell.Accounts.Remove(account);
                RefreshAccounts();
                _shell.LaunchPage.Refresh();
                ShowSaved();
            };
            actions.Children.Add(delete);
            Grid.SetColumn(actions, 2);
            row.Children.Add(actions);
            _accounts.Children.Add(row);
        }
    }
}

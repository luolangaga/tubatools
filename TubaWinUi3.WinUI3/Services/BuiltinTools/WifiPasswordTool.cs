using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Pages;
using Windows.UI;

namespace TubaWinUi3.Services;

public sealed class WifiPasswordTool : IBuiltinTool
{
    public string Id => "wifi-password";
    public string Name => LocalizationService.L("Builtin_wifi-password_Name", "WiFi 密码");
    public string Description => LocalizationService.L("Builtin_wifi-password_Desc", "查看本机已连接过的 WiFi 网络名称和密码。");
    public string Glyph => "\uE701";
    public string Category => "网络工具";
    public BuiltinToolKind Kind => BuiltinToolKind.BackgroundTask;

    private static readonly Color AccentBlue = Color.FromArgb(255, 96, 165, 250);
    private static readonly Color AccentGreen = Color.FromArgb(255, 74, 222, 128);

    public Task ExecuteAsync(BuiltinToolContext context)
    {
        var content = BuildDialogContent();

        App.MainWindow?.NavigateToToolPage(typeof(ToolContentPage), new ToolContentPageParam
        {
            Title = "WiFi 密码查看",
            Description = "查看本机已连接过的 WiFi 网络名称和密码，密码默认隐藏，点击眼睛图标显示",
            Glyph = Glyph,
            Content = content
        });

        _ = LoadNetworksAsync(content);

        return Task.CompletedTask;
    }

    private async Task LoadNetworksAsync(ScrollViewer root)
    {
        var state = GetState(root);
        if (state is null) return;

        state.LoadingPanel.Visibility = Visibility.Visible;
        state.LoadingRing.IsActive = true;
        state.NetworkList.Children.Clear();

        var current = await WifiPasswordService.GetCurrentNetworkAsync();
        var networks = await WifiPasswordService.GetNetworksAsync();

        foreach (var network in networks)
        {
            if (current is not null && network.Ssid == current.Ssid)
                network.IsConnected = true;
        }

        state.LoadingPanel.Visibility = Visibility.Collapsed;
        state.LoadingRing.IsActive = false;

        if (networks.Count == 0)
        {
            state.EmptyPanel.Visibility = Visibility.Visible;
            return;
        }

        state.NetworkCountText.Text = networks.Count.ToString();
        state.ConnectedCountText.Text = networks.Count(n => n.IsConnected).ToString();

        foreach (var network in networks)
        {
            state.NetworkList.Children.Add(CreateNetworkRow(network, state));
        }
    }

    private Border CreateNetworkRow(WifiNetwork network, WifiPasswordState state)
    {
        var accentColor = network.IsConnected ? AccentGreen : AccentBlue;
        var iconBorder = new Border
        {
            Width = 36,
            Height = 36,
            Background = new SolidColorBrush(Color.FromArgb(26, accentColor.R, accentColor.G, accentColor.B)),
            CornerRadius = new CornerRadius(6),
            Child = new FontIcon { FontSize = 16, Foreground = new SolidColorBrush(accentColor), Glyph = "\uE701" }
        };

        var ssidText = new TextBlock
        {
            Text = network.Ssid,
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            Foreground = new SolidColorBrush(ThemeColors.PrimaryText)
        };

        var connectedBadge = new Border
        {
            Padding = new Thickness(6, 2, 6, 2),
            Background = new SolidColorBrush(Color.FromArgb(26, AccentGreen.R, AccentGreen.G, AccentGreen.B)),
            CornerRadius = new CornerRadius(4),
            Child = new TextBlock { Text = "已连接", FontSize = 10, Foreground = new SolidColorBrush(AccentGreen), FontWeight = Microsoft.UI.Text.FontWeights.Bold },
            Visibility = network.IsConnected ? Visibility.Visible : Visibility.Collapsed
        };

        var namePanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        namePanel.Children.Add(ssidText);
        namePanel.Children.Add(connectedBadge);

        var authText = new TextBlock
        {
            Text = string.IsNullOrEmpty(network.Authentication) ? "" : network.Authentication,
            FontSize = 11,
            Foreground = new SolidColorBrush(ThemeColors.DimText)
        };

        var infoPanel = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        infoPanel.Children.Add(namePanel);
        infoPanel.Children.Add(authText);

        var passwordText = new TextBlock
        {
            Text = network.HasPassword ? network.Password : "开放网络",
            FontSize = 13,
            FontFamily = new FontFamily("Consolas"),
            Foreground = new SolidColorBrush(network.HasPassword ? ThemeColors.PrimaryText : ThemeColors.DimText),
            VerticalAlignment = VerticalAlignment.Center,
            IsTextSelectionEnabled = true
        };

        var passwordReveal = new Button
        {
            Content = new FontIcon { Glyph = "\uE7B3", FontSize = 12 },
            Padding = new Thickness(6, 2, 6, 2),
            Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
            Foreground = new SolidColorBrush(ThemeColors.DimText),
            VerticalAlignment = VerticalAlignment.Center,
            Tag = false
        };

        if (network.HasPassword)
        {
            passwordText.Text = new string('*', network.Password.Length);
            passwordReveal.Click += (_, _) =>
            {
                var revealed = !(bool)passwordReveal.Tag;
                passwordReveal.Tag = revealed;
                passwordText.Text = revealed ? network.Password : new string('*', network.Password.Length);
                ((FontIcon)passwordReveal.Content).Glyph = revealed ? "\uE7B4" : "\uE7B3";
            };
        }
        else
        {
            passwordReveal.Visibility = Visibility.Collapsed;
        }

        var copyBtn = new Button
        {
            Content = new FontIcon { Glyph = "\uE8C8", FontSize = 12 },
            Padding = new Thickness(6, 2, 6, 2),
            Background = new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)),
            Foreground = new SolidColorBrush(ThemeColors.DimText),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = network.HasPassword ? Visibility.Visible : Visibility.Collapsed
        };
        copyBtn.Click += (_, _) =>
        {
            // 统一走 ClipboardService：占用重试，失败只留痕（卡片内没有提示位）
            ClipboardService.TrySetText(network.Password);
        };

        var passwordPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        passwordPanel.Children.Add(passwordText);
        passwordPanel.Children.Add(passwordReveal);
        passwordPanel.Children.Add(copyBtn);

        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(iconBorder);
        grid.Children.Add(infoPanel); Grid.SetColumn(infoPanel, 1);
        grid.Children.Add(passwordPanel); Grid.SetColumn(passwordPanel, 2);

        return new Border
        {
            Padding = new Thickness(12, 8, 12, 8),
            Background = new SolidColorBrush(ThemeColors.CardBg),
            BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = grid
        };
    }

    private ScrollViewer BuildDialogContent()
    {
        var networkCountText = new TextBlock { FontSize = 22, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = new SolidColorBrush(AccentBlue) };
        var connectedCountText = new TextBlock { FontSize = 22, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = new SolidColorBrush(AccentGreen) };

        var networkCard = MakeStatCard("已保存网络", networkCountText, "\uE701", AccentBlue);
        var connectedCard = MakeStatCard("当前连接", connectedCountText, "\uE73E", AccentGreen);

        var statsGrid = new Grid { ColumnSpacing = 10 };
        statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        statsGrid.Children.Add(networkCard); Grid.SetColumn(networkCard, 0);
        statsGrid.Children.Add(connectedCard); Grid.SetColumn(connectedCard, 1);

        var refreshBtn = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new FontIcon { Glyph = "\uE72C", FontSize = 12 },
                    new TextBlock { Text = "刷新" }
                }
            }
        };

        var showAllBtn = new ToggleSwitch
        {
            OnContent = "显示密码",
            OffContent = "隐藏密码",
            IsOn = false
        };

        var actionBar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actionBar.Children.Add(refreshBtn);
        actionBar.Children.Add(showAllBtn);

        var networkList = new StackPanel { Spacing = 6 };
        var listScroll = new ScrollViewer
        {
            Content = networkList,
            MaxHeight = 420,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        var loadingRing = new ProgressRing { Width = 36, Height = 36, IsActive = true };
        var loadingText = new TextBlock { Text = "正在获取 WiFi 信息...", FontSize = 13, Foreground = new SolidColorBrush(ThemeColors.DimText) };
        var loadingPanel = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = 8,
            Padding = new Thickness(0, 30, 0, 30),
            Children = { loadingRing, loadingText }
        };

        var emptyPanel = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = 8,
            Padding = new Thickness(0, 30, 0, 30),
            Visibility = Visibility.Collapsed,
            Children =
            {
                new FontIcon { Glyph = "\uE701", FontSize = 36, Foreground = new SolidColorBrush(ThemeColors.DimText) },
                new TextBlock { Text = "未找到已保存的 WiFi 网络", FontSize = 14, Foreground = new SolidColorBrush(ThemeColors.DimText) }
            }
        };

        var rootStack = new StackPanel { Spacing = 14, MaxWidth = 800 };
        rootStack.Children.Add(statsGrid);
        rootStack.Children.Add(actionBar);
        rootStack.Children.Add(loadingPanel);
        rootStack.Children.Add(emptyPanel);
        rootStack.Children.Add(listScroll);

        var scrollViewer = new ScrollViewer
        {
            Content = rootStack,
            MaxWidth = 860,
            Padding = new Thickness(24, 0, 24, 24),
            HorizontalAlignment = HorizontalAlignment.Center
        };
        scrollViewer.Tag = new WifiPasswordState
        {
            NetworkCountText = networkCountText,
            ConnectedCountText = connectedCountText,
            NetworkList = networkList,
            LoadingPanel = loadingPanel,
            LoadingRing = loadingRing,
            EmptyPanel = emptyPanel,
            RefreshBtn = refreshBtn,
            ShowAllBtn = showAllBtn
        };

        refreshBtn.Click += async (_, _) => await LoadNetworksAsync(scrollViewer);

        return scrollViewer;
    }

    private static Border MakeStatCard(string label, TextBlock value, string glyph, Color accent)
    {
        var iconBorder = new Border
        {
            Width = 36,
            Height = 36,
            Background = new SolidColorBrush(Color.FromArgb(26, accent.R, accent.G, accent.B)),
            CornerRadius = new CornerRadius(6),
            Child = new FontIcon { FontSize = 16, Foreground = new SolidColorBrush(accent), Glyph = glyph }
        };
        var labelBlock = new TextBlock { Text = label, FontSize = 11, Foreground = new SolidColorBrush(ThemeColors.DimText) };
        var stack = new StackPanel { Spacing = 2 };
        stack.Children.Add(labelBlock);
        stack.Children.Add(value);

        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(36) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(iconBorder);
        grid.Children.Add(stack); Grid.SetColumn(stack, 1);

        return new Border
        {
            Padding = new Thickness(12),
            Background = new SolidColorBrush(ThemeColors.CardBg),
            BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = grid
        };
    }

    private static WifiPasswordState? GetState(ScrollViewer root) => root?.Tag as WifiPasswordState;

    private sealed class WifiPasswordState
    {
        public TextBlock NetworkCountText = null!;
        public TextBlock ConnectedCountText = null!;
        public StackPanel NetworkList = null!;
        public StackPanel LoadingPanel = null!;
        public ProgressRing LoadingRing = null!;
        public StackPanel EmptyPanel = null!;
        public Button RefreshBtn = null!;
        public ToggleSwitch ShowAllBtn = null!;
    }
}
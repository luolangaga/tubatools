using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using TubaWinUi3.Services;

namespace TubaWinUi3.Services;

public sealed class WindowsActivationTool : IBuiltinTool
{
    public string Id => "windows-activation";
    public string Name => "Windows 激活";
    public string Description => "扫描 KMS 服务器列表，选择最优服务器激活 Windows，完成后自动重启。";
    public string Glyph => "\uE895";
    public string Category => "系统工具";
    public BuiltinToolKind Kind => BuiltinToolKind.Dialog;

    private DialogState? _state;

    public async Task ExecuteAsync(BuiltinToolContext context)
    {
        var dialog = new ContentDialog
        {
            Title = "Windows 激活",
            CloseButtonText = "关闭",
            XamlRoot = context.XamlRoot
        };
        dialog.Resources["ContentDialogMaxWidth"] = 860;

        var content = BuildDialogContent();
        dialog.Content = content;

        dialog.Closing += (_, args) =>
        {
            if (_state?.IsActivating == true) args.Cancel = true;
        };

        await dialog.ShowAsync();
    }

    private ScrollViewer BuildDialogContent()
    {
        var accentBlue = ThemeColors.AccentBlue;
        var accentGreen = ThemeColors.AccentGreen;
        var accentOrange = ThemeColors.AccentOrange;

        var statusIcon = new FontIcon { Glyph = "\uE73E", FontSize = 28, Foreground = new SolidColorBrush(accentOrange) };
        var statusText = new TextBlock { FontSize = 22, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Text = "准备就绪" };
        var statusDesc = new TextBlock { FontSize = 12, Foreground = new SolidColorBrush(ThemeColors.DimText), Text = "点击「扫描服务器」获取 KMS 服务器列表" };

        var statusGrid = new Grid { ColumnSpacing = 12 };
        statusGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
        statusGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var statusIconBorder = new Border
        {
            Width = 44, Height = 44,
            Background = new SolidColorBrush(Color.FromArgb(26, accentOrange.R, accentOrange.G, accentOrange.B)),
            CornerRadius = new CornerRadius(8),
            Child = statusIcon
        };
        var statusStack = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        statusStack.Children.Add(statusText);
        statusStack.Children.Add(statusDesc);
        statusGrid.Children.Add(statusIconBorder);
        statusGrid.Children.Add(statusStack);
        Grid.SetColumn(statusStack, 1);

        var statusCard = new Border
        {
            Padding = new Thickness(16),
            Background = new SolidColorBrush(ThemeColors.CardBg),
            BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = statusGrid
        };

        var scanBtn = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new FontIcon { Glyph = "\uE721", FontSize = 12 },
                    new TextBlock { Text = "扫描服务器" }
                }
            },
            Style = Application.Current.Resources["AccentButtonStyle"] as Style
        };

        var activateBtn = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new FontIcon { Glyph = "\uE895", FontSize = 12 },
                    new TextBlock { Text = "激活 Windows" }
                }
            },
            IsEnabled = false
        };

        var rebootBtn = new Button
        {
            Content = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 6,
                Children =
                {
                    new FontIcon { Glyph = "\uE777", FontSize = 12 },
                    new TextBlock { Text = "立即重启" }
                }
            },
            Visibility = Visibility.Collapsed
        };

        var cancelRebootBtn = new Button
        {
            Content = "取消重启",
            Visibility = Visibility.Collapsed
        };

        var actionBar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actionBar.Children.Add(scanBtn);
        actionBar.Children.Add(activateBtn);
        actionBar.Children.Add(rebootBtn);
        actionBar.Children.Add(cancelRebootBtn);

        var loadingRing = new ProgressRing { Width = 32, Height = 32, IsActive = false };
        var loadingText = new TextBlock { FontSize = 13, Foreground = new SolidColorBrush(ThemeColors.DimText), Text = "" };
        var loadingPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Visibility = Visibility.Collapsed
        };
        loadingPanel.Children.Add(loadingRing);
        loadingPanel.Children.Add(loadingText);

        var serverList = new StackPanel { Spacing = 4 };

        var listScroll = new ScrollViewer
        {
            Content = serverList,
            MaxHeight = 320,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            Visibility = Visibility.Collapsed
        };

        var warningBar = new InfoBar
        {
            Title = "注意事项",
            Message = "此工具使用 KMS 激活方式，需要管理员权限。激活后 180 天内需重新连接 KMS 服务器续期。仅适用于 KMS 客户端版本的 Windows。",
            Severity = InfoBarSeverity.Warning,
            IsOpen = true,
            IsClosable = true
        };

        var logText = new TextBlock
        {
            FontSize = 12,
            Foreground = new SolidColorBrush(ThemeColors.DimText),
            TextWrapping = TextWrapping.Wrap,
            Visibility = Visibility.Collapsed
        };

        var rootStack = new StackPanel { Spacing = 14, MaxWidth = 800 };
        rootStack.Children.Add(new TextBlock
        {
            Text = "从 monitor.yerong.org 获取 KMS 服务器列表，选择最优服务器激活 Windows",
            FontSize = 12,
            Foreground = new SolidColorBrush(ThemeColors.DimText)
        });
        rootStack.Children.Add(warningBar);
        rootStack.Children.Add(statusCard);
        rootStack.Children.Add(actionBar);
        rootStack.Children.Add(loadingPanel);
        rootStack.Children.Add(listScroll);
        rootStack.Children.Add(logText);

        _state = new DialogState
        {
            StatusIcon = statusIcon,
            StatusIconBorder = statusIconBorder,
            StatusText = statusText,
            StatusDesc = statusDesc,
            ScanBtn = scanBtn,
            ActivateBtn = activateBtn,
            RebootBtn = rebootBtn,
            CancelRebootBtn = cancelRebootBtn,
            LoadingRing = loadingRing,
            LoadingText = loadingText,
            LoadingPanel = loadingPanel,
            ServerList = serverList,
            ListScroll = listScroll,
            LogText = logText,
            AccentBlue = accentBlue,
            AccentGreen = accentGreen,
            AccentOrange = accentOrange
        };

        scanBtn.Click += async (_, _) => await ScanServersAsync();
        activateBtn.Click += async (_, _) => await ActivateAsync();
        rebootBtn.Click += (_, _) => KmsActivationService.ScheduleReboot();
        cancelRebootBtn.Click += (_, _) =>
        {
            KmsActivationService.CancelReboot();
            _state.RebootBtn.Visibility = Visibility.Collapsed;
            _state.CancelRebootBtn.Visibility = Visibility.Collapsed;
            AppendLog("已取消重启。");
        };

        return new ScrollViewer { Content = rootStack, MaxWidth = 860 };
    }

    private async Task ScanServersAsync()
    {
        var state = _state;
        if (state is null) return;

        state.ScanBtn.IsEnabled = false;
        state.ActivateBtn.IsEnabled = false;
        state.LoadingPanel.Visibility = Visibility.Visible;
        state.LoadingRing.IsActive = true;
        state.LoadingText.Text = "正在扫描 KMS 服务器...";
        state.ListScroll.Visibility = Visibility.Collapsed;
        state.ServerList.Children.Clear();
        state.SelectedServer = null;

        try
        {
            var servers = await KmsActivationService.FetchKmsServersAsync();

            state.LoadingPanel.Visibility = Visibility.Collapsed;
            state.LoadingRing.IsActive = false;

            if (servers.Count == 0)
            {
                state.StatusText.Text = "未找到服务器";
                state.StatusDesc.Text = "无法获取 KMS 服务器列表，请检查网络连接";
                state.StatusIcon.Glyph = "\uE783";
                state.StatusIcon.Foreground = new SolidColorBrush(ThemeColors.AccentRed);
                state.StatusIconBorder.Background = new SolidColorBrush(Color.FromArgb(26, ThemeColors.AccentRed.R, ThemeColors.AccentRed.G, ThemeColors.AccentRed.B));
                return;
            }

            var top5 = servers.Take(5).ToList();

            state.StatusText.Text = $"找到 {servers.Count} 个可用服务器";
            state.StatusDesc.Text = $"已筛选出前 {top5.Count} 个最优服务器供选择";
            state.StatusIcon.Glyph = "\uE73E";
            state.StatusIcon.Foreground = new SolidColorBrush(ThemeColors.AccentGreen);
            state.StatusIconBorder.Background = new SolidColorBrush(Color.FromArgb(26, ThemeColors.AccentGreen.R, ThemeColors.AccentGreen.G, ThemeColors.AccentGreen.B));

            var radioGroup = new StackPanel { Spacing = 4 };
            var firstRadio = new RadioButton { IsChecked = true };
            state.SelectedServer = top5[0];

            for (int i = 0; i < top5.Count; i++)
            {
                var server = top5[i];
                var radio = i == 0 ? firstRadio : new RadioButton();
                radio.GroupName = "KmsServer";
                radio.Tag = server;

                var hostText = new TextBlock
                {
                    Text = server.Host,
                    FontSize = 14,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                };
                var countryText = new TextBlock
                {
                    Text = GetCountryName(server.Country),
                    FontSize = 11,
                    Foreground = new SolidColorBrush(ThemeColors.DimText)
                };
                var infoStack = new StackPanel { Spacing = 2 };
                infoStack.Children.Add(hostText);
                infoStack.Children.Add(countryText);

                var rateText = new TextBlock
                {
                    Text = $"{server.SuccessRate:0}%",
                    FontSize = 16,
                    FontWeight = Microsoft.UI.Text.FontWeights.Bold,
                    Foreground = new SolidColorBrush(server.SuccessRate >= 95 ? ThemeColors.AccentGreen : ThemeColors.AccentOrange)
                };
                var rateLabel = new TextBlock
                {
                    Text = "成功率",
                    FontSize = 10,
                    Foreground = new SolidColorBrush(ThemeColors.DimText)
                };
                var rateStack = new StackPanel { Spacing = 0, HorizontalAlignment = HorizontalAlignment.Center };
                rateStack.Children.Add(rateText);
                rateStack.Children.Add(rateLabel);

                var latencyText = new TextBlock
                {
                    Text = server.AverageTime > 0 ? $"{server.AverageTime:0}ms" : "-",
                    FontSize = 14,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                };
                var latencyLabel = new TextBlock
                {
                    Text = "延迟",
                    FontSize = 10,
                    Foreground = new SolidColorBrush(ThemeColors.DimText)
                };
                var latencyStack = new StackPanel { Spacing = 0, HorizontalAlignment = HorizontalAlignment.Center };
                latencyStack.Children.Add(latencyText);
                latencyStack.Children.Add(latencyLabel);

                var recentText = new TextBlock
                {
                    Text = $"{server.RecentSuccessRate:0}%",
                    FontSize = 14,
                    FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                    Foreground = new SolidColorBrush(server.RecentSuccessRate >= 80 ? ThemeColors.AccentGreen : ThemeColors.AccentOrange)
                };
                var recentLabel = new TextBlock
                {
                    Text = "近期",
                    FontSize = 10,
                    Foreground = new SolidColorBrush(ThemeColors.DimText)
                };
                var recentStack = new StackPanel { Spacing = 0, HorizontalAlignment = HorizontalAlignment.Center };
                recentStack.Children.Add(recentText);
                recentStack.Children.Add(recentLabel);

                var statsGrid = new Grid { ColumnSpacing = 16 };
                statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                statsGrid.Children.Add(infoStack);
                statsGrid.Children.Add(rateStack); Grid.SetColumn(rateStack, 1);
                statsGrid.Children.Add(latencyStack); Grid.SetColumn(latencyStack, 2);
                statsGrid.Children.Add(recentStack); Grid.SetColumn(recentStack, 3);

                radio.Content = statsGrid;

                radio.Checked += (_, _) =>
                {
                    state.SelectedServer = server;
                    state.ActivateBtn.IsEnabled = true;
                };

                radioGroup.Children.Add(radio);
            }

            state.ServerList.Children.Clear();
            state.ServerList.Children.Add(new TextBlock
            {
                Text = "推荐服务器（按综合评分排序）",
                FontSize = 13,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4)
            });
            state.ServerList.Children.Add(radioGroup);

            state.ListScroll.Visibility = Visibility.Visible;
            state.ActivateBtn.IsEnabled = true;
        }
        catch (Exception ex)
        {
            state.LoadingPanel.Visibility = Visibility.Collapsed;
            state.LoadingRing.IsActive = false;
            state.StatusText.Text = "扫描失败";
            state.StatusDesc.Text = ex.Message;
            state.StatusIcon.Glyph = "\uE783";
            state.StatusIcon.Foreground = new SolidColorBrush(ThemeColors.AccentRed);
            state.StatusIconBorder.Background = new SolidColorBrush(Color.FromArgb(26, ThemeColors.AccentRed.R, ThemeColors.AccentRed.G, ThemeColors.AccentRed.B));
        }
        finally
        {
            state.ScanBtn.IsEnabled = true;
        }
    }

    private async Task ActivateAsync()
    {
        var state = _state;
        if (state is null || state.SelectedServer is null) return;

        state.IsActivating = true;
        state.ActivateBtn.IsEnabled = false;
        state.ScanBtn.IsEnabled = false;
        state.LogText.Visibility = Visibility.Visible;
        state.LogText.Text = "";

        state.StatusText.Text = "正在激活...";
        state.StatusDesc.Text = "请在 UAC 弹窗中点击「是」授予管理员权限";
        state.StatusIcon.Glyph = "\uE928";
        state.StatusIcon.Foreground = new SolidColorBrush(ThemeColors.AccentBlue);
        state.StatusIconBorder.Background = new SolidColorBrush(Color.FromArgb(26, ThemeColors.AccentBlue.R, ThemeColors.AccentBlue.G, ThemeColors.AccentBlue.B));

        var server = state.SelectedServer;
        AppendLog($"KMS 服务器: {server.Host}:{server.Port}");
        AppendLog($"成功率: {server.SuccessRate:0}% | 延迟: {server.AverageTime:0}ms");
        AppendLog("正在执行激活命令...");

        try
        {
            var exitCode = await KmsActivationService.ActivateWindowsAsync(
                server.Host, server.Port,
                msg => AppendLog(msg));

            if (exitCode == 0)
            {
                AppendLog("激活命令已执行完成。");
                state.StatusText.Text = "激活完成";
                state.StatusDesc.Text = "Windows 激活命令已执行，10 秒后重启以完成激活";
                state.StatusIcon.Glyph = "\uE73E";
                state.StatusIcon.Foreground = new SolidColorBrush(ThemeColors.AccentGreen);
                state.StatusIconBorder.Background = new SolidColorBrush(Color.FromArgb(26, ThemeColors.AccentGreen.R, ThemeColors.AccentGreen.G, ThemeColors.AccentGreen.B));

                state.RebootBtn.Visibility = Visibility.Visible;
                state.CancelRebootBtn.Visibility = Visibility.Visible;

                KmsActivationService.ScheduleReboot();
                AppendLog("已计划 10 秒后重启。点击「取消重启」可取消。");
            }
            else
            {
                AppendLog($"激活命令返回退出码: {exitCode}");
                state.StatusText.Text = "激活可能失败";
                state.StatusDesc.Text = "请检查弹出的 PowerShell 窗口中的 slmgr 输出信息";
                state.StatusIcon.Glyph = "\uE7BA";
                state.StatusIcon.Foreground = new SolidColorBrush(ThemeColors.AccentOrange);
                state.StatusIconBorder.Background = new SolidColorBrush(Color.FromArgb(26, ThemeColors.AccentOrange.R, ThemeColors.AccentOrange.G, ThemeColors.AccentOrange.B));
            }
        }
        catch (Exception ex)
        {
            AppendLog($"错误: {ex.Message}");
            state.StatusText.Text = "激活失败";
            state.StatusDesc.Text = ex.Message;
            state.StatusIcon.Glyph = "\uE783";
            state.StatusIcon.Foreground = new SolidColorBrush(ThemeColors.AccentRed);
            state.StatusIconBorder.Background = new SolidColorBrush(Color.FromArgb(26, ThemeColors.AccentRed.R, ThemeColors.AccentRed.G, ThemeColors.AccentRed.B));
        }
        finally
        {
            state.IsActivating = false;
            state.ActivateBtn.IsEnabled = true;
            state.ScanBtn.IsEnabled = true;
        }
    }

    private void AppendLog(string message)
    {
        var state = _state;
        if (state is null) return;
        state.LogText.Visibility = Visibility.Visible;
        state.LogText.Text += $"[{DateTime.Now:HH:mm:ss}] {message}\n";
    }

    private static string GetCountryName(string code) => code.ToUpperInvariant() switch
    {
        "CN" => "中国",
        "HK" => "中国香港",
        "TW" => "中国台湾",
        "JP" => "日本",
        "KR" => "韩国",
        "SG" => "新加坡",
        "US" => "美国",
        "CA" => "加拿大",
        "DE" => "德国",
        "FR" => "法国",
        "GB" => "英国",
        "NL" => "荷兰",
        "AU" => "澳大利亚",
        "LU" => "卢森堡",
        "RU" => "俄罗斯",
        _ => code
    };

    private sealed class DialogState
    {
        public FontIcon StatusIcon = null!;
        public Border StatusIconBorder = null!;
        public TextBlock StatusText = null!;
        public TextBlock StatusDesc = null!;
        public Button ScanBtn = null!;
        public Button ActivateBtn = null!;
        public Button RebootBtn = null!;
        public Button CancelRebootBtn = null!;
        public ProgressRing LoadingRing = null!;
        public TextBlock LoadingText = null!;
        public StackPanel LoadingPanel = null!;
        public StackPanel ServerList = null!;
        public ScrollViewer ListScroll = null!;
        public TextBlock LogText = null!;
        public KmsServerInfo? SelectedServer;
        public bool IsActivating;
        public Color AccentBlue;
        public Color AccentGreen;
        public Color AccentOrange;
    }
}

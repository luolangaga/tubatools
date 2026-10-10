using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Models;
using TubaWinUi3.Pages;
using Windows.UI;

namespace TubaWinUi3.Services;

public sealed class CertBlockTool : IBuiltinTool
{
    private DialogState? _state;

    public string Id => "cert-block";
    public string Name => LocalizationService.L("Builtin_cert-block_Name", "恶意软件拦截器");
    public string Description => LocalizationService.L("Builtin_cert-block_Desc", "通过将软件厂商证书加入系统不信任列表，阻止流氓软件安装和运行。");
    public string Glyph => "\uE72E";
    public string Category => "系统工具";
    public BuiltinToolKind Kind => BuiltinToolKind.Dialog;

    public Task ExecuteAsync(BuiltinToolContext context)
    {
        CertBlockService.LoadAsync();

        var content = BuildDialogContent();

        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        timer.Tick += (_, _) => RefreshUI();
        timer.Start();

        App.MainWindow?.NavigateToToolPage(typeof(ToolContentPage), new ToolContentPageParam
        {
            ToolId = Id,
            Title = "恶意软件拦截器",
            Description = "通过将软件厂商证书加入系统不信任列表，阻止流氓软件安装和运行",
            Glyph = Glyph,
            Content = content,
            OnClose = () =>
            {
                timer.Stop();
                _state = null;
            }
        });

        return Task.CompletedTask;
    }

    private ScrollViewer BuildDialogContent()
    {
        var vendorCountText = new TextBlock { FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
        var totalCertsText = new TextBlock { FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
        var blockedCertsText = new TextBlock { FontSize = 20, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = new SolidColorBrush(ThemeColors.AccentGreen) };
        var adminText = new TextBlock { FontSize = 14, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };

        var vendorCard = MakeStatCard("厂商", vendorCountText, "\uE7F4");
        var certCard = MakeStatCard("证书", totalCertsText, "\uE72E");
        var blockedCard = MakeStatCard("已封锁", blockedCertsText, "\uE72E");
        var adminCard = MakeStatCard("管理员", adminText, "\uE77B");

        var statsGrid = new Grid { ColumnSpacing = 10 };
        statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        statsGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        statsGrid.Children.Add(vendorCard); Grid.SetColumn(vendorCard, 0);
        statsGrid.Children.Add(certCard); Grid.SetColumn(certCard, 1);
        statsGrid.Children.Add(blockedCard); Grid.SetColumn(blockedCard, 2);
        statsGrid.Children.Add(adminCard); Grid.SetColumn(adminCard, 3);

        var adminWarning = new InfoBar
        {
            Title = "需要管理员权限",
            Message = "恶意软件拦截器需要以管理员身份运行本程序才能操作。请右键程序选择「以管理员身份运行」后重试。",
            Severity = InfoBarSeverity.Error,
            IsOpen = !CertBlockService.IsAdmin,
            IsClosable = false
        };

        var isAdmin = CertBlockService.IsAdmin;
        var blockAllBtn = new Button { Content = "全部封锁", MinWidth = 90, IsEnabled = isAdmin };
        var unblockAllBtn = new Button { Content = "全部解锁", MinWidth = 90, IsEnabled = isAdmin };
        var refreshBtn = new Button { Content = "刷新", MinWidth = 70 };

        blockAllBtn.Click += (_, _) => { CertBlockService.BlockAll(); };
        unblockAllBtn.Click += (_, _) => { CertBlockService.UnblockAll(); };
        refreshBtn.Click += (_, _) => { CertBlockService.Reload(); };

        var actionBar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        actionBar.Children.Add(blockAllBtn);
        actionBar.Children.Add(unblockAllBtn);
        actionBar.Children.Add(refreshBtn);

        var vendorList = new StackPanel { Spacing = 2 };
        var listScroll = new ScrollViewer
        {
            Content = vendorList,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto
        };

        var loadingRing = new ProgressRing { Width = 40, Height = 40, IsActive = true };
        var loadingText = new TextBlock { Text = "正在加载证书...", FontSize = 14, Opacity = 0.68 };
        var loadingPanel = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = 8,
            Children = { loadingRing, loadingText }
        };

        var contentGrid = new Grid { RowSpacing = 14 };
        contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        contentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        contentGrid.Children.Add(statsGrid); Grid.SetRow(statsGrid, 0);
        contentGrid.Children.Add(adminWarning); Grid.SetRow(adminWarning, 1);
        contentGrid.Children.Add(actionBar); Grid.SetRow(actionBar, 2);
        contentGrid.Children.Add(loadingPanel); Grid.SetRow(loadingPanel, 3);
        contentGrid.Children.Add(listScroll); Grid.SetRow(listScroll, 3);

        var root = new StackPanel { Spacing = 14, Padding = new Thickness(24, 0, 24, 16) };
        root.Children.Add(new TextBlock
        {
            Text = "Malware-Patch 恶意软件拦截引擎 · 将厂商证书加入系统不信任列表",
            FontSize = 12,
            Opacity = 0.68
        });
        root.Children.Add(contentGrid);

        _state = new DialogState
        {
            VendorCountText = vendorCountText,
            TotalCertsText = totalCertsText,
            BlockedCertsText = blockedCertsText,
            AdminText = adminText,
            AdminWarning = adminWarning,
            VendorList = vendorList,
            LoadingPanel = loadingPanel,
            ListScroll = listScroll,
            BlockAllBtn = blockAllBtn,
            UnblockAllBtn = unblockAllBtn
        };

        return new ScrollViewer
        {
            Content = root,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalAlignment = HorizontalAlignment.Center,
            MaxWidth = 1080
        };
    }

    private void RefreshUI()
    {
        var state = _state;
        if (state is null) return;

        state.VendorCountText.Text = CertBlockService.VendorCount.ToString();
        state.TotalCertsText.Text = CertBlockService.TotalCerts.ToString();
        state.BlockedCertsText.Text = CertBlockService.BlockedCerts.ToString();
        state.AdminText.Text = CertBlockService.IsAdmin ? "是" : "否";

        if (CertBlockService.IsLoading)
        {
            state.LoadingPanel.Visibility = Visibility.Visible;
            state.ListScroll.Visibility = Visibility.Collapsed;
            return;
        }

        state.LoadingPanel.Visibility = Visibility.Collapsed;
        state.ListScroll.Visibility = Visibility.Visible;

        var vendors = CertBlockService.Vendors;
        if (vendors.Count == 0) return;

        if (state.VendorList.Children.Count != vendors.Count)
        {
            state.VendorList.Children.Clear();
            foreach (var vendor in vendors)
            {
                state.VendorList.Children.Add(CreateVendorRow(vendor, state));
            }
        }
        else
        {
            for (int i = 0; i < vendors.Count; i++)
            {
                UpdateVendorRow(state.VendorList.Children[i] as Border, vendors[i]);
            }
        }
    }

    private Border CreateVendorRow(CertBlockVendor vendor, DialogState state)
    {
        var indicator = new Border
        {
            Width = 4,
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(0, 0, 10, 0),
            VerticalAlignment = VerticalAlignment.Stretch
        };
        UpdateIndicator(indicator, vendor);

        var nameText = new TextBlock
        {
            Text = vendor.DisplayName,
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            TextAlignment = TextAlignment.Left
        };
        var countText = new TextBlock
        {
            FontSize = 12,
            Opacity = 0.68,
            TextAlignment = TextAlignment.Left
        };
        UpdateCountText(countText, vendor);

        var infoPanel = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        infoPanel.Children.Add(nameText);
        infoPanel.Children.Add(countText);

        var toggle = new ToggleSwitch
        {
            IsOn = vendor.IsBlocked,
            IsEnabled = CertBlockService.IsAdmin,
            Margin = new Thickness(8, 0, 0, 0),
            OnContent = "",
            OffContent = ""
        };
        toggle.Toggled += (_, _) =>
        {
            if (toggle.IsOn)
                CertBlockService.BlockVendor(vendor);
            else
                CertBlockService.UnblockVendor(vendor);
            UpdateIndicator(indicator, vendor);
            UpdateCountText(countText, vendor);
        };

        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(indicator); Grid.SetColumn(indicator, 0);
        grid.Children.Add(infoPanel); Grid.SetColumn(infoPanel, 1);
        grid.Children.Add(toggle); Grid.SetColumn(toggle, 2);

        var border = new Border
        {
            Padding = new Thickness(10, 10, 10, 10),
            BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = grid
        };

        return border;
    }

    private static void UpdateVendorRow(Border? border, CertBlockVendor vendor)
    {
        if (border is null) return;
        var grid = border.Child as Grid;
        if (grid is null) return;

        foreach (var child in grid.Children)
        {
            if (child is Border indicator && indicator.Width == 4)
                UpdateIndicator(indicator, vendor);
            if (child is StackPanel panel)
            {
                foreach (var item in panel.Children)
                {
                    if (item is TextBlock { FontSize: 12 } countText)
                        UpdateCountText(countText, vendor);
                }
            }
            if (child is ToggleSwitch toggle)
            {
                if (toggle.IsOn != vendor.IsBlocked)
                    toggle.IsOn = vendor.IsBlocked;
            }
        }
    }

    private static void UpdateIndicator(Border indicator, CertBlockVendor vendor)
    {
        if (vendor.IsBlocked)
            indicator.Background = new SolidColorBrush(ThemeColors.AccentGreen);
        else if (vendor.IsPartiallyBlocked)
            indicator.Background = new SolidColorBrush(ThemeColors.AccentOrange);
        else
            indicator.Background = new SolidColorBrush(ThemeColors.BorderColor);
    }

    private static void UpdateCountText(TextBlock textBlock, CertBlockVendor vendor)
    {
        textBlock.Text = $"{vendor.BlockedCount} / {vendor.TotalCount} 个证书";
    }

    private static Border MakeStatCard(string label, TextBlock value, string glyph)
    {
        var iconBorder = new Border
        {
            Width = 36,
            Height = 36,
            Background = new SolidColorBrush(Color.FromArgb(26, ThemeColors.PrimaryText.R, ThemeColors.PrimaryText.G, ThemeColors.PrimaryText.B)),
            CornerRadius = new CornerRadius(8),
            Child = new FontIcon { FontSize = 16, Glyph = glyph }
        };
        var labelBlock = new TextBlock { Text = label, FontSize = 12, Opacity = 0.68 };
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
            CornerRadius = new CornerRadius(8),
            Child = grid
        };
    }

    private sealed class DialogState
    {
        public TextBlock VendorCountText = null!;
        public TextBlock TotalCertsText = null!;
        public TextBlock BlockedCertsText = null!;
        public TextBlock AdminText = null!;
        public InfoBar AdminWarning = null!;
        public StackPanel VendorList = null!;
        public StackPanel LoadingPanel = null!;
        public ScrollViewer ListScroll = null!;
        public Button BlockAllBtn = null!;
        public Button UnblockAllBtn = null!;
    }
}

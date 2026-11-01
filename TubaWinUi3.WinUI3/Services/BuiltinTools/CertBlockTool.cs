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

        var compatibilityWarning = new InfoBar
        {
            Title = "兼容性提示",
            Message = "已知反馈：租号玩系列软件依赖系统证书，全部禁用证书可能会被判定为环境异常。使用这类软件前请勿点击「全部封锁」；如已出现异常，请点击「全部解锁」后重试。（反馈：GitHub Issue #229）",
            Severity = InfoBarSeverity.Warning,
            IsOpen = true,
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

        var searchBox = new TextBox { PlaceholderText = "搜索厂商或证书名称", HorizontalAlignment = HorizontalAlignment.Stretch };
        var filter = new ComboBox { MinWidth = 130, HorizontalAlignment = HorizontalAlignment.Stretch };
        filter.Items.Add("全部状态");
        filter.Items.Add("已全部封锁");
        filter.Items.Add("部分封锁");
        filter.Items.Add("未封锁");
        filter.SelectedIndex = 0;
        filter.SelectionChanged += (_, _) => { if (_state is not null) { _state.VendorList.Children.Clear(); RefreshUI(); } };
        var searchGrid = new Grid { ColumnSpacing = 8 };
        searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        searchGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        searchGrid.Children.Add(searchBox);
        searchGrid.Children.Add(filter); Grid.SetColumn(filter, 1);
        var listHeading = new TextBlock { Text = "厂商列表", FontSize = 18, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold };
        var resultText = new TextBlock { FontSize = 12, Opacity = 0.68 };
        var emptyText = new TextBlock { Text = "没有找到符合条件的厂商", Opacity = 0.65, Margin = new Thickness(0, 24, 0, 24), HorizontalAlignment = HorizontalAlignment.Center, Visibility = Visibility.Collapsed };
        var controls = new StackPanel { Spacing = 12 };
        controls.Children.Add(actionBar);
        controls.Children.Add(listHeading);
        controls.Children.Add(searchGrid);
        controls.Children.Add(resultText);
        searchBox.TextChanged += (_, _) => { if (_state is not null) { _state.VendorList.Children.Clear(); RefreshUI(); } };

        var vendorList = new StackPanel { Spacing = 6 };
        var listScroll = new ScrollViewer
        {
            Content = new StackPanel { Children = { vendorList, emptyText } },
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
        contentGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        contentGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        contentGrid.Children.Add(statsGrid); Grid.SetRow(statsGrid, 0);
        contentGrid.Children.Add(adminWarning); Grid.SetRow(adminWarning, 1);
        contentGrid.Children.Add(compatibilityWarning); Grid.SetRow(compatibilityWarning, 2);
        contentGrid.Children.Add(controls); Grid.SetRow(controls, 3);
        contentGrid.Children.Add(loadingPanel); Grid.SetRow(loadingPanel, 4);
        contentGrid.Children.Add(listScroll); Grid.SetRow(listScroll, 4);

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
            SearchBox = searchBox,
            Filter = filter,
            ResultText = resultText,
            EmptyText = emptyText,
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

        var query = state.SearchBox.Text.Trim();
        var vendors = CertBlockService.Vendors.Where(v =>
            v.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            v.Id.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            v.Certificates.Any(c => c.CommonName.Contains(query, StringComparison.OrdinalIgnoreCase)))
            .Where(v => state.Filter.SelectedIndex switch
            {
                1 => v.IsBlocked,
                2 => v.IsPartiallyBlocked,
                3 => !v.IsBlocked && !v.IsPartiallyBlocked,
                _ => true
            }).ToList();
        state.ResultText.Text = $"显示 {vendors.Count} / {CertBlockService.VendorCount} 个厂商";
        state.EmptyText.Visibility = vendors.Count == 0 ? Visibility.Visible : Visibility.Collapsed;

        if (state.VendorList.Children.Count != vendors.Count ||
            vendors.Where((vendor, index) => index >= state.VendorList.Children.Count || (state.VendorList.Children[index] as Border)?.Tag as string != vendor.Id).Any())
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
                state.UpdatingRows = true;
                try { UpdateVendorRow(state.VendorList.Children[i] as Border, vendors[i]); }
                finally { state.UpdatingRows = false; }
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

        var infoPanel = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
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
            if (state.UpdatingRows) return;
            if (toggle.IsOn)
                CertBlockService.BlockVendor(vendor);
            else
                CertBlockService.UnblockVendor(vendor);
            UpdateIndicator(indicator, vendor);
            UpdateCountText(countText, vendor);
        };

        var details = new Button { Content = "证书明细", VerticalAlignment = VerticalAlignment.Center };
        var detailsList = new StackPanel { Spacing = 8, MaxWidth = 440 };
        foreach (var cert in vendor.Certificates)
        {
            detailsList.Children.Add(new TextBlock
            {
                Text = $"{cert.CommonName}\n指纹：{cert.Thumbprint}",
                TextWrapping = TextWrapping.Wrap,
                FontSize = 12
            });
        }
        details.Flyout = new Flyout { Content = new ScrollViewer { Content = detailsList, MaxHeight = 360 } };
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        actions.Children.Add(details);
        actions.Children.Add(toggle);
        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(indicator); Grid.SetColumn(indicator, 0);
        grid.Children.Add(infoPanel); Grid.SetColumn(infoPanel, 1);
        grid.Children.Add(actions); Grid.SetColumn(actions, 2);

        var border = new Border
        {
            Tag = vendor.Id,
            Padding = new Thickness(14, 12, 14, 12),
            Background = new SolidColorBrush(ThemeColors.CardBg),
            CornerRadius = new CornerRadius(8),
            BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
            BorderThickness = new Thickness(1),
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
            if (child is StackPanel actions)
            {
                foreach (var item in actions.Children)
                    if (item is ToggleSwitch toggle && toggle.IsOn != vendor.IsBlocked)
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
        textBlock.Text = $"{(vendor.IsBlocked ? "已全部封锁" : vendor.IsPartiallyBlocked ? "部分封锁" : "未封锁")} · {vendor.BlockedCount} / {vendor.TotalCount} 个证书";
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
        public TextBox SearchBox = null!;
        public ComboBox Filter = null!;
        public TextBlock ResultText = null!;
        public TextBlock EmptyText = null!;
        public bool UpdatingRows;
        public StackPanel LoadingPanel = null!;
        public ScrollViewer ListScroll = null!;
        public Button BlockAllBtn = null!;
        public Button UnblockAllBtn = null!;
    }
}

using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Text;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.WinUI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using SkiaSharp;
using TubaWinUi3.Controls;
using TubaWinUi3.Models;
using TubaWinUi3.Services;

namespace TubaWinUi3.Pages;

/// <summary>
/// 硬件详细信息页：全部硬件参数的瀑布流卡片 + 实时监控。
/// 卡片由「分区定义 → 采集数据 → 行项目」构建，排布完全自动（自适应列数 + 均衡分列 + 铺满对齐），
/// 不提供手动拖动/缩放——布局始终由默认策略保证合理。
/// </summary>
public sealed partial class HardwareDetailPage : Page, ILocalizablePage
{
    private bool _dataLoaded;
    private DispatcherTimer? _monitorTimer;
    private const int MaxPoints = 50;
    private HardwareDetailData? _lastDetailData;
    private bool _pageAlive;

    private readonly ObservableCollection<double> _cpuHist = [];
    private readonly ObservableCollection<double> _gpuHist = [];
    private readonly ObservableCollection<double> _memHist = [];
    private readonly ObservableCollection<double> _diskReadHist = [], _diskWriteHist = [];
    private readonly ObservableCollection<double> _batHist = [];
    private bool _isLaptop;

    public HardwareDetailPage()
    {
        InitializeComponent();
        ChartInitializer.EnsureConfigured();
        Loaded += HardwareDetailPage_Loaded;
        Unloaded += HardwareDetailPage_Unloaded;
        // 代码构建的画刷（图表调色板 / 条目计数）不会随主题自动刷新，切换主题后按缓存数据重渲染
        ActualThemeChanged += (_, _) =>
        {
            if (!_pageAlive) return;
            StopRealtimeMonitor();
            InitRealtimeMonitor();
            if (_lastDetailData is not null)
                ApplyData(_lastDetailData);
        };
    }

    private void HardwareDetailPage_Loaded(object sender, RoutedEventArgs e)
    {
        _pageAlive = true;
        _ = LoadDetailAsync();
        InitRealtimeMonitor();
        ContentScroller.SizeChanged += ContentScroller_SizeChanged;
        RealtimeSection.SizeChanged += ContentScroller_SizeChanged;
        UpdateBoardFillHeight();
    }

    private void HardwareDetailPage_Unloaded(object sender, RoutedEventArgs e)
    {
        _pageAlive = false;
        StopRealtimeMonitor();
        ContentScroller.SizeChanged -= ContentScroller_SizeChanged;
        RealtimeSection.SizeChanged -= ContentScroller_SizeChanged;
    }

    private void ContentScroller_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateBoardFillHeight();
    }

    /// <summary>
    /// 把「铺满」目标高度交给卡片面板：视口高度减去页头 / 提示 / 实时区等固定内容，
    /// 剩下的空间让卡片自动拉高填满（内容更高时照常滚动，不压缩）。
    /// </summary>
    private void UpdateBoardFillHeight()
    {
        try
        {
            var viewport = ContentScroller.ViewportHeight;
            if (viewport <= 0)
            {
                Board.FillHeight = 0;
                return;
            }

            var padding = ContentScroller.Padding;
            var chrome = HeaderPanel.ActualHeight
                       + RealtimeSection.ActualHeight
                       + RealtimeSection.Margin.Top
                       + LayoutRoot.Spacing * 2;
            Board.FillHeight = Math.Max(0, viewport - padding.Top - padding.Bottom - chrome - 2);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[HwDetail] 铺满高度计算失败: {ex.Message}");
            Board.FillHeight = 0;
        }
    }

    #region 分区定义

    /// <summary>
    /// 页面的信息分区：标题（数据键，显示时翻译）、分区枚举、HwIcons 图标、默认跨列数。
    /// 采集侧新增数据块必须在这里补一条，否则该数据永远不会显示
    /// （<c>SectionDefinitions_CoverEveryDetailPart</c> 回归测试拦截漏配）；
    /// 每个图标都需要 <c>Assets/HwIcons/&lt;icon&gt;.svg</c>。
    /// </summary>
    private static readonly List<SectionDefinition> SectionDefinitions =
    [
        new("系统", HardwareDetailPart.System, "system", 2),
        new("设备", HardwareDetailPart.Computer, "computer", 1),
        new("处理器", HardwareDetailPart.Cpu, "cpu", 2),
        new("主板", HardwareDetailPart.Motherboard, "motherboard", 2),
        new("内存", HardwareDetailPart.Memory, "memory", 1),
        new("显卡", HardwareDetailPart.Gpu, "gpu", 1),
        new("NPU", HardwareDetailPart.Npu, "npu", 1),
        new("硬盘", HardwareDetailPart.Disk, "disk", 2),
        new("显示器", HardwareDetailPart.Display, "display", 1),
        new("声卡", HardwareDetailPart.Sound, "sound", 1),
        new("网卡", HardwareDetailPart.Network, "network", 2),
        new("电池", HardwareDetailPart.Battery, "battery", 1),
        new("安全", HardwareDetailPart.Security, "security", 1),
        new("USB", HardwareDetailPart.Usb, "usb", 1),
        new("其他设备", HardwareDetailPart.Devices, "devices", 1),
    ];

    private sealed record SectionDefinition(string Title, HardwareDetailPart Part, string Icon, int Span);

    private sealed record DetailSection(SectionDefinition Definition, List<HardwareInfoItem> Items);

    private List<DetailSection> BuildSections(HardwareDetailData data)
    {
        var sections = new List<DetailSection>();
        foreach (var definition in SectionDefinitions)
        {
            var items = BuildItems(definition.Part, data);
            if (items.Count > 0)
                sections.Add(new DetailSection(definition, items));
        }
        return sections;
    }

    private static List<HardwareInfoItem> BuildItems(HardwareDetailPart part, HardwareDetailData data) => part switch
    {
        HardwareDetailPart.System => BuildWindowsItems(data.Windows),
        HardwareDetailPart.Computer => BuildComputerItems(data.Computer),
        HardwareDetailPart.Cpu => BuildCpuItems(data.Cpu),
        HardwareDetailPart.Motherboard => BuildBoardItems(data.Motherboard),
        HardwareDetailPart.Memory => BuildMemoryItems(data.Memory),
        HardwareDetailPart.Gpu => BuildGpuItems(data.Gpus),
        HardwareDetailPart.Npu => BuildNpuItems(data.Npu),
        HardwareDetailPart.Disk => BuildDiskItems(data.Disks),
        HardwareDetailPart.Display => BuildDisplayItems(data.Displays),
        HardwareDetailPart.Sound => BuildSoundItems(data.SoundDevices),
        HardwareDetailPart.Network => BuildNetworkItems(data.NetworkAdapters),
        HardwareDetailPart.Battery => BuildBatteryItems(data.Battery),
        HardwareDetailPart.Security => BuildSecurityItems(data.Security),
        HardwareDetailPart.Usb => BuildUsbItems(data.Usb),
        HardwareDetailPart.Devices => BuildDeviceIssueItems(data.OtherDevices),
        _ => [],
    };

    #endregion

    #region 实时监控

    /// <summary>主题语义色 → Skia 画刷色（保留原有 alpha 派生，如 40/30）。</summary>
    private static SKColor ChartColor(Windows.UI.Color color, byte alpha = 255)
        => new(color.R, color.G, color.B, alpha);

    private void InitRealtimeMonitor()
    {
        _isLaptop = HardwareInfoService.IsLaptop();
        BatteryCard.Visibility = _isLaptop ? Visibility.Visible : Visibility.Collapsed;

        CpuChart.Series = [new LineSeries<double>
        {
            Values = _cpuHist,
            Stroke = new SolidColorPaint(ChartColor(ThemeColors.Series1)) { StrokeThickness = 2 },
            Fill = new SolidColorPaint(ChartColor(ThemeColors.Series1, 40)),
            GeometrySize = 0,
            LineSmoothness = 0.4
        }];
        CpuChart.XAxes = [new Axis { IsVisible = false }];
        CpuChart.YAxes = [new Axis { IsVisible = false, MinLimit = 0, MaxLimit = 100 }];
        CpuChart.AnimationsSpeed = TimeSpan.FromMilliseconds(100);
        CpuChart.EasingFunction = null;

        GpuChart.Series = [new LineSeries<double>
        {
            Values = _gpuHist,
            Stroke = new SolidColorPaint(ChartColor(ThemeColors.Series2)) { StrokeThickness = 2 },
            Fill = new SolidColorPaint(ChartColor(ThemeColors.Series2, 40)),
            GeometrySize = 0,
            LineSmoothness = 0.4
        }];
        GpuChart.XAxes = [new Axis { IsVisible = false }];
        GpuChart.YAxes = [new Axis { IsVisible = false, MinLimit = 0, MaxLimit = 100 }];
        GpuChart.AnimationsSpeed = TimeSpan.FromMilliseconds(100);
        GpuChart.EasingFunction = null;

        MemChart.Series = [new LineSeries<double>
        {
            Values = _memHist,
            Stroke = new SolidColorPaint(ChartColor(ThemeColors.Series3)) { StrokeThickness = 2 },
            Fill = new SolidColorPaint(ChartColor(ThemeColors.Series3, 40)),
            GeometrySize = 0,
            LineSmoothness = 0.4
        }];
        MemChart.XAxes = [new Axis { IsVisible = false }];
        MemChart.YAxes = [new Axis { IsVisible = false, MinLimit = 0, MaxLimit = 100 }];
        MemChart.AnimationsSpeed = TimeSpan.FromMilliseconds(100);
        MemChart.EasingFunction = null;

        DiskChart.Series =
        [
            new LineSeries<double>
            {
                Values = _diskReadHist,
                Stroke = new SolidColorPaint(ChartColor(ThemeColors.Series1)) { StrokeThickness = 1.5f },
                Fill = new SolidColorPaint(ChartColor(ThemeColors.Series1, 30)),
                GeometrySize = 0,
                LineSmoothness = 0.4
            },
            new LineSeries<double>
            {
                Values = _diskWriteHist,
                Stroke = new SolidColorPaint(ChartColor(ThemeColors.Series2)) { StrokeThickness = 1.5f },
                Fill = new SolidColorPaint(ChartColor(ThemeColors.Series2, 30)),
                GeometrySize = 0,
                LineSmoothness = 0.4
            }
        ];
        DiskChart.XAxes = [new Axis { IsVisible = false }];
        DiskChart.YAxes = [new Axis { IsVisible = false, MinLimit = 0 }];
        DiskChart.AnimationsSpeed = TimeSpan.FromMilliseconds(100);
        DiskChart.EasingFunction = null;

        if (_isLaptop)
        {
            BatChart.Series = [new LineSeries<double>
            {
                Values = _batHist,
                Stroke = new SolidColorPaint(ChartColor(ThemeColors.AccentGreen)) { StrokeThickness = 2 },
                Fill = new SolidColorPaint(ChartColor(ThemeColors.AccentGreen, 40)),
                GeometrySize = 0,
                LineSmoothness = 0.4
            }];
            BatChart.XAxes = [new Axis { IsVisible = false }];
            BatChart.YAxes = [new Axis { IsVisible = false, MinLimit = 0, MaxLimit = 100 }];
            BatChart.AnimationsSpeed = TimeSpan.FromMilliseconds(100);
            BatChart.EasingFunction = null;
        }

        _monitorTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _monitorTimer.Tick += MonitorTimer_Tick;
        _monitorTimer.Start();
        _ = UpdateMonitorAsync();
    }

    private void StopRealtimeMonitor()
    {
        _monitorTimer?.Stop();
        _monitorTimer = null;
    }

    private async void MonitorTimer_Tick(object? sender, object e)
    {
        try
        {
            await UpdateMonitorAsync();
        }
        catch
        {
            // 页面销毁/卸载期间更新 UI 可能抛 COMException，忽略即可
        }
    }

    private async Task UpdateMonitorAsync()
    {
        MonitorSample sample;
        try
        {
            sample = await Task.Run(() => LiteMonitorService.Instance.Read());
        }
        catch
        {
            return;
        }

        // 读取期间页面可能已销毁（用户快速返回）：不再触碰已卸载的 UI
        if (!_pageAlive) return;

        Push(_cpuHist, Val(sample.CpuLoad));
        Push(_gpuHist, Val(sample.GpuLoad));
        Push(_memHist, Val(sample.MemLoad));
        Push(_diskReadHist, Val(sample.DiskReadMBs));
        Push(_diskWriteHist, Val(sample.DiskWriteMBs));

        if (_isLaptop)
            Push(_batHist, Val(sample.BatPercent));

        try
        {
            CpuLoadText.Text = sample.CpuLoad >= 0 ? $"{sample.CpuLoad:0}%" : "--";
            GpuLoadText.Text = sample.GpuLoad >= 0 ? $"{sample.GpuLoad:0}%" : "--";
            MemLoadText.Text = sample.MemLoad >= 0 ? $"{sample.MemLoad:0}%" : "--";
            DiskReadText.Text = sample.DiskReadMBs >= 0 ? $"↑{sample.DiskReadMBs:0.0}" : "--";
            DiskWriteText.Text = sample.DiskWriteMBs >= 0 ? $"↓{sample.DiskWriteMBs:0.0}" : "--";

            if (_isLaptop)
            {
                BatPercentText.Text = sample.BatPercent >= 0 ? $"{sample.BatPercent:0}%" : "--";
                BatPowerText.Text = sample.BatPower >= 0
                    ? $"{(sample.BatCharging ? "+" : "")}{sample.BatPower:0.1}W"
                    : "";
            }
        }
        catch
        {
            // 极少数情况下控件已被释放（如页面快速切换），不再更新
        }
    }

    private static void Push(ObservableCollection<double> list, double value)
    {
        list.Add(value);
        if (list.Count > MaxPoints) list.RemoveAt(0);
    }

    private static double Val(float v) => v >= 0 ? Math.Round(v, 1) : 0;

    #endregion

    #region 卡片构建

    private void ApplyData(HardwareDetailData data)
    {
        _lastDetailData = data;

        // 顺序 = 分区定义顺序；列与跨度完全交给瀑布流自动计算（无手动排布）
        Board.Children.Clear();
        foreach (var section in BuildSections(data))
        {
            var card = CreateCard(section);
            CardBoard.SetColumnSpan(card, Math.Max(1, section.Definition.Span));
            Board.Children.Add(card);
        }

        // CPU-Z badge
        CpuzBadge.Visibility = data.Cpu?.IsVerified == true || data.Motherboard?.IsVerified == true
            ? Visibility.Visible : Visibility.Collapsed;

        _dataLoaded = true;

        // 卡片内容变化后重新校准铺满高度（内容区高度变化会影响可用空间）
        UpdateBoardFillHeight();
    }

    private FrameworkElement CreateCard(DetailSection section)
    {
        var card = new Border
        {
            Style = (Style)Resources["SectionCardStyle"],
            Child = new StackPanel { Spacing = 1 }
        };

        var panel = (StackPanel)card.Child;

        // 标题区：分区图标 + 标题 + 条目数
        var header = new Grid
        {
            ColumnSpacing = 8,
            Margin = new Thickness(0, 0, 0, 6),
            Padding = new Thickness(2, 2, 2, 2)
        };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var icon = HardwareIconService.Get(section.Definition.Icon);
        if (icon is not null)
        {
            header.Children.Add(new Image
            {
                Source = icon,
                Width = 18,
                Height = 18,
                VerticalAlignment = VerticalAlignment.Center
            });
        }

        var title = new TextBlock
        {
            Text = LocalizationService.TranslateHardwareLabel(section.Definition.Title),
            Style = (Style)Resources["SectionTitleStyle"]
        };
        Grid.SetColumn(title, 1);
        header.Children.Add(title);

        var count = new TextBlock
        {
            Text = section.Items.Count.ToString(),
            FontSize = 12,
            Foreground = new SolidColorBrush(ThemeColors.DimText),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 4, 0)
        };
        Grid.SetColumn(count, 2);
        header.Children.Add(count);

        panel.Children.Add(header);

        var repeater = new ItemsRepeater
        {
            ItemsSource = section.Items,
            ItemTemplate = (DataTemplate)Resources["DetailRowTemplate"]
        };
        panel.Children.Add(repeater);

        return card;
    }

    #endregion

    #region 加载 / 导出 / 复制

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        _ = LoadDetailAsync(forceRefresh: true);
    }

    /// <summary>语言切换后按当前语言重建参数卡片（标题等 Uid 控件自动更新）。</summary>
    public void ApplyLocalization()
    {
        if (_lastDetailData is not null)
            ApplyData(_lastDetailData);
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack)
            Frame.GoBack();
        else
            Frame.Navigate(typeof(HardwarePage), null, new SlideNavigationTransitionInfo() { Effect = SlideNavigationTransitionEffect.FromRight });
    }

    private async void ExportButton_Click(object sender, RoutedEventArgs e)
    {
        var data = _lastDetailData;
        if (data == null)
        {
            ShowStatusBar(LocalizationService.L("HwDetail_ExportFailed", "导出失败"), LocalizationService.L("HwDetail_NoData", "暂无硬件数据"), InfoBarSeverity.Warning);
            return;
        }

        try
        {
            var filePath = Path.Combine(Path.GetTempPath(), $"{LocalizationService.L("HwDetail_ExportFilePrefix", "硬件信息")}_{DateTime.Now:yyyyMMdd_HHmmss}.html");
            var html = BuildHtml(data);
            await File.WriteAllTextAsync(filePath, html);
            ShowStatusBar(LocalizationService.L("HwDetail_ExportOk", "导出成功"), filePath, InfoBarSeverity.Success);
            Process.Start(new ProcessStartInfo(filePath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ShowStatusBar(LocalizationService.L("HwDetail_ExportFailed", "导出失败"), ex.Message, InfoBarSeverity.Error);
        }
    }

    private string BuildHtml(HardwareDetailData data)
    {
        var sb = new StringBuilder();
        sb.AppendLine("<!DOCTYPE html>");
        sb.AppendLine($"<html lang=\"{LocalizationService.CurrentLanguage}\">");
        sb.AppendLine("<head>");
        sb.AppendLine("<meta charset=\"UTF-8\">");
        sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1.0\">");
        sb.AppendLine($"<title>{LocalizationService.L("HwDetail_HtmlTitle", "硬件详细信息")}</title>");
        sb.AppendLine("<style>");
        sb.AppendLine("*{margin:0;padding:0;box-sizing:border-box}");
        sb.AppendLine("body{font-family:-apple-system,\"Microsoft YaHei\",\"Segoe UI\",sans-serif;background:rgb(245,245,245);color:rgb(26,26,26);padding:24px}");
        sb.AppendLine(".container{max-width:1200px;margin:0 auto}");
        sb.AppendLine("h1{font-size:22px;font-weight:600;margin-bottom:4px}");
        sb.AppendLine(".sub{font-size:13px;color:rgb(136,136,136);margin-bottom:20px}");
        sb.AppendLine(".grid{display:grid;grid-template-columns:repeat(3,1fr);gap:12px}");
        sb.AppendLine("@media(max-width:900px){.grid{grid-template-columns:repeat(2,1fr)}}");
        sb.AppendLine("@media(max-width:560px){.grid{grid-template-columns:1fr}}");
        sb.AppendLine(".card{background:rgb(255,255,255);border:1px solid rgb(229,229,229);border-radius:8px;padding:12px 14px}");
        sb.AppendLine(".card-title{font-size:13px;font-weight:600;color:rgb(85,85,85);margin-bottom:6px;padding-bottom:4px;border-bottom:1px solid rgb(240,240,240)}");
        sb.AppendLine(".row{display:flex;padding:3px 0;font-size:12px;line-height:1.6}");
        sb.AppendLine(".row-label{color:rgb(136,136,136);min-width:96px;flex-shrink:0}");
        sb.AppendLine(".row-sep{width:1px;background:rgb(224,224,224);margin:2px 8px;flex-shrink:0}");
        sb.AppendLine(".row-value{color:rgb(26,26,26);font-weight:500;word-break:break-all}");
        sb.AppendLine(".footer{margin-top:20px;font-size:11px;color:rgb(187,187,187);text-align:center}");
        sb.AppendLine("</style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine("<div class=\"container\">");
        sb.AppendLine($"<h1>{LocalizationService.L("HwDetail_HtmlTitle", "硬件详细信息")}</h1>");
        sb.AppendLine($"<div class=\"sub\">{string.Format(LocalizationService.L("HwDetail_HtmlSubtitle", "{0} · 导出时间 {1}"), LocalizationService.L("App_Title", "图吧工具箱CE"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))}</div>");
        sb.AppendLine("<div class=\"grid\">");

        foreach (var section in BuildSections(data))
        {
            var title = LocalizationService.TranslateHardwareLabel(section.Definition.Title);
            sb.AppendLine($"<div class=\"card\"><div class=\"card-title\">{HtmlEscape(title)}</div>");
            foreach (var item in section.Items)
            {
                if (string.IsNullOrWhiteSpace(item.Label) && string.IsNullOrWhiteSpace(item.Value)) continue;
                sb.AppendLine($"<div class=\"row\"><span class=\"row-label\">{HtmlEscape(LocalizationService.TranslateHardwareLabel(item.Label))}</span><span class=\"row-sep\"></span><span class=\"row-value\">{HtmlEscape(item.Value)}</span></div>");
            }
            sb.AppendLine("</div>");
        }

        sb.AppendLine("</div>");
        sb.AppendLine($"<div class=\"footer\">{string.Format(LocalizationService.L("HwDetail_HtmlFooter", "由{0} 自动生成"), LocalizationService.L("App_Title", "图吧工具箱CE"))}</div>");
        sb.AppendLine("</div>");
        sb.AppendLine("</body>");
        sb.AppendLine("</html>");

        return sb.ToString();
    }

    private async Task LoadDetailAsync(bool forceRefresh = false)
    {
        if (_dataLoaded && !forceRefresh) return;
        SetLoading(true);

        try
        {
            var data = await HardwareInfoService.LoadDetailForDisplayAsync(forceRefresh);

            // 数据构建（WMI/LHM 可能耗时数秒）期间页面可能已被用户关闭：
            // 此时不能触碰已卸载的视觉树，否则在 async void 上下文中
            // 抛出的 COMException 会直接导致进程崩溃（闪退）。
            if (!_pageAlive) return;

            ApplyData(data);
            StatusBar.IsOpen = false;
        }
        catch (Exception ex)
        {
            if (_pageAlive)
                ShowStatusBar(LocalizationService.L("Hw_LoadFailed", "硬件信息读取失败"), ex.Message, InfoBarSeverity.Error);
        }
        finally
        {
            if (_pageAlive)
                SetLoading(false);
        }
    }

    #endregion

    #region 行项目构建

    private static void AddIf(List<HardwareInfoItem> items, string label, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
            items.Add(Item(label, value));
    }

    private static List<HardwareInfoItem> BuildWindowsItems(WindowsDetail? w)
    {
        var items = new List<HardwareInfoItem>();
        if (w == null) return items;
        items.Add(Item("操作系统", JoinValues(w.ProductName, w.DisplayVersion is null ? null : $"({w.DisplayVersion})")));
        AddIf(items, "内部版本", w.Build);
        AddIf(items, "系统架构", w.Architecture);
        AddIf(items, "安装日期", w.InstallDate);
        AddIf(items, "安装语言", w.InstallLanguage);
        AddIf(items, "运行时间", w.Uptime);
        AddIf(items, "计算机名", w.ComputerName);
        AddIf(items, "域或工作组", w.DomainOrWorkgroup);
        AddIf(items, "授权状态", w.LicenseStatus);
        AddIf(items, "授权通道", w.LicenseChannel);
        AddIf(items, "产品密钥", w.PartialProductKey);
        AddIf(items, "产品 ID", w.ProductId);
        AddIf(items, "注册用户", w.RegisteredOwner);
        AddIf(items, "DirectX", w.DirectX);
        AddIf(items, ".NET Framework", w.DotNetFramework);
        AddIf(items, "显示缩放", w.DisplayScaling);
        AddIf(items, "系统盘", w.SystemDrive);
        AddIf(items, "系统目录", w.WindowsDirectory);
        AddIf(items, "物理内存（可用/总量）", w.PhysicalMemory);
        AddIf(items, "虚拟内存（可用/总量）", w.VirtualMemory);
        AddIf(items, "页面文件", w.PageFile);
        return items;
    }

    private static List<HardwareInfoItem> BuildComputerItems(ComputerDetail? c)
    {
        var items = new List<HardwareInfoItem>();
        if (c == null) return items;
        AddIf(items, "制造商", c.Manufacturer);
        AddIf(items, "型号", c.Model);
        AddIf(items, "产品系列", c.Family);
        AddIf(items, "SKU", c.Sku);
        AddIf(items, "系统类型", c.SystemType);
        AddIf(items, "机型", c.PcType);
        AddIf(items, "机箱类型", c.Chassis);
        AddIf(items, "序列号", c.SerialNumber);
        AddIf(items, "产品 UUID", c.Uuid);
        AddIf(items, "产品版本", c.ProductVersion);
        AddIf(items, "固件模式", c.FirmwareMode);
        return items;
    }

    private static List<HardwareInfoItem> BuildCpuItems(CpuDetail? cpu)
    {
        var items = new List<HardwareInfoItem>();
        if (cpu == null) return items;
        items.Add(Item("名称", cpu.Name));
        AddIf(items, "代号", cpu.CodeName);
        AddIf(items, "封装", cpu.Package);
        AddIf(items, "插槽", cpu.Socket);
        if (cpu.Cores > 0) items.Add(Item("核心数", $"{cpu.Cores}"));
        if (cpu.Threads > 0) items.Add(Item("线程数", $"{cpu.Threads}"));
        AddIf(items, "最大频率", cpu.MaxClockSpeed);
        AddIf(items, "当前频率", cpu.CurrentClockSpeed);
        AddIf(items, "外频", cpu.ExtClock);
        AddIf(items, "L2 缓存", cpu.L2CacheSize);
        AddIf(items, "L3 缓存", cpu.L3CacheSize);
        AddIf(items, "架构", cpu.Architecture);
        AddIf(items, "数据宽度", cpu.DataWidth);
        if (cpu.VirtualizationEnabled.HasValue)
            items.Add(Item("硬件虚拟化", cpu.VirtualizationEnabled.Value ? "已启用" : "未启用"));
        if (cpu.SlatEnabled.HasValue)
            items.Add(Item("二级地址转换", cpu.SlatEnabled.Value ? "已支持" : "不支持"));
        AddIf(items, "制造商", cpu.Manufacturer);
        AddIf(items, "ProcessorID", cpu.ProcessorId);
        return items;
    }

    private static List<HardwareInfoItem> BuildBoardItems(MotherboardDetail? mb)
    {
        var items = new List<HardwareInfoItem>();
        if (mb == null) return items;
        AddIf(items, "制造商", mb.Manufacturer);
        AddIf(items, "型号", mb.Model);
        AddIf(items, "版本", mb.Version);
        AddIf(items, "芯片组", mb.Chipset);
        AddIf(items, "序列号", mb.SerialNumber);
        AddIf(items, "BIOS 品牌", mb.BiosBrand);
        AddIf(items, "BIOS 版本", mb.BiosVersion);
        AddIf(items, "BIOS 日期", mb.BiosDate);
        AddIf(items, "BIOS 序列号", mb.BiosSerialNumber);
        AddIf(items, "SMBIOS 版本", mb.SmbiosVersion);
        return items;
    }

    private static List<HardwareInfoItem> BuildMemoryItems(MemoryDetail? mem)
    {
        var items = new List<HardwareInfoItem>();
        if (mem == null) return items;
        AddIf(items, "总容量", mem.TotalCapacity);
        AddIf(items, "类型", mem.MemoryType);
        AddIf(items, "通道模式", mem.ChannelMode);
        AddIf(items, "最大容量", mem.MaxCapacity);
        items.Add(Item("插槽", string.Format(LocalizationService.L("HwDetail_SlotsUsed", "{0}/{1} 已使用"), mem.UsedSlots, mem.TotalSlots)));
        foreach (var mod in mem.Modules)
        {
            var isSlot = mod.Capacity == "空";
            var label = isSlot ? $"  └ {mod.Designation}" : $"  ├ {mod.Designation}";
            var value = isSlot
                ? LocalizationService.L("HwDetail_EmptySlot", "空")
                : JoinValues(mod.Capacity, mod.Speed,
                    mod.RatedSpeed is null ? null : string.Format(LocalizationService.L("HwDetail_RatedSpeed", "额定 {0}"), mod.RatedSpeed),
                    mod.Type, mod.Manufacturer, mod.PartNumber, mod.SerialNumber, mod.Voltage);
            items.Add(Item(label, value));
        }
        return items;
    }

    private static List<HardwareInfoItem> BuildGpuItems(List<GpuDetail> gpus)
    {
        var items = new List<HardwareInfoItem>();
        foreach (var gpu in gpus)
        {
            if (items.Count > 0) items.Add(Separator());
            AddIf(items, "名称", gpu.Name);
            AddIf(items, "GPU 代码", gpu.GpuCode);
            AddIf(items, "显存", gpu.AdapterRAM);
            AddIf(items, "显存", gpu.MemorySize);
            AddIf(items, "显存类型", gpu.MemoryType);
            AddIf(items, "显存位宽", gpu.MemoryBus);
            AddIf(items, "厂商", gpu.AdapterCompatibility);
            AddIf(items, "驱动版本", gpu.DriverVersion);
            AddIf(items, "驱动日期", gpu.DriverDate);
            AddIf(items, "视频处理器", gpu.VideoProcessor);
            AddIf(items, "当前分辨率", gpu.CurrentResolution);
            AddIf(items, "刷新率", gpu.CurrentRefreshRate);
            AddIf(items, "设备 ID", FirstUseful(gpu.DeviceId, gpu.PnpDeviceId));
            AddIf(items, "状态", gpu.Status);
        }
        return items;
    }

    private static List<HardwareInfoItem> BuildNpuItems(NpuDetail? npu)
    {
        var items = new List<HardwareInfoItem>();
        if (npu == null) return items;
        AddIf(items, "名称", npu.Name);
        AddIf(items, "算力", npu.ComputeCapability);
        AddIf(items, "制造商", npu.Manufacturer);
        AddIf(items, "驱动版本", npu.DriverVersion);
        AddIf(items, "驱动日期", npu.DriverDate);
        return items;
    }

    private static List<HardwareInfoItem> BuildDiskItems(List<DiskDetail> disks)
    {
        var items = new List<HardwareInfoItem>();
        foreach (var disk in disks)
        {
            if (items.Count > 0) items.Add(Separator());
            AddIf(items, "型号", disk.Model);
            AddIf(items, "类型", disk.MediaType);
            AddIf(items, "容量", disk.Size);
            AddIf(items, "接口", disk.InterfaceType);
            AddIf(items, "分区样式", disk.PartitionStyle);
            AddIf(items, "转速", disk.RotationRate);
            AddIf(items, "状态", disk.Status);
            AddIf(items, "固件版本", disk.FirmwareRevision);
            AddIf(items, "序列号", disk.SerialNumber);
            if (disk.Temperature.HasValue) items.Add(Item("温度", $"{disk.Temperature.Value:0}°C"));
            AddIf(items, "健康状态", disk.SmartHealth);
            AddIf(items, "通电时间", disk.PowerOnHours);
            AddIf(items, "通电次数", disk.PowerOnCount);
            AddIf(items, "总写入量", disk.TotalWritten);
            foreach (var part in disk.Partitions)
            {
                var partLabel = $"  ├ {part.Name}";
                var partValue = JoinValues(part.DriveLetter, part.FileSystem, part.Size,
                    part.FreeSpace != null ? string.Format(LocalizationService.L("HwDetail_FreeSpace", "可用 {0}"), part.FreeSpace) : null);
                items.Add(Item(partLabel, partValue));
            }
        }
        return items;
    }

    private static List<HardwareInfoItem> BuildDisplayItems(List<DisplayDetail> displays)
    {
        var items = new List<HardwareInfoItem>();
        foreach (var disp in displays)
        {
            if (items.Count > 0) items.Add(Separator());
            var nameLabel = disp.IsPrimary ? LocalizationService.L("Hw_Field_PrimaryDisplay", "主显示器") : LocalizationService.L("Hw_Label_Display", "显示器");
            AddIf(items, nameLabel, disp.Name);
            AddIf(items, "分辨率", disp.Resolution);
            AddIf(items, "刷新率", disp.RefreshRate);
            AddIf(items, "尺寸", disp.DiagonalInches);
            AddIf(items, "制造日期", disp.MadeDate);
        }
        return items;
    }

    private static List<HardwareInfoItem> BuildSoundItems(List<SoundDetail> sounds)
    {
        var items = new List<HardwareInfoItem>();
        foreach (var snd in sounds)
        {
            if (items.Count > 0) items.Add(Separator());
            AddIf(items, "名称", snd.Name);
            AddIf(items, "制造商", snd.Manufacturer);
            AddIf(items, "状态", snd.Status);
        }
        return items;
    }

    private static List<HardwareInfoItem> BuildNetworkItems(List<NetworkDetail> nets)
    {
        var items = new List<HardwareInfoItem>();
        foreach (var net in nets)
        {
            if (items.Count > 0) items.Add(Separator());
            AddIf(items, "名称", net.Name);
            AddIf(items, "制造商", net.Manufacturer);
            AddIf(items, "状态", net.ConnectionStatus);
            AddIf(items, "MAC 地址", net.MacAddress);
            AddIf(items, "速度", net.Speed);
            AddIf(items, "类型", net.AdapterType);
            AddIf(items, "IP 地址", net.IpAddresses);
            AddIf(items, "默认网关", net.Gateway);
        }
        return items;
    }

    private static List<HardwareInfoItem> BuildBatteryItems(BatteryDetail? battery)
    {
        var items = new List<HardwareInfoItem>();
        if (battery == null) return items;
        AddIf(items, "名称", battery.Name);
        AddIf(items, "制造商", battery.Manufacturer);
        AddIf(items, "状态", battery.Status);
        AddIf(items, "化学类型", battery.Chemistry);
        AddIf(items, "设计容量", battery.DesignCapacity);
        AddIf(items, "满充容量", battery.FullChargeCapacity);
        AddIf(items, "电池健康", battery.Health);
        AddIf(items, "循环次数", battery.CycleCount);
        AddIf(items, "序列号", battery.SerialNumber);
        return items;
    }

    private static List<HardwareInfoItem> BuildSecurityItems(SecurityDetail? security)
    {
        var items = new List<HardwareInfoItem>();
        if (security == null) return items;
        AddIf(items, "TPM 版本", security.TpmVersion);
        AddIf(items, "TPM 厂商", security.TpmManufacturer);
        AddIf(items, "TPM 状态", security.TpmState);
        AddIf(items, "安全启动", security.SecureBoot);
        AddIf(items, "虚拟化安全", security.Vbs);
        AddIf(items, "内存完整性", security.Hvci);
        AddIf(items, "虚拟机监控程序", security.Hypervisor);
        return items;
    }

    private static List<HardwareInfoItem> BuildUsbItems(UsbDetail? usb)
    {
        var items = new List<HardwareInfoItem>();
        if (usb == null) return items;
        foreach (var controller in usb.Controllers)
            AddIf(items, "控制器", controller);
        foreach (var device in usb.Devices)
            AddIf(items, "设备", device);
        if (usb.DeviceTotalCount > usb.Devices.Count)
            items.Add(Item("设备总数", string.Format(LocalizationService.L("HwDetail_UsbTruncated", "{0} 个（仅显示前 {1} 个）"), usb.DeviceTotalCount, usb.Devices.Count)));
        return items;
    }

    private static List<HardwareInfoItem> BuildDeviceIssueItems(List<DeviceIssueDetail> devices)
    {
        var items = new List<HardwareInfoItem>();
        foreach (var device in devices)
            AddIf(items, device.Name ?? LocalizationService.L("Hw_Unknown", "未知"), device.Status);
        return items;
    }

    private static HardwareInfoItem Item(string label, string? value)
    {
        return new HardwareInfoItem
        {
            Label = label,
            Value = string.IsNullOrWhiteSpace(value) ? LocalizationService.L("Hw_Unknown", "未知") : value
        };
    }

    /// <summary>多设备分段之间的空行分隔（空标签空值，不参与复制/导出）。</summary>
    private static HardwareInfoItem Separator() => new() { Label = "", Value = "" };

    private static string JoinValues(params string?[] values)
    {
        return string.Join(" | ", values.Where(v => !string.IsNullOrWhiteSpace(v)));
    }

    private static string? FirstUseful(params string?[] values)
    {
        return values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));
    }

    #endregion

    #region 复制 / 状态栏

    private void DetailItem_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe) return;
        if (fe.DataContext is not HardwareInfoItem item) return;
        if (string.IsNullOrWhiteSpace(item.Value)) return;
        TryCopyToClipboard(item.Value);
    }

    /// <summary>
    /// 复制并给出反馈。剪贴板被占用是瞬时状态，失败重试由 ClipboardService 负责；
    /// 这里只负责把最终结果翻译成状态栏提示（失败也不抛，避免闪退）。
    /// </summary>
    private bool TryCopyToClipboard(string text)
    {
        var result = ClipboardService.TrySetText(text);
        if (result.Success)
        {
            ShowCopyToast(text);
            return true;
        }

        ShowStatusBar(
            LocalizationService.L("Hw_CopyFailed", "复制失败"),
            LocalizationService.L("Hw_CopyBusyRetry", "复制失败：剪贴板被其他程序占用，请稍后重试"),
            InfoBarSeverity.Warning);
        return false;
    }

    private void ShowCopyToast(string text)
    {
        StatusBar.Title = LocalizationService.L("Hw_Copied", "已复制");
        StatusBar.Message = text.Length > 80 ? text[..80] + "…" : text;
        StatusBar.Severity = InfoBarSeverity.Success;
        StatusBar.IsOpen = true;

        RestartStatusBarTimer(TimeSpan.FromSeconds(5));
    }

    private DispatcherTimer? _statusBarTimer;

    private void RestartStatusBarTimer(TimeSpan interval)
    {
        _statusBarTimer?.Stop();
        _statusBarTimer = new DispatcherTimer { Interval = interval };
        _statusBarTimer.Tick += (s, e) =>
        {
            StatusBar.IsOpen = false;
            ((DispatcherTimer)s!).Stop();
        };
        _statusBarTimer.Start();
    }

    private void SetLoading(bool isLoading)
    {
        LoadingRing.IsActive = isLoading;
        LoadingRing.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ShowStatusBar(string title, string message, InfoBarSeverity severity)
    {
        StatusBar.Title = title;
        StatusBar.Message = message;
        StatusBar.Severity = severity;
        StatusBar.IsOpen = true;

        RestartStatusBarTimer(TimeSpan.FromSeconds(5));
    }

    private static string HtmlEscape(string? s) => string.IsNullOrEmpty(s) ? "" : s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");

    #endregion
}

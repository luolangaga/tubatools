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
using Microsoft.UI.Xaml.Media.Animation;
using SkiaSharp;
using TubaWinUi3.Models;
using TubaWinUi3.Services;
using Windows.ApplicationModel.DataTransfer;

namespace TubaWinUi3.Pages;

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
    }

    private void HardwareDetailPage_Loaded(object sender, RoutedEventArgs e)
    {
        _pageAlive = true;
        _ = LoadDetailAsync();
        InitRealtimeMonitor();
    }

    private void HardwareDetailPage_Unloaded(object sender, RoutedEventArgs e)
    {
        _pageAlive = false;
        StopRealtimeMonitor();
    }

    private void InitRealtimeMonitor()
    {
        _isLaptop = HardwareInfoService.IsLaptop();
        BatteryCard.Visibility = _isLaptop ? Visibility.Visible : Visibility.Collapsed;

        CpuChart.Series = [new LineSeries<double>
        {
            Values = _cpuHist,
            Stroke = new SolidColorPaint(new SKColor(76, 110, 245)) { StrokeThickness = 2 },
            Fill = new SolidColorPaint(new SKColor(76, 110, 245, 40)),
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
            Stroke = new SolidColorPaint(new SKColor(121, 80, 242)) { StrokeThickness = 2 },
            Fill = new SolidColorPaint(new SKColor(121, 80, 242, 40)),
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
            Stroke = new SolidColorPaint(new SKColor(21, 170, 191)) { StrokeThickness = 2 },
            Fill = new SolidColorPaint(new SKColor(21, 170, 191, 40)),
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
                Stroke = new SolidColorPaint(new SKColor(76, 110, 245)) { StrokeThickness = 1.5f },
                Fill = new SolidColorPaint(new SKColor(76, 110, 245, 30)),
                GeometrySize = 0,
                LineSmoothness = 0.4
            },
            new LineSeries<double>
            {
                Values = _diskWriteHist,
                Stroke = new SolidColorPaint(new SKColor(121, 80, 242)) { StrokeThickness = 1.5f },
                Fill = new SolidColorPaint(new SKColor(121, 80, 242, 30)),
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
                Stroke = new SolidColorPaint(new SKColor(64, 192, 87)) { StrokeThickness = 2 },
                Fill = new SolidColorPaint(new SKColor(64, 192, 87, 40)),
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

    private static string Fmt(float v, string unit) => v >= 0 ? $"{v:0}{unit}" : "--";

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

    private static string BuildHtml(HardwareDetailData data)
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
        sb.AppendLine("body{font-family:-apple-system,\"Microsoft YaHei\",\"Segoe UI\",sans-serif;background:#f5f5f5;color:#1a1a1a;padding:24px}");
        sb.AppendLine(".container{max-width:1100px;margin:0 auto}");
        sb.AppendLine("h1{font-size:22px;font-weight:600;margin-bottom:4px}");
        sb.AppendLine(".sub{font-size:13px;color:#888;margin-bottom:20px}");
        sb.AppendLine(".grid{display:grid;grid-template-columns:repeat(3,1fr);gap:12px}");
        sb.AppendLine("@media(max-width:800px){.grid{grid-template-columns:repeat(2,1fr)}}");
        sb.AppendLine("@media(max-width:520px){.grid{grid-template-columns:1fr}}");
        sb.AppendLine(".card{background:#fff;border:1px solid #e5e5e5;border-radius:8px;padding:12px 14px}");
        sb.AppendLine(".card-title{font-size:13px;font-weight:600;color:#555;margin-bottom:6px;padding-bottom:4px;border-bottom:1px solid #f0f0f0}");
        sb.AppendLine(".row{display:flex;padding:3px 0;font-size:12px;line-height:1.6}");
        sb.AppendLine(".row-label{color:#888;min-width:90px;flex-shrink:0}");
        sb.AppendLine(".row-sep{width:1px;background:#e0e0e0;margin:2px 8px;flex-shrink:0}");
        sb.AppendLine(".row-value{color:#1a1a1a;font-weight:500;word-break:break-all}");
        sb.AppendLine(".footer{margin-top:20px;font-size:11px;color:#bbb;text-align:center}");
        sb.AppendLine("</style>");
        sb.AppendLine("</head>");
        sb.AppendLine("<body>");
        sb.AppendLine("<div class=\"container\">");
        sb.AppendLine($"<h1>{LocalizationService.L("HwDetail_HtmlTitle", "硬件详细信息")}</h1>");
        sb.AppendLine($"<div class=\"sub\">{string.Format(LocalizationService.L("HwDetail_HtmlSubtitle", "{0} · 导出时间 {1}"), LocalizationService.L("App_Title", "图吧工具箱CE"), DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))}</div>");
        sb.AppendLine("<div class=\"grid\">");

        AppendSection(sb, "处理器", BuildCpuItems(data.Cpu));
        AppendSection(sb, "主板", BuildBoardItems(data.Motherboard));
        AppendSection(sb, "内存", BuildMemoryItems(data.Memory));
        AppendSection(sb, "显卡", BuildGpuItems(data.Gpus));
        if (data.Npu != null)
            AppendSection(sb, "NPU", BuildNpuItems(data.Npu));
        AppendSection(sb, "硬盘", BuildDiskItems(data.Disks));
        AppendSection(sb, "显示器", BuildDisplayItems(data.Displays));
        AppendSection(sb, "声卡", BuildSoundItems(data.SoundDevices));
        AppendSection(sb, "网卡", BuildNetworkItems(data.NetworkAdapters));

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

    private void ApplyData(HardwareDetailData data)
    {
        _lastDetailData = data;
        var sections = new List<DetailSection>();

        // CPU
        var cpuItems = BuildCpuItems(data.Cpu);
        if (cpuItems.Count > 0)
            sections.Add(new DetailSection("处理器", cpuItems, cpuItems.Count));

        // Motherboard
        var boardItems = BuildBoardItems(data.Motherboard);
        if (boardItems.Count > 0)
            sections.Add(new DetailSection("主板", boardItems, boardItems.Count));

        // Memory
        var memItems = BuildMemoryItems(data.Memory);
        if (memItems.Count > 0)
            sections.Add(new DetailSection("内存", memItems, memItems.Count));

        // GPU
        var gpuItems = BuildGpuItems(data.Gpus);
        if (gpuItems.Count > 0)
            sections.Add(new DetailSection("显卡", gpuItems, gpuItems.Count));

        // NPU
        if (data.Npu != null)
        {
            var npuItems = BuildNpuItems(data.Npu);
            if (npuItems.Count > 0)
                sections.Add(new DetailSection("NPU", npuItems, npuItems.Count));
        }

        // Disks
        var diskItems = BuildDiskItems(data.Disks);
        if (diskItems.Count > 0)
            sections.Add(new DetailSection("硬盘", diskItems, diskItems.Count));

        // Displays
        var displayItems = BuildDisplayItems(data.Displays);
        if (displayItems.Count > 0)
            sections.Add(new DetailSection("显示器", displayItems, displayItems.Count));

        // Sound
        var soundItems = BuildSoundItems(data.SoundDevices);
        if (soundItems.Count > 0)
            sections.Add(new DetailSection("声卡", soundItems, soundItems.Count));

        // Network
        var netItems = BuildNetworkItems(data.NetworkAdapters);
        if (netItems.Count > 0)
            sections.Add(new DetailSection("网卡", netItems, netItems.Count));

        // Layout: arrange cards into grid rows
        LayoutCards(sections);

        // CPU-Z badge
        CpuzBadge.Visibility = data.Cpu?.IsVerified == true || data.Motherboard?.IsVerified == true
            ? Visibility.Visible : Visibility.Collapsed;

        _dataLoaded = true;
    }

    private void LayoutCards(List<DetailSection> sections)
    {
        CardsContainer.Children.Clear();
        CardsContainer.ColumnDefinitions.Clear();
        CardsContainer.RowDefinitions.Clear();

        if (sections.Count == 0) return;

        // Decide column count based on total sections and their sizes
        // "weight" ≈ how many rows of items the section has
        // Large sections (CPU, memory, disks) get more space; small ones (sound, network) share a row
        var totalWeight = sections.Sum(s => s.Weight);
        int colCount = totalWeight switch
        {
            <= 8 => 2,
            <= 20 => 3,
            _ => 3
        };

        for (int c = 0; c < colCount; c++)
            CardsContainer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        // Greedy row-packing: fill each row until its total weight exceeds a threshold, then start next row
        var rows = new List<List<(DetailSection Section, int ColSpan)>>();
        var currentRow = new List<(DetailSection Section, int ColSpan)>();
        var rowWeight = 0;
        var maxRowWeight = totalWeight / colCount + 2;

        foreach (var sec in sections)
        {
            // Decide colSpan: large sections span more columns
            int colSpan = sec.Weight >= maxRowWeight ? colCount
                        : sec.Weight >= maxRowWeight / 2 ? Math.Max(1, colCount / 2)
                        : 1;

            if (currentRow.Count > 0 && (rowWeight + sec.Weight > maxRowWeight * 1.2 || currentRow.Sum(r => r.ColSpan) + colSpan > colCount))
            {
                rows.Add(currentRow);
                currentRow = new List<(DetailSection Section, int ColSpan)>();
                rowWeight = 0;
            }

            currentRow.Add((sec, colSpan));
            rowWeight += sec.Weight;
        }

        if (currentRow.Count > 0)
            rows.Add(currentRow);

        // Build Grid rows and place cards
        for (int r = 0; r < rows.Count; r++)
        {
            CardsContainer.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            int col = 0;
            foreach (var (sec, colSpan) in rows[r])
            {
                var card = CreateCard(sec);
                Grid.SetRow(card, r);
                Grid.SetColumn(card, col);
                Grid.SetColumnSpan(card, colSpan);
                CardsContainer.Children.Add(card);
                col += colSpan;
            }
        }
    }

    private Border CreateCard(DetailSection section)
    {
        var card = new Border
        {
            Style = (Style)Resources["SectionCardStyle"],
            Child = new StackPanel { Spacing = 1 }
        };

        var panel = (StackPanel)card.Child;
        panel.Children.Add(new TextBlock
        {
            Text = section.DisplayTitle,
            Style = (Style)Resources["SectionTitleStyle"]
        });

        var repeater = new ItemsRepeater
        {
            ItemsSource = section.Items
        };

        var template = (DataTemplate)Resources["DetailRowTemplate"];
        repeater.ItemTemplate = template;

        panel.Children.Add(repeater);
        return card;
    }

    #region Item Builders

    private static List<HardwareInfoItem> BuildCpuItems(CpuDetail? cpu)
    {
        var items = new List<HardwareInfoItem>();
        if (cpu == null) return items;
        items.Add(Item("名称", cpu.Name));
        if (!string.IsNullOrWhiteSpace(cpu.CodeName)) items.Add(Item("代号", cpu.CodeName));
        if (!string.IsNullOrWhiteSpace(cpu.Package)) items.Add(Item("封装", cpu.Package));
        if (cpu.Cores > 0) items.Add(Item("核心数", $"{cpu.Cores}"));
        if (cpu.Threads > 0) items.Add(Item("线程数", $"{cpu.Threads}"));
        if (!string.IsNullOrWhiteSpace(cpu.MaxClockSpeed)) items.Add(Item("最大频率", cpu.MaxClockSpeed));
        if (!string.IsNullOrWhiteSpace(cpu.CurrentClockSpeed)) items.Add(Item("当前频率", cpu.CurrentClockSpeed));
        if (!string.IsNullOrWhiteSpace(cpu.L2CacheSize)) items.Add(Item("L2 缓存", cpu.L2CacheSize));
        if (!string.IsNullOrWhiteSpace(cpu.L3CacheSize)) items.Add(Item("L3 缓存", cpu.L3CacheSize));
        if (!string.IsNullOrWhiteSpace(cpu.ExtClock)) items.Add(Item("外频", cpu.ExtClock));
        if (!string.IsNullOrWhiteSpace(cpu.Architecture)) items.Add(Item("架构", cpu.Architecture));
        if (!string.IsNullOrWhiteSpace(cpu.Manufacturer)) items.Add(Item("制造商", cpu.Manufacturer));
        if (!string.IsNullOrWhiteSpace(cpu.ProcessorId)) items.Add(Item("ProcessorID", cpu.ProcessorId));
        return items;
    }

    private static List<HardwareInfoItem> BuildBoardItems(MotherboardDetail? mb)
    {
        var items = new List<HardwareInfoItem>();
        if (mb == null) return items;
        items.Add(Item("制造商", mb.Manufacturer));
        items.Add(Item("型号", mb.Model));
        if (!string.IsNullOrWhiteSpace(mb.Version)) items.Add(Item("版本", mb.Version));
        if (!string.IsNullOrWhiteSpace(mb.Chipset)) items.Add(Item("芯片组", mb.Chipset));
        items.Add(Item("BIOS 品牌", mb.BiosBrand));
        items.Add(Item("BIOS 版本", mb.BiosVersion));
        if (!string.IsNullOrWhiteSpace(mb.BiosDate)) items.Add(Item("BIOS 日期", mb.BiosDate));
        return items;
    }

    private static List<HardwareInfoItem> BuildMemoryItems(MemoryDetail? mem)
    {
        var items = new List<HardwareInfoItem>();
        if (mem == null) return items;
        items.Add(Item("总容量", mem.TotalCapacity));
        if (!string.IsNullOrWhiteSpace(mem.MemoryType)) items.Add(Item("类型", mem.MemoryType));
        if (!string.IsNullOrWhiteSpace(mem.ChannelMode)) items.Add(Item("通道模式", mem.ChannelMode));
        items.Add(Item("插槽", string.Format(LocalizationService.L("HwDetail_SlotsUsed", "{0}/{1} 已使用"), mem.UsedSlots, mem.TotalSlots)));
        foreach (var mod in mem.Modules)
        {
            var isSlot = mod.Capacity == "空";
            var label = isSlot ? $"  └ {mod.Designation}" : $"  ├ {mod.Designation}";
            var value = isSlot ? LocalizationService.L("HwDetail_EmptySlot", "空") : JoinValues(mod.Capacity, mod.Speed, mod.Manufacturer, mod.PartNumber);
            items.Add(Item(label, value));
        }
        return items;
    }

    private static List<HardwareInfoItem> BuildGpuItems(List<GpuDetail> gpus)
    {
        var items = new List<HardwareInfoItem>();
        foreach (var gpu in gpus)
        {
            if (items.Count > 0) items.Add(Item("", ""));
            items.Add(Item("名称", gpu.Name));
            if (!string.IsNullOrWhiteSpace(gpu.GpuCode)) items.Add(Item("GPU 代码", gpu.GpuCode));
            if (!string.IsNullOrWhiteSpace(gpu.AdapterRAM)) items.Add(Item("显存", gpu.AdapterRAM));
            if (!string.IsNullOrWhiteSpace(gpu.MemorySize)) items.Add(Item("显存", gpu.MemorySize));
            if (!string.IsNullOrWhiteSpace(gpu.MemoryType)) items.Add(Item("显存类型", gpu.MemoryType));
            if (!string.IsNullOrWhiteSpace(gpu.MemoryBus)) items.Add(Item("显存位宽", gpu.MemoryBus));
            if (!string.IsNullOrWhiteSpace(gpu.DriverVersion)) items.Add(Item("驱动版本", gpu.DriverVersion));
            if (!string.IsNullOrWhiteSpace(gpu.DriverDate)) items.Add(Item("驱动日期", gpu.DriverDate));
            if (!string.IsNullOrWhiteSpace(gpu.VideoProcessor)) items.Add(Item("视频处理器", gpu.VideoProcessor));
            if (!string.IsNullOrWhiteSpace(gpu.CurrentResolution)) items.Add(Item("当前分辨率", gpu.CurrentResolution));
            if (!string.IsNullOrWhiteSpace(gpu.CurrentRefreshRate)) items.Add(Item("刷新率", gpu.CurrentRefreshRate));
        }
        return items;
    }

    private static List<HardwareInfoItem> BuildNpuItems(NpuDetail npu)
    {
        var items = new List<HardwareInfoItem>();
        if (!string.IsNullOrWhiteSpace(npu.Name)) items.Add(Item("名称", npu.Name));
        if (!string.IsNullOrWhiteSpace(npu.ComputeCapability)) items.Add(Item("算力", npu.ComputeCapability));
        if (!string.IsNullOrWhiteSpace(npu.Manufacturer)) items.Add(Item("制造商", npu.Manufacturer));
        if (!string.IsNullOrWhiteSpace(npu.DriverVersion)) items.Add(Item("驱动版本", npu.DriverVersion));
        if (!string.IsNullOrWhiteSpace(npu.DriverDate)) items.Add(Item("驱动日期", npu.DriverDate));
        return items;
    }

    private static List<HardwareInfoItem> BuildDiskItems(List<DiskDetail> disks)
    {
        var items = new List<HardwareInfoItem>();
        foreach (var disk in disks)
        {
            if (items.Count > 0) items.Add(Item("", ""));
            items.Add(Item("型号", disk.Model));
            if (!string.IsNullOrWhiteSpace(disk.MediaType)) items.Add(Item("类型", disk.MediaType));
            if (!string.IsNullOrWhiteSpace(disk.Size)) items.Add(Item("容量", disk.Size));
            if (disk.Temperature.HasValue) items.Add(Item("温度", $"{disk.Temperature.Value:0}°C"));
            if (!string.IsNullOrWhiteSpace(disk.InterfaceType)) items.Add(Item("接口", disk.InterfaceType));
            if (!string.IsNullOrWhiteSpace(disk.FirmwareRevision)) items.Add(Item("固件版本", disk.FirmwareRevision));
            if (!string.IsNullOrWhiteSpace(disk.SerialNumber)) items.Add(Item("序列号", disk.SerialNumber));
            foreach (var part in disk.Partitions)
            {
                var partLabel = $"  ├ {part.Name}";
                var partValue = JoinValues(part.DriveLetter, part.FileSystem, part.Size, part.FreeSpace != null ? string.Format(LocalizationService.L("HwDetail_FreeSpace", "可用 {0}"), part.FreeSpace) : null);
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
            if (items.Count > 0) items.Add(Item("", ""));
            var nameLabel = disp.IsPrimary ? LocalizationService.L("Hw_Field_PrimaryDisplay", "主显示器") : LocalizationService.L("Hw_Label_Display", "显示器");
            items.Add(Item(nameLabel, disp.Name));
            if (!string.IsNullOrWhiteSpace(disp.Resolution)) items.Add(Item("分辨率", disp.Resolution));
            if (!string.IsNullOrWhiteSpace(disp.RefreshRate)) items.Add(Item("刷新率", disp.RefreshRate));
            if (!string.IsNullOrWhiteSpace(disp.DiagonalInches)) items.Add(Item("尺寸", disp.DiagonalInches));
        }
        return items;
    }

    private static List<HardwareInfoItem> BuildSoundItems(List<SoundDetail> sounds)
    {
        var items = new List<HardwareInfoItem>();
        foreach (var snd in sounds)
        {
            if (items.Count > 0) items.Add(Item("", ""));
            items.Add(Item("名称", snd.Name));
            if (!string.IsNullOrWhiteSpace(snd.Manufacturer)) items.Add(Item("制造商", snd.Manufacturer));
            if (!string.IsNullOrWhiteSpace(snd.Status)) items.Add(Item("状态", snd.Status));
        }
        return items;
    }

    private static List<HardwareInfoItem> BuildNetworkItems(List<NetworkDetail> nets)
    {
        var items = new List<HardwareInfoItem>();
        foreach (var net in nets)
        {
            if (items.Count > 0) items.Add(Item("", ""));
            items.Add(Item("名称", net.Name));
            if (!string.IsNullOrWhiteSpace(net.Manufacturer)) items.Add(Item("制造商", net.Manufacturer));
            if (!string.IsNullOrWhiteSpace(net.MacAddress)) items.Add(Item("MAC 地址", net.MacAddress));
            if (!string.IsNullOrWhiteSpace(net.Speed)) items.Add(Item("速度", net.Speed));
            if (!string.IsNullOrWhiteSpace(net.AdapterType)) items.Add(Item("类型", net.AdapterType));
        }
        return items;
    }

    #endregion

    private static HardwareInfoItem Item(string label, string? value)
    {
        return new HardwareInfoItem
        {
            Label = label,
            Value = string.IsNullOrWhiteSpace(value) ? LocalizationService.L("Hw_Unknown", "未知") : value
        };
    }

    private static string JoinValues(params string?[] values)
    {
        return string.Join(" | ", values.Where(v => !string.IsNullOrWhiteSpace(v)));
    }

    private void DetailItem_Tapped(object sender, TappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe) return;
        if (fe.DataContext is not HardwareInfoItem item) return;
        if (item.Value == LocalizationService.L("Hw_Unknown", "未知") && string.IsNullOrWhiteSpace(item.Label)) return;
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

    private DispatcherTimer? _statusBarTimer;

    private void ShowCopyToast(string text)
    {
        StatusBar.Title = LocalizationService.L("Hw_Copied", "已复制");
        StatusBar.Message = text.Length > 80 ? text[..80] + "…" : text;
        StatusBar.Severity = InfoBarSeverity.Success;
        StatusBar.IsOpen = true;

        _statusBarTimer?.Stop();
        _statusBarTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
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

    private DispatcherTimer? _statusBarAutoCloseTimer;

    private void ShowStatusBar(string title, string message, InfoBarSeverity severity)
    {
        StatusBar.Title = title;
        StatusBar.Message = message;
        StatusBar.Severity = severity;
        StatusBar.IsOpen = true;

        _statusBarAutoCloseTimer?.Stop();
        _statusBarAutoCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _statusBarAutoCloseTimer.Tick += (s, e) =>
        {
            StatusBar.IsOpen = false;
            ((DispatcherTimer)s!).Stop();
        };
        _statusBarAutoCloseTimer.Start();
    }

    private sealed class DetailSection
    {
        public string Title { get; }

        /// <summary>显示用标题（Title 是数据键，显示层按当前语言翻译）。</summary>
        public string DisplayTitle => LocalizationService.TranslateHardwareLabel(Title);
        public List<HardwareInfoItem> Items { get; }
        public int Weight { get; }

        public DetailSection(string title, List<HardwareInfoItem> items, int weight)
        {
            Title = title;
            Items = items;
            Weight = weight;
        }
    }

    private static void AppendSection(StringBuilder sb, string title, List<HardwareInfoItem> items)
    {
        if (items.Count == 0) return;
        sb.AppendLine($"<div class=\"card\"><div class=\"card-title\">{HtmlEscape(LocalizationService.TranslateHardwareLabel(title))}</div>");
        foreach (var item in items)
        {
            if (string.IsNullOrEmpty(item.Label) && item.Value == LocalizationService.L("Hw_Unknown", "未知")) continue;
            sb.AppendLine($"<div class=\"row\"><span class=\"row-label\">{HtmlEscape(LocalizationService.TranslateHardwareLabel(item.Label))}</span><span class=\"row-sep\"></span><span class=\"row-value\">{HtmlEscape(item.Value)}</span></div>");
        }
        sb.AppendLine("</div>");
    }

    private static string HtmlEscape(string? s) => string.IsNullOrEmpty(s) ? "" : s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}

using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using TubaWinUi3.Models;
using TubaWinUi3.Services;
using Windows.ApplicationModel.DataTransfer;

namespace TubaWinUi3.Pages;

public sealed partial class HardwareDetailPage : Page
{
    private bool _dataLoaded;

    public HardwareDetailPage()
    {
        InitializeComponent();
        Loaded += HardwareDetailPage_Loaded;
    }

    private void HardwareDetailPage_Loaded(object sender, RoutedEventArgs e)
    {
        ApplyBackground();
        _ = LoadDetailAsync();
    }

    private void ApplyBackground()
    {
        var bmp = BackgroundService.LoadBackgroundImage();
        if (bmp is not null)
        {
            BackgroundImg.Source = bmp;
            BackgroundImg.Opacity = BackgroundService.GetBackgroundOpacity();
            BackgroundImg.Visibility = Visibility.Visible;
        }
        else
        {
            BackgroundImg.Source = null;
            BackgroundImg.Visibility = Visibility.Collapsed;
        }
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        _ = LoadDetailAsync(forceRefresh: true);
    }

    private void BackButton_Click(object sender, RoutedEventArgs e)
    {
        if (Frame.CanGoBack)
            Frame.GoBack();
        else
            Frame.Navigate(typeof(HardwarePage));
    }

    private async Task LoadDetailAsync(bool forceRefresh = false)
    {
        if (_dataLoaded && !forceRefresh) return;
        SetLoading(true);

        try
        {
            var data = await HardwareInfoService.LoadDetailAsync(forceRefresh);

            var useCpuz = AppSettings.GetBool("UseCpuzDataSource", false);
            if (useCpuz)
            {
                var cpuzInfo = CpuzInfoService.CachedInfo;
                if (cpuzInfo == null)
                {
                    try
                    {
                        cpuzInfo = await CpuzInfoService.FetchAsync(timeoutMs: 30000);
                    }
                    catch { }
                }

                if (cpuzInfo != null)
                {
                    data = HardwareInfoService.ApplyCpuzDetailOverride(data, cpuzInfo);
                }
            }

            ApplyData(data);
            StatusBar.IsOpen = false;
        }
        catch (Exception ex)
        {
            StatusBar.Title = "硬件信息读取失败";
            StatusBar.Message = ex.Message;
            StatusBar.Severity = InfoBarSeverity.Error;
            StatusBar.IsOpen = true;
        }
        finally
        {
            SetLoading(false);
        }
    }

    private void ApplyData(HardwareDetailData data)
    {
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
            Text = section.Title,
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
        items.Add(Item("插槽", $"{mem.UsedSlots}/{mem.TotalSlots} 已使用"));
        foreach (var mod in mem.Modules)
        {
            var isSlot = mod.Capacity == "空";
            var label = isSlot ? $"  └ {mod.Designation}" : $"  ├ {mod.Designation}";
            var value = isSlot ? "空" : JoinValues(mod.Capacity, mod.Speed, mod.Manufacturer, mod.PartNumber);
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
            // 优先使用 CPU-Z 的显存信息，因为 WMI 的 AdapterRAM 对某些笔记本显卡不准确
            // 例如 RTX 2060 移动版 WMI 返回 4GB 而实际是 6GB
            if (!string.IsNullOrWhiteSpace(gpu.MemorySize))
            {
                items.Add(Item("显存", gpu.MemorySize));
            }
            else if (!string.IsNullOrWhiteSpace(gpu.AdapterRAM))
            {
                items.Add(Item("显存", gpu.AdapterRAM));
            }
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

    private static List<HardwareInfoItem> BuildDiskItems(List<DiskDetail> disks)
    {
        var items = new List<HardwareInfoItem>();
        foreach (var disk in disks)
        {
            if (items.Count > 0) items.Add(Item("", ""));
            items.Add(Item("型号", disk.Model));
            if (!string.IsNullOrWhiteSpace(disk.MediaType)) items.Add(Item("类型", disk.MediaType));
            if (!string.IsNullOrWhiteSpace(disk.Size)) items.Add(Item("容量", disk.Size));
            if (!string.IsNullOrWhiteSpace(disk.InterfaceType)) items.Add(Item("接口", disk.InterfaceType));
            if (!string.IsNullOrWhiteSpace(disk.FirmwareRevision)) items.Add(Item("固件版本", disk.FirmwareRevision));
            if (!string.IsNullOrWhiteSpace(disk.SerialNumber)) items.Add(Item("序列号", disk.SerialNumber));
            foreach (var part in disk.Partitions)
            {
                var partLabel = $"  ├ {part.Name}";
                var partValue = JoinValues(part.DriveLetter, part.FileSystem, part.Size, part.FreeSpace != null ? $"可用 {part.FreeSpace}" : null);
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
            var nameLabel = disp.IsPrimary ? "主显示器" : "显示器";
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
            Value = string.IsNullOrWhiteSpace(value) ? "未知" : value
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
        if (item.Value == "未知" && string.IsNullOrWhiteSpace(item.Label)) return;
        CopyToClipboard(item.Value);
    }

    private void CopyToClipboard(string text)
    {
        var dp = new DataPackage();
        dp.SetText(text);
        Clipboard.SetContent(dp);
        ShowCopyToast(text);
    }

    private void ShowCopyToast(string text)
    {
        StatusBar.Title = "已复制";
        StatusBar.Message = text.Length > 80 ? text[..80] + "…" : text;
        StatusBar.Severity = InfoBarSeverity.Success;
        StatusBar.IsOpen = true;
    }

    private void SetLoading(bool isLoading)
    {
        LoadingRing.IsActive = isLoading;
        LoadingRing.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
    }

    private sealed class DetailSection
    {
        public string Title { get; }
        public List<HardwareInfoItem> Items { get; }
        public int Weight { get; }

        public DetailSection(string title, List<HardwareInfoItem> items, int weight)
        {
            Title = title;
            Items = items;
            Weight = weight;
        }
    }
}

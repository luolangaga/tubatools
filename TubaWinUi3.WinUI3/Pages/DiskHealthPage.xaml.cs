using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Services;
using Windows.UI;
using UIElement = Microsoft.UI.Xaml.UIElement;

namespace TubaWinUi3.Pages;

/// <summary>磁盘卡片视图模型（环形图仪表盘数据源）。</summary>
public sealed class DiskCardVm
{
    public required string Model { get; init; }
    public required string SizeText { get; init; }
    public required double HealthRingValue { get; init; }
    public required SolidColorBrush HealthRingBrush { get; init; }
    /// <summary>健康度大数字（-- 表示未知）。</summary>
    public required string HealthText { get; init; }
    public required SolidColorBrush HealthTextBrush { get; init; }
    public required IReadOnlyList<DiskTagVm> Tags { get; init; }
    public required double TempRingValue { get; init; }
    public required string TempText { get; init; }
    public required SolidColorBrush TempRingBrush { get; init; }
    public required IReadOnlyList<PartitionVm> Partitions { get; init; }
    public required string PowerOnHoursText { get; init; }
    public required string PowerOnCountText { get; init; }
    public required string DataReadText { get; init; }
    public required string DataWrittenText { get; init; }
    public required double DataReadGb { get; init; }
    public required double DataWriteGb { get; init; }
    public required double ReadWriteMax { get; init; }
    public required SolidColorBrush ReadBarBrush { get; init; }
    public required SolidColorBrush WriteBarBrush { get; init; }
    public required string OperationalText { get; init; }
    public required SolidColorBrush OperationalBrush { get; init; }
    /// <summary>该盘读取失败时的错误信息（展示在卡片上；空字符串 = 读取正常）。</summary>
    public string ErrorDetail { get; init; } = "";

    /// <summary>错误信息可见性：仅读取失败的盘展示错误行。</summary>
    public Visibility ErrorDetailVisibility => ErrorDetail.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
}

/// <summary>小圆角提示框（标签）。</summary>
public sealed class DiskTagVm
{
    public required string Text { get; init; }
    public required SolidColorBrush Background { get; init; }
    public required SolidColorBrush Foreground { get; init; }
    public string Glyph { get; init; } = "";
    public Visibility GlyphVisibility => Glyph.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
}

/// <summary>分区视图模型（占用环图 + Flyout 确认优化）。</summary>
public sealed class PartitionVm
{
    public PartitionVm Self => this;
    public required string DriveLetter { get; init; }
    public required string Filesystem { get; init; }
    public required string UsageText { get; init; }
    public required string PercentText { get; init; }
    public required double UsagePercent { get; init; }
    public required SolidColorBrush UsageRingBrush { get; init; }
    public required uint Index { get; init; }
    public required string InterfaceType { get; init; }
    public required string Model { get; init; }
    public required bool IsSsd { get; init; }
    public string OptimizeText => IsSsd ? LocalizationService.L("DiskHealth_OptimizeTrim", "优化/TRIM") : LocalizationService.L("DiskHealth_Defragment", "整理碎片");
    public string OptimizeGlyph => IsSsd ? "\uE8D9" : "\uE90F";
    public string FlyoutTitle => string.Format(LocalizationService.L("DiskHealth_FlyoutTitle", "{0} — {1}"), DriveLetter, IsSsd ? LocalizationService.L("DiskHealth_TrimConfirm", "TRIM 优化确认") : LocalizationService.L("DiskHealth_DefragConfirm", "碎片整理确认"));
    public string FlyoutDesc => IsSsd
        ? string.Format(LocalizationService.L("DiskHealth_FlyoutDescTrim", "将对 {0} 执行固态硬盘 TRIM 优化，通常几秒内完成，可立即恢复磁盘写入性能。"), DriveLetter)
        : LocalizationService.L("DiskHealth_FlyoutDescDefrag", "将在后台以低优先级启动碎片整理，可能需要数十分钟到数小时，期间可继续正常使用电脑。");
}

/// <summary>
/// 磁盘健康仪表盘：品牌紫渐变面板 + 环形图（健康度/温度/分区占用）+ 分段容量条形图 + 读写对比条，
/// 分区优化按钮带 Flyout 二次确认。逻辑见 DiskHealthService / DiskSmartReader。
/// </summary>
public sealed partial class DiskHealthPage : Page, ILocalizablePage
{
    // 语义状态色（跟随系统主题，经 ThemeColors 取官方 SystemFillColor* 语义色）
    private static Color SuccessGreen => ThemeColors.AccentGreen;
    private static Color CautionAmber => ThemeColors.AccentOrange;
    private static Color CriticalRed => ThemeColors.AccentRed;
    private static Color NeutralGray => ThemeColors.Neutral;

    /// <summary>分区配色（系统图表系列色，按全局分区顺序轮转，环图与容量条形图共用同一色）。</summary>
    private static Color[] PartitionPalette =>
    [
        ThemeColors.Series1,
        ThemeColors.Series2,
        ThemeColors.Series3,
        ThemeColors.Series4,
    ];

    private CancellationTokenSource? _cts;
    private DiskHealthResponse? _lastResponse;
    private bool _loading;
    private readonly HashSet<string> _optimizingDriveLetters = new();

    public IReadOnlyList<DiskCardVm> DiskCards { get; private set; } = [];

    public DiskHealthPage()
    {
        InitializeComponent();
        // 代码构建的画刷不会随主题自动刷新，切换主题后按缓存数据重渲染
        ActualThemeChanged += (_, _) =>
        {
            if (_lastResponse is not null)
                Render(_lastResponse);
        };
    }

    // ───────────────────────────── 初始化 / 清理 ─────────────────────────────

    private void DiskHealthPage_Loaded(object sender, RoutedEventArgs e)
    {
        _cts = new CancellationTokenSource();
        _ = ReloadAsync();
    }

    private void DiskHealthPage_Unloaded(object sender, RoutedEventArgs e)
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    /// <summary>语言切换后按当前语言重渲染（卡片文本由代码生成；表头等 Uid 控件自动更新）。</summary>
    public void ApplyLocalization()
    {
        if (_lastResponse is not null)
            Render(_lastResponse);
    }
    private static SolidColorBrush Brush(Color color) => new(color);

    // ───────────────────────────── 加载 / 渲染 ─────────────────────────────

    private async Task ReloadAsync()
    {
        if (_loading)
            return;
        _loading = true;
        SetLoadingUi(true);
        ErrorBar.IsOpen = false;
        SuccessBar.IsOpen = false;
        RefreshButton.IsEnabled = false;
        try
        {
            var response = await DiskHealthService.GetHealthAsync();
            if (_cts is null || _cts.IsCancellationRequested)
                return;
            _lastResponse = response;
            Render(response);
        }
        catch (Exception ex)
        {
            ErrorText.Text = ex.Message;
            LoadingPanel.Visibility = Visibility.Collapsed;
            ErrorPanel.Visibility = Visibility.Visible;
            ContentPanel.Visibility = Visibility.Collapsed;
        }
        finally
        {
            _loading = false;
            RefreshButton.IsEnabled = true;
        }
    }

    private void SetLoadingUi(bool loading)
    {
        LoadingPanel.Visibility = loading ? Visibility.Visible : Visibility.Collapsed;
        ErrorPanel.Visibility = Visibility.Collapsed;
        ContentPanel.Visibility = loading ? Visibility.Collapsed : Visibility.Visible;
    }

    private void Render(DiskHealthResponse response)
    {
        TotalValue.Text = response.TotalCount.ToString();
        HealthyValue.Text = response.HealthyCount.ToString();
        WarningValue.Text = response.WarningCount.ToString();
        UnhealthyValue.Text = response.UnhealthyCount.ToString();

        if (response.Disks.Count == 0)
        {
            ErrorText.Text = LocalizationService.L("DiskHealth_NoDisk", "未检测到可用的物理硬盘");
            LoadingPanel.Visibility = Visibility.Collapsed;
            ErrorPanel.Visibility = Visibility.Visible;
            ContentPanel.Visibility = Visibility.Collapsed;
            return;
        }

        // 分区配色：按全局顺序轮转，供环图与总览条形图共用
        var paletteCursor = 0;
        var cards = new List<DiskCardVm>();
        foreach (var disk in response.Disks)
        {
            cards.Add(BuildDiskCard(disk, ref paletteCursor));
        }
        DiskCards = cards;
        BuildOverview(response);

        Bindings.Update();
        LoadingPanel.Visibility = Visibility.Collapsed;
        ErrorPanel.Visibility = Visibility.Collapsed;
        ContentPanel.Visibility = Visibility.Visible;
    }

    private DiskCardVm BuildDiskCard(DiskHealthInfo disk, ref int paletteCursor)
    {
        // 该项读取失败：健康度不判定，卡片以「读取失败」标记并展示错误信息；其余盘不受影响
        var hasError = disk.HasError;
        var healthColor = hasError ? NeutralGray : disk.HealthStatus switch
        {
            "healthy" => SuccessGreen,
            "warning" => CautionAmber,
            "unhealthy" => CriticalRed,
            _ => NeutralGray,
        };

        var tags = new List<DiskTagVm>();
        if (hasError)
            tags.Add(new DiskTagVm
            {
                Text = LocalizationService.L("DiskHealth_ReadFailed", "读取失败"),
                Glyph = "\uEA39",
                Background = Brush(Color.FromArgb(0x1E, CriticalRed.R, CriticalRed.G, CriticalRed.B)),
                Foreground = Brush(CriticalRed),
            });
        else
            tags.Add(BuildHealthTag(disk.HealthStatus));
        tags.AddRange(BuildInfoTags(disk));

        // 温度环：0-100°C 满量程
        var temp = disk.TemperatureC;
        var tempBrush = temp switch
        {
            > 55 => CriticalRed,
            > 45 => CautionAmber,
            _ => SuccessGreen,
        };

        var partitions = new List<PartitionVm>();
        foreach (var p in disk.Partitions)
        {
            partitions.Add(BuildPartition(p, disk, PartitionPalette[paletteCursor % PartitionPalette.Length]));
            paletteCursor++;
        }

        var readGb = disk.DataReadBytes is { } r ? r / 1073741824.0 : 0;
        var writeGb = disk.DataWrittenBytes is { } w ? w / 1073741824.0 : 0;

        return new DiskCardVm
        {
            Model = disk.Model,
            SizeText = disk.SizeGb >= 1024
                ? $"{disk.SizeGb / 1024:F2} TB  ·  {disk.InterfaceType}"
                : $"{disk.SizeGb:F1} GB  ·  {disk.InterfaceType}",
            HealthRingValue = disk.HealthPercent ?? 0,
            HealthRingBrush = Brush(healthColor),
            HealthText = disk.HealthPercent is { } hp ? hp.ToString() : "--",
            HealthTextBrush = Brush(healthColor),
            Tags = tags,
            TempRingValue = temp is { } t ? Math.Clamp(t, 0, 100) : 0,
            TempText = temp is { } t2 ? $"{t2}°C" : "--",
            TempRingBrush = Brush(tempBrush),
            Partitions = partitions,
            PowerOnHoursText = disk.PowerOnHours is { } h ? string.Format(LocalizationService.L("DiskHealth_Hours", "{0} 小时"), FormatCount(h)) : "--",
            PowerOnCountText = disk.PowerOnCount is { } c ? FormatCount(c) : "--",
            DataReadText = disk.DataReadBytes is null ? LocalizationService.L("DiskHealth_ReadEmpty", "读 --") : string.Format(LocalizationService.L("DiskHealth_ReadValue", "读 {0}"), FormatBytes(disk.DataReadBytes.Value)),
            DataWrittenText = disk.DataWrittenBytes is null ? LocalizationService.L("DiskHealth_WriteEmpty", "写 --") : string.Format(LocalizationService.L("DiskHealth_WriteValue", "写 {0}"), FormatBytes(disk.DataWrittenBytes.Value)),
            DataReadGb = readGb,
            DataWriteGb = writeGb,
            ReadWriteMax = Math.Max(Math.Max(readGb, writeGb), 0.1),
            ReadBarBrush = Brush(ThemeColors.Series1),
            WriteBarBrush = Brush(ThemeColors.Series2),
            OperationalText = hasError ? LocalizationService.L("DiskHealth_ReadFailed", "读取失败")
                : disk.OperationalStatus switch
                {
                    "OK" => LocalizationService.L("DiskHealth_StatusOk", "正常"),
                    "Degraded" => LocalizationService.L("DiskHealth_StatusDegraded", "降级"),
                    "Failure" => LocalizationService.L("DiskHealth_StatusFailure", "故障"),
                    _ => LocalizationService.L("DiskHealth_StatusUnknown", "未知"),
                },
            OperationalBrush = Brush(hasError ? CriticalRed : disk.HealthStatus switch
            {
                "healthy" => SuccessGreen,
                "warning" => CautionAmber,
                "unhealthy" => CriticalRed,
                _ => NeutralGray,
            }),
            ErrorDetail = hasError ? TruncateError(disk.Error ?? LocalizationService.L("DiskHealth_UnknownError", "未知错误")) : "",
        };
    }

    /// <summary>错误信息截断为单行可读长度（完整信息在悬停提示中）。</summary>
    private static string TruncateError(string message)
    {
        const int max = 60;
        return message.Length <= max ? message : message[..max] + "…";
    }

    private static DiskTagVm BuildHealthTag(string healthStatus)
    {
        var (text, color, glyph) = healthStatus switch
        {
            "healthy" => (LocalizationService.L("DiskHealth_HealthHealthy", "健康"), SuccessGreen, "\uE73E"),
            "warning" => (LocalizationService.L("DiskHealth_HealthWarning", "警告"), CautionAmber, "\uE7BA"),
            "unhealthy" => (LocalizationService.L("DiskHealth_HealthCritical", "异常"), CriticalRed, "\uEA39"),
            _ => (LocalizationService.L("DiskHealth_HealthUnknown", "未知"), NeutralGray, "\uE946"),
        };
        return new DiskTagVm
        {
            Text = text,
            Glyph = glyph,
            Background = Brush(Color.FromArgb(0x1E, color.R, color.G, color.B)),
            Foreground = Brush(color),
        };
    }

    private static IEnumerable<DiskTagVm> BuildInfoTags(DiskHealthInfo disk)
    {
        var tags = new List<DiskTagVm>();
        var media = disk.MediaType.ToLowerInvariant();
        var mediaText = disk.IsNvme ? "NVMe"
            : media.Contains("hdd") ? "HDD"
            : media.Contains("ssd") || media.Contains("solid state") ? "SSD"
            : LocalizationService.L("DiskHealth_FirmwareDisk", "固件硬盘");
        var mediaColor = disk.IsNvme || media.Contains("ssd") || media.Contains("solid state")
            ? ThemeColors.Series2 : CautionAmber;
        tags.Add(new DiskTagVm
        {
            Text = mediaText,
            Background = Brush(Color.FromArgb(0x1E, mediaColor.R, mediaColor.G, mediaColor.B)),
            Foreground = Brush(mediaColor),
        });
        if (disk.PartitionStyle is "GPT" or "MBR")
            tags.Add(GrayTag(disk.PartitionStyle));
        if (disk.InterfaceType.Length > 0 && disk.InterfaceType != "未知")
            tags.Add(GrayTag(disk.InterfaceType));
        if (disk.IsBootDisk)
            tags.Add(new DiskTagVm
            {
                Text = LocalizationService.L("DiskHealth_SystemDisk", "系统盘"),
                Glyph = "\uE734",
                Background = Brush(Color.FromArgb(0x1E, CautionAmber.R, CautionAmber.G, CautionAmber.B)),
                Foreground = Brush(CautionAmber),
            });
        if (!disk.HasSmart && !disk.HasError)
            tags.Add(GrayTag(LocalizationService.L("DiskHealth_SmartUnreadable", "SMART 不可读")));
        return tags;
    }

    private static DiskTagVm GrayTag(string text) => new()
    {
        Text = text,
        Background = Brush(Color.FromArgb(0x16, NeutralGray.R, NeutralGray.G, NeutralGray.B)),
        Foreground = Brush(NeutralGray),
    };

    private static PartitionVm BuildPartition(PartitionInfo partition, DiskHealthInfo disk, Color color) => new()
    {
        DriveLetter = $"{partition.DriveLetter}:",
        Filesystem = partition.Filesystem,
        UsageText = $"{FormatGb(partition.UsedGb)} / {FormatGb(partition.TotalGb)}",
        PercentText = $"{partition.UsagePercent:0}%",
        UsagePercent = partition.UsagePercent,
        UsageRingBrush = Brush(color),
        Index = disk.Index,
        InterfaceType = disk.InterfaceType,
        Model = disk.Model,
        IsSsd = disk.IsSsd,
    };

    // ───────────────────────────── 整体容量总览 ─────────────────────────────

    private void BuildOverview(DiskHealthResponse response)
    {
        var disks = response.Disks.Where(d => d.Partitions.Count > 0).ToList();
        if (disks.Count == 0)
        {
            CapacityPanel.Visibility = Visibility.Collapsed;
            return;
        }

        var totalUsed = disks.Sum(d => d.TotalUsageGb);
        var totalCap = disks.Sum(d => d.TotalCapacityGb);
        var pct = totalCap > 0 ? totalUsed * 100.0 / totalCap : 0.0;
        OverviewRing.Value = pct;
        OverviewPctText.Text = $"{pct:0}%";
        OverviewUsedText.Text = string.Format(LocalizationService.L("DiskHealth_Used", "已用 {0}"), FormatGb(totalUsed));
        OverviewTotalText.Text = string.Format(LocalizationService.L("DiskHealth_Total", "共 {0}"), FormatGb(totalCap));

        OverviewDiskList.Children.Clear();
        var cursor = 0;
        foreach (var disk in disks)
        {
            OverviewDiskList.Children.Add(BuildOverviewDiskRow(disk, ref cursor));
        }
        CapacityPanel.Visibility = Visibility.Visible;
    }

    /// <summary>单个磁盘的容量条：每个分区一个「胶囊」色块，宽度与占用成正比。</summary>
    private UIElement BuildOverviewDiskRow(DiskHealthInfo disk, ref int paletteCursor)
    {
        var row = new Grid { ColumnSpacing = 12 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(220) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var label = new TextBlock
        {
            Text = disk.Model,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
        };
        ToolTipService.SetToolTip(label, disk.Model);
        Grid.SetColumn(label, 0);
        row.Children.Add(label);

        var barContainer = new Border
        {
            Height = 16,
            CornerRadius = new CornerRadius(4),
            Background = Brush(ThemeColors.SubtleBg),
            VerticalAlignment = VerticalAlignment.Center,
        };
        var barGrid = new Grid { Margin = new Thickness(1) };
        var colIndex = 0;
        foreach (var partition in disk.Partitions)
        {
            var color = PartitionPalette[paletteCursor % PartitionPalette.Length];
            paletteCursor++;
            var usedGb = Math.Max(partition.UsedGb, 0);
            var freeGb = Math.Max(partition.TotalGb - partition.UsedGb, 0);
            barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(usedGb, 0.01), GridUnitType.Star) });
            barGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(freeGb, 0.01), GridUnitType.Star) });
            var usedSegment = new Border
            {
                Background = Brush(color),
                CornerRadius = new CornerRadius(4),
                Margin = new Thickness(0, 0, 2, 0),
                MinWidth = 3,
            };
            Grid.SetColumn(usedSegment, colIndex);
            barGrid.Children.Add(usedSegment);
            colIndex += 2;
        }
        barContainer.Child = barGrid;
        Grid.SetColumn(barContainer, 1);
        row.Children.Add(barContainer);

        var summary = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Spacing = 1 };
        var usedPct = disk.TotalCapacityGb > 0 ? disk.TotalUsageGb * 100.0 / disk.TotalCapacityGb : 0.0;
        summary.Children.Add(new TextBlock
        {
            Text = $"{usedPct:0}%",
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            Foreground = Brush(PartitionPalette[0]),
            TextAlignment = TextAlignment.Right,
        });
        summary.Children.Add(new TextBlock
        {
            Text = string.Format(LocalizationService.L("DiskHealth_UsedOf", "已用 {0} / {1}"), FormatGb(disk.TotalUsageGb), FormatGb(disk.TotalCapacityGb)),
            FontSize = 12,
            Foreground = Brush(ThemeColors.SecondaryText),
            TextAlignment = TextAlignment.Right,
        });
        Grid.SetColumn(summary, 2);
        row.Children.Add(summary);

        return row;
    }

    // ───────────────────────────── 分区优化（Flyout 确认） ─────────────────────────────

    private void CancelFlyout_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement element)
            HideParentFlyout(element);
    }

    private async void ConfirmOptimize_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: PartitionVm vm })
            return;
        HideParentFlyout((FrameworkElement)sender);
        if (_optimizingDriveLetters.Contains(vm.DriveLetter))
            return;
        _optimizingDriveLetters.Add(vm.DriveLetter);
        SuccessBar.IsOpen = false;
        ErrorBar.IsOpen = false;
        try
        {
            var result = await DiskHealthService.OptimizeAsync(vm.DriveLetter, vm.Index, vm.InterfaceType, vm.Model);
            var done = result.Operation == "retrim" ? LocalizationService.L("DiskHealth_TrimDone", "TRIM 优化完成") : LocalizationService.L("DiskHealth_DefragDone", "碎片整理完成");
            SuccessBar.Severity = result.Background ? InfoBarSeverity.Informational : InfoBarSeverity.Success;
            SuccessBar.Title = $"{vm.DriveLetter}：{done}";
            SuccessBar.Message = result.Message;
            SuccessBar.IsOpen = true;
        }
        catch (Exception ex)
        {
            ErrorBar.Title = string.Format(LocalizationService.L("DiskHealth_OptimizeFailed", "{0}：磁盘优化失败"), vm.DriveLetter);
            ErrorBar.Message = ex.Message;
            ErrorBar.IsOpen = true;
        }
        finally
        {
            _optimizingDriveLetters.Remove(vm.DriveLetter);
        }
    }

    private static void HideParentFlyout(FrameworkElement element)
    {
        DependencyObject? current = element;
        while ((current = VisualTreeHelper.GetParent(current)) is not null)
        {
            if (current is FlyoutPresenter { } presenter && VisualTreeHelper.GetParent(presenter) is Flyout flyout)
            {
                flyout.Hide();
                return;
            }
        }
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => _ = ReloadAsync();

    private void RetryButton_Click(object sender, RoutedEventArgs e) => _ = ReloadAsync();

    // ───────────────────────────── 格式化 ─────────────────────────────

    private static string FormatGb(double gb)
    {
        if (gb >= 1024) return $"{gb / 1024:F2} TB";
        if (gb >= 1) return $"{gb:F2} GB";
        return $"{gb * 1024:F1} MB";
    }

    private static string FormatCount(ulong count) => count.ToString("N0");

    private static string FormatBytes(ulong bytes)
    {
        var gb = bytes / (1024.0 * 1024 * 1024);
        if (gb >= 1024) return $"{gb / 1024:F2} TB";
        return $"{gb:F1} GB";
    }
}
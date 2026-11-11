using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Services;
using Windows.UI;

namespace TubaWinUi3.Pages;

public sealed partial class HardwareSpooferPage : Page
{
    // 语义状态色（警告 = Caution、成功 = Success、危险 = Critical）取自 ThemeColors，随主题解析
    private static Color AccentOrange => ThemeColors.AccentOrange;
    private static Color AccentGreen => ThemeColors.AccentGreen;
    private static Color AccentRed => ThemeColors.AccentRed;

    private List<HardwareSpooferEntry>? _entries;
    private List<HardwareSpooferGpu> _gpus = [];
    private readonly List<(HardwareSpooferGpu Gpu, AutoSuggestBox NameBox, AutoSuggestBox ProviderBox)> _gpuEditors = [];

    private static Brush SecondaryBrush => (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];

    private static readonly string[] GpuProviderPresets = ["NVIDIA", "AMD", "Intel"];

    private static readonly string[] CpuPresets =
    [
        "Intel(R) Core(TM) i9-14900K",
        "Intel(R) Core(TM) i9-13900K",
        "Intel(R) Core(TM) i7-13700K",
        "Intel(R) Core(TM) i7-12700K",
        "Intel(R) Core(TM) i5-13600K",
        "Intel(R) Core(TM) i5-12600K",
        "Intel(R) Core(TM) i3-13100",
        "AMD Ryzen 9 7950X",
        "AMD Ryzen 9 7900X",
        "AMD Ryzen 7 7800X3D",
        "AMD Ryzen 7 7700X",
        "AMD Ryzen 5 7600X",
        "AMD Ryzen 5 5600X",
        "Intel(R) Core(TM) Ultra 9 285K",
        "Intel(R) Core(TM) Ultra 7 265K",
        "Intel(R) Core(TM) Ultra 5 245K",
    ];

    private static readonly string[] GpuPresets =
    [
        "NVIDIA GeForce RTX 4090",
        "NVIDIA GeForce RTX 4080",
        "NVIDIA GeForce RTX 4070 Ti",
        "NVIDIA GeForce RTX 4070",
        "NVIDIA GeForce RTX 4060 Ti",
        "NVIDIA GeForce RTX 4060",
        "NVIDIA GeForce RTX 3090",
        "NVIDIA GeForce RTX 3080",
        "NVIDIA GeForce RTX 3070",
        "AMD Radeon RX 7900 XTX",
        "AMD Radeon RX 7900 XT",
        "AMD Radeon RX 7800 XT",
        "AMD Radeon RX 7600",
        "Intel(R) Arc(TM) A770",
        "Intel(R) Arc(TM) A750",
    ];

    private static readonly string[] SystemProductPresets =
    [
        "XPS 15 9530",
        "ThinkPad X1 Carbon Gen 11",
        "ROG Strix G16",
        "Alienware x16 R2",
        "Zenbook Pro 16X OLED",
        "Surface Laptop 5",
        "MacBook Pro",
    ];

    public HardwareSpooferPage()
    {
        InitializeComponent();

        // 代码构建的画刷不会随主题自动刷新，切换主题后按当前状态重新着色
        ActualThemeChanged += (_, _) => ApplyStatusColors(HardwareSpooferService.IsAdmin);

        ApplyStatusColors(HardwareSpooferService.IsAdmin);

        PopulatePresets();
        LoadCurrentValues();
    }

    /// <summary>
    /// 风险提示与管理员状态的语义色：警告用 Caution、成功/危险用 Success/Critical；
    /// 浅底为基色的 alpha 派生（保留原 30/80 透明度）。
    /// </summary>
    private void ApplyStatusColors(bool isAdmin)
    {
        WarningBorder.Background = new SolidColorBrush(Color.FromArgb(30, AccentOrange.R, AccentOrange.G, AccentOrange.B));
        WarningBorder.BorderBrush = new SolidColorBrush(Color.FromArgb(80, AccentOrange.R, AccentOrange.G, AccentOrange.B));
        WarningIcon.Foreground = new SolidColorBrush(AccentOrange);

        AdminStatusText.Foreground = isAdmin
            ? new SolidColorBrush(AccentGreen)
            : new SolidColorBrush(AccentRed);
    }

    private void PopulatePresets()
    {
        foreach (var cpu in CpuPresets)
            CpuNameBox.Items.Add(cpu);
        foreach (var sp in SystemProductPresets)
            SystemProductBox.Items.Add(sp);
    }

    private void LoadCurrentValues()
    {
        _entries = HardwareSpooferService.ReadAllCurrent();
        _gpus = HardwareSpooferService.ReadGpuDevices();
        BuildGpuEditors();

        // CPU
        var cpuName = GetEntry("ProcessorNameString");
        var cpuVendor = GetEntry("VendorIdentifier");
        var cpuMhz = GetEntry("~MHz");
        CpuNameBox.Text = cpuName?.CurrentValue ?? "";
        if (cpuVendor?.CurrentValue is string v)
        {
            CpuVendorBox.SelectedIndex = v.Equals("AuthenticAMD", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        }
        if (cpuMhz?.CurrentValue is string mhz && int.TryParse(mhz, out var mhzVal))
        {
            CpuMhzBox.Text = mhzVal.ToString();
        }
        CpuOriginalText.Text = BuildOriginalHint("CPU", cpuName, cpuVendor, cpuMhz);

        // System
        var sysProduct = GetEntry("SystemProductName");
        var sysManufacturer = GetEntry("SystemManufacturer");
        var sysFamily = GetEntry("SystemFamily");
        SystemProductBox.Text = sysProduct?.CurrentValue ?? "";
        SystemManufacturerBox.Text = sysManufacturer?.CurrentValue ?? "";
        SystemFamilyBox.Text = sysFamily?.CurrentValue ?? "";
        SystemOriginalText.Text = BuildOriginalHint("系统", sysProduct, sysManufacturer, sysFamily);

        // BIOS
        var biosVendor = GetEntry("BIOSVendor");
        var biosVersion = GetEntry("BIOSVersion");
        var boardMfr = GetEntry("BaseBoardManufacturer");
        var boardProduct = GetEntry("BaseBoardProduct");
        BiosVendorBox.Text = biosVendor?.CurrentValue ?? "";
        BiosVersionBox.Text = biosVersion?.CurrentValue ?? "";
        BoardManufacturerBox.Text = boardMfr?.CurrentValue ?? "";
        BoardProductBox.Text = boardProduct?.CurrentValue ?? "";
        BiosOriginalText.Text = BuildOriginalHint("BIOS", biosVendor, biosVersion, boardMfr, boardProduct);

        UpdateStatusCards();
    }

    /// <summary>
    /// 每张显卡一张编辑卡片：名称与驱动提供商各自独立，修改只影响这张卡本身的注册表键。
    /// </summary>
    private void BuildGpuEditors()
    {
        GpuListPanel.Children.Clear();
        _gpuEditors.Clear();

        if (_gpus.Count == 0)
        {
            GpuListPanel.Children.Add(new TextBlock
            {
                Text = "未检测到可修改的显卡。",
                FontSize = 12,
                Foreground = SecondaryBrush
            });
            return;
        }

        for (var i = 0; i < _gpus.Count; i++)
        {
            var gpu = _gpus[i];

            var nameBox = new AutoSuggestBox
            {
                Header = "GPU 名称",
                Text = gpu.NewName,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                ItemsSource = new List<string>(GpuPresets)
            };
            nameBox.TextChanged += (_, _) =>
            {
                gpu.NewName = nameBox.Text;
                UpdateStatusCards();
            };

            var providerBox = new AutoSuggestBox
            {
                Header = "驱动提供商",
                Text = gpu.NewProvider,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                ItemsSource = new List<string>(GpuProviderPresets)
            };
            providerBox.TextChanged += (_, _) =>
            {
                gpu.NewProvider = providerBox.Text;
                UpdateStatusCards();
            };

            var panel = new StackPanel { Spacing = 12, Padding = new Thickness(12) };
            panel.Children.Add(new TextBlock
            {
                Text = $"显卡 {i + 1} · {ShortHardwareId(gpu.HardwareId)}",
                Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"]
            });
            panel.Children.Add(nameBox);
            panel.Children.Add(providerBox);

            var hint = new List<string> { gpu.DisplayName };
            if (!string.IsNullOrEmpty(gpu.ProviderName)) hint.Add(gpu.ProviderName);
            panel.Children.Add(new TextBlock
            {
                Text = $"当前值 — {string.Join(" · ", hint)}",
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                Foreground = SecondaryBrush
            });

            GpuListPanel.Children.Add(new Border
            {
                Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
                BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Child = panel
            });

            _gpuEditors.Add((gpu, nameBox, providerBox));
        }
    }

    private static string ShortHardwareId(string hardwareId)
    {
        var parts = hardwareId.Split('&');
        return parts.Length >= 2 ? $"{parts[0]}&{parts[1]}" : hardwareId;
    }

    private HardwareSpooferEntry? GetEntry(string valueName)
    {
        return _entries?.FirstOrDefault(e => e.ValueName == valueName);
    }

    private static string BuildOriginalHint(string section, params HardwareSpooferEntry?[] entries)
    {
        var parts = new List<string>();
        foreach (var e in entries)
        {
            if (e is not null && !string.IsNullOrEmpty(e.OriginalValue))
                parts.Add($"{e.ValueName}: {e.OriginalValue}");
        }
        return parts.Count > 0 ? $"原始值 — {string.Join(" | ", parts)}" : "";
    }

    private void UpdateStatusCards()
    {
        var modified = (_entries?.Count(e => e.IsModified) ?? 0) + _gpus.Count(g => g.IsModified);
        ModifiedCountText.Text = modified.ToString();

        BackupStatusText.Text = HardwareSpooferService.HasBackup ? "已有备份" : "无备份";

        var isAdmin = HardwareSpooferService.IsAdmin;
        AdminStatusText.Text = isAdmin ? "是" : "否";
        ApplyStatusColors(isAdmin);
    }

    private void CollectChanges()
    {
        foreach (var (gpu, nameBox, providerBox) in _gpuEditors)
        {
            gpu.NewName = (nameBox.Text ?? "").Trim();
            gpu.NewProvider = (providerBox.Text ?? "").Trim();
        }

        if (_entries is null) return;

        var cpuName = GetEntry("ProcessorNameString");
        if (cpuName is not null) cpuName.CurrentValue = CpuNameBox.Text;

        var cpuVendor = GetEntry("VendorIdentifier");
        if (cpuVendor is not null) cpuVendor.CurrentValue = (CpuVendorBox.SelectedItem as string) ?? CpuVendorBox.Text;

        var cpuMhz = GetEntry("~MHz");
        if (cpuMhz is not null) cpuMhz.CurrentValue = CpuMhzBox.Text;

        var sysProduct = GetEntry("SystemProductName");
        if (sysProduct is not null) sysProduct.CurrentValue = SystemProductBox.Text;

        var sysManufacturer = GetEntry("SystemManufacturer");
        if (sysManufacturer is not null) sysManufacturer.CurrentValue = SystemManufacturerBox.Text;

        var sysFamily = GetEntry("SystemFamily");
        if (sysFamily is not null) sysFamily.CurrentValue = SystemFamilyBox.Text;

        var biosVendor = GetEntry("BIOSVendor");
        if (biosVendor is not null) biosVendor.CurrentValue = BiosVendorBox.Text;

        var biosVersion = GetEntry("BIOSVersion");
        if (biosVersion is not null) biosVersion.CurrentValue = BiosVersionBox.Text;

        var boardMfr = GetEntry("BaseBoardManufacturer");
        if (boardMfr is not null) boardMfr.CurrentValue = BoardManufacturerBox.Text;

        var boardProduct = GetEntry("BaseBoardProduct");
        if (boardProduct is not null) boardProduct.CurrentValue = BoardProductBox.Text;
    }

    private async void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        if (!HardwareSpooferService.IsAdmin)
        {
            ShowStatus("权限不足", "修改 HKLM 注册表需要管理员权限，请以管理员身份运行本程序。", InfoBarSeverity.Error);
            return;
        }

        CollectChanges();

        var changedGpus = _gpus.Count(g => g.IsModified);
        if (_gpus.Any(g => g.IsModified && string.IsNullOrWhiteSpace(g.NewName)))
        {
            ShowStatus("名称不能为空", "请为每张显卡填写名称，或点击刷新恢复当前值。", InfoBarSeverity.Warning);
            return;
        }

        var modified = (_entries?.Count(e => e.IsModified) ?? 0) + changedGpus;
        if (modified == 0)
        {
            ShowStatus("无需修改", "未检测到任何更改。", InfoBarSeverity.Informational);
            return;
        }

        var dialog = new TubaWinUi3.Controls.FluentContentDialog
        {
            Title = "确认修改",
            Content = $"即将修改 {modified} 个注册表项（其中 {changedGpus} 张显卡）。修改前会自动备份原始值。\n\n确定要继续吗？",
            PrimaryButtonText = "确认修改",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        try
        {
            var count = await Task.Run(() => HardwareSpooferService.ApplyChanges(_entries!, _gpus));
            ShowStatus("修改成功", $"已成功修改 {count} 个注册表项。", InfoBarSeverity.Success);
            LoadCurrentValues();
        }
        catch (Exception ex)
        {
            ShowStatus("修改失败", ex.Message, InfoBarSeverity.Error);
        }
    }

    private async void RestoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (!HardwareSpooferService.IsAdmin)
        {
            ShowStatus("权限不足", "恢复注册表需要管理员权限，请以管理员身份运行本程序。", InfoBarSeverity.Error);
            return;
        }

        if (!HardwareSpooferService.HasBackup)
        {
            ShowStatus("无备份", "未找到备份文件，无法恢复。", InfoBarSeverity.Warning);
            return;
        }

        var dialog = new TubaWinUi3.Controls.FluentContentDialog
        {
            Title = "确认恢复",
            Content = "即将恢复所有注册表项到修改前的原始值，多显卡会按每张卡各自的原值还原。\n\n确定要继续吗？",
            PrimaryButtonText = "确认恢复",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        try
        {
            var count = await Task.Run(() => HardwareSpooferService.RestoreAll());
            ShowStatus("恢复成功", $"已成功恢复 {count} 个注册表项。", InfoBarSeverity.Success);
            LoadCurrentValues();
        }
        catch (Exception ex)
        {
            ShowStatus("恢复失败", ex.Message, InfoBarSeverity.Error);
        }
    }

    private async void BackupButton_Click(object sender, RoutedEventArgs e)
    {
        CollectChanges();
        try
        {
            await Task.Run(() => HardwareSpooferService.SaveBackup(_entries!, _gpus));
            ShowStatus("备份成功", "当前配置已备份。", InfoBarSeverity.Success);
            UpdateStatusCards();
        }
        catch (Exception ex)
        {
            ShowStatus("备份失败", ex.Message, InfoBarSeverity.Error);
        }
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        LoadCurrentValues();
        ShowStatus("已刷新", "已重新读取当前注册表值。", InfoBarSeverity.Informational);
    }

    private DispatcherTimer? _statusBarTimer;

    private void ShowStatus(string title, string message, InfoBarSeverity severity)
    {
        StatusBar.Title = title;
        StatusBar.Message = message;
        StatusBar.Severity = severity;
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
}

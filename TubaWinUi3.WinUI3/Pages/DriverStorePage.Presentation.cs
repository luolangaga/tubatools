using System.ComponentModel;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TubaWinUi3.Services;
using TubaWinUi3.Services.DriverStore;

namespace TubaWinUi3.Pages;

public sealed class DriverPackageRow(DriverPackage package, Action<DriverPackage, bool> select, bool selected) : INotifyPropertyChanged
{
    public DriverPackage Package { get; } = package;
    private bool _selected = selected;
    public bool? IsSelected
    {
        get => _selected;
        set
        {
            if (_selected == (value == true)) return;
            _selected = value == true;
            select(Package, _selected);
            PropertyChanged?.Invoke(this, new(nameof(IsSelected)));
        }
    }
    public string Version => string.IsNullOrEmpty(Package.Version) ? "未知版本" : Package.Version;
    public string Metadata => Package.InfName + (Package.Date is { } date ? $" · {date:yyyy-MM-dd}" : "")
        + (Package.BootCritical ? " · 启动关键" : Package.InUse ? " · 有设备使用" : Package.UsageKnown ? " · 无设备关联" : " · 设备关联未知");
    public string Size => Package.SizeKnown ? TempCleanupService.FormatBytes(Package.SizeBytes) : "大小未知";
    public string SelectionName => $"选择 {Package.InfName}，版本 {Version}";
    public bool CanDelete => Package.CanDelete;
    public Visibility LatestVisibility => Package.IsNewest ? Visibility.Visible : Visibility.Collapsed;
    public Visibility OldVisibility => Package.IsNewest ? Visibility.Collapsed : Visibility.Visible;
    public string Details => $"{Package.OriginalInfName}\n发布名称：{Package.InfName}\n版本：{Version}\n日期：{Package.Date:yyyy-MM-dd}\n提供商：{Package.Provider}\n类别：{Package.ClassName}\n签名状态：{Package.Signer}\n占用：{Size}\n设备：{(Package.Devices.Count > 0 ? string.Join("、", Package.Devices) : Package.UsageKnown ? "无设备关联" : "未知")}\n驱动库路径：{Package.StoreInfPath}";
    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed class DriverGroupCard(DriverGroup group, List<DriverPackageRow> rows)
{
    public string Title => group.OriginalInfName;
    public string Subtitle => string.Join(" · ", new[] { group.Provider, group.ClassName, $"{Rows.Count} 个版本",
        $"占用 {TempCleanupService.FormatBytes(Rows.Sum(r => r.Package.SizeBytes))}" }.Where(s => !string.IsNullOrEmpty(s)));
    public List<DriverPackageRow> Rows { get; } = rows;
    public bool CanClean => Rows.Any(r => r.CanDelete);
    public string CleanupLabel => CanClean ? $"清理 {Rows.Count(r => r.CanDelete)} 个旧版本" : "无旧版本";
    public string DeviceSummary => "关联设备：" + string.Join("、", Rows.SelectMany(r => r.Package.Devices).Distinct());
    public Visibility DeviceVisibility => Rows.Any(r => r.Package.Devices.Count > 0) ? Visibility.Visible : Visibility.Collapsed;
}

public sealed partial class DriverStorePage
{
    private void ResultBar_Closed(InfoBar sender, InfoBarClosedEventArgs args) => sender.Visibility = Visibility.Collapsed;

    private void CardHeader_SizeChanged(object sender, SizeChangedEventArgs args)
    {
        if (sender is not Grid grid || grid.Children.Count < 3) return;
        var actions = (StackPanel)grid.Children[2];
        var narrow = args.NewSize.Width < 560;
        Grid.SetRow(actions, narrow ? 1 : 0);
        Grid.SetColumn(actions, narrow ? 1 : 2);
        Grid.SetColumnSpan(actions, narrow ? 2 : 1);
        actions.HorizontalAlignment = narrow ? HorizontalAlignment.Left : HorizontalAlignment.Right;
    }

    private readonly HashSet<string> _selectedInfNames = new(StringComparer.OrdinalIgnoreCase);
    private List<DriverGroupCard> _cards = [];

    private void SetSelected(DriverPackage package, bool selected)
    {
        if (selected) _selectedInfNames.Add(package.InfName);
        else _selectedInfNames.Remove(package.InfName);
        UpdateSelection();
    }

    private void SetSelection(IEnumerable<DriverPackage> packages)
    {
        _selectedInfNames.Clear();
        foreach (var package in packages) _selectedInfNames.Add(package.InfName);
        foreach (var row in _cards.SelectMany(c => c.Rows)) row.IsSelected = _selectedInfNames.Contains(row.Package.InfName);
        UpdateSelection();
    }

    private async void BackupGroup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: DriverGroupCard card })
            await RunAsync(() => BackupAsync(false, card.Rows.Select(r => r.Package).ToList()));
    }

    private async void CleanGroup_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: DriverGroupCard card })
            await RunAsync(() => DeletePackagesAsync(card.Rows.Where(r => r.CanDelete).Select(r => r.Package).ToList()));
    }

    private async void BackupRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: DriverPackageRow row })
            await RunAsync(() => BackupAsync(false, [row.Package]));
    }

    private async void DeleteRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is MenuFlyoutItem { Tag: DriverPackageRow row })
            await RunAsync(() => DeletePackagesAsync([row.Package]));
    }

    private async void DetailsRow_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem { Tag: DriverPackageRow row }) return;
        await RunAsync(async () => await new ContentDialog
        {
            XamlRoot = XamlRoot, Title = "驱动版本详情", CloseButtonText = "关闭",
            RequestedTheme = ThemeService.CurrentElementTheme,
            Content = new ScrollViewer { MaxHeight = 420, Content = new TextBlock { Text = row.Details, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true } }
        }.ShowAsync());
    }
}

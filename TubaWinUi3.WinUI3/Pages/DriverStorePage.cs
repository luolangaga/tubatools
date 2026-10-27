using System.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TubaWinUi3.Services;
using TubaWinUi3.Services.DriverStore;

namespace TubaWinUi3.Pages;

public sealed partial class DriverStorePage : Page
{
    private List<DriverGroup> _groups = [];
    private List<DriverPackage> _visible = [];
    private string? _offlineRoot;
    private bool _busy;
    private bool _ready;
    private readonly CancellationTokenSource _lifetime = new();

    public DriverStorePage()
    {
        InitializeComponent();
        _ready = true;
        Loaded += async (_, _) => await RunAsync(ScanCoreAsync);
        Unloaded += (_, _) => _lifetime.Cancel();
    }

    private async Task RunAsync(Func<Task> action)
    {
        if (_busy || _lifetime.IsCancellationRequested) return;
        _busy = true;
        Commands.IsEnabled = Search.IsEnabled = Filter.IsEnabled = Sort.IsEnabled = Packages.IsEnabled = false;
        Progress.Visibility = Visibility.Visible;
        try { await action(); }
        catch (OperationCanceledException) { }
        catch (Exception ex) { ShowResult("操作失败", ex.Message, InfoBarSeverity.Error); }
        finally
        {
            _busy = false;
            Commands.IsEnabled = Search.IsEnabled = Filter.IsEnabled = Sort.IsEnabled = Packages.IsEnabled = true;
            Progress.Visibility = Visibility.Collapsed;
            UpdateSelection();
        }
    }

    private async Task ScanCoreAsync()
    {
        // 失败时先清空旧快照，不能对另一映像/旧扫描结果执行操作。
        _groups.Clear();
        _selectedInfNames.Clear();
        Render();
        var groups = await DriverStoreService.ScanAsync(message => DispatcherQueue.TryEnqueue(() =>
        {
            if (!_lifetime.IsCancellationRequested) { Status.Text = message; Status.Visibility = Visibility.Visible; }
        }), _lifetime.Token, _offlineRoot);
        _lifetime.Token.ThrowIfCancellationRequested();
        _groups = groups;
        Status.Text = _offlineRoot is null ? "本机驱动库 · 扫描完成" : $"离线驱动库 · {_offlineRoot}（仅浏览与备份）";
        Render();
    }

    private void Render()
    {
        if (!_ready) return;
        IEnumerable<DriverPackage> query = Filter.SelectedIndex == 1
            ? _groups.Where(g => g.Packages.Count > 1).SelectMany(g => g.Packages)
            : _groups.SelectMany(g => g.Packages);
        query = Filter.SelectedIndex switch
        {
            2 => query.Where(p => p.CanDelete),
            3 => query.Where(p => p.UsageKnown && !p.InUse),
            _ => query
        };
        var keyword = Search.Text.Trim();
        if (keyword.Length > 0) query = query.Where(p => new[] { p.InfName, p.OriginalInfName, p.Provider, p.ClassName, p.Version, p.DisplayDevices }
            .Any(value => value.Contains(keyword, StringComparison.OrdinalIgnoreCase)));
        query = Sort.SelectedIndex switch
        {
            1 => query.OrderByDescending(p => p.SizeBytes),
            2 => query.OrderByDescending(p => p.Date),
            3 => query.OrderBy(p => p.Provider, StringComparer.OrdinalIgnoreCase).ThenBy(p => p.OriginalInfName),
            _ => query.OrderBy(p => p.OriginalInfName, StringComparer.OrdinalIgnoreCase).ThenByDescending(p => p.Date).ThenByDescending(p => DriverStoreService.ParseVersionOrNull(p.Version))
        };
        _visible = query.ToList();
        // 一种驱动一张卡，版本始终相邻；排序控制卡片次序，保留组内日期/版本顺序。
        var groups = _groups.Select(g => new DriverGroupCard(g, g.Packages.Where(_visible.Contains)
            .Select(p => new DriverPackageRow(p, SetSelected, _selectedInfNames.Contains(p.InfName))).ToList()))
            .Where(c => c.Rows.Count > 0);
        var rank = _visible.Select((p, i) => (p.InfName, i)).ToDictionary(x => x.InfName, x => x.i, StringComparer.OrdinalIgnoreCase);
        _cards = groups.OrderBy(c => c.Rows.Min(r => rank[r.Package.InfName])).ToList();
        _selectedInfNames.IntersectWith(_visible.Select(p => p.InfName));
        Packages.ItemsSource = _cards;
        Empty.Visibility = _visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateSelection();
    }

    private void UpdateSelection()
    {
        if (!_ready) return;
        var all = _groups.SelectMany(g => g.Packages).ToList();
        var selected = Selected();
        Summary.Text = $"{_groups.Count} 组驱动 · {all.Count} 个驱动包 · 可清理 {all.Count(p => p.CanDelete)} 个旧版本"
            + (selected.Count > 0 ? $" · 已选 {selected.Count} 个" : "")
            + (_offlineRoot is null ? "" : " · 离线映像不判断设备使用情况，不自动清理");
        DeleteButton.IsEnabled = selected.Count > 0 && selected.All(p => p.CanDelete);
    }

    private List<DriverPackage> Selected() => _visible.Where(p => _selectedInfNames.Contains(p.InfName)).ToList();
    private void ShowResult(string title, string message, InfoBarSeverity severity)
    {
        ResultBar.Visibility = Visibility.Visible;
        ResultBar.Title = title;
        ResultBar.Message = message;
        ResultBar.Severity = severity;
        ResultBar.IsOpen = true;
    }

    private async Task<bool> ConfirmAsync(string title, string text, string verb)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot, Title = title,
            Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = verb, CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close,
            RequestedTheme = ThemeService.CurrentElementTheme
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RunAsync(ScanCoreAsync);
    private void Search_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args) => Render();
    private void Filter_Changed(object sender, SelectionChangedEventArgs e) => Render();
    private void ClearSelection_Click(object sender, RoutedEventArgs e) => SetSelection([]);
    private void SelectOld_Click(object sender, RoutedEventArgs e)
    {
        SetSelection(_visible.Where(p => p.CanDelete).ToList());
    }

    private async void Delete_Click(object sender, RoutedEventArgs e) => await RunAsync(() => DeletePackagesAsync(Selected()));

    private async Task DeletePackagesAsync(List<DriverPackage> selected)
    {
        if (selected.Count == 0 || selected.Any(p => !p.CanDelete)) return;
        if (!await ConfirmAsync("删除驱动旧版本", $"即将删除 {selected.Count} 个驱动包：\n{string.Join("、", selected.Select(p => p.InfName))}\n建议先备份。无设备关联并不代表所有软件驱动都可安全删除。", "删除所选")) return;
        var failures = new List<string>();
        foreach (var p in selected)
        {
            Status.Text = $"正在删除 {p.InfName}…";
            var result = await DriverStoreService.DeleteAsync(p.InfName, _lifetime.Token);
            if (!result.Success) failures.Add($"{p.InfName}：{result.Message}");
        }
        await ScanCoreAsync();
        ShowResult(failures.Count == 0 ? "删除完成" : "部分删除失败", $"成功 {selected.Count - failures.Count} / {selected.Count}\n" + string.Join("\n", failures),
            failures.Count == 0 ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
    }

    private async Task BackupAsync(bool all, List<DriverPackage>? explicitSelection = null)
    {
        var selected = explicitSelection ?? Selected();
        if (!all && selected.Count == 0) { ShowResult("请先选择驱动", "勾选列表中的驱动包后再备份", InfoBarSeverity.Informational); return; }
        var dir = await Win32Dialogs.PickFolderAsync();
        if (string.IsNullOrEmpty(dir)) return;
        var names = all ? new List<string> { "*" } : selected.Select(p => p.InfName).ToList();
        var errors = new List<string>();
        foreach (var name in names)
        {
            Status.Text = $"正在备份 {name}…";
            var result = await DriverStoreService.ExportAsync(name, dir, _lifetime.Token, _offlineRoot);
            if (!result.Success) errors.Add(result.Message);
        }
        Status.Text = "备份操作完成";
        ShowResult(errors.Count == 0 ? "备份完成" : "部分备份失败", errors.Count == 0 ? $"驱动包已导出到：{dir}" : string.Join("\n", errors),
            errors.Count == 0 ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
    }
    private async void BackupSelected_Click(object sender, RoutedEventArgs e) => await RunAsync(() => BackupAsync(false));
    private async void BackupAll_Click(object sender, RoutedEventArgs e) => await RunAsync(() => BackupAsync(true));

    private async Task InstallAsync(bool directory)
    {
        if (_offlineRoot is not null) { ShowResult("当前为离线浏览", "安装驱动前请返回本机驱动库", InfoBarSeverity.Informational); return; }
        var source = directory ? await Win32Dialogs.PickFolderAsync() : Win32Dialogs.PickOpen("驱动 INF 文件\0*.inf\0\0", "选择驱动 INF");
        if (string.IsNullOrEmpty(source)) return;
        if (!await ConfirmAsync("安装驱动", $"将安装：{source}\n目录内的 INF 将逐个安装；Windows 按驱动匹配规则选择，不强制替换现有驱动。", "开始安装")) return;
        Status.Text = "正在安装驱动…";
        var result = await DriverStoreService.ImportAsync(source, _lifetime.Token);
        await ScanCoreAsync();
        ShowResult(result.Success ? "安装完成" : "安装存在失败", result.Message + (result.RebootRequired ? "\n需要重启电脑才能完全生效" : ""),
            result.Success ? InfoBarSeverity.Success : InfoBarSeverity.Warning);
    }
    private async void Install_Click(object sender, RoutedEventArgs e) => await RunAsync(() => InstallAsync(false));
    private async void Restore_Click(object sender, RoutedEventArgs e) => await RunAsync(() => InstallAsync(true));

    private async void Csv_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        var path = Win32Dialogs.PickSave("CSV 文件\0*.csv\0\0", "csv", "驱动列表.csv");
        if (path is null) return;
        await File.WriteAllTextAsync(path, DriverStoreService.ToCsv(_visible), new UTF8Encoding(true), _lifetime.Token);
        ShowResult("列表已导出", path, InfoBarSeverity.Success);
    });
    private async void Offline_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        var root = await Win32Dialogs.PickFolderAsync();
        if (string.IsNullOrEmpty(root)) return;
        _offlineRoot = root;
        await ScanCoreAsync();
    });
    private async void Online_Click(object sender, RoutedEventArgs e) => await RunAsync(async () =>
    {
        _offlineRoot = null;
        await ScanCoreAsync();
    });
}

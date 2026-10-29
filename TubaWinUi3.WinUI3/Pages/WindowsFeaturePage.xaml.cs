using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Security.Principal;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TubaWinUi3.Services;

namespace TubaWinUi3.Pages;

/// <summary>保留行实例，刷新配置时只通知属性变化，避免列表重置滚动位置。</summary>
public sealed class FeatureRowVm : INotifyPropertyChanged
{
    private FeatureFlagEntry _entry;
    private bool _isAdmin;
    public FeatureRowVm(FeatureFlagEntry entry, bool isAdmin) { _entry = entry; _isAdmin = isAdmin; }
    public FeatureRowVm Self => this;
    public uint FeatureId => _entry.FeatureId;
    public string DisplayName => _entry.Name ?? $"功能 {FeatureId}";
    public string ActionLabel => $"启用或禁用功能 {FeatureId}：{DisplayName}";
    public string IdText => $"#{FeatureId}";
    public string StateText => WindowsFeaturePage.StateLabel(_entry.RuntimeState);
    public string RestartText => $"重启后的配置：{WindowsFeaturePage.StateLabel(_entry.BootState)}";
    public Visibility RestartVisibility => _entry.RuntimeState != _entry.BootState ? Visibility.Visible : Visibility.Collapsed;
    // 开关表示保存的目标配置；当前与目标不一致时，在行内说明重启后的配置。
    public bool IsOn => (_entry.BootState ?? _entry.RuntimeState) == FeatureState.Enabled;
    public bool CanToggle => _isAdmin;
    public bool CanReset => _isAdmin && _entry.HasConfig;
    public event PropertyChangedEventHandler? PropertyChanged;
    public void Apply(FeatureFlagEntry entry, bool isAdmin)
    {
        _entry = entry;
        _isAdmin = isAdmin;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null));
    }
}

/// <summary>统一功能目录、名称/ID 搜索与批量 ID 操作。</summary>
public sealed partial class WindowsFeaturePage : Page
{
    private Dictionary<uint, string> _dictionary = new();
    private bool _busy;

    private bool _isAdmin = true;
    private string _search = string.Empty;
    private bool _namedOnly = true;
    private List<FeatureFlagEntry> _catalog = new();

    /// <summary>当前查询返回的全部行（已含字典补充项）。</summary>
    private List<FeatureRowVm> _rows = new();
    private FeatureIncrementalCollection _visibleRows;

    public WindowsFeaturePage()
    {
        InitializeComponent();
        _visibleRows = new FeatureIncrementalCollection(_rows, DispatcherQueue);
        FeatureList.ItemsSource = _visibleRows;
    }

    // ───────────────────────────── 初始化 / 清理 ─────────────────────────────

    private void WindowsFeaturePage_Loaded(object sender, RoutedEventArgs e) => _ = LoadAsync();


    private static bool IsCurrentUserAdmin() =>
        new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    // ───────────────────────────── 首次加载 ─────────────────────────────

    private async Task LoadAsync()
    {
        if (_busy) return;
        SetBusy(true);
        LoadingPanel.Visibility = Visibility.Visible;
        ContentPanel.Visibility = Visibility.Collapsed;
        MissingPanel.Visibility = Visibility.Collapsed;

        try
        {
            if (!WindowsFeatureService.IsSupported())
            {
                LoadingPanel.Visibility = Visibility.Collapsed;
                MissingPanel.Visibility = Visibility.Visible;
                MissingText.Text = "当前系统不支持功能配置 API（需要 Windows 10 1903 / build 18963 或更高版本）。" +
                    "系统版本过低或 ntdll 缺少 RtlQueryAllFeatureConfigurations 等导出点时不可用。";
                return;
            }

            // ① 功能字典（名字 → ID，进程内缓存一次）
            _dictionary = await Task.Run(WindowsFeatureService.LoadDictionary);
            // ② 管理员判定（nexbox status.is_admin：非管理员时禁用操作按钮）
            _isAdmin = await Task.Run(IsCurrentUserAdmin);

            await RefreshCoreAsync();

            LoadingPanel.Visibility = Visibility.Collapsed;
            ContentPanel.Visibility = Visibility.Visible;
        }
        catch (Exception ex)
        {
            LoadingPanel.Visibility = Visibility.Collapsed;
            ErrorBar.Title = "加载功能配置失败";
            ErrorBar.Message = ex.Message;
            ErrorBar.IsOpen = true;
        }
        finally
        {
            SetBusy(false);
        }
    }

    // ───────────────────────────── 刷新（nexbox refresh） ─────────────────────────────

    /// <summary>刷新统计与列表：nexbox refresh = status + feature_flags_query 同款重查。</summary>
    private async Task RefreshCoreAsync()
    {
        var queryTask = Task.Run(() => WindowsFeatureService.QueryUnifiedFeatures());
        var buildTask = Task.Run(WindowsFeatureService.GetOsBuild);
        var bootPendingTask = Task.Run(WindowsFeatureService.IsBootPending);
        await Task.WhenAll(queryTask, buildTask, bootPendingTask);

        _catalog = queryTask.Result;
        var storeCount = _catalog.Count(e => e.HasConfig);
        var storeEnabled = _catalog.Count(e => e.RuntimeState == FeatureState.Enabled);
        BuildValue.Text = buildTask.Result > 0 ? buildTask.Result.ToString() : "--";
        DictValue.Text = _dictionary.Count.ToString("N0");
        ConfiguredValue.Text = storeCount.ToString("N0");
        EnabledValue.Text = storeEnabled.ToString("N0");

        BootPendingBar.Visibility = bootPendingTask.Result ? Visibility.Visible : Visibility.Collapsed;
        DictMissingBar.Visibility = _dictionary.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        NotAdminBar.Visibility = _isAdmin ? Visibility.Collapsed : Visibility.Visible;

        ApplyFilter(preserveView: true);
    }

    private void ApplyFilter(bool preserveView = false)
    {
        var entries = WindowsFeatureCatalog.Filter(_catalog, _search, _namedOnly);
        if (preserveView && entries.Select(e => e.FeatureId).SequenceEqual(_rows.Select(r => r.FeatureId)))
        {
            for (var i = 0; i < entries.Count; i++) _rows[i].Apply(entries[i], _isAdmin);
            UpdateListStats();
            return;
        }
        _rows = entries.Select(e => new FeatureRowVm(e, _isAdmin)).ToList();
        _visibleRows.BatchLoaded -= UpdateListStats;
        _visibleRows = new FeatureIncrementalCollection(_rows, DispatcherQueue);
        _visibleRows.BatchLoaded += UpdateListStats;
        // 只有搜索条件/成员变化才重建数据源；加载下一批及配置刷新都保留原集合。
        FeatureList.ItemsSource = _visibleRows;
        UpdateListStats();
    }

    /// <summary>刷新护栏：busy 防重入 + 错误弹条。</summary>
    private async Task RunRefreshAsync()
    {
        if (_busy) return;
        SetBusy(true);
        try
        {
            await RefreshCoreAsync();
        }
        catch (Exception ex)
        {
            ErrorBar.Title = "刷新功能配置失败";
            ErrorBar.Message = ex.Message;
            ErrorBar.IsOpen = true;
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void UpdateListStats()
    {
        ListStats.Text = $"共 {_rows.Count:N0} 条 · 已显示 {_visibleRows.Count:N0} 条";
        NoResultsText.Text = _search.Length == 0
            ? "没有可显示的功能，请尝试关闭名称筛选"
            : $"未找到与「{_search}」匹配的功能";
        NoResultsPanel.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // ───────────────────────────── 搜索（nexbox submitSearch） ─────────────────────────────


    /// <summary>回车提交（AutoSuggestBox 无候选列表时回车即 QuerySubmitted）。</summary>
    private void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args) => SubmitSearch();

    /// <summary>提交搜索：打字不触发查询，仅回车 / 点击搜索按钮时提交（nexbox 同款）。</summary>
    private void SubmitSearch()
    {
        _search = SearchBox.Text.Trim();
        ApplyFilter();
    }

    private void NamedOnlyToggle_Click(object sender, RoutedEventArgs e)
    {
        _namedOnly = NamedOnlyToggle.IsChecked == true;
        ApplyFilter();
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => _ = RunRefreshAsync();

    internal static string StateLabel(FeatureState? state) => state switch
    {
        FeatureState.Enabled => "已启用",
        FeatureState.Disabled => "已禁用",
        FeatureState.Default => "系统默认",
        _ => "未配置"
    };

    private void FeatureToggle_Toggled(object sender, RoutedEventArgs e)
    {
        // 等绑定完成后再比较，避免虚拟化复用控件时 IsOn/Tag 的更新顺序触发写入。
        if (sender is ToggleSwitch toggle)
            DispatcherQueue.TryEnqueue(() => _ = ApplyToggleAsync(toggle));
    }

    private async Task ApplyToggleAsync(ToggleSwitch toggle)
    {
        if (toggle.Tag is not FeatureRowVm vm || !toggle.IsLoaded || toggle.IsOn == vm.IsOn) return;
        if (_busy || !_isAdmin) { toggle.IsOn = vm.IsOn; return; }
        try { await RunActionsAsync([vm.FeatureId], toggle.IsOn ? "启用" : "禁用"); }
        finally
        {
            // 初始化、虚拟化重用、数据回读和失败回退都不会再次执行写入。
            toggle.IsOn = vm.IsOn;
        }
    }

    private async void ResetFeature_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || !_isAdmin || sender is not Button { Tag: FeatureRowVm vm }) return;
        var dialog = new TubaWinUi3.Controls.FluentContentDialog
        {
            XamlRoot = XamlRoot,
            Title = $"恢复功能 #{vm.FeatureId} 的默认配置？",
            Content = $"{vm.DisplayName}\n将移除当前和重启后的 User 配置。",
            PrimaryButtonText = "恢复默认",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await RunActionsAsync([vm.FeatureId], "重置");
    }

    public Visibility InfoBarVisibility(bool isOpen) => isOpen ? Visibility.Visible : Visibility.Collapsed;

    private void IdInputRow_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // 窄窗口只将按钮换到下一行，输入和所有操作仍然可见。
        var narrow = e.NewSize.Width < 560;
        IdActionsPanel.Margin = new Thickness(0, narrow ? 8 : 0, 0, 0);
        Grid.SetRow(IdActionsPanel, narrow ? 1 : 0);
        Grid.SetColumn(IdActionsPanel, narrow ? 0 : 1);
        Grid.SetColumnSpan(FeatureIdsBox, narrow ? 2 : 1);
    }

    private async void BatchAction_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || !_isAdmin || sender is not Button { Tag: string action }) return;
        uint[] ids;
        try { ids = WindowsFeatureCatalog.ParseIds(FeatureIdsBox.Text); }
        catch (ArgumentException ex)
        {
            ErrorBar.Title = "功能 ID 格式错误";
            ErrorBar.Message = ex.Message;
            ErrorBar.IsOpen = true;
            SuccessBar.IsOpen = false;
            FeatureIdsBox.Focus(FocusState.Programmatic);
            return;
        }
        await RunActionsAsync(ids, action);
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        foreach (var button in IdActionsPanel.Children.OfType<Button>())
            button.IsEnabled = !busy && _isAdmin;
        FeatureList.IsEnabled = !busy;
        RefreshButton.IsEnabled = !busy;
    }

    private async Task RunActionsAsync(uint[] ids, string action)
    {
        if (_busy || !_isAdmin) return;
        SetBusy(true);
        ErrorBar.IsOpen = SuccessBar.IsOpen = false;
        try
        {
            var failures = await Task.Run(() =>
            {
                var errors = new List<string>();
                foreach (var id in ids)
                {
                    try
                    {
                        if (action == "重置") WindowsFeatureService.Reset(id);
                        else WindowsFeatureService.SetState(id, action == "启用");
                    }
                    catch (Exception ex) { errors.Add($"#{id}：{ex.Message}"); }
                }
                return errors;
            });
            var succeeded = ids.Length - failures.Count;
            if (succeeded > 0)
            {
                SuccessBar.Title = $"{(action == "重置" ? "恢复默认" : action)}完成：{succeeded} 个功能";
                SuccessBar.Message = "已更新当前配置并保存重启后的配置，建议重启 Windows。";
                SuccessBar.IsOpen = true;
            }
            if (failures.Count > 0)
            {
                ErrorBar.Title = $"{failures.Count} 个功能操作失败";
                ErrorBar.Message = string.Join("\n", failures);
                ErrorBar.IsOpen = true;
            }
            try { await RefreshCoreAsync(); }
            catch (Exception ex)
            {
                ErrorBar.Title = "操作后刷新失败";
                ErrorBar.Message = string.Join("\n", failures.Append(ex.Message));
                ErrorBar.IsOpen = true;
            }
        }
        finally { SetBusy(false); }
    }

}

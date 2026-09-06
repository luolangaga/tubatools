using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Principal;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Services;
using Windows.UI;

namespace TubaWinUi3.Pages;

/// <summary>功能开关行视图模型（nexbox FeatureFlagEntry 的展示层映射）。</summary>
public sealed class FeatureRowVm
{
    public FeatureRowVm Self => this;
    public required uint FeatureId { get; init; }
    /// <summary>字典名；无名字时回退编号文案（nexbox：name ?? id）。</summary>
    public required string DisplayName { get; init; }
    public required string IdText { get; init; }
    public required string StateText { get; init; }
    public required SolidColorBrush StateBrush { get; init; }
    public required SolidColorBrush StateBackground { get; init; }
    public required string ExperimentText { get; init; }
    public required SolidColorBrush ExperimentBrush { get; init; }
    public required SolidColorBrush ExperimentBackground { get; init; }
    /// <summary>字典命中但无配置的条目不展示「实验功能 / 系统覆盖」标签。</summary>
    public required Visibility ExperimentVisibility { get; init; }
    public required string PriorityText { get; init; }
    /// <summary>已启用时禁用「启用」按钮（nexbox：disabled = has_config && enabled_state===2）。</summary>
    public required bool CanEnable { get; init; }
    /// <summary>已禁用时禁用「禁用」按钮。</summary>
    public required bool CanDisable { get; init; }
    /// <summary>仅系统已有自定义配置时可用「重置」（nexbox：disabled = !has_config）。</summary>
    public required bool CanReset { get; init; }
    public required string FlyoutTitle { get; init; }
    public required string FlyoutDesc { get; init; }
    public required FeatureState State { get; init; }
}

/// <summary>
/// Windows 隐藏功能页：ID 获取逻辑完全对照 nexbox（gitcode.com/MuLiuSaMa/nexbox，同为 ViVe 移植）——
/// 总列表 = 查询所选存储（运行时 / 引导）的全部配置、字典补名、按功能 ID 升序，
/// 单次查询上限 500 条、每批展示 100 条「加载更多」；
/// 搜索 = 独立提交的查询（打字不触发，回车 / 按钮提交）：按 ID / 名称过滤配置存储，
/// 并把字典命中但当前无配置的条目补充进来（标记「未配置」）；
/// 浏览态（无搜索词）默认仅显示字典可识别名称的条目（namedOnly）。
/// 查询逻辑见 WindowsFeatureService.QueryFeatures。
/// </summary>
public sealed partial class WindowsFeaturePage : Page
{
    // 品牌调色板（与主题无关）
    private static readonly Color SuccessGreen = Color.FromArgb(255, 43, 182, 115);
    private static readonly Color CautionAmber = Color.FromArgb(255, 245, 166, 35);
    private static readonly Color CriticalRed = Color.FromArgb(255, 242, 80, 59);
    private static readonly Color NeutralGray = Color.FromArgb(255, 142, 142, 142);

    /// <summary>单次查询条数上限（nexbox QUERY_LIMIT）。</summary>
    private const int QueryLimit = WindowsFeatureService.DefaultQueryLimit;
    /// <summary>每批展示条数，超出后「加载更多」（nexbox PAGE_SIZE）。</summary>
    private const int PageSize = 100;

    private Dictionary<uint, string> _dictionary = new();
    private bool _busy;
    private bool _isAdmin = true;

    // ── 查询状态（nexbox 同款：store / search / namedOnly / persistBoot） ──
    /// <summary>当前查询的存储（nexbox store，默认 runtime）。</summary>
    private string _store = WindowsFeatureService.StoreRuntime;
    /// <summary>已提交的搜索词；空 = 浏览态（nexbox search，仅提交时更新）。</summary>
    private string _search = string.Empty;
    /// <summary>浏览态仅显示有名称条目（nexbox namedOnly，默认开）。</summary>
    private bool _namedOnly = true;
    /// <summary>启用/禁用是否持久化到 Boot 存储（nexbox persistBoot，默认开）。</summary>
    private bool _persistBoot = true;

    /// <summary>当前查询返回的全部行（已含字典补充项）。</summary>
    private List<FeatureRowVm> _rows = new();
    /// <summary>当前展示到的条数（nexbox visibleCount，从 PAGE_SIZE 起步）。</summary>
    private int _visibleCount = PageSize;

    public WindowsFeaturePage()
    {
        InitializeComponent();
        SearchBox.KeyDown += SearchBox_KeyDown;
        // 先置默认值再挂事件，避免初始化时触发 Toggled 引发多余刷新
        NamedOnlyToggle.IsOn = true;
        NamedOnlyToggle.Toggled += NamedOnlyToggle_Toggled;
        PersistBootToggle.IsOn = true;
        PersistBootToggle.Toggled += PersistBootToggle_Toggled;
        RuntimeStoreButton.IsChecked = true;
    }

    // ───────────────────────────── 初始化 / 清理 ─────────────────────────────

    private void WindowsFeaturePage_Loaded(object sender, RoutedEventArgs e) => _ = LoadAsync();

    private void BackButton_Click(object sender, RoutedEventArgs e) => App.MainWindow?.NavigateBack();

    private static SolidColorBrush Brush(Color color) => new(color);

    private static bool IsCurrentUserAdmin() =>
        new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    // ───────────────────────────── 首次加载 ─────────────────────────────

    private async Task LoadAsync()
    {
        if (_busy) return;
        _busy = true;
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
            _busy = false;
        }
    }

    // ───────────────────────────── 刷新（nexbox refresh） ─────────────────────────────

    /// <summary>刷新统计与列表：nexbox refresh = status + feature_flags_query 同款重查。</summary>
    private async Task RefreshCoreAsync()
    {
        var queryTask = Task.Run(() => WindowsFeatureService.QueryFeatures(_store, _search, _namedOnly, QueryLimit));
        var buildTask = Task.Run(WindowsFeatureService.GetOsBuild);
        var bootPendingTask = Task.Run(WindowsFeatureService.IsBootPending);
        await Task.WhenAll(queryTask, buildTask, bootPendingTask);

        var (entries, storeCount, storeEnabled) = queryTask.Result;
        BuildValue.Text = buildTask.Result > 0 ? buildTask.Result.ToString() : "--";
        DictValue.Text = _dictionary.Count.ToString("N0");
        ConfiguredValue.Text = storeCount.ToString("N0");
        EnabledValue.Text = storeEnabled.ToString("N0");

        BootPendingBar.Visibility = bootPendingTask.Result ? Visibility.Visible : Visibility.Collapsed;
        DictMissingBar.Visibility = _dictionary.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        NotAdminBar.Visibility = _isAdmin ? Visibility.Collapsed : Visibility.Visible;

        _rows = entries.Select(BuildRow).ToList();
        _visibleCount = PageSize;
        RenderRows();
    }

    /// <summary>刷新护栏：busy 防重入 + 错误弹条。</summary>
    private async Task RunRefreshAsync()
    {
        if (_busy) return;
        _busy = true;
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
            _busy = false;
        }
    }

    /// <summary>按 _visibleCount 分批展示（nexbox：visibleEntries = entries.slice(0, visibleCount)）。</summary>
    private void RenderRows()
    {
        FeatureList.ItemsSource = _rows.Take(_visibleCount).ToList();

        var stats = $"共 {_rows.Count:N0} 条";
        if (_rows.Count >= QueryLimit)
            stats += $"（已达单次查询上限 {QueryLimit} 条，可细化搜索条件）";
        ListStats.Text = stats;

        NoResultsText.Text = _search.Length == 0
            ? "当前存储中没有可显示的配置"
            : $"未找到与「{_search}」匹配的功能";
        NoResultsPanel.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        LoadMoreButton.Visibility = _visibleCount < _rows.Count ? Visibility.Visible : Visibility.Collapsed;
    }

    // ───────────────────────────── 搜索（nexbox submitSearch） ─────────────────────────────

    private void SearchButton_Click(object sender, RoutedEventArgs e) => SubmitSearch();

    private void SearchBox_KeyDown(object sender, Microsoft.UI.Xaml.Input.KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
            SubmitSearch();
    }

    /// <summary>提交搜索：打字不触发查询，仅回车 / 点击搜索按钮时提交（nexbox 同款）。</summary>
    private async void SubmitSearch()
    {
        var term = SearchBox.Text.Trim();
        if (term == _search)
            return;
        _search = term;
        await RunRefreshAsync();
    }

    // ───────────────────────────── 存储切换 / 开关 ─────────────────────────────

    /// <summary>存储切换（nexbox store runtime/boot）：重新查询所选存储。</summary>
    private void StoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton clicked)
            return;
        var target = ReferenceEquals(clicked, BootStoreButton)
            ? WindowsFeatureService.StoreBoot
            : WindowsFeatureService.StoreRuntime;
        if (target == _store)
        {
            clicked.IsChecked = true; // 不允许两个都弹起
            return;
        }
        _store = target;
        RuntimeStoreButton.IsChecked = ReferenceEquals(RuntimeStoreButton, clicked);
        BootStoreButton.IsChecked = ReferenceEquals(BootStoreButton, clicked);
        _ = RunRefreshAsync();
    }

    private void NamedOnlyToggle_Toggled(object sender, RoutedEventArgs e)
    {
        _namedOnly = NamedOnlyToggle.IsOn;
        _ = RunRefreshAsync();
    }

    private void PersistBootToggle_Toggled(object sender, RoutedEventArgs e)
    {
        _persistBoot = PersistBootToggle.IsOn;
    }

    private void LoadMoreButton_Click(object sender, RoutedEventArgs e)
    {
        _visibleCount += PageSize;
        RenderRows();
    }

    private void RefreshButton_Click(object sender, RoutedEventArgs e) => _ = RunRefreshAsync();

    // ───────────────────────────── 行构建 ─────────────────────────────

    private FeatureRowVm BuildRow(FeatureFlagEntry entry)
    {
        var hasConfig = entry.HasConfig;
        var state = entry.State;
        var display = entry.Name ?? $"功能 {entry.FeatureId}";

        var (stateText, stateBrush, stateBg) = state switch
        {
            FeatureState.Enabled => ("已启用", Brush(SuccessGreen), Brush(Color.FromArgb(0x14, 43, 182, 115))),
            FeatureState.Disabled => ("已禁用", Brush(CriticalRed), Brush(Color.FromArgb(0x16, 242, 80, 59))),
            _ => ("未配置", Brush(NeutralGray), Brush(Color.FromArgb(0x16, 142, 142, 142)))
        };

        return new FeatureRowVm
        {
            FeatureId = entry.FeatureId,
            DisplayName = display,
            IdText = $"#{entry.FeatureId}",
            StateText = stateText,
            StateBrush = stateBrush,
            StateBackground = stateBg,
            ExperimentText = entry.IsExperiment ? "实验功能" : "系统覆盖",
            ExperimentBrush = Brush(entry.IsExperiment ? CautionAmber : NeutralGray),
            ExperimentBackground = Brush(entry.IsExperiment
                ? Color.FromArgb(0x16, 245, 166, 35)
                : Color.FromArgb(0x14, 142, 142, 142)),
            ExperimentVisibility = hasConfig ? Visibility.Visible : Visibility.Collapsed,
            PriorityText = entry.PriorityText,
            CanEnable = _isAdmin && state != FeatureState.Enabled,
            CanDisable = _isAdmin && state != FeatureState.Disabled,
            CanReset = _isAdmin && hasConfig,
            FlyoutTitle = hasConfig
                ? $"功能「{display}」当前为{stateText}，确认执行操作？"
                : $"「{display}」尚无自定义配置，确认启用？",
            FlyoutDesc = BuildFlyoutDesc(state),
            State = state
        };
    }

    private string BuildFlyoutDesc(FeatureState state) => state switch
    {
        FeatureState.Enabled => "将禁用该功能（User 优先级，ViVeTool 同款语义）。",
        FeatureState.Disabled => "将启用该功能（User 优先级，ViVeTool 同款语义）。实验性功能可能导致系统不稳定，请谨慎开启。",
        _ => "将对功能写入 User 优先级配置（ViVeTool 同款语义）。"
    };

    // ───────────────────────────── 操作（Flyout 确认） ─────────────────────────────

    private void FeatureButton_Click(object sender, RoutedEventArgs e)
    {
        // 按钮自身打开 Flyout，无需额外逻辑
    }

    private void CancelFlyout_Click(object sender, RoutedEventArgs e) => HideParentFlyout((FrameworkElement)sender);

    private async void ConfirmFeature_Click(object sender, RoutedEventArgs e)
    {
        var confirm = (Button)sender;
        HideParentFlyout(confirm);
        if (confirm.Tag is not FeatureRowVm vm || _busy)
            return;

        var action = (confirm.Content as string)?.Replace("确认", "") ?? "启用";
        var resultText = await RunActionAsync(vm, action);
        if (resultText is null)
            return;

        SuccessBar.Title = action switch
        {
            "重置" => $"已重置功能 #{vm.FeatureId}",
            "禁用" => $"已禁用功能 #{vm.FeatureId}",
            _ => $"已启用功能 #{vm.FeatureId}"
        };
        SuccessBar.Message = resultText;
        SuccessBar.IsOpen = true;
        ErrorBar.IsOpen = false;
        await RunRefreshAsync();
    }

    /// <summary>执行启用/禁用/重置；返回结果文案；失败时展示错误并返回 null。</summary>
    private async Task<string?> RunActionAsync(FeatureRowVm vm, string action)
    {
        if (_busy) return null;
        _busy = true;
        try
        {
            return await Task.Run(() => action switch
            {
                "重置" => WindowsFeatureService.Reset(vm.FeatureId),
                "禁用" => WindowsFeatureService.SetState(vm.FeatureId, false, _persistBoot),
                _ => WindowsFeatureService.SetState(vm.FeatureId, true, _persistBoot)
            });
        }
        catch (Exception ex)
        {
            ErrorBar.Title = $"操作失败（{action} #{vm.FeatureId}）";
            ErrorBar.Message = ex.Message;
            ErrorBar.IsOpen = true;
            SuccessBar.IsOpen = false;
            return null;
        }
        finally
        {
            _busy = false;
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
}

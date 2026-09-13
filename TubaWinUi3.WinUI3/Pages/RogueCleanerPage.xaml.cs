using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using Microsoft.UI.Xaml.Controls.Primitives;
using System.Runtime.InteropServices.WindowsRuntime;
using System.ComponentModel;
using System.Collections.ObjectModel;
using TubaWinUi3.Services;
using TubaWinUi3.Services.ActiveIntercept;
using TubaWinUi3.Services.RogueCleaner;
using Windows.ApplicationModel.DataTransfer;
using Windows.UI;

namespace TubaWinUi3.Pages;

/// <summary>拦截列表分区标题行（现代菜单 / 非现代菜单 分界线，不可选中）。</summary>
public sealed class AiSectionHeaderVm
{
    public string Key { get; init; } = "";
    public string Text { get; init; } = "";
}

/// <summary>拦截列表行模板选择器：分区标题用标题模板，条目用条目模板。</summary>
public sealed class AiRowTemplateSelector : DataTemplateSelector
{
    public DataTemplate? ItemTemplate { get; set; }
    public DataTemplate? HeaderTemplate { get; set; }

    protected override DataTemplate SelectTemplateCore(object item)
        => item is AiSectionHeaderVm ? HeaderTemplate! : ItemTemplate!;

    protected override DataTemplate SelectTemplateCore(object item, DependencyObject container)
        => SelectTemplateCore(item);
}

/// <summary>右键菜单主列表的“场景分区标题”行（Key=场景名，Count=场景内项数，不可选中）。</summary>
public sealed class CmSectionHeaderVm
{
    public string Key { get; init; } = "";
    public int Count { get; init; }
}

/// <summary>右键菜单主列表行模板选择器：场景分区标题行与条目行分别使用不同模板。</summary>
public sealed class CmRowTemplateSelector : DataTemplateSelector
{
    public DataTemplate? ItemTemplate { get; set; }
    public DataTemplate? HeaderTemplate { get; set; }

    protected override DataTemplate SelectTemplateCore(object item)
        => item is CmSectionHeaderVm ? HeaderTemplate! : ItemTemplate!;

    protected override DataTemplate SelectTemplateCore(object item, DependencyObject container)
        => SelectTemplateCore(item);
}

/// <summary>右键菜单主列表的“场景分组”中间结构：Key = 场景名，自身是条目集合。
/// 供按场景排序后拍平为“分区标题 + 条目”列表使用。</summary>
internal sealed class CmSceneGroup : List<ContextMenuEntry>
{
    public string Key { get; }

    public CmSceneGroup(string key, IEnumerable<ContextMenuEntry> items)
        : base(items)
    {
        Key = key;
    }
}

/// <summary>左侧“场景分类”导航项（Scene="" 表示全部场景）。</summary>
internal sealed class CmSceneNavVm
{
    public string Key { get; init; } = "";
    public string Scene { get; init; } = "";
    public int Count { get; init; }
}

/// <summary>「流氓软件的克星」内置工具页面（移植自 RogueCleaner，MIT）。</summary>
public sealed partial class RogueCleanerPage : Page
{
    private readonly DataStore _store = DataStore.CreateDefault();
    private readonly ScannerEngine _scanner = new();
    private readonly CleanerEngine _cleaner;
    private List<Finding> _allFindings = [];
    private string _filter = "popup";
    private CancellationTokenSource? _cts;
    private bool _scanning;
    private bool _suppressRender;
    private bool _hasScanned;
    private bool _startupAllMode;
    private bool _findingsAllMode;
    private Finding? _flyoutFinding;

    // 软件图标缓存（原版结果行展示软件图标）
    private readonly Dictionary<int, BitmapImage> _findingIcons = [];
    private readonly Dictionary<string, BitmapImage> _menuIcons = [];

    // 统计
    private int _statFound;
    private int _statSuggested;
    private int _statManageable;
    private int _statReportOnly;

    // 右键菜单管理
    private bool _cmAllMode;
    private string _cmSearchKeyword = "";
    private string _cmSceneFilter = ""; // 左侧“场景分类”选中的场景，空 = 全部
    private bool _syncingCmSceneNav;
    private List<ContextMenuEntry> _cmEntries = [];
    private List<SpecialMenuEntry> _specialEntries = [];
    private List<AdvancedMenuEntry> _advancedEntries = [];
    private List<CleanupBatch> _batches = [];
    // 刷新代数：新一轮刷新开始时递增；旧一轮的延迟水合重绘/图标转换携带旧代数，直接丢弃，
    // 避免新旧数据交错导致列表反复重建（"列表无限刷新"）。
    private int _cmRefreshGeneration;
    private int _specialRefreshGeneration;
    private int _advancedRefreshGeneration;

    public RogueCleanerPage()
    {
        InitializeComponent();
        _cleaner = new CleanerEngine(_store);
        _store.Ensure();
        Logger.Initialize(_store);
        Loaded += OnLoaded;

        // MSIX 沙箱下不支持主动拦截后端，隐藏导航项
        if (RuntimeHelper.IsMsixPackaged)
        {
            NavActiveIntercept.Visibility = Visibility.Collapsed;
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (Nav.SelectedItem is null) Nav.SelectedItem = NavContextMenu;
        BuildStatCards();
        RefreshContextMenus();
        RefreshRecovery();
        // 进入页面自动扫描一次；之后点「刷新」重新扫描
        ScanNow();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is string target && target == "contextmenu")
        {
            Nav.SelectedItem = NavContextMenu;
        }
        else if (e.Parameter is string target2 && target2 == "activeintercept")
        {
            Nav.SelectedItem = NavActiveIntercept;
        }
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _cts?.Cancel();
        StopAiPolling();
        RemoveAiSubscriptions();
    }

    #region 导航

    private void Nav_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        var tag = (args.SelectedItem as NavigationViewItem)?.Tag as string ?? "contextmenu";
        bool scanPanel = tag is "popup";
        ScanPanel.Visibility = scanPanel ? Visibility.Visible : Visibility.Collapsed;
        ContextMenuPanel.Visibility = tag == "contextmenu" ? Visibility.Visible : Visibility.Collapsed;
        ActiveInterceptPanel.Visibility = tag == "activeintercept" ? Visibility.Visible : Visibility.Collapsed;
        RecoveryPanel.Visibility = tag == "recovery" ? Visibility.Visible : Visibility.Collapsed;
        if (scanPanel)
        {
            _filter = tag;
            _findingsAllMode = false;
            RenderFindings();
        }
        else if (tag == "activeintercept")
        {
            RefreshActiveIntercept();
        }
    }

    private List<Finding> FilteredFindings()
    {
        if (_findingsAllMode) return _allFindings;
        if (_filter == "popup")
        {
            return _allFindings.Where(RogueCleanerViewFilters.MatchesPopupTab).ToList();
        }
        return _allFindings;
    }

    #endregion

#region 主动拦截（后端审核）

    // ================= 条目视图模型（前台渲染，勾选计数实时刷新） =================

    /// <summary>拦截条目 UI 模型：包装后端 InterceptItemDto，提供显示属性与勾选状态。</summary>
    public sealed class AiItemVm : INotifyPropertyChanged
    {
        private static readonly SolidColorBrush BrPending = new(Color.FromArgb(255, 79, 124, 255));
        private static readonly SolidColorBrush BrBlocked = new(Color.FromArgb(255, 196, 43, 28));
        private static readonly SolidColorBrush BrAllowed = new(Color.FromArgb(255, 15, 123, 15));
        private static readonly SolidColorBrush BrIgnored = new(Color.FromArgb(255, 138, 143, 152));
        private static readonly SolidColorBrush BrDeleted = new(Color.FromArgb(255, 110, 112, 120));
        private static readonly SolidColorBrush BrNone = new(Color.FromArgb(255, 107, 107, 107));

        public AiItemVm(InterceptItemDto dto)
        {
            Dto = dto;
        }

        public InterceptItemDto Dto { get; private set; }

        public string Id => Dto.Id;
        public string Name => Dto.Name;
        public string SubKey => Dto.SubKey;
        public string ExePath => Dto.ExePath;
        public string Command => Dto.Command;
        public string Note => Dto.Note;
        public string Clsid => Dto.Clsid;

        public bool IsPending => Dto.IsPendingApproval && !Dto.IsDeleted;
        public bool IsDeleted => Dto.IsDeleted;
        public bool IsIgnored => Dto.IsIgnored;
        public bool HasBackup => Dto.HasBackup;

        /// <summary>程序图标（异步加载后设置，行内就地更新，不触发整表重建）。</summary>
        private ImageSource? _iconDisplay;

        public ImageSource? IconDisplay
        {
            get => _iconDisplay;
            set
            {
                if (ReferenceEquals(_iconDisplay, value)) return;
                _iconDisplay = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IconDisplay)));
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IconFallbackVisibility)));
            }
        }

        /// <summary>图标未加载出来时显示的占位图标（现代菜单等多数字标无法提取时兜底）。</summary>
        public Visibility IconFallbackVisibility => _iconDisplay is null
            ? Visibility.Visible
            : Visibility.Collapsed;

        /// <summary>图标是否已发起加载（避免每次刷新重复请求）。</summary>
        public bool IconRequested { get; set; }

        /// <summary>用新 DTO 就地同步（触发全部显示属性刷新，勾选状态保留）。</summary>
        public void UpdateFrom(InterceptItemDto dto)
        {
            Dto = dto;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(string.Empty));
        }

        /// <summary>是否现代菜单（Windows 11 新右键菜单 / AppX 打包应用扩展，来自后端扫描分类）。
        /// 现代菜单行不参与勾选/批量操作（复选框隐藏），并默认放行。</summary>
        public bool IsModernMenu => Dto.IsModernMenu;

        /// <summary>现代菜单与已停止追踪的行不参与勾选/批量操作（复选框隐藏）。</summary>
        public Visibility CheckVisibility => IsModernMenu || IsIgnored
            ? Visibility.Collapsed
            : Visibility.Visible;

        public string StateText => IsIgnored ? "已停止追踪" : IsDeleted ? "已删除" : IsPending ? "待审核" : Dto.DesiredState switch
        {
            "blocked" => "已拦截",
            "allowed" => "已放行",
            _ => "未审核",
        };

        public SolidColorBrush StateBrush => IsIgnored ? BrIgnored : IsDeleted ? BrDeleted : IsPending
            ? BrPending
            : Dto.DesiredState switch
            {
                "blocked" => BrBlocked,
                "allowed" => BrAllowed,
                _ => BrNone,
            };

        public string PendingChipText => Dto.PendingChangeKind == "reappeared" ? "重现待审" : "新增待审";

        public Visibility PendingChipVisibility => IsPending ? Visibility.Visible : Visibility.Collapsed;

        public Visibility IssueVisibility => string.IsNullOrWhiteSpace(Dto.ConsistencyIssue)
            ? Visibility.Collapsed
            : Visibility.Visible;

        public string SubKeyShort
        {
            get
            {
                if (string.IsNullOrEmpty(Dto.SubKey)) return "";
                return Dto.SubKey.Length <= 60 ? Dto.SubKey : "…" + Dto.SubKey[^60..];
            }
        }

        public string ExeFileName
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Dto.ExePath)) return "";
                try { return Path.GetFileName(Dto.ExePath); } catch { return Dto.ExePath; }
            }
        }

        public string TimeText => LocalTime(Dto.UpdatedAtUtc, "MM-dd HH:mm");

        public string FirstSeenText => LocalTime(Dto.FirstSeenUtc, "yyyy-MM-dd HH:mm");

        private static string LocalTime(string utc, string format)
        {
            if (DateTime.TryParse(utc, null, DateTimeStyles.RoundtripKind, out var dt))
            {
                return dt.ToLocalTime().ToString(format);
            }
            return utc;
        }

        private bool _selected;

        public bool Selected
        {
            get => _selected;
            set
            {
                if (_selected == value) return;
                _selected = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Selected)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
    }

    // ================= 页面状态 =================

    private List<AiItemVm> _aiItemVms = [];
    private List<AiItemVm> _aiUserVms = [];
    private List<AiItemVm> _aiModernVms = [];
    private List<InterceptEventDto> _aiEventVms = [];

    /// <summary>操作记录绑定集合：就地增量增删（绝不整表重建，杜绝列表闪烁）。</summary>
    private readonly ObservableCollection<InterceptEventDto> _aiEventCollection = [];
    private string _aiView = "items";
    private string _aiFilter = "all";
    private string _aiSearch = "";
    private AiItemVm? _aiSelectedItem;
    private InterceptEventDto? _aiSelectedEvent;
    private AiItemVm? _aiContextItem;
    private InterceptEventDto? _aiContextEvent;
    private bool _aiPageActive;
    private bool _aiBusy;
    private bool _aiSuppressSelection;
    private bool _aiSubscribed;
    private bool _aiStartupInitializing;
    private CancellationTokenSource? _aiRefreshCts;

    // ================= 生命周期与订阅 =================

    private void EnsureAiSubscriptions()
    {
        if (_aiSubscribed) return;
        _aiSubscribed = true;
        InterceptWorkspace.ItemsChanged += AiOnWorkspaceItemsChanged;
        InterceptWorkspace.EventsChanged += AiOnWorkspaceEventsChanged;
        InterceptWorkspace.PendingApprovalDetected += AiOnPendingDetected;
        InterceptWorkspace.ServiceAttention += AiOnServiceAttention;
        InterceptWorkspace.ConnectionChanged += AiOnConnectionChanged;
    }

    /// <summary>离开页面时退订静态工作区事件，防止页面实例被静态事件持有而泄漏。</summary>
    private void RemoveAiSubscriptions()
    {
        if (!_aiSubscribed) return;
        _aiSubscribed = false;
        InterceptWorkspace.ItemsChanged -= AiOnWorkspaceItemsChanged;
        InterceptWorkspace.EventsChanged -= AiOnWorkspaceEventsChanged;
        InterceptWorkspace.PendingApprovalDetected -= AiOnPendingDetected;
        InterceptWorkspace.ServiceAttention -= AiOnServiceAttention;
        InterceptWorkspace.ConnectionChanged -= AiOnConnectionChanged;
    }

    /// <summary>导航到「主动拦截」时调用（也由 Nav_SelectionChanged 触发）。</summary>
    private void RefreshActiveIntercept()
    {
        if (ActiveInterceptPanel is null) return;
        _aiPageActive = true;
        EnsureAiSubscriptions();

        // 非管理员模式下显示遮罩，阻止使用
        if (!AdminUtil.IsAdministrator())
        {
            AiAdminMask.Visibility = Visibility.Visible;
            return;
        }
        AiAdminMask.Visibility = Visibility.Collapsed;

        var enabled = AppSettings.GetBool("ActiveInterceptEnabled", false);
        var running = ActiveInterceptService.IsRunning;

        if (enabled)
        {
            AiEnableBackendBtn.Visibility = Visibility.Collapsed;
            AiDisableBackendBtn.Visibility = Visibility.Visible;
            if (running)
            {
                AiStatusDot.Fill = new SolidColorBrush(Color.FromArgb(255, 15, 157, 88));
                AiRunningText.Text = "主动拦截后端：运行中";
                CloseAiStatus();
                // 后端已在运行但工作区未初始化（如开机自启场景），补初始化管道连接
                InterceptWorkspace.Initialize(DispatcherQueue);
            }
            else
            {
                AiStatusDot.Fill = new SolidColorBrush(Color.FromArgb(255, 234, 88, 12));
                AiRunningText.Text = "主动拦截后端：未运行";
                ShowAiStatus("主动拦截后端未在运行，请点击「启用主动拦截」启动常驻后端。", InfoBarSeverity.Warning);
            }
        }
        else
        {
            AiStatusDot.Fill = new SolidColorBrush(Color.FromArgb(255, 138, 143, 152));
            AiRunningText.Text = "主动拦截后端：已关闭";
            AiDisableBackendBtn.Visibility = Visibility.Collapsed;
            AiEnableBackendBtn.Visibility = Visibility.Visible;
            ShowAiStatus("主动拦截后端已关闭：新增第三方右键菜单不会被自动拦截。可在上方或设置页开启。", InfoBarSeverity.Warning);
        }

        _ = AiRefreshStartupStateAsync();
        _ = AiRefreshAllAsync(quiet: false);
    }

    /// <summary>读取开机自启计划任务状态并刷新开关 UI。</summary>
    private async Task AiRefreshStartupStateAsync()
    {
        if (AiStartupToggle is null) return;
        try
        {
            var type = await ActiveInterceptStartupService.GetStartupTypeAsync();
            var on = type != ActiveInterceptStartupService.StartupType.None;
            _aiStartupInitializing = true;
            AiStartupToggle.IsOn = on;
            _aiStartupInitializing = false;
            AiStartupHint.Text = on
                ? $"计划任务：{ActiveInterceptStartupService.ScheduleTaskName}"
                : "未配置开机自启";
        }
        catch (Exception ex)
        {
            AiStartupHint.Text = $"读取开机自启状态失败：{ex.Message}";
        }
    }

    private async void AiStartupToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_aiStartupInitializing) return;
        var desired = AiStartupToggle.IsOn;
        AiStartupToggle.IsEnabled = false;
        try
        {
            var ok = await ActiveInterceptStartupService.SetStartupEnabledAsync(desired);
            if (ok)
            {
                AiStartupHint.Text = desired
                    ? $"计划任务：{ActiveInterceptStartupService.ScheduleTaskName}"
                    : "未配置开机自启";
                ShowAiStatus(desired ? "已设置主动拦截后端开机自启。" : "已取消主动拦截后端开机自启。", InfoBarSeverity.Success);
            }
            else
            {
                AiStartupToggle.IsOn = !desired;
                AiStartupHint.Text = desired
                    ? "开机自启设置失败：后端程序缺失，或计划任务创建未成功（需要管理员权限）。"
                    : "取消失败：计划任务删除未成功。";
                ShowAiStatus(desired ? "开机自启设置失败。" : "取消开机自启失败。", InfoBarSeverity.Error);
            }
        }
        catch (Exception ex)
        {
            AiStartupToggle.IsOn = !desired;
            AiStartupHint.Text = $"操作失败：{ex.Message}";
            ShowAiStatus("操作开机自启失败。", InfoBarSeverity.Error);
        }
        finally
        {
            AiStartupToggle.IsEnabled = true;
        }
    }

    /// <summary>页面离开时调用：停止活动状态（托管于 OnNavigatedFrom）。</summary>
    private void StopAiPolling()
    {
        _aiPageActive = false;
        _aiRefreshCts?.Cancel();
    }

    private void AiOnWorkspaceItemsChanged(object? sender, EventArgs e)
    {
        if (_aiPageActive) AiScheduleRefresh();
    }

    private void AiOnWorkspaceEventsChanged(object? sender, EventArgs e)
    {
        if (_aiPageActive) AiScheduleRefresh();
    }

    private void AiOnPendingDetected(object? sender, InterceptItemDto item)
    {
        if (!_aiPageActive) return;
        ShowAiStatus($"检测到新的待审核项：{item.Name}，已拦截（先拦截后审核）", InfoBarSeverity.Informational);
        AiScheduleRefresh();
    }

    private void AiOnServiceAttention(object? sender, string message)
    {
        if (_aiPageActive) ShowAiStatus(message, InfoBarSeverity.Warning);
    }

    private void AiOnConnectionChanged(object? sender, bool connected)
    {
        if (!_aiPageActive) return;
        if (connected)
        {
            CloseAiStatus();
            AiStatusDot.Fill = new SolidColorBrush(Color.FromArgb(255, 15, 157, 88));
            AiRunningText.Text = "主动拦截后端：运行中";
        }
        else
        {
            AiStatusDot.Fill = new SolidColorBrush(Color.FromArgb(255, 234, 88, 12));
            AiRunningText.Text = "主动拦截后端：连接中断";
        }
        _ = AiRefreshAllAsync(quiet: true);
    }

    private void AiScheduleRefresh()
    {
        _aiRefreshCts?.Cancel();
        var cts = new CancellationTokenSource();
        _aiRefreshCts = cts;
        _ = Task.Delay(250).ContinueWith(_ =>
        {
            if (cts.IsCancellationRequested) return;
            DispatcherQueue.TryEnqueue(() =>
            {
                if (_aiPageActive && !_aiBusy) _ = AiRefreshAllAsync(quiet: true);
            });
        });
    }

    // ================= 刷新 =================

    private async Task AiRefreshAllAsync(bool quiet)
    {
        if (_aiBusy)
        {
            AiScheduleRefresh();
            return;
        }
        _aiBusy = true;
        try
        {
            var ok = await InterceptWorkspace.RefreshAsync();
            if (ok)
            {
                CloseAiStatus();
            }
            else if (!quiet)
            {
                ShowAiStatus("无法连接主动拦截后端（命名管道未就绪），请确认后端已启用。", InfoBarSeverity.Warning);
            }
            ApplyAiView();
        }
        finally
        {
            _aiBusy = false;
        }
    }

    private void AiRefresh_Click(object sender, RoutedEventArgs e) => _ = AiRefreshAllAsync(quiet: false);

    // ================= 列表构建 / 筛选 / 搜索 =================

    private void ApplyAiView()
    {
        // 1) 条目：就地同步（复用 VM 实例 → 行内 INPC 更新，绝不整表重建，杜绝闪烁）
        var existingItems = _aiItemVms.ToDictionary(v => v.Id, StringComparer.OrdinalIgnoreCase);
        var removedIds = new HashSet<string>(existingItems.Keys, StringComparer.OrdinalIgnoreCase);
        var nextItems = new List<AiItemVm>(InterceptWorkspace.Items.Count);
        foreach (var dto in InterceptWorkspace.Items)
        {
            removedIds.Remove(dto.Id);
            if (existingItems.TryGetValue(dto.Id, out var vm))
            {
                vm.UpdateFrom(dto);
                nextItems.Add(vm);
            }
            else
            {
                var created = new AiItemVm(dto);
                created.PropertyChanged += AiOnItemVmPropertyChanged;
                nextItems.Add(created);
            }
        }
        foreach (var id in removedIds)
        {
            if (existingItems.TryGetValue(id, out var gone))
            {
                gone.PropertyChanged -= AiOnItemVmPropertyChanged;
            }
        }
        _aiItemVms = nextItems;
        _aiUserVms = _aiItemVms.Where(v => !v.IsModernMenu).ToList();
        _aiModernVms = _aiItemVms.Where(v => v.IsModernMenu).ToList();

        // 2) 操作记录：同样复用 DTO 实例（保留勾选），行内 CopyFrom 更新
        var existingEvents = _aiEventVms.ToDictionary(ev => ev.RowId, StringComparer.OrdinalIgnoreCase);
        var removedEventIds = new HashSet<string>(existingEvents.Keys, StringComparer.OrdinalIgnoreCase);
        var nextEvents = new List<InterceptEventDto>(InterceptWorkspace.Events.Count);
        foreach (var ev in InterceptWorkspace.Events)
        {
            removedEventIds.Remove(ev.RowId);
            if (existingEvents.TryGetValue(ev.RowId, out var old))
            {
                old.CopyFrom(ev);
                nextEvents.Add(old);
            }
            else
            {
                nextEvents.Add(ev);
            }
        }
        _aiEventVms = nextEvents;

        // 3) 可见列表：操作记录走就地增量同步（仅增删真正变化的行，绝不再整体重建）；
        //    拦截列表 = 现代菜单在上 + 分界线 + 非现代菜单在下，合并进同一个列表；
        //    仅当内容（Id / 分区 Key 序列）真正变化才重新绑定，否则行内更新即可
        var visibleEvents = FilteredEvents();
        if (AiEventList.ItemsSource is null)
        {
            AiEventList.ItemsSource = _aiEventCollection;
        }
        AiSyncEventRows(visibleEvents);

        var visibleItems = AiBuildItemDisplay(FilteredItemVms(), FilteredModernVms());
        if (!SameItemKeys(AiItemList.ItemsSource as System.Collections.IList, visibleItems))
        {
            AiItemList.ItemsSource = visibleItems;
        }

        // 4) 选中修复（实例复用后引用天然保持；仅当选中项被筛选掉时清除）
        _aiSuppressSelection = true;
        try
        {
            if (_aiView == "items")
            {
                if (_aiSelectedItem is not null && !visibleItems.Any(v => ReferenceEquals(v, _aiSelectedItem)))
                {
                    AiItemList.SelectedItem = null;
                    _aiSelectedItem = null;
                }
                else if (_aiSelectedItem is null && visibleItems.Count > 0 && AiItemList.SelectedItem is null)
                {
                    // 跳过分区标题行，选中第一条真实条目
                    var firstSelectable = visibleItems.OfType<AiItemVm>().FirstOrDefault();
                    if (firstSelectable is not null) AiItemList.SelectedItem = firstSelectable;
                }
            }
            else
            {
                if (_aiSelectedEvent is not null && !visibleEvents.Any(ev => ReferenceEquals(ev, _aiSelectedEvent)))
                {
                    AiEventList.SelectedItem = null;
                    _aiSelectedEvent = null;
                }
                else if (_aiSelectedEvent is null && visibleEvents.Count > 0 && AiEventList.SelectedItem is null)
                {
                    AiEventList.SelectedItem = visibleEvents[0];
                }
            }
        }
        finally
        {
            _aiSuppressSelection = false;
        }

        // 5) 视图可见性
        AiItemsSection.Visibility = _aiView == "items" ? Visibility.Visible : Visibility.Collapsed;
        AiItemList.Visibility = _aiView == "items" ? Visibility.Visible : Visibility.Collapsed;
        AiEventList.Visibility = _aiView == "events" ? Visibility.Visible : Visibility.Collapsed;
        AiFilterBar.Visibility = _aiView == "items" ? Visibility.Visible : Visibility.Collapsed;
        AiStateHeader.Text = _aiView == "items" ? "状态" : "动作";

        int shown = _aiView == "items" ? visibleItems.Count : visibleEvents.Count;
        AiEmptyText.Visibility = shown == 0 ? Visibility.Visible : Visibility.Collapsed;
        AiEmptyText.Text = _aiView == "items"
            ? (InterceptWorkspace.Items.Count == 0 ? "暂无拦截记录：新增右键项会自动进入这里等待审核" : "没有符合筛选条件的拦截项")
            : (InterceptWorkspace.Events.Count == 0 ? "暂无操作记录" : "没有符合筛选条件的记录");

        // 6) 详情同步 + 图标（仅新条目发起加载）
        if (_aiView == "items")
        {
            if (_aiSelectedItem is not null)
            {
                AiRenderDetail(_aiSelectedItem);
            }
            else
            {
                AiRenderDetail(null);
            }
            foreach (var vm in _aiItemVms)
            {
                if (!vm.IconRequested)
                {
                    vm.IconRequested = true;
                    _ = AiEnsureIconAsync(vm);
                }
            }
        }
        else
        {
            if (_aiSelectedEvent is not null)
            {
                AiRenderEventDetail(_aiSelectedEvent);
            }
            else
            {
                AiRenderEventDetail(null);
            }
        }

        UpdateAiSummary();
    }

    /// <summary>两个条目列表（含分区标题行）的 Key 序列是否完全一致（一致则无需重绑定）。</summary>
    private static bool SameItemKeys(System.Collections.IList? current, List<object> next)
    {
        if (current is null || current.Count != next.Count) return false;
        for (int i = 0; i < current.Count; i++)
        {
            var a = KeyOf(current[i]);
            var b = KeyOf(next[i]);
            if (!string.Equals(a, b, StringComparison.OrdinalIgnoreCase)) return false;
        }
        return true;
    }

    private static string KeyOf(object row)
        => row switch
        {
            AiItemVm vm => vm.Id,
            AiSectionHeaderVm h => h.Key,
            _ => "",
        };

    /// <summary>操作记录就地增量同步：只增删/移动真正变化的行，绝不整表重建（杜绝 ListView 重新实例化导致的闪烁）。</summary>
    private void AiSyncEventRows(List<InterceptEventDto> next)
    {
        var current = _aiEventCollection;

        // 1) 删除已不在集合中的行（倒序删除，避免索引位移）
        var nextKeys = new HashSet<string>(next.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var e in next) nextKeys.Add(e.RowId);
        for (int i = current.Count - 1; i >= 0; i--)
        {
            if (!nextKeys.Contains(current[i].RowId)) current.RemoveAt(i);
        }

        // 2) 逐位对齐：缺失行插入、乱序行移动（增量操作，List 本身绝不被替换）
        for (int i = 0; i < next.Count; i++)
        {
            if (i < current.Count
                && string.Equals(current[i].RowId, next[i].RowId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            int src = -1;
            for (int k = i; k < current.Count; k++)
            {
                if (string.Equals(current[k].RowId, next[i].RowId, StringComparison.OrdinalIgnoreCase))
                {
                    src = k;
                    break;
                }
            }
            if (src >= 0)
            {
                var dto = current[src];
                current.RemoveAt(src);
                current.Insert(i, dto);
            }
            else
            {
                current.Insert(i, next[i]);
            }
        }
        while (current.Count > next.Count) current.RemoveAt(current.Count - 1);
    }

    // ================= 程序图标（ToolIconService 缓存 PNG，异步加载后行内赋值） =================

    private readonly Dictionary<string, BitmapImage> _aiIcons = [];

    private async Task AiEnsureIconAsync(AiItemVm vm)
    {
        if (vm.IconDisplay is not null) return;
        var exe = vm.ExePath;
        if (string.IsNullOrWhiteSpace(exe)) return;

        if (!_aiIcons.TryGetValue(exe, out var bmp))
        {
            // 现代菜单/AppX 包的 ExePath 是包目录：走 manifest Logo；其余走 exe 图标提取
            bmp = Directory.Exists(exe)
                ? await AiLoadPackageIconAsync(exe)
                : await AiLoadIconAsync(exe);
            if (bmp is null)
            {
                _aiIcons[exe] = null!;
                return;
            }
            _aiIcons[exe] = bmp;
        }
        if (bmp is not null && vm.IconDisplay is null)
        {
            vm.IconDisplay = bmp;
        }
    }

    private static async Task<BitmapImage?> AiLoadIconAsync(string exePath)
    {
        try
        {
            var cached = ToolIconService.GetCachedIconPath(exePath);
            if (string.IsNullOrEmpty(cached))
            {
                cached = await ToolIconService.ExtractIconToCacheAsync(exePath);
            }
            if (string.IsNullOrEmpty(cached) || !File.Exists(cached)) return null;

            var bitmap = new BitmapImage();
            using var stream = File.OpenRead(cached);
            await bitmap.SetSourceAsync(stream.AsRandomAccessStream());
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>从 AppX 包目录读取应用 Logo（AppxManifest.xml Properties/Logo，含 scale 变体兜底）。</summary>
    private static async Task<BitmapImage?> AiLoadPackageIconAsync(string packageDir)
    {
        try
        {
            var logo = ResolveAppxLogoPath(packageDir);
            if (string.IsNullOrEmpty(logo) || !File.Exists(logo)) return null;

            var bitmap = new BitmapImage();
            using var stream = File.OpenRead(logo);
            await bitmap.SetSourceAsync(stream.AsRandomAccessStream());
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    private static string ResolveAppxLogoPath(string packageDir)
    {
        try
        {
            var manifest = Path.Combine(packageDir, "AppxManifest.xml");
            if (!File.Exists(manifest)) return "";

            var xml = new System.Xml.XmlDocument { XmlResolver = null };
            xml.Load(manifest);
            var logoNode = xml.SelectSingleNode(
                "/*[local-name()='Package']/*[local-name()='Properties']/*[local-name()='Logo']");
            var relative = logoNode?.InnerText?.Trim() ?? "";
            if (!string.IsNullOrEmpty(relative))
            {
                var direct = Path.Combine(packageDir, relative);
                if (File.Exists(direct)) return direct;

                // 资源限定符变体：StoreLogo.scale-100/200/400.png 等
                var dir = Path.GetDirectoryName(relative) ?? "";
                var name = Path.GetFileNameWithoutExtension(relative);
                var ext = Path.GetExtension(relative);
                foreach (var scale in new[] { ".scale-100", ".scale-200", ".scale-400", ".scale-125", ".scale-150" })
                {
                    var variant = Path.Combine(packageDir, dir, name + scale + ext);
                    if (File.Exists(variant)) return variant;
                }
            }

            // 兜底：Assets 目录里任意的 Logo/Store 型图片
            var assets = Path.Combine(packageDir, "Assets");
            if (Directory.Exists(assets))
            {
                var images = Directory.EnumerateFiles(assets, "*", SearchOption.TopDirectoryOnly)
                    .Where(f => f.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
                             || f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
                             || f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                var byName = images.FirstOrDefault(f =>
                {
                    var n = Path.GetFileName(f);
                    return n.Contains("logo", StringComparison.OrdinalIgnoreCase)
                        || n.Contains("store", StringComparison.OrdinalIgnoreCase);
                });
                if (byName is not null) return byName;
                return images.FirstOrDefault() ?? "";
            }
        }
        catch
        {
        }
        return "";
    }

    private List<AiItemVm> FilteredItemVms()
    {
        IEnumerable<AiItemVm> q = _aiUserVms;
        q = _aiFilter switch
        {
            "pending" => q.Where(v => v.IsPending),
            "blocked" => q.Where(v => !v.IsPending && !v.IsDeleted && !v.IsIgnored && v.Dto.DesiredState == "blocked"),
            "allowed" => q.Where(v => !v.IsDeleted && !v.IsIgnored && v.Dto.DesiredState == "allowed"),
            "ignored" => q.Where(v => v.IsIgnored),
            "deleted" => q.Where(v => v.IsDeleted),
            _ => q,
        };

        if (!string.IsNullOrWhiteSpace(_aiSearch))
        {
            var kw = _aiSearch.Trim();
            q = q.Where(v => ContainsIgnoreCase(v.Name, kw)
                             || ContainsIgnoreCase(v.SubKey, kw)
                             || ContainsIgnoreCase(v.ExePath, kw)
                             || ContainsIgnoreCase(v.ExeFileName, kw)
                             || ContainsIgnoreCase(v.Command, kw));
        }
        return q.ToList();
    }

    /// <summary>现代菜单的筛选搜索（与 FilteredItemVms 同规则）。</summary>
    private List<AiItemVm> FilteredModernVms()
    {
        IEnumerable<AiItemVm> q = _aiModernVms;
        q = _aiFilter switch
        {
            "pending" => q.Where(v => v.IsPending),
            "blocked" => q.Where(v => !v.IsPending && !v.IsDeleted && !v.IsIgnored && v.Dto.DesiredState == "blocked"),
            "allowed" => q.Where(v => !v.IsDeleted && !v.IsIgnored && v.Dto.DesiredState == "allowed"),
            "ignored" => q.Where(v => v.IsIgnored),
            "deleted" => q.Where(v => v.IsDeleted),
            _ => q,
        };
        if (!string.IsNullOrWhiteSpace(_aiSearch))
        {
            var kw = _aiSearch.Trim();
            q = q.Where(v => ContainsIgnoreCase(v.Name, kw)
                             || ContainsIgnoreCase(v.SubKey, kw)
                             || ContainsIgnoreCase(v.ExePath, kw)
                             || ContainsIgnoreCase(v.ExeFileName, kw)
                             || ContainsIgnoreCase(v.Command, kw));
        }
        return q.ToList();
    }

    /// <summary>合并拦截列表：现代菜单在上 + 分界线 + 非现代菜单在下（同一个列表）。</summary>
    private static List<object> AiBuildItemDisplay(List<AiItemVm> classic, List<AiItemVm> modern)
    {
        var rows = new List<object>();
        if (modern.Count > 0)
        {
            rows.Add(new AiSectionHeaderVm { Key = "header:modern", Text = $"现代菜单（{modern.Count}）· 默认放行" });
            rows.AddRange(modern);
        }
        if (classic.Count > 0)
        {
            rows.Add(new AiSectionHeaderVm { Key = "header:classic", Text = $"非现代菜单（{classic.Count}）" });
            rows.AddRange(classic);
        }
        return rows;
    }

    private List<InterceptEventDto> FilteredEvents()
    {
        IEnumerable<InterceptEventDto> q = _aiEventVms;
        if (!string.IsNullOrWhiteSpace(_aiSearch))
        {
            var kw = _aiSearch.Trim();
            q = q.Where(e => ContainsIgnoreCase(e.Name, kw)
                             || ContainsIgnoreCase(e.SubKey, kw)
                             || ContainsIgnoreCase(e.ExePath, kw)
                             || ContainsIgnoreCase(e.Command, kw));
        }
        return q.ToList();
    }

    private static bool ContainsIgnoreCase(string? source, string keyword)
        => source is not null && source.Contains(keyword, StringComparison.OrdinalIgnoreCase);

    // ================= 计数 / 勾选联动 =================

    private void UpdateAiSummary()
    {
        var items = _aiUserVms;
        int pending = items.Count(v => v.IsPending);
        int blocked = items.Count(v => !v.IsPending && !v.IsDeleted && !v.IsIgnored && v.Dto.DesiredState == "blocked");
        int allowed = items.Count(v => !v.IsDeleted && !v.IsIgnored && v.Dto.DesiredState == "allowed");
        int ignored = items.Count(v => v.IsIgnored);
        int deleted = items.Count(v => v.IsDeleted);

        AiCountText.Text = _aiView == "items"
            ? $"条目 {items.Count} · 现代菜单 {_aiModernVms.Count} · 待审核 {pending} · 已拦截 {blocked} · 已放行 {allowed} · 已停止追踪 {ignored} · 已删除 {deleted}"
            : $"操作记录 {InterceptWorkspace.Events.Count}";

        int total;
        int selected;
        if (_aiView == "items")
        {
            var list = FilteredItemVms();
            total = list.Count;
            selected = list.Count(v => v.Selected);
        }
        else
        {
            var visibleRows = FilteredEvents().Select(e => e.RowId).ToHashSet(StringComparer.OrdinalIgnoreCase);
            total = _aiEventVms.Count(e => visibleRows.Contains(e.RowId));
            selected = _aiEventVms.Count(e => e.Selected && visibleRows.Contains(e.RowId));
        }

        AiSelectionPill.Visibility = selected > 0 ? Visibility.Visible : Visibility.Collapsed;
        AiSelectionText.Text = $"已选 {selected} 项";

        // 批量按钮按视图启用
        var canBatch = selected > 0;
        AiBatchAllowBtn.IsEnabled = canBatch && _aiView == "items";
        AiBatchReblockBtn.IsEnabled = canBatch && _aiView == "items";
        AiBatchTrackBtn.IsEnabled = canBatch && _aiView == "items";
        AiBatchDeleteBtn.IsEnabled = canBatch && _aiView == "events";
        AiClearAllBtn.IsEnabled = _aiView == "events" && InterceptWorkspace.Events.Count > 0;
        AiShowIgnoredBtn.Content = $"已停止追踪（{InterceptWorkspace.Ignored.Count}）";

        // 表头全选（三态）
        AiMasterCheck.IsChecked = total == 0 ? false : selected == 0 ? false : selected == total ? true : (bool?)null;
    }

    private void AiOnItemVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AiItemVm.Selected))
        {
            UpdateAiSummary();
        }
    }

    private void AiMasterCheck_Click(object sender, RoutedEventArgs e)
    {
        var check = !(AiMasterCheck.IsChecked == true);
        if (_aiView == "items")
        {
            foreach (var vm in FilteredItemVms()) vm.Selected = check;
        }
        else
        {
            foreach (var ev in _aiEventVms.Where(ev => FilteredEvents().Any(ve => string.Equals(ve.RowId, ev.RowId, StringComparison.OrdinalIgnoreCase))))
            {
                ev.Selected = check;
            }
        }
        UpdateAiSummary();
    }

    private void AiSelectAll_Click(object sender, RoutedEventArgs e)
    {
        if (_aiView == "items")
        {
            foreach (var vm in FilteredItemVms()) vm.Selected = true;
        }
        else
        {
            foreach (var ev in _aiEventVms.Where(ev => FilteredEvents().Any(ve => string.Equals(ve.RowId, ev.RowId, StringComparison.OrdinalIgnoreCase))))
            {
                ev.Selected = true;
            }
        }
        UpdateAiSummary();
    }

    private void AiDeselectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var vm in _aiItemVms) vm.Selected = false;
        foreach (var ev in _aiEventVms) ev.Selected = false;
        UpdateAiSummary();
    }

    private void AiViewBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        var tag = (sender.SelectedItem as SelectorBarItem)?.Tag as string ?? "items";
        _aiView = tag;
        ApplyAiView();
    }

    private void AiFilterBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        var tag = (sender.SelectedItem as SelectorBarItem)?.Tag as string ?? "all";
        _aiFilter = tag;
        ApplyAiView();
    }

    private void AiSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _aiSearch = AiSearchBox.Text;
        AiSearchClearBtn.Visibility = string.IsNullOrEmpty(_aiSearch) ? Visibility.Collapsed : Visibility.Visible;
        ApplyAiView();
    }

    private void AiSearchClear_Click(object sender, RoutedEventArgs e)
    {
        AiSearchBox.Text = "";
    }

    // ================= 列表选中 / 右键 =================

    private void AiItemList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_aiSuppressSelection) return;
        // 分区标题行不可选中；只有真实条目才进入详情
        if (AiItemList.SelectedItem is not AiItemVm item) return;
        _aiSelectedItem = item;
        if (_aiView == "items")
        {
            AiRenderDetail(_aiSelectedItem);
        }
    }

    private void AiEventList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_aiSuppressSelection) return;
        _aiSelectedEvent = AiEventList.SelectedItem as InterceptEventDto;
        if (_aiView == "events")
        {
            AiRenderEventDetail(_aiSelectedEvent);
        }
    }

    private void AiItemList_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is AiItemVm vm)
        {
            _aiContextItem = vm;
            AiItemList.SelectedItem = vm;
            AiPrepareItemFlyout(vm);
        }
    }

    /// <summary>按条目状态启用/禁用右键菜单项（主列表与系统列表共用）。</summary>
    private void AiPrepareItemFlyout(AiItemVm vm)
    {
        var canReview = !vm.IsDeleted && !vm.IsIgnored;
        // 待审核（先拦截后审核）与已拦截项均可直接放行；已放行项显示「保持拦截」
        var allowable = canReview && (vm.IsPending || (!vm.IsPending && vm.Dto.DesiredState == "blocked"));
        AiFlyAllow.IsEnabled = allowable;
        AiFlyTrust.IsEnabled = allowable && !string.IsNullOrWhiteSpace(vm.ExePath);
        AiFlyReblock.IsEnabled = canReview && !vm.IsPending && vm.Dto.DesiredState == "allowed";
        AiFlyDeleteItem.IsEnabled = !vm.IsDeleted && !vm.IsIgnored;
        AiFlyUndo.IsEnabled = vm.IsDeleted && vm.HasBackup;
        AiFlyPurge.IsEnabled = vm.IsDeleted;
        AiFlyIgnore.IsEnabled = !vm.IsIgnored && !vm.IsDeleted;
        AiFlyResume.IsEnabled = vm.IsIgnored;
        AiFlyPolicy.IsEnabled = !string.IsNullOrWhiteSpace(vm.ExePath);
    }

    private void AiEventList_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is InterceptEventDto evt)
        {
            _aiContextEvent = evt;
            AiEventList.SelectedItem = evt;
        }
    }

    // ================= 详情渲染 =================

    private void AiRenderDetail(AiItemVm? vm)
    {
        AiPolicySection.Visibility = Visibility.Collapsed;
        AiDetailActions.Visibility = Visibility.Collapsed;
        if (vm is null)
        {
            AiDetailsText.Text = "选中一条拦截项查看详情";
            return;
        }

        var d = vm.Dto;
        var sb = new StringBuilder();
        sb.AppendLine($"名称：{d.Name}");
        sb.AppendLine($"状态：{vm.StateText}");
        sb.AppendLine($"菜单类型：{(d.IsModernMenu ? "现代菜单（AppX 打包应用）" : "非现代菜单（经典注册表项）")}");
        if (vm.IsPending) sb.AppendLine($"来源：{(d.PendingChangeKind == "reappeared" ? "已删除项重现" : "新增项")}（先拦截后审核）");
        if (!string.IsNullOrWhiteSpace(d.ExePath)) sb.AppendLine($"程序：{d.ExePath}");
        if (!string.IsNullOrWhiteSpace(d.Command)) sb.AppendLine($"命令：{d.Command}");
        if (!string.IsNullOrWhiteSpace(d.Clsid)) sb.AppendLine($"CLSID：{d.Clsid}");
        sb.AppendLine($"注册表：{HiveName(d.Hive)}\\{d.SubKey}");
        if (!string.IsNullOrWhiteSpace(d.ConsistencyIssue)) sb.AppendLine($"提示：{d.ConsistencyIssue}");
        if (!string.IsNullOrWhiteSpace(d.Note)) sb.AppendLine($"说明：{d.Note}");
        sb.AppendLine($"首次出现：{vm.FirstSeenText}");
        sb.AppendLine($"最近更新：{vm.TimeText}");
        if (vm.IsDeleted) sb.AppendLine($"已删除（{(d.HasBackup ? "含备份，可撤销" : "无备份")}）");
        var text = sb.ToString().TrimEnd();
        if (AiDetailsText.Text != text) AiDetailsText.Text = text;

        var canReview = !vm.IsDeleted && !vm.IsIgnored;
        var allowable = canReview && (vm.IsPending || (!vm.IsPending && d.DesiredState == "blocked"));
        AiAllowBtn.Visibility = allowable ? Visibility.Visible : Visibility.Collapsed;
        AiAllowTrustBtn.Visibility = allowable && !string.IsNullOrWhiteSpace(d.ExePath) ? Visibility.Visible : Visibility.Collapsed;
        AiReblockBtn.Visibility = canReview && !vm.IsPending && d.DesiredState == "allowed" ? Visibility.Visible : Visibility.Collapsed;
        AiDeleteItemBtn.Visibility = canReview ? Visibility.Visible : Visibility.Collapsed;
        AiUndoBtn.Visibility = vm.IsDeleted && d.HasBackup ? Visibility.Visible : Visibility.Collapsed;
        AiPurgeBtn.Visibility = vm.IsDeleted ? Visibility.Visible : Visibility.Collapsed;
        AiIgnoreBtn.Visibility = canReview ? Visibility.Visible : Visibility.Collapsed;
        AiResumeBtn.Visibility = vm.IsIgnored ? Visibility.Visible : Visibility.Collapsed;
        AiOpenLocationBtn.Visibility = string.IsNullOrWhiteSpace(d.ExePath) ? Visibility.Collapsed : Visibility.Visible;
        AiOpenRegistryBtn.Visibility = Visibility.Visible;
        AiCopyBtn.Visibility = Visibility.Visible;
        AiDetailActions.Visibility = Visibility.Visible;

        if (!string.IsNullOrWhiteSpace(d.ExePath))
        {
            AiPolicySection.Visibility = Visibility.Visible;
            AiFillPolicyComboBox(d.ExePath);
        }
    }

    private void AiRenderEventDetail(InterceptEventDto? evt)
    {
        AiPolicySection.Visibility = Visibility.Collapsed;
        AiDetailActions.Visibility = Visibility.Collapsed;
        AiAllowBtn.Visibility = Visibility.Collapsed;
        AiAllowTrustBtn.Visibility = Visibility.Collapsed;
        AiReblockBtn.Visibility = Visibility.Collapsed;
        AiDeleteItemBtn.Visibility = Visibility.Collapsed;
        AiUndoBtn.Visibility = Visibility.Collapsed;
        AiPurgeBtn.Visibility = Visibility.Collapsed;
        AiIgnoreBtn.Visibility = Visibility.Collapsed;
        AiResumeBtn.Visibility = Visibility.Collapsed;
        AiOpenLocationBtn.Visibility = Visibility.Collapsed;
        AiOpenRegistryBtn.Visibility = Visibility.Collapsed;
        if (evt is null)
        {
            AiDetailsText.Text = "选中一条操作记录查看详情";
            return;
        }

        var sb = new StringBuilder();
        sb.AppendLine($"动作：{AiEventActionText(evt.Action)}");
        sb.AppendLine($"名称：{evt.Name}");
        if (!string.IsNullOrWhiteSpace(evt.ExePath)) sb.AppendLine($"程序：{evt.ExePath}");
        if (!string.IsNullOrWhiteSpace(evt.Command)) sb.AppendLine($"命令：{evt.Command}");
        sb.AppendLine($"注册表：{HiveName((int)evt.Hive)}\\{evt.SubKey}");
        if (!string.IsNullOrWhiteSpace(evt.Note)) sb.AppendLine($"说明：{evt.Note}");
        sb.AppendLine($"时间：{AiEventTimeText(evt.TimestampUtc)}");
        var text = sb.ToString().TrimEnd();
        if (AiDetailsText.Text != text) AiDetailsText.Text = text;

        AiCopyBtn.Visibility = Visibility.Visible;
        AiDetailActions.Visibility = Visibility.Visible;
    }

    private static string AiEventActionText(string action) => action switch
    {
        "Blocked" => "已拦截",
        "Reblocked" => "自动纠偏/重新拦截",
        "Allowed" => "已放行",
        "Unblocked" => "已解除",
        "BlockedFailed" => "拦截失败",
        "Removed" => "已移除",
        "Restored" => "已撤销恢复",
        "Reappeared" => "已拦截（重现）",
        "Ignored" => "已停止追踪",
        "Tracking" => "已恢复追踪",
        "Pending" => "待审核（新增）",
        "Purged" => "已永久清除",
        "Modified" => "外部修改",
        _ => action,
    };

    private static string AiEventTimeText(string utc)
    {
        if (DateTime.TryParse(utc, null, DateTimeStyles.RoundtripKind, out var dt))
        {
            return dt.ToLocalTime().ToString("yyyy-MM-dd HH:mm:ss");
        }
        return utc;
    }

    private static string HiveName(int hive) => hive == 1 ? "HKLM" : "HKCU";

    // ================= 单项操作 =================

    private bool AiTryGetSelectedItem(out AiItemVm? vm)
    {
        vm = _aiContextItem ?? _aiSelectedItem;
        if (vm is null && AiItemList.SelectedItem is AiItemVm listSelected)
        {
            vm = listSelected;
        }
        return vm is not null;
    }

    private async Task<bool> AiEnsureBackendReadyAsync()
    {
        if (!AppSettings.GetBool("ActiveInterceptEnabled", false) || !ActiveInterceptService.IsRunning)
        {
            ShowAiStatus("主动拦截后端未在运行，请先点击「启用主动拦截」。", InfoBarSeverity.Warning);
            return false;
        }
        return true;
    }

    private async Task AiAllowCoreAsync(AiItemVm vm, bool trust)
    {
        if (!await AiEnsureBackendReadyAsync()) return;
        try
        {
            if (trust)
            {
                var name = string.IsNullOrWhiteSpace(vm.Name) ? vm.ExeFileName : vm.Name;
                if (!await AiConfirmAsync("放行并信任此程序",
                        $"将放行「{name}」，并把该程序（{vm.ExePath}）加入信任名单：以后该程序新增的右键项都会自动放行。\n确定？",
                        "放行并信任"))
                {
                    return;
                }
            }
            await InterceptWorkspace.ApplyDecisionAsync(vm.Id, InterceptDecision.Allow, trust);
            ShowAiStatus(trust ? "已放行并信任此程序" : "已放行", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowAiStatus($"操作失败:{ex.Message}", InfoBarSeverity.Error);
        }
    }

    private async Task AiDenyCoreAsync(AiItemVm vm)
    {
        if (!await AiEnsureBackendReadyAsync()) return;
        try
        {
            await InterceptWorkspace.ApplyDecisionAsync(vm.Id, InterceptDecision.Deny, false);
            ShowAiStatus("已保持拦截（该软件若再改回启用，将自动重新拦截且不打扰您）", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowAiStatus($"操作失败:{ex.Message}", InfoBarSeverity.Error);
        }
    }

    private async Task AiDeleteItemCoreAsync(AiItemVm vm)
    {
        if (!await AiEnsureBackendReadyAsync()) return;
        var name = string.IsNullOrWhiteSpace(vm.Name) ? vm.ExeFileName : vm.Name;
        if (!await AiConfirmAsync("删除条目",
                $"将删除「{name}」的注册表项（删除前自动导出 .reg 备份，可随时撤销）。\n确定删除？",
                "删除"))
        {
            return;
        }
        try
        {
            await InterceptWorkspace.DeleteItemAsync(vm.Id);
            ShowAiStatus("条目已删除（已备份，可在「撤销删除」中恢复）", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowAiStatus($"删除失败:{ex.Message}", InfoBarSeverity.Error);
        }
    }

    private async Task AiUndoCoreAsync(AiItemVm vm)
    {
        if (!await AiEnsureBackendReadyAsync()) return;
        try
        {
            await InterceptWorkspace.UndoDeleteAsync(vm.Id);
            ShowAiStatus("已从备份恢复注册表项，并重新进入待审核", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowAiStatus($"撤销失败:{ex.Message}", InfoBarSeverity.Error);
        }
    }

    private async Task AiPurgeCoreAsync(AiItemVm vm)
    {
        if (!await AiEnsureBackendReadyAsync()) return;
        if (!await AiConfirmAsync("永久清除",
                $"将永久删除「{vm.Name}」的备份文件与状态记录，删除后【不可恢复】。\n确定永久清除？",
                "永久清除"))
        {
            return;
        }
        try
        {
            await InterceptWorkspace.PurgeDeletedItemAsync(vm.Id);
            ShowAiStatus("已永久清除", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowAiStatus($"操作失败:{ex.Message}", InfoBarSeverity.Error);
        }
    }

    private async Task AiIgnoreCoreAsync(AiItemVm vm)
    {
        if (!await AiEnsureBackendReadyAsync()) return;
        if (!await AiConfirmAsync("停止追踪",
                $"将停止追踪「{vm.Name}」：保留注册表现状，不再拦截、不再提醒。\n可在「已停止追踪」管理中恢复。\n确定？",
                "停止追踪"))
        {
            return;
        }
        try
        {
            await InterceptWorkspace.StopTrackingAsync(vm.Id);
            ShowAiStatus("已停止追踪（可在「已停止追踪」中恢复）", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowAiStatus($"操作失败:{ex.Message}", InfoBarSeverity.Error);
        }
    }

    private async Task AiResumeCoreAsync(AiItemVm vm)
    {
        if (!await AiEnsureBackendReadyAsync()) return;
        try
        {
            await InterceptWorkspace.ResumeTrackingAsync(vm.Id);
            ShowAiStatus($"已恢复追踪：{vm.Name}（后续出现将重新进入拦截流程）", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowAiStatus($"操作失败:{ex.Message}", InfoBarSeverity.Error);
        }
    }

    // ---- 详情区按钮 ----

    private async void AiAllow_Click(object sender, RoutedEventArgs e)
    {
        if (AiTryGetSelectedItem(out var vm) && vm is not null) await AiAllowCoreAsync(vm, trust: false);
    }

    private async void AiAllowTrust_Click(object sender, RoutedEventArgs e)
    {
        if (AiTryGetSelectedItem(out var vm) && vm is not null) await AiAllowCoreAsync(vm, trust: true);
    }

    private async void AiReblock_Click(object sender, RoutedEventArgs e)
    {
        if (AiTryGetSelectedItem(out var vm) && vm is not null) await AiDenyCoreAsync(vm);
    }

    private async void AiDeleteItem_Click(object sender, RoutedEventArgs e)
    {
        if (AiTryGetSelectedItem(out var vm) && vm is not null) await AiDeleteItemCoreAsync(vm);
    }

    private async void AiUndo_Click(object sender, RoutedEventArgs e)
    {
        if (AiTryGetSelectedItem(out var vm) && vm is not null) await AiUndoCoreAsync(vm);
    }

    private async void AiPurge_Click(object sender, RoutedEventArgs e)
    {
        if (AiTryGetSelectedItem(out var vm) && vm is not null) await AiPurgeCoreAsync(vm);
    }

    private async void AiIgnore_Click(object sender, RoutedEventArgs e)
    {
        if (AiTryGetSelectedItem(out var vm) && vm is not null) await AiIgnoreCoreAsync(vm);
    }

    private async void AiResume_Click(object sender, RoutedEventArgs e)
    {
        if (AiTryGetSelectedItem(out var vm) && vm is not null) await AiResumeCoreAsync(vm);
    }

    // ---- 右键菜单 ----

    private async void AiFlyAllow_Click(object sender, RoutedEventArgs e)
    {
        if (_aiContextItem is not null) await AiAllowCoreAsync(_aiContextItem, trust: false);
    }

    private async void AiFlyTrust_Click(object sender, RoutedEventArgs e)
    {
        if (_aiContextItem is not null) await AiAllowCoreAsync(_aiContextItem, trust: true);
    }

    private async void AiFlyReblock_Click(object sender, RoutedEventArgs e)
    {
        if (_aiContextItem is not null) await AiDenyCoreAsync(_aiContextItem);
    }

    private async void AiFlyDeleteItem_Click(object sender, RoutedEventArgs e)
    {
        if (_aiContextItem is not null) await AiDeleteItemCoreAsync(_aiContextItem);
    }

    private async void AiFlyUndo_Click(object sender, RoutedEventArgs e)
    {
        if (_aiContextItem is not null) await AiUndoCoreAsync(_aiContextItem);
    }

    private async void AiFlyPurge_Click(object sender, RoutedEventArgs e)
    {
        if (_aiContextItem is not null) await AiPurgeCoreAsync(_aiContextItem);
    }

    private async void AiFlyIgnore_Click(object sender, RoutedEventArgs e)
    {
        if (_aiContextItem is not null) await AiIgnoreCoreAsync(_aiContextItem);
    }

    private async void AiFlyResume_Click(object sender, RoutedEventArgs e)
    {
        if (_aiContextItem is not null) await AiResumeCoreAsync(_aiContextItem);
    }

    private async void AiFlyDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_aiContextEvent is null) return;
        if (!await AiConfirmAsync("删除记录", $"将删除这条操作记录（不影响拦截状态）。\n确定？", "删除")) return;
        try
        {
            await InterceptWorkspace.RemoveEventRowsAsync(new[] { _aiContextEvent.RowId });
            ShowAiStatus("记录已删除", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowAiStatus($"删除失败:{ex.Message}", InfoBarSeverity.Error);
        }
    }

    // ================= 批量操作 =================

    private List<AiItemVm> AiSelectedItemVms()
    {
        var selected = new HashSet<string>(
            FilteredItemVms().Where(v => v.Selected).Select(v => v.Id),
            StringComparer.OrdinalIgnoreCase);
        return _aiItemVms.Where(v => selected.Contains(v.Id)).ToList();
    }

    private List<InterceptEventDto> AiSelectedEvents()
    {
        var visible = FilteredEvents().Select(e => e.RowId).ToHashSet(StringComparer.OrdinalIgnoreCase);
        return _aiEventVms.Where(e => e.Selected && visible.Contains(e.RowId)).ToList();
    }

    private async void AiBatchAllow_Click(object sender, RoutedEventArgs e)
    {
        var vms = AiSelectedItemVms();
        if (vms.Count == 0) return;
        if (!await AiEnsureBackendReadyAsync()) return;
        int ok = 0;
        foreach (var vm in vms)
        {
            try
            {
                await InterceptWorkspace.ApplyDecisionAsync(vm.Id, InterceptDecision.Allow, false);
                ok++;
            }
            catch { }
        }
        ShowAiStatus(ok > 0 ? $"已放行 {ok} 项" : "没有条目被放行", ok > 0 ? InfoBarSeverity.Success : InfoBarSeverity.Error);
    }

    private async void AiBatchReblock_Click(object sender, RoutedEventArgs e)
    {
        var vms = AiSelectedItemVms();
        if (vms.Count == 0) return;
        if (!await AiEnsureBackendReadyAsync()) return;
        int ok = 0;
        foreach (var vm in vms)
        {
            try
            {
                await InterceptWorkspace.ApplyDecisionAsync(vm.Id, InterceptDecision.Deny, false);
                ok++;
            }
            catch { }
        }
        ShowAiStatus(ok > 0 ? $"已保持拦截 {ok} 项" : "没有条目被拦截", ok > 0 ? InfoBarSeverity.Success : InfoBarSeverity.Error);
    }

    private async void AiBatchTrack_Click(object sender, RoutedEventArgs e)
    {
        var vms = AiSelectedItemVms();
        if (vms.Count == 0) return;
        if (!await AiEnsureBackendReadyAsync()) return;
        if (!await AiConfirmAsync("停止追踪",
                $"将停止追踪选中的 {vms.Count} 项：保留注册表现状，不再拦截、不再提醒。\n确定？",
                "停止追踪"))
        {
            return;
        }
        int ok = 0;
        foreach (var vm in vms)
        {
            try
            {
                await InterceptWorkspace.StopTrackingAsync(vm.Id);
                ok++;
            }
            catch { }
        }
        ShowAiStatus(ok > 0 ? $"已停止追踪 {ok} 项" : "没有条目被停止追踪", ok > 0 ? InfoBarSeverity.Success : InfoBarSeverity.Error);
    }

    private async void AiBatchDelete_Click(object sender, RoutedEventArgs e)
    {
        var events = AiSelectedEvents();
        if (events.Count == 0) return;
        if (!await AiEnsureBackendReadyAsync()) return;
        if (!await AiConfirmAsync("删除记录", $"将删除选中的 {events.Count} 条操作记录（不影响拦截状态）。\n确定？", "删除")) return;
        try
        {
            await InterceptWorkspace.RemoveEventRowsAsync(events.Select(ev => ev.RowId));
            ShowAiStatus($"已删除 {events.Count} 条操作记录", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowAiStatus($"删除失败:{ex.Message}", InfoBarSeverity.Error);
        }
    }

    private async void AiClearAll_Click(object sender, RoutedEventArgs e)
    {
        if (!await AiEnsureBackendReadyAsync()) return;
        if (!await AiConfirmAsync("清空记录",
                "将清空全部操作记录（不影响拦截列表与信任策略）。\n确定？",
                "清空")) return;
        try
        {
            await InterceptWorkspace.ClearEventsAsync();
            ShowAiStatus("操作记录已清空", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowAiStatus($"清空失败:{ex.Message}", InfoBarSeverity.Error);
        }
    }

    // ================= 打开位置 / 注册表 / 复制 =================

    private void AiOpenLocation_Click(object sender, RoutedEventArgs e)
    {
        if (!AiTryGetSelectedItem(out var vm) || vm is null) return;
        var raw = vm.ExePath;
        if (string.IsNullOrWhiteSpace(raw)) return;
        try
        {
            var path = Environment.ExpandEnvironmentVariables(raw).Trim();
            if (Directory.Exists(path))
            {
                // 现代菜单/AppX 包目录：直接打开该目录
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{path}\"") { UseShellExecute = true });
                return;
            }
            if (File.Exists(path))
            {
                // 文件：资源管理器中选中它
                Process.Start(new ProcessStartInfo("explorer.exe", $"/select,\"{path}\"") { UseShellExecute = true });
                return;
            }

            // 路径已失效：逐级向上找最近一个存在的目录
            var dir = Path.GetDirectoryName(path);
            while (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
            {
                var parent = Path.GetDirectoryName(dir);
                if (string.IsNullOrWhiteSpace(parent)
                    || string.Equals(parent, dir, StringComparison.OrdinalIgnoreCase)) break;
                dir = parent;
            }
            if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
            {
                Process.Start(new ProcessStartInfo("explorer.exe", $"\"{dir}\"") { UseShellExecute = true });
            }
            else
            {
                ShowAiStatus($"无法打开位置：路径无效（{raw}）", InfoBarSeverity.Warning);
            }
        }
        catch (Exception ex)
        {
            ShowAiStatus($"打开位置失败:{ex.Message}", InfoBarSeverity.Error);
        }
    }

    private void AiOpenRegistry_Click(object sender, RoutedEventArgs e)
    {
        if (!AiTryGetSelectedItem(out var vm) || vm is null) return;
        try
        {
            var hiveText = HiveName(vm.Dto.Hive);
            var full = $"{hiveText}\\{vm.SubKey}";
            // 让 regedit 打开到目标键（ContextMenuMgr 的 LastKey 技巧）
            try
            {
                using var key = Microsoft.Win32.Registry.CurrentUser.CreateSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Applets\Regedit");
                key.SetValue("LastKey", $"计算机\\HKEY_{hiveText}\\{vm.SubKey}", Microsoft.Win32.RegistryValueKind.String);
            }
            catch { }
            Process.Start(new ProcessStartInfo("regedit.exe") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            ShowAiStatus($"打开注册表失败:{ex.Message}", InfoBarSeverity.Error);
        }
    }

    private void AiCopyDetail_Click(object sender, RoutedEventArgs e)
    {
        var text = AiDetailsText.Text;
        if (string.IsNullOrWhiteSpace(text)) return;
        try
        {
            var package = new DataPackage { RequestedOperation = DataPackageOperation.Copy };
            package.SetText(text);
            Clipboard.SetContent(package);
            ShowAiStatus("详情已复制到剪贴板", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowAiStatus($"复制失败:{ex.Message}", InfoBarSeverity.Error);
        }
    }

    // ================= 信任策略 =================

    private string _aiPolicyExe = "";
    private string _aiPolicyCurrent = "";

    private void AiFillPolicyComboBox(string exePath)
    {
        var current = InterceptWorkspace.Policies.TryGetValue(exePath, out var policy) ? policy : "ask";
        // 同一条目、同一策略时不重建列表 —— 否则每次后台刷新都会重置 ComboBox，
        // 用户一打开下拉就被打断关闭
        if (string.Equals(_aiPolicyExe, exePath, StringComparison.OrdinalIgnoreCase)
            && string.Equals(_aiPolicyCurrent, current, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }
        _aiPolicyExe = exePath;
        _aiPolicyCurrent = current;

        AiPolicyComboBox.Items.Clear();
        AiPolicyComboBox.Items.Add(new ComboBoxItem { Content = "每次询问（默认）", Tag = "ask" });
        AiPolicyComboBox.Items.Add(new ComboBoxItem { Content = "总是放行", Tag = "allow" });
        AiPolicyComboBox.Items.Add(new ComboBoxItem { Content = "总是拦截", Tag = "block" });
        for (int i = 0; i < AiPolicyComboBox.Items.Count; i++)
        {
            if (string.Equals((AiPolicyComboBox.Items[i] as ComboBoxItem)?.Tag as string, current, StringComparison.OrdinalIgnoreCase))
            {
                AiPolicyComboBox.SelectedIndex = i;
                break;
            }
        }
    }

    private async void AiSavePolicy_Click(object sender, RoutedEventArgs e)
    {
        if (!AiTryGetSelectedItem(out var vm) || string.IsNullOrWhiteSpace(vm.ExePath)) return;
        if (!await AiEnsureBackendReadyAsync()) return;
        var policy = (AiPolicyComboBox.SelectedItem as ComboBoxItem)?.Tag as string ?? "ask";
        var policyText = policy switch { "allow" => "总是放行", "block" => "总是拦截", _ => "每次询问" };
        try
        {
            await InterceptWorkspace.SetTrustPolicyAsync(vm.ExePath, policy);
            ShowAiStatus($"信任策略已设为「{policyText}」，对该程序所有待审核条目立即生效", InfoBarSeverity.Success);
        }
        catch (Exception ex)
        {
            ShowAiStatus($"保存失败:{ex.Message}", InfoBarSeverity.Error);
        }
    }

    private async void AiFlyPolicy_Click(object sender, RoutedEventArgs e)
    {
        if (_aiContextItem is null || string.IsNullOrWhiteSpace(_aiContextItem.ExePath)) return;
        if (!await AiEnsureBackendReadyAsync()) return;
        var dialog = new ContentDialog
        {
            Title = "设置信任策略",
            Content = $"选择「{_aiContextItem.Name}」（{_aiContextItem.ExePath}）的信任策略：",
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };
        var combo = new ComboBox { Width = 260, Header = "信任策略" };
        combo.Items.Add(new ComboBoxItem { Content = "每次询问（默认）", Tag = "ask" });
        combo.Items.Add(new ComboBoxItem { Content = "总是放行", Tag = "allow" });
        combo.Items.Add(new ComboBoxItem { Content = "总是拦截", Tag = "block" });
        var current = InterceptWorkspace.Policies.TryGetValue(_aiContextItem.ExePath, out var policy) ? policy : "ask";
        for (int i = 0; i < combo.Items.Count; i++)
        {
            if (string.Equals((combo.Items[i] as ComboBoxItem)?.Tag as string, current, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedIndex = i;
                break;
            }
        }
        dialog.Content = new StackPanel { Spacing = 8, Children = { new TextBlock { Text = $"选择「{_aiContextItem.Name}」的信任策略：", TextWrapping = TextWrapping.Wrap }, combo } };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            var value = (combo.SelectedItem as ComboBoxItem)?.Tag as string ?? "ask";
            var policyText = value switch { "allow" => "总是放行", "block" => "总是拦截", _ => "每次询问" };
            try
            {
                await InterceptWorkspace.SetTrustPolicyAsync(_aiContextItem.ExePath, value);
                ShowAiStatus($"信任策略已设为「{policyText}」", InfoBarSeverity.Success);
            }
            catch (Exception ex)
            {
                ShowAiStatus($"保存失败:{ex.Message}", InfoBarSeverity.Error);
            }
        }
    }

    // ================= 已停止追踪管理 =================

    private async void AiShowIgnored_Click(object sender, RoutedEventArgs e)
    {
        var items = ActiveInterceptData.ReadIgnored();
        var stack = new StackPanel { Spacing = 8, MinWidth = 460, MaxHeight = 420 };

        if (items.Count == 0)
        {
            stack.Children.Add(new TextBlock { Text = "暂无已停止追踪的条目", Foreground = new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)) });
        }
        else
        {
            foreach (var item in items.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase))
            {
                var border = new Border
                {
                    Padding = new Thickness(10, 8, 10, 8),
                    CornerRadius = new CornerRadius(6),
                    Background = new SolidColorBrush(Color.FromArgb(20, 128, 128, 128)),
                    Child = new Grid { ColumnSpacing = 8 },
                };
                var grid = (Grid)border.Child;
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                var info = new StackPanel { Spacing = 2 };
                info.Children.Add(new TextBlock { Text = item.Name, FontSize = 13, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
                info.Children.Add(new TextBlock { Text = item.SubKey, FontSize = 11, Foreground = new SolidColorBrush(Color.FromArgb(255, 130, 130, 130)), TextTrimming = TextTrimming.CharacterEllipsis });
                grid.Children.Add(info);
                var captured = item;
                var btn = new Button { Content = "恢复追踪", VerticalAlignment = VerticalAlignment.Center, Padding = new Thickness(10, 4, 10, 4) };
                btn.Click += async (_, _) =>
                {
                    try
                    {
                        await InterceptWorkspace.ResumeTrackingAsync(captured.Id);
                        ShowAiStatus($"已恢复追踪：{captured.Name}", InfoBarSeverity.Success);
                    }
                    catch (Exception ex)
                    {
                        ShowAiStatus($"恢复失败:{ex.Message}", InfoBarSeverity.Error);
                    }
                };
                grid.Children.Add(btn);
                stack.Children.Add(border);
            }
        }

        var dialog = new ContentDialog
        {
            Title = "已停止追踪",
            Content = new ScrollViewer { Content = stack, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled },
            PrimaryButtonText = "全部恢复追踪",
            CloseButtonText = "关闭",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
        };
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary && items.Count > 0)
        {
            int ok = 0;
            foreach (var item in items)
            {
                try
                {
                    await InterceptWorkspace.ResumeTrackingAsync(item.Id);
                    ok++;
                }
                catch { }
            }
            ShowAiStatus(ok > 0 ? $"已恢复 {ok} 项的追踪" : "没有条目被恢复", ok > 0 ? InfoBarSeverity.Success : InfoBarSeverity.Error);
        }
    }

    // ================= 后端开关 / 设置 =================

    private void AiOpenSettings_Click(object sender, RoutedEventArgs e)
    {
        App.MainWindow?.NavigateToSettings("ActiveInterceptNotifyMode");
    }

    private void AiEnableBackend_Click(object sender, RoutedEventArgs e)
    {
        AppSettings.Set("ActiveInterceptEnabled", true);
        // 同步而非裸启动：若游戏后台监控开着，后端已在运行，只需换配置重启装配
        ActiveInterceptService.SyncBackend();
        RefreshActiveIntercept();
    }

    private void AiDisableBackend_Click(object sender, RoutedEventArgs e)
    {
        AppSettings.Set("ActiveInterceptEnabled", false);
        // 同步而非裸停止：游戏后台监控仍开着时后端必须继续常驻（只是卸掉拦截子系统）
        ActiveInterceptService.SyncBackend();
        RefreshActiveIntercept();
    }

    // ================= 反馈条 =================

    private void ShowAiStatus(string message, InfoBarSeverity severity)
    {
        AiStatusBar.Severity = severity;
        AiStatusBar.Title = message;
        AiStatusBar.IsOpen = true;
    }

    private void CloseAiStatus()
    {
        AiStatusBar.IsOpen = false;
    }

    private async Task<bool> AiConfirmAsync(string title, string message, string confirmText)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = confirmText,
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
        };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }

    #endregion
    #region 扫描与清理

    private void Scan_Click(object sender, RoutedEventArgs e) => ScanNow();

    private void ScanNow()
    {
        if (_scanning)
        {
            _cts?.Cancel();
            return;
        }
        _cts = new CancellationTokenSource();
        _startupAllMode = false;
        _findingsAllMode = false;
        StartupAllList.Visibility = Visibility.Collapsed;
        BackToFindingsBtn.Visibility = Visibility.Collapsed;
        _allFindings = [];
        _findingIcons.Clear();
        _statFound = _statSuggested = _statManageable = _statReportOnly = 0;
        UpdateStatCards();
        ResultsList.ItemsSource = null;
        EmptyPanel.Visibility = Visibility.Visible;
        EmptyText.Text = "正在扫描…";
        ScanRing.IsActive = true;
        ProgressPanel.Visibility = Visibility.Visible;
        StageText.Text = "准备扫描…";
        ScanBtn.Content = "取消扫描";
        _scanning = true;
        SetActionButtons(false);

        var sink = new PageProgressSink(DispatcherQueue,
            stage => StageText.Text = stage,
            f => OnFindingDiscovered(f));

        Task.Run(() =>
        {
            List<Finding> findings = [];
            try { findings = _scanner.ScanAll(sink); }
            catch (Exception ex)
            {
                Logger.Error("扫描失败", ex);
                sink.Stage("扫描出错：" + ex.Message);
            }
            return findings;
        }).ContinueWith(t =>
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                _scanning = false;
                _hasScanned = true;
                ScanBtn.Content = "刷新";
                ScanRing.IsActive = false;
                if (t.IsFaulted || t.Result is null && t.Exception != null)
                {
                    var ex = t.Exception?.GetBaseException();
                    ProgressPanel.Visibility = Visibility.Visible;
                    StageText.Text = "扫描失败：" + (ex?.Message ?? "未知错误") + "。可点击「刷新」重试。";
                    EmptyPanel.Visibility = Visibility.Visible;
                    EmptyText.Text = "扫描失败，未能读取本机信息。";
                }
                else
                {
                    var findings = t.Result ?? [];
                    UserWhitelistStore.Apply(_store, findings);
                    _allFindings = findings;
                    ProgressPanel.Visibility = Visibility.Collapsed;
                    var warnings = _scanner.Warnings.Count;
                    StageText.Text = $"扫描完成，发现 {findings.Count} 项" + (warnings > 0 ? $"，另有 {warnings} 个受保护位置无法读取" : "") + "。";
                    ReportBtn.IsEnabled = findings.Count > 0;
                    RenderFindings();
                    HydrateFindingIcons();
                }
                SetActionButtons(true);
            });
        }, TaskScheduler.Default);
    }

    private void OnFindingDiscovered(Finding finding)
    {
        _statFound++;
        if (finding.CanClean) _statManageable++;
        else _statReportOnly++;
        if (finding.Risk == "高" || finding.Risk == "中") _statSuggested++;
        UpdateStatCards();
    }

    private void SetActionButtons(bool enabled)
    {
        CleanBtn.IsEnabled = enabled;
        SelectAllBtn.IsEnabled = enabled;
        SelectLowBtn.IsEnabled = enabled;
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var f in FilteredFindings()) f.Selected = f.BulkSelectable;
    }

    private void SelectLow_Click(object sender, RoutedEventArgs e)
    {
        foreach (var f in FilteredFindings()) f.Selected = f.BulkSelectable && f.Risk == "低";
    }

    // 行内「处理」按钮：直接处理这一条（先备份，可在恢复中心还原）
    private async void RowAction_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not Finding finding || !finding.CanClean) return;
        var dialog = new ContentDialog
        {
            Title = "处理：" + finding.CompactTitle,
            Content = new TextBlock
            {
                Text = $"将执行：{finding.ActionText}\n\n位置：{finding.TechnicalLocation}\n\n处理前会自动备份，可在「恢复中心」还原。",
                IsTextSelectionEnabled = true,
                TextWrapping = TextWrapping.Wrap
            },
            PrimaryButtonText = "开始处理",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        finding.Selected = true;
        CleanBtn.IsEnabled = false;
        StageText.Text = "正在处理：" + finding.CompactTitle + "…";
        ProgressPanel.Visibility = Visibility.Visible;
        CleanupBatch? batch = null;
        try
        {
            batch = await Task.Run(() => _cleaner.Clean(new[] { finding }));
        }
        catch (Exception ex)
        {
            Logger.Error("单条清理失败", ex);
            await ShowInfo("处理失败", ex.Message);
        }
        finally
        {
            ProgressPanel.Visibility = Visibility.Collapsed;
            CleanBtn.IsEnabled = FilteredFindings().Any(f => f.Selected && f.CanClean);
        }

        if (batch != null)
        {
            var result = batch.Results.FirstOrDefault();
            if (result != null)
            {
                finding.Status = ChineseDisplayText.CleanupStatus(result.Status);
                await ShowInfo("处理完成", result.Status == "Done"
                    ? $"「{finding.CompactTitle}」已处理（{result.Message}）。可在「恢复中心」还原。"
                    : result.Status == "Launched"
                        ? "已打开该产品自己的卸载窗口，请按窗口提示操作。"
                        : $"处理结果：{result.Message}");
            }
            RenderFindings();
            RefreshRecovery();
        }
    }

    private async void ShowAllStartup_Click(object sender, RoutedEventArgs e)
    {
        _startupAllMode = true;
        ShowAllStartupBtn.Visibility = Visibility.Collapsed;
        BackToFindingsBtn.Visibility = Visibility.Visible;
        FilterTitle.Text = "全部启动项（只读，仅核对）";
        CountText.Text = "";
        EmptyPanel.Visibility = Visibility.Visible;
        EmptyText.Text = "正在读取全部启动项…";
        var items = await Task.Run(() => StartupItemEnumerator.List());
        StartupAllList.ItemsSource = items;
        EmptyPanel.Visibility = items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (items.Count == 0) EmptyText.Text = "没有找到任何启动项。";
    }

    private void BackToFindings_Click(object sender, RoutedEventArgs e)
    {
        _startupAllMode = false;
        RenderFindings();
    }

    // 列表底部「展示全部」：显示被当前 tab 筛选器隐藏的其他分类（与总览一致），再点一次回到筛选结果
    private void FindingsToggle_Click(object sender, RoutedEventArgs e)
    {
        _findingsAllMode = !_findingsAllMode;
        RenderFindings();
    }

    private async void Clean_Click(object sender, RoutedEventArgs e)
    {
        var selected = FilteredFindings().Where(f => f.Selected && f.CanClean).ToList();
        if (selected.Count == 0)
        {
            await ShowInfo("没有可清理的勾选项", "请先勾选要处理的项目（风险「高/中/低」且不是「仅提示」的项目可以清理）。");
            return;
        }

        var preview = new StringBuilder();
        foreach (var f in selected.Take(15)) preview.AppendLine("· " + f.CompactTitle + " → " + f.ActionText);
        if (selected.Count > 15) preview.AppendLine("… 还有 " + (selected.Count - 15) + " 项");
        var adminNote = selected.Any(f => f.RequiresAdmin) && !AdminUtil.IsAdministrator()
            ? "\n\n部分项目属于系统范围，需要管理员权限；当前可能失败。"
            : "";

        var dialog = new ContentDialog
        {
            Title = "确认清理 " + selected.Count + " 项？",
            Content = new ScrollViewer
            {
                MaxHeight = 360,
                Content = new TextBlock
                {
                    Text = "所有操作会先备份到恢复中心，处理后可随时还原。\n\n" + preview + adminNote,
                    IsTextSelectionEnabled = true,
                    TextWrapping = TextWrapping.Wrap
                }
            },
            PrimaryButtonText = "开始清理",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        CleanBtn.IsEnabled = false;
        ScanBtn.IsEnabled = false;
        StageText.Text = "正在清理 " + selected.Count + " 项…";
        ProgressPanel.Visibility = Visibility.Visible;

        var batches = new List<CleanupBatch>();
        try
        {
            batches = await Task.Run(() => new List<CleanupBatch> { _cleaner.Clean(selected) });
        }
        catch (Exception ex)
        {
            Logger.Error("清理失败", ex);
            await ShowInfo("清理失败", ex.Message);
        }
        finally
        {
            ProgressPanel.Visibility = Visibility.Collapsed;
            CleanBtn.IsEnabled = true;
            ScanBtn.IsEnabled = true;
        }

        if (batches.Count > 0)
        {
            var results = batches[0].Results;
            foreach (var result in results)
            {
                var finding = _allFindings.FirstOrDefault(f => f.Id == result.Id);
                if (finding != null) finding.Status = ChineseDisplayText.CleanupStatus(result.Status);
            }
            RenderFindings();
            var done = results.Count(r => r.Status == "Done");
            var launched = results.Count(r => r.Status == "Launched");
            var failed = results.Count(r => r.Status == "Failed" || r.Status == "Skipped");
            await ShowInfo("清理完成",
                $"成功 {done} 项，打开卸载窗口 {launched} 项，失败/跳过 {failed} 项。\n\n如需还原，请到「恢复中心」选择本次批次。");
            RefreshRecovery();
        }
    }

    private async void Report_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var path = Path.Combine(_store.Reports, "scan-evidence-" + _store.Timestamp() + ".json");
            CleanerEngine.WriteJson(path, new ScanEvidenceReport
            {
                ScannedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                ProductVersion = AppMeta.Version,
                FindingCount = _allFindings.Count,
                WarningCount = _scanner.Warnings.Count,
                Findings = _allFindings,
                Warnings = _scanner.Warnings
            });
            var dialog = new ContentDialog
            {
                Title = "证据报告已导出",
                Content = new TextBlock
                {
                    Text = "报告文件：\n" + path + "\n\n包含全部扫描发现与证据，可用于人工复核或交给管理员处理。",
                    IsTextSelectionEnabled = true,
                    TextWrapping = TextWrapping.Wrap
                },
                PrimaryButtonText = "打开所在文件夹",
                CloseButtonText = "关闭",
                XamlRoot = XamlRoot,
                RequestedTheme = ThemeService.CurrentElementTheme
            };
            if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            {
                try { Process.Start(new ProcessStartInfo { FileName = "explorer.exe", Arguments = "/select,\"" + path + "\"", UseShellExecute = true }); } catch { }
            }
        }
        catch (Exception ex)
        {
            await ShowInfo("导出失败", ex.Message);
        }
    }

    #endregion

    #region 结果列表与详情

    private void RenderFindings()
    {
        _suppressRender = true;
        List<Finding> list;
        try
        {
            list = FilteredFindings();
            ResultsList.Visibility = Visibility.Visible;
            StartupAllList.Visibility = Visibility.Collapsed;
            ShowAllStartupBtn.Visibility = Visibility.Collapsed;
            BackToFindingsBtn.Visibility = Visibility.Collapsed;

            ResultsList.ItemsSource = null;
            ResultsList.ItemsSource = list;
            FilterTitle.Text = _findingsAllMode ? "全部发现" : _filter switch
            {
                "popup" => "弹窗与守护进程诊断",
                _ => "全部发现"
            };
            RefreshCountText();
            UpdateStatCards();
            ReportBtn.IsEnabled = _allFindings.Count > 0;

            if (list.Count == 0)
            {
                EmptyPanel.Visibility = Visibility.Visible;
                if (!_hasScanned)
                {
                    EmptyText.Text = "正在扫描…";
                }
                else if (_findingsAllMode)
                {
                    EmptyText.Text = "扫描完成，未发现可疑项。";
                }
                else if (_filter == "popup")
                {
                    EmptyText.Text = "扫描完成，未发现可疑的弹窗与守护进程。";
                }
                else
                {
                    EmptyText.Text = "扫描完成，未发现可疑项。";
                }
            }
            else
            {
                EmptyPanel.Visibility = Visibility.Collapsed;
            }
            UpdateFindingsFooter();
        }
        finally
        {
            _suppressRender = false;
        }
        if (!_startupAllMode && ResultsList.SelectedItem is null && list.Count > 0)
        {
            ResultsList.SelectedIndex = 0;
            RenderDetail(ResultsList.SelectedItem as Finding);
        }
        else
        {
            RenderDetail(ResultsList.SelectedItem as Finding);
        }
    }

    // 列表底部「展示全部」的提示与切换按钮；总览 tab 已显示全部，不显示
    private void UpdateFindingsFooter()
    {
        if (_filter is not "popup")
        {
            FindingsFooter.Visibility = Visibility.Collapsed;
            return;
        }
        int filtered = _allFindings.Count(RogueCleanerViewFilters.MatchesPopupTab);
        int hidden = _allFindings.Count - filtered;
        if (_findingsAllMode)
        {
            FindingsHiddenHint.Text = "已显示全部 " + _allFindings.Count + " 项";
            FindingsToggleBtn.Content = "仅显示筛选结果";
            FindingsFooter.Visibility = Visibility.Visible;
        }
        else
        {
            FindingsHiddenHint.Text = "另有 " + hidden + " 项被当前筛选器隐藏";
            FindingsToggleBtn.Content = "展示全部 " + _allFindings.Count + " 项";
            FindingsFooter.Visibility = hidden > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void RefreshCountText()
    {
        var list = FilteredFindings();
        int selected = list.Count(f => f.Selected && f.CanClean);
        CountText.Text = $"共 {list.Count} 项 · 已勾选 {selected} 项";
        CleanBtn.IsEnabled = !_scanning && selected > 0;
    }

    private void ResultsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressRender) return;
        RenderDetail(ResultsList.SelectedItem as Finding);
    }

    private void RenderDetail(Finding? f)
    {
        DetailPanel.Children.Clear();
        if (f is null)
        {
            DetailPanel.Children.Add(new TextBlock
            {
                Text = "在左侧选择一项查看详情。",
                FontSize = 13,
                Foreground = new SolidColorBrush(ThemeColors.DimText),
                TextWrapping = TextWrapping.Wrap
            });
            return;
        }
        AddDetailRow("项目", f.UserVisibleName, true);
        AddDetailRow("风险", f.RiskDisplay + (string.IsNullOrWhiteSpace(f.Status) ? "" : " · " + f.Status));
        AddDetailRow("软件", string.IsNullOrWhiteSpace(f.SoftwareName) ? f.Vendor ?? "来源未确认" : f.SoftwareName + (string.IsNullOrWhiteSpace(f.Vendor) ? "" : "（" + f.Vendor + "）"));
        if (!string.IsNullOrWhiteSpace(f.IdentityExplanation)) AddDetailRow("身份依据", f.IdentityExplanation);
        AddDetailRow("位置", f.TechnicalLocation);
        AddDetailRow("影响", f.UserImpact);
        AddDetailRow("处理方式", f.ActionText);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 6, 0, 0) };
        var copy = new Button { Content = "复制详情" };
        copy.Click += (_, _) => CopyFindingDetail(f);
        buttons.Children.Add(copy);
        var wl = new Button { Content = "加入白名单" };
        wl.Click += async (_, _) => { UserWhitelistStore.Add(_store, f); await ReloadWhitelistState(); };
        buttons.Children.Add(wl);
        var fb = new Button { Content = "反馈" };
        fb.Click += async (_, _) => await ShowFeedbackDialog(f);
        buttons.Children.Add(fb);
        DetailPanel.Children.Add(buttons);
    }

    private void AddDetailRow(string label, string? value, bool bold = false)
    {
        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(74) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.Children.Add(new TextBlock
        {
            Text = label,
            FontSize = 12,
            VerticalAlignment = VerticalAlignment.Top,
            Foreground = new SolidColorBrush(ThemeColors.DimText)
        });
        grid.Children.Add(new TextBlock
        {
            Text = value ?? "",
            FontSize = bold ? 14 : 12,
            FontWeight = bold ? Microsoft.UI.Text.FontWeights.Bold : Microsoft.UI.Text.FontWeights.Normal,
            IsTextSelectionEnabled = true,
            TextWrapping = TextWrapping.Wrap,
            Foreground = new SolidColorBrush(ThemeColors.PrimaryText)
        });
        Grid.SetColumn((FrameworkElement)grid.Children[1], 1);
        DetailPanel.Children.Add(grid);
    }

    private void CopyFindingDetail(Finding f)
    {
        var text = $"项目：{f.UserVisibleName}\n风险：{f.RiskDisplay}\n软件：{f.SoftwareName}\n位置：{f.TechnicalLocation}\n影响：{f.UserImpact}\n处理方式：{f.ActionText}\n证据：{f.Evidence}";
        try
        {
            var data = new DataPackage();
            data.SetText(text);
            Clipboard.SetContent(data);
            Clipboard.Flush();
        }
        catch { }
    }

    #endregion

    #region 软件图标（原版结果行展示软件图标）

    private void HydrateFindingIcons()
    {
        if (_allFindings.Count == 0) return;
        SoftwarePresentationQueue.Hydrate(DispatcherQueue, _allFindings, () => _ = ConvertFindingIconsAsync());
    }

    private async Task ConvertFindingIconsAsync()
    {
        bool changed = false;
        foreach (var f in _allFindings)
        {
            if (_findingIcons.TryGetValue(f.Id, out var cached))
            {
                f.IconDisplay = cached;
                changed = true;
                continue;
            }
            if (f.SoftwareIcon == null) continue;
            var bmp = await ToBitmapImageAsync(f.SoftwareIcon);
            if (bmp != null)
            {
                _findingIcons[f.Id] = bmp;
                f.IconDisplay = bmp;
                changed = true;
            }
        }
        if (changed) RenderFindings();
    }

    private async Task ConvertMenuIconsAsync()
    {
        bool changed = false;
        foreach (var e in _cmEntries) changed |= await SetMenuIconAsync(e.Id, e.SoftwareIcon, () => e.IconDisplay, v => e.IconDisplay = v);
        foreach (var e in _specialEntries) changed |= await SetMenuIconAsync(e.Id, e.SoftwareIcon, () => e.IconDisplay, v => e.IconDisplay = v);
        foreach (var e in _advancedEntries) changed |= await SetMenuIconAsync(e.Id, e.SoftwareIcon, () => e.IconDisplay, v => e.IconDisplay = v);
        if (changed)
        {
            ApplyCmFilter();
            ApplySpecialFilter();
            ApplyAdvancedFilter();
        }
    }

    private async Task<bool> SetMenuIconAsync(string id, System.Drawing.Image? icon, Func<ImageSource> current, Action<ImageSource> setter)
    {
        if (string.IsNullOrEmpty(id)) return false;
        // 刷新后条目是全新对象：缓存命中也要把图标赋给新条目并触发重新绑定。
        // 只有当图标确实变化（尚未赋值）时才返回 true，避免每次重绘都重建列表（"列表无限刷新"）。
        if (_menuIcons.TryGetValue(id, out var cached))
        {
            if (!ReferenceEquals(current(), cached))
            {
                setter(cached);
                return true;
            }
            return false;
        }
        if (icon == null) return false;
        var bmp = await ToBitmapImageAsync(icon);
        if (bmp == null) return false;
        _menuIcons[id] = bmp;
        if (!ReferenceEquals(current(), bmp))
        {
            setter(bmp);
            return true;
        }
        return false;
    }

    private static async Task<BitmapImage?> ToBitmapImageAsync(System.Drawing.Image? bitmap)
    {
        if (bitmap == null) return null;
        try
        {
            using var ms = new MemoryStream();
            bitmap.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
            ms.Position = 0;
            var bmp = new BitmapImage();
            using var ras = ms.AsRandomAccessStream();
            await bmp.SetSourceAsync(ras);
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    private void ResultsList_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
    }

    private void CmList_ContainerContentChanging(ListViewBase sender, ContainerContentChangingEventArgs args)
    {
        // 主管理列表的「显示」列开关：已显示=开(绿)，已隐藏=关(灰)
        if (args.Item is ContextMenuEntry entry && args.ItemContainer.ContentTemplateRoot is FrameworkElement root)
        {
            if (root.FindName("ToggleBtn") is Button btn)
            {
                btn.Content = entry.Enabled ? "开" : "关";
                btn.Background = new SolidColorBrush(entry.Enabled ? ParseHex("#16A34A") : ParseHex("#6B7280"));
                btn.Foreground = new SolidColorBrush(ParseHex("#FFFFFF"));
                btn.IsEnabled = !entry.ReadOnly;
            }
        }
    }

    #endregion

    private void ResultsList_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if ((e.OriginalSource as FrameworkElement)?.DataContext is Finding finding)
        {
            ResultsList.SelectedItem = finding;
            _flyoutFinding = finding;
            bool whitelisted = finding.ActionKind == "ReportOnly" && finding.Status == "已白名单";
            WLAddItem.Visibility = whitelisted ? Visibility.Collapsed : Visibility.Visible;
            WLRemoveItem.Visibility = whitelisted ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private async void WhitelistAdd_Click(object sender, RoutedEventArgs e)
    {
        if (_flyoutFinding is null) return;
        bool added = UserWhitelistStore.Add(_store, _flyoutFinding);
        await ReloadWhitelistState();
        if (!added) await ShowInfo("白名单", "该项已在本地白名单中。");
    }

    private async void WhitelistRemove_Click(object sender, RoutedEventArgs e)
    {
        if (_flyoutFinding is null) return;
        UserWhitelistStore.Remove(_store, _flyoutFinding);
        await ReloadWhitelistState();
    }

    private async Task ReloadWhitelistState()
    {
        UserWhitelistStore.Apply(_store, _allFindings);
        RenderFindings();
        if (ResultsList.SelectedItem is Finding f) RenderDetail(f);
        await Task.CompletedTask;
    }


    #region 统计卡片

    private readonly List<TextBlock> _statValueTexts = [];

    private void BuildStatCards()
    {
        StatCards.ColumnDefinitions.Clear();
        StatCards.Children.Clear();
        _statValueTexts.Clear();
        var cards = new (string label, string glyph, string color)[]
        {
            ("发现项目", "\uE9D9", "#2563EB"),
            ("建议处理", "\uE783", "#EA580C"),
            ("可管理", "\uE74D", "#16A34A"),
            ("仅提示·未知", "\uE9CE", "#6B7280")
        };
        for (int i = 0; i < cards.Length; i++)
        {
            StatCards.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            var value = new TextBlock { FontSize = 22, FontWeight = Microsoft.UI.Text.FontWeights.Bold, Foreground = new SolidColorBrush(ThemeColors.PrimaryText), Text = "0" };
            var label = new TextBlock { FontSize = 12, Foreground = new SolidColorBrush(ThemeColors.DimText), Text = cards[i].label };
            var icon = new FontIcon { Glyph = cards[i].glyph, FontSize = 18, Foreground = new SolidColorBrush(ParseHex(cards[i].color)) };
            var border = new Border
            {
                CornerRadius = new CornerRadius(10),
                Padding = new Thickness(14, 10, 14, 10),
                Background = new SolidColorBrush(ThemeColors.CardBg),
                BorderBrush = new SolidColorBrush(ThemeColors.BorderColor),
                BorderThickness = new Thickness(1),
                Child = new StackPanel { Spacing = 2, Children = { new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { icon, label } }, value } }
            };
            Grid.SetColumn(border, i);
            StatCards.Children.Add(border);
            _statValueTexts.Add(value);
        }
    }

    private void UpdateStatCards()
    {
        if (_statValueTexts.Count != 4) return;
        _statValueTexts[0].Text = _statFound.ToString();
        _statValueTexts[1].Text = _statSuggested.ToString();
        _statValueTexts[2].Text = _statManageable.ToString();
        _statValueTexts[3].Text = _statReportOnly.ToString();
    }

    private static Color ParseHex(string hex)
    {
        try
        {
            return Color.FromArgb(255,
                byte.Parse(hex.Substring(1, 2), NumberStyles.HexNumber),
                byte.Parse(hex.Substring(3, 2), NumberStyles.HexNumber),
                byte.Parse(hex.Substring(5, 2), NumberStyles.HexNumber));
        }
        catch
        {
            return Color.FromArgb(255, 100, 116, 139);
        }
    }

    internal static SolidColorBrush HexBrush(string hex) => new(ParseHex(hex));

    #endregion

    #region 反馈与关于

    private async void Feedback_Click(object sender, RoutedEventArgs e) => await ShowFeedbackDialog(ResultsList.SelectedItem as Finding ?? _allFindings.FirstOrDefault());

    private async void FeedbackItem_Click(object sender, RoutedEventArgs e) => await ShowFeedbackDialog(_flyoutFinding);

    private async Task ShowFeedbackDialog(Finding? finding)
    {
        if (finding is null)
        {
            await ShowInfo("反馈", "请先扫描并选择要反馈的项目。");
            return;
        }
        var types = new ComboBox
        {
            Header = "反馈类型",
            ItemsSource = new[] { "误报", "漏报", "身份错误", "关联错误" },
            SelectedIndex = 0,
            Width = 280
        };
        var expected = new TextBox { Header = "期望结果（选填）", PlaceholderText = "例如：这是正版软件，不应提示", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 60 };
        var preview = new TextBlock { IsTextSelectionEnabled = true, TextWrapping = TextWrapping.Wrap, FontSize = 12, Foreground = new SolidColorBrush(ThemeColors.DimText), MaxHeight = 200 };
        void UpdatePreview()
        {
            try
            {
                var report = FeedbackService.CreateReport(finding, types.SelectedItem as string ?? "误报", expected.Text, false);
                preview.Text = FeedbackService.BuildMarkdown(report);
            }
            catch { }
        }
        types.SelectionChanged += (_, _) => UpdatePreview();
        expected.TextChanged += (_, _) => UpdatePreview();

        var panel = new StackPanel { Spacing = 10, Width = 440, Children = { types, expected, new TextBlock { Text = "预览（会自动脱敏用户名、路径、邮箱、URL、令牌）：", FontSize = 12, Foreground = new SolidColorBrush(ThemeColors.DimText) }, preview } };
        var dialog = new ContentDialog
        {
            Title = "反馈：" + finding.CompactTitle,
            Content = panel,
            PrimaryButtonText = "保存到本地",
            SecondaryButtonText = "打开 GitHub Issue",
            CloseButtonText = "取消",
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };
        UpdatePreview();
        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.None) return;
        try
        {
            var report = FeedbackService.CreateReport(finding, types.SelectedItem as string ?? "误报", expected.Text, false);
            if (result == ContentDialogResult.Primary)
            {
                var saved = FeedbackService.Save(_store, report);
                await ShowInfo("已保存到本地", "反馈已脱敏并保存：\n" + saved.MarkdownPath + "\n" + saved.JsonPath + "\n\n如需上报，可在反馈报告中打开 GitHub Issue。");
            }
            else
            {
                var url = FeedbackService.BuildIssueUrl(report);
                try
                {
                    var data = new DataPackage();
                    data.SetText(FeedbackService.BuildMarkdown(report));
                    Clipboard.SetContent(data);
                    Clipboard.Flush();
                }
                catch { }
                try { Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true }); } catch { }
            }
        }
        catch (Exception ex)
        {
            await ShowInfo("反馈失败", ex.Message);
        }
    }

    private async void About_Click(object sender, RoutedEventArgs e)
    {
        var text = $"流氓软件的克星 v{AppMeta.Version}\n\n" +
            "扫描和清理 Windows 流氓右键菜单、自启动、计划任务、服务、浏览器插件和文件关联残留；" +
            "全部处理先备份，恢复中心可还原。\n\n" +
            "本工具移植自开源项目 RogueCleaner（作者 aakk007，52pojie），MIT License。\n" +
            "项目主页：https://github.com/aakk007/RogueCleaner\n" +
            "原版社区：https://www.52pojie.cn/home.php?mod=space&uid=286924\n\n" +
            "厂商识别依据本地规则库与数字签名；白名单为本地文件，不会上传任何数据。";
        var dialog = new ContentDialog
        {
            Title = "关于",
            Content = new TextBlock { Text = text, IsTextSelectionEnabled = true, TextWrapping = TextWrapping.Wrap },
            CloseButtonText = "关闭",
            PrimaryButtonText = "打开项目主页",
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            try { Process.Start(new ProcessStartInfo { FileName = AppMeta.Repository, UseShellExecute = true }); } catch { }
        }
    }

    private async Task ShowInfo(string title, string message)
    {
        var dialog = new ContentDialog
        {
            Title = title,
            Content = new TextBlock { Text = message, IsTextSelectionEnabled = true, TextWrapping = TextWrapping.Wrap },
            CloseButtonText = "确定",
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };
        await dialog.ShowAsync();
    }

    #endregion

    #region 右键菜单管理

    private ContextMenuInventory? _cmInventory;
    private int _cmPresentationCandidates;
    private List<ContextMenuEntry> _visibleCmEntries = [];
    private List<SpecialMenuEntry> _visibleSpecialEntries = [];
    private List<AdvancedMenuEntry> _visibleAdvancedEntries = [];

    // 主列表按“场景”分组：组顺序遵循固定清单（贴近用户感知的右击场景），
    // 未列出的场景（如“文件类型 .xxx”）统一排到末尾并按键名稳定排序。
    private static readonly string[] CmSceneOrder =
    [
        "所有文件", "所有文件系统对象", "可执行文件", "快捷方式", "未知文件",
        "文件夹", "文件夹对象", "文件夹拖放", "文件夹背景", "桌面背景",
        "磁盘", "磁盘拖放", "命令仓库",
        "文件右键", "文件夹右键", "文件夹空白处右键", "磁盘右键", "文件资源管理器右键"
    ];

    private static int CmSceneRank(string scene)
    {
        int index = Array.IndexOf(CmSceneOrder, scene);
        return index < 0 ? 1000 : index;
    }

    /// <summary>把过滤后的条目按 Scene 分组（组内保留原有排序，组间按场景清单排序）。</summary>
    private static List<CmSceneGroup> BuildCmSceneGroups(IReadOnlyList<ContextMenuEntry> entries)
    {
        return entries
            .GroupBy(e => e.Scene)
            .Select(g => new CmSceneGroup(g.Key, g))
            .OrderBy(g => CmSceneRank(g.Key))
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // ---------- 视图切换（原版为三个独立窗口，这里为页面内三个视图） ----------

    private void CmSpecial_Click(object sender, RoutedEventArgs e)
    {
        CmMainView.Visibility = Visibility.Collapsed;
        CmSpecialView.Visibility = Visibility.Visible;
        InitSpecialView();
    }

    private void CmAdvanced_Click(object sender, RoutedEventArgs e)
    {
        CmMainView.Visibility = Visibility.Collapsed;
        CmAdvancedView.Visibility = Visibility.Visible;
        InitAdvancedView();
    }

    private void CmBack_Click(object sender, RoutedEventArgs e)
    {
        CmSpecialView.Visibility = Visibility.Collapsed;
        CmAdvancedView.Visibility = Visibility.Collapsed;
        CmMainView.Visibility = Visibility.Visible;
        RefreshContextMenus();
    }

    // ---------- 跨视图搜索（主列表 / 更多位置 / 系统高级） ----------

    private void CmSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _cmSearchKeyword = CmSearchBox.Text.Trim();
        CmSearchClearBtn.Visibility = _cmSearchKeyword.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        // 专用/高级清单是懒加载的；搜索时后台补齐，保证跨视图计数与跳转准确
        if (_cmSearchKeyword.Length > 0 && _specialEntries.Count == 0) RefreshSpecial();
        if (_cmSearchKeyword.Length > 0 && _advancedEntries.Count == 0) RefreshAdvanced();
        if (CmSpecialView.Visibility == Visibility.Visible) ApplySpecialFilter();
        else if (CmAdvancedView.Visibility == Visibility.Visible) ApplyAdvancedFilter();
        else ApplyCmFilter();
        UpdateCmSearchHint();
    }

    private void CmSearchClear_Click(object sender, RoutedEventArgs e) => CmSearchBox.Text = string.Empty;

    private void CmJumpSpecial_Click(object sender, RoutedEventArgs e)
    {
        CmMainView.Visibility = Visibility.Collapsed;
        CmAdvancedView.Visibility = Visibility.Collapsed;
        CmSpecialView.Visibility = Visibility.Visible;
        InitSpecialView();
    }

    private void CmJumpAdvanced_Click(object sender, RoutedEventArgs e)
    {
        CmMainView.Visibility = Visibility.Collapsed;
        CmSpecialView.Visibility = Visibility.Collapsed;
        CmAdvancedView.Visibility = Visibility.Visible;
        InitAdvancedView();
    }

    // 搜索提示行：统计三个视图的匹配数，提供跨视图跳转定位
    private void UpdateCmSearchHint()
    {
        bool searching = !string.IsNullOrWhiteSpace(_cmSearchKeyword);
        CmSearchHintPanel.Visibility = searching ? Visibility.Visible : Visibility.Collapsed;
        if (!searching) return;
        int mainCount = _cmInventory == null ? 0 : _cmInventory.Entries.Count(e => RogueCleanerViewFilters.MatchesKeyword(e, _cmSearchKeyword));
        int specialCount = _specialEntries.Count(e => RogueCleanerViewFilters.MatchesKeyword(e, _cmSearchKeyword));
        int advancedCount = _advancedEntries.Count(e => RogueCleanerViewFilters.MatchesKeyword(e, _cmSearchKeyword));
        CmSearchHintText.Text = "搜索“" + _cmSearchKeyword + "” · 主列表 " + mainCount + " 项 · 更多位置 " + specialCount + " 项 · 系统高级 " + advancedCount + " 项";
        bool inSpecial = CmSpecialView.Visibility == Visibility.Visible;
        bool inAdvanced = CmAdvancedView.Visibility == Visibility.Visible;
        CmJumpSpecialBtn.Visibility = !inSpecial && specialCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        CmJumpAdvancedBtn.Visibility = !inAdvanced && advancedCount > 0 ? Visibility.Visible : Visibility.Collapsed;
        CmJumpSpecialBtn.Content = "去更多位置查看（" + specialCount + "）";
        CmJumpAdvancedBtn.Content = "去系统高级查看（" + advancedCount + "）";
    }

    // ---------- 主管理视图（对应原版 ContextMenuManagerForm） ----------

    private void CmRefresh_Click(object sender, RoutedEventArgs e) => RefreshContextMenus();

    private void RefreshContextMenus()
    {
        if (!CmRefreshBtn.IsEnabled) return;
        CmRefreshBtn.IsEnabled = false;
        int generation = ++_cmRefreshGeneration;
        CmStatusText.Text = "正在枚举当前用户、所有用户以及 32/64 位右键入口……";
        CmEmptyText.Visibility = Visibility.Visible;
        CmEmptyText.Text = "正在加载右键菜单清单…";
        Task.Run(() => new ContextMenuDiscoveryService(_store).Enumerate(true))
            .ContinueWith(t =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    CmRefreshBtn.IsEnabled = true;
                    if (generation != _cmRefreshGeneration) return; // 已有更新的刷新，丢弃陈旧结果
                    if (t.IsFaulted)
                    {
                        Logger.Error("枚举右键菜单失败", t.Exception);
                        CmStatusText.Text = "枚举失败：" + (t.Exception?.GetBaseException().Message ?? "未知错误");
                        return;
                    }
                    _cmInventory = t.Result;
                    _cmEntries = _cmInventory.Entries;
                    _cmAllMode = false;
                    foreach (ContextMenuEntry entry in _cmInventory.Entries)
                    {
                        entry.SoftwareIcon = null;
                        entry.SoftwareName = "正在识别…";
                        entry.PresentationResolved = false;
                        entry.IsThirdParty = false;
                    }
                    List<ContextMenuEntry> candidates = _cmInventory.Entries.Where(i => !i.AdvancedOnly).ToList();
                    _cmPresentationCandidates = candidates.Count;
                    ApplyCmFilter();
                    // 重绘只做图标转换；确有变化时 ConvertMenuIconsAsync 内部会按视图重绘，
                    // 不再在每次重绘时额外重建一次列表。
                    SoftwarePresentationQueue.Hydrate(DispatcherQueue, candidates, () =>
                    {
                        if (generation == _cmRefreshGeneration) _ = ConvertMenuIconsAsync();
                    });
                });
            }, TaskScheduler.Default);
    }

    private void ApplyCmFilter()
    {
        if (_cmInventory == null) return;
        bool searching = !string.IsNullOrWhiteSpace(_cmSearchKeyword);

        // 1) 基础候选集：搜索 / 常规主列表（已识别第三方 + 用户自建；「展示全部」时含系统内置等）
        List<ContextMenuEntry> pool;
        if (searching)
        {
            pool = _cmInventory.Entries.Where(e => RogueCleanerViewFilters.MatchesKeyword(e, _cmSearchKeyword)).ToList();
        }
        else
        {
            pool = _cmInventory.Entries.Where(e => _cmAllMode || RogueCleanerViewFilters.MatchesMainMenuList(e)).ToList();
        }

        // 场景分类筛选（左侧导航；空 = 全部场景）。
        // 若当前选中的场景在候选集中已不存在（如搜索/「展示全部」切换把它滤掉），自动回到全部场景。
        if (_cmSceneFilter.Length > 0 && !pool.Any(e => e.Scene == _cmSceneFilter))
            _cmSceneFilter = "";
        _visibleCmEntries = string.IsNullOrEmpty(_cmSceneFilter)
            ? pool
            : pool.Where(e => e.Scene == _cmSceneFilter).ToList();

        // 3) 统计文案
        if (searching)
        {
            CmSummaryText.Text = "搜索“" + _cmSearchKeyword + "”：主列表匹配 " + _visibleCmEntries.Count + " 项（含系统内置与未识别项）";
            CmStatusText.Text = "当前为搜索模式，按名称、软件、命令、位置或组件编号过滤；清空搜索框返回常规列表。";
        }
        else
        {
            int resolved = _cmInventory.Entries.Count(e => !e.AdvancedOnly && e.PresentationResolved);
            int enabled = _visibleCmEntries.Count(e => e.Enabled);
            if (!string.IsNullOrEmpty(_cmSceneFilter))
            {
                int hiddenInScene = _cmInventory.Entries.Count(e =>
                    e.Scene == _cmSceneFilter && !_cmAllMode && !RogueCleanerViewFilters.MatchesMainMenuList(e));
                CmSummaryText.Text = "场景「" + _cmSceneFilter + "」：第三方 " + _visibleCmEntries.Count + " 项  ·  已显示 " + enabled
                    + "  ·  已隐藏 " + (_visibleCmEntries.Count - enabled) + "  ·  同场景另有 " + hiddenInScene + " 项系统/未识别";
                CmStatusText.Text = "正在按场景分类浏览，点击左侧“全部场景”可查看完整列表。";
            }
            else
            {
                int hiddenSystem = _cmInventory.Entries.Count(e => !e.AdvancedOnly && e.PresentationResolved && !e.IsThirdParty);
                int hiddenInternal = _cmInventory.Entries.Count - _cmPresentationCandidates;
                CmSummaryText.Text = "第三方菜单 " + _visibleCmEntries.Count + " 项  ·  已显示 " + enabled + "  ·  已隐藏 "
                    + (_visibleCmEntries.Count - enabled) + "  ·  系统内置不显示";
                CmStatusText.Text = resolved < _cmPresentationCandidates
                    ? "正在识别软件来源 " + resolved + " / " + _cmPresentationCandidates + "……"
                    : "已隐藏 " + hiddenSystem + " 项系统菜单、" + hiddenInternal + " 项内部技术记录；" + _cmInventory.Warnings.Count + " 个受保护位置未读取。";
            }
        }

        // 4) 按场景分区展示：先按固定场景顺序分组，再拍平为“分区标题 + 条目”列表，
        //    由 CmRowSelector 按项类型选择模板（避免同场景条目在视觉上混在一起）。
        CmList.ItemsSource = null;
        var flatCmItems = new List<object>();
        foreach (var sceneGroup in BuildCmSceneGroups(_visibleCmEntries))
        {
            flatCmItems.Add(new CmSectionHeaderVm { Key = sceneGroup.Key, Count = sceneGroup.Count });
            flatCmItems.AddRange(sceneGroup);
        }
        CmList.ItemsSource = flatCmItems;
        CmEmptyText.Visibility = _visibleCmEntries.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_visibleCmEntries.Count == 0)
            CmEmptyText.Text = searching
                ? "没有找到匹配“" + _cmSearchKeyword + "”的右键菜单项目。"
                : string.IsNullOrEmpty(_cmSceneFilter)
                    ? "没有找到已识别的第三方右键菜单。"
                    : "该场景下没有已识别的第三方右键菜单。";
        UpdateCmActions();
        ShowCmDetails();
        UpdateCmFooter();
        UpdateCmSearchHint();
        UpdateCmSceneNav();
    }

    /// <summary>左侧“场景分类”导航：从当前搜索/「展示全部」候选集统计各场景数量。</summary>
    private void UpdateCmSceneNav()
    {
        if (_cmInventory == null) return;
        bool searching = !string.IsNullOrWhiteSpace(_cmSearchKeyword);
        List<ContextMenuEntry> pool = searching
            ? _cmInventory.Entries.Where(e => RogueCleanerViewFilters.MatchesKeyword(e, _cmSearchKeyword)).ToList()
            : _cmInventory.Entries.Where(e => _cmAllMode || RogueCleanerViewFilters.MatchesMainMenuList(e)).ToList();

        var items = new List<CmSceneNavVm> { new() { Key = "全部场景", Scene = "", Count = pool.Count } };
        items.AddRange(pool
            .GroupBy(e => e.Scene)
            .Select(g => new CmSceneNavVm { Key = g.Key, Scene = g.Key, Count = g.Count() })
            .OrderBy(v => CmSceneRank(v.Scene))
            .ThenBy(v => v.Key, StringComparer.OrdinalIgnoreCase));

        _syncingCmSceneNav = true;
        try
        {
            CmSceneNavList.ItemsSource = items;
            CmSceneNavList.SelectedItem = items.FirstOrDefault(v => v.Scene == _cmSceneFilter) ?? items[0];
        }
        finally
        {
            _syncingCmSceneNav = false;
        }
    }

    private void CmSceneNav_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingCmSceneNav) return;
        _cmSceneFilter = (CmSceneNavList.SelectedItem as CmSceneNavVm)?.Scene ?? "";
        ApplyCmFilter();
    }

    // 列表底部「展示全部」的提示与切换按钮
    private void UpdateCmFooter()
    {
        // 搜索模式下列表已含全部匹配项，不再提示「展示全部」
        if (!string.IsNullOrWhiteSpace(_cmSearchKeyword))
        {
            CmFooter.Visibility = Visibility.Collapsed;
            return;
        }
        if (_cmInventory == null) return;
        int filtered = _cmInventory.Entries.Count(RogueCleanerViewFilters.MatchesMainMenuList);
        int hidden = _cmInventory.Entries.Count - filtered;
        if (_cmAllMode)
        {
            CmHiddenHint.Text = "已显示全部 " + _cmInventory.Entries.Count + " 项（含系统内置与未识别项）";
            CmToggleBtn.Content = "仅显示第三方菜单";
            CmFooter.Visibility = Visibility.Visible;
        }
        else
        {
            CmHiddenHint.Text = "另有 " + hidden + " 项被隐藏（系统内置 / 未识别 / 技术记录）";
            CmToggleBtn.Content = "展示全部 " + _cmInventory.Entries.Count + " 项";
            CmFooter.Visibility = hidden > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void CmToggle_Click(object sender, RoutedEventArgs e)
    {
        _cmAllMode = !_cmAllMode;
        ApplyCmFilter();
    }

    private void CmList_SelectionChanged(object sender, SelectionChangedEventArgs e) => ShowCmDetails();

    private void ShowCmDetails()
    {
        ContextMenuEntry? entry = CmList.SelectedItem as ContextMenuEntry;
        CmDetailsText.Text = entry == null
            ? "请选择一个项目。"
            : "这是什么\r\n" + entry.Name + (string.IsNullOrWhiteSpace(entry.NameReadStatus) ? string.Empty : "\r\n" + entry.NameReadStatus)
            + "\r\n\r\n属于哪个软件\r\n" + (string.IsNullOrEmpty(entry.SoftwareName) ? "来源未确认" : entry.SoftwareName) + "\r\n" + (string.IsNullOrEmpty(entry.IdentityExplanation) ? "正在识别软件来源…" : entry.IdentityExplanation)
            + "\r\n\r\n在哪里出现\r\n" + entry.Scene + "（" + entry.Scope + "）"
            + "\r\n\r\n显示或隐藏的影响\r\n" + (entry.Enabled ? "当前会显示；隐藏后只移除右键入口，不卸载对应软件。" : "当前已隐藏；显示后会恢复右键入口。")
            + "\r\n\r\n技术详情\r\n原始名称：" + (string.IsNullOrWhiteSpace(entry.RawName) ? "无" : entry.RawName)
            + "\r\n类型：" + ChineseDisplayText.ContextMenuType(entry.Type)
            + "\r\n执行命令：" + (string.IsNullOrWhiteSpace(entry.Command) ? "无" : entry.Command)
            + "\r\n组件编号：" + (string.IsNullOrWhiteSpace(entry.Clsid) ? "无" : entry.Clsid)
            + "\r\n注册表位置：" + entry.TechnicalLocation + (entry.ReadOnly ? "\r\n只读原因：" + entry.ReadOnlyReason : string.Empty);
        UpdateCmActions();
    }

    private void UpdateCmActions()
    {
        ContextMenuEntry? entry = CmList.SelectedItem as ContextMenuEntry;
        CmEnableBtn.IsEnabled = entry != null && !entry.ReadOnly && !entry.Enabled;
        CmDisableBtn.IsEnabled = entry != null && !entry.ReadOnly && entry.Enabled;
        CmEditBtn.IsEnabled = entry != null && !entry.ReadOnly
            && !string.Equals(entry.Type, "Shell 扩展", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(entry.Type, "现代右键扩展", StringComparison.OrdinalIgnoreCase);
        CmDeleteBtn.IsEnabled = entry != null && !entry.ReadOnly
            && !string.Equals(entry.Type, "现代右键扩展", StringComparison.OrdinalIgnoreCase);
        CmCopyBtn.IsEnabled = entry != null;
        CmLocationBtn.IsEnabled = entry != null;
    }

    // 行内「显示」列开关点击：直接操作点击行，不依赖选中项
    private async void CmRowToggle_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is ContextMenuEntry entry && !entry.ReadOnly)
            await CmToggle(entry, !entry.Enabled);
    }

    private async void CmEnable_Click(object sender, RoutedEventArgs e) => await CmToggle(CmList.SelectedItem as ContextMenuEntry, true);

    private async void CmDisable_Click(object sender, RoutedEventArgs e) => await CmToggle(CmList.SelectedItem as ContextMenuEntry, false);

    private async Task CmToggle(ContextMenuEntry? entry, bool enabled)
    {
        if (entry == null) return;
        if (entry.RequiresAdmin && !AdminUtil.IsAdministrator())
        {
            await ShowInfo("需要管理员权限", "该项目属于所有用户范围，需要管理员权限。");
            return;
        }
        var dialog = new ContentDialog
        {
            Title = "确认右键菜单操作",
            Content = new TextBlock { Text = "将“" + entry.Name + "”" + (enabled ? "启用" : "禁用") + "？\n\n工具会先保存原值，操作后可在恢复中心还原。", TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = "确定",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        try
        {
            new ContextMenuMutationService(_store).SetEnabled(entry, enabled);
            RefreshContextMenus();
            CmStatusText.Text = "已" + (enabled ? "启用：" : "禁用：") + entry.Name + "，恢复记录已生成。";
        }
        catch (Exception ex)
        {
            Logger.Error("修改右键菜单失败", ex);
            await ShowInfo("修改失败", ex.Message);
        }
    }

    private async void CmEdit_Click(object sender, RoutedEventArgs e) => await ShowCmEditorDialog(CmList.SelectedItem as ContextMenuEntry);

    private async void CmAdd_Click(object sender, RoutedEventArgs e) => await ShowCmEditorDialog(null);

    private async Task ShowCmEditorDialog(ContextMenuEntry? existing)
    {
        var locationCombo = new ComboBox { Header = "作用位置", Width = 320, HorizontalAlignment = HorizontalAlignment.Left };
        var keyNameBox = new TextBox { Header = "内部项名称", Width = 320, HorizontalAlignment = HorizontalAlignment.Left };
        var displayNameBox = new TextBox { Header = "显示名称", Width = 320, HorizontalAlignment = HorizontalAlignment.Left };
        var iconBox = new TextBox { Header = "图标", Width = 320, HorizontalAlignment = HorizontalAlignment.Left, PlaceholderText = "例如 notepad.exe,0（可留空）" };
        var commandBox = new TextBox { Header = "执行命令", Width = 320, HorizontalAlignment = HorizontalAlignment.Left };
        var subCommandsBox = new TextBox { Header = "子菜单引用", Width = 320, HorizontalAlignment = HorizontalAlignment.Left, PlaceholderText = "CommandStore 项名称，多个用分号分隔（可留空）" };
        var helpText = new TextBlock
        {
            Text = "普通菜单填写执行命令；级联子菜单填写 CommandStore 项名称，多个名称用分号分隔。\n图标和子菜单均可留空。添加操作默认写入当前用户，不影响其他账户。",
            FontSize = 11,
            Foreground = new SolidColorBrush(ThemeColors.DimText),
            TextWrapping = TextWrapping.Wrap
        };

        if (existing != null)
        {
            displayNameBox.Text = existing.Name;
            iconBox.Text = existing.Icon;
            commandBox.Text = existing.Command;
            subCommandsBox.Text = existing.SubCommands;
            int slash = existing.SubKey.LastIndexOf('\\');
            keyNameBox.Text = slash < 0 ? existing.SubKey : existing.SubKey.Substring(slash + 1);
            locationCombo.Items.Add(new LocationOption { Scene = existing.Scene, RootSubKey = slash < 0 ? existing.SubKey : existing.SubKey.Substring(0, slash) });
            locationCombo.SelectedIndex = 0;
            locationCombo.IsEnabled = false;
            keyNameBox.IsEnabled = false;
        }
        else
        {
            foreach (var scene in AllScenes()) locationCombo.Items.Add(new LocationOption { Scene = scene, RootSubKey = RootPathForScene(scene) });
            locationCombo.SelectedIndex = 0;
        }

        var panel = new StackPanel { Spacing = 8, Width = 340, Children = { locationCombo, keyNameBox, displayNameBox, iconBox, commandBox, subCommandsBox, helpText } };
        var dialog = new ContentDialog
        {
            Title = existing == null ? "添加右键菜单" : "编辑右键菜单",
            Content = new ScrollViewer { MaxHeight = 560, Content = panel },
            PrimaryButtonText = existing == null ? "添加" : "保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        if (locationCombo.SelectedItem is not LocationOption option) { await ShowInfo("请选择作用位置", "请选择作用位置。"); return; }
        if (string.IsNullOrWhiteSpace(displayNameBox.Text)) { await ShowInfo("请输入显示名称", "请输入显示名称。"); return; }
        if (string.IsNullOrWhiteSpace(commandBox.Text) && string.IsNullOrWhiteSpace(subCommandsBox.Text)) { await ShowInfo("请填写命令或子菜单", "执行命令和子菜单引用至少填写一项。"); return; }
        try
        {
            var mutation = new ContextMenuMutationService(_store);
            if (existing == null)
                mutation.Add(option.Scene, option.RootSubKey, keyNameBox.Text, displayNameBox.Text, iconBox.Text, commandBox.Text, subCommandsBox.Text);
            else
                mutation.Edit(existing, displayNameBox.Text, iconBox.Text, commandBox.Text, subCommandsBox.Text);
            RefreshContextMenus();
            CmStatusText.Text = "已" + (existing == null ? "添加：" : "编辑：") + displayNameBox.Text + "，恢复记录已生成。";
        }
        catch (Exception ex)
        {
            Logger.Error("保存右键菜单失败", ex);
            await ShowInfo(existing == null ? "添加失败" : "编辑失败", ex.Message);
        }
    }

    private sealed class LocationOption
    {
        public string Scene { get; set; } = "";
        public string RootSubKey { get; set; } = "";
        public override string ToString() => Scene;
    }

    private static string[] AllScenes()
    {
        return new[] { "所有文件", "所有文件系统对象", "文件夹", "文件夹背景", "桌面背景", "磁盘", "文件夹对象", "快捷方式", "可执行文件", "未知文件", "命令仓库" };
    }

    private static string RootPathForScene(string scene)
    {
        return scene switch
        {
            "所有文件" => @"Software\Classes\*\shell",
            "所有文件系统对象" => @"Software\Classes\AllFilesystemObjects\shell",
            "文件夹" => @"Software\Classes\Directory\shell",
            "文件夹背景" => @"Software\Classes\Directory\Background\shell",
            "桌面背景" => @"Software\Classes\DesktopBackground\shell",
            "磁盘" => @"Software\Classes\Drive\shell",
            "文件夹对象" => @"Software\Classes\Folder\shell",
            "快捷方式" => @"Software\Classes\lnkfile\shell",
            "可执行文件" => @"Software\Classes\exefile\shell",
            "未知文件" => @"Software\Classes\Unknown\shell",
            "命令仓库" => @"Software\Microsoft\Windows\CurrentVersion\Explorer\CommandStore\shell",
            _ => @"Software\Classes\*\shell"
        };
    }

    private async void CmDelete_Click(object sender, RoutedEventArgs e)
    {
        var entry = CmList.SelectedItem as ContextMenuEntry;
        if (entry == null) return;
        if (entry.RequiresAdmin && !AdminUtil.IsAdministrator()) { await ShowInfo("需要管理员权限", "该项目属于所有用户范围，需要管理员权限。"); return; }
        var dialog = new ContentDialog
        {
            Title = "删除右键菜单",
            Content = new TextBlock { Text = "确定删除“" + entry.Name + "”？\n\n完整注册表结构会先进入恢复中心。", TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        try
        {
            new ContextMenuMutationService(_store).Delete(entry);
            RefreshContextMenus();
            CmStatusText.Text = "已删除：" + entry.Name + "，可在恢复中心还原。";
        }
        catch (Exception ex)
        {
            Logger.Error("删除右键菜单失败", ex);
            await ShowInfo("删除失败", ex.Message);
        }
    }

    private void CmCopy_Click(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(CmDetailsText.Text)) return;
        try
        {
            var data = new DataPackage();
            data.SetText(CmDetailsText.Text);
            Clipboard.SetContent(data);
            Clipboard.Flush();
            CmStatusText.Text = "详情已复制到剪贴板。";
        }
        catch { }
    }

    private void CmLocation_Click(object sender, RoutedEventArgs e)
    {
        var entry = CmList.SelectedItem as ContextMenuEntry;
        if (entry == null) return;
        try
        {
            var data = new DataPackage();
            data.SetText(entry.TechnicalLocation);
            Clipboard.SetContent(data);
            Clipboard.Flush();
            Process.Start(new ProcessStartInfo { FileName = "regedit.exe", UseShellExecute = true });
            CmStatusText.Text = "注册表位置已复制，并已打开注册表编辑器。";
        }
        catch { }
    }

    // ---------- 专用模块视图（对应原版 SpecialContextMenuForm） ----------

    private void InitSpecialView()
    {
        if (SpecialModuleCombo.Items.Count == 0)
        {
            foreach (var m in new[] { "全部模块", "新建菜单", "发送到菜单", "打开方式", "打开方式应用程序", "组件屏蔽" })
                SpecialModuleCombo.Items.Add(m);
            SpecialModuleCombo.SelectedIndex = 0;
        }
        RefreshSpecial();
    }

    private void SpecialModuleCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplySpecialFilter();

    private void SpecialRefresh_Click(object sender, RoutedEventArgs e) => RefreshSpecial();

    private void RefreshSpecial()
    {
        if (!SpecialRefreshBtn.IsEnabled) return;
        SpecialRefreshBtn.IsEnabled = false;
        int generation = ++_specialRefreshGeneration;
        SpecialStatusText.Text = "正在枚举专用模块……";
        Task.Run(() => new SpecialMenuInventoryService(_store).Enumerate())
            .ContinueWith(t =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    SpecialRefreshBtn.IsEnabled = true;
                    if (generation != _specialRefreshGeneration) return; // 已有更新的刷新，丢弃陈旧结果
                    if (t.IsFaulted)
                    {
                        Logger.Error("专用模块枚举失败", t.Exception);
                        SpecialStatusText.Text = "枚举失败：" + (t.Exception?.GetBaseException().Message ?? "未知错误");
                        return;
                    }
                    _specialEntries = t.Result?.Entries ?? [];
                    foreach (var entry in _specialEntries) { entry.SoftwareIcon = null; entry.SoftwareName = "正在识别…"; }
                    ApplySpecialFilter();
                    SoftwarePresentationQueue.Hydrate(DispatcherQueue, _specialEntries, () =>
                    {
                        if (generation == _specialRefreshGeneration) _ = ConvertMenuIconsAsync();
                    });
                    SpecialStatusText.Text = "共发现 " + _specialEntries.Count + " 项；" + (t.Result?.Warnings?.Count ?? 0) + " 个位置未读取。";
                });
            }, TaskScheduler.Default);
    }

    private void ApplySpecialFilter()
    {
        bool searching = !string.IsNullOrWhiteSpace(_cmSearchKeyword);
        if (searching)
        {
            // 搜索模式：忽略模块下拉框，在全部专用模块里匹配
            _visibleSpecialEntries = _specialEntries.Where(e => RogueCleanerViewFilters.MatchesKeyword(e, _cmSearchKeyword)).ToList();
        }
        else
        {
            string selected = SpecialMenuDisplay.Key(Convert.ToString(SpecialModuleCombo.SelectedItem));
            _visibleSpecialEntries = _specialEntries.Where(e => selected == "全部模块" || e.Module == selected).ToList();
        }
        SpecialList.ItemsSource = null;
        SpecialList.ItemsSource = _visibleSpecialEntries;
        UpdateSpecialActions();
        UpdateCmSearchHint();
    }

    private void SpecialList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateSpecialActions();

    private void UpdateSpecialActions()
    {
        var entry = SpecialList.SelectedItem as SpecialMenuEntry;
        SpecialEnableBtn.IsEnabled = entry != null && !entry.ReadOnly && !entry.Enabled;
        SpecialDisableBtn.IsEnabled = entry != null && !entry.ReadOnly && entry.Enabled;
        SpecialDeleteBtn.IsEnabled = entry != null && !entry.ReadOnly && entry.Module != "OpenWith 应用程序";
    }

    private async void SpecialEnable_Click(object sender, RoutedEventArgs e) => await SpecialToggle(true);

    private async void SpecialDisable_Click(object sender, RoutedEventArgs e) => await SpecialToggle(false);

    private async Task SpecialToggle(bool enabled)
    {
        var entry = SpecialList.SelectedItem as SpecialMenuEntry;
        if (entry == null) return;
        if (entry.RequiresAdmin && !AdminUtil.IsAdministrator()) { await ShowInfo("需要管理员权限", "该项目属于所有用户范围，需要管理员权限。"); return; }
        try
        {
            new SpecialContextMenuMutationService(_store).SetEnabled(entry, enabled);
            RefreshSpecial();
        }
        catch (Exception ex) { await ShowInfo("操作失败", ex.Message); }
    }

    private async void SpecialDelete_Click(object sender, RoutedEventArgs e)
    {
        var entry = SpecialList.SelectedItem as SpecialMenuEntry;
        if (entry == null) return;
        if (entry.RequiresAdmin && !AdminUtil.IsAdministrator()) { await ShowInfo("需要管理员权限", "该项目属于所有用户范围，需要管理员权限。"); return; }
        var dialog = new ContentDialog
        {
            Title = "删除专用菜单项",
            Content = new TextBlock { Text = "删除“" + entry.Name + "”？操作前会备份。", TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        try
        {
            new SpecialContextMenuMutationService(_store).Delete(entry);
            RefreshSpecial();
        }
        catch (Exception ex) { await ShowInfo("删除失败", ex.Message); }
    }

    private async void SpecialAdd_Click(object sender, RoutedEventArgs e)
    {
        string selected = SpecialMenuDisplay.Key(Convert.ToString(SpecialModuleCombo.SelectedItem));
        if (selected == "全部模块" || selected == "OpenWith 应用程序")
        {
            await ShowInfo("请先选择模块", "请先选择新建菜单、发送到菜单、打开方式或组件屏蔽。");
            return;
        }
        string firstLabel = selected.StartsWith("ShellNew") || selected.StartsWith("OpenWith") ? "文件扩展名" : selected.StartsWith("SendTo") ? "显示名称" : "组件编号";
        string secondLabel = selected.StartsWith("ShellNew") ? "模板文件（可空）" : selected.StartsWith("OpenWith") ? "程序关联标识" : selected.StartsWith("SendTo") ? "目标路径" : "说明（可空）";
        var firstBox = new TextBox { Header = firstLabel, Width = 320, HorizontalAlignment = HorizontalAlignment.Left };
        var secondBox = new TextBox { Header = secondLabel, Width = 320, HorizontalAlignment = HorizontalAlignment.Left };
        var dialog = new ContentDialog
        {
            Title = "添加 " + SpecialMenuDisplay.Name(selected),
            Content = new StackPanel { Spacing = 8, Children = { firstBox, secondBox } },
            PrimaryButtonText = "添加",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        if (string.IsNullOrWhiteSpace(firstBox.Text) || (!selected.StartsWith("ShellNew") && !selected.StartsWith("GUID") && string.IsNullOrWhiteSpace(secondBox.Text)))
        {
            await ShowInfo("请填写必填项", "请填写必填项。");
            return;
        }
        try
        {
            var service = new SpecialContextMenuMutationService(_store);
            if (selected == "ShellNew 新建菜单") service.AddShellNew(firstBox.Text, secondBox.Text);
            else if (selected == "SendTo 发送到") service.AddSendTo(firstBox.Text, secondBox.Text);
            else if (selected == "OpenWith 打开方式") service.AddOpenWith(firstBox.Text, secondBox.Text);
            else service.AddBlockedGuid(firstBox.Text, secondBox.Text);
            RefreshSpecial();
        }
        catch (Exception ex) { await ShowInfo("添加失败", ex.Message); }
    }

    // ---------- 高级兼容视图（对应原版 AdvancedContextMenuForm） ----------

    private void InitAdvancedView()
    {
        if (AdvancedModuleCombo.Items.Count == 0)
        {
            foreach (var m in new[] { "全部模块", "系统快捷菜单", "Windows 现代菜单", "IE 旧式菜单", "安全增强菜单" })
                AdvancedModuleCombo.Items.Add(m);
            AdvancedModuleCombo.SelectedIndex = 0;
        }
        RefreshAdvanced();
    }

    private void AdvancedModuleCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => ApplyAdvancedFilter();

    private void AdvRefresh_Click(object sender, RoutedEventArgs e) => RefreshAdvanced();

    private void RefreshAdvanced()
    {
        if (!AdvRefreshBtn.IsEnabled) return;
        AdvRefreshBtn.IsEnabled = false;
        int generation = ++_advancedRefreshGeneration;
        AdvancedStatusText.Text = "正在后台枚举高级菜单，不阻塞鼠标……";
        Task.Run(() => new AdvancedMenuInventoryService(_store).Enumerate())
            .ContinueWith(t =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    AdvRefreshBtn.IsEnabled = true;
                    if (generation != _advancedRefreshGeneration) return; // 已有更新的刷新，丢弃陈旧结果
                    if (t.IsFaulted)
                    {
                        Logger.Error("高级菜单枚举失败", t.Exception);
                        AdvancedStatusText.Text = "枚举失败：" + (t.Exception?.GetBaseException().Message ?? "未知错误");
                        return;
                    }
                    _advancedEntries = t.Result?.Entries ?? [];
                    foreach (var entry in _advancedEntries) { entry.SoftwareIcon = null; entry.SoftwareName = "正在识别…"; }
                    ApplyAdvancedFilter();
                    SoftwarePresentationQueue.Hydrate(DispatcherQueue, _advancedEntries, () =>
                    {
                        if (generation == _advancedRefreshGeneration) _ = ConvertMenuIconsAsync();
                    });
                    AdvancedStatusText.Text = "共发现 " + _advancedEntries.Count + " 项；" + (t.Result?.Warnings?.Count ?? 0) + " 个位置已安全跳过。现代菜单仅列出应用包清单明确声明的文件资源管理器命令。";
                });
            }, TaskScheduler.Default);
    }

    private void ApplyAdvancedFilter()
    {
        bool searching = !string.IsNullOrWhiteSpace(_cmSearchKeyword);
        if (searching)
        {
            // 搜索模式：忽略模块下拉框，在全部高级模块里匹配
            _visibleAdvancedEntries = _advancedEntries.Where(e => RogueCleanerViewFilters.MatchesKeyword(e, _cmSearchKeyword)).ToList();
        }
        else
        {
            string selected = AdvancedMenuDisplay.Key(Convert.ToString(AdvancedModuleCombo.SelectedItem));
            _visibleAdvancedEntries = _advancedEntries.Where(e => selected == "全部模块" || e.Module == selected).ToList();
        }
        AdvancedList.ItemsSource = null;
        AdvancedList.ItemsSource = _visibleAdvancedEntries;
        UpdateAdvancedActions();
        UpdateCmSearchHint();
    }

    private void AdvancedList_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateAdvancedActions();

    private void UpdateAdvancedActions()
    {
        var entry = AdvancedList.SelectedItem as AdvancedMenuEntry;
        AdvEnableBtn.IsEnabled = entry != null && !entry.ReadOnly && !entry.Enabled;
        AdvDisableBtn.IsEnabled = entry != null && !entry.ReadOnly && entry.Enabled;
        AdvEditBtn.IsEnabled = entry != null && entry.Module == "IE 旧式菜单";
        AdvDeleteBtn.IsEnabled = entry != null && (entry.Module == "WinX 快捷菜单" || entry.Module == "IE 旧式菜单" || (entry.Module == "安全增强菜单" && entry.Enabled));
        AdvUpBtn.IsEnabled = AdvDownBtn.IsEnabled = entry != null && entry.Module == "WinX 快捷菜单" && entry.Enabled;
        string selected = AdvancedMenuDisplay.Key(Convert.ToString(AdvancedModuleCombo.SelectedItem));
        AdvAddBtn.IsEnabled = selected == "全部模块" || selected == "IE 旧式菜单";
    }

    private async void AdvEnable_Click(object sender, RoutedEventArgs e) => await AdvancedToggle(true);

    private async void AdvDisable_Click(object sender, RoutedEventArgs e) => await AdvancedToggle(false);

    private async Task AdvancedToggle(bool value)
    {
        var entry = AdvancedList.SelectedItem as AdvancedMenuEntry;
        if (entry == null) return;
        if (entry.RequiresAdmin && !AdminUtil.IsAdministrator()) { await ShowInfo("需要管理员权限", "该项目属于所有用户范围，需要管理员权限。"); return; }
        try
        {
            new AdvancedContextMenuMutationService(_store).SetEnabled(entry, value);
            RefreshAdvanced();
        }
        catch (Exception ex) { await ShowInfo("操作失败", ex.Message); }
    }

    private async void AdvDelete_Click(object sender, RoutedEventArgs e)
    {
        var entry = AdvancedList.SelectedItem as AdvancedMenuEntry;
        if (entry == null) return;
        if (entry.RequiresAdmin && !AdminUtil.IsAdministrator()) { await ShowInfo("需要管理员权限", "该项目属于所有用户范围，需要管理员权限。"); return; }
        var dialog = new ContentDialog
        {
            Title = "高级右键兼容",
            Content = new TextBlock { Text = "删除“" + entry.Name + "”？操作前会完整备份。", TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        try
        {
            new AdvancedContextMenuMutationService(_store).Delete(entry);
            RefreshAdvanced();
        }
        catch (Exception ex) { await ShowInfo("删除失败", ex.Message); }
    }

    private async void AdvEdit_Click(object sender, RoutedEventArgs e)
    {
        var entry = AdvancedList.SelectedItem as AdvancedMenuEntry;
        if (entry != null && entry.Module == "IE 旧式菜单") await ShowIeEditorDialog(entry);
    }

    private async void AdvAdd_Click(object sender, RoutedEventArgs e) => await ShowIeEditorDialog(null);

    private async Task ShowIeEditorDialog(AdvancedMenuEntry? existing)
    {
        var nameBox = new TextBox { Header = "菜单名称", Text = existing?.Name ?? "", Width = 320, HorizontalAlignment = HorizontalAlignment.Left };
        var urlBox = new TextBox { Header = "脚本或页面地址", Text = existing?.Detail ?? "", Width = 320, HorizontalAlignment = HorizontalAlignment.Left };
        var contextsBox = new NumberBox { Header = "适用位置代码", Minimum = 0, Maximum = int.MaxValue, Value = existing?.Contexts ?? 0, Width = 320, HorizontalAlignment = HorizontalAlignment.Left };
        var dialog = new ContentDialog
        {
            Title = existing == null ? "添加 IE 旧式菜单" : "编辑 IE 旧式菜单",
            Content = new StackPanel { Spacing = 8, Children = { nameBox, urlBox, contextsBox } },
            PrimaryButtonText = "保存",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        if (string.IsNullOrWhiteSpace(nameBox.Text) || string.IsNullOrWhiteSpace(urlBox.Text))
        {
            await ShowInfo("名称和地址不能为空", "名称和地址不能为空。");
            return;
        }
        try
        {
            new AdvancedContextMenuMutationService(_store).AddOrEditIe(existing, nameBox.Text, urlBox.Text, (int)contextsBox.Value);
            RefreshAdvanced();
        }
        catch (Exception ex) { await ShowInfo("保存失败", ex.Message); }
    }

    private async void AdvUp_Click(object sender, RoutedEventArgs e) => await MoveWinX(-1);

    private async void AdvDown_Click(object sender, RoutedEventArgs e) => await MoveWinX(1);

    private async Task MoveWinX(int direction)
    {
        var entry = AdvancedList.SelectedItem as AdvancedMenuEntry;
        if (entry == null) return;
        if (entry.RequiresAdmin && !AdminUtil.IsAdministrator()) { await ShowInfo("需要管理员权限", "该项目属于所有用户范围，需要管理员权限。"); return; }
        try
        {
            new AdvancedContextMenuMutationService(_store).MoveWinX(entry, direction);
            RefreshAdvanced();
        }
        catch (Exception ex) { await ShowInfo("调整失败", ex.Message); }
    }
    #endregion

    #region 恢复中心

    private void RefreshRecovery()
    {
        Task.Run(() => _cleaner.LoadBatches())
            .ContinueWith(t =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    try { _batches = t.IsFaulted ? [] : (t.Result ?? []); } catch { _batches = []; }
                    var prev = BatchList.SelectedItem;
                    BatchList.ItemsSource = null;
                    BatchList.ItemsSource = _batches;
                    if (prev is CleanupBatch batch && _batches.Any(b => b.Id == batch.Id)) BatchList.SelectedItem = batch;
                    else if (_batches.Count > 0) BatchList.SelectedIndex = 0;
                });
            }, TaskScheduler.Default);
    }

    private void BatchList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var batch = BatchList.SelectedItem as CleanupBatch;
        BatchItemsList.ItemsSource = batch?.Results ?? [];
        BatchItemsTitle.Text = batch is null ? "恢复对象" : "恢复对象 · " + batch.CreatedAt + " · " + (batch.Results?.Count ?? 0) + " 项";
        RecoveryRestoreBtn.IsEnabled = batch != null;
        RecoveryDeleteBtn.IsEnabled = batch != null;
    }

    private async void RecoveryRestore_Click(object sender, RoutedEventArgs e)
    {
        var batch = BatchList.SelectedItem as CleanupBatch;
        if (batch is null) return;
        var dialog = new ContentDialog
        {
            Title = "确认恢复该批次？",
            Content = new TextBlock { Text = $"批次 {batch.CreatedAt}，共 {(batch.Results?.Count ?? 0)} 项。\n\n将还原注册表项/值、文件、服务与计划任务到处理前的状态。", TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = "恢复",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Primary,
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        try
        {
            var summary = await Task.Run(() => _cleaner.RestoreBatch(batch));
            var message = $"成功恢复 {summary.Succeeded} 项，失败 {summary.Failed} 项。\n\n" + string.Join("\n", summary.Messages.Take(10).ToArray());
            if (summary.Failed > 0) message += "\n\n失败的条目会保留在批次中，可稍后重试。";
            await ShowInfo(summary.AllSucceeded ? "恢复完成" : "部分恢复失败", message);
            RefreshRecovery();
        }
        catch (Exception ex)
        {
            await ShowInfo("恢复失败", ex.Message);
        }
    }

    private async void RecoveryDelete_Click(object sender, RoutedEventArgs e)
    {
        var batch = BatchList.SelectedItem as CleanupBatch;
        if (batch is null) return;
        long size = 0;
        try { size = _cleaner.GetBatchStorageBytes(batch); } catch { }
        var dialog = new ContentDialog
        {
            Title = "确认删除该批次？",
            Content = new TextBlock { Text = $"将永久删除批次 {batch.CreatedAt} 的备份数据（{(size > 0 ? FormatSize(size) : "无法计算大小")}），删除后无法恢复。\n\n建议先确认其中项目已不需要还原。", TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = "永久删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        try
        {
            _cleaner.DeleteBatchRecord(batch);
            await ShowInfo("已删除", "批次备份已删除。");
            RefreshRecovery();
        }
        catch (Exception ex)
        {
            await ShowInfo("删除失败", ex.Message);
        }
    }

    private async void RecoveryPrune_Click(object sender, RoutedEventArgs e)
    {
        var old = _cleaner.FindOldBatchRecords(_batches, DateTime.Now, 20, 30);
        if (old.Count == 0)
        {
            await ShowInfo("清理旧记录", "没有超过 30 天且不在最近 20 批内的旧记录。");
            return;
        }
        var dialog = new ContentDialog
        {
            Title = "清理旧记录",
            Content = new TextBlock { Text = $"将删除 {old.Count} 个旧批次（超过 30 天且不在最近 20 批内）。删除后无法恢复。", TextWrapping = TextWrapping.Wrap },
            PrimaryButtonText = "清理",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        try
        {
            foreach (var batch in old) _cleaner.DeleteBatchRecord(batch);
            await ShowInfo("已清理", $"已删除 {old.Count} 个旧批次。");
            RefreshRecovery();
        }
        catch (Exception ex)
        {
            await ShowInfo("清理失败", ex.Message);
        }
    }

    private async void RecoveryRefresh_Click(object sender, RoutedEventArgs e) => RefreshRecovery();

    private static string FormatSize(long bytes)
    {
        string[] units = { "B", "KB", "MB", "GB" };
        double value = bytes;
        int unit = 0;
        while (value >= 1024 && unit < units.Length - 1) { value /= 1024; unit++; }
        return value.ToString(unit == 0 ? "0" : "0.0") + " " + units[unit];
    }

    #endregion

    #region 扫描进度

    private sealed class PageProgressSink : IProgressSink
    {
        private readonly DispatcherQueue _queue;
        private readonly Action<string> _onStage;
        private readonly Action<Finding> _onFinding;

        public PageProgressSink(DispatcherQueue queue, Action<string> onStage, Action<Finding> onFinding)
        {
            _queue = queue;
            _onStage = onStage;
            _onFinding = onFinding;
        }

        public void Stage(string text)
        {
            try { _queue.TryEnqueue(() => _onStage(text)); } catch { }
        }

        public void Finding(Finding finding)
        {
            try { _queue.TryEnqueue(() => _onFinding(finding)); } catch { }
        }
    }

    #endregion
}

#region 转换器

/// <summary>风险等级 → 徽章颜色（高红/中橙/低蓝/仅提示灰）。</summary>
public sealed class RiskToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var risk = value as string;
        var color = risk switch
        {
            "高" => "#C42B1C",
            "中" => "#D97706",
            "低" => "#2563EB",
            _ => "#6B7280"
        };
        return RogueCleanerPage.HexBrush(color);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}

/// <summary>状态 → 徽章颜色。</summary>
public sealed class StatusToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var status = value as string;
        var color = status switch
        {
            "已处理" or "已启用" or "Restored" or "Done" => "#16A34A",
            "失败" or "恢复失败" or "RestoreFailed" or "Failed" => "#DC2626",
            "已打开卸载窗口" or "Launched" => "#2563EB",
            "已禁用" => "#EA580C",
            "已白名单" => "#2563EB",
            _ => "#6B7280"
        };
        return RogueCleanerPage.HexBrush(color);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}

/// <summary>恢复状态 → 中文显示。</summary>
public sealed class StatusTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) => ChineseDisplayText.CleanupStatus(value as string);

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}

/// <summary>主动拦截动作 → 徽章颜色。</summary>
public sealed class ActionToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var action = value as string;
        var color = action switch
        {
            "Blocked" or "Reblocked" => "#C42B1C",
            "Allowed" or "Unblocked" => "#16A34A",
            "BlockedFailed" => "#EA580C",
            _ => "#6B7280"
        };
        return RogueCleanerPage.HexBrush(color);
    }

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}

/// <summary>字符串非空 → Visible。</summary>
public sealed class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
        => string.IsNullOrWhiteSpace(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}

/// <summary>bool → Visible。</summary>
#endregion

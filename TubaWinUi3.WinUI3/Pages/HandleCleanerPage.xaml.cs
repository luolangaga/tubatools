using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using System.Text;
using TubaWinUi3.Services;
using TubaWinUi3.Services.HandleCleaner;
using Windows.UI;

namespace TubaWinUi3.Pages;

/// <summary>进程句柄排行的一行（句柄数与标签来自一次总览快照，失效句柄徽标在扫描后就地刷新）。</summary>
public sealed class HandleCleanerProcessRow : TunnelObservable
{
    public int ProcessId { get; init; }
    public string Name { get; init; } = "";
    public string ImagePath { get; init; } = "";
    public DateTime? StartTimeUtc { get; init; }
    public int TotalHandles { get; init; }
    public int FileHandles { get; init; }
    public bool CleaningAllowed { get; init; }
    public HandleLeakLevel LeakLevel { get; init; }
    public IReadOnlyList<KeyValuePair<string, int>> TopTypes { get; init; } = [];

    public string PidText => ProcessId.ToString();
    public string TotalText => TotalHandles.ToString("N0");
    public string FileText => FileHandles.ToString("N0");
    public string TypesSummary => string.Join(" · ", TopTypes.Select(t => $"{t.Key} {t.Value:N0}"));

    public Visibility ProtectedVisibility => CleaningAllowed ? Visibility.Collapsed : Visibility.Visible;
    public string ProtectedText => LocalizationService.L("HandleCleaner_TagProtected", "受保护");
    public Brush ProtectedBrush => HandleCleanerPage.NeutralBrush;
    public Brush ProtectedBackground => HandleCleanerPage.NeutralBackground;

    public Visibility LeakVisibility => LeakLevel == HandleLeakLevel.Normal ? Visibility.Collapsed : Visibility.Visible;
    public string LeakText => LeakLevel == HandleLeakLevel.Critical
        ? LocalizationService.L("HandleCleaner_TagLeakCritical", "严重泄漏")
        : LocalizationService.L("HandleCleaner_TagLeak", "疑似泄漏");
    public Brush LeakBrush => LeakLevel == HandleLeakLevel.Critical
        ? HandleCleanerPage.CriticalBrush
        : HandleCleanerPage.CautionBrush;
    public Brush LeakBackground => LeakLevel == HandleLeakLevel.Critical
        ? HandleCleanerPage.CriticalBackground
        : HandleCleanerPage.CautionBackground;

    private string _candidateText = "";

    public string CandidateText
    {
        get => _candidateText;
        set
        {
            if (Set(ref _candidateText, value)) Raise(nameof(CandidateVisibility));
        }
    }

    public Visibility CandidateVisibility => _candidateText.Length == 0 ? Visibility.Collapsed : Visibility.Visible;
    public Brush CandidateBrush => HandleCleanerPage.AccentBrush;
    public Brush CandidateBackground => HandleCleanerPage.AccentBackground;
}

/// <summary>「解除文件占用」结果行（按进程勾选）。</summary>
public sealed class HandleCleanerUnlockRow : TunnelObservable
{
    public PathHandleGroup Group { get; init; } = null!;
    public string Title { get; init; } = "";
    public string SamplePath { get; init; } = "";

    private bool _isChecked = true;
    public bool IsChecked
    {
        get => _isChecked;
        set => Set(ref _isChecked, value);
    }
}

/// <summary>句柄浏览器行。</summary>
public sealed class HandleCleanerBrowseRow : TunnelObservable
{
    public ProcessHandleInfo Info { get; init; } = null!;
    public string AccessText { get; init; } = "";
    public string HandleText { get; init; } = "";
    public Brush RiskBrush { get; init; } = HandleCleanerPage.NeutralBrush;
    public Brush RiskBackground { get; init; } = HandleCleanerPage.NeutralBackground;
    public bool CheckEnabled { get; init; } = true;

    private string _typeLabel = "";
    public string TypeLabel
    {
        get => _typeLabel;
        private set => Set(ref _typeLabel, value);
    }

    private string _nameText = "";
    public string NameText
    {
        get => _nameText;
        private set => Set(ref _nameText, value);
    }

    private bool _isChecked;
    public bool IsChecked
    {
        get => _isChecked;
        set => Set(ref _isChecked, value);
    }

    /// <summary>语言切换后就地刷新显示名（不重建列表、不丢勾选）。</summary>
    public void RefreshLocalization()
    {
        TypeLabel = HandleCleanerPolicy.TypeLabel(Info.TypeName);
        NameText = BuildName(Info);
    }

    public static string BuildName(ProcessHandleInfo info)
    {
        string name = info.ObjectName ?? LocalizationService.L("HandleCleaner_BrowserUnnamed", "（未命名对象）");
        if (info.IsDeletedFile)
            name = "[" + LocalizationService.L("HandleCleaner_DeletedTag", "已删除") + "] " + name;
        return name;
    }
}

/// <summary>句柄浏览器的类型筛选项。</summary>
public sealed class HandleCleanerBrowserTypeOption
{
    public ushort? TypeIndex { get; init; }
    public string Display { get; init; } = "";
}

/// <summary>
/// 句柄清理：把「长期开机后系统越来越卡」变成两步操作——
/// 看总览（谁在泄漏）→ 一键安全清理失效句柄（只关闭指向已删除文件的句柄）。
/// 完整的安全边界见 <see cref="HandleCleanerService"/> 与页面「说明」对话框。
/// </summary>
public sealed partial class HandleCleanerPage : Page, ILocalizablePage
{
    // 语义调色板（跟随系统主题：经 ThemeColors 取官方语义色，不再使用固定品牌色）
    public static Color AccentColor => ThemeColors.AccentBlue;
    public static Color SuccessColor => ThemeColors.AccentGreen;
    public static Color CautionColor => ThemeColors.AccentOrange;
    public static Color CriticalColor => ThemeColors.AccentRed;
    public static Color NeutralColor => ThemeColors.Neutral;

    public static Brush AccentBrush => new SolidColorBrush(AccentColor);
    public static Brush SuccessBrush => new SolidColorBrush(SuccessColor);
    public static Brush CautionBrush => new SolidColorBrush(CautionColor);
    public static Brush CriticalBrush => new SolidColorBrush(CriticalColor);
    public static Brush NeutralBrush => new SolidColorBrush(NeutralColor);
    public static Brush AccentBackground => TintFor(AccentColor);
    public static Brush SuccessBackground => TintFor(SuccessColor);
    public static Brush CautionBackground => TintFor(CautionColor);
    public static Brush CriticalBackground => TintFor(CriticalColor);
    public static Brush NeutralBackground => TintFor(NeutralColor);

    private const string DeepModeSettingKey = "HandleCleanerDeepMode";

    private List<HandleCleanerProcessRow> _allRows = [];
    private HandleCleanerOverview? _overview;
    private CandidateScanResult? _lastScan;
    private CancellationTokenSource? _scanCts;
    private DateTime _lastProgressAt;
    private bool _busy;
    private bool _adminRestartBusy;
    private bool _initialized;
    private bool _isPageAlive = true;

    // ---- 深度模式 ----
    private bool _deepMode;
    private PathHandleScanResult? _lastUnlockResult;
    private readonly List<HandleCleanerUnlockRow> _unlockRows = [];
    private bool _unlockIsDirectory;

    // ---- 句柄浏览器 ----
    private HandleCleanerProcessRow? _browserProcess;
    private ProcessHandleListResult? _browserResult;
    private List<HandleCleanerBrowseRow> _browserAll = [];
    private List<HandleCleanerBrowseRow> _browserFiltered = [];
    private ushort? _browserTypeFilter;
    private bool _browseTypeComboReady;
    private bool _browseLoading;
    private bool _browseBusy;
    private CancellationTokenSource? _browseCts;

    public HandleCleanerPage()
    {
        InitializeComponent();
        // 代码构建的语义画刷不会随主题自动刷新，切换主题后按缓存数据重渲染
        ActualThemeChanged += (_, _) =>
        {
            if (_overview is not null)
            {
                RenderHero(_overview);
                RenderRows(_overview);
            }
        };
    }

    public static SolidColorBrush TintFor(Color color)
        => new(Color.FromArgb(28, color.R, color.G, color.B));

    private static string L(string key, string fallback) => LocalizationService.L(key, fallback);

    // ══════════════════ 生命周期 ══════════════════

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        _isPageAlive = true;
        if (_initialized) return;

        _initialized = true;
        ApplyAdminBarTexts();
        RenderLastClean(HandleCleanerState.Load());

        // 模式选择（持久化在 AppSettings；事件会触发 ApplyModeUi）。
        ModeRadios.SelectedIndex = AppSettings.GetBool(DeepModeSettingKey, false) ? 1 : 0;
        ApplyModeUi();

        await RefreshOverviewAsync();
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        _isPageAlive = false;
        _scanCts?.Cancel();
        _browseCts?.Cancel();
    }

    private void ModeRadios_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ModeRadios.SelectedIndex < 0) return;

        bool deep = ModeRadios.SelectedIndex == 1;
        if (deep != _deepMode)
        {
            _deepMode = deep;
            AppSettings.Set(DeepModeSettingKey, deep);
        }
        ApplyModeUi();
    }

    /// <summary>模式切换：安全模式显示「一键安全清理」；深度模式显示解除占用 + 句柄浏览器入口。</summary>
    private void ApplyModeUi()
    {
        bool initialized = HeroQuickCleanPanel is not null;
        if (!initialized) return;

        HeroQuickCleanPanel.Visibility = _deepMode ? Visibility.Collapsed : Visibility.Visible;
        DeepPanel.Visibility = _deepMode ? Visibility.Visible : Visibility.Collapsed;
        RankingHint.Text = _deepMode
            ? L("HandleCleaner_RankingHintDeep", "深度模式：点击任意一行打开句柄浏览器（可关闭任意句柄）；系统关键进程仅可查看。")
            : L("HandleCleaner_RankingHint", "点任意一行查看句柄类型分布，并可单独清理该进程的失效句柄。");
    }

    /// <summary>页头返回：浏览器打开时先回到主视图，而不是退出工具。</summary>
    private void PageHeader_BackRequested(object? sender, EventArgs e)
    {
        if (BrowserView.Visibility == Visibility.Visible)
        {
            BrowserBack_Click(sender ?? this, new RoutedEventArgs());
            return;
        }
        App.MainWindow?.NavigateBack();
    }

    /// <summary>语言切换后重绘自绘文本（Uid 控件由 WinUI3Localizer 自动刷新）。</summary>
    public void ApplyLocalization()
    {
        ApplyAdminBarTexts();
        RenderLastClean(HandleCleanerState.Load());
        ApplyModeUi();

        if (_lastUnlockResult is not null) RenderUnlockResult(_lastUnlockResult);

        if (BrowserView.Visibility == Visibility.Visible && _browserProcess is not null)
        {
            SetBrowserHintForProtection();
            BrowserTitleText.Text = string.Format(L("HandleCleaner_BrowserTitleFormat", "句柄浏览器 · {0}（PID {1}）"),
                _browserProcess.Name, _browserProcess.ProcessId);
            foreach (var row in _browserAll) row.RefreshLocalization();
            ApplyBrowserFilter();
        }

        if (_overview is not null)
        {
            RenderHero(_overview);
            RenderRows(_overview);
        }
    }

    private void ApplyAdminBarTexts()
    {
        AdminBar.Title = L("HandleCleaner_AdminBarTitle", "当前未以管理员身份运行");
        AdminBar.Message = L("HandleCleaner_AdminBarMessage", "部分系统进程的文件句柄读不到，清理范围会受限。以管理员身份重启可完整扫描与清理。");
        AdminBar.IsOpen = !App.IsRunningAsAdmin();
    }

    private void RenderLastClean(HandleCleanerState state)
    {
        LastCleanText.Text = state.LastCleanUtc is { } utc && state.LastFreedHandles > 0
            ? string.Format(L("HandleCleaner_LastCleanFormat", "上次安全清理：{0} · 释放 {1} 个句柄"),
                utc.ToLocalTime().ToString("yyyy-MM-dd HH:mm"), state.LastFreedHandles.ToString("N0"))
            : L("HandleCleaner_LastCleanNone", "还没有清理过");
    }

    // ══════════════════ 忙碌状态 ══════════════════

    private void EnterBusy(bool allowCancel)
    {
        _busy = true;
        QuickCleanButton.IsEnabled = false;
        RefreshButton.IsEnabled = false;
        RestartExplorerButton.IsEnabled = false;
        CopyReportButton.IsEnabled = false;
        ProcessList.IsEnabled = false;
        SearchBox.IsEnabled = false;
        CancelButton.IsEnabled = allowCancel;
        CancelButton.Visibility = allowCancel ? Visibility.Visible : Visibility.Collapsed;
        ProgressPanel.Visibility = Visibility.Visible;
        HeroRing.IsActive = true;
        HeroRing.Visibility = Visibility.Visible;
        _lastProgressAt = DateTime.MinValue;
    }

    private void ExitBusy()
    {
        _busy = false;
        QuickCleanButton.IsEnabled = true;
        RefreshButton.IsEnabled = true;
        RestartExplorerButton.IsEnabled = true;
        CopyReportButton.IsEnabled = true;
        ProcessList.IsEnabled = true;
        SearchBox.IsEnabled = true;
        CancelButton.IsEnabled = false;
        CancelButton.Visibility = Visibility.Collapsed;
        ProgressPanel.Visibility = Visibility.Collapsed;
        HeroRing.IsActive = false;
        HeroRing.Visibility = Visibility.Collapsed;
    }

    private void OnProgress(string text)
    {
        if (!_isPageAlive) return;

        // 千级句柄会产出密集进度：100ms 节流，避免顶栏文案高频刷新造成闪烁。
        var now = DateTime.UtcNow;
        if ((now - _lastProgressAt).TotalMilliseconds < 100) return;
        _lastProgressAt = now;
        ProgressText.Text = text;
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        _scanCts?.Cancel();
        CancelButton.IsEnabled = false;
        ProgressText.Text = L("HandleCleaner_Cancelling", "正在取消…");
    }

    // ══════════════════ 总览 ══════════════════

    private async void RefreshButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        await RefreshOverviewAsync();
    }

    private async Task RefreshOverviewAsync()
    {
        if (_busy || !_isPageAlive) return;

        EnterBusy(allowCancel: false);
        ProgressText.Text = L("HandleCleaner_ReadingOverview", "正在读取系统句柄占用…");
        try
        {
            var overview = await HandleCleanerService.ScanOverviewAsync(CancellationToken.None);
            if (!_isPageAlive) return;

            _overview = overview;
            RenderHero(overview);
            RenderRows(overview);
        }
        catch (Exception ex)
        {
            ShowResult(false, L("HandleCleaner_ScanFailedTitle", "读取句柄占用失败"), ex.Message);
        }
        finally
        {
            ExitBusy();
        }
    }

    private void RenderHero(HandleCleanerOverview overview)
    {
        StatTotalText.Text = overview.TotalHandles.ToString("N0");
        StatProcessText.Text = overview.ProcessCount.ToString("N0");
        StatSuspectText.Text = overview.SuspectedLeakCount.ToString("N0");
        StatSuspectText.Foreground = overview.SuspectedLeakCount > 0 ? CautionBrush : SuccessBrush;

        if (overview.SuspectedLeakCount > 0)
        {
            HeroTitle.Text = string.Format(L("HandleCleaner_HeroSuspectTitle", "发现 {0} 个疑似泄漏进程"), overview.SuspectedLeakCount);
            HeroDetail.Text = string.Format(
                L("HandleCleaner_HeroSuspectDetail", "系统句柄总数 {0:N0}（其中文件句柄 {1:N0}）；长期开机后建议清理一次失效句柄。"),
                overview.TotalHandles, overview.FileHandles);
        }
        else
        {
            HeroTitle.Text = L("HandleCleaner_HeroNormalTitle", "句柄占用正常");
            HeroDetail.Text = string.Format(
                L("HandleCleaner_HeroNormalDetail", "系统句柄总数 {0:N0}（其中文件句柄 {1:N0}），没有发现明显泄漏。"),
                overview.TotalHandles, overview.FileHandles);
        }
    }

    private void RenderRows(HandleCleanerOverview overview)
    {
        _allRows = overview.Processes.Select(p => new HandleCleanerProcessRow
        {
            ProcessId = p.ProcessId,
            Name = p.Name,
            ImagePath = p.ImagePath,
            StartTimeUtc = p.StartTimeUtc,
            TotalHandles = p.TotalHandles,
            FileHandles = p.FileHandles,
            CleaningAllowed = p.CleaningAllowed,
            LeakLevel = p.LeakLevel,
            TopTypes = p.TopTypes,
        }).ToList();

        RankingSummaryText.Text = string.Format(L("HandleCleaner_RankingSummaryFormat", "共 {0} 个进程"), _allRows.Count);
        ApplyFilter();
        if (_lastScan is not null) UpdateRowsWithScan(_lastScan);
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args) => ApplyFilter();

    private void SearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args) => ApplyFilter();

    private void ApplyFilter()
    {
        var query = SearchBox.Text.Trim();
        List<HandleCleanerProcessRow> rows;

        if (query.Length == 0)
        {
            rows = _allRows;
        }
        else
        {
            rows = _allRows.Where(r =>
                    r.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                    || r.PidText.Contains(query, StringComparison.Ordinal)
                    || r.ImagePath.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();
        }

        ProcessList.ItemsSource = rows;
        RankingEmptyPanel.Visibility = rows.Count == 0 && _allRows.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateRowsWithScan(CandidateScanResult scan)
    {
        var counts = scan.Groups
            .Where(g => g.Handles.Count > 0)
            .ToDictionary(g => g.ProcessId, g => g.Handles.Count);

        foreach (var row in _allRows)
        {
            row.CandidateText = counts.TryGetValue(row.ProcessId, out int count)
                ? string.Format(L("HandleCleaner_TagCandidates", "失效 {0}"), count)
                : "";
        }
    }

    // ══════════════════ 一键安全清理 ══════════════════

    private async void QuickClean_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;

        var scan = await RunCandidateScanAsync(null);
        if (scan is null || !_isPageAlive) return;
        await HandleScanThenConfirmAsync(scan);
    }

    private async Task<CandidateScanResult?> RunCandidateScanAsync(int? pidFilter)
    {
        if (_busy) return null;

        EnterBusy(allowCancel: true);
        ProgressText.Text = L("HandleCleaner_ScanningPhase", "正在扫描各进程的文件句柄…");

        var cts = new CancellationTokenSource();
        _scanCts = cts;
        try
        {
            var progress = new Progress<string>(OnProgress);
            var result = await Task.Run(() => HandleCleanerService.ScanCandidates(pidFilter, cts.Token, progress));
            if (!_isPageAlive) return null;

            _lastScan = result;
            UpdateRowsWithScan(result);

            if (result.TotalCandidates > 0)
            {
                ShowResult(true,
                    string.Format(L("HandleCleaner_CandidatesFoundFormat", "发现 {0} 个失效句柄，涉及 {1} 个进程"),
                        result.TotalCandidates, result.AffectedProcessCount),
                    BuildScanSummary(result));
            }

            return result;
        }
        catch (OperationCanceledException)
        {
            ProgressText.Text = L("HandleCleaner_ScanCancelled", "已取消扫描");
            return null;
        }
        catch (Exception ex)
        {
            ShowResult(false, L("HandleCleaner_ScanFailedTitle", "扫描失效句柄失败"), ex.Message);
            return null;
        }
        finally
        {
            if (ReferenceEquals(_scanCts, cts)) _scanCts = null;
            cts.Dispose();
            ExitBusy();
        }
    }

    private async Task HandleScanThenConfirmAsync(CandidateScanResult scan)
    {
        if (scan.FileTypeIndexUnavailable)
        {
            ShowResult(false,
                L("HandleCleaner_TypeIndexFailedTitle", "无法定位文件句柄类型"),
                L("HandleCleaner_TypeIndexFailedDetail", "本次启动未能识别文件对象的类型索引（样本句柄创建失败），请重试或重启程序。"));
            return;
        }

        if (scan.TotalCandidates == 0)
        {
            ShowResult(true, L("HandleCleaner_NoCandidatesTitle", "没有发现失效句柄"), BuildScanSummary(scan));
            return;
        }

        var selected = await ConfirmCleanAsync(scan);
        if (selected is null || selected.Count == 0 || !_isPageAlive) return;
        await RunCleanAsync(selected);
    }

    private async Task<List<CandidateProcessGroup>?> ConfirmCleanAsync(CandidateScanResult scan)
    {
        var listPanel = new StackPanel { Spacing = 6 };
        var checks = new List<(CheckBox Box, CandidateProcessGroup Group)>();

        foreach (var group in scan.Groups.Where(g => g.Handles.Count > 0))
        {
            var content = new StackPanel { Spacing = 2 };
            content.Children.Add(new TextBlock
            {
                FontSize = 14,
                Text = string.Format(L("HandleCleaner_ConfirmProcessFormat", "{0}（PID {1}）— 失效 {2} 个"),
                    group.Name, group.ProcessId, group.Handles.Count),
                TextWrapping = TextWrapping.Wrap,
            });

            string? sample = group.Handles.Select(h => h.DisplayPath).FirstOrDefault(p => p.Length > 0);
            if (sample is not null)
            {
                content.Children.Add(new TextBlock
                {
                    FontSize = 12,
                    Foreground = NeutralBrush,
                    Text = sample + (group.Handles.Count > 1 ? " …" : ""),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                });
            }

            var box = new CheckBox { IsChecked = true, Content = content, MinWidth = 0 };
            listPanel.Children.Add(box);
            checks.Add((box, group));
        }

        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(new TextBlock
        {
            FontSize = 14,
            Text = L("HandleCleaner_ConfirmMessage",
                "将关闭下列进程里指向已删除文件的句柄。这类文件已经不会回来，关闭句柄只是让系统释放残留占用；持有句柄的程序若再次使用它会得到一条错误提示，通常不影响正常使用。"),
            TextWrapping = TextWrapping.Wrap,
        });
        body.Children.Add(listPanel);

        var dialog = CreateDialog(L("HandleCleaner_ConfirmTitle", "确认清理失效句柄"), L("Common_Cancel", "取消"));
        dialog.PrimaryButtonText = string.Format(L("HandleCleaner_ConfirmPrimaryFormat", "清理 {0} 个句柄"), scan.TotalCandidates);
        dialog.DefaultButton = ContentDialogButton.Primary;
        dialog.Content = new ScrollViewer { MaxHeight = 380, Content = body };

        bool confirmed = false;
        dialog.PrimaryButtonClick += (_, _) => confirmed = true;
        await ContentDialogGuard.ShowWhenIdleAsync(dialog, TimeSpan.FromSeconds(10));
        if (!confirmed || !_isPageAlive) return null;

        return checks.Where(c => c.Box.IsChecked == true).Select(c => c.Group).ToList();
    }

    private async Task RunCleanAsync(IReadOnlyList<CandidateProcessGroup> groups)
    {
        if (_busy || groups.Count == 0) return;

        EnterBusy(allowCancel: false);
        ProgressText.Text = L("HandleCleaner_CleaningPhase", "正在清理失效句柄…");

        CleanOutcome outcome;
        try
        {
            var progress = new Progress<string>(OnProgress);
            outcome = await HandleCleanerService.CleanAsync(groups, progress);
        }
        catch (Exception ex)
        {
            ExitBusy();
            ShowResult(false, L("HandleCleaner_CleanFailedTitle", "清理失败"), ex.Message);
            return;
        }
        ExitBusy();
        if (!_isPageAlive) return;

        // 清理后旧的候选计数不再准确：作废本次会话的扫描快照，徽标交给下一次扫描。
        _lastScan = null;

        int involved = groups.Count(g => g.Handles.Count > 0);
        if (outcome.Freed > 0)
        {
            var state = HandleCleanerState.Load();
            state.LastCleanUtc = DateTime.UtcNow;
            state.LastFreedHandles = outcome.Freed;
            state.LastProcessCount = involved;
            state.Save();
            RenderLastClean(state);
        }

        var title = outcome.Freed > 0
            ? string.Format(L("HandleCleaner_CleanDoneFormat", "已释放 {0} 个句柄 · 涉及 {1} 个进程"), outcome.Freed, involved)
            : L("HandleCleaner_CleanNoneFreedTitle", "没有句柄被释放");
        ShowResult(outcome.OtherFailures == 0 && !outcome.TimedOut, title, BuildCleanDetail(outcome));

        await RefreshOverviewAsync();
    }

    private string BuildCleanDetail(CleanOutcome outcome)
    {
        var parts = new List<string>();
        if (outcome.AlreadyGone > 0) parts.Add(string.Format(L("HandleCleaner_CleanSkippedGone", "已自行释放 {0} 个"), outcome.AlreadyGone));
        if (outcome.AccessDenied > 0) parts.Add(string.Format(L("HandleCleaner_CleanSkippedDenied", "权限不足 {0} 个"), outcome.AccessDenied));
        if (outcome.ProcessGone > 0) parts.Add(string.Format(L("HandleCleaner_CleanSkippedProcess", "进程已退出或变化 {0} 个"), outcome.ProcessGone));
        if (outcome.SkippedType > 0) parts.Add(string.Format(L("HandleCleaner_CleanSkippedType", "类型不再匹配 {0} 个"), outcome.SkippedType));
        if (outcome.OtherFailures > 0) parts.Add(string.Format(L("HandleCleaner_CleanSkippedOther", "其他失败 {0} 个"), outcome.OtherFailures));
        if (outcome.TimedOut) parts.Add(L("HandleCleaner_CleanTimedOut", "清理超时，剩余句柄已跳过"));
        if (outcome.FailureSamples.Count > 0) parts.Add(string.Join("\n", outcome.FailureSamples));

        return parts.Count == 0
            ? L("HandleCleaner_CleanAllGood", "全部候选句柄都已处理")
            : string.Join("　·　", parts);
    }

    private string BuildScanSummary(CandidateScanResult scan)
    {
        var parts = new List<string>
        {
            string.Format(L("HandleCleaner_ScanStatsFormat", "已检查 {0:N0} 个文件句柄（用时 {1:F1} 秒）"),
                scan.ScannedFileHandles, scan.Elapsed.TotalSeconds),
        };

        if (scan.TotalCandidates > 0)
            parts.Add(string.Format(L("HandleCleaner_FoundHintFormat", "失效句柄 {0} 个、涉及 {1} 个进程"), scan.TotalCandidates, scan.AffectedProcessCount));
        if (scan.TotalUnknown > 0)
            parts.Add(string.Format(L("HandleCleaner_UnknownHintFormat", "另有 {0:N0} 个句柄无法判定，已跳过（不会清理）"), scan.TotalUnknown));
        if (scan.SkippedProcesses > 0)
            parts.Add(string.Format(L("HandleCleaner_SkippedHintFormat", "{0} 个进程因权限不足被跳过"), scan.SkippedProcesses));
        if (scan.GuardedHandles > 0)
            parts.Add(string.Format(L("HandleCleaner_GuardedHintFormat", "{0} 个句柄因系统响应卡顿被跳过"), scan.GuardedHandles));
        if (scan.Truncated)
            parts.Add(L("HandleCleaner_TruncatedHint", "扫描被截断，结果可能不完整"));

        return string.Join("　·　", parts);
    }

    // ══════════════════ 进程详情 ══════════════════

    private async void ProcessList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (_busy || _browseBusy || e.ClickedItem is not HandleCleanerProcessRow row) return;

        if (_deepMode) await ShowBrowserAsync(row);
        else await ShowProcessDetailAsync(row);
    }

    private async Task ShowProcessDetailAsync(HandleCleanerProcessRow row)
    {
        var body = new StackPanel { Spacing = 10 };
        body.Children.Add(new TextBlock
        {
            FontSize = 12,
            Foreground = NeutralBrush,
            Text = string.Format(L("HandleCleaner_ProcessDetailTotalFormat", "共 {0:N0} 个句柄（文件 {1:N0} 个）"), row.TotalHandles, row.FileHandles),
            TextWrapping = TextWrapping.Wrap,
        });

        if (!row.CleaningAllowed)
        {
            body.Children.Add(new TextBlock
            {
                FontSize = 12,
                Foreground = CautionBrush,
                TextWrapping = TextWrapping.Wrap,
                Text = L("HandleCleaner_ProcessDetailProtected", "该进程属于系统关键进程，工具不会清理它的句柄。"),
            });
        }
        else
        {
            int candidates = _lastScan?.Groups.FirstOrDefault(g => g.ProcessId == row.ProcessId)?.Handles.Count ?? 0;
            body.Children.Add(new TextBlock
            {
                FontSize = 12,
                Foreground = candidates > 0 ? AccentBrush : NeutralBrush,
                TextWrapping = TextWrapping.Wrap,
                Text = candidates > 0
                    ? string.Format(L("HandleCleaner_ProcessDetailCandidates", "已扫描到 {0} 个失效句柄"), candidates)
                    : L("HandleCleaner_ProcessDetailCleanHint", "点击下方按钮可扫描并清理该进程的失效句柄。"),
            });
        }

        if (row.TopTypes.Count > 0)
        {
            body.Children.Add(new TextBlock
            {
                FontSize = 14,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                Text = L("HandleCleaner_ProcessDetailTypes", "句柄类型分布"),
            });
            body.Children.Add(BuildTypeBreakdown(row.TopTypes, row.TotalHandles));
        }

        var dialog = CreateDialog(
            string.Format(L("HandleCleaner_ProcessDetailTitle", "进程详情 · {0}（PID {1}）"), row.Name, row.ProcessId),
            L("Common_GotIt", "知道了"));
        if (row.CleaningAllowed)
        {
            dialog.PrimaryButtonText = L("HandleCleaner_ProcessDetailCleanButton", "清理此进程的失效句柄");
            dialog.DefaultButton = ContentDialogButton.Primary;
        }
        dialog.Content = new ScrollViewer { MaxHeight = 420, Content = body };

        bool cleanRequested = false;
        dialog.PrimaryButtonClick += (_, _) => cleanRequested = true;
        await ContentDialogGuard.ShowWhenIdleAsync(dialog, TimeSpan.FromSeconds(10));
        if (!cleanRequested || !_isPageAlive) return;

        var scan = await RunCandidateScanAsync(row.ProcessId);
        if (scan is null || !_isPageAlive) return;
        await HandleScanThenConfirmAsync(scan);
    }

    private static UIElement BuildTypeBreakdown(IReadOnlyList<KeyValuePair<string, int>> types, int total)
    {
        var panel = new StackPanel { Spacing = 6 };
        foreach (var (label, count) in types)
        {
            var grid = new Grid { ColumnSpacing = 10 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(96) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var labelBlock = new TextBlock
            {
                FontSize = 12,
                Text = label,
                TextTrimming = TextTrimming.CharacterEllipsis,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(labelBlock, 0);
            grid.Children.Add(labelBlock);

            var bar = new ProgressBar
            {
                Minimum = 0,
                Maximum = Math.Max(1, total),
                Value = count,
                Height = 6,
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(bar, 1);
            grid.Children.Add(bar);

            var countBlock = new TextBlock
            {
                FontSize = 12,
                FontFamily = new FontFamily("Consolas"),
                Text = count.ToString("N0"),
                VerticalAlignment = VerticalAlignment.Center,
            };
            Grid.SetColumn(countBlock, 2);
            grid.Children.Add(countBlock);

            panel.Children.Add(grid);
        }
        return panel;
    }

    // ══════════════════ 快速动作 ══════════════════

    private async void RestartExplorer_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;

        var confirmed = await ConfirmAsync(
            L("HandleCleaner_RestartExplorerConfirmTitle", "重启资源管理器"),
            L("HandleCleaner_RestartExplorerConfirmMessage",
                "将结束并重新启动 explorer.exe：已打开的文件夹窗口会关闭，桌面与任务栏短暂消失后自动恢复。\n正在运行的其他程序不受影响。"),
            L("HandleCleaner_RestartExplorerConfirmPrimary", "立即重启"));
        if (!confirmed || !_isPageAlive) return;

        EnterBusy(allowCancel: false);
        ProgressText.Text = L("HandleCleaner_RestartExplorerBusy", "正在重启资源管理器…");
        try
        {
            await Task.Run(ExplorerShellService.Restart);
            ShowResult(true,
                L("HandleCleaner_RestartExplorerDone", "资源管理器已重启"),
                L("HandleCleaner_RestartExplorerDoneDetail", "explorer.exe 及其占用的句柄已随重启释放。"));
        }
        catch (Exception ex)
        {
            ShowResult(false, L("HandleCleaner_RestartExplorerFailed", "重启资源管理器失败"), ex.Message);
        }
        finally
        {
            ExitBusy();
        }

        await RefreshOverviewAsync();
    }

    private async void CopyReport_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;

        try
        {
            var overview = _overview ?? await HandleCleanerService.ScanOverviewAsync(CancellationToken.None);
            var report = HandleCleanerService.BuildReport(overview, _lastScan, App.IsRunningAsAdmin());
            var result = ClipboardService.TrySetText(report);

            if (!result.Success)
            {
                ShowResult(false,
                    L("HandleCleaner_ReportFailedTitle", "复制诊断报告失败"),
                    L("Hw_CopyBusyRetry", "复制失败：剪贴板被其他程序占用，请稍后重试"));
                return;
            }

            ShowResult(true,
                L("HandleCleaner_ReportCopiedTitle", "诊断报告已复制到剪贴板"),
                L("HandleCleaner_ReportCopiedDetail", "内容包含句柄总数、进程排行、失效句柄统计与运行环境，可直接粘贴给他人排查。"));
        }
        catch (Exception ex)
        {
            ShowResult(false, L("HandleCleaner_ReportFailedTitle", "复制诊断报告失败"), ex.Message);
        }
    }

    // ══════════════════ 提权重启 ══════════════════

    /// <summary>
    /// 以管理员身份重启并直达本工具（提权后经 --open-builtin 回到「句柄清理」）。
    /// 打包版经 PowerShell 载体提权（提权进程会丢失包身份，App.TryRelaunchElevated 会带上恢复标记）。
    /// </summary>
    private async void RestartAsAdmin_Click(object sender, RoutedEventArgs e)
    {
        if (_adminRestartBusy) return;
        _adminRestartBusy = true;
        RestartAsAdminBtn.IsEnabled = false;
        RestartAsAdminRing.Visibility = Visibility.Visible;
        RestartAsAdminRing.IsActive = true;

        try
        {
            var arguments = new List<string> { "--open-builtin", "handle-cleaner" };
            var ok = false;
            var detail = string.Empty;
            await Task.Run(() => { ok = App.TryRelaunchElevated(arguments, out detail); });

            if (ok)
            {
                App.RequestExit();
                return;
            }

            await ShowMessageAsync(
                L("HandleCleaner_AdminFailedTitle", "无法提权重启"),
                string.Format(L("HandleCleaner_AdminFailedMessage", "提权请求未成功：{0}"), detail));
        }
        finally
        {
            _adminRestartBusy = false;
            RestartAsAdminBtn.IsEnabled = true;
            RestartAsAdminRing.IsActive = false;
            RestartAsAdminRing.Visibility = Visibility.Collapsed;
        }
    }

    // ══════════════════ 深度模式：解除文件占用 ══════════════════

    private void UnlockPickFile_Click(object sender, RoutedEventArgs e)
    {
        var filter = L("Settings_AllFilesFilter", "所有文件") + "\0*.*\0\0";
        var picked = Win32Dialogs.PickOpen(filter, L("HandleCleaner_UnlockPickFile", "选择文件"));
        if (!string.IsNullOrWhiteSpace(picked)) UnlockPathBox.Text = picked;
    }

    private async void UnlockPickFolder_Click(object sender, RoutedEventArgs e)
    {
        var picked = await Win32Dialogs.PickFolderAsync();
        if (!string.IsNullOrWhiteSpace(picked)) UnlockPathBox.Text = picked;
    }

    private async void UnlockScan_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        await RunUnlockScanAsync();
    }

    private async Task RunUnlockScanAsync()
    {
        var path = UnlockPathBox.Text.Trim();
        if (path.Length == 0)
        {
            ShowUnlockResult(false, L("HandleCleaner_UnlockEmptyPathTitle", "请先填写文件或目录路径"), "");
            return;
        }
        if (_busy) return;

        EnterBusy(allowCancel: true);
        ProgressText.Text = L("HandleCleaner_UnlockScanningPhase", "正在扫描占用该路径的句柄…");

        var cts = new CancellationTokenSource();
        _scanCts = cts;
        try
        {
            var progress = new Progress<string>(OnProgress);
            var result = await HandleCleanerService.FindHandlesForPathAsync(path, null, cts.Token, progress);
            if (!_isPageAlive) return;

            _lastUnlockResult = result;
            RenderUnlockResult(result);
        }
        catch (OperationCanceledException)
        {
            ProgressText.Text = L("HandleCleaner_ScanCancelled", "已取消扫描");
        }
        catch (Exception ex)
        {
            ShowUnlockResult(false, L("HandleCleaner_ScanFailedTitle", "扫描失败"), ex.Message);
        }
        finally
        {
            if (ReferenceEquals(_scanCts, cts)) _scanCts = null;
            cts.Dispose();
            ExitBusy();
        }
    }

    private void RenderUnlockResult(PathHandleScanResult? result)
    {
        _unlockRows.Clear();

        if (result is null)
        {
            UnlockSummaryText.Visibility = Visibility.Collapsed;
            UnlockList.Visibility = Visibility.Collapsed;
            UnlockActionsPanel.Visibility = Visibility.Collapsed;
            return;
        }

        switch (result.Error)
        {
            case PathHandleScanError.EmptyPath:
                ShowUnlockResult(false, L("HandleCleaner_UnlockEmptyPathTitle", "请先填写文件或目录路径"), "");
                HideUnlockList();
                return;
            case PathHandleScanError.NotFound:
                ShowUnlockResult(false, L("HandleCleaner_UnlockPathNotFound", "路径不存在，请检查"), "");
                HideUnlockList();
                return;
            case PathHandleScanError.ResolveFailed:
            case PathHandleScanError.ObjectTypeUnavailable:
                ShowUnlockResult(false, L("HandleCleaner_ScanFailedTitle", "扫描失败"), result.ErrorDetail ?? "");
                HideUnlockList();
                return;
        }

        foreach (var group in result.Groups.Where(g => g.Handles.Count > 0))
        {
            string sample = group.Handles.Select(h => h.DisplayPath).FirstOrDefault(p => p.Length > 0) ?? "";
            var row = new HandleCleanerUnlockRow
            {
                Group = group,
                Title = string.Format(L("HandleCleaner_UnlockGroupFormat", "{0}（PID {1}）— {2} 个句柄"),
                    group.Name, group.ProcessId, group.Handles.Count),
                SamplePath = sample,
            };
            row.PropertyChanged += (_, _) => UpdateUnlockCloseButtonText();
            _unlockRows.Add(row);
        }

        UnlockList.ItemsSource = null;
        UnlockList.ItemsSource = _unlockRows;

        bool any = _unlockRows.Count > 0;
        UnlockList.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        UnlockActionsPanel.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
        UnlockSummaryText.Visibility = Visibility.Visible;
        UnlockSummaryText.Text = any
            ? string.Format(L("HandleCleaner_UnlockFoundFormat", "发现 {0} 个句柄，来自 {1} 个进程；勾选后强制关闭。"),
                result.TotalHandles, result.AffectedProcessCount)
              + (result.Truncated ? "　·　" + L("HandleCleaner_TruncatedHint", "扫描被截断，结果可能不完整") : "")
            : L("HandleCleaner_UnlockEmpty", "没有进程持有该路径的句柄。");

        _unlockIsDirectory = result.IsDirectoryTarget;
        UnlockCloseDeleteButton.IsEnabled = any && !_unlockIsDirectory;
        UpdateUnlockCloseButtonText();
    }

    private void HideUnlockList()
    {
        UnlockSummaryText.Visibility = Visibility.Collapsed;
        UnlockList.Visibility = Visibility.Collapsed;
        UnlockActionsPanel.Visibility = Visibility.Collapsed;
    }

    private void UpdateUnlockCloseButtonText()
    {
        int count = _unlockRows.Where(r => r.IsChecked).Sum(r => r.Group.Handles.Count);
        UnlockCloseButtonText.Text = string.Format(L("HandleCleaner_UnlockCloseSelectedFormat", "关闭选中句柄（{0} 个）"), count);
    }

    private async void UnlockClose_Click(object sender, RoutedEventArgs e) => await RunUnlockCloseAsync(tryDelete: false);

    private async void UnlockCloseDelete_Click(object sender, RoutedEventArgs e) => await RunUnlockCloseAsync(tryDelete: true);

    private async Task RunUnlockCloseAsync(bool tryDelete)
    {
        if (_busy || _lastUnlockResult is null) return;

        var selected = _unlockRows.Where(r => r.IsChecked).Select(r => r.Group).ToList();
        int count = selected.Sum(g => g.Handles.Count);
        if (count == 0)
        {
            ShowUnlockResult(false, L("HandleCleaner_UnlockNothingSelected", "请先勾选要关闭句柄的进程"), "");
            return;
        }

        var confirmed = await ConfirmAsync(
            L("HandleCleaner_UnlockConfirmTitle", "强制关闭句柄"),
            string.Format(L("HandleCleaner_UnlockConfirmMessage",
                "将强制关闭选中的 {0} 个句柄。持有这些句柄的程序下次使用它们时会得到错误，可能导致程序异常或卡死；关闭后文件通常即可删除 / 移动。\n\n已内置保护：句柄值在扫描后被系统回收成别的对象时会自动跳过。\n确认继续？"), count),
            L("HandleCleaner_UnlockConfirmPrimary", "强制关闭"));
        if (!confirmed || !_isPageAlive) return;

        EnterBusy(allowCancel: false);
        ProgressText.Text = L("HandleCleaner_ForceClosePhase", "正在关闭句柄…");

        var closeSucceeded = false;
        try
        {
            var groups = selected.Select(g => new ForceCloseGroup
            {
                ProcessId = g.ProcessId,
                Name = g.Name,
                ImagePath = g.ImagePath,
                StartTimeUtc = g.StartTimeUtc,
                Handles = g.Handles.Select(h => new ForceCloseHandleRequest
                {
                    HandleValue = h.HandleValue,
                    ExpectedTypeIndex = h.TypeIndex,
                    IsFile = true,
                    ExpectedPath = h.DisplayPath.Length > 0 ? h.DisplayPath : null,
                }).ToList(),
            }).ToList();

            var progress = new Progress<string>(OnProgress);
            var outcome = await HandleCleanerService.ForceCloseAsync(groups, CancellationToken.None, progress);
            if (!_isPageAlive) return;

            closeSucceeded = outcome.Freed > 0;
            int skipped = outcome.AlreadyGone + outcome.RecycledSkipped + outcome.Unverifiable
                          + outcome.AccessDenied + outcome.ProcessGone + outcome.ProtectedSkipped;
            ShowUnlockResult(outcome.OtherFailures == 0,
                string.Format(L("HandleCleaner_UnlockDoneFormat", "已关闭 {0} 个句柄（跳过 {1} 个）"), outcome.Freed, skipped),
                BuildForceCloseDetail(outcome));
        }
        catch (Exception ex)
        {
            ShowUnlockResult(false, L("HandleCleaner_CleanFailedTitle", "清理失败"), ex.Message);
        }
        finally
        {
            ExitBusy();
        }

        if (!_isPageAlive) return;

        if (tryDelete && closeSucceeded) await TryDeleteAfterUnlockAsync();
        await RunUnlockScanAsync();
    }

    private async Task TryDeleteAfterUnlockAsync()
    {
        var path = UnlockPathBox.Text.Trim();
        if (path.Length == 0) return;

        if (Directory.Exists(path))
        {
            ShowUnlockResult(false, L("HandleCleaner_UnlockOnlyFiles", "只有文件支持自动删除，目录请手动处理"), "");
            return;
        }

        try
        {
            File.Delete(path);
            ShowUnlockResult(true, L("HandleCleaner_UnlockDeleteOk", "句柄已关闭，文件已删除"), "");
        }
        catch (Exception ex)
        {
            ShowUnlockResult(false, string.Format(L("HandleCleaner_UnlockDeleteFailedFormat", "句柄已关闭，但删除仍失败：{0}"), ex.Message), "");
        }
    }

    private void ShowUnlockResult(bool ok, string title, string? detail)
    {
        UnlockBar.Severity = ok ? InfoBarSeverity.Success : InfoBarSeverity.Error;
        UnlockBar.Title = title;
        UnlockBar.Message = Summarize(detail);
        UnlockBar.ActionButton = !string.IsNullOrEmpty(detail) ? DetailsButton(detail) : null;
        UnlockBar.IsOpen = true;
    }

    private string BuildForceCloseDetail(ForceCloseOutcome outcome)
    {
        var parts = new List<string>();
        if (outcome.AlreadyGone > 0) parts.Add(string.Format(L("HandleCleaner_ForceSkippedGone", "已自行释放 {0} 个"), outcome.AlreadyGone));
        if (outcome.RecycledSkipped > 0) parts.Add(string.Format(L("HandleCleaner_ForceSkippedRecycled", "{0} 个句柄值已被系统复用（对象不同），已跳过"), outcome.RecycledSkipped));
        if (outcome.Unverifiable > 0) parts.Add(string.Format(L("HandleCleaner_ForceSkippedUnverifiable", "{0} 个无法复核目标，已跳过"), outcome.Unverifiable));
        if (outcome.AccessDenied > 0) parts.Add(string.Format(L("HandleCleaner_ForceDenied", "权限不足 {0} 个"), outcome.AccessDenied));
        if (outcome.ProcessGone > 0) parts.Add(string.Format(L("HandleCleaner_ForceProcessGone", "进程已退出或变化 {0} 个"), outcome.ProcessGone));
        if (outcome.ProtectedSkipped > 0) parts.Add(string.Format(L("HandleCleaner_ForceProtected", "系统关键进程受保护 {0} 个"), outcome.ProtectedSkipped));
        if (outcome.OtherFailures > 0) parts.Add(string.Format(L("HandleCleaner_ForceOther", "其他失败 {0} 个"), outcome.OtherFailures));
        if (outcome.Truncated) parts.Add(L("HandleCleaner_TruncatedHint", "扫描被截断，结果可能不完整"));
        if (outcome.FailureSamples.Count > 0) parts.Add(string.Join("\n", outcome.FailureSamples));

        return parts.Count == 0 ? L("HandleCleaner_CleanAllGood", "全部候选句柄都已处理") : string.Join("　·　", parts);
    }

    // ══════════════════ 深度模式：句柄浏览器 ══════════════════

    private async Task ShowBrowserAsync(HandleCleanerProcessRow row)
    {
        _browserProcess = row;
        _browserTypeFilter = null;
        _browserResult = null;
        _browserAll = [];
        _browserFiltered = [];
        BrowserDeletedOnly.IsChecked = false;
        BrowserSearchBox.Text = "";
        BrowserStatusText.Text = "";
        BrowserTitleText.Text = string.Format(L("HandleCleaner_BrowserTitleFormat", "句柄浏览器 · {0}（PID {1}）"),
            row.Name, row.ProcessId);
        SetBrowserHintForProtection();

        MainScroll.Visibility = Visibility.Collapsed;
        BrowserView.Visibility = Visibility.Visible;

        await LoadBrowserAsync(resetTypeCombo: true);
    }

    private void BrowserBack_Click(object sender, RoutedEventArgs e)
    {
        if (_browseBusy) return;

        _browseCts?.Cancel();
        BrowserView.Visibility = Visibility.Collapsed;
        MainScroll.Visibility = Visibility.Visible;
        _browserProcess = null;
        _browserResult = null;
        _browserAll = [];
        _browserFiltered = [];

        _ = RefreshOverviewAsync();   // 关闭过程可能改动了句柄数：回来时刷新一次
    }

    private void SetBrowserHintForProtection()
    {
        bool protectedProcess = _browserProcess is { CleaningAllowed: false };
        BrowserHintText.Text = protectedProcess
            ? L("HandleCleaner_BrowserHintProtected", "该进程属于系统关键进程：句柄可以查看，但工具永远不会关闭它们。")
            : L("HandleCleaner_BrowserHint", "勾选要关闭的句柄。关闭任意句柄都可能影响目标程序（报错 / 卡死），请谨慎操作；系统关键进程仅可查看。");
    }

    private async Task LoadBrowserAsync(bool resetTypeCombo)
    {
        if (_browserProcess is null) return;

        _browseLoading = true;
        SetBrowserBusy(true, null);
        _browseCts?.Dispose();
        var cts = new CancellationTokenSource();
        _browseCts = cts;

        try
        {
            var progress = new Progress<string>(text =>
            {
                if (BrowserLoadingPanel.Visibility == Visibility.Visible) BrowserLoadingText.Text = text;
            });

            var result = await HandleCleanerService.EnumerateHandlesAsync(
                _browserProcess.ProcessId, _browserTypeFilter, cts.Token, progress);
            if (!_isPageAlive || BrowserView.Visibility != Visibility.Visible) return;

            _browserResult = result;
            UpdateBrowserTypeCombo(result, resetTypeCombo);
            BuildBrowserRows(result);
            ApplyBrowserFilter();

            BrowserSubtitleText.Text = string.Format(
                L("HandleCleaner_BrowserSubtitleFormat", "共 {0:N0} 个句柄 · 当前显示 {1:N0} 个"),
                result.TotalInProcess, result.Handles.Count)
                + (result.Truncated
                    ? "　·　" + string.Format(L("HandleCleaner_BrowserTruncatedFormat", "句柄过多，仅显示前 {0:N0} 个，可按类型筛选"),
                        HandleCleanerService.MaxEnumerateHandles)
                    : "");
        }
        catch (OperationCanceledException)
        {
            // 关页/切页时的正常取消
        }
        catch (Exception ex)
        {
            BrowserStatusText.Text = string.Format(L("HandleCleaner_BrowserFailedTitle", "读取句柄失败：{0}"), ex.Message);
        }
        finally
        {
            if (ReferenceEquals(_browseCts, cts)) _browseCts = null;
            cts.Dispose();
            _browseLoading = false;
            SetBrowserBusy(false, null);
            UpdateBrowserCloseButtonText();
        }
    }

    private void SetBrowserBusy(bool busy, string? status)
    {
        _browseBusy = busy;
        BrowserRefreshButton.IsEnabled = !busy;
        BrowserCopyButton.IsEnabled = !busy;
        BrowserTypeCombo.IsEnabled = !busy;
        BrowserSearchBox.IsEnabled = !busy;
        BrowserDeletedOnly.IsEnabled = !busy;
        BrowserSelectAllButton.IsEnabled = !busy;
        BrowserClearButton.IsEnabled = !busy;
        BrowserList.IsEnabled = !busy;
        BrowserBusyRing.IsActive = busy;
        BrowserBusyRing.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        BrowserLoadingPanel.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        BrowserLoadingRing.IsActive = busy;
        if (status is not null) BrowserStatusText.Text = status;
    }

    private void UpdateBrowserTypeCombo(ProcessHandleListResult result, bool resetSelection)
    {
        var options = new List<HandleCleanerBrowserTypeOption>
        {
            new()
            {
                TypeIndex = null,
                Display = string.Format(L("HandleCleaner_BrowserTypeAllFormat", "全部类型（{0:N0}）"), result.TotalInProcess),
            },
        };
        options.AddRange(result.TypeCounts.Select(t => new HandleCleanerBrowserTypeOption
        {
            TypeIndex = t.TypeIndex,
            Display = string.Format(L("HandleCleaner_BrowserTypeFormat", "{0}（{1:N0}）"),
                HandleCleanerPolicy.TypeLabel(t.TypeName), t.Count),
        }));

        _browseTypeComboReady = false;
        BrowserTypeCombo.ItemsSource = options;
        BrowserTypeCombo.SelectedIndex = resetSelection || _browserTypeFilter is null
            ? 0
            : Math.Max(0, options.FindIndex(o => o.TypeIndex == _browserTypeFilter));
        _browseTypeComboReady = true;
    }

    private void BuildBrowserRows(ProcessHandleListResult result)
    {
        bool protectedProcess = _browserProcess is { CleaningAllowed: false };
        var rows = new List<HandleCleanerBrowseRow>(result.Handles.Count);

        foreach (var info in result.Handles)
        {
            var (brush, background) = info.Risk switch
            {
                HandleRisk.Low => (SuccessBrush, SuccessBackground),
                HandleRisk.High => (CriticalBrush, CriticalBackground),
                _ => (CautionBrush, CautionBackground),
            };

            var row = new HandleCleanerBrowseRow
            {
                Info = info,
                AccessText = info.AccessText,
                HandleText = "0x" + info.HandleValue.ToString("X"),
                RiskBrush = brush,
                RiskBackground = background,
                CheckEnabled = !protectedProcess,
            };
            row.RefreshLocalization();
            row.PropertyChanged += (_, _) => UpdateBrowserCloseButtonText();
            rows.Add(row);
        }

        _browserAll = rows;
        UpdateBrowserCloseButtonText();
    }

    private async void BrowserTypeCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_browseTypeComboReady) return;

        ushort? newFilter = (BrowserTypeCombo.SelectedItem as HandleCleanerBrowserTypeOption)?.TypeIndex;
        if (newFilter == _browserTypeFilter) return;

        _browserTypeFilter = newFilter;
        await LoadBrowserAsync(resetTypeCombo: false);
    }

    private void BrowserSearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (_browseLoading) return;
        ApplyBrowserFilter();
    }

    private void BrowserSearchBox_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        if (_browseLoading) return;
        ApplyBrowserFilter();
    }

    private void BrowserDeletedOnly_Changed(object sender, RoutedEventArgs e)
    {
        if (_browseLoading) return;
        ApplyBrowserFilter();
    }

    private void ApplyBrowserFilter()
    {
        var query = BrowserSearchBox.Text.Trim();
        bool deletedOnly = BrowserDeletedOnly.IsChecked == true;

        IEnumerable<HandleCleanerBrowseRow> rows = _browserAll;
        if (deletedOnly) rows = rows.Where(r => r.Info.IsDeletedFile);
        if (query.Length > 0)
        {
            rows = rows.Where(r =>
                (r.Info.ObjectName?.Contains(query, StringComparison.OrdinalIgnoreCase) ?? false)
                || r.HandleText.Contains(query, StringComparison.OrdinalIgnoreCase)
                || r.TypeLabel.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        _browserFiltered = rows.ToList();
        BrowserList.ItemsSource = _browserFiltered;
        BrowserList.Visibility = _browserFiltered.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        BrowserEmptyText.Visibility = _browserFiltered.Count == 0 && !_browseLoading ? Visibility.Visible : Visibility.Collapsed;
        BrowserCountText.Text = string.Format(L("HandleCleaner_BrowserCountFormat", "显示 {0:N0} / {1:N0}"),
            _browserFiltered.Count, _browserAll.Count);
    }

    private void BrowserSelectAll_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in _browserFiltered)
        {
            if (row.CheckEnabled) row.IsChecked = true;
        }
        UpdateBrowserCloseButtonText();
    }

    private void BrowserClear_Click(object sender, RoutedEventArgs e)
    {
        foreach (var row in _browserAll) row.IsChecked = false;
        UpdateBrowserCloseButtonText();
    }

    private async void BrowserRefresh_Click(object sender, RoutedEventArgs e) => await LoadBrowserAsync(resetTypeCombo: false);

    private void UpdateBrowserCloseButtonText()
    {
        int count = _browserAll.Count(r => r.IsChecked);
        BrowserCloseSelectedText.Text = count > 0
            ? string.Format(L("HandleCleaner_BrowserCloseSelectedFormat", "关闭选中（{0} 个）"), count)
            : L("HandleCleaner_BrowserCloseSelectedIdle", "关闭选中");
        BrowserCloseSelectedButton.IsEnabled = !_browseBusy && count > 0 && _browserProcess is { CleaningAllowed: true };
    }

    private void BrowserCopy_Click(object sender, RoutedEventArgs e)
    {
        if (_browserFiltered.Count == 0)
        {
            BrowserStatusText.Text = L("HandleCleaner_BrowserNothingToCopy", "没有可复制的句柄");
            return;
        }

        var text = new StringBuilder();
        text.AppendLine(L("HandleCleaner_BrowserCopyHeader", "类型\t访问权限\t句柄\t对象名"));
        foreach (var row in _browserFiltered)
            text.AppendLine($"{row.Info.TypeName}\t{row.AccessText}\t{row.HandleText}\t{row.Info.ObjectName ?? ""}");

        var result = ClipboardService.TrySetText(text.ToString());
        BrowserStatusText.Text = result.Success
            ? string.Format(L("HandleCleaner_BrowserCopied", "已复制 {0:N0} 行句柄列表到剪贴板"), _browserFiltered.Count)
            : L("Hw_CopyBusyRetry", "复制失败：剪贴板被其他程序占用，请稍后重试");
    }

    private async void BrowserCloseSelected_Click(object sender, RoutedEventArgs e)
    {
        if (_browseBusy || _browserProcess is null) return;

        if (!_browserProcess.CleaningAllowed)
        {
            BrowserStatusText.Text = L("HandleCleaner_BrowserProtected", "系统关键进程的句柄受保护，不能关闭。");
            return;
        }

        var selected = _browserAll.Where(r => r.IsChecked).ToList();
        if (selected.Count == 0) return;

        var confirmed = await ConfirmAsync(
            L("HandleCleaner_BrowserConfirmTitle", "强制关闭句柄"),
            string.Format(L("HandleCleaner_BrowserConfirmBody",
                "将强制关闭选中的 {0} 个句柄（{1}）。关闭后目标程序若继续使用这些句柄会得到错误，可能导致报错、卡顿或功能异常。\n\n已内置保护：句柄值在扫描后若被系统回收成别的对象，会被自动跳过。\n确认继续？"),
                selected.Count, DescribeCheckedRisks(selected)),
            L("HandleCleaner_UnlockConfirmPrimary", "强制关闭"));
        if (!confirmed || !_isPageAlive) return;

        var group = new ForceCloseGroup
        {
            ProcessId = _browserProcess.ProcessId,
            Name = _browserProcess.Name,
            ImagePath = _browserProcess.ImagePath,
            StartTimeUtc = _browserProcess.StartTimeUtc,
            Handles = selected.Select(r => new ForceCloseHandleRequest
            {
                HandleValue = r.Info.HandleValue,
                ExpectedTypeIndex = r.Info.TypeIndex,
                IsFile = r.Info.TypeName == "File",
                ExpectedPath = r.Info.TypeName == "File" ? r.Info.ObjectName : null,
            }).ToList(),
        };

        SetBrowserBusy(true, L("HandleCleaner_ForceClosePhase", "正在关闭句柄…"));
        try
        {
            var outcome = await HandleCleanerService.ForceCloseAsync([group], CancellationToken.None);
            if (!_isPageAlive) return;

            int skipped = outcome.AlreadyGone + outcome.RecycledSkipped + outcome.Unverifiable
                          + outcome.AccessDenied + outcome.ProcessGone + outcome.ProtectedSkipped;
            BrowserStatusText.Text = string.Format(
                L("HandleCleaner_BrowserCloseDoneFormat", "已关闭 {0} 个 · 跳过 {1} 个 · 失败 {2} 个"),
                outcome.Freed, skipped, outcome.OtherFailures);
        }
        catch (Exception ex)
        {
            BrowserStatusText.Text = string.Format(L("HandleCleaner_BrowserCloseFailedTitle", "关闭句柄失败：{0}"), ex.Message);
        }
        finally
        {
            SetBrowserBusy(false, null);
        }

        await LoadBrowserAsync(resetTypeCombo: false);
    }

    private string DescribeCheckedRisks(List<HandleCleanerBrowseRow> rows)
    {
        int low = rows.Count(r => r.Info.Risk == HandleRisk.Low);
        int high = rows.Count(r => r.Info.Risk == HandleRisk.High);
        int medium = rows.Count - low - high;

        var parts = new List<string>();
        if (low > 0) parts.Add(string.Format(L("HandleCleaner_RiskSummaryLow", "低风险 {0}"), low));
        if (medium > 0) parts.Add(string.Format(L("HandleCleaner_RiskSummaryMedium", "中风险 {0}"), medium));
        if (high > 0) parts.Add(string.Format(L("HandleCleaner_RiskSummaryHigh", "高风险 {0}"), high));
        return string.Join("、", parts);
    }

    // ══════════════════ 说明 / 结果 / 对话框 ══════════════════

    private async void HelpButton_Click(object sender, RoutedEventArgs e)
    {
        var body = new StackPanel { Spacing = 12 };
        body.Children.Add(new TextBlock
        {
            TextWrapping = TextWrapping.Wrap,
            LineHeight = 21,
            FontSize = 14,
            Text = L("HandleCleaner_HelpBody", """
            它能做什么
            · 总览：全系统句柄总数、每个进程的句柄占用排行，超过 5 万的进程会标「疑似泄漏」（20 万以上标红）。
            · 一键安全清理：关闭「指向已删除文件」的失效句柄 —— 文件已经不会回来，这类句柄只是残留占用。
            · 进程详情：点排行里任意一行，可看句柄类型分布，并单独清理该进程的失效句柄。
            · 快速动作：重启资源管理器（句柄泄漏的头号常客）、复制诊断报告。

            为什么只清理失效句柄
            · 事件、互斥体、注册表、进程、线程等句柄直接关闭可能破坏程序的同步状态，本工具一律不碰；
              只有指向已删除文件的 File 句柄是公认可以安全释放的（文件本身已不可用）。

            安全措施
            · 系统关键进程（System、csrss、lsass、services、winlogon 等）永不清理；
            · 清理前重新核对进程身份（防 PID 复用）并再次确认句柄仍失效，随后立即关闭；
            · 非管理员运行时，受保护进程读不到，会如实显示「跳过进程」数量。

            实现依据（官方文档）
            · 关闭远程句柄：DuplicateHandle + DUPLICATE_CLOSE_SOURCE；
            · 删除态判断：GetFileInformationByHandleEx(FileStandardInfo) 的 DeletePending。
            · 句柄表枚举 NtQuerySystemInformation(SystemExtendedHandleInformation) 为 Windows 内部接口（与本仓库「文件占用查看」同一实现思路，对标 Microsoft PowerToys File Locksmith）。

            建议
            · 长期开机（一周以上）或感觉系统变卡时，先看排行（谁占用最多），再一键安全清理；
            · 句柄数长期居高不下的程序，建议重启该程序或找它的新版本。
            """)
        });

        var links = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 14 };
        links.Children.Add(MakeHyperlink(
            L("HandleCleaner_HelpLink1", "DuplicateHandle 官方文档"),
            "https://learn.microsoft.com/windows/win32/api/handleapi/nf-handleapi-duplicatehandle"));
        links.Children.Add(MakeHyperlink(
            L("HandleCleaner_HelpLink2", "FILE_STANDARD_INFO 官方文档"),
            "https://learn.microsoft.com/windows/win32/api/fileapi/nf-fileapi-getfileinformationbyhandleex"));
        body.Children.Add(links);

        var dialog = CreateDialog(L("HandleCleaner_HelpTitle", "句柄清理怎么用"), L("Common_GotIt", "知道了"));
        dialog.Content = new ScrollViewer { MaxHeight = 440, Content = body };
        await ContentDialogGuard.ShowWhenIdleAsync(dialog, TimeSpan.FromSeconds(10));
    }

    private static HyperlinkButton MakeHyperlink(string text, string url) => new()
    {
        Content = text,
        NavigateUri = new Uri(url),
        Padding = new Thickness(0),
    };

    private void ShowResult(bool ok, string title, string? detail)
    {
        ResultBar.Severity = ok ? InfoBarSeverity.Success : InfoBarSeverity.Error;
        ResultBar.Title = title;
        ResultBar.Message = Summarize(detail);
        ResultBar.ActionButton = detail is { Length: > 0 } ? DetailsButton(detail) : null;
        ResultBar.IsOpen = true;
    }

    private Button DetailsButton(string detail)
    {
        var button = new Button { Content = L("HandleCleaner_ViewDetails", "查看详情") };
        button.Click += async (_, _) => await ShowMessageAsync(L("HandleCleaner_ActionDetails", "操作详情"), detail);
        return button;
    }

    private static string Summarize(string? detail)
    {
        if (string.IsNullOrEmpty(detail)) return "";
        var firstLine = detail.Split('\n').FirstOrDefault(l => l.Trim().Length > 0)?.Trim() ?? "";
        return firstLine.Length > 160 ? firstLine[..160] + "…" : firstLine;
    }

    private ContentDialog CreateDialog(string title, string closeText)
        => new()
        {
            Title = title,
            CloseButtonText = closeText,
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme,
        };

    private async Task ShowMessageAsync(string title, string message)
    {
        var dialog = CreateDialog(title, L("Common_GotIt", "知道了"));
        dialog.Content = new ScrollViewer
        {
            MaxHeight = 380,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, LineHeight = 20, FontSize = 14 },
        };
        await ContentDialogGuard.ShowWhenIdleAsync(dialog, TimeSpan.FromSeconds(10));
    }

    private async Task<bool> ConfirmAsync(string title, string message, string primaryText)
    {
        var dialog = CreateDialog(title, L("Common_Cancel", "取消"));
        dialog.PrimaryButtonText = primaryText;
        dialog.DefaultButton = ContentDialogButton.Primary;
        dialog.Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, LineHeight = 20, FontSize = 14 };

        bool confirmed = false;
        dialog.PrimaryButtonClick += (_, _) => confirmed = true;
        await ContentDialogGuard.ShowWhenIdleAsync(dialog, TimeSpan.FromSeconds(10));
        return confirmed;
    }
}

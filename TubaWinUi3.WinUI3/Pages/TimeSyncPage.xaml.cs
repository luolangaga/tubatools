using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Services;
using Windows.UI;

namespace TubaWinUi3.Pages;

/// <summary>时间服务器卡片（预设或自定义入口）。状态徽标在测速后就地刷新。</summary>
public sealed class TimeSyncServerCard : TunnelObservable
{
    public TimeSyncServerCard Self => this;
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Summary { get; init; } = "";
    public string HostText { get; init; } = "";
    public string Tooltip { get; init; } = "";
    public bool IsCustom { get; init; }
    public IReadOnlyList<string> Hosts { get; init; } = [];

    private bool _selected;
    public bool IsSelected
    {
        get => _selected;
        set
        {
            if (!Set(ref _selected, value)) return;
            Raise(nameof(BorderBrush));
            Raise(nameof(BorderThickness));
            Raise(nameof(CheckVisibility));
        }
    }

    public Brush BorderBrush => _selected ? TimeSyncPage.AccentBrush : TimeSyncPage.CardStrokeBrush;
    public Thickness BorderThickness => _selected ? new Thickness(2) : new Thickness(1);
    public Visibility CheckVisibility => _selected ? Visibility.Visible : Visibility.Collapsed;

    private string _statusText = "";
    private Brush _statusBrush = TimeSyncPage.NeutralBrush;
    private Brush _statusBackground = TimeSyncPage.NeutralBackground;

    public string StatusText
    {
        get => _statusText;
        private set
        {
            if (!Set(ref _statusText, value)) return;
            Raise(nameof(StatusVisibility));
        }
    }

    public Brush StatusBrush
    {
        get => _statusBrush;
        private set => Set(ref _statusBrush, value);
    }

    public Brush StatusBackground
    {
        get => _statusBackground;
        private set => Set(ref _statusBackground, value);
    }

    public Visibility StatusVisibility => _statusText.Length == 0 ? Visibility.Collapsed : Visibility.Visible;

    public void ClearStatus() => SetStatus("", TimeSyncPage.NeutralColor);

    public void SetStatus(string text, Color color)
    {
        StatusText = text;
        StatusBrush = new SolidColorBrush(color);
        StatusBackground = TimeSyncPage.TintFor(color);
    }
}

/// <summary>体检结果里的一条。</summary>
public sealed class TimeSyncIssueItem
{
    public string Title { get; init; } = "";
    public string Detail { get; init; } = "";
    public string Glyph { get; init; } = "\uE7BA";
    public Brush Brush { get; init; } = TimeSyncPage.CautionBrush;
    public Brush Background { get; init; } = TimeSyncPage.CautionBackground;

    public static TimeSyncIssueItem From(TimeSyncIssue issue)
    {
        var (glyph, color, background) = issue.Level switch
        {
            TimeSyncIssueLevel.Critical => ("\uEA39", TimeSyncPage.CriticalBrush, TimeSyncPage.CriticalBackground),
            TimeSyncIssueLevel.Warning => ("\uE7BA", TimeSyncPage.CautionBrush, TimeSyncPage.CautionBackground),
            _ => ("\uE946", TimeSyncPage.AccentBrush, TimeSyncPage.AccentBackground),
        };

        return new TimeSyncIssueItem
        {
            Title = issue.Title,
            Detail = issue.Detail,
            Glyph = glyph,
            Brush = color,
            Background = background,
        };
    }
}

/// <summary>
/// 时间同步：把「网络正常但时间不对」这类问题变成几步操作——
/// 看状态 → 挑一台连得上的 NTP 服务器（可先测速）→ 应用并校时，必要时一键修复时间服务。
/// 依赖 Windows 自带 w32tm / 时间服务，不装驱动、不驻留后台。
/// </summary>
public sealed partial class TimeSyncPage : Page, ILocalizablePage
{
    // 品牌调色板（与其它内置工具页一致）
    public static readonly Color AccentColor = Color.FromArgb(255, 91, 141, 239);
    public static readonly Color SuccessColor = Color.FromArgb(255, 43, 182, 115);
    public static readonly Color CautionColor = Color.FromArgb(255, 245, 166, 35);
    public static readonly Color CriticalColor = Color.FromArgb(255, 242, 80, 59);
    public static readonly Color NeutralColor = Color.FromArgb(255, 142, 142, 142);

    public static readonly Brush AccentBrush = new SolidColorBrush(AccentColor);
    public static readonly Brush SuccessBrush = new SolidColorBrush(SuccessColor);
    public static readonly Brush CautionBrush = new SolidColorBrush(CautionColor);
    public static readonly Brush CriticalBrush = new SolidColorBrush(CriticalColor);
    public static readonly Brush NeutralBrush = new SolidColorBrush(NeutralColor);
    public static readonly Brush CardStrokeBrush = new SolidColorBrush(Color.FromArgb(38, 128, 128, 128));
    public static readonly Brush AccentBackground = TintFor(AccentColor);
    public static readonly Brush SuccessBackground = TintFor(SuccessColor);
    public static readonly Brush CautionBackground = TintFor(CautionColor);
    public static readonly Brush CriticalBackground = TintFor(CriticalColor);
    public static readonly Brush NeutralBackground = TintFor(NeutralColor);

    private const string CustomCardId = "custom";

    private readonly List<TimeSyncServerCard> _cards = [];
    private readonly List<(string Host, NtpProbeResult Result)> _probes = [];

    private DispatcherQueueTimer? _clockTimer;
    private TimeSyncSettings _settings = new();
    private TimeSyncSnapshot? _snapshot;
    private TimeSyncServerCard? _selectedCard;
    private NtpProbeResult? _driftProbe;

    private bool _busy;
    private bool _probing;
    private bool _rendering;
    private bool _initialized;

    public TimeSyncPage()
    {
        InitializeComponent();
    }

    public static SolidColorBrush TintFor(Color color)
        => new(Color.FromArgb(28, color.R, color.G, color.B));

    // ══════════════════ 生命周期 ══════════════════

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        StartClock();
        if (_initialized) return;

        _initialized = true;
        _settings = TimeSyncCatalog.LoadSettings();
        BuildCards();
        await RefreshAsync(includeConfiguration: false);
        _ = ProbeAllAsync(includeCustom: false);
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        _clockTimer?.Stop();
        _clockTimer = null;
    }

    private void StartClock()
    {
        UpdateClock();
        _clockTimer ??= DispatcherQueue.CreateTimer();
        _clockTimer.Interval = TimeSpan.FromSeconds(1);
        _clockTimer.Tick -= ClockTimer_Tick;
        _clockTimer.Tick += ClockTimer_Tick;
        _clockTimer.Start();
    }

    private void ClockTimer_Tick(DispatcherQueueTimer sender, object args) => UpdateClock();

    private void UpdateClock()
    {
        var now = DateTime.Now;
        ClockText.Text = now.ToString("HH:mm:ss");
        ClockDateText.Text = now.ToString("yyyy-MM-dd dddd");
    }

    // ══════════════════ 卡片 ══════════════════

    private void BuildCards()
    {
        foreach (var preset in TimeSyncCatalog.Presets)
        {
            var card = new TimeSyncServerCard
            {
                Id = preset.Id,
                Name = LocalizationService.L($"TimeSync_Preset_{preset.Id}_Name", preset.Name),
                Summary = LocalizationService.L($"TimeSync_Preset_{preset.Id}_Summary", preset.Summary),
                HostText = string.Join(" · ", preset.Hosts),
                Tooltip = LocalizationService.L($"TimeSync_Preset_{preset.Id}_Note", preset.Note),
                Hosts = preset.Hosts,
            };
            _cards.Add(card);
        }

        _cards.Add(new TimeSyncServerCard
        {
            Id = CustomCardId,
            Name = LocalizationService.L("TimeSync_CustomCardName", "自定义"),
            Summary = LocalizationService.L("TimeSync_CustomCardSummary", "填自己的 NTP 服务器地址"),
            HostText = LocalizationService.L("TimeSync_CustomCardHost", "手动输入"),
            Tooltip = LocalizationService.L("TimeSync_CustomCardTooltip", "填入任意 NTP 服务器（域名或 IP），测速正常即可应用。"),
            IsCustom = true,
        });

        ServerList.ItemsSource = _cards;
    }

    private void ServerCard_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: TimeSyncServerCard card }) SelectCard(card);
    }

    private void SelectCard(TimeSyncServerCard card)
    {
        _selectedCard = card;
        foreach (var item in _cards) item.IsSelected = ReferenceEquals(item, card);

        CustomPanel.Visibility = card.IsCustom ? Visibility.Visible : Visibility.Collapsed;
        if (card.IsCustom)
        {
            if (CustomHostBox.Text.Length == 0 && _settings.CustomHosts.Length > 0)
                CustomHostBox.Text = _settings.CustomHosts;
            CustomHostBox.Focus(FocusState.Programmatic);
        }

        UpdateSelectedHint();
    }

    private void UpdateSelectedHint()
    {
        if (_selectedCard is null)
        {
            SelectedHint.Text = LocalizationService.L("TimeSync_SelectServerFirst", "先选一台服务器");
            return;
        }

        var hosts = SelectedHosts();
        if (hosts.Count == 0)
        {
            SelectedHint.Text = LocalizationService.L("TimeSync_EnterCustomAddress", "填写自定义地址后再应用");
            return;
        }

        var interval = SelectedIntervalSeconds();
        SelectedHint.Text = string.Format(LocalizationService.L("TimeSync_WillWrite", "将写入：{0}"), TimeSyncCatalog.BuildPeerList(hosts, interval > 0));
    }

    private List<string> SelectedHosts()
    {
        if (_selectedCard is null) return [];
        if (_selectedCard.IsCustom) return TimeSyncCatalog.SplitHosts(CustomHostBox.Text);
        return [.. _selectedCard.Hosts];
    }

    private int SelectedIntervalSeconds() => Math.Max(0, _settings.SyncIntervalSeconds);

    private void CustomHostBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        var hosts = TimeSyncCatalog.SplitHosts(CustomHostBox.Text);
        var error = hosts.Count == 0 ? null : TimeSyncCatalog.ValidateHosts(hosts);
        CustomErrorText.Text = error ?? "";
        CustomErrorText.Visibility = error is null ? Visibility.Collapsed : Visibility.Visible;
        if (_selectedCard is { IsCustom: true }) UpdateSelectedHint();
    }

    // ══════════════════ 状态刷新 ══════════════════

    private async Task RefreshAsync(bool includeConfiguration)
    {
        try
        {
            _snapshot = await TimeSyncService.GetSnapshotAsync(includeConfiguration);
            Render();
        }
        catch (Exception ex)
        {
            StatusTitle.Text = LocalizationService.L("TimeSync_ReadFailedTitle", "读取系统时间配置失败");
            StatusDetail.Text = ex.Message;
        }
    }

    private void Render()
    {
        if (_snapshot is null) return;
        _rendering = true;
        try
        {
            RenderHero();
            RenderIssues();
            RenderAdvanced();
            if (_selectedCard is null) RestoreSelection();
            UpdateSelectedHint();
        }
        finally
        {
            _rendering = false;
        }
    }

    private void RestoreSelection()
    {
        // 以系统真实配置为准回显；识别不出预设时（自定义地址）就落到自定义卡片
        var matched = _snapshot?.MatchedPreset;
        if (matched is not null)
        {
            var card = _cards.FirstOrDefault(c => c.Id == matched.Id);
            if (card is not null) { SelectCard(card); return; }
        }

        var custom = _cards.FirstOrDefault(c => c.IsCustom);
        if (custom is null) return;

        var peers = TimeSyncCatalog.SplitHosts(_snapshot?.NtpClient.Peers);
        if (peers.Count > 0)
            CustomHostBox.Text = string.Join(" ", peers);

        SelectCard(custom);
    }

    private void RenderHero()
    {
        var snapshot = _snapshot!;
        var service = snapshot.Service;

        StatusIcon.Glyph = "\uE823";
        StatusIcon.Foreground = AccentBrush;
        SourceRow.Visibility = Visibility.Collapsed;
        LastSyncText.Visibility = Visibility.Collapsed;

        if (!service.Exists)
        {
            StatusIcon.Glyph = "\uEA39";
            StatusIcon.Foreground = CriticalBrush;
            StatusTitle.Text = LocalizationService.L("TimeSync_ServiceMissingTitle", "找不到 Windows 时间服务");
            StatusDetail.Text = LocalizationService.L("TimeSync_ServiceMissingDetail", "系统里没有 w32time 服务，时间同步处于完全不可用状态。可用「高级设置 → 重置时间服务」重新注册。");
        }
        else if (!service.Running)
        {
            StatusIcon.Glyph = "\uE7BA";
            StatusIcon.Foreground = CautionBrush;
            StatusTitle.Text = LocalizationService.L("TimeSync_ServiceStoppedTitle", "Windows 时间服务没有运行");
            StatusDetail.Text = LocalizationService.L("TimeSync_ServiceStoppedDetail", "服务停止时系统不会校时，也读不到时间源。点「一键修复」即可启动它。");
        }
        else if (snapshot.CurrentSource.Length == 0)
        {
            StatusTitle.Text = LocalizationService.L("TimeSync_NeverSyncedTitle", "时间服务运行中，但还没同步过时间源");
            StatusDetail.Text = LocalizationService.L("TimeSync_NeverSyncedDetail", "选一台服务器点「应用并立即同步」，或先「服务器测速」看看哪台连得上。");
            ShowSourceRow(snapshot);
        }
        else if (snapshot.SourceIsLocalClock)
        {
            StatusIcon.Glyph = "\uE7BA";
            StatusIcon.Foreground = CautionBrush;
            StatusTitle.Text = LocalizationService.L("TimeSync_LocalClockTitle", "正在使用本机硬件时钟");
            StatusDetail.Text = LocalizationService.L("TimeSync_LocalClockDetail", "没有任何网络时间源在起作用，本机时钟误差会持续累积。选一台 NTP 服务器应用即可。");
            ShowSourceRow(snapshot);
        }
        else
        {
            StatusIcon.Glyph = "\uE73E";
            StatusIcon.Foreground = SuccessBrush;
            StatusTitle.Text = LocalizationService.L("TimeSync_WorkingTitle", "时间同步已生效");
            StatusDetail.Text = LocalizationService.L("TimeSync_WorkingDetail", "系统正在按下面的时间源校时；如需换源，重新选一台应用即可。");
            ShowSourceRow(snapshot);
        }

        var last = snapshot.LastSyncTime?.ToLocalTime();
        if (last is not null)
        {
            var age = DateTimeOffset.Now - last.Value;
            var ageText = age.TotalMinutes < 2 ? LocalizationService.L("TimeSync_JustNow", "刚刚")
                : age.TotalHours < 1 ? string.Format(LocalizationService.L("TimeSync_MinutesAgo", "{0} 分钟前"), (int)age.TotalMinutes)
                : age.TotalDays < 1 ? string.Format(LocalizationService.L("TimeSync_HoursAgo", "{0} 小时前"), (int)age.TotalHours)
                : string.Format(LocalizationService.L("TimeSync_DaysAgo", "{0} 天前"), (int)age.TotalDays);
            LastSyncText.Text = string.Format(
                LocalizationService.L("TimeSync_LastSyncFormat", "上次成功同步：{0}（{1}）　·　同步模式：{2}"),
                last.Value.ToString("yyyy-MM-dd HH:mm:ss"), ageText, snapshot.NtpClient.SyncTypeText);
        }
        else
        {
            LastSyncText.Text = string.Format(LocalizationService.L("TimeSync_SyncModeNoRecord", "同步模式：{0}　·　没有同步成功的记录"), snapshot.NtpClient.SyncTypeText);
        }

        LastSyncText.Visibility = Visibility.Visible;

        if (!snapshot.IsAdmin)
        {
            ProblemBar.Severity = InfoBarSeverity.Error;
            ProblemBar.Title = LocalizationService.L("TimeSync_NotAdminTitle", "当前未以管理员身份运行");
            ProblemBar.Message = LocalizationService.L("TimeSync_NotAdminMessage", "切换时间源、启停时间服务都需要管理员权限。可在「高级设置 → 以管理员身份重启」重新提权后再操作。");
            ProblemBar.IsOpen = true;
        }

        RenderDrift();
    }

    private void ShowSourceRow(TimeSyncSnapshot snapshot)
    {
        SourceText.Text = snapshot.CurrentSource;
        SourceRow.Visibility = Visibility.Visible;
    }

    private void RenderDrift()
    {
        if (_driftProbe is { Ok: true } probe)
        {
            DriftText.Text = string.Format(
                LocalizationService.L("TimeSync_DriftMeasured", "实测与 {0} 相差 {1}（延迟 {2:F0} ms）"),
                probe.Host, TimeSyncService.FormatOffset(probe.OffsetMs), probe.RoundTripMs);
            DriftText.Foreground = Math.Abs(probe.OffsetMs) > 100 ? CautionBrush : SuccessBrush;
        }
        else
        {
            DriftText.Text = LocalizationService.L("TimeSync_DriftHint", "点右上角「服务器测速」可实测本机时间偏差");
            DriftText.Foreground = NeutralBrush;
        }
    }

    private void RenderIssues()
    {
        if (_snapshot is null) return;

        var issues = TimeSyncService.Evaluate(_snapshot, _driftProbe);
        IssueList.ItemsSource = issues.Select(TimeSyncIssueItem.From).ToList();

        var fixable = issues.Where(i => !i.Title.StartsWith(LocalizationService.L("TimeSync_IssueNotAdminPrefix", "当前未以管理员"), StringComparison.Ordinal)).ToList();
        AllGoodPanel.Visibility = issues.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        IssueSummaryText.Text = issues.Count == 0
            ? LocalizationService.L("TimeSync_AllNormal", "全部正常")
            : fixable.Count > 0
                ? string.Format(LocalizationService.L("TimeSync_IssuesFixable", "{0} 项待处理（可一键修复）"), issues.Count)
                : string.Format(LocalizationService.L("TimeSync_IssuesPending", "{0} 项待处理"), issues.Count);
        RepairButton.Visibility = fixable.Count > 0 && _snapshot.IsAdmin ? Visibility.Visible : Visibility.Collapsed;

        if (issues.Count == 0)
        {
            ProblemBar.IsOpen = false;
        }
        else if (_snapshot.IsAdmin)
        {
            var worst = issues.OrderByDescending(i => i.Level).First();
            ProblemBar.Severity = worst.Level == TimeSyncIssueLevel.Critical ? InfoBarSeverity.Error : InfoBarSeverity.Warning;
            ProblemBar.Title = worst.Title;
            ProblemBar.Message = worst.Detail;
            ProblemBar.IsOpen = true;
        }
    }

    private void RenderAdvanced()
    {
        if (_snapshot is null) return;

        if (IntervalCombo.Items.Count == 0)
        {
            foreach (var option in TimeSyncCatalog.IntervalOptions)
                IntervalCombo.Items.Add(LocalizationService.L($"TimeSync_Interval_{option.Seconds}", option.Label));
        }
        var stored = _settings.SyncIntervalSeconds;
        var index = TimeSyncCatalog.IntervalOptions.ToList().FindIndex(o => o.Seconds == stored);
        IntervalCombo.SelectedIndex = index < 0 ? 0 : index;
        ApplyIntervalButton.IsEnabled = false;

        SslSeedToggle.IsOn = _snapshot.SslTimeSeedEnabled;
        ElevateButton.Visibility = _snapshot.IsAdmin ? Visibility.Collapsed : Visibility.Visible;

        RenderPresetHint(_snapshot);
    }

    private void RenderPresetHint(TimeSyncSnapshot snapshot)
    {
        if (_probes.Count > 0) return;
        PresetHint.Text = snapshot.NtpClient.Peers.Length > 0
            ? string.Format(LocalizationService.L("TimeSync_CurrentConfig", "当前配置：{0}"), snapshot.NtpClient.Peers)
            : LocalizationService.L("TimeSync_PresetHint", "点卡片选中，可先测速再决定");
    }

    // ══════════════════ 测速 ══════════════════

    private async void ProbeButton_Click(object sender, RoutedEventArgs e)
        => await ProbeAllAsync(includeCustom: true);

    private async Task ProbeAllAsync(bool includeCustom)
    {
        if (_probing || _busy) return;
        _probing = true;

        var timeout = TimeSpan.FromSeconds(2.5);
        PresetHint.Text = LocalizationService.L("TimeSync_ProbingAll", "正在测试各服务器的可用性与延迟…");
        SetProbeBusy(true);

        try
        {
            _probes.Clear();
            _driftProbe = null;

            var targets = new List<(TimeSyncServerCard Card, string Host)>();
            foreach (var card in _cards)
            {
                if (card.IsCustom)
                {
                    if (!includeCustom) continue;
                    foreach (var host in TimeSyncCatalog.SplitHosts(CustomHostBox.Text))
                        targets.Add((card, host));
                    continue;
                }

                foreach (var host in card.Hosts) targets.Add((card, host));
            }

            foreach (var card in _cards) card.SetStatus(LocalizationService.L("TimeSync_Testing", "测试中…"), NeutralColor);

            // 先并发拿回全部结果，再一次性落到卡片上：避免边写边读探测结果集合
            var outcomes = await Task.WhenAll(targets.Select(async target =>
            {
                var result = await TimeSyncProbe.QueryAsync(target.Host, timeout);
                return (target.Card, target.Host, Result: result);
            }));

            foreach (var outcome in outcomes) _probes.Add((outcome.Host, outcome.Result));
            foreach (var group in outcomes.GroupBy(o => o.Card)) RenderCardsProbes(group.Key, group.ToList());

            var ok = _probes.Where(p => p.Result.Ok).OrderBy(p => p.Result.RoundTripMs).ToList();
            _driftProbe = ok.Count > 0 ? ok[0].Result : null;

            PresetHint.Text = ok.Count == 0
                ? LocalizationService.L("TimeSync_AllServersUnreachable", "所有服务器都没响应：可能是当前网络屏蔽了 UDP 123（换网络或检查防火墙）")
                : string.Format(LocalizationService.L("TimeSync_ProbeDoneBest", "测速完成：{0} 台可用，最快 {1}（{2:F0} ms）"), ok.Count, ok[0].Host, ok[0].Result.RoundTripMs);

            RenderDrift();
            RenderIssues();
        }
        finally
        {
            _probing = false;
            SetProbeBusy(false);
        }
    }

    private void RenderCardsProbes(TimeSyncServerCard card, IReadOnlyList<(TimeSyncServerCard Card, string Host, NtpProbeResult Result)> outcomes)
    {
        var available = outcomes.Count(o => o.Result.Ok);
        if (available == 0)
        {
            var error = outcomes.FirstOrDefault().Result?.Error ?? LocalizationService.L("TimeSync_NoResponse", "没有响应");
            card.SetStatus(string.Format(LocalizationService.L("TimeSync_UnavailableWithError", "不可用（{0}）"), error), CriticalColor);
            return;
        }

        var best = outcomes.Where(o => o.Result.Ok).OrderBy(o => o.Result.RoundTripMs).First().Result;
        var text = outcomes.Count > 1
            ? string.Format(LocalizationService.L("TimeSync_LatencyAvailable", "延迟 {0:F0} ms · {1}/{2} 台可用"), best.RoundTripMs, available, outcomes.Count)
            : string.Format(LocalizationService.L("TimeSync_LatencyOffset", "延迟 {0:F0} ms · {1}"), best.RoundTripMs, TimeSyncService.FormatOffset(best.OffsetMs));
        card.SetStatus(text, best.RoundTripMs < 400 ? SuccessColor : CautionColor);
    }

    private async void CustomProbeButton_Click(object sender, RoutedEventArgs e)
    {
        if (_probing || _busy) return;
        if (_selectedCard is not { IsCustom: true }) SelectCard(_cards.First(c => c.IsCustom));
        await ProbeAllAsync(includeCustom: true);
    }

    private void SetProbeBusy(bool busy)
    {
        ProbeButton.IsEnabled = !busy && !_busy;
        CustomProbeButton.IsEnabled = !busy && !_busy;
        ProbeText.Text = busy
            ? LocalizationService.L("TimeSync_Probing", "测速中…")
            : LocalizationService.L("TimeSync_ProbeButton", "服务器测速");
        ProbeIcon.Glyph = busy ? "\uE895" : "\uE9D9";
        if (!busy) foreach (var card in _cards.Where(c => c.StatusText == LocalizationService.L("TimeSync_Testing", "测试中…"))) card.ClearStatus();
    }

    // ══════════════════ 操作 ══════════════════

    private async void ApplyButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;

        var card = _selectedCard
            ?? _cards.FirstOrDefault(c => !c.IsCustom && c.Id == _settings.PresetId);
        if (card is null)
        {
            await ShowMessageAsync(
                LocalizationService.L("TimeSync_PickServerTitle", "先选一台时间服务器"),
                LocalizationService.L("TimeSync_PickServerMessage", "点一张卡片选中它（想用自己的地址就选「自定义」并填写），然后再点「应用并立即同步」。"));
            return;
        }

        var hosts = card.IsCustom ? TimeSyncCatalog.SplitHosts(CustomHostBox.Text) : [.. card.Hosts];
        var error = TimeSyncCatalog.ValidateHosts(hosts);
        if (error is not null)
        {
            await ShowMessageAsync(LocalizationService.L("TimeSync_InvalidAddressTitle", "地址不合法"), error);
            return;
        }

        SetBusy(true, LocalizationService.L("TimeSync_BusyApplying", "正在写入时间源、重启时间服务并校时…"));
        try
        {
            var result = await TimeSyncService.ApplyServersAsync(hosts, SelectedIntervalSeconds());

            if (result.Ok)
            {
                _settings.PresetId = card.IsCustom ? "" : card.Id;
                if (card.IsCustom) _settings.CustomHosts = CustomHostBox.Text.Trim();
                TimeSyncCatalog.SaveSettings(_settings);
            }

            await RefreshAsync(includeConfiguration: false);
            ShowResult(result);
        }
        finally
        {
            SetBusy(false, null);
        }
    }

    private async void ResyncButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        SetBusy(true, LocalizationService.L("TimeSync_BusyResync", "正在向时间源校时…"));
        try
        {
            var result = await TimeSyncService.ResyncAsync();
            await RefreshAsync(includeConfiguration: false);
            ShowResult(result);
        }
        finally
        {
            SetBusy(false, null);
        }
    }

    private async void RestoreButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _snapshot is null) return;

        var target = _snapshot.DomainJoined
            ? LocalizationService.L("TimeSync_TargetDomain", "域时间层次（NT5DS）")
            : LocalizationService.L("TimeSync_TargetDefault", "time.windows.com（系统出厂配置）");
        var confirmed = await ConfirmAsync(
            LocalizationService.L("TimeSync_RestoreConfirmTitle", "恢复系统默认时间源"),
            string.Format(LocalizationService.L("TimeSync_RestoreConfirmMessage", "会把时间源改回 {0}，然后重启时间服务并立即校时。\n同步频率不会被改动（想改可在「高级设置 → 同步频率」里调整）。"), target),
            LocalizationService.L("TimeSync_RestoreConfirmPrimary", "恢复默认"));
        if (!confirmed) return;

        SetBusy(true, LocalizationService.L("TimeSync_BusyRestoring", "正在恢复系统默认时间源…"));
        try
        {
            var result = await TimeSyncService.RestoreDefaultAsync(_snapshot.DomainJoined);
            if (result.Ok)
            {
                _settings.PresetId = _snapshot.DomainJoined ? "" : "microsoft";
                TimeSyncCatalog.SaveSettings(_settings);
                _selectedCard = null;
                foreach (var card in _cards) card.IsSelected = false;
            }

            await RefreshAsync(includeConfiguration: false);
            ShowResult(result);
        }
        finally
        {
            SetBusy(false, null);
        }
    }

    private async void RepairButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || _snapshot is null) return;

        var hosts = SelectedHosts();
        var plan = new List<string>
        {
            LocalizationService.L("TimeSync_RepairStepEnableService", "把 Windows 时间服务设为「自动（延迟启动）」并启动"),
            LocalizationService.L("TimeSync_RepairStepEnsureClient", "确保 NTP 客户端处于启用状态"),
        };
        if (hosts.Count > 0) plan.Add(string.Format(LocalizationService.L("TimeSync_RepairStepApplyHosts", "应用当前选择的时间源（{0}）并立即校时"), string.Join(" ", hosts)));
        else plan.Add(LocalizationService.L("TimeSync_RepairStepRestartOnly", "重启时间服务并立即校时（保留现有时间源配置）"));

        var confirmed = await ConfirmAsync(
            LocalizationService.L("TimeSync_RepairConfirmTitle", "一键修复时间同步"),
            string.Join("\n", plan.Select((p, i) => $"{i + 1}. {p}")),
            LocalizationService.L("TimeSync_RepairConfirmPrimary", "开始修复"));
        if (!confirmed) return;

        SetBusy(true, LocalizationService.L("TimeSync_BusyRepairing", "正在修复时间服务…"));
        try
        {
            var result = await TimeSyncService.RepairAsync(hosts.Count > 0 ? hosts : null, SelectedIntervalSeconds());
            await RefreshAsync(includeConfiguration: false);
            ShowResult(result);
        }
        finally
        {
            SetBusy(false, null);
        }
    }

    private async void ApplyIntervalButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy || IntervalCombo.SelectedIndex < 0) return;

        var seconds = TimeSyncCatalog.IntervalOptions[IntervalCombo.SelectedIndex].Seconds;
        SetBusy(true, LocalizationService.L("TimeSync_BusyApplyingInterval", "正在应用同步频率…"));
        try
        {
            var result = await TimeSyncService.SetSyncIntervalAsync(seconds);
            if (result.Ok)
            {
                _settings.SyncIntervalSeconds = seconds;
                TimeSyncCatalog.SaveSettings(_settings);
            }

            await RefreshAsync(includeConfiguration: false);
            ShowResult(result);
        }
        finally
        {
            SetBusy(false, null);
        }
    }

    private void IntervalCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_rendering) return;
        ApplyIntervalButton.IsEnabled = IntervalCombo.SelectedIndex >= 0
            && TimeSyncCatalog.IntervalOptions[IntervalCombo.SelectedIndex].Seconds != _settings.SyncIntervalSeconds;
    }

    private async void SslSeedToggle_Toggled(object sender, RoutedEventArgs e)
    {
        if (_rendering || _busy || _snapshot is null) return;
        if (SslSeedToggle.IsOn == _snapshot.SslTimeSeedEnabled) return;

        SetBusy(true, SslSeedToggle.IsOn
            ? LocalizationService.L("TimeSync_BusySslEnable", "正在启用 SSL 时间种子…")
            : LocalizationService.L("TimeSync_BusySslDisable", "正在关闭 SSL 时间种子…"));
        try
        {
            var result = await TimeSyncService.SetSslTimeSeedAsync(SslSeedToggle.IsOn);
            if (result.Ok)
            {
                _settings.SslTimeSeedDisabled = !SslSeedToggle.IsOn;
                TimeSyncCatalog.SaveSettings(_settings);
            }

            await RefreshAsync(includeConfiguration: false);
            ShowResult(result);
        }
        finally
        {
            SetBusy(false, null);
        }
    }

    private async void ResetServiceButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;

        var confirmed = await ConfirmAsync(
            LocalizationService.L("TimeSync_ResetConfirmTitle", "重置 Windows 时间服务"),
            LocalizationService.L("TimeSync_ResetConfirmMessage", "会注销并重新注册 Windows 时间服务（w32tm /unregister + /register），服务的全部自定义配置会被清空，时间源回到系统默认。\n适用于时间服务配置损坏、怎么改都不生效的情况。重置后需要的话再点一次「应用并立即同步」写入你想要的服务器。"),
            LocalizationService.L("TimeSync_ResetConfirmPrimary", "重置服务"));
        if (!confirmed) return;

        SetBusy(true, LocalizationService.L("TimeSync_BusyReregister", "正在重新注册时间服务…"));
        try
        {
            var result = await TimeSyncService.ResetTimeServiceAsync();
            _selectedCard = null;
            foreach (var card in _cards) card.IsSelected = false;
            await RefreshAsync(includeConfiguration: false);
            ShowResult(result);
        }
        finally
        {
            SetBusy(false, null);
        }
    }

    private async void CopyReportButton_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        SetBusy(true, LocalizationService.L("TimeSync_BusyCollecting", "正在收集诊断信息…"));
        try
        {
            var snapshot = await TimeSyncService.GetSnapshotAsync(includeConfiguration: true);
            _snapshot = snapshot;
            var probes = _probes.OrderBy(p => p.Result.Ok ? 0 : 1).ThenBy(p => p.Result.RoundTripMs).Select(p => p.Result).ToList();
            var report = TimeSyncService.BuildDiagnosticReport(snapshot, probes);

            var result = ClipboardService.TrySetText(report);
            if (!result.Success)
            {
                ShowResult(TimeSyncActionResult.Failure(
                    LocalizationService.L("TimeSync_CopyReportFailed", "复制诊断报告失败"),
                    LocalizationService.L("Hw_CopyBusyRetry", "复制失败：剪贴板被其他程序占用，请稍后重试")));
                Render();
                return;
            }

            ShowResult(TimeSyncActionResult.Success(
                LocalizationService.L("TimeSync_ReportCopiedTitle", "诊断报告已复制到剪贴板"),
                LocalizationService.L("TimeSync_ReportCopiedDetail", "内容包含时间服务状态、当前时间源、NTP 配置与服务器实测结果，可直接粘贴给他人排查。")));
            Render();
        }
        catch (Exception ex)
        {
            ShowResult(TimeSyncActionResult.Failure(LocalizationService.L("TimeSync_CopyReportFailed", "复制诊断报告失败"), ex.Message));
        }
        finally
        {
            SetBusy(false, null);
        }
    }

    private void OpenTimeSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "ms-settings:dateandtime",
                UseShellExecute = true,
            });
        }
        catch (Exception ex)
        {
            _ = ShowMessageAsync(LocalizationService.L("TimeSync_OpenTimeSettingsFailed", "打不开系统时间设置"), ex.Message);
        }
    }

    private async void ElevateButton_Click(object sender, RoutedEventArgs e)
    {
        if (TimeSyncService.TryRestartElevated())
        {
            await ShowMessageAsync(
                LocalizationService.L("TimeSync_ElevateRequestedTitle", "已请求管理员权限"),
                LocalizationService.L("TimeSync_ElevateRequestedMessage", "工具箱会以管理员身份重新启动，稍后回到「时间同步」页面即可继续切换时间源。"));
            return;
        }

        await ShowMessageAsync(
            LocalizationService.L("TimeSync_ElevateFailedTitle", "无法自动提权"),
            LocalizationService.L("TimeSync_ElevateFailedMessage", "请关闭工具箱，右键「图吧工具箱」图标选择「以管理员身份运行」；MSIX（微软商店）版本受系统限制无法自行提权。"));
    }

    /// <summary>语言切换后重绘卡片与状态区（Uid 控件由 WinUI3Localizer 自动更新）。</summary>
    public void ApplyLocalization()
    {
        _cards.Clear();
        BuildCards();
        if (_snapshot is not null) Render();
    }

    private async void CopySourceButton_Click(object sender, RoutedEventArgs e)
    {
        if (SourceText.Text.Length == 0) return;
        var result = ClipboardService.TrySetText(SourceText.Text);
        if (!result.Success)
        {
            await ShowMessageAsync(
                LocalizationService.L("Hw_CopyFailed", "复制失败"),
                LocalizationService.L("Hw_CopyBusyRetry", "复制失败：剪贴板被其他程序占用，请稍后重试"));
            return;
        }
        await ShowMessageAsync(
            LocalizationService.L("TimeSync_CopiedTitle", "已复制"),
            string.Format(LocalizationService.L("TimeSync_CopiedSource", "当前时间源：{0}"), SourceText.Text));
    }

    private async void HelpButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = CreateDialog(
            LocalizationService.L("TimeSync_HelpTitle", "时间同步怎么用"),
            LocalizationService.L("Common_GotIt", "知道了"));
        dialog.Content = new ScrollViewer
        {
            MaxHeight = 420,
            Content = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                LineHeight = 21,
                FontSize = 13,
                Text = LocalizationService.L("TimeSync_HelpBody", """
                为什么需要它
                · 网页证书、账号登录、验证码、购票抢票都依赖准确的系统时间；系统时间偏得多了，网络明明是通的也会出错。
                · Windows 默认时间源是 time.windows.com，国内线路偶尔超时或响应很慢——这就是「有些网站怪怪的」的常见原因。

                三步搞定
                1. 先看上方状态卡：时间服务是否在跑、当前时间源是什么、上次同步是什么时候。
                2. 点右上角「服务器测速」，看哪几台服务器连得上、延迟多少；卡片上会直接给出结果。
                3. 选一台（绿色/黄色徽标都可用，绿色延迟更低）→「应用并立即同步」。

                换错了想还原
                · 点「恢复系统默认」：域电脑回到域时间层次，普通电脑回到 time.windows.com。

                能用到多深
                · 高级设置里可以固定同步频率（默认交给系统自适应）、开关 SSL 时间种子、重置时间服务；
                · 「复制诊断报告」会把服务状态、时间源、NTP 配置和实测结果整理成文本，方便求助时贴给别人。

                权限说明
                · 修改时间源和启停时间服务需要管理员权限；未打包版启动时自动申请，也可以用「以管理员身份重启」。
                · 全程调用系统自带的 w32tm 命令（Microsoft Learn 官方文档《Windows Time service tools and settings》），不装驱动、不常驻后台。
                """)
            }
        };
        await dialog.ShowAsync();
    }

    // ══════════════════ 忙碌 / 结果 / 对话框 ══════════════════

    private void SetBusy(bool busy, string? message)
    {
        _busy = busy;

        BusyRing.IsActive = busy;
        BusyRing.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;

        ApplyButton.IsEnabled = !busy;
        ResyncButton.IsEnabled = !busy;
        RestoreButton.IsEnabled = !busy;
        RepairButton.IsEnabled = !busy;
        ResetServiceButton.IsEnabled = !busy;
        ApplyIntervalButton.IsEnabled = !busy && IntervalCombo.SelectedIndex >= 0;
        ProbeButton.IsEnabled = !busy;
        CustomProbeButton.IsEnabled = !busy;
        ElevateButton.IsEnabled = !busy;
        SslSeedToggle.IsEnabled = !busy;

        if (busy && message is not null) StatusDetail.Text = message;
    }

    private void ShowResult(TimeSyncActionResult result)
    {
        ResultBar.Severity = result.Ok ? InfoBarSeverity.Success : InfoBarSeverity.Error;
        ResultBar.Title = result.Message;
        ResultBar.Message = Summarize(result.Detail);
        ResultBar.ActionButton = result.Detail.Length > 0 ? DetailsButton(result) : null;
        ResultBar.IsOpen = true;
    }

    private Button DetailsButton(TimeSyncActionResult result)
    {
        var button = new Button { Content = LocalizationService.L("TimeSync_ViewDetails", "查看详情") };
        button.Click += async (_, _) => await ShowMessageAsync(
            result.Ok
                ? LocalizationService.L("TimeSync_ActionDetails", "操作详情")
                : string.Format(LocalizationService.L("TimeSync_FailureDetails", "失败详情：{0}"), result.Message),
            result.Detail);
        return button;
    }

    private static string Summarize(string detail)
    {
        if (detail.Length == 0) return "";
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
        var dialog = CreateDialog(title, LocalizationService.L("Common_GotIt", "知道了"));
        dialog.Content = new ScrollViewer
        {
            MaxHeight = 360,
            Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, LineHeight = 20, FontSize = 13 }
        };
        await dialog.ShowAsync();
    }

    private async Task<bool> ConfirmAsync(string title, string message, string primaryText)
    {
        var dialog = CreateDialog(title, LocalizationService.L("Common_Cancel", "取消"));
        dialog.PrimaryButtonText = primaryText;
        dialog.DefaultButton = ContentDialogButton.Primary;
        dialog.Content = new TextBlock { Text = message, TextWrapping = TextWrapping.Wrap, LineHeight = 20, FontSize = 13 };
        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}

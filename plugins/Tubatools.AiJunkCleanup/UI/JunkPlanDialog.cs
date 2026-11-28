using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Tubatools.AiJunkCleanup.Models;
using Tubatools.AiJunkCleanup.Runtime;
using Tubatools.AiJunkCleanup.Services;
using Windows.ApplicationModel.DataTransfer;

namespace Tubatools.AiJunkCleanup.UI;

/// <summary>
/// 清理清单确认对话框。所有项目默认折叠；低风险默认勾选，中/高风险默认不勾选；
/// 被安全规则阻止的项目不可勾选。删除由插件直接完成，不进入回收站；大目录使用原生递归删除，
/// 进度按已处理项数/清单大小估算（大目录删除期间为不确定进度）。
/// </summary>
internal sealed class JunkPlanDialog : ContentDialog
{
    private sealed class ItemState
    {
        public required JunkPlanItem Item { get; init; }
        public required JunkSafetyResult Safety { get; init; }
        public required string Path { get; init; }
        public required string Category { get; init; }
        public required JunkRisk Risk { get; init; }
        public required bool Blocked { get; init; }
        public required bool IsDirectory { get; init; }
        public required CheckBox Check { get; init; }
        public required TextBlock SizeText { get; init; }
        public required TextBlock Status { get; init; }
        public required Expander Expander { get; init; }
        public long Bytes { get; set; }
        public bool Partial { get; set; }
        public bool Deleted { get; set; }

        public void MarkSkipped(string message)
        {
            Check.IsChecked = false;
            Check.IsEnabled = !Blocked;
            Status.Text = "已跳过：" + message;
            Status.Foreground = Ui.Secondary;
            Status.Visibility = Visibility.Visible;
        }

        public void MarkFailed(string message)
        {
            Check.IsChecked = false;
            Check.IsEnabled = !Blocked;
            Status.Text = "删除失败：" + message;
            Status.Foreground = Ui.Critical;
            Status.Visibility = Visibility.Visible;
        }
    }

    private readonly JunkPlan _plan;
    private readonly JunkScanDepth _depth;
    private readonly string? _dataDirectory;
    private readonly Action<JunkCleanupResult>? _onCompleted;
    private readonly JunkSafetyEnvironment _environment;
    private readonly List<ItemState> _states = [];
    private readonly Dictionary<string, TextBlock> _categoryHeaders = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<ItemState>> _categoryItems = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _lifetime = new();
    private CancellationTokenSource? _measure;
    private ComboBox _filter = null!;
    private InfoBar _warning = null!;
    private InfoBar _error = null!;
    private InfoBar _result = null!;
    private CheckBox _riskConfirm = null!;
    private TextBlock _summaryCount = null!;
    private TextBlock _summarySize = null!;
    private TextBlock _lowChip = null!;
    private TextBlock _mediumChip = null!;
    private TextBlock _highChip = null!;
    private TextBlock _blockedChip = null!;
    private TextBlock _progressText = null!;
    private ProgressBar _progress = null!;
    private Border _lockPrompt = null!;
    private TextBlock _lockText = null!;
    private Button _lockCloseButton = null!;
    private Button _lockCloseAllButton = null!;
    private Button _lockSkipAllButton = null!;
    private readonly SemaphoreSlim _lockGate = new(1, 1);
    private TaskCompletionSource<LockResolution>? _lockTcs;
    private IReadOnlyList<LockedProcess> _lockProcesses = [];
    private int _lockAllPolicy; // 0 = 逐次询问，1 = 后续全部关闭，2 = 后续全部跳过
    private bool _busy;
    private bool _completed;
    private bool _closed;

    internal JunkPlanDialog(JunkPlan plan, JunkScanDepth depth, XamlRoot root,
        string? dataDirectory, Action<JunkCleanupResult>? onCompleted)
    {
        _plan = plan;
        _depth = depth;
        _dataDirectory = dataDirectory;
        _onCompleted = onCompleted;
        _environment = JunkSafetyEnvironment.CreateDefault(HostBridge.TryGetDataDirectory());

        Title = "垃圾清理清单 · " + JunkScanDepths.Label(depth);
        PrimaryButtonText = "删除所选";
        CloseButtonText = "取消";
        DefaultButton = ContentDialogButton.Close;
        XamlRoot = root;
        MaxWidth = 980;
        MinWidth = 520;
        Resources["ContentDialogMaxWidth"] = 980d;
        Resources["ContentDialogMinWidth"] = 520d;
        Content = BuildContent();
        if (root.Size.Height > 320 && Content is FrameworkElement content)
            content.MaxHeight = Math.Max(320, root.Size.Height - 220);
        PrimaryButtonClick += OnPrimaryClick;
        SecondaryButtonClick += OnSecondaryClick;
        Closing += OnClosing;
        Opened += (_, _) => _ = MeasureSizesAsync();
        Closed += (_, _) => { _closed = true; _lifetime.Cancel(); _measure?.Cancel(); };
        UpdateSummary();
        UpdateSelection();
    }

    private FrameworkElement BuildContent()
    {
        var root = new Grid
        {
            MinWidth = 460,
            MaxWidth = 900,
            RowSpacing = 10
        };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var top = new StackPanel { Spacing = 10 };
        top.Children.Add(BuildSummary());
        top.Children.Add(BuildToolbar());

        _warning = new InfoBar
        {
            IsOpen = false, IsClosable = false, Severity = InfoBarSeverity.Warning,
            Title = "已选中需要确认的项目"
        };
        top.Children.Add(_warning);
        _error = new InfoBar { IsOpen = false, IsClosable = true, Severity = InfoBarSeverity.Error, Title = "无法继续" };
        top.Children.Add(_error);

        _riskConfirm = new CheckBox
        {
            Content = "我已阅读高风险项目的风险说明，并确认删除。",
            Visibility = Visibility.Collapsed,
            Margin = new Thickness(0, -4, 0, 0)
        };
        _riskConfirm.Checked += (_, _) => UpdateSelection();
        _riskConfirm.Unchecked += (_, _) => UpdateSelection();
        top.Children.Add(_riskConfirm);

        // 删除警告、进度、占用提示固定在顶部；列表占满剩余高度并自己滚动，避免底部项目被裁掉。
        top.Children.Add(BuildFooter());
        Grid.SetRow(top, 0);
        root.Children.Add(top);

        var list = new StackPanel { Spacing = 4 };
        BuildItems(list);
        var scroll = new ScrollViewer
        {
            Content = list,
            MinHeight = 120,
            VerticalAlignment = VerticalAlignment.Stretch,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Margin = new Thickness(0, 2, 0, 2)
        };
        Grid.SetRow(scroll, 1);
        root.Children.Add(scroll);

        return root;
    }

    private FrameworkElement BuildSummary()
    {
        var grid = new Grid { ColumnSpacing = 24, RowSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var countBlock = StatBlock("已选 / 总共（项）", out _summaryCount, 30);
        grid.Children.Add(countBlock);

        var sizeBlock = StatBlock("已选 / 总共（大小）", out _summarySize, 26);
        Grid.SetColumn(sizeBlock, 1);
        grid.Children.Add(sizeBlock);

        var chips = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        chips.Children.Add(Chip("低风险 0", Ui.SuccessBackground, Ui.Success, out _lowChip));
        chips.Children.Add(Chip("中风险 0", Ui.CautionBackground, Ui.Caution, out _mediumChip));
        chips.Children.Add(Chip("高风险 0", Ui.CriticalBackground, Ui.Critical, out _highChip));
        chips.Children.Add(Chip("不建议 0", Ui.NeutralBackground, Ui.Secondary, out _blockedChip));
        Grid.SetRow(chips, 1);
        Grid.SetColumnSpan(chips, 2);
        grid.Children.Add(chips);
        return grid;
    }

    private static StackPanel StatBlock(string label, out TextBlock value, double fontSize)
    {
        var panel = new StackPanel { Spacing = 1 };
        panel.Children.Add(new TextBlock { Text = label, FontSize = 11, Foreground = Ui.Secondary });
        value = new TextBlock
        {
            FontSize = fontSize,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1
        };
        panel.Children.Add(value);
        return panel;
    }

    private FrameworkElement BuildToolbar()
    {
        var grid = new Grid { ColumnSpacing = 8 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        _filter = new ComboBox { MinWidth = 132, SelectedIndex = 0, VerticalAlignment = VerticalAlignment.Center };
        _filter.Items.Add(new ComboBoxItem { Content = "全部项目", Tag = "all" });
        _filter.Items.Add(new ComboBoxItem { Content = "只看低风险", Tag = "low" });
        _filter.Items.Add(new ComboBoxItem { Content = "只看中风险", Tag = "medium" });
        _filter.Items.Add(new ComboBoxItem { Content = "只看高风险", Tag = "high" });
        _filter.Items.Add(new ComboBoxItem { Content = "不建议删除", Tag = "blocked" });
        _filter.SelectionChanged += (_, _) => ApplyFilter();
        AutomationProperties.SetName(_filter, "筛选清理项目");
        grid.Children.Add(_filter);

        var selectLow = new Button { Content = "全选低风险", Padding = new Thickness(10, 4, 10, 4) };
        selectLow.Click += (_, _) => SelectLowRisk();
        Grid.SetColumn(selectLow, 1);
        grid.Children.Add(selectLow);

        var clear = new Button { Content = "全部取消", Padding = new Thickness(10, 4, 10, 4) };
        clear.Click += (_, _) => ClearAll();
        Grid.SetColumn(clear, 2);
        grid.Children.Add(clear);

        var expandAll = new Button { Content = "展开全部", Padding = new Thickness(10, 4, 10, 4) };
        expandAll.Click += (_, _) => ToggleAll(true);
        Grid.SetColumn(expandAll, 3);
        grid.Children.Add(expandAll);
        return grid;
    }

    private FrameworkElement BuildFooter()
    {
        var panel = new StackPanel { Spacing = 6, Margin = new Thickness(0, 2, 0, 0) };
        panel.Children.Add(new InfoBar
        {
            IsOpen = true,
            IsClosable = true,
            Severity = InfoBarSeverity.Warning,
            Title = "直接删除（不进入回收站）",
            Message = "插件会逐文件直接删除选中的项目，不调用回收站；删除后无法恢复，请确认这些内容已不再需要。"
        });

        _lockPrompt = BuildLockPrompt();
        panel.Children.Add(_lockPrompt);

        _progress = new ProgressBar { Visibility = Visibility.Collapsed, IsIndeterminate = false, Maximum = 100, Value = 0, Margin = new Thickness(0, 2, 0, 0) };
        panel.Children.Add(_progress);
        _progressText = new TextBlock { Visibility = Visibility.Collapsed, FontSize = 12, Foreground = Ui.Secondary, TextWrapping = TextWrapping.Wrap };
        panel.Children.Add(_progressText);
        _result = new InfoBar { IsOpen = false, IsClosable = true, Severity = InfoBarSeverity.Success };
        panel.Children.Add(_result);
        return panel;
    }

    private Border BuildLockPrompt()
    {
        var panel = new StackPanel { Spacing = 8 };
        panel.Children.Add(new TextBlock { Text = "文件被占用", FontWeight = FontWeights.SemiBold, FontSize = 13 });
        _lockText = new TextBlock
        {
            FontSize = 12,
            Foreground = Ui.Secondary,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true
        };
        panel.Children.Add(_lockText);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        _lockCloseButton = new Button { Content = "关闭软件并重试", Padding = new Thickness(10, 4, 10, 4) };
        _lockCloseButton.Click += async (_, _) => await CloseLockedProcessesAsync(false);
        _lockCloseAllButton = new Button { Content = "全部关闭并重试", Padding = new Thickness(10, 4, 10, 4) };
        _lockCloseAllButton.Click += async (_, _) => await CloseLockedProcessesAsync(true);
        var skip = new Button { Content = "跳过此文件", Padding = new Thickness(10, 4, 10, 4) };
        skip.Click += (_, _) => CompleteLock(LockResolution.Skip);
        _lockSkipAllButton = new Button { Content = "全部跳过", Padding = new Thickness(10, 4, 10, 4) };
        _lockSkipAllButton.Click += (_, _) => CompleteAllLock(2, LockResolution.Skip);
        var stop = new Button { Content = "停止清理", Padding = new Thickness(10, 4, 10, 4) };
        stop.Click += (_, _) => { _lifetime.Cancel(); CompleteLock(LockResolution.Cancel); };
        buttons.Children.Add(_lockCloseButton);
        buttons.Children.Add(_lockCloseAllButton);
        buttons.Children.Add(skip);
        buttons.Children.Add(_lockSkipAllButton);
        buttons.Children.Add(stop);
        panel.Children.Add(buttons);

        return new Border
        {
            Background = Ui.Card,
            BorderBrush = Ui.Critical,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12),
            Visibility = Visibility.Collapsed,
            Child = panel
        };
    }

    private void CompleteLock(LockResolution result)
    {
        var tcs = _lockTcs;
        _lockTcs = null;
        _lockProcesses = [];
        _lockPrompt.Visibility = Visibility.Collapsed;
        tcs?.TrySetResult(result);
    }

    private async Task CloseLockedProcessesAsync(bool all)
    {
        var tcs = _lockTcs;
        if (tcs is null) return;

        var processes = _lockProcesses;
        if (processes.Count == 0)
        {
            if (all) CompleteAllLock(1, LockResolution.Retry);
            else CompleteLock(LockResolution.Retry);
            return;
        }

        var original = _lockText.Text;
        _lockCloseButton.IsEnabled = false;
        _lockCloseAllButton.IsEnabled = false;
        _lockText.Text = original + "\n\n正在关闭进程…";

        var error = await Task.Run(() => ProcessCloser.Terminate(processes));
        if (!ReferenceEquals(_lockTcs, tcs)) return;
        if (error is not null)
        {
            _lockText.Text = original + "\n\n无法关闭进程：" + error + "\n可以再次尝试、跳过或停止。";
            _lockCloseButton.IsEnabled = processes.Any(process => process.CanClose);
            _lockCloseAllButton.IsEnabled = _lockCloseButton.IsEnabled;
            return;
        }

        if (all) CompleteAllLock(1, LockResolution.Retry);
        else CompleteLock(LockResolution.Retry);
    }

    private void CompleteAllLock(int policy, LockResolution result)
    {
        Interlocked.Exchange(ref _lockAllPolicy, policy);
        CompleteLock(result);
    }

    private async Task<LockResolution> ResolveLockAsync(string path, IReadOnlyList<LockedProcess> processes,
        CancellationToken ct)
    {
        var policy = Volatile.Read(ref _lockAllPolicy);
        if (policy != 0) return policy == 1 ? LockResolution.Retry : LockResolution.Skip;

        await _lockGate.WaitAsync(ct);
        try
        {
            // 等锁期间可能已经选了“全部关闭/全部跳过”。
            policy = Volatile.Read(ref _lockAllPolicy);
            if (policy != 0) return policy == 1 ? LockResolution.Retry : LockResolution.Skip;

            var tcs = new TaskCompletionSource<LockResolution>(TaskCreationOptions.RunContinuationsAsynchronously);
            using var registration = ct.Register(() => tcs.TrySetResult(LockResolution.Cancel));
            if (!DispatcherQueue.TryEnqueue(() => ShowLockPrompt(path, processes, tcs)))
                return LockResolution.Skip;
            var result = await tcs.Task;
            DispatcherQueue.TryEnqueue(() =>
            {
                if (!ReferenceEquals(_lockTcs, tcs)) return;
                _lockTcs = null;
                _lockPrompt.Visibility = Visibility.Collapsed;
            });
            return result;
        }
        finally { _lockGate.Release(); }
    }

    private void ShowLockPrompt(string path, IReadOnlyList<LockedProcess> processes,
        TaskCompletionSource<LockResolution> tcs)
    {
        if (_closed)
        {
            tcs.TrySetResult(LockResolution.Cancel);
            return;
        }
        _lockTcs = tcs;
        _lockProcesses = processes;
        var lines = string.Join("\n", processes.Select(process =>
            "- " + process.Name + " (PID " + process.Pid + ")" +
            (string.IsNullOrWhiteSpace(process.Warning) ? "" : " · " + process.Warning)));
        _lockText.Text = path + "\n\n检测到以下程序正在使用该文件：\n" + lines +
                         "\n\n关闭后会自动重试删除；也可以跳过该文件，或选择全部关闭/全部跳过（本次清理后续不再提示）。";
        _lockCloseButton.IsEnabled = processes.Any(process => process.CanClose);
        _lockCloseAllButton.IsEnabled = _lockCloseButton.IsEnabled;
        _lockCloseButton.Content = processes.Any(process => process.CanClose && process.HasWindow)
            ? "关闭软件并重试"
            : "结束进程并重试";
        _lockPrompt.Visibility = Visibility.Visible;
    }

    private void BuildItems(StackPanel panel)
    {
        var groups = _plan.Items
            .GroupBy(item => string.IsNullOrWhiteSpace(item.Category) ? "其他" : item.Category.Trim())
            .OrderBy(group => group.Key, StringComparer.CurrentCulture);
        foreach (var group in groups)
        {
            var states = new List<ItemState>();
            foreach (var item in group.OrderByDescending(i => i.Risk).ThenByDescending(i => i.SizeBytes))
            {
                var safety = JunkSafety.Evaluate(item, _environment);
                if (!safety.Exists) continue; // 已经不存在的路径不展示。
                var state = BuildItem(item, group.Key, safety);
                states.Add(state);
                _states.Add(state);
            }
            if (states.Count == 0) continue;

            var header = new TextBlock
            {
                Text = group.Key + " · " + states.Count + " 项",
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 8, 0, 2),
                Foreground = Ui.Secondary
            };
            _categoryHeaders[group.Key] = header;
            _categoryItems[group.Key] = states;
            panel.Children.Add(header);
            foreach (var state in states) panel.Children.Add(state.Expander);
        }
    }

    private ItemState BuildItem(JunkPlanItem item, string category, JunkSafetyResult safety)
    {
        var path = safety.Path.Length > 0 ? safety.Path : item.Path;
        var isDirectory = safety.IsDirectory;
        var risk = safety.Risk;
        var blocked = safety.Blocked;

        var check = new CheckBox
        {
            IsChecked = !blocked && risk == JunkRisk.Low,
            IsEnabled = !blocked,
            MinWidth = 0,
            Padding = new Thickness(0),
            VerticalAlignment = VerticalAlignment.Center
        };
        AutomationProperties.SetName(check, (blocked ? "不建议删除：" : "删除：") + path);
        if (blocked)
            ToolTipService.SetToolTip(check, "安全规则已锁定，不能勾选：" + safety.Note);

        var badgeLabel = new TextBlock
        {
            Text = blocked ? "不建议" : RiskLabel(risk),
            FontSize = 11,
            Foreground = blocked ? Ui.Secondary : RiskForeground(risk)
        };
        var badge = new Border
        {
            Background = blocked ? Ui.NeutralBackground : RiskBackground(risk),
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(7, 2, 7, 2),
            VerticalAlignment = VerticalAlignment.Center,
            Child = badgeLabel
        };

        var name = new TextBlock
        {
            Text = DisplayName(path, isDirectory),
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1
        };
        var title = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        title.Children.Add(new FontIcon { Glyph = isDirectory ? "\uE8B7" : "\uE7C3", FontSize = 12, Foreground = Ui.Secondary, VerticalAlignment = VerticalAlignment.Center });
        title.Children.Add(name);

        var reason = new TextBlock
        {
            Text = item.Reason,
            FontSize = 12,
            Foreground = Ui.Secondary,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1
        };
        var pathText = new TextBlock
        {
            Text = path,
            FontSize = 11,
            Foreground = Ui.Tertiary,
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxLines = 1
        };
        var content = new StackPanel { Spacing = 2 };
        content.Children.Add(title);
        content.Children.Add(reason);
        if (blocked && !string.IsNullOrWhiteSpace(safety.Note))
        {
            // 锁定原因直接摆出来：否则用户只看到一个勾不动的复选框，不知道是安全规则锁的。
            content.Children.Add(new TextBlock
            {
                Text = "安全规则已锁定：" + safety.Note,
                FontSize = 11,
                Foreground = Ui.Critical,
                TextTrimming = TextTrimming.CharacterEllipsis,
                MaxLines = 2
            });
        }
        content.Children.Add(pathText);

        var sizeText = new TextBlock
        {
            Text = JunkSizeScanner.FormatBytes(item.SizeBytes),
            FontSize = 12,
            Foreground = Ui.Secondary,
            VerticalAlignment = VerticalAlignment.Center,
            MinWidth = 62,
            TextAlignment = TextAlignment.Right
        };

        var header = new Grid { ColumnSpacing = 10 };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(check);
        Grid.SetColumn(badge, 1);
        header.Children.Add(badge);
        Grid.SetColumn(content, 2);
        header.Children.Add(content);
        Grid.SetColumn(sizeText, 3);
        header.Children.Add(sizeText);

        var details = new StackPanel { Spacing = 8, Padding = new Thickness(12, 0, 8, 10) };
        if (isDirectory)
            details.Children.Add(DetailLine("注意", "这是一个目录，删除会连同其中的所有文件和子目录一起删除。", true));
        details.Children.Add(DetailLine("删除原因", item.Reason));
        details.Children.Add(DetailLine("风险说明", string.IsNullOrWhiteSpace(item.RiskDetail) ? "AI 没有提供更多说明。" : item.RiskDetail, risk >= JunkRisk.Medium));
        if (!string.IsNullOrWhiteSpace(item.Suggestion)) details.Children.Add(DetailLine("建议", item.Suggestion));
        if (!string.IsNullOrWhiteSpace(safety.Escalation)) details.Children.Add(DetailLine("安全规则", safety.Escalation, true));
        if (!string.IsNullOrWhiteSpace(safety.Note)) details.Children.Add(DetailLine(blocked ? "不建议删除的原因" : "检查结果", safety.Note, blocked));
        details.Children.Add(DetailLine("完整路径", path, mono: true));

        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(0, 2, 0, 0) };
        var reveal = new Button { Content = "打开所在位置", Padding = new Thickness(10, 4, 10, 4), IsEnabled = safety.Exists };
        reveal.Click += (_, _) => ShellFileOperations.Reveal(path);
        actions.Children.Add(reveal);
        var copy = new Button { Content = "复制路径", Padding = new Thickness(10, 4, 10, 4) };
        copy.Click += (_, _) => CopyPath(path);
        actions.Children.Add(copy);
        details.Children.Add(actions);

        var status = new TextBlock { Visibility = Visibility.Collapsed, FontSize = 12, TextWrapping = TextWrapping.Wrap, Foreground = Ui.Secondary };
        details.Children.Add(status);

        var expander = new Expander
        {
            Header = header,
            Content = details,
            IsExpanded = false,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Background = Ui.Card,
            Padding = new Thickness(2, 2, 2, 2)
        };
        AutomationProperties.SetName(expander, path);

        var state = new ItemState
        {
            Item = item,
            Safety = safety,
            Path = path,
            Category = category,
            Risk = risk,
            Blocked = blocked,
            IsDirectory = isDirectory,
            Check = check,
            SizeText = sizeText,
            Status = status,
            Expander = expander,
            Bytes = item.SizeBytes
        };
        check.Checked += (_, _) => UpdateSelection();
        check.Unchecked += (_, _) => UpdateSelection();
        return state;
    }

    private static FrameworkElement DetailLine(string label, string value, bool warning = false, bool mono = false)
    {
        var panel = new StackPanel { Spacing = 1 };
        panel.Children.Add(new TextBlock { Text = label, FontSize = 11, Foreground = Ui.Secondary });
        var content = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(value) ? "—" : value,
            TextWrapping = TextWrapping.Wrap,
            IsTextSelectionEnabled = true,
            FontSize = 12
        };
        if (mono) content.FontFamily = new FontFamily("Cascadia Code, Consolas");
        if (warning) content.Foreground = Ui.Critical;
        panel.Children.Add(content);
        return panel;
    }

    private void CopyPath(string path)
    {
        try
        {
            var package = new DataPackage();
            package.SetText(path);
            Clipboard.SetContent(package);
            Clipboard.Flush();
        }
        catch (Exception ex) when (ex is InvalidOperationException or UnauthorizedAccessException) { }
    }

    private void SelectLowRisk()
    {
        foreach (var state in _states)
            if (!state.Blocked && !state.Deleted && state.Risk == JunkRisk.Low) state.Check.IsChecked = true;
        UpdateSelection();
    }

    private void ClearAll()
    {
        foreach (var state in _states) state.Check.IsChecked = false;
        UpdateSelection();
    }

    private void ToggleAll(bool expand)
    {
        foreach (var state in _states)
            if (state.Expander.Visibility == Visibility.Visible) state.Expander.IsExpanded = expand;
    }

    private void ApplyFilter()
    {
        if (_filter.SelectedItem is not ComboBoxItem { Tag: string tag }) return;
        foreach (var state in _states)
        {
            var visible = tag switch
            {
                "low" => !state.Blocked && state.Risk == JunkRisk.Low,
                "medium" => !state.Blocked && state.Risk == JunkRisk.Medium,
                "high" => !state.Blocked && state.Risk == JunkRisk.High,
                "blocked" => state.Blocked,
                _ => true
            };
            state.Expander.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        }
        foreach (var pair in _categoryHeaders)
            pair.Value.Visibility = _categoryItems[pair.Key].Any(s => s.Expander.Visibility == Visibility.Visible)
                ? Visibility.Visible : Visibility.Collapsed;
    }

    private void UpdateSummary()
    {
        if (_lowChip is null) return;
        var active = _states.Where(s => !s.Deleted).ToList();
        var low = active.Count(s => !s.Blocked && s.Risk == JunkRisk.Low);
        var medium = active.Count(s => !s.Blocked && s.Risk == JunkRisk.Medium);
        var high = active.Count(s => !s.Blocked && s.Risk == JunkRisk.High);
        var blocked = active.Count(s => s.Blocked);
        _lowChip.Text = "低风险 " + low;
        _mediumChip.Text = "中风险 " + medium;
        _highChip.Text = "高风险 " + high;
        _blockedChip.Text = "不建议 " + blocked;
    }

    private void UpdateSelection()
    {
        if (_summaryCount is null) return;
        var active = _states.Where(s => !s.Deleted).ToList();
        var selected = SelectedStates();
        var totalBytes = active.Where(s => !s.Blocked).Sum(s => s.Bytes);
        var selectedBytes = selected.Sum(s => s.Bytes);
        var totalPartial = active.Any(s => !s.Blocked && s.Partial);
        var selectedPartial = selected.Any(s => s.Partial);
        _summaryCount.Text = selected.Count + " / " + active.Count;
        _summarySize.Text = (selectedPartial ? "≥ " : "") + JunkSizeScanner.FormatBytes(selectedBytes) +
            " / " + (totalPartial ? "≥ " : "") + JunkSizeScanner.FormatBytes(totalBytes);

        var medium = selected.Count(s => s.Risk == JunkRisk.Medium);
        var high = selected.Count(s => s.Risk == JunkRisk.High);
        _warning.IsOpen = medium + high > 0;
        if (medium + high > 0)
            _warning.Message = "已选中 " + medium + " 项中风险、" + high + " 项高风险内容。删除前请确认这些文件不再需要，尤其是高风险项目。";
        if (high == 0) _riskConfirm.IsChecked = false;
        _riskConfirm.Visibility = high > 0 ? Visibility.Visible : Visibility.Collapsed;
        IsPrimaryButtonEnabled = !_busy && selected.Count > 0;
    }

    private List<ItemState> SelectedStates() =>
        _states.Where(s => s.Check.IsChecked == true && !s.Blocked && !s.Deleted).ToList();

    private async Task MeasureSizesAsync()
    {
        _measure = CancellationTokenSource.CreateLinkedTokenSource(_lifetime.Token);
        try
        {
            foreach (var state in _states.Where(s => !s.Blocked).ToList())
            {
                if (_closed || _measure.IsCancellationRequested) break;
                JunkSize? size = null;
                try
                {
                    size = await JunkSizeScanner.MeasureAsync(state.Path, state.IsDirectory, _measure.Token, TimeSpan.FromSeconds(5));
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException) { continue; }
                if (size is null) continue;
                state.Bytes = size.Bytes;
                state.Partial = size.Partial;
                _ = DispatcherQueue.TryEnqueue(() =>
                {
                    if (_closed) return;
                    state.SizeText.Text = (size.Partial ? "≥ " : "") + JunkSizeScanner.FormatBytes(size.Bytes);
                    UpdateSummary();
                    UpdateSelection();
                });
                await Task.Delay(15, _measure.Token);
            }
        }
        catch (OperationCanceledException) { }
        finally
        {
            if (!_closed) _ = DispatcherQueue.TryEnqueue(() => { UpdateSummary(); UpdateSelection(); });
        }
    }

    private async void OnPrimaryClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (_completed) return;
        args.Cancel = true;
        if (_busy) return;
        var selected = SelectedStates();
        if (selected.Count == 0)
        {
            ShowError("请先勾选要删除的项目。");
            return;
        }
        if (selected.Any(s => s.Risk == JunkRisk.High) && _riskConfirm.IsChecked != true)
        {
            ShowError("已选中高风险项目，请先展开阅读风险说明并勾选确认。");
            return;
        }
        await ExecuteAsync(selected);
    }

    private void OnSecondaryClick(ContentDialog sender, ContentDialogButtonClickEventArgs args)
    {
        if (!_busy) return;
        args.Cancel = true;
        _measure?.Cancel();
        _lifetime.Cancel();
        _progressText.Text = "正在停止：当前文件完成后会停下。";
    }

    private void OnClosing(ContentDialog sender, ContentDialogClosingEventArgs args)
    {
        if (_busy) args.Cancel = true;
        else { _measure?.Cancel(); _lifetime.Cancel(); }
    }

    private void ShowError(string message)
    {
        _error.Message = message;
        _error.IsOpen = true;
    }

    private async Task ExecuteAsync(List<ItemState> selected)
    {
        _busy = true;
        _completed = false;
        _error.IsOpen = false;
        _result.IsOpen = false;
        _measure?.Cancel();
        _filter.IsEnabled = false;
        PrimaryButtonText = "正在删除…";
        IsPrimaryButtonEnabled = false;
        SecondaryButtonText = "停止";
        CloseButtonText = "";
        _progress.Visibility = Visibility.Visible;
        _progress.IsIndeterminate = false;
        _progress.Value = 0;
        _progressText.Visibility = Visibility.Visible;
        _progressText.Text = "正在准备直接删除…";

        var unique = new List<ItemState>();
        foreach (var state in selected.OrderBy(s => s.Path.Length))
            if (!unique.Any(parent => IsSameOrInside(state.Path, parent.Path))) unique.Add(state);
        var merged = selected.Count - unique.Count;

        var deleted = 0;
        var failed = 0;
        var skipped = 0;
        long freed = 0;
        var errors = new List<string>();
        var canceled = false;
        try
        {
            var targets = new List<DeleteTarget>();
            var targetStates = new Dictionary<string, ItemState>(StringComparer.OrdinalIgnoreCase);
            long totalBytes = 0;
            foreach (var state in unique)
            {
                if (_lifetime.IsCancellationRequested) { canceled = true; break; }
                var check = JunkSafety.Evaluate(state.Item with { Path = state.Path }, _environment);
                if (check.Blocked)
                {
                    skipped++;
                    state.MarkSkipped(check.Note);
                    errors.Add(state.Path + "：" + check.Note);
                    CleanupAuditLog.Append(_dataDirectory, _depth, _plan.Summary, state.Path, false, false, 0, "安全检查阻止：" + check.Note);
                    continue;
                }

                var key = NormalizePath(state.Path);
                targetStates[key] = state;
                targets.Add(new DeleteTarget(state.Path, state.IsDirectory, Math.Max(0, state.Bytes)));
                totalBytes += Math.Max(0, state.Bytes);
            }

            if (targets.Count > 0)
            {
                _progress.IsIndeterminate = totalBytes <= 0;
                var progress = new Progress<DeleteProgress>(p =>
                {
                    if (_closed) return;
                    if (p.Indeterminate)
                    {
                        // 原生递归删除大目录时没有逐文件进度，显示不确定进度条。
                        _progress.IsIndeterminate = true;
                    }
                    else
                    {
                        var total = p.BytesTotal > 0 ? p.BytesTotal : p.BytesDeleted;
                        _progress.IsIndeterminate = total <= 0;
                        if (total > 0)
                            _progress.Value = Math.Min(99, p.BytesDeleted * 100d / Math.Max(total, p.BytesDeleted));
                    }
                    var size = p.BytesTotal > 0
                        ? $"{JunkSizeScanner.FormatBytes(p.BytesDeleted)} / {JunkSizeScanner.FormatBytes(p.BytesTotal)}"
                        : JunkSizeScanner.FormatBytes(p.BytesDeleted);
                    _progressText.Text = $"已处理 {Math.Min(p.TargetsDone + 1, Math.Max(1, p.TargetsTotal))}/{p.TargetsTotal} 项 · 约释放 {size}" +
                        $"\n{p.CurrentPath}";
                });

                DeleteOutcome outcome;
                try
                {
                    outcome = await FastFileDeleter.DeleteAsync(targets, totalBytes, progress, _lifetime.Token);
                }
                catch (OperationCanceledException)
                {
                    outcome = new DeleteOutcome(true, 0, 0, []);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    outcome = new DeleteOutcome(false, 0, 0, []);
                    errors.Add("删除引擎异常：" + ex.Message);
                }

                canceled = outcome.Canceled;
                freed = outcome.BytesDeleted;
                var results = new Dictionary<string, DeleteTargetResult>(StringComparer.OrdinalIgnoreCase);
                foreach (var result in outcome.Targets) results[NormalizePath(result.Path)] = result;

                foreach (var pair in targetStates)
                {
                    var state = pair.Value;
                    if (results.TryGetValue(pair.Key, out var result))
                    {
                        if (result.Success)
                        {
                            deleted++;
                            state.Deleted = true;
                            CleanupAuditLog.Append(_dataDirectory, _depth, _plan.Summary, state.Path, false, true, result.BytesDeleted, "");
                        }
                        else
                        {
                            failed++;
                            var message = result.Error ?? "项目仍然存在，可能被占用或被系统拒绝。";
                            state.MarkSkipped(message);
                            errors.Add(state.Path + "：" + message);
                            CleanupAuditLog.Append(_dataDirectory, _depth, _plan.Summary, state.Path, false, false, result.BytesDeleted, message);
                        }
                    }
                    else if (!File.Exists(state.Path) && !Directory.Exists(state.Path))
                    {
                        deleted++;
                        state.Deleted = true;
                        CleanupAuditLog.Append(_dataDirectory, _depth, _plan.Summary, state.Path, false, true, 0, "");
                    }
                    else
                    {
                        skipped++;
                        var message = canceled ? "已停止，可能已部分删除。" : "未处理。";
                        state.MarkSkipped(message);
                        CleanupAuditLog.Append(_dataDirectory, _depth, _plan.Summary, state.Path, false, false, 0, message);
                    }
                }
            }
        }
        finally
        {
            _progress.Value = 100;
            _busy = false;
            _completed = false;
            _filter.IsEnabled = true;
            PrimaryButtonText = "删除所选";
            SecondaryButtonText = "";
            CloseButtonText = "关闭";
            _progress.Visibility = Visibility.Collapsed;
            _progressText.Visibility = Visibility.Collapsed;
            _lockTcs = null;
            _lockPrompt.Visibility = Visibility.Collapsed;

            RemoveDeletedRows();
            var result = new JunkCleanupResult(deleted, failed, skipped, freed, errors);
            _onCompleted?.Invoke(result);
            _result.Severity = failed + skipped == 0 && !canceled ? InfoBarSeverity.Success : InfoBarSeverity.Warning;
            _result.Message = BuildResultText(result, merged, canceled);
            _result.IsOpen = true;
            UpdateSummary();
            UpdateSelection();
            if (_lifetime.IsCancellationRequested)
            {
                PrimaryButtonText = "关闭";
                IsPrimaryButtonEnabled = false;
            }
        }
    }

    /// <summary>成功删除的行直接移除，弹窗保持打开，剩余项目可以继续分批勾选删除。</summary>
    private void RemoveDeletedRows()
    {
        foreach (var state in _states.Where(s => s.Deleted).ToList())
        {
            if (state.Expander.Parent is Panel parent) parent.Children.Remove(state.Expander);
            _states.Remove(state);
            if (_categoryItems.TryGetValue(state.Category, out var items)) items.Remove(state);
        }

        foreach (var pair in _categoryHeaders.ToList())
        {
            if (_categoryItems.TryGetValue(pair.Key, out var items) && items.Count > 0) continue;
            if (pair.Value.Parent is Panel parent) parent.Children.Remove(pair.Value);
            _categoryHeaders.Remove(pair.Key);
            _categoryItems.Remove(pair.Key);
        }
        ApplyFilter();
    }

    private static string NormalizePath(string path)
    {
        try { return Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException) { return path; }
    }

    private static string BuildResultText(JunkCleanupResult result, int merged, bool canceled)
    {
        var parts = new List<string>();
        if (canceled) parts.Add("已停止");
        parts.Add("已删除 " + result.Deleted + " 项");
        if (result.FreedBytes > 0) parts.Add("约释放 " + JunkSizeScanner.FormatBytes(result.FreedBytes));
        if (result.Failed > 0) parts.Add("跳过 " + result.Failed + " 项（被占用/无法删除）");
        if (result.Skipped > 0) parts.Add("跳过 " + result.Skipped + " 项");
        if (merged > 0) parts.Add("已自动合并 " + merged + " 个重复/子项");
        var text = string.Join(" · ", parts) + "。";
        if (result.Errors.Count > 0)
            text += "\n" + string.Join("\n", result.Errors.Take(8)) + (result.Errors.Count > 8 ? "\n…" : "");
        return text;
    }

    private static bool IsSameOrInside(string path, string parent)
    {
        var normalized = Path.TrimEndingDirectorySeparator(path);
        var root = Path.TrimEndingDirectorySeparator(parent);
        if (string.Equals(normalized, root, StringComparison.OrdinalIgnoreCase)) return true;
        var prefix = root.EndsWith(Path.DirectorySeparatorChar) ? root : root + Path.DirectorySeparatorChar;
        return normalized.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }

    private static string DisplayName(string path, bool isDirectory)
    {
        try
        {
            var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var name = Path.GetFileName(trimmed);
            if (!string.IsNullOrWhiteSpace(name)) return name;
        }
        catch (ArgumentException) { }
        return path;
    }

    private static string RiskLabel(JunkRisk risk) => risk switch
    {
        JunkRisk.Low => "低风险",
        JunkRisk.Medium => "中风险",
        _ => "高风险"
    };

    private static Brush RiskBackground(JunkRisk risk) => risk switch
    {
        JunkRisk.Low => Ui.SuccessBackground,
        JunkRisk.Medium => Ui.CautionBackground,
        _ => Ui.CriticalBackground
    };

    private static Brush RiskForeground(JunkRisk risk) => risk switch
    {
        JunkRisk.Low => Ui.Success,
        JunkRisk.Medium => Ui.Caution,
        _ => Ui.Critical
    };

    private static Border Chip(string text, Brush background, Brush foreground, out TextBlock label)
    {
        label = new TextBlock { Text = text, FontSize = 11, Foreground = foreground };
        return new Border
        {
            Background = background,
            CornerRadius = new CornerRadius(9),
            Padding = new Thickness(8, 2, 8, 2),
            Child = label
        };
    }

    private static class Ui
    {
        internal static Brush Card => Resource("CardBackgroundFillColorDefaultBrush", Windows.UI.Color.FromArgb(0x14, 0x80, 0x80, 0x80));
        internal static Brush Secondary => Resource("TextFillColorSecondaryBrush", Windows.UI.Color.FromArgb(0xB0, 0x80, 0x80, 0x80));
        internal static Brush Tertiary => Resource("TextFillColorTertiaryBrush", Windows.UI.Color.FromArgb(0x80, 0x80, 0x80, 0x80));
        internal static Brush Success => Resource("SystemFillColorSuccessBrush", Windows.UI.Color.FromArgb(0xFF, 0x10, 0x7C, 0x10));
        internal static Brush Caution => Resource("SystemFillColorCautionBrush", Windows.UI.Color.FromArgb(0xFF, 0x9D, 0x5D, 0x00));
        internal static Brush Critical => Resource("SystemFillColorCriticalBrush", Windows.UI.Color.FromArgb(0xFF, 0xC4, 0x2B, 0x1C));
        internal static Brush SuccessBackground => Resource("SystemFillColorSuccessBackgroundBrush", Windows.UI.Color.FromArgb(0x24, 0x10, 0x7C, 0x10));
        internal static Brush CautionBackground => Resource("SystemFillColorCautionBackgroundBrush", Windows.UI.Color.FromArgb(0x24, 0x9D, 0x5D, 0x00));
        internal static Brush CriticalBackground => Resource("SystemFillColorCriticalBackgroundBrush", Windows.UI.Color.FromArgb(0x24, 0xC4, 0x2B, 0x1C));
        internal static Brush NeutralBackground => Resource("SystemFillColorNeutralBackgroundBrush", Windows.UI.Color.FromArgb(0x1A, 0x80, 0x80, 0x80));

        private static Brush Resource(string key, Windows.UI.Color fallback)
        {
            if (Application.Current?.Resources.TryGetValue(key, out var value) == true && value is Brush brush) return brush;
            return new SolidColorBrush(fallback);
        }
    }
}

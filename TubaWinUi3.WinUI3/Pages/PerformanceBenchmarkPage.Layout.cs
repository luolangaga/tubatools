using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Markup;

namespace TubaWinUi3.Pages;

public sealed partial class PerformanceBenchmarkPage
{
    private const int StageCount = 7;
    private CheckBox _chkLatency = null!;
    private readonly string[] _stageNames = ["处理器", "核间延迟", "内存", "硬盘", "显卡", "浏览器", "Windows 界面"];
    private readonly string[] _stageDescriptions = ["单核 · 多核", "核心之间的通信延迟", "读取物理内存容量", "顺序读写 · 4K 随机读写", "FurMark · 1080p 渲染", "脚本 · 页面 · 动画 · 交互", "真实控件 · 连续五轮测量"];
    private readonly string[] _stageIcons = ["NavIcons/cpu.svg", "BuiltinIcons/cpu-ranking.svg", "NavIcons/memory.svg", "NavIcons/disk.svg", "NavIcons/gpu.svg", "NavIcons/network.svg", "HwIcons/system.svg"];
    private readonly TextBlock[] _stageLabels = new TextBlock[StageCount];
    private readonly TextBlock[] _stageSummaries = new TextBlock[StageCount];
    private readonly ProgressRing[] _stageRings = new ProgressRing[StageCount];
    private readonly Expander[] _detailSections = new Expander[StageCount];
    private TextBlock _phaseTitle = null!;
    private TextBlock _elapsedText = null!;
    private TextBlock _completionText = null!;
    private TextBlock _resultNote = null!;
    private readonly Stopwatch _runClock = new();
    private DispatcherTimer _runTimer = null!;
    private CheckBox[] StageChecks => [_chkCpu, _chkLatency, _chkMem, _chkDisk, _chkGpu, _chkBrowser, _chkWin];

    private TextBlock Label(string text, double size = 14, bool secondary = false)
    {
        string key = secondary ? "TextFillColorSecondaryBrush" : "TextFillColorPrimaryBrush";
        var label = (TextBlock)XamlReader.Load("<TextBlock xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" Foreground=\"{ThemeResource " + key + "}\"/>");
        label.Text = text; label.FontSize = size; label.TextWrapping = TextWrapping.Wrap;
        return label;
    }

    private Border Surface(UIElement child, double padding = 20)
    {
        var border = (Border)XamlReader.Load("<Border xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\" Background=\"{ThemeResource CardBackgroundFillColorDefaultBrush}\" BorderBrush=\"{ThemeResource CardStrokeColorDefaultBrush}\"/>");
        border.Child = child; border.CornerRadius = new CornerRadius(10); border.Padding = new Thickness(padding); border.BorderThickness = new Thickness(1);
        return border;
    }

    private Button ActionButton(string text, string id, RoutedEventHandler handler, bool accent = false)
    {
        var button = new Button { Content = text, Padding = new Thickness(16, 9, 16, 9), MinHeight = 40 };
        if (accent) button.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
        AutomationProperties.SetAutomationId(button, id);
        button.Click += handler;
        return button;
    }

    private ScrollViewer BuildUI()
    {
        cardBg = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"];
        cardBorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"];
        _startBtn = ActionButton("开始测试", "BenchmarkStart", OnStartClick, true);
        _stopBtn = ActionButton("停止测试", "BenchmarkStop", OnStopClick);
        _stopBtn.Visibility = Visibility.Collapsed;
        _exportBtn = ActionButton("导出报告", "BenchmarkExport", OnExportClick);
        _exportBtn.IsEnabled = false;
        _historyBtn = ActionButton("历史对比", "BenchmarkHistory", OnHistoryClick);
        _rankingBtn = ActionButton("社区排行", "BenchmarkRanking", OnRankingClick);
        _latencyOnlyBtn = ActionButton("单测核间延迟", "BenchmarkLatency", OnLatencyOnlyClick);
        var more = new Button { Content = new FontIcon { Glyph = "\uE712", FontSize = 16 }, MinHeight = 40 };
        AutomationProperties.SetName(more, "更多测试操作");
        AutomationProperties.SetAutomationId(more, "BenchmarkMore");
        var flyout = new Flyout();
        flyout.Content = new StackPanel { Spacing = 8, Children = { _exportBtn, _rankingBtn, _latencyOnlyBtn } };
        more.Flyout = flyout;

        var heading = new StackPanel { Spacing = 5, Children = { Label("性能测试", 28), Label("用真实负载，了解电脑的每一面。", 13, true) } };
        var commands = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center, Children = { _historyBtn, more, _startBtn, _stopBtn } };
        var header = new Grid { ColumnSpacing = 16, ColumnDefinitions = { new() { Width = new GridLength(1, GridUnitType.Star) }, new() { Width = GridLength.Auto } }, Children = { heading, commands } };
        Grid.SetColumn(commands, 1);

        _phaseTitle = Label("就绪", 13);
        _phaseTitle.FontWeight = FontWeights.SemiBold;
        AutomationProperties.SetAutomationId(_phaseTitle, "BenchmarkPhase");
        _statusText = Label("建议接通电源，并关闭占用资源的应用。", 12, true);
        AutomationProperties.SetAutomationId(_statusText, "BenchmarkStatus");
        _completionText = Label("已选择 7 项", 12, true);
        _elapsedText = Label("00:00", 13, true);
        _elapsedText.FontFamily = new FontFamily("Consolas");
        _globalProgress = new ProgressBar { Maximum = StageCount, Value = 0, Height = 2 };
        AutomationProperties.SetName(_globalProgress, "已完成测试项目");
        var live = new Grid { ColumnSpacing = 12, ColumnDefinitions = { new() { Width = new GridLength(1, GridUnitType.Star) }, new() { Width = GridLength.Auto } } };
        live.Children.Add(new StackPanel { Spacing = 4, Children = { _phaseTitle, _statusText } });
        var time = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 12, VerticalAlignment = VerticalAlignment.Top, Children = { _completionText, _elapsedText } };
        live.Children.Add(time); Grid.SetColumn(time, 1);
        var runStatus = new StackPanel { Spacing = 8, Children = { live, _globalProgress } };

        _chkLatency = new CheckBox(); _chkCpu = new CheckBox(); _chkMem = new CheckBox(); _chkDisk = new CheckBox();
        _chkGpu = new CheckBox(); _chkBrowser = new CheckBox(); _chkWin = new CheckBox();
        var queue = new Grid { RowSpacing = 4, ColumnSpacing = 12 };
        queue.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        queue.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        for (int i = 0; i < StageCount; i++) queue.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var rows = new List<FrameworkElement>();
        for (int i = 0; i < StageCount; i++)
        {
            var check = StageChecks[i];
            check.IsChecked = true;
            check.HorizontalAlignment = HorizontalAlignment.Stretch;
            check.HorizontalContentAlignment = HorizontalAlignment.Stretch;
            AutomationProperties.SetName(check, _stageNames[i]);
            AutomationProperties.SetAutomationId(check, "BenchmarkSelect" + i);
            var title = Label(_stageNames[i]); title.FontWeight = FontWeights.SemiBold;
            var icon = new Image { Source = new SvgImageSource(new Uri("ms-appx:///Assets/" + _stageIcons[i])), Width = 32, Height = 32, VerticalAlignment = VerticalAlignment.Center };
            var text = new StackPanel { Spacing = 3, Children = { title, Label(_stageDescriptions[i], 11, true) } };
            var checkContent = new Grid { ColumnSpacing = 10, ColumnDefinitions = { new() { Width = new GridLength(32) }, new() { Width = new GridLength(1, GridUnitType.Star) } }, Children = { icon, text } };
            Grid.SetColumn(text, 1); check.Content = checkContent;
            _stageLabels[i] = Label("待测试", 11, true);
            _stageSummaries[i] = Label("展开查看测量明细", 12, true);
            _stageRings[i] = new ProgressRing { Width = 14, Height = 14, IsActive = false, Visibility = Visibility.Collapsed };
            var state = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(44, 2, 0, 0), Children = { _stageRings[i], _stageLabels[i] } };
            var row = new StackPanel { Padding = new Thickness(0, 4, 0, 6), Children = { check, state } };
            rows.Add(row); queue.Children.Add(row); Grid.SetRow(row, i); Grid.SetColumnSpan(row, 2);
            check.Checked += OnSelectionChanged; check.Unchecked += OnSelectionChanged;
        }
        var queueTitle = Label("测试项目", 16); queueTitle.FontWeight = FontWeights.SemiBold;
        var queueSurface = Surface(new StackPanel { Spacing = 8, Children = { queueTitle, queue, Label("取消或失败的项目不计分。\n综合分需要处理器、内存、硬盘、显卡及浏览器数据。", 12, true) } }, 20);
        queueSurface.VerticalAlignment = VerticalAlignment.Top;

        var resultTitle = Label("结果总览", 18); resultTitle.FontWeight = FontWeights.SemiBold;
        _resultNote = Label("100 分为参考基准，性能越强可超过 100 分。", 12, true);
        Panel[] detailContent = [BuildCpuContent(), BuildLatencyContent(), BuildMemoryContent(), BuildDiskContent(), BuildGpuContent(), BuildBrowserContent(), BuildWinContent()];
        var details = new StackPanel { Spacing = 8 };
        for (int i = 0; i < StageCount; i++)
        {
            PrepareDetails(detailContent[i]);
            _detailSections[i] = new Expander
            {
                HorizontalAlignment = HorizontalAlignment.Stretch, HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Header = new StackPanel { Spacing = 4, Children = { Label(_stageNames[i], 14), _stageSummaries[i] } },
                Content = detailContent[i]
            };
            AutomationProperties.SetAutomationId(_detailSections[i], "BenchmarkDetail" + i);
            details.Children.Add(_detailSections[i]);
        }
        var results = new StackPanel { Spacing = 12, Children = { resultTitle, BuildTopCards(), _resultNote } };
        var workspace = new Grid { ColumnSpacing = 24, RowSpacing = 24 };
        workspace.ColumnDefinitions.Add(new() { Width = new GridLength(300) });
        workspace.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        workspace.RowDefinitions.Add(new() { Height = GridLength.Auto });
        workspace.RowDefinitions.Add(new() { Height = GridLength.Auto });
        workspace.RowDefinitions.Add(new() { Height = GridLength.Auto });
        var resultColumn = new StackPanel { Spacing = 16, Children = { results, details } };
        workspace.Children.Add(queueSurface); workspace.Children.Add(resultColumn); Grid.SetColumn(resultColumn, 1);
        bool compactLayout = false;
        var body = new StackPanel { Spacing = 20, Padding = new Thickness(24, 12, 24, 24), Children = { header, runStatus, workspace } };
        body.SizeChanged += (_, e) =>
        {
            bool narrow = e.NewSize.Width < 900;
            workspace.ColumnDefinitions[0].Width = narrow ? new GridLength(1, GridUnitType.Star) : new GridLength(300);
            workspace.ColumnDefinitions[1].Width = narrow ? new GridLength(0) : new GridLength(1, GridUnitType.Star);
            if (compactLayout != narrow)
            {
                compactLayout = narrow;
                if (narrow)
                {
                    resultColumn.Children.Clear(); workspace.Children.Remove(resultColumn);
                    workspace.Children.Add(results); workspace.Children.Add(details);
                }
                else
                {
                    workspace.Children.Remove(results); workspace.Children.Remove(details);
                    resultColumn.Children.Add(results); resultColumn.Children.Add(details); workspace.Children.Add(resultColumn);
                }
            }
            Grid.SetColumn(results, 0); Grid.SetRow(results, 0);
            Grid.SetRow(queueSurface, narrow ? 1 : 0);
            Grid.SetColumn(details, 0); Grid.SetRow(details, 2);
            for (int i = 0; i < rows.Count; i++) { Grid.SetRow(rows[i], narrow ? i / 2 : i); Grid.SetColumn(rows[i], narrow ? i % 2 : 0); Grid.SetColumnSpan(rows[i], narrow ? 1 : 2); }
            _historyBtn.Visibility = e.NewSize.Width < 650 ? Visibility.Collapsed : Visibility.Visible;
            // History remains reachable at narrow widths.
            if (_historyBtn.Visibility == Visibility.Collapsed)
            {
                if (!((StackPanel)flyout.Content).Children.Contains(_historyOverflow))
                    ((StackPanel)flyout.Content).Children.Insert(0, _historyOverflow);
            }
        };
        _historyOverflow = ActionButton("历史对比", "BenchmarkHistoryOverflow", OnHistoryClick);
        _runTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _runTimer.Tick += (_, _) => _elapsedText.Text = _runClock.Elapsed.ToString(@"mm\:ss");
        // The main window owns Mica/Acrylic and custom backgrounds. Keep this page transparent.
        var scroll = new ScrollViewer();
        scroll.Content = body; scroll.HorizontalScrollMode = ScrollMode.Disabled; scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        return scroll;
    }

    private static void PrepareDetails(DependencyObject element)
    {
        // Let the native Expander provide theme-aware text, including contrast themes.
        if (element is TextBlock text) text.ClearValue(TextBlock.ForegroundProperty);
        if (element is Panel panel) foreach (var child in panel.Children) PrepareDetails(child);
        if (element is Border border && border.Child != null) { border.ClearValue(Border.BackgroundProperty); PrepareDetails(border.Child); }
    }

    private Button _historyOverflow = null!;

    private void OnSelectionChanged(object sender, RoutedEventArgs e)
    {
        if (_isRunning) return;
        int count = StageChecks.Count(c => c.IsChecked == true);
        _completionText.Text = $"{count} 个项目已选择";
        _startBtn.IsEnabled = count > 0;
        if (_runClock.Elapsed == TimeSpan.Zero)
            for (int i = 0; i < StageCount; i++) _stageLabels[i].Text = StageChecks[i].IsChecked == true ? "待测试" : "未选择";
    }

    private Grid BuildTopCards()
    {
        var grid = new Grid { ColumnSpacing = 12 };
        for (int i = 0; i < 3; i++) grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        var gaming = BuildScoreCard("游戏表现", out _gamingScoreText, out _gamingGradeText, out _gamingBar);
        var office = BuildScoreCard("办公表现", out _officeScoreText, out _officeGradeText, out _officeBar);
        var win = BuildScoreCard("界面响应", out _winScoreText, out _winGradeText, out _winBar);
        int index = 0;
        foreach (var content in new[] { gaming, office, win }) { var card = Surface(content, 16); grid.Children.Add(card); Grid.SetColumn(card, index++); }
        return grid;
    }

    private StackPanel BuildScoreCard(string title, out TextBlock score, out TextBlock grade, out ProgressBar bar)
    {
        score = Label("—", 36); score.FontWeight = FontWeights.SemiBold;
        grade = Label("待完成测试", 11, true);
        // Scores have no upper bound. Do not represent a score as a full/empty progress bar.
        bar = new ProgressBar { Visibility = Visibility.Collapsed };
        return new StackPanel { Spacing = 8, Children = { Label(title, 13, true), score, grade } };
    }
}

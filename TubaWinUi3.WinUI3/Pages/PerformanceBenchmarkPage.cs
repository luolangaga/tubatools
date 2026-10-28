using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.Web.WebView2.Core;
using TubaWinUi3.Models;
using TubaWinUi3.Services;
using Windows.Graphics;
using Windows.System;
using Windows.UI;

namespace TubaWinUi3.Pages;

public sealed partial class PerformanceBenchmarkPage : Page
{
	private CancellationTokenSource _cts;
	private PerformanceBenchmarkResult? _result;
	private bool _isRunning;
	private bool _historyInProgress;
	// 每次开始新测试/载入历史记录时自增，用于丢弃仍在途的异步回填（恢复热力图等）
	private int _uiGeneration;
	private Brush cardBg = null!;
	private Brush cardBorderBrush = null!;
	private TextBlock _gamingScoreText = null!;
	private TextBlock _gamingGradeText = null!;
	private ProgressBar _gamingBar = null!;
	private TextBlock _officeScoreText = null!;
	private TextBlock _officeGradeText = null!;
	private ProgressBar _officeBar = null!;
	private TextBlock _winScoreText = null!;
	private TextBlock _winGradeText = null!;
	private ProgressBar _winBar = null!;
	private TextBlock _cpuSingleScoreText = null!;
	private TextBlock _cpuMultiScoreText = null!;
	private TextBlock _cpuLatencyScoreText = null!;
	private Border _latencyGridContainer = null!;
	private Image _latencyHeatmapImage = null!;
	private string? _latencyHeatmapPath;
	private TextBlock _gpuRenderScoreText = null!;
	private TextBlock _gpuFurMarkScoreText = null!;
	private TextBlock _gpuAvgFpsText = null!;
	private TextBlock _gpuMinFpsText = null!;
	private TextBlock _gpuMaxFpsText = null!;
	private TextBlock _gpuNameText = null!;
	private TextBlock _memCapacityText = null!;
	private TextBlock _diskSeqReadScoreText = null!;
	private TextBlock _diskSeqWriteScoreText = null!;
	private TextBlock _disk4KReadScoreText = null!;
	private TextBlock _disk4KWriteScoreText = null!;
	private TextBlock _diskSeqReadDetailText = null!;
	private TextBlock _diskSeqWriteDetailText = null!;
	private TextBlock _disk4KReadDetailText = null!;
	private TextBlock _disk4KWriteDetailText = null!;
	private TextBlock _diskTempText = null!;
	private TextBlock _brJsScoreText = null!;
	private TextBlock _brJsDetailText = null!;
	private TextBlock _brDomScoreText = null!;
	private TextBlock _brDomDetailText = null!;
	private TextBlock _brCardScoreText = null!;
	private TextBlock _brCardDetailText = null!;
	private TextBlock _brCssScoreText = null!;
	private TextBlock _brCssDetailText = null!;
	private TextBlock _brLayoutScoreText = null!;
	private TextBlock _brLayoutDetailText = null!;
	private TextBlock _brEventScoreText = null!;
	private TextBlock _brEventDetailText = null!;
	private TextBlock _winListLoadText = null!;
	private TextBlock _winImageListText = null!;
	private TextBlock _winTabSwitchText = null!;
	private TextBlock _winScrollText = null!;
	private TextBlock _winTreeExpandText = null!;
	private TextBlock _winSortFilterText = null!;
	private TextBlock _winTextRenderText = null!;
	private TextBlock _winTotalText = null!;
	private Button _startBtn = null!;
	private Button _stopBtn = null!;
	private Button _exportBtn = null!;
	private Button _historyBtn = null!;
	private Button _rankingBtn = null!;
	private Button _latencyOnlyBtn = null!;
	private ProgressBar _globalProgress = null!;
	private TextBlock _statusText = null!;
	private CheckBox _chkCpu = null!;
	private CheckBox _chkGpu = null!;
	private CheckBox _chkMem = null!;
	private CheckBox _chkDisk = null!;
	private CheckBox _chkBrowser = null!;
	private CheckBox _chkWin = null!;
	private List<FurMarkGpuInfo> _availableGpus = [];

	// WinUI 性能测试工作区控件
	private ItemsControl _winIconList = null!;
	private ListView _winBigList = null!;
	private TabView _winTabView = null!;
	private ScrollViewer _winScrollHost = null!;
	private TreeView _winTreeView = null!;
	private TextBlock _winLongText = null!;
	private IReadOnlyList<string> _winIconPaths = [];
	private List<KeyValuePair<string, int>> _winSortData = [];
	private readonly List<WinPerformanceRunResult> _winRuns = [];

	// 视觉色板（评分/等级用语义强调色与图表系列色，均取自 ThemeColors，随主题解析）
	private static Color AccentBlue => ThemeColors.AccentBlue;
	private static Color ColorS => ThemeColors.Series3;         // S
	private static Color ColorAPlus => ThemeColors.AccentGreen; // A+
	private static Color ColorA => ThemeColors.AccentBlue;      // A
	private static Color ColorBPlus => ThemeColors.Series4;     // B+
	private static Color ColorB => ThemeColors.AccentOrange;    // B
	private static Color ColorC => ThemeColors.AccentRed;       // C
	private static Color ColorD => ThemeColors.Neutral;         // D（最低，用中性灰与 C 可辨）

	public PerformanceBenchmarkPage()
	{
		_cts = new CancellationTokenSource();
		base.Content = BuildUI();
		Unloaded += (_, _) => { _uiGeneration++; _cts.Cancel(); _runTimer.Stop(); };
		// 导航返回 / 重启应用后，自动回填上一次测试的完整结果，避免重新跑分
		_ = RestoreLastResultAsync();
	}

	private Grid BuildScoreRow(string label, out TextBlock scoreText)
	{
		Grid obj = new()
		{
			ColumnSpacing = 8.0,
			ColumnDefinitions =
			{
				new ColumnDefinition { Width = new GridLength(100.0) },
				new ColumnDefinition { Width = GridLength.Auto },
				new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
			}
		};
		TextBlock textBlock = new()
		{
			Text = label,
			FontSize = 12.0,
			Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
			VerticalAlignment = VerticalAlignment.Center
		};
		obj.Children.Add(textBlock);
		Grid.SetColumn(textBlock, 0);
		scoreText = new TextBlock
		{
			Text = "—",
			FontSize = 14.0,
			FontWeight = FontWeights.SemiBold,
			VerticalAlignment = VerticalAlignment.Center,
			Foreground = new SolidColorBrush(ThemeColors.DimText)
		};
		obj.Children.Add(scoreText);
		Grid.SetColumn(scoreText, 1);
		return obj;
	}

	private StackPanel BuildCpuContent()
	{
		StackPanel obj = new()
		{
			Spacing = 6.0,
			Children =
			{
				(UIElement)BuildScoreRow("单核", out _cpuSingleScoreText),
				(UIElement)BuildScoreRow("多核", out _cpuMultiScoreText)
			}
		};
		return obj;
	}

	private StackPanel BuildLatencyContent()
	{
		var obj = new StackPanel { Spacing = 8 };
		obj.Children.Add(BuildScoreRow("核间延迟", out _cpuLatencyScoreText));
		_latencyHeatmapImage = new Image
		{
			MaxHeight = 400.0,
			Stretch = Stretch.Uniform,
			HorizontalAlignment = HorizontalAlignment.Center,
			Visibility = Visibility.Collapsed
		};
		_latencyGridContainer = new Border
		{
			Visibility = Visibility.Collapsed,
			Padding = new Thickness(8.0),
			CornerRadius = new CornerRadius(8.0),
			Background = cardBg,
			Child = _latencyHeatmapImage
		};
		obj.Children.Add(_latencyGridContainer);
		return obj;
	}

	private StackPanel BuildGpuContent()
	{
		_gpuNameText = new TextBlock
		{
			Text = "",
			FontSize = 12.0,
			Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
			TextWrapping = TextWrapping.Wrap
		};
		return new StackPanel
		{
			Spacing = 6.0,
			Children =
			{
				(UIElement)_gpuNameText,
				(UIElement)BuildScoreRow("渲染性能", out _gpuRenderScoreText),
				(UIElement)BuildDetailRow("FurMark分数", out _gpuFurMarkScoreText, out _gpuAvgFpsText),
				(UIElement)BuildDetailRow("FPS范围", out _gpuMinFpsText, out _gpuMaxFpsText)
			}
		};
	}

	private StackPanel BuildMemoryContent()
	{
		StackPanel obj = new() { Spacing = 6.0 };
		Grid grid = new() { ColumnSpacing = 8.0 };
		grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100.0) });
		grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
		TextBlock textBlock = new()
		{
			Text = "容量",
			FontSize = 12.0,
			Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
			VerticalAlignment = VerticalAlignment.Center
		};
		grid.Children.Add(textBlock);
		Grid.SetColumn(textBlock, 0);
		_memCapacityText = new TextBlock
		{
			Text = "—",
			FontSize = 12.0,
			VerticalAlignment = VerticalAlignment.Center,
			Foreground = new SolidColorBrush(ThemeColors.DimText)
		};
		grid.Children.Add(_memCapacityText);
		Grid.SetColumn(_memCapacityText, 1);
		obj.Children.Add(grid);
		return obj;
	}

	private Grid BuildDetailRow(string label, out TextBlock scoreText, out TextBlock detailText)
	{
		Grid obj = new()
		{
			ColumnSpacing = 8.0,
			ColumnDefinitions =
			{
				new ColumnDefinition { Width = new GridLength(100.0) },
				new ColumnDefinition { Width = GridLength.Auto },
				new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) }
			}
		};
		TextBlock textBlock = new()
		{
			Text = label,
			FontSize = 12.0,
			Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
			VerticalAlignment = VerticalAlignment.Center
		};
		obj.Children.Add(textBlock);
		Grid.SetColumn(textBlock, 0);
		scoreText = new TextBlock
		{
			Text = "—",
			FontSize = 14.0,
			FontWeight = FontWeights.SemiBold,
			VerticalAlignment = VerticalAlignment.Center,
			Foreground = new SolidColorBrush(ThemeColors.DimText)
		};
		obj.Children.Add(scoreText);
		Grid.SetColumn(scoreText, 1);
		detailText = new TextBlock
		{
			TextWrapping = TextWrapping.Wrap,
			Text = "",
			FontSize = 12.0,
			Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"],
			VerticalAlignment = VerticalAlignment.Center
		};
		obj.Children.Add(detailText);
		Grid.SetColumn(detailText, 2);
		return obj;
	}

	private StackPanel BuildDiskContent()
	{
		StackPanel obj = new()
		{
			Spacing = 6.0,
			Children =
			{
				(UIElement)BuildDetailRow("顺序读取", out _diskSeqReadScoreText, out _diskSeqReadDetailText),
				(UIElement)BuildDetailRow("顺序写入", out _diskSeqWriteScoreText, out _diskSeqWriteDetailText),
				(UIElement)BuildDetailRow("4K随机读", out _disk4KReadScoreText, out _disk4KReadDetailText),
				(UIElement)BuildDetailRow("4K随机写", out _disk4KWriteScoreText, out _disk4KWriteDetailText)
			}
		};
		Grid grid = new() { ColumnSpacing = 8.0 };
		grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100.0) });
		grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
		TextBlock textBlock = new()
		{
			Text = "温度",
			FontSize = 12.0,
			Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
			VerticalAlignment = VerticalAlignment.Center
		};
		grid.Children.Add(textBlock);
		Grid.SetColumn(textBlock, 0);
		_diskTempText = new TextBlock
		{
			Text = "—",
			FontSize = 12.0,
			VerticalAlignment = VerticalAlignment.Center,
			Foreground = new SolidColorBrush(ThemeColors.DimText)
		};
		grid.Children.Add(_diskTempText);
		Grid.SetColumn(_diskTempText, 1);
		obj.Children.Add(grid);
		return obj;
	}

	private StackPanel BuildBrowserContent()
	{
		StackPanel stackPanel = new() { Spacing = 6.0 };
		stackPanel.Children.Add(BuildDetailRow("JS 引擎", out _brJsScoreText, out _brJsDetailText));
		stackPanel.Children.Add(BuildDetailRow("DOM 表格", out _brDomScoreText, out _brDomDetailText));
		stackPanel.Children.Add(BuildDetailRow("DOM 卡片", out _brCardScoreText, out _brCardDetailText));
		StackPanel stackPanel2 = new() { Spacing = 6.0 };
		stackPanel2.Children.Add(BuildDetailRow("CSS 动画", out _brCssScoreText, out _brCssDetailText));
		stackPanel2.Children.Add(BuildDetailRow("布局重排", out _brLayoutScoreText, out _brLayoutDetailText));
		stackPanel2.Children.Add(BuildDetailRow("事件处理", out _brEventScoreText, out _brEventDetailText));
		Grid grid = new() { ColumnSpacing = 24.0 };
		grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
		grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
		grid.Children.Add(stackPanel);
		Grid.SetColumn(stackPanel, 0);
		grid.Children.Add(stackPanel2);
		Grid.SetColumn(stackPanel2, 1);
		return new StackPanel
		{
			Spacing = 6.0,
			Children = { (UIElement)grid }
		};
	}

	private StackPanel BuildWinContent()
	{
		StackPanel left = new() { Spacing = 6.0 };
		left.Children.Add(BuildDetailRow("列表加载", out _winListLoadText, out var _));
		left.Children.Add(BuildDetailRow($"图片列表 ({WinPerformanceService.ImageListCount}张)", out _winImageListText, out var _));
		left.Children.Add(BuildDetailRow("标签切换", out _winTabSwitchText, out var _));
		left.Children.Add(BuildDetailRow("滚动", out _winScrollText, out var _));
		StackPanel right = new() { Spacing = 6.0 };
		right.Children.Add(BuildDetailRow($"树形展开 ({WinPerformanceService.TreeExpandCount}节点)", out _winTreeExpandText, out var _));
		right.Children.Add(BuildDetailRow($"排序过滤 ({WinPerformanceService.SortFilterCount}条)", out _winSortFilterText, out var _));
		right.Children.Add(BuildDetailRow($"长文本 ({WinPerformanceService.LongTextChars}字符)", out _winTextRenderText, out var _));
		right.Children.Add(BuildDetailRow("平均总耗时", out _winTotalText, out var _));

		Grid layout = new() { ColumnSpacing = 24.0 };
		layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
		layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
		layout.Children.Add(left);
		Grid.SetColumn(left, 0);
		layout.Children.Add(right);
		Grid.SetColumn(right, 1);

		return new StackPanel
		{
			Spacing = 8.0,
			Children =
			{
				(UIElement)layout,
				new TextBlock
				{
					Text = "勾选「WinUI」并点击「开始测试」后，将弹窗执行 5 轮（去掉最慢一轮），实时展示渲染过程。",
					FontSize = 12.0,
					TextWrapping = TextWrapping.Wrap,
					Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"]
				}
			}
		};
	}

	private void SetCheckboxesEnabled(bool enabled)
	{
		_chkCpu.IsEnabled = enabled;
		_chkLatency.IsEnabled = enabled;
		_chkGpu.IsEnabled = enabled;
		_chkMem.IsEnabled = enabled;
		_chkDisk.IsEnabled = enabled;
		_chkBrowser.IsEnabled = enabled;
		_chkWin.IsEnabled = enabled;
	}

	/// <summary>
	/// 把一次完整测试结果整体回填到界面（页面重建后的恢复 / 从历史窗口载入共用）。
	/// 只回填实际有数据的分区，未测过的项目保持占位符 "—"。
	/// </summary>
	private void ApplyResultToUI(PerformanceBenchmarkResult r)
	{
		if (r.GamingScore > 0)
			UpdateTopCard(_gamingScoreText, _gamingGradeText, _gamingBar, r.GamingScore, r.GamingGrade);
		if (r.OfficeScore > 0)
			UpdateTopCard(_officeScoreText, _officeGradeText, _officeBar, r.OfficeScore, r.OfficeGrade);
		if (r.Win.FinalScore > 0)
			UpdateTopCard(_winScoreText, _winGradeText, _winBar, r.Win.FinalScore, r.Win.Grade);

		if (r.Cpu.SingleCoreScore > 0)
			UpdateScoreRow(_cpuSingleScoreText, r.Cpu.SingleCoreScore);
		if (r.Cpu.MultiCoreScore > 0)
			UpdateScoreRow(_cpuMultiScoreText, r.Cpu.MultiCoreScore);
		if (r.Cpu.LatencyMatrix != null && r.Cpu.LatencyScore >= 0)
			if (r.Cpu.LatencyMatrix != null) UpdateScoreRow(_cpuLatencyScoreText, r.Cpu.LatencyScore);
		else _cpuLatencyScoreText.Text = "未测量";

		string gpuName = !string.IsNullOrEmpty(r.Gpu.GpuName) ? r.Gpu.GpuName : r.GpuName;
		if (!string.IsNullOrEmpty(gpuName))
			_gpuNameText.Text = gpuName;
		if (r.Gpu.RenderScore > 0)
		{
			UpdateScoreRow(_gpuRenderScoreText, r.Gpu.RenderScore);
			UpdateDetailRow(_gpuFurMarkScoreText, _gpuAvgFpsText, r.Gpu.FurMarkScore, $"平均 {r.Gpu.AvgFps:F0} FPS");
			UpdateDetailRow(_gpuMinFpsText, _gpuMaxFpsText, (int)r.Gpu.MinFps, $"最低 {r.Gpu.MinFps:F0} / 最高 {r.Gpu.MaxFps:F0}");
		}

		if (r.Memory.TotalCapacityGB > 0)
			_memCapacityText.Text = $"{r.Memory.TotalCapacityGB:F0} GB";

		if (r.Disk.SeqReadMBs > 0)
			UpdateDetailRow(_diskSeqReadScoreText, _diskSeqReadDetailText, r.Disk.SeqReadScore, $"{r.Disk.SeqReadMBs:F0} MB/s");
		if (r.Disk.SeqWriteMBs > 0)
			UpdateDetailRow(_diskSeqWriteScoreText, _diskSeqWriteDetailText, r.Disk.SeqWriteScore, $"{r.Disk.SeqWriteMBs:F0} MB/s");
		if (r.Disk.Random4KReadIops > 0)
			UpdateDetailRow(_disk4KReadScoreText, _disk4KReadDetailText, r.Disk.Random4KReadScore, $"{r.Disk.Random4KReadIops / 1000.0:F0}K IOPS");
		if (r.Disk.Random4KWriteIops > 0)
			UpdateDetailRow(_disk4KWriteScoreText, _disk4KWriteDetailText, r.Disk.Random4KWriteScore, $"{r.Disk.Random4KWriteIops / 1000.0:F0}K IOPS");
		if (r.Disk.Temperature > 0f)
			_diskTempText.Text = $"{r.Disk.Temperature:F0}℃";

		if (r.Browser.TotalScore > 0)
		{
			UpdateDetailRow(_brJsScoreText, _brJsDetailText, r.Browser.JsScore, r.Browser.JsDetail);
			UpdateDetailRow(_brDomScoreText, _brDomDetailText, r.Browser.DomScore, r.Browser.DomDetail);
			UpdateDetailRow(_brCardScoreText, _brCardDetailText, r.Browser.CardScore, r.Browser.CardDetail);
			UpdateDetailRow(_brCssScoreText, _brCssDetailText, r.Browser.CssScore, r.Browser.CssDetail);
			UpdateDetailRow(_brLayoutScoreText, _brLayoutDetailText, r.Browser.LayoutScore, r.Browser.LayoutDetail);
			UpdateDetailRow(_brEventScoreText, _brEventDetailText, r.Browser.EventScore, r.Browser.EventDetail);
		}

		if (r.Win.BestAvgMs > 0)
		{
			_winListLoadText.Text = $"{r.Win.AvgListLoadMs:F0} ms";
			_winImageListText.Text = $"{r.Win.AvgImageListMs:F0} ms";
			_winTabSwitchText.Text = $"{r.Win.AvgTabSwitchMs:F0} ms";
			_winScrollText.Text = $"{r.Win.AvgScrollMs:F0} ms";
			_winTreeExpandText.Text = $"{r.Win.AvgTreeExpandMs:F0} ms";
			_winSortFilterText.Text = $"{r.Win.AvgSortFilterMs:F0} ms";
			_winTextRenderText.Text = $"{r.Win.AvgTextRenderMs:F0} ms";
			_winTotalText.Text = $"{r.Win.BestAvgMs:F0} ms";
		}
	}

	/// <summary>页面刚构建时回填上一次测试结果（含核间延迟热力图），免于重新跑分。</summary>
	private async Task RestoreLastResultAsync()
	{
		try
		{
			int generation = _uiGeneration;
			List<PerformanceBenchmarkResult> history = await Task.Run(PerformanceBenchmarkService.LoadHistory);
			if (generation != _uiGeneration || _isRunning || history.Count == 0) return;
			PerformanceBenchmarkResult last = history[^1];
			_result = last;
			_exportBtn.IsEnabled = true;
			ApplyResultToUI(last);
			_statusText.Text = $"已恢复上次测试结果 · {last.TestTime:yyyy-MM-dd HH:mm}（点击「开始测试」可重新跑分）";
			if (last.Cpu.LatencyMatrix != null)
			{
				var matrix = last.Cpu.LatencyMatrix;
				string? png = await Task.Run(() => PerformanceBenchmarkService.GenerateLatencyHeatmap(matrix));
				if (generation != _uiGeneration || _isRunning) return;
				_latencyHeatmapPath = png;
				ShowLatencyHeatmap(png);
			}
		}
		catch { }
	}

	/// <summary>历史窗口点「载入到主页」：清空当前展示后回填所选历史记录。</summary>
	private async void LoadResultIntoMainPage(PerformanceBenchmarkResult r)
	{
		if (_isRunning) return;
		try
		{
			int generation = ++_uiGeneration;
			_result = r;
			ResetUI();
			ApplyResultToUI(r);
			_exportBtn.IsEnabled = true;
			_statusText.Text = $"已载入历史测试记录 · {r.TestTime:yyyy-MM-dd HH:mm}（点击「开始测试」可重新跑分）";
			if (r.Cpu.LatencyMatrix != null)
			{
				var matrix = r.Cpu.LatencyMatrix;
				string? png = await Task.Run(() => PerformanceBenchmarkService.GenerateLatencyHeatmap(matrix));
				if (generation != _uiGeneration) return;
				_latencyHeatmapPath = png;
				ShowLatencyHeatmap(png);
			}
		}
		catch { }
	}

	// The original workloads run in an isolated worker. The controller remains responsive.

	private FrameworkElement BuildWinTestControls()
	{
		Grid work = new() { ColumnSpacing = 12.0, RowSpacing = 8.0 };
		work.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
		work.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });

		_winIconList = new ItemsControl { MaxHeight = 200.0, IsTabStop = false };
		_winIconList.ItemsPanel = (ItemsPanelTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(
			"<ItemsPanelTemplate xmlns=\"http://schemas.microsoft.com/winfx/2006/xaml/presentation\">" +
			"<StackPanel Orientation=\"Horizontal\"/></ItemsPanelTemplate>");
		_winBigList = new ListView { MaxHeight = 160.0, SelectionMode = ListViewSelectionMode.None };
		_winTabView = new TabView { TabWidthMode = TabViewWidthMode.Equal, MaxHeight = 130.0, IsAddTabButtonVisible = false };
		for (int i = 0; i < 8; i++)
			_winTabView.TabItems.Add(new TabViewItem { Header = $"标签 {i + 1}", Content = new TextBlock { Text = $"第 {i + 1} 个标签页内容", Margin = new Thickness(8) } });
		_winScrollHost = new ScrollViewer { MaxHeight = 130.0, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollMode = ScrollMode.Disabled };
		var scrollInner = new StackPanel { Spacing = 4.0 };
		for (int i = 0; i < 400; i++)
			scrollInner.Children.Add(new TextBlock { Text = $"滚动行 {i + 1}", FontSize = 12.0 });
		_winScrollHost.Content = scrollInner;
		_winTreeView = new TreeView { MaxHeight = 130.0, SelectionMode = TreeViewSelectionMode.None };
		_winLongText = new TextBlock { FontSize = 12.0, TextWrapping = TextWrapping.Wrap, MaxHeight = 130.0, TextTrimming = TextTrimming.CharacterEllipsis };

		AddCard(0, "图片列表", _winIconList);
		AddCard(0, "列表", _winBigList);
		AddCard(1, "标签切换", _winTabView);
		AddCard(1, "滚动", _winScrollHost);
		AddCard(2, "树形", _winTreeView);
		AddCard(2, "长文本", _winLongText);

		void AddCard(int row, string title, UIElement content)
		{
			Brush cardBg = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"];
			Brush cardBorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"];
			var border = new Border
			{
				Background = cardBg,
				BorderBrush = cardBorderBrush,
				BorderThickness = new Thickness(1.0),
				CornerRadius = new CornerRadius(8.0),
				Padding = new Thickness(12.0, 8.0, 12.0, 8.0),
				Child = new StackPanel
				{
					Spacing = 4.0,
					Children =
					{
						new TextBlock { Text = title, FontSize = 12.0, Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] },
						content
					}
				}
			};
			int column = work.Children.Count % 2;
			work.Children.Add(border);
			Grid.SetRow(border, row);
			Grid.SetColumn(border, column);
			while (work.RowDefinitions.Count <= row)
				work.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		}

		return work;
	}

	private async Task<WinPerformanceRunResult> RunWinRoundAsync(CancellationToken ct, TextBlock statusText)
	{
		var run = new WinPerformanceRunResult();
		ct.ThrowIfCancellationRequested();

		run.ImageListMs = await TimeUiAsync(statusText, "渲染 10000 张图标", () => RunWinImageListAsync(ct), ct);
		run.ListLoadMs = await TimeUiAsync(statusText, "加载 20000 条列表", () => RunWinListLoadAsync(ct), ct);
		run.TabSwitchMs = await TimeUiAsync(statusText, "快速切换标签", () => RunWinTabSwitchAsync(ct), ct);
		run.ScrollMs = await TimeUiAsync(statusText, "滚动", () => RunWinScrollAsync(ct), ct);
		run.TreeExpandMs = await TimeUiAsync(statusText, "树形展开", () => RunWinTreeExpandAsync(ct), ct);
		run.SortFilterMs = await RunWinSortFilterAsync(ct);
		run.TextRenderMs = await TimeUiAsync(statusText, "长文本渲染", () => RunWinTextRenderAsync(ct), ct);
		return run;
	}

	/// <summary>在 UI 线程执行操作并测量耗时（毫秒），实时更新弹窗状态。</summary>
	private async Task<double> TimeUiAsync(TextBlock statusText, string label, Func<Task> action, CancellationToken ct)
	{
		var tcs = new TaskCompletionSource<double>(TaskCreationOptions.RunContinuationsAsynchronously);
		DispatcherQueue.TryEnqueue(() => _ = RunTimedAsync());
		double ms = await tcs.Task;
		ct.ThrowIfCancellationRequested();
		return ms;

		async Task RunTimedAsync()
		{
			await Task.Yield();
			statusText.Text = $"正在执行: {label}...";
			var sw = Stopwatch.StartNew();
			try
			{
				await action();
				sw.Stop();
				statusText.Text = $"{label} 完成，耗时 {sw.Elapsed.TotalMilliseconds:F0} ms";
				tcs.TrySetResult(sw.Elapsed.TotalMilliseconds);
			}
			catch (Exception ex)
			{
				sw.Stop();
				tcs.TrySetException(ex);
			}
		}
	}

	private Task RunWinImageListAsync(CancellationToken ct)
	{
		ct.ThrowIfCancellationRequested();
		var uris = _winIconPaths.Where(p => !string.IsNullOrEmpty(p)).Select(p => new Uri(p)).ToList();
		if (uris.Count == 0) return Task.CompletedTask;
		_winIconList.Items.Clear();
		foreach (var uri in uris)
		{
			ct.ThrowIfCancellationRequested();
			_winIconList.Items.Add(new Image
			{
				Width = 48.0,
				Height = 48.0,
				Stretch = Stretch.Uniform,
				Source = new BitmapImage(uri)
			});
		}
		return Task.CompletedTask;
	}

	private Task RunWinListLoadAsync(CancellationToken ct)
	{
		ct.ThrowIfCancellationRequested();
		_winBigList.Items.Clear();
		for (int i = 0; i < WinPerformanceService.ListLoadCount; i++)
		{
			ct.ThrowIfCancellationRequested();
			_winBigList.Items.Add($"工具项 {i + 1}");
		}
		return Task.CompletedTask;
	}

	private async Task RunWinTabSwitchAsync(CancellationToken ct)
	{
		int count = _winTabView.TabItems.Count;
		if (count == 0) return;
		for (int i = 0; i < count * 3; i++)
		{
			ct.ThrowIfCancellationRequested();
			_winTabView.SelectedIndex = i % count;
			await Task.Yield();
		}
	}

	private async Task RunWinScrollAsync(CancellationToken ct)
	{
		for (int i = 0; i < 3; i++)
		{
			ct.ThrowIfCancellationRequested();
			_winScrollHost.ChangeView(null, _winScrollHost.ScrollableHeight, null);
			await Task.Yield();
			_winScrollHost.ChangeView(null, 0.0, null);
			await Task.Yield();
		}
	}

	private async Task RunWinTreeExpandAsync(CancellationToken ct)
	{
		_winTreeView.RootNodes.Clear();
		var rand = new Random(7);
		int total = 0;
		for (int g = 0; g < 30 && total < WinPerformanceService.TreeExpandCount; g++)
		{
			var groupNode = new TreeViewNode { Content = $"分组 {g + 1}" };
			for (int c = 0; c < 100 && total < WinPerformanceService.TreeExpandCount; c++)
			{
				groupNode.Children.Add(new TreeViewNode { Content = $"项 {rand.Next(0, 100000)}" });
				total++;
			}
			_winTreeView.RootNodes.Add(groupNode);
		}
		foreach (var group in _winTreeView.RootNodes.ToList())
		{
			ct.ThrowIfCancellationRequested();
			group.IsExpanded = true;
			await Task.Yield();
		}
		foreach (var group in _winTreeView.RootNodes.ToList())
		{
			ct.ThrowIfCancellationRequested();
			group.IsExpanded = false;
			await Task.Yield();
		}
	}

	private async Task<double> RunWinSortFilterAsync(CancellationToken ct)
	{
		var sw = Stopwatch.StartNew();
		await Task.Run(() =>
		{
			var data = _winSortData;
			for (int i = 0; i < 6; i++)
			{
				ct.ThrowIfCancellationRequested();
				_ = data.OrderByDescending(kv => kv.Value).ToList();
				_ = data.Where(kv => kv.Value % 2 == 0).ToList();
			}
		});
		sw.Stop();
		return sw.Elapsed.TotalMilliseconds;
	}

	private Task RunWinTextRenderAsync(CancellationToken ct)
	{
		ct.ThrowIfCancellationRequested();
		var sb = new System.Text.StringBuilder(WinPerformanceService.LongTextChars + 64);
		for (int i = 0; i < WinPerformanceService.LongTextChars; i++)
		{
			ct.ThrowIfCancellationRequested();
			sb.Append((char)('A' + (i % 26)));
			if (i % 60 == 0) sb.Append(' ');
		}
		_winLongText.Text = sb.ToString();
		return Task.CompletedTask;
	}

	private static int GpuPriority(string name)
	{
		if (name.Contains("NVIDIA", StringComparison.OrdinalIgnoreCase)) return 0;
		if (name.Contains("Radeon(TM) Graphics", StringComparison.OrdinalIgnoreCase)) return 2;
		if (name.Contains("AMD", StringComparison.OrdinalIgnoreCase)) return 0;
		if (name.Contains("Arc", StringComparison.OrdinalIgnoreCase)) return 1;
		if (name.Contains("Intel", StringComparison.OrdinalIgnoreCase)) return 3;
		return 4;
	}

	private async Task<(int Index, string Name)?> ShowGpuSelectDialogAsync()
	{
		if (_availableGpus.Count == 0)
		{
			try
			{
				_availableGpus = await Task.Run(PerformanceBenchmarkService.GetFurMarkGpus);
			}
			catch { _availableGpus = []; }
			if (_availableGpus.Count == 0)
			{
				try
				{
					_availableGpus = LiteMonitorService.GetAvailableGpus()
						.Select(g => new FurMarkGpuInfo { Index = g.Index, Name = g.Name })
						.ToList();
				}
				catch { _availableGpus = []; }
			}
			if (_availableGpus.Count == 0)
				_availableGpus = [new FurMarkGpuInfo { Index = 0, Name = "GPU 0 (自动检测)" }];
		}

		int bestIdx = 0;
		int bestPri = int.MaxValue;
		for (int i = 0; i < _availableGpus.Count; i++)
		{
			int pri = GpuPriority(_availableGpus[i].Name);
			if (pri < bestPri) { bestPri = pri; bestIdx = i; }
		}

		var radios = new List<RadioButton>();
		var panel = new StackPanel { Spacing = 6.0 };
		for (int i = 0; i < _availableGpus.Count; i++)
		{
			var gpu = _availableGpus[i];
			var detail = new List<string>();
			if (gpu.Index > 0) detail.Add("GPU " + gpu.Index);
			if (!string.IsNullOrWhiteSpace(gpu.DeviceId)) detail.Add("deviceID: " + gpu.DeviceId);
			if (!string.IsNullOrWhiteSpace(gpu.Memory)) detail.Add(gpu.Memory);
			if (!string.IsNullOrWhiteSpace(gpu.Driver)) detail.Add("driver: " + gpu.Driver);
			var rb = new RadioButton
			{
				IsChecked = i == bestIdx,
				Content = new StackPanel
				{
					Spacing = 2.0,
					Children =
					{
						(UIElement)new TextBlock
						{
							Text = gpu.Name,
							FontSize = 14.0,
							TextWrapping = TextWrapping.Wrap
						},
						(UIElement)new TextBlock
						{
							Text = string.Join(" · ", detail),
							FontSize = 12.0,
							Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
							TextWrapping = TextWrapping.Wrap
						}
					}
				}
			};
			radios.Add(rb);
			panel.Children.Add(rb);
		}

		var dialog = new ContentDialog
		{
			Title = "选择要测试的GPU",
			Content = new ScrollViewer
			{
				Content = panel,
				MaxHeight = 360.0,
				VerticalScrollBarVisibility = ScrollBarVisibility.Auto
			},
			PrimaryButtonText = "开始测试",
			CloseButtonText = "取消",
			XamlRoot = XamlRoot,
			RequestedTheme = ActualTheme
		};
		var result = await dialog.ShowAsync();
		if (result != ContentDialogResult.Primary) return null;
		int selIdx = 0;
		for (int i = 0; i < radios.Count; i++)
		{
			if (radios[i].IsChecked == true) { selIdx = i; break; }
		}
		string name = _availableGpus[selIdx].Name;
		if (name == "GPU 0 (自动检测)") name = "";
		return (_availableGpus[selIdx].Index, name);
	}

	private void OnStopClick(object sender, RoutedEventArgs e)
	{
		PerformanceBenchmarkService.Cancel();
		_cts.Cancel();
		_stopBtn.IsEnabled = false;
		_statusText.Text = "正在停止测试并释放资源…";
	}

	private async Task<(string csv, string stderr)> ShowCoreToCoreLatencyDialog(string exePath, CancellationToken ct = default)
	{
		bool isC2C = string.Equals(Path.GetFileName(exePath), "C2CLatency.exe", StringComparison.OrdinalIgnoreCase);
		using var local = CancellationTokenSource.CreateLinkedTokenSource(ct);
		local.CancelAfter(TimeSpan.FromMinutes(30));
		var token = local.Token;
		var detail = Label("正在启动测试进程…", 13, true);
		var log = Label("", 11, true); log.IsTextSelectionEnabled = true;
		var ring = new ProgressRing { Width = 36, Height = 36, IsActive = true, HorizontalAlignment = HorizontalAlignment.Left };
		var dialog = new ContentDialog
		{
			Title = "测量核间延迟", CloseButtonText = "停止测试", XamlRoot = XamlRoot,
			RequestedTheme = ActualTheme,
			Content = new StackPanel { Spacing = 16, Children =
			{
				ring, Label("正在测量处理器核心之间的通信延迟。", 16), detail,
				Label("请保持窗口打开。关闭后会取消测试，不生成分数或热力图。", 12, true),
				new Expander { Header = "查看工具输出", HorizontalAlignment = HorizontalAlignment.Stretch,
					Content = new ScrollViewer { Content = log, MaxHeight = 220 } }
			} }
		};
		bool finished = false;
		bool userCancelled = false;
		dialog.Closing += (_, _) => { if (!finished && !local.IsCancellationRequested) { userCancelled = true; local.Cancel(); } };
		var showTask = dialog.ShowAsync().AsTask();
		using var closeOnCancel = token.Register(() => DispatcherQueue.TryEnqueue(() => dialog.Hide()));
		try
		{
			using var process = Process.Start(new ProcessStartInfo
			{
				FileName = exePath, Arguments = isC2C ? "" : "--csv", UseShellExecute = false,
				RedirectStandardOutput = true, RedirectStandardError = true, RedirectStandardInput = isC2C,
				CreateNoWindow = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8
			}) ?? throw new InvalidOperationException("核间延迟工具启动失败。");
			using var killOnCancel = token.Register(() => { try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { } });
			if (isC2C) { await process.StandardInput.WriteLineAsync(); await process.StandardInput.FlushAsync(); }
			var outputTask = process.StandardOutput.ReadToEndAsync(token);
			var errorTask = ReadErrorsAsync();
			await process.WaitForExitAsync(token);
			string output = await outputTask;
			string errors = await errorTask;
			token.ThrowIfCancellationRequested();
			if (process.ExitCode != 0 || (isC2C && !errors.Contains("Testing completed successfully", StringComparison.Ordinal)))
				throw new InvalidDataException("核间延迟工具提前关闭或未正常完成，未采用本次数据。");
			string csv = isC2C ? PerformanceBenchmarkService.ConvertC2CLatencyTextToCsv(errors, Math.Min(Environment.ProcessorCount, 64)) : output;
			BenchmarkResultValidation.Latency(PerformanceBenchmarkService.ParseCoreToCoreCsv(csv, Math.Min(Environment.ProcessorCount, 64)));
			return (csv, errors);

			async Task<string> ReadErrorsAsync()
			{
				var builder = new StringBuilder();
				int pairs = 0;
				while (await process.StandardError.ReadLineAsync(token) is string line)
				{
					builder.AppendLine(line);
					if (line.Contains(" to ")) pairs++;
					if (isC2C && line.Contains("Testing completed successfully", StringComparison.Ordinal))
					{
						await process.StandardInput.WriteLineAsync(); await process.StandardInput.FlushAsync();
					}
					// Keep raw output secondary; do not animate a fabricated completion percentage.
					detail.Text = pairs > 0 ? $"已取得 {pairs} 组核心通信样本" : "工具正在初始化与测量…";
					log.Text = builder.Length > 10000 ? builder.ToString(builder.Length - 10000, 10000) : builder.ToString();
				}
				return builder.ToString();
			}
		}
		catch (OperationCanceledException) when (!ct.IsCancellationRequested && !userCancelled)
		{
			throw new TimeoutException("核间延迟测量超时，未采用本次结果。");
		}
		finally
		{
			finished = true; ring.IsActive = false;
			dialog.Hide();
			try { await showTask; } catch { }
		}
	}

	private async Task RunBrowserTestsAsync(PerformanceBenchmarkResult result, int gpuSec, CancellationToken ct)
	{
		using var local = CancellationTokenSource.CreateLinkedTokenSource(ct);
		local.CancelAfter(TimeSpan.FromMinutes(10));
		var webView = new WebView2 { Width = 900, Height = 600 };
		var status = Label("正在加载浏览器测试…", 13, true);
		var dialog = new ContentDialog
		{
			Title = "浏览器性能测试", CloseButtonText = "停止测试", XamlRoot = XamlRoot,
			RequestedTheme = ActualTheme,
			Content = new StackPanel { Spacing = 12, Children = { status,
				new ScrollViewer { Content = webView, MaxWidth = 900, MaxHeight = 600,
					HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto } } }
		};
		var tcs = new TaskCompletionSource<BrowserBenchmarkResult>(TaskCreationOptions.RunContinuationsAsynchronously);
		bool finished = false;
		dialog.Closing += (_, _) => { if (!finished) local.Cancel(); };
		var showTask = dialog.ShowAsync().AsTask();
		using var cancel = local.Token.Register(() => tcs.TrySetCanceled(local.Token));
		try
		{
			await webView.EnsureCoreWebView2Async(await WebView2EnvironmentService.GetAsync()).AsTask().WaitAsync(local.Token);
			webView.CoreWebView2.NavigationCompleted += (_, args) =>
			{
				if (!args.IsSuccess) tcs.TrySetException(new InvalidOperationException("浏览器测试加载失败：" + args.WebErrorStatus));
			};
			webView.CoreWebView2.ProcessFailed += (_, _) => tcs.TrySetException(new InvalidOperationException("浏览器测试进程异常退出。"));
			webView.CoreWebView2.WebMessageReceived += (_, args) =>
			{
				try
				{
					using var doc = JsonDocument.Parse(args.TryGetWebMessageAsString());
					var root = doc.RootElement;
					if (root.TryGetProperty("error", out var error)) throw new InvalidDataException("浏览器测试失败：" + error.GetString());
					if (!root.TryGetProperty("completed", out var done) || done.ValueKind != JsonValueKind.True)
						throw new InvalidDataException("浏览器测试未完整完成，未采用本次结果。");
					int Score(string key) { int score = root.GetProperty(key).GetInt32(); if (score < 0) throw new InvalidDataException("浏览器分数无效。"); return score; }
					string Detail(string key) => root.GetProperty(key).GetString() ?? "";
					var br = new BrowserBenchmarkResult
					{
						JsScore = Score("jsScore"), JsDetail = Detail("jsDetail"), DomScore = Score("domScore"), DomDetail = Detail("domDetail"),
						CardScore = Score("cardScore"), CardDetail = Detail("cardDetail"), CssScore = Score("cssScore"), CssDetail = Detail("cssDetail"),
						LayoutScore = Score("layoutScore"), LayoutDetail = Detail("layoutDetail"), EventScore = Score("eventScore"), EventDetail = Detail("eventDetail"),
						TotalScore = Score("totalScore")
					};
					br.Grade = PerformanceBenchmarkService.ComputeGrade(br.TotalScore);
					tcs.TrySetResult(br);
				}
				catch (Exception ex) { tcs.TrySetException(ex); }
			};
			status.Text = "依次测量脚本、页面渲染、动画与交互。关闭窗口会取消本项测试。";
			webView.CoreWebView2.Navigate(new Uri(Path.Combine(AppContext.BaseDirectory, "Assets", "Benchmark", "browser-benchmark.html")).AbsoluteUri);
			var br = await tcs.Task;
			local.Token.ThrowIfCancellationRequested();
			result.Browser = br;
			UpdateDetailRow(_brJsScoreText, _brJsDetailText, br.JsScore, br.JsDetail);
			UpdateDetailRow(_brDomScoreText, _brDomDetailText, br.DomScore, br.DomDetail);
			UpdateDetailRow(_brCardScoreText, _brCardDetailText, br.CardScore, br.CardDetail);
			UpdateDetailRow(_brCssScoreText, _brCssDetailText, br.CssScore, br.CssDetail);
			UpdateDetailRow(_brLayoutScoreText, _brLayoutDetailText, br.LayoutScore, br.LayoutDetail);
			UpdateDetailRow(_brEventScoreText, _brEventDetailText, br.EventScore, br.EventDetail);
		}
		finally
		{
			finished = true; dialog.Hide();
			try { await showTask; } catch { }
			webView.Close();
		}
	}

	private void UpdateCpuUI(PerformanceBenchmarkResult r)
	{
		UpdateScoreRow(_cpuSingleScoreText, r.Cpu.SingleCoreScore);
		UpdateScoreRow(_cpuMultiScoreText, r.Cpu.MultiCoreScore);
		if (r.Cpu.LatencyMatrix != null) UpdateScoreRow(_cpuLatencyScoreText, r.Cpu.LatencyScore);
		else _cpuLatencyScoreText.Text = "未测量";
	}

	private void UpdateMemoryUI(PerformanceBenchmarkResult r)
	{
		_memCapacityText.Text = $"{r.Memory.TotalCapacityGB:F0} GB";
	}

	private void UpdateGpuUI(PerformanceBenchmarkResult r)
	{
		_gpuNameText.Text = !string.IsNullOrEmpty(r.Gpu.GpuName) ? r.Gpu.GpuName : r.GpuName;
		UpdateScoreRow(_gpuRenderScoreText, r.Gpu.RenderScore);
		UpdateDetailRow(_gpuFurMarkScoreText, _gpuAvgFpsText, r.Gpu.FurMarkScore, $"平均 {r.Gpu.AvgFps:F0} FPS");
		UpdateDetailRow(_gpuMinFpsText, _gpuMaxFpsText, (int)r.Gpu.MinFps, $"最低 {r.Gpu.MinFps:F0} / 最高 {r.Gpu.MaxFps:F0}");
	}

	private void UpdateDiskUI(PerformanceBenchmarkResult r)
	{
		UpdateDetailRow(_diskSeqReadScoreText, _diskSeqReadDetailText, r.Disk.SeqReadScore, $"{r.Disk.SeqReadMBs:F0} MB/s");
		UpdateDetailRow(_diskSeqWriteScoreText, _diskSeqWriteDetailText, r.Disk.SeqWriteScore, $"{r.Disk.SeqWriteMBs:F0} MB/s");
		UpdateDetailRow(_disk4KReadScoreText, _disk4KReadDetailText, r.Disk.Random4KReadScore, $"{r.Disk.Random4KReadIops / 1000.0:F0}K IOPS");
		UpdateDetailRow(_disk4KWriteScoreText, _disk4KWriteDetailText, r.Disk.Random4KWriteScore, $"{r.Disk.Random4KWriteIops / 1000.0:F0}K IOPS");
		_diskTempText.Text = r.Disk.Temperature > 0f ? $"{r.Disk.Temperature:F0}℃" : "N/A";
	}

	private void UpdateDetailRow(TextBlock scoreText, TextBlock detailText, int score, string detail)
	{
		scoreText.Text = score.ToString();
		scoreText.Foreground = new SolidColorBrush(ScoreColor(score));
		detailText.Text = detail;
	}

	private void UpdateTopCard(TextBlock scoreText, TextBlock gradeText, ProgressBar bar, int score, string grade)
	{
		scoreText.Text = score.ToString();
		scoreText.Foreground = new SolidColorBrush(GradeColor(grade));
		gradeText.Text = grade;
		gradeText.Foreground = new SolidColorBrush(GradeColor(grade));
		bar.Maximum = Math.Max(score, 100);
		bar.Value = score;
	}

	private void UpdateScoreRow(TextBlock scoreText, int score, string detail = "")
	{
		scoreText.Text = score.ToString();
		scoreText.Foreground = new SolidColorBrush(ScoreColor(score));
	}

	private void ShowLatencyHeatmap(string? imagePath)
	{
		if (string.IsNullOrEmpty(imagePath) || !File.Exists(imagePath)) return;
		try
		{
			BitmapImage source = new(new Uri(imagePath));
			_latencyHeatmapImage.Source = source;
			_latencyHeatmapImage.Visibility = Visibility.Visible;
			_latencyGridContainer.Visibility = Visibility.Visible;
		}
		catch { }
	}

	private static Color GradeColor(string grade) => grade switch
	{
		"S" => ColorS,
		"A+" => ColorAPlus,
		"A" => ColorA,
		"B+" => ColorBPlus,
		"B" => ColorB,
		"C" => ColorC,
		_ => ColorD
	};

	private static Color ScoreColor(int score)
	{
		if (score >= 75) return score >= 130 ? ColorS : score >= 100 ? ColorAPlus : ColorA;
		if (score >= 40) return score >= 55 ? ColorBPlus : ColorB;
		return score >= 20 ? ColorC : ColorD;
	}

	private void ResetUI()
	{
		_gamingScoreText.Text = "—";
		_gamingGradeText.Text = "待完成测试";
		_gamingBar.Value = 0.0;
		_officeScoreText.Text = "—";
		_officeGradeText.Text = "待完成测试";
		_officeBar.Value = 0.0;
		_winScoreText.Text = "—";
		_winGradeText.Text = "待完成测试";
		_winBar.Value = 0.0;
		_winListLoadText.Text = "—";
		_winImageListText.Text = "—";
		_winTabSwitchText.Text = "—";
		_winScrollText.Text = "—";
		_winTreeExpandText.Text = "—";
		_winSortFilterText.Text = "—";
		_winTextRenderText.Text = "—";
		_winTotalText.Text = "—";
		_winRuns.Clear();
		_cpuSingleScoreText.Text = "—";
		_cpuMultiScoreText.Text = "—";
		_cpuLatencyScoreText.Text = "—";
		_latencyGridContainer.Visibility = Visibility.Collapsed;
		_latencyHeatmapImage.Source = null;
		_latencyHeatmapImage.Visibility = Visibility.Collapsed;
		_latencyHeatmapPath = null;
		_gpuNameText.Text = "";
		_gpuRenderScoreText.Text = "—";
		_gpuFurMarkScoreText.Text = "—";
		_gpuAvgFpsText.Text = "";
		_gpuMinFpsText.Text = "—";
		_gpuMaxFpsText.Text = "";
		_memCapacityText.Text = "—";
		_diskSeqReadScoreText.Text = "—";
		_diskSeqReadDetailText.Text = "";
		_diskSeqWriteScoreText.Text = "—";
		_diskSeqWriteDetailText.Text = "";
		_disk4KReadScoreText.Text = "—";
		_disk4KReadDetailText.Text = "";
		_disk4KWriteScoreText.Text = "—";
		_disk4KWriteDetailText.Text = "";
		_diskTempText.Text = "—";
		_brJsScoreText.Text = "—";
		_brJsDetailText.Text = "";
		_brDomScoreText.Text = "—";
		_brDomDetailText.Text = "";
		_brCardScoreText.Text = "—";
		_brCardDetailText.Text = "";
		_brCssScoreText.Text = "—";
		_brCssDetailText.Text = "";
		_brLayoutScoreText.Text = "—";
		_brLayoutDetailText.Text = "";
		_brEventScoreText.Text = "—";
		_brEventDetailText.Text = "";
		_globalProgress.Value = 0.0;
	}

	private async void OnExportClick(object sender, RoutedEventArgs e)
	{
		if (_result == null || _isRunning) return;
		try
		{
			_statusText.Text = "正在准备报告...";
			Window obj = new() { Title = "性能测试报告" };
			AppWindow appWindow = obj.AppWindow;
			appWindow.Resize(new SizeInt32(900, 900));
			appWindow.Move(new PointInt32(100, 50));
			WebView2 pdfWv = new();
			Button button = new()
			{
				Content = "打印/导出PDF",
				HorizontalAlignment = HorizontalAlignment.Right,
				Margin = new Thickness(0.0, 8.0, 16.0, 8.0)
			};
			TextBlock statusLabel = new()
			{
				Text = "报告加载中...",
				FontSize = 12.0,
				Margin = new Thickness(16.0, 0.0, 0.0, 8.0),
				Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
				VerticalAlignment = VerticalAlignment.Center
			};
			Grid grid = new() { Height = 44.0 };
			grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
			grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
			grid.Children.Add(statusLabel);
			Grid.SetColumn(statusLabel, 0);
			grid.Children.Add(button);
			Grid.SetColumn(button, 1);
			Grid grid2 = new()
			{
				RowDefinitions =
				{
					new RowDefinition { Height = GridLength.Auto },
					new RowDefinition { Height = new GridLength(1.0, GridUnitType.Star) }
				},
				Children = { (UIElement)grid }
			};
			Grid.SetRow(grid, 0);
			grid2.Children.Add(pdfWv);
			Grid.SetRow(pdfWv, 1);
			obj.Content = grid2;
			button.Click += async (_, _) =>
			{
				statusLabel.Text = "请在打印对话框中选择\"另存为 PDF\"来导出";
				await pdfWv.CoreWebView2.ExecuteScriptAsync("window.print();");
			};
			obj.Activate();
			await pdfWv.EnsureCoreWebView2Async(await WebView2EnvironmentService.GetAsync());
			string folderPath = Path.Combine(AppContext.BaseDirectory, "Assets", "Benchmark");
			pdfWv.CoreWebView2.SetVirtualHostNameToFolderMapping("bench.local", folderPath, CoreWebView2HostResourceAccessKind.Allow);
			var reportReady = new TaskCompletionSource<bool>();
			pdfWv.CoreWebView2.WebMessageReceived += (_, args) =>
			{
				try
				{
					if (args.TryGetWebMessageAsString().Contains("report_ready"))
						reportReady.TrySetResult(true);
				}
				catch { }
			};
			pdfWv.CoreWebView2.NavigationCompleted += async (_, args) =>
			{
				if (!args.IsSuccess)
				{
					reportReady.TrySetException(new Exception("导航失败"));
				}
				else
				{
					await Task.Delay(300);
					await pdfWv.CoreWebView2.ExecuteScriptAsync("window.REPORT_DATA=" + PerformanceBenchmarkService.BuildReportJson(_result, _latencyHeatmapPath) + ";window.renderReport();");
				}
			};
			pdfWv.CoreWebView2.Navigate("https://bench.local/generate-report.html");
			await Task.WhenAny(reportReady.Task, Task.Delay(15000));
			statusLabel.Text = "报告已就绪，点击右上角按钮导出PDF";
			_statusText.Text = "报告窗口已打开";
		}
		catch (Exception ex)
		{
			_statusText.Text = "导出失败: " + ex.Message;
		}
	}

	private async void OnHistoryClick(object sender, RoutedEventArgs e)
	{
		// 防止重复打开多个历史窗口
		if (_historyInProgress) return;
		_historyInProgress = true;
		try
		{
			List<PerformanceBenchmarkResult> list = await Task.Run(PerformanceBenchmarkService.LoadHistory);
			if (list.Count == 0)
			{
				_historyInProgress = false;
				await new ContentDialog
				{
					Title = "历史对比",
					Content = new TextBlock { Text = "暂无历史测试记录，先完成一次性能测试吧。", Margin = new Thickness(16.0) },
					CloseButtonText = "关闭",
					XamlRoot = XamlRoot,
					RequestedTheme = ActualTheme
				}.ShowAsync();
				return;
			}
			BenchmarkHistoryWindow window = new(list, LoadResultIntoMainPage);
			window.Closed += (_, _) => _historyInProgress = false;
			window.Activate();
		}
		catch
		{
			_historyInProgress = false;
		}
	}

	private void OnRankingClick(object sender, RoutedEventArgs e)
	{
		var tool = new BenchmarkCloudTool();
		var context = new BuiltinToolContext { XamlRoot = XamlRoot };
		MainWindow.ActiveToolName = tool.Name;
		tool.ExecuteAsync(context);
	}

	private async void OnLatencyOnlyClick(object sender, RoutedEventArgs e)
	{
		if (_isRunning || PerformanceBenchmarkService.IsRunning) return;
		string? coreToCoreExe = PerformanceBenchmarkService.FindCoreToCoreLatencyExe();
		if (coreToCoreExe == null)
		{
			await new ContentDialog
			{
				Title = "未找到测试工具",
				Content = "未找到核间延迟测试程序（C2CLatency.exe），请确认 Tools/处理器工具/C2CLatency 目录完整。",
				CloseButtonText = "确定",
				XamlRoot = XamlRoot,
				RequestedTheme = ActualTheme
			}.ShowAsync();
			return;
		}
		if (!PerformanceBenchmarkService.TryBeginSession()) return;
		_uiGeneration++;
		_cts.Dispose(); _cts = new CancellationTokenSource();
		_result = null; _exportBtn.IsEnabled = false;
		SetRunning(true);
		_phaseTitle.Text = "正在测试 · 核间延迟";
		try
		{
			_statusText.Text = "正在运行核间延迟测试...";
			var (csv, _) = await ShowCoreToCoreLatencyDialog(coreToCoreExe, _cts.Token);
			if (string.IsNullOrEmpty(csv))
			{
				_statusText.Text = "核间延迟测试未完成";
				return;
			}
			int maxCores = Math.Min(Environment.ProcessorCount, 64);
			var matrix = PerformanceBenchmarkService.ParseCoreToCoreCsv(csv, maxCores);
			BenchmarkResultValidation.Latency(matrix);
			string? heatmapPath = PerformanceBenchmarkService.GenerateLatencyHeatmap(matrix);
			_latencyHeatmapPath = heatmapPath;
			DispatcherQueue.TryEnqueue(() =>
			{
				ShowLatencyHeatmap(_latencyHeatmapPath);
				UpdateScoreRow(_cpuLatencyScoreText, PerformanceBenchmarkService.NormalizeLatency(matrix.AverageNs));
			});
			var ask = new ContentDialog
			{
				Title = "核间延迟测试完成",
				Content = "测试完成！是否将核间延迟热力图上传到社区，供其他用户查看对比？",
				PrimaryButtonText = "上传",
				CloseButtonText = "不传",
				XamlRoot = XamlRoot,
				RequestedTheme = ActualTheme
			};
			if (await ask.ShowAsync() != ContentDialogResult.Primary)
			{
				_statusText.Text = "核间延迟测试完成，热力图已生成（未上传）";
				return;
			}
			await Task.Yield();
			if (!GitHubAuthService.IsLoggedIn)
			{
				try { await GitHubAuthService.EnsureAuthenticatedAsync(XamlRoot, CancellationToken.None); }
				catch { _statusText.Text = "未登录，取消上传"; return; }
			}
			var hardware = new PerformanceBenchmarkResult();
			await PerformanceBenchmarkService.PopulateHardwareInfoAsync(hardware);
			ContentDialog progressDialog = new()
			{
				Title = "正在上传",
				Content = new ProgressBar { IsIndeterminate = true },
				XamlRoot = XamlRoot,
				RequestedTheme = ActualTheme
			};
			var progressTask = progressDialog.ShowAsync().AsTask();
			try
			{
				var progress = new Progress<string>(message => DispatcherQueue.TryEnqueue(() =>
					progressDialog.Content = new TextBlock { Text = message }));
				string prUrl = await BenchmarkCloudService.UploadLatencyImageOnlyAsync(hardware.CpuName, heatmapPath!, progress, CancellationToken.None);
				progressDialog.Hide();
				try { await progressTask; } catch { }
				_statusText.Text = "核间延迟热力图上传成功";
				if (await new ContentDialog
				{
					Title = "上传成功",
					Content = "核间延迟热力图已通过 PR 提交，合并后可在核间延迟查询中查看。\n\nPR 链接：" + prUrl,
					PrimaryButtonText = "打开 PR",
					CloseButtonText = "关闭",
					XamlRoot = XamlRoot,
					RequestedTheme = ActualTheme
				}.ShowAsync() == ContentDialogResult.Primary)
					await Launcher.LaunchUriAsync(new Uri(prUrl));
			}
			catch (Exception ex)
			{
				progressDialog.Hide();
				try { await progressTask; } catch { }
				await new ContentDialog
				{
					Title = "上传失败",
					Content = ex.Message,
					CloseButtonText = "确定",
					XamlRoot = XamlRoot,
					RequestedTheme = ActualTheme
				}.ShowAsync();
			}
		}
		catch (OperationCanceledException) { _statusText.Text = "核间延迟测试已取消，未生成分数。"; }
		catch (Exception ex)
		{
			_statusText.Text = "核间延迟测试出错: " + ex.Message;
		}
		finally
		{
			SetRunning(false);
			PerformanceBenchmarkService.EndSession();
			_phaseTitle.Text = "核间延迟测试已结束";
		}
	}
}

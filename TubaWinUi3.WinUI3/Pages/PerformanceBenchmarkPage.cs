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
	private bool _uploadInProgress;
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
	private Button _uploadBtn = null!;
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

	private static readonly Color AccentBlue = Color.FromArgb(byte.MaxValue, 0, 99, 177);
	private static readonly Color ColorS = Color.FromArgb(byte.MaxValue, 74, 222, 128);
	private static readonly Color ColorAPlus = Color.FromArgb(byte.MaxValue, 34, 197, 94);
	private static readonly Color ColorA = Color.FromArgb(byte.MaxValue, 0, 99, 177);
	private static readonly Color ColorBPlus = Color.FromArgb(byte.MaxValue, 251, 191, 36);
	private static readonly Color ColorB = Color.FromArgb(byte.MaxValue, 251, 146, 60);
	private static readonly Color ColorC = Color.FromArgb(byte.MaxValue, 248, 113, 113);
	private static readonly Color ColorD = Color.FromArgb(byte.MaxValue, 220, 38, 38);

	public PerformanceBenchmarkPage()
	{
		_cts = new CancellationTokenSource();
		base.Content = BuildUI();
		// 导航返回 / 重启应用后，自动回填上一次测试的完整结果，避免重新跑分
		_ = RestoreLastResultAsync();
	}

	private ScrollViewer BuildUI()
	{
		Brush cardBg = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"];
		Brush cardBorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"];
		Grid grid = new()
		{
			RowSpacing = 0.0,
			Padding = new Thickness(28.0, 48.0, 28.0, 0.0)
		};
		grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.0, GridUnitType.Star) });
		grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		Grid grid2 = BuildTopCards();
		grid.Children.Add(grid2);
		Grid.SetRow(grid2, 0);
		Grid grid3 = new()
		{
			ColumnSpacing = 12.0,
			RowSpacing = 12.0,
			Padding = new Thickness(0.0, 12.0, 0.0, 12.0)
		};
		grid3.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
		grid3.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) });
		grid3.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.0, GridUnitType.Star) });
		grid3.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.0, GridUnitType.Star) });
		grid3.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.0, GridUnitType.Star) });
		grid3.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.0, GridUnitType.Star) });
		Border border = BuildSection("CPU 性能", "\ueea1", BuildCpuContent());
		grid3.Children.Add(border);
		Grid.SetRow(border, 0);
		Grid.SetColumn(border, 0);
		Border border2 = BuildSection("GPU 性能", "\ue950", BuildGpuContent());
		grid3.Children.Add(border2);
		Grid.SetRow(border2, 0);
		Grid.SetColumn(border2, 1);
		Border border3 = BuildSection("内存性能", "\ue90f", BuildMemoryContent());
		grid3.Children.Add(border3);
		Grid.SetRow(border3, 1);
		Grid.SetColumn(border3, 0);
		Border border4 = BuildSection("硬盘性能", "\ueda2", BuildDiskContent());
		grid3.Children.Add(border4);
		Grid.SetRow(border4, 1);
		Grid.SetColumn(border4, 1);
		Border border5 = BuildSection("浏览器流畅度", "\ue774", BuildBrowserContent());
		grid3.Children.Add(border5);
		Grid.SetRow(border5, 2);
		Grid.SetColumn(border5, 0);
		Grid.SetColumnSpan(border5, 2);
		Border border6 = BuildSection("WinUI 性能", "\ue80f", BuildWinContent());
		grid3.Children.Add(border6);
		Grid.SetRow(border6, 3);
		Grid.SetColumn(border6, 0);
		Grid.SetColumnSpan(border6, 2);
		ScrollViewer scrollViewer = new()
		{
			Content = grid3,
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
			HorizontalScrollMode = ScrollMode.Disabled
		};
		grid.Children.Add(scrollViewer);
		Grid.SetRow(scrollViewer, 1);
		StackPanel stackPanel = BuildControlBar();
		grid.Children.Add(stackPanel);
		Grid.SetRow(stackPanel, 2);
		return new ScrollViewer
		{
			Content = grid,
			VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
			HorizontalScrollMode = ScrollMode.Disabled,
			Transitions = new TransitionCollection
			{
				new EntranceThemeTransition { FromVerticalOffset = 16 }
			}
		};
	}

	private Grid BuildTopCards()
	{
		Grid obj = new()
		{
			ColumnSpacing = 12.0,
			ColumnDefinitions =
			{
				new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) },
				new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) },
				new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) }
			}
		};
		Border border = new()
		{
			Background = cardBg,
			BorderBrush = cardBorderBrush,
			BorderThickness = new Thickness(1.0),
			CornerRadius = new CornerRadius(8.0),
			Padding = new Thickness(20.0, 16.0, 20.0, 16.0),
			Child = BuildScoreCard("游戏性能", out _gamingScoreText, out _gamingGradeText, out _gamingBar)
		};
		obj.Children.Add(border);
		Grid.SetColumn(border, 0);
		Border border2 = new()
		{
			Background = cardBg,
			BorderBrush = cardBorderBrush,
			BorderThickness = new Thickness(1.0),
			CornerRadius = new CornerRadius(8.0),
			Padding = new Thickness(20.0, 16.0, 20.0, 16.0),
			Child = BuildScoreCard("办公性能", out _officeScoreText, out _officeGradeText, out _officeBar)
		};
		obj.Children.Add(border2);
		Grid.SetColumn(border2, 1);
		Border border3 = new()
		{
			Background = cardBg,
			BorderBrush = cardBorderBrush,
			BorderThickness = new Thickness(1.0),
			CornerRadius = new CornerRadius(8.0),
			Padding = new Thickness(20.0, 16.0, 20.0, 16.0),
			Child = BuildScoreCard("Win性能", out _winScoreText, out _winGradeText, out _winBar)
		};
		obj.Children.Add(border3);
		Grid.SetColumn(border3, 2);
		return obj;
	}

	private StackPanel BuildScoreCard(string label, out TextBlock scoreText, out TextBlock gradeText, out ProgressBar bar)
	{
		TextBlock item = new()
		{
			Text = label,
			FontSize = 13.0,
			Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
		};
		scoreText = new TextBlock
		{
			Text = "—",
			FontSize = 36.0,
			FontWeight = FontWeights.Bold,
			Foreground = new SolidColorBrush(ThemeColors.DimText)
		};
		gradeText = new TextBlock
		{
			Text = "",
			FontSize = 16.0,
			FontWeight = FontWeights.Bold,
			Foreground = new SolidColorBrush(ThemeColors.DimText),
			VerticalAlignment = VerticalAlignment.Center,
			Margin = new Thickness(8.0, 0.0, 0.0, 0.0)
		};
		bar = new ProgressBar
		{
			Value = 0.0,
			Maximum = 100.0,
			Height = 6.0
		};
		StackPanel stackPanel = new()
		{
			Orientation = Orientation.Horizontal,
			Spacing = 4.0
		};
		stackPanel.Children.Add(scoreText);
		stackPanel.Children.Add(gradeText);
		return new StackPanel
		{
			Spacing = 6.0,
			Children =
			{
				(UIElement)item,
				(UIElement)stackPanel,
				(UIElement)bar
			}
		};
	}

	private Border BuildSection(string title, string glyph, Panel content)
	{
		StackPanel stackPanel = new()
		{
			Orientation = Orientation.Horizontal,
			Spacing = 8.0
		};
		stackPanel.Children.Add(new FontIcon
		{
			Glyph = glyph,
			FontSize = 16.0,
			Foreground = new SolidColorBrush(AccentBlue)
		});
		stackPanel.Children.Add(new TextBlock
		{
			Text = title,
			FontSize = 15.0,
			FontWeight = FontWeights.Bold
		});
		StackPanel stackPanel2 = new() { Spacing = 8.0 };
		stackPanel2.Children.Add(stackPanel);
		stackPanel2.Children.Add(content);
		return new Border
		{
			Background = cardBg,
			BorderBrush = cardBorderBrush,
			BorderThickness = new Thickness(1.0),
			CornerRadius = new CornerRadius(8.0),
			Padding = new Thickness(16.0, 12.0, 16.0, 12.0),
			Child = stackPanel2
		};
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
				new ColumnDefinition { Width = GridLength.Auto }
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
			FontSize = 13.0,
			FontWeight = FontWeights.Bold,
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
				(UIElement)BuildScoreRow("多核", out _cpuMultiScoreText),
				(UIElement)BuildScoreRow("核间延迟", out _cpuLatencyScoreText)
			}
		};
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
			CornerRadius = new CornerRadius(6.0),
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
			FontSize = 11.0,
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
				new ColumnDefinition { Width = GridLength.Auto }
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
			FontSize = 13.0,
			FontWeight = FontWeights.Bold,
			VerticalAlignment = VerticalAlignment.Center,
			Foreground = new SolidColorBrush(ThemeColors.DimText)
		};
		obj.Children.Add(scoreText);
		Grid.SetColumn(scoreText, 1);
		detailText = new TextBlock
		{
			Text = "",
			FontSize = 11.0,
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
					FontSize = 11.0,
					TextWrapping = TextWrapping.Wrap,
					Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"]
				}
			}
		};
	}

	private StackPanel BuildControlBar()
	{
		_startBtn = new Button
		{
			Content = new StackPanel
			{
				Orientation = Orientation.Horizontal,
				Spacing = 6.0,
				Children =
				{
					(UIElement)new FontIcon { Glyph = "\ue768", FontSize = 14.0 },
					(UIElement)new TextBlock { Text = "开始测试", FontSize = 13.0 }
				}
			},
			CornerRadius = new CornerRadius(6.0),
			Padding = new Thickness(16.0, 8.0, 16.0, 8.0)
		};
		_startBtn.Click += OnStartClick;
		_stopBtn = new Button
		{
			Content = new StackPanel
			{
				Orientation = Orientation.Horizontal,
				Spacing = 6.0,
				Children =
				{
					(UIElement)new FontIcon { Glyph = "\ue71a", FontSize = 14.0 },
					(UIElement)new TextBlock { Text = "停止", FontSize = 13.0 }
				}
			},
			CornerRadius = new CornerRadius(6.0),
			Padding = new Thickness(12.0, 8.0, 12.0, 8.0),
			IsEnabled = false
		};
		_stopBtn.Click += OnStopClick;
		_exportBtn = new Button
		{
			Content = new StackPanel
			{
				Orientation = Orientation.Horizontal,
				Spacing = 6.0,
				Children =
				{
					(UIElement)new FontIcon { Glyph = "\uede1", FontSize = 14.0 },
					(UIElement)new TextBlock { Text = "导出 PDF", FontSize = 13.0 }
				}
			},
			CornerRadius = new CornerRadius(6.0),
			Padding = new Thickness(12.0, 8.0, 12.0, 8.0),
			IsEnabled = false
		};
		_exportBtn.Click += OnExportClick;
		_historyBtn = new Button
		{
			Content = new StackPanel
			{
				Orientation = Orientation.Horizontal,
				Spacing = 6.0,
				Children =
				{
					(UIElement)new FontIcon { Glyph = "\ue81c", FontSize = 14.0 },
					(UIElement)new TextBlock { Text = "历史对比", FontSize = 13.0 }
				}
			},
			CornerRadius = new CornerRadius(6.0),
			Padding = new Thickness(12.0, 8.0, 12.0, 8.0)
		};
		_historyBtn.Click += OnHistoryClick;
		_uploadBtn = new Button
		{
			Content = new StackPanel
			{
				Orientation = Orientation.Horizontal,
				Spacing = 6.0,
				Children =
				{
					(UIElement)new FontIcon { Glyph = "\ue898", FontSize = 14.0 },
					(UIElement)new TextBlock { Text = "上传排行", FontSize = 13.0 }
				}
			},
			CornerRadius = new CornerRadius(6.0),
			Padding = new Thickness(12.0, 8.0, 12.0, 8.0)
		};
		_uploadBtn.Click += OnUploadClick;
		_rankingBtn = new Button
		{
			Content = new StackPanel
			{
				Orientation = Orientation.Horizontal,
				Spacing = 6.0,
				Children =
				{
					(UIElement)new FontIcon { Glyph = "\ue9d5", FontSize = 14.0 },
					(UIElement)new TextBlock { Text = "排行榜", FontSize = 13.0 }
				}
			},
			CornerRadius = new CornerRadius(6.0),
			Padding = new Thickness(12.0, 8.0, 12.0, 8.0)
		};
		_rankingBtn.Click += OnRankingClick;
		_latencyOnlyBtn = new Button
		{
			Content = new StackPanel
			{
				Orientation = Orientation.Horizontal,
				Spacing = 6.0,
				Children =
				{
					(UIElement)new FontIcon { Glyph = "\ue9d9", FontSize = 14.0 },
					(UIElement)new TextBlock { Text = "单独测核间延迟", FontSize = 13.0 }
				}
			},
			CornerRadius = new CornerRadius(6.0),
			Padding = new Thickness(12.0, 8.0, 12.0, 8.0)
		};
		_latencyOnlyBtn.Click += OnLatencyOnlyClick;
		_globalProgress = new ProgressBar
		{
			Value = 0.0,
			Maximum = 100.0,
			Height = 4.0
		};
		_statusText = new TextBlock
		{
			Text = "",
			FontSize = 12.0,
			Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"]
		};
		_chkCpu = new CheckBox { Content = "CPU", IsChecked = true, FontSize = 12.0 };
		_chkGpu = new CheckBox { Content = "GPU", IsChecked = true, FontSize = 12.0 };
		_chkMem = new CheckBox { Content = "内存", IsChecked = true, FontSize = 12.0 };
		_chkDisk = new CheckBox { Content = "硬盘", IsChecked = true, FontSize = 12.0 };
		_chkBrowser = new CheckBox { Content = "浏览器", IsChecked = true, FontSize = 12.0 };
		_chkWin = new CheckBox { Content = "WinUI", IsChecked = true, FontSize = 12.0 };
		StackPanel stackPanel = new()
		{
			Orientation = Orientation.Horizontal,
			Spacing = 12.0
		};
		stackPanel.Children.Add(new TextBlock
		{
			Text = "测试项目:",
			FontSize = 12.0,
			VerticalAlignment = VerticalAlignment.Center,
			Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
		});
		stackPanel.Children.Add(_chkCpu);
		stackPanel.Children.Add(_chkGpu);
		stackPanel.Children.Add(_chkMem);
		stackPanel.Children.Add(_chkDisk);
		stackPanel.Children.Add(_chkBrowser);
		stackPanel.Children.Add(_chkWin);
		_ = LoadAvailableGpusAsync();
		StackPanel stackPanel2 = new()
		{
			Orientation = Orientation.Horizontal,
			Spacing = 8.0
		};
		stackPanel2.Children.Add(_startBtn);
		stackPanel2.Children.Add(_stopBtn);
		stackPanel2.Children.Add(_exportBtn);
		stackPanel2.Children.Add(_historyBtn);
		stackPanel2.Children.Add(_uploadBtn);
		stackPanel2.Children.Add(_rankingBtn);
		stackPanel2.Children.Add(_latencyOnlyBtn);
		return new StackPanel
		{
			Spacing = 8.0,
			Children =
			{
				(UIElement)stackPanel,
				(UIElement)stackPanel2,
				(UIElement)_globalProgress,
				(UIElement)_statusText
			}
		};
	}

	private async void OnStartClick(object sender, RoutedEventArgs e)
	{
		if (_isRunning) return;
		_uiGeneration++; // 使仍在途的历史恢复回填失效
		bool runCpu = _chkCpu.IsChecked == true;
		bool runGpu = _chkGpu.IsChecked == true;
		bool runMem = _chkMem.IsChecked == true;
		bool runDisk = _chkDisk.IsChecked == true;
		bool runBrowser = _chkBrowser.IsChecked == true;
		bool runWin = _chkWin.IsChecked == true;
		if (!runCpu && !runGpu && !runMem && !runDisk && !runBrowser && !runWin)
		{
			_statusText.Text = "请至少选择一个测试项目";
			return;
		}
		_isRunning = true;
		_startBtn.IsEnabled = false;
		_stopBtn.IsEnabled = true;
		_exportBtn.IsEnabled = false;
		SetCheckboxesEnabled(false);
		_cts = new CancellationTokenSource();
		ResetUI();
		int gpuIdx = 0;
		string gpuName = "";
		if (runGpu)
		{
			var picked = await ShowGpuSelectDialogAsync();
			if (picked == null)
			{
				runGpu = false;
				_statusText.Text = "已取消GPU测试，继续其他项目...";
			}
			else
			{
				gpuIdx = picked.Value.Index;
				gpuName = picked.Value.Name;
			}
		}
		try
		{
			var result = new PerformanceBenchmarkResult
			{
				TestTime = DateTime.Now,
				DurationMode = "Deep"
			};
			PerformanceBenchmarkService.PopulateHardwareInfo(result);
			Stopwatch sw = Stopwatch.StartNew();
			var progress = new Progress<BenchmarkProgress>(p =>
			{
				DispatcherQueue.TryEnqueue(() =>
				{
					_statusText.Text = $"{p.Phase} · {p.SubPhase}  {p.Detail}  (可随时点击停止)";
					_globalProgress.Value = p.Progress * 100.0;
				});
			});
			if (runCpu)
			{
				result.Cpu = await Task.Run(() => PerformanceBenchmarkService.RunCpuBenchmark(60, progress, _cts.Token), _cts.Token);
				_cts.Token.ThrowIfCancellationRequested();
				DispatcherQueue.TryEnqueue(() => UpdateCpuUI(result));
				string? coreToCoreExe = PerformanceBenchmarkService.FindCoreToCoreLatencyExe();
				if (coreToCoreExe != null)
				{
					var (csv, _) = await ShowCoreToCoreLatencyDialog(coreToCoreExe);
					if (!string.IsNullOrEmpty(csv))
					{
						int maxCores = Math.Min(Environment.ProcessorCount, 64);
						var matrix = PerformanceBenchmarkService.ParseCoreToCoreCsv(csv, maxCores);
						PerformanceBenchmarkService.ApplyLatencyResult(result.Cpu, matrix);
						_latencyHeatmapPath = PerformanceBenchmarkService.GenerateLatencyHeatmap(matrix);
						DispatcherQueue.TryEnqueue(() =>
						{
							ShowLatencyHeatmap(_latencyHeatmapPath);
							UpdateScoreRow(_cpuLatencyScoreText, result.Cpu.LatencyScore);
						});
					}
				}
			}
			if (runMem)
			{
				result.Memory = await Task.Run(() => PerformanceBenchmarkService.RunMemoryBenchmark(1, progress, _cts.Token), _cts.Token);
				_cts.Token.ThrowIfCancellationRequested();
				DispatcherQueue.TryEnqueue(() => UpdateMemoryUI(result));
			}
			if (runDisk)
			{
				result.Disk = await Task.Run(() => PerformanceBenchmarkService.RunDiskBenchmark(20, progress, _cts.Token), _cts.Token);
				_cts.Token.ThrowIfCancellationRequested();
				DispatcherQueue.TryEnqueue(() => UpdateDiskUI(result));
			}
			if (runGpu)
			{
				result.Gpu = await Task.Run(() => PerformanceBenchmarkService.RunGpuBenchmarkFurMark(60, progress, _cts.Token, gpuIdx, gpuName), _cts.Token);
				if (!string.IsNullOrEmpty(result.Gpu.GpuName))
					result.GpuName = result.Gpu.GpuName;
				_cts.Token.ThrowIfCancellationRequested();
				DispatcherQueue.TryEnqueue(() => UpdateGpuUI(result));
			}
			if (runBrowser)
			{
				result.Browser = new BrowserBenchmarkResult();
				await RunBrowserTestsAsync(result, 60, _cts.Token);
				_cts.Token.ThrowIfCancellationRequested();
			}
			if (runWin)
			{
				await RunWinBenchmarkAsync(result, _cts.Token);
				_cts.Token.ThrowIfCancellationRequested();
			}
			result.GamingScore = PerformanceBenchmarkService.ComputeGamingScore(result);
			result.GamingGrade = PerformanceBenchmarkService.ComputeGrade(result.GamingScore);
			result.OfficeScore = PerformanceBenchmarkService.ComputeOfficeScore(result);
			result.OfficeGrade = PerformanceBenchmarkService.ComputeGrade(result.OfficeScore);
			sw.Stop();
			result.TotalDuration = sw.Elapsed;
			DispatcherQueue.TryEnqueue(() =>
			{
				UpdateTopCard(_gamingScoreText, _gamingGradeText, _gamingBar, result.GamingScore, result.GamingGrade);
				UpdateTopCard(_officeScoreText, _officeGradeText, _officeBar, result.OfficeScore, result.OfficeGrade);
				if (runWin)
					UpdateTopCard(_winScoreText, _winGradeText, _winBar, result.Win.FinalScore, result.Win.Grade);
			});
			PerformanceBenchmarkService.SaveHistory(result);
			_result = result;
			_exportBtn.IsEnabled = true;
			_statusText.Text = $"测试完成！总耗时: {result.TotalDuration:mm\\mss\\s}";
			_globalProgress.Value = 100.0;
			DispatcherQueue.TryEnqueue(() => _ = ShowPostBenchmarkDialogAsync());
		}
		catch (OperationCanceledException)
		{
			_statusText.Text = "测试已取消";
		}
		catch (Exception ex)
		{
			_statusText.Text = "测试出错: " + ex.Message;
		}
		finally
		{
			_isRunning = false;
			_startBtn.IsEnabled = true;
			_stopBtn.IsEnabled = false;
			SetCheckboxesEnabled(true);
		}
	}

	private void SetCheckboxesEnabled(bool enabled)
	{
		_chkCpu.IsEnabled = enabled;
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
		if (r.Cpu.LatencyScore > 0)
			UpdateScoreRow(_cpuLatencyScoreText, r.Cpu.LatencyScore);

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

	// ---------- WinUI 性能测试（弹窗运行） ----------

	private async Task RunWinBenchmarkAsync(PerformanceBenchmarkResult result, CancellationToken ct)
	{
		DispatcherQueue.TryEnqueue(() => _statusText.Text = "正在准备 WinUI 性能测试数据...");
		_winIconPaths = await Task.Run(() => WinPerformanceService.CollectIconImagePaths());
		_winSortData = await Task.Run(() => WinPerformanceService.GenerateSortData());

		// 弹窗：包含状态区 + 真实测试控件（用于布局计时）
		var statusText = new TextBlock
		{
			Text = "正在初始化...",
			FontSize = 13.0,
			TextWrapping = TextWrapping.Wrap,
			Margin = new Thickness(0.0, 4.0, 0.0, 0.0),
			Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
		};
		var progressBar = new ProgressBar { Value = 0.0, Maximum = 100.0, Height = 4.0 };
		var logText = new TextBlock
		{
			FontSize = 11.0,
			TextWrapping = TextWrapping.Wrap,
			Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"]
		};
		var logScroll = new ScrollViewer
		{
			Content = logText,
			MaxHeight = 90.0,
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
			HorizontalScrollMode = ScrollMode.Disabled
		};

		var winControls = BuildWinTestControls();
		StackPanel dialogContent = new() { Spacing = 6.0 };
		dialogContent.Children.Add(statusText);
		dialogContent.Children.Add(progressBar);
		dialogContent.Children.Add(winControls);
		dialogContent.Children.Add(logScroll);

		ContentDialog dialog = new()
		{
			Title = "WinUI 性能测试",
			Content = new ScrollViewer
			{
				Content = dialogContent,
				MaxHeight = 620.0,
				VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
				HorizontalScrollMode = ScrollMode.Disabled
			},
			CloseButtonText = "取消",
			XamlRoot = XamlRoot,
			RequestedTheme = ThemeService.CurrentElementTheme
		};

		using var cancelReg = ct.Register(() =>
		{
			DispatcherQueue.TryEnqueue(() =>
			{
				try { dialog.Hide(); } catch { }
			});
		});

		var showTask = dialog.ShowAsync().AsTask();
		// 等对话框完全加载后再开始计时（首帧布局）
		await Task.Delay(150);
		if (showTask.IsCompleted)
		{
			// 用户直接点取消
			ct.ThrowIfCancellationRequested();
		}

		var winResult = new WinPerformanceResult
		{
			TestTime = DateTime.Now,
			RunCount = 5,
			DroppedRunCount = 1
		};

		try
		{
			for (int round = 1; round <= winResult.RunCount; round++)
			{
				ct.ThrowIfCancellationRequested();
				// 用户点了「取消」则中止
				if (showTask.IsCompleted)
				{
					ct.ThrowIfCancellationRequested();
				}
				statusText.Text = $"WinUI 性能测试 第 {round}/{winResult.RunCount} 轮...";
				var run = await RunWinRoundAsync(ct, statusText);
				_winRuns.Add(run);
				statusText.Text = $"第 {round}/{winResult.RunCount} 轮完成，耗时 {run.TotalMs:F0} ms";
				progressBar.Value = round * 100.0 / winResult.RunCount;
			}

			winResult.Runs = new List<WinPerformanceRunResult>(_winRuns);
			WinPerformanceService.FinalizeResult(winResult);
			result.Win = winResult;
			WinPerformanceService.SaveHistory(winResult);

			statusText.Text = $"Win性能得分: {winResult.FinalScore} ({winResult.Grade})，最佳平均耗时: {winResult.BestAvgMs:F0} ms";
			progressBar.Value = 100.0;
			logText.Text = BuildWinLog(winResult);

			DispatcherQueue.TryEnqueue(() =>
			{
				_winListLoadText.Text = $"{winResult.AvgListLoadMs:F0} ms";
				_winImageListText.Text = $"{winResult.AvgImageListMs:F0} ms";
				_winTabSwitchText.Text = $"{winResult.AvgTabSwitchMs:F0} ms";
				_winScrollText.Text = $"{winResult.AvgScrollMs:F0} ms";
				_winTreeExpandText.Text = $"{winResult.AvgTreeExpandMs:F0} ms";
				_winSortFilterText.Text = $"{winResult.AvgSortFilterMs:F0} ms";
				_winTextRenderText.Text = $"{winResult.AvgTextRenderMs:F0} ms";
				_winTotalText.Text = $"{winResult.BestAvgMs:F0} ms";
			});

			// 得分已同步到主页面，稍作停留展示结果后自动关闭弹窗
			await Task.Delay(1500);
		}
		finally
		{
			try { dialog.Hide(); } catch { }
		}
	}

	private static string BuildWinLog(WinPerformanceResult r)
	{
		var sb = new System.Text.StringBuilder();
		sb.AppendLine($"列表加载: {r.AvgListLoadMs:F0} ms");
		sb.AppendLine($"图片列表({WinPerformanceService.ImageListCount}张): {r.AvgImageListMs:F0} ms");
		sb.AppendLine($"标签切换: {r.AvgTabSwitchMs:F0} ms");
		sb.AppendLine($"滚动: {r.AvgScrollMs:F0} ms");
		sb.AppendLine($"树形展开({WinPerformanceService.TreeExpandCount}节点): {r.AvgTreeExpandMs:F0} ms");
		sb.AppendLine($"排序过滤({WinPerformanceService.SortFilterCount}条): {r.AvgSortFilterMs:F0} ms");
		sb.AppendLine($"长文本({WinPerformanceService.LongTextChars}字符): {r.AvgTextRenderMs:F0} ms");
		sb.AppendLine($"平均总耗时: {r.BestAvgMs:F0} ms");
		return sb.ToString();
	}

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
				CornerRadius = new CornerRadius(6.0),
				Padding = new Thickness(10.0, 6.0, 10.0, 6.0),
				Child = new StackPanel
				{
					Spacing = 4.0,
					Children =
					{
						new TextBlock { Text = title, FontSize = 11.0, Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] },
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

	private async Task LoadAvailableGpusAsync()
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
							FontSize = 13.0,
							TextWrapping = TextWrapping.Wrap
						},
						(UIElement)new TextBlock
						{
							Text = string.Join(" · ", detail),
							FontSize = 11.0,
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
			RequestedTheme = ThemeService.CurrentElementTheme
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
		return (selIdx, name);
	}

	private async Task ShowPostBenchmarkDialogAsync()
	{
		if (AppSettings.GetBool("BenchmarkPostPromptDisabled")) return;

		var chkDontShow = new CheckBox
		{
			Content = "下次不再提示",
			FontSize = 12,
			Margin = new Thickness(0, 8, 0, 0)
		};
		var content = new StackPanel
		{
			Spacing = 4,
			Children =
			{
				new TextBlock { Text = "测试已经跑完了，你可以上传你的跑分或者给你的电脑打分。", TextWrapping = TextWrapping.Wrap },
				chkDontShow
			}
		};
		var dialog = new ContentDialog
		{
			Title = "测试完成",
			Content = content,
			PrimaryButtonText = "上传跑分",
			SecondaryButtonText = "评价电脑",
			CloseButtonText = "取消",
			XamlRoot = XamlRoot,
			RequestedTheme = ThemeService.CurrentElementTheme
		};
		var result = await dialog.ShowAsync();
		if (chkDontShow.IsChecked == true)
			AppSettings.Set("BenchmarkPostPromptDisabled", true);
		if (result == ContentDialogResult.Primary)
			// 延迟到下一帧再打开上传流程，确保"测试完成"对话框已完全关闭
			DispatcherQueue.TryEnqueue(() => OnUploadClick(this, null!));
		else if (result == ContentDialogResult.Secondary)
		{
			var tool = new RatingSystemTool();
			var ctx = new BuiltinToolContext { XamlRoot = XamlRoot };
			MainWindow.ActiveToolName = tool.Name;
			try { await tool.ExecuteAsync(ctx); }
			finally { MainWindow.ActiveToolName = null; }
		}
	}

	private void OnStopClick(object sender, RoutedEventArgs e)
	{
		PerformanceBenchmarkService.Cancel();
		_cts.Cancel();
	}

	private async Task<(string csv, string stderr)> ShowCoreToCoreLatencyDialog(string exePath)
	{
		bool isC2CLatency = string.Equals(Path.GetFileName(exePath), "C2CLatency.exe", StringComparison.OrdinalIgnoreCase);
		string toolName = isC2CLatency ? "C2CLatency" : "core-to-core-latency";
		TextBlock outputText = new()
		{
			FontFamily = new FontFamily("Consolas"),
			FontSize = 12.0,
			TextWrapping = TextWrapping.Wrap,
			IsTextSelectionEnabled = true,
			Text = $"正在运行 {toolName}...\n"
		};
		ScrollViewer scroll = new()
		{
			Content = outputText,
			MaxHeight = 400.0,
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto
		};
		ContentDialog dialog = new()
		{
			Title = $"核间延迟测试 — {toolName}",
			Content = scroll,
			CloseButtonText = "取消",
			XamlRoot = XamlRoot,
			RequestedTheme = ThemeService.CurrentElementTheme
		};
		var csvBuilder = new StringBuilder();
		var stderrBuilder = new StringBuilder();
		var tcs = new TaskCompletionSource<(string csv, string stderr)>();
		bool procExited = false;
		bool dialogShown = false;
		string BuildCsv()
		{
			string csv = csvBuilder.ToString();
			if (isC2CLatency)
				csv = PerformanceBenchmarkService.ConvertC2CLatencyTextToCsv(stderrBuilder.ToString(), Math.Min(Environment.ProcessorCount, 64));
			return csv;
		}
		ProcessStartInfo startInfo = new()
		{
			FileName = exePath,
			Arguments = isC2CLatency ? "" : "--csv",
			UseShellExecute = false,
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			RedirectStandardInput = isC2CLatency,
			CreateNoWindow = true,
			StandardOutputEncoding = Encoding.UTF8,
			StandardErrorEncoding = Encoding.UTF8
		};
		Process? proc = null;
		try
		{
			proc = Process.Start(startInfo)!;
			if (proc == null)
			{
				outputText.Text = $"无法启动 {toolName}";
				return (csv: "", stderr: "");
			}
			if (isC2CLatency)
			{
				proc.StandardInput.WriteLine();
				proc.StandardInput.Flush();
			}
			proc.OutputDataReceived += (_, e) =>
			{
				if (e.Data != null) csvBuilder.AppendLine(e.Data);
			};
			proc.ErrorDataReceived += (_, e) =>
			{
				if (e.Data != null)
				{
					stderrBuilder.AppendLine(e.Data);
					string captured = e.Data;
					DispatcherQueue.TryEnqueue(() =>
					{
						TextBlock textBlock = outputText;
						textBlock.Text = textBlock.Text + captured + "\n";
						scroll.ChangeView(null, scroll.ScrollableHeight, null);
					});
					if (isC2CLatency && captured.Contains("Testing completed successfully", StringComparison.Ordinal))
					{
						try
						{
							proc.StandardInput.WriteLine();
							proc.StandardInput.Flush();
						}
						catch { }
					}
				}
			};
			proc.BeginOutputReadLine();
			proc.BeginErrorReadLine();
			proc.EnableRaisingEvents = true;
			proc.Exited += (_, _) =>
			{
				procExited = true;
				DispatcherQueue.TryEnqueue(() =>
				{
					outputText.Text += "\n--- 测试完成 ---\n";
					scroll.ChangeView(null, scroll.ScrollableHeight, null);
					if (!tcs.Task.IsCompleted)
					{
						tcs.SetResult((BuildCsv(), stderrBuilder.ToString()));
					}
					if (dialogShown)
					{
						try { dialog.Hide(); } catch { }
					}
				});
			};
		}
		catch (Exception ex)
		{
			outputText.Text = "启动失败: " + ex.Message;
			return (csv: "", stderr: "");
		}
		Task<ContentDialogResult> showTask;
		try
		{
			showTask = dialog.ShowAsync().AsTask();
			dialogShown = true;
		}
		catch (Exception ex)
		{
			outputText.Text = "对话框打开失败: " + ex.Message;
			return (csv: "", stderr: "");
		}
		_ = showTask.ContinueWith(_ =>
		{
			dialogShown = false;
			if (!procExited && proc != null)
			{
				try { if (!proc.HasExited) proc.Kill(); } catch { }
			}
			DispatcherQueue.TryEnqueue(() =>
			{
				if (!tcs.Task.IsCompleted)
				{
					tcs.SetResult((BuildCsv(), stderrBuilder.ToString()));
				}
			});
		}, TaskScheduler.Default);
		(var csv, var stderr) = await tcs.Task;
		try { await Task.WhenAny(showTask, Task.Delay(500)); } catch { }
		return (csv, stderr);
	}

	private async Task RunBrowserTestsAsync(PerformanceBenchmarkResult result, int gpuSec, CancellationToken ct)
	{
		WebView2 webView = new() { Width = 900.0, Height = 600.0 };
		TextBlock item = new()
		{
			Text = "正在加载浏览器测试...",
			FontSize = 13.0,
			Margin = new Thickness(0.0, 4.0, 0.0, 0.0),
			Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
		};
		StackPanel stackPanel = new() { Spacing = 4.0 };
		stackPanel.Children.Add(webView);
		stackPanel.Children.Add(item);
		ContentDialog dialog = new()
		{
			Title = "浏览器性能测试",
			Content = stackPanel,
			CloseButtonText = "取消",
			XamlRoot = XamlRoot,
			RequestedTheme = ThemeService.CurrentElementTheme
		};
		await webView.EnsureCoreWebView2Async(await WebView2EnvironmentService.GetAsync());
		var tcs = new TaskCompletionSource<BrowserBenchmarkResult>();
		webView.CoreWebView2.WebMessageReceived += (_, args) =>
		{
			try
			{
				JsonElement root = JsonDocument.Parse(args.TryGetWebMessageAsString()).RootElement;
				var br = new BrowserBenchmarkResult();
				br.JsScore = root.TryGetProperty("jsScore", out var v1) ? v1.GetInt32() : 0;
				br.JsDetail = root.TryGetProperty("jsDetail", out var v2) ? v2.GetString() ?? "" : "";
				br.DomScore = root.TryGetProperty("domScore", out var v3) ? v3.GetInt32() : 0;
				br.DomDetail = root.TryGetProperty("domDetail", out var v4) ? v4.GetString() ?? "" : "";
				br.CardScore = root.TryGetProperty("cardScore", out var v5) ? v5.GetInt32() : 0;
				br.CardDetail = root.TryGetProperty("cardDetail", out var v6) ? v6.GetString() ?? "" : "";
				br.CssScore = root.TryGetProperty("cssScore", out var v7) ? v7.GetInt32() : 0;
				br.CssDetail = root.TryGetProperty("cssDetail", out var v8) ? v8.GetString() ?? "" : "";
				br.LayoutScore = root.TryGetProperty("layoutScore", out var v9) ? v9.GetInt32() : 0;
				br.LayoutDetail = root.TryGetProperty("layoutDetail", out var v10) ? v10.GetString() ?? "" : "";
				br.EventScore = root.TryGetProperty("eventScore", out var v11) ? v11.GetInt32() : 0;
				br.EventDetail = root.TryGetProperty("eventDetail", out var v12) ? v12.GetString() ?? "" : "";
				br.TotalScore = root.TryGetProperty("totalScore", out var v13) ? v13.GetInt32() : 0;
				br.Grade = PerformanceBenchmarkService.ComputeGrade(br.TotalScore);
				tcs.TrySetResult(br);
			}
			catch { }
		};
		_ = dialog.ShowAsync().AsTask().ContinueWith(_ =>
		{
			DispatcherQueue.TryEnqueue(() =>
			{
				if (!tcs.Task.IsCompleted) tcs.TrySetCanceled();
			});
		});
		string uriString = Path.Combine(AppContext.BaseDirectory, "Assets", "Benchmark", "browser-benchmark.html");
		webView.CoreWebView2.Navigate(new Uri(uriString).AbsoluteUri);
		using (ct.Register(() => tcs.TrySetCanceled()))
		{
			try
			{
				BrowserBenchmarkResult br = await tcs.Task;
				result.Browser = br;
				DispatcherQueue.TryEnqueue(() =>
				{
					UpdateDetailRow(_brJsScoreText, _brJsDetailText, br.JsScore, br.JsDetail);
					UpdateDetailRow(_brDomScoreText, _brDomDetailText, br.DomScore, br.DomDetail);
					UpdateDetailRow(_brCardScoreText, _brCardDetailText, br.CardScore, br.CardDetail);
					UpdateDetailRow(_brCssScoreText, _brCssDetailText, br.CssScore, br.CssDetail);
					UpdateDetailRow(_brLayoutScoreText, _brLayoutDetailText, br.LayoutScore, br.LayoutDetail);
					UpdateDetailRow(_brEventScoreText, _brEventDetailText, br.EventScore, br.EventDetail);
				});
				dialog.Hide();
			}
			catch (OperationCanceledException) { throw; }
			catch { }
		}
	}

	private void UpdateCpuUI(PerformanceBenchmarkResult r)
	{
		UpdateScoreRow(_cpuSingleScoreText, r.Cpu.SingleCoreScore);
		UpdateScoreRow(_cpuMultiScoreText, r.Cpu.MultiCoreScore);
		UpdateScoreRow(_cpuLatencyScoreText, r.Cpu.LatencyScore);
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
		_gamingGradeText.Text = "";
		_gamingBar.Value = 0.0;
		_officeScoreText.Text = "—";
		_officeGradeText.Text = "";
		_officeBar.Value = 0.0;
		_winScoreText.Text = "—";
		_winGradeText.Text = "";
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
		if (_result == null) return;
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
				await new ContentDialog
				{
					Title = "历史对比",
					Content = new TextBlock { Text = "暂无历史测试记录，先完成一次性能测试吧。", Margin = new Thickness(16.0) },
					CloseButtonText = "关闭",
					XamlRoot = XamlRoot,
					RequestedTheme = ThemeService.CurrentElementTheme
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

	private async void OnUploadClick(object sender, RoutedEventArgs e)
	{
		// 防止重复触发导致两个 ContentDialog 同时打开（WinUI 3 只允许一个对话框）
		if (_uploadInProgress) return;
		_uploadInProgress = true;
		_uploadBtn.IsEnabled = false;
		try
		{
			await OnUploadClickCoreAsync();
		}
		finally
		{
			_uploadInProgress = false;
			_uploadBtn.IsEnabled = true;
		}
	}

	private async Task OnUploadClickCoreAsync()
	{
		List<PerformanceBenchmarkResult> candidates = PerformanceBenchmarkService.LoadHistory()
			.OrderByDescending(h => h.TestTime)
			.ToList();
		if (_result != null && !candidates.Any(c => c.TestTime == _result.TestTime))
			candidates.Insert(0, _result);
		if (candidates.Count == 0)
		{
			await new ContentDialog
			{
				Title = "无测试报告",
				Content = "请先运行一次性能测试，再上传报告。",
				CloseButtonText = "确定",
				XamlRoot = XamlRoot,
				RequestedTheme = ThemeService.CurrentElementTheme
			}.ShowAsync();
			return;
		}
		if (!GitHubAuthService.IsLoggedIn)
		{
			try
			{
				await GitHubAuthService.EnsureAuthenticatedAsync(XamlRoot, CancellationToken.None);
			}
			catch
			{
				await new ContentDialog
				{
					Title = "需要登录",
					Content = "上传报告需要 GitHub 账号，请先在设置中登录。",
					CloseButtonText = "确定",
					XamlRoot = XamlRoot,
					RequestedTheme = ThemeService.CurrentElementTheme
				}.ShowAsync();
				return;
			}
		}
		// 若刚完成登录，等登录对话框完全关闭后再弹下一个对话框，避免 "Only a single ContentDialog can be open"
		await Task.Yield();
		PerformanceBenchmarkResult? selected = candidates.Count == 1
			? candidates[0]
			: await PickReportToUploadAsync(candidates);
		if (selected == null) return;
		var confirmContent = new StackPanel
		{
			Spacing = 8.0,
			Children =
			{
				new TextBlock
				{
						Text = $"将上传以下测试报告：\n\nCPU: {selected.CpuName}\nGPU: {selected.GpuName}\n游戏: {selected.GamingScore} ({selected.GamingGrade})\n办公: {selected.OfficeScore} ({selected.OfficeGrade})\nWin性能: {selected.Win.FinalScore} ({selected.Win.Grade})\n测试时间: {selected.TestTime:yyyy-MM-dd HH:mm}\n\n报告将通过 PR 提交到社区仓库，合并后出现在排行榜。",
					TextWrapping = TextWrapping.Wrap
				}
			}
		};
		CheckBox? chkHeatmap = null;
		if (selected.Cpu.LatencyMatrix != null)
		{
			chkHeatmap = new CheckBox
			{
				Content = "同时上传核间延迟热力图（可在核间延迟查询工具中查看）",
				IsChecked = true
			};
			confirmContent.Children.Add(chkHeatmap);
		}
		if (await new ContentDialog
		{
			Title = "上传测试报告",
			Content = confirmContent,
			PrimaryButtonText = "上传",
			CloseButtonText = "取消",
			XamlRoot = XamlRoot,
			RequestedTheme = ThemeService.CurrentElementTheme
		}.ShowAsync() != ContentDialogResult.Primary)
		{
			return;
		}
		ContentDialog progressDlg = new()
		{
			Title = "正在上传",
			Content = new ProgressBar { IsIndeterminate = true },
			XamlRoot = XamlRoot,
			RequestedTheme = ThemeService.CurrentElementTheme
		};
		var progressShowTask = progressDlg.ShowAsync().AsTask();
		try
		{
			var progress = new Progress<string>(msg =>
			{
				DispatcherQueue.TryEnqueue(() =>
				{
					progressDlg.Content = new StackPanel
					{
						Spacing = 8.0,
						Children =
						{
							(UIElement)new TextBlock { Text = msg },
							(UIElement)new ProgressBar { IsIndeterminate = true }
						}
					};
				});
			});
			string? heatmapPath = null;
			if (chkHeatmap?.IsChecked == true && selected.Cpu.LatencyMatrix != null)
			{
				heatmapPath = PerformanceBenchmarkService.GenerateLatencyHeatmap(selected.Cpu.LatencyMatrix);
			}
			string prUrl = await BenchmarkCloudService.UploadReportAsync(selected, progress, CancellationToken.None, heatmapPath);
			progressDlg.Hide();
			try { await progressShowTask; } catch { }
			if (await new ContentDialog
			{
				Title = "上传成功",
				Content = "报告已通过 PR 提交，合并后将出现在排行榜。\n\nPR 链接：" + prUrl,
				PrimaryButtonText = "打开 PR",
				CloseButtonText = "关闭",
				XamlRoot = XamlRoot,
				RequestedTheme = ThemeService.CurrentElementTheme
			}.ShowAsync() == ContentDialogResult.Primary)
			{
				await Launcher.LaunchUriAsync(new Uri(prUrl));
			}
		}
		catch (Exception ex)
		{
			progressDlg.Hide();
			try { await progressShowTask; } catch { }
			await new ContentDialog
			{
				Title = "上传失败",
				Content = ex.Message,
				CloseButtonText = "确定",
				XamlRoot = XamlRoot,
				RequestedTheme = ThemeService.CurrentElementTheme
			}.ShowAsync();
		}
	}

	private async Task<PerformanceBenchmarkResult?> PickReportToUploadAsync(List<PerformanceBenchmarkResult> candidates)
	{
		ListView listView = new()
		{
			MaxHeight = 360.0,
			SelectionMode = ListViewSelectionMode.Single
		};
		foreach (PerformanceBenchmarkResult c in candidates)
		{
			listView.Items.Add(new ListViewItem
			{
				Content = new StackPanel
				{
					Spacing = 2.0,
					Children =
					{
						new TextBlock
						{
							Text = $"{c.TestTime:yyyy-MM-dd HH:mm}  {c.CpuName}",
							FontWeight = FontWeights.Bold
						},
						new TextBlock
						{
							Text = $"GPU: {c.GpuName}   游戏: {c.GamingScore} ({c.GamingGrade})   办公: {c.OfficeScore} ({c.OfficeGrade})   Win: {c.Win.FinalScore} ({c.Win.Grade})",
							FontSize = 12.0,
							Opacity = 0.7
						}
					}
				}
			});
		}
		if (listView.Items.Count > 0) listView.SelectedIndex = 0;
		ContentDialog dialog = new()
		{
			Title = "选择要上传的报告",
			Content = listView,
			PrimaryButtonText = "下一步",
			CloseButtonText = "取消",
			IsPrimaryButtonEnabled = listView.Items.Count > 0,
			XamlRoot = XamlRoot,
			RequestedTheme = ThemeService.CurrentElementTheme
		};
		listView.SelectionChanged += (_, _) => dialog.IsPrimaryButtonEnabled = listView.SelectedIndex >= 0;
		if (await dialog.ShowAsync() != ContentDialogResult.Primary) return null;
		int idx = listView.SelectedIndex;
		return idx >= 0 && idx < candidates.Count ? candidates[idx] : null;
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
		if (_isRunning) return;
		string? coreToCoreExe = PerformanceBenchmarkService.FindCoreToCoreLatencyExe();
		if (coreToCoreExe == null)
		{
			await new ContentDialog
			{
				Title = "未找到测试工具",
				Content = "未找到核间延迟测试程序（C2CLatency.exe），请确认 Tools/处理器工具/C2CLatency 目录完整。",
				CloseButtonText = "确定",
				XamlRoot = XamlRoot,
				RequestedTheme = ThemeService.CurrentElementTheme
			}.ShowAsync();
			return;
		}
		_isRunning = true;
		_latencyOnlyBtn.IsEnabled = false;
		_startBtn.IsEnabled = false;
		SetCheckboxesEnabled(false);
		try
		{
			_statusText.Text = "正在运行核间延迟测试...";
			var (csv, _) = await ShowCoreToCoreLatencyDialog(coreToCoreExe);
			if (string.IsNullOrEmpty(csv))
			{
				_statusText.Text = "核间延迟测试未完成";
				return;
			}
			int maxCores = Math.Min(Environment.ProcessorCount, 64);
			var matrix = PerformanceBenchmarkService.ParseCoreToCoreCsv(csv, maxCores);
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
				RequestedTheme = ThemeService.CurrentElementTheme
			};
			if (await ask.ShowAsync() != ContentDialogResult.Primary)
			{
				_statusText.Text = "核间延迟测试完成，热力图已生成（未上传）";
				return;
			}
			// 等询问对话框完全关闭后再继续，避免紧接着弹出的对话框发生冲突
			await Task.Yield();
			if (!GitHubAuthService.IsLoggedIn)
			{
				try
				{
					await GitHubAuthService.EnsureAuthenticatedAsync(XamlRoot, CancellationToken.None);
				}
				catch
				{
					_statusText.Text = "未登录，取消上传";
					return;
				}
			}
			var tmp = new PerformanceBenchmarkResult();
			PerformanceBenchmarkService.PopulateHardwareInfo(tmp);
			ContentDialog progressDlg = new()
			{
				Title = "正在上传",
				Content = new ProgressBar { IsIndeterminate = true },
				XamlRoot = XamlRoot,
				RequestedTheme = ThemeService.CurrentElementTheme
			};
			var progressShowTask = progressDlg.ShowAsync().AsTask();
			try
			{
				var progress = new Progress<string>(msg =>
				{
					DispatcherQueue.TryEnqueue(() =>
					{
						progressDlg.Content = new StackPanel
						{
							Spacing = 8.0,
							Children =
							{
								(UIElement)new TextBlock { Text = msg },
								(UIElement)new ProgressBar { IsIndeterminate = true }
							}
						};
					});
				});
				string prUrl = await BenchmarkCloudService.UploadLatencyImageOnlyAsync(tmp.CpuName, heatmapPath!, progress, CancellationToken.None);
				progressDlg.Hide();
				try { await progressShowTask; } catch { }
				_statusText.Text = "核间延迟热力图上传成功";
				if (await new ContentDialog
				{
					Title = "上传成功",
					Content = "核间延迟热力图已通过 PR 提交，合并后可在核间延迟查询中查看。\n\nPR 链接：" + prUrl,
					PrimaryButtonText = "打开 PR",
					CloseButtonText = "关闭",
					XamlRoot = XamlRoot,
					RequestedTheme = ThemeService.CurrentElementTheme
				}.ShowAsync() == ContentDialogResult.Primary)
				{
					await Launcher.LaunchUriAsync(new Uri(prUrl));
				}
			}
			catch (Exception ex)
			{
				progressDlg.Hide();
				try { await progressShowTask; } catch { }
				await new ContentDialog
				{
					Title = "上传失败",
					Content = ex.Message,
					CloseButtonText = "确定",
					XamlRoot = XamlRoot,
					RequestedTheme = ThemeService.CurrentElementTheme
				}.ShowAsync();
			}
		}
		catch (Exception ex)
		{
			_statusText.Text = "核间延迟测试出错: " + ex.Message;
		}
		finally
		{
			_isRunning = false;
			_latencyOnlyBtn.IsEnabled = true;
			_startBtn.IsEnabled = true;
			SetCheckboxesEnabled(true);
		}
	}
}

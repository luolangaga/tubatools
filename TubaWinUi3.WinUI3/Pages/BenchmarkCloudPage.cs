using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.WinUI;
using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Controls;
using TubaWinUi3.Models;
using TubaWinUi3.Services;
using Windows.System;
using Windows.UI;

namespace TubaWinUi3.Pages;

public sealed partial class BenchmarkCloudPage : Page
{
	private List<BenchmarkReportEntry> _allReports = new List<BenchmarkReportEntry>();

	private ObservableCollection<BenchmarkLeaderboardEntry> _leaderboard = new ObservableCollection<BenchmarkLeaderboardEntry>();

	private List<BenchmarkReportEntry> _myReports = new List<BenchmarkReportEntry>();

	private List<BenchmarkLeaderboardEntry> _allLeaderboardEntries = new List<BenchmarkLeaderboardEntry>();

	private bool _loaded;

	private int _currentPage = -1;

	private bool _isLoadingMore;

	private bool _hasMorePages;

	private bool _isReportDetailBusy;

	private Pivot MainPivot = null!;

	private ProgressBar MyHistoryProgress = null!;

	private ScrollViewer MyHistoryArea = null!;

	private StackPanel MyHistoryEmpty = null!;

	private ListView MyHistoryList = null!;

	private CartesianChart MyHistoryChart = null!;

	private Button DeleteMyReportBtn = null!;

	private TextBlock MyHistoryLoginHint = null!;

	private ProgressBar SameHwProgress = null!;

	private ListView SameHwList = null!;

	private StackPanel SameHwEmpty = null!;

	private StackPanel SameHwInfo = null!;

	private TextBlock SameHwCpuText = null!;

	private TextBlock SameHwGpuText = null!;

	private ProgressBar CompareProgress = null!;

	private ScrollViewer CompareResultArea = null!;

	private StackPanel CompareEmpty = null!;

	private PolarChart CompareRadarChart = null!;

	private CartesianChart CompareBarChart = null!;

	private Grid CompareTableGrid = null!;

	private StackPanel CompareViewTogglePanel = null!;

	private List<ComboBox> CompareCombos = new List<ComboBox>();

	private StackPanel CompareComboPanel = null!;

	private Button CompareButton = null!;

	private const int MaxCompareCount = 6;

	private ProgressBar LeaderboardProgress = null!;

	private ListView LeaderboardList = null!;

	private ScrollViewer? _leaderboardScrollViewer;

	private StackPanel LeaderboardEmpty = null!;

	private TextBlock LeaderboardEmptyText = null!;

	private ComboBox SortByCombo = null!;

	private AutoSuggestBox CpuFilterBox = null!;

	private Button RefreshButton = null!;


	private ComboBox SourceCombo = null!;

	private TextBlock ReportCountText = null!;

	private ProgressBar LoadMoreProgress = null!;

	private TextBlock LoadMoreText = null!;

	// 视觉色板（品牌色/固定色板统一取自 ThemeColors，随主题解析）
	private static Color AccentBlue => ThemeColors.AccentBlue;

	public BenchmarkCloudPage()
	{
		InitializeComponent();
		ChartInitializer.EnsureConfigured();
		Content = BuildUI();
		Loaded += OnLoaded;
	}

	private Grid BuildUI()
	{
		bool isDark = ThemeService.IsDarkEffective;

		Color borderColor = isDark ? Color.FromArgb(255, 60, 60, 60) : Color.FromArgb(255, 229, 229, 229);
		SolidColorBrush cardBg = isDark
			? new SolidColorBrush(Color.FromArgb(255, 45, 45, 45))
			: new SolidColorBrush(Color.FromArgb(255, 249, 249, 249));
		SolidColorBrush dimText = isDark
			? new SolidColorBrush(Color.FromArgb(255, 153, 153, 153))
			: new SolidColorBrush(Color.FromArgb(255, 117, 117, 117));

		Grid root = new()
		{
			RowSpacing = 0.0,
			Padding = new Thickness(24.0, 4.0, 24.0, 32.0)
		};
		root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.0, GridUnitType.Star) });

		ToolPageHeader header = new()
		{
            ToolId = "benchmark-cloud",
			HeaderPadding = new Thickness(0.0, 0.0, 0.0, 12.0),
			Title = "跑分排行",
			Glyph = "\uE9D5"
		};

		RefreshButton = new Button
		{
			Content = new StackPanel
			{
				Orientation = Orientation.Horizontal,
				Spacing = 8.0,
				Children =
				{
					(UIElement)new FontIcon { Glyph = "\ue72c", FontSize = 14.0 },
					(UIElement)new TextBlock { Text = "刷新" }
				}
			},
			Style = (Style)Application.Current.Resources["ToolHeaderActionButtonStyle"]
		};
		RefreshButton.Click += RefreshButton_Click;
		header.Actions.Add(RefreshButton);

		SourceCombo = new ComboBox
		{
			ItemsSource = new List<string> { "GitHub", "GitCode" },
			SelectedIndex = BenchmarkCloudService.CurrentSource == LeaderboardSource.GitCode ? 1 : 0,
			MinWidth = 100.0
		};
		SourceCombo.SelectionChanged += SourceCombo_SelectionChanged;
		header.Actions.Add(SourceCombo);


		ReportCountText = new TextBlock
		{
			Text = "",
			FontSize = 12.0,
			VerticalAlignment = VerticalAlignment.Center,
			Foreground = dimText
		};
		header.Actions.Add(ReportCountText);

		root.Children.Add(header);
		Grid.SetRow(header, 0);

		MainPivot = new Pivot();
		MainPivot.Items.Add(BuildLeaderboardPivotItem(cardBg, borderColor, dimText));
		MainPivot.Items.Add(BuildSameHwPivotItem(cardBg, borderColor, dimText));
		MainPivot.Items.Add(BuildComparePivotItem(cardBg, borderColor, dimText));
		MainPivot.Items.Add(BuildMyHistoryPivotItem(cardBg, borderColor, dimText));
		root.Children.Add(MainPivot);
		Grid.SetRow(MainPivot, 1);

		return root;
	}

	private PivotItem BuildLeaderboardPivotItem(SolidColorBrush cardBg, Color borderColor, SolidColorBrush dimText)
	{
		Grid grid = new();
		grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.0, GridUnitType.Star) });

		StackPanel filterRow = new()
		{
			Orientation = Orientation.Horizontal,
			Spacing = 12.0,
			Margin = new Thickness(0.0, 0.0, 0.0, 8.0)
		};
		SortByCombo = new ComboBox
		{
			ItemsSource = new List<string> { "游戏性能", "办公性能", "CPU", "GPU", "硬盘", "浏览器" },
			SelectedIndex = 0,
			MinWidth = 120.0
		};
		SortByCombo.SelectionChanged += SortByCombo_SelectionChanged;
		filterRow.Children.Add(SortByCombo);

		CpuFilterBox = new AutoSuggestBox
		{
			PlaceholderText = "筛选 CPU...",
			Width = 200.0
		};
		CpuFilterBox.QuerySubmitted += Filter_QuerySubmitted;
		filterRow.Children.Add(CpuFilterBox);

		grid.Children.Add(filterRow);
		Grid.SetRow(filterRow, 0);

		LeaderboardProgress = new ProgressBar
		{
			Visibility = Visibility.Collapsed,
			IsIndeterminate = true,
			Margin = new Thickness(0.0, 4.0, 0.0, 4.0)
		};
		grid.Children.Add(LeaderboardProgress);
		Grid.SetRow(LeaderboardProgress, 1);

		var listFooter = new StackPanel();

		LeaderboardList = new ListView
		{
			Visibility = Visibility.Collapsed,
			ItemTemplate = BuildLeaderboardItemTemplate(),
			Footer = listFooter
		};
		LeaderboardList.SelectionChanged += LeaderboardList_SelectionChanged;
		LeaderboardList.Loaded += LeaderboardList_Loaded;

		LoadMoreProgress = new ProgressBar
		{
			Visibility = Visibility.Collapsed,
			IsIndeterminate = true,
			Margin = new Thickness(0.0, 4.0, 0.0, 4.0)
		};
		listFooter.Children.Add(LoadMoreProgress);

		LoadMoreText = new TextBlock
		{
			Text = "",
			FontSize = 12.0,
			Foreground = dimText,
			HorizontalAlignment = HorizontalAlignment.Center,
			Margin = new Thickness(0.0, 4.0, 0.0, 8.0),
			Visibility = Visibility.Collapsed
		};
		listFooter.Children.Add(LoadMoreText);

		grid.Children.Add(LeaderboardList);
		Grid.SetRow(LeaderboardList, 2);

		LeaderboardEmpty = new StackPanel
		{
			Visibility = Visibility.Collapsed,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			Spacing = 8.0
		};
		LeaderboardEmpty.Children.Add(new FontIcon
		{
			Glyph = "\ue946",
			FontSize = 28.0,
			Foreground = dimText
		});
		LeaderboardEmptyText = new TextBlock
		{
			Text = "暂无排行数据",
			FontSize = 14.0,
			Foreground = dimText,
			HorizontalAlignment = HorizontalAlignment.Center
		};
		LeaderboardEmpty.Children.Add(LeaderboardEmptyText);
		grid.Children.Add(LeaderboardEmpty);
		Grid.SetRow(LeaderboardEmpty, 2);

		return new PivotItem
		{
			Header = "排行榜",
			Content = grid
		};
	}

	private DataTemplate BuildLeaderboardItemTemplate()
	{
		return (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(
			@"<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
							xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
							xmlns:m='using:TubaWinUi3.Models'>
				<Grid Padding='4,8' ColumnSpacing='12'>
					<Grid.ColumnDefinitions>
						<ColumnDefinition Width='48'/>
						<ColumnDefinition Width='*'/>
						<ColumnDefinition Width='Auto'/>
					</Grid.ColumnDefinitions>
					<TextBlock Text='{Binding Rank}' FontSize='18' FontWeight='SemiBold' VerticalAlignment='Center'
							   Foreground='{Binding RankBrush}'/>
					<StackPanel Grid.Column='1' Spacing='2'>
						<TextBlock Text='{Binding Report.Author}' FontSize='14' FontWeight='SemiBold'/>
						<TextBlock FontSize='12' Foreground='{ThemeResource TextFillColorSecondaryBrush}'>
							<Run Text='{Binding Report.CpuName}'/><Run Text=' | '/><Run Text='{Binding Report.GpuName}'/>
						</TextBlock>
						<StackPanel Orientation='Horizontal' Spacing='8' Margin='0,2,0,0'>
							<StackPanel Orientation='Horizontal'>
								<TextBlock Text='CPU' FontSize='12' Foreground='{ThemeResource TextFillColorTertiaryBrush}' VerticalAlignment='Center' Margin='0,0,2,0'/>
								<TextBlock Text='{Binding Report.CpuMultiCoreScore}' FontSize='12' Foreground='{ThemeResource TextFillColorSecondaryBrush}' VerticalAlignment='Center'/>
							</StackPanel>
							<StackPanel Orientation='Horizontal'>
								<TextBlock Text='GPU' FontSize='12' Foreground='{ThemeResource TextFillColorTertiaryBrush}' VerticalAlignment='Center' Margin='0,0,2,0'/>
								<TextBlock Text='{Binding Report.GpuRenderScore}' FontSize='12' Foreground='{ThemeResource TextFillColorSecondaryBrush}' VerticalAlignment='Center'/>
							</StackPanel>
							<StackPanel Orientation='Horizontal'>
								<TextBlock Text='内存' FontSize='12' Foreground='{ThemeResource TextFillColorTertiaryBrush}' VerticalAlignment='Center' Margin='0,0,2,0'/>
								<TextBlock Text='{Binding Report.MemoryCapacityScore}' FontSize='12' Foreground='{ThemeResource TextFillColorSecondaryBrush}' VerticalAlignment='Center'/>
							</StackPanel>
							<StackPanel Orientation='Horizontal'>
								<TextBlock Text='硬盘' FontSize='12' Foreground='{ThemeResource TextFillColorTertiaryBrush}' VerticalAlignment='Center' Margin='0,0,2,0'/>
								<TextBlock Text='{Binding Report.DiskSeqReadScore}' FontSize='12' Foreground='{ThemeResource TextFillColorSecondaryBrush}' VerticalAlignment='Center'/>
							</StackPanel>
						</StackPanel>
					</StackPanel>
					<StackPanel Grid.Column='2' Orientation='Horizontal' Spacing='16' VerticalAlignment='Center'>
						<StackPanel Spacing='0' HorizontalAlignment='Right'>
							<TextBlock Text='{Binding Report.GamingScore}' FontSize='14' FontWeight='SemiBold' HorizontalAlignment='Right'/>
							<TextBlock Text='游戏' FontSize='12' Foreground='{ThemeResource TextFillColorTertiaryBrush}' HorizontalAlignment='Right'/>
						</StackPanel>
						<StackPanel Spacing='0' HorizontalAlignment='Right'>
							<TextBlock Text='{Binding Report.OfficeScore}' FontSize='14' FontWeight='SemiBold' HorizontalAlignment='Right'/>
							<TextBlock Text='办公' FontSize='12' Foreground='{ThemeResource TextFillColorTertiaryBrush}' HorizontalAlignment='Right'/>
						</StackPanel>
					</StackPanel>
				</Grid>
			</DataTemplate>");
	}

	private PivotItem BuildSameHwPivotItem(SolidColorBrush cardBg, Color borderColor, SolidColorBrush dimText)
	{
		Grid grid = new();
		grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.0, GridUnitType.Star) });

		SameHwInfo = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Spacing = 16.0,
			Margin = new Thickness(0.0, 0.0, 0.0, 8.0)
		};
		SameHwCpuText = new TextBlock
		{
			Text = "CPU: ",
			FontSize = 14.0,
			Foreground = dimText
		};
		SameHwInfo.Children.Add(SameHwCpuText);
		SameHwGpuText = new TextBlock
		{
			Text = "GPU: ",
			FontSize = 14.0,
			Foreground = dimText
		};
		SameHwInfo.Children.Add(SameHwGpuText);
		grid.Children.Add(SameHwInfo);
		Grid.SetRow(SameHwInfo, 0);

		SameHwProgress = new ProgressBar
		{
			Visibility = Visibility.Collapsed,
			IsIndeterminate = true,
			Margin = new Thickness(0.0, 4.0, 0.0, 4.0)
		};
		grid.Children.Add(SameHwProgress);
		Grid.SetRow(SameHwProgress, 1);

		SameHwList = new ListView
		{
			Visibility = Visibility.Collapsed,
			ItemTemplate = BuildLeaderboardItemTemplate()
		};
		SameHwList.SelectionChanged += SameHwList_SelectionChanged;
		grid.Children.Add(SameHwList);
		Grid.SetRow(SameHwList, 2);

		SameHwEmpty = new StackPanel
		{
			Visibility = Visibility.Collapsed,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			Spacing = 8.0
		};
		SameHwEmpty.Children.Add(new FontIcon
		{
			Glyph = "\ue946",
			FontSize = 28.0,
			Foreground = dimText
		});
		SameHwEmpty.Children.Add(new TextBlock
		{
			Text = "未找到同配置用户",
			FontSize = 14.0,
			Foreground = dimText,
			HorizontalAlignment = HorizontalAlignment.Center
		});
		grid.Children.Add(SameHwEmpty);
		Grid.SetRow(SameHwEmpty, 2);

		return new PivotItem
		{
			Header = "同配置",
			Content = grid
		};
	}

	private PivotItem BuildComparePivotItem(SolidColorBrush cardBg, Color borderColor, SolidColorBrush dimText)
	{
		StackPanel stack = new() { Spacing = 8.0 };

		CompareComboPanel = new StackPanel { Spacing = 6.0 };
		AddCompareRow();
		AddCompareRow();
		stack.Children.Add(CompareComboPanel);

		StackPanel btnRow = new()
		{
			Orientation = Orientation.Horizontal,
			Spacing = 8.0
		};
		var addBtn = new Button
		{
			Content = new StackPanel
			{
				Orientation = Orientation.Horizontal,
				Spacing = 4.0,
				Children =
				{
					new FontIcon { Glyph = "\uE710", FontSize = 12.0 },
					new TextBlock { Text = "添加报告", FontSize = 12.0 }
				}
			},
			CornerRadius = new CornerRadius(8.0),
			Padding = new Thickness(12.0, 4.0, 12.0, 4.0)
		};
		addBtn.Click += (s, e) =>
		{
			if (CompareCombos.Count < MaxCompareCount) AddCompareRow();
			if (CompareCombos.Count >= MaxCompareCount) addBtn.IsEnabled = false;
		};
		btnRow.Children.Add(addBtn);

		CompareButton = new Button
		{
			Content = "对比",
			CornerRadius = new CornerRadius(8.0),
			Padding = new Thickness(16.0, 8.0, 16.0, 8.0)
		};
		CompareButton.Click += CompareButton_Click;
		btnRow.Children.Add(CompareButton);
		stack.Children.Add(btnRow);

		CompareViewTogglePanel = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Spacing = 12,
			Visibility = Visibility.Collapsed,
			Margin = new Thickness(0, 8, 0, 0)
		};
		var radarRadio = new RadioButton { Content = "雷达图", IsChecked = true, Tag = "radar" };
		var barRadio = new RadioButton { Content = "柱状图", Tag = "bar" };
		var tableRadio = new RadioButton { Content = "数据表", Tag = "table" };
		radarRadio.Checked += CompareViewRadio_Checked;
		barRadio.Checked += CompareViewRadio_Checked;
		tableRadio.Checked += CompareViewRadio_Checked;
		CompareViewTogglePanel.Children.Add(radarRadio);
		CompareViewTogglePanel.Children.Add(barRadio);
		CompareViewTogglePanel.Children.Add(tableRadio);
		stack.Children.Add(CompareViewTogglePanel);

		CompareProgress = new ProgressBar
		{
			Visibility = Visibility.Collapsed,
			IsIndeterminate = true
		};
		stack.Children.Add(CompareProgress);

		CompareRadarChart = new PolarChart { Height = 460.0 };
		CompareBarChart = new CartesianChart { Height = 420.0, Visibility = Visibility.Collapsed };
		CompareTableGrid = new Grid { Visibility = Visibility.Collapsed };

		var resultStack = new StackPanel { Spacing = 8.0 };
		resultStack.Children.Add(CompareRadarChart);
		resultStack.Children.Add(CompareBarChart);
		resultStack.Children.Add(CompareTableGrid);

		CompareResultArea = new ScrollViewer
		{
			Content = resultStack,
			Visibility = Visibility.Collapsed,
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
			HorizontalScrollMode = ScrollMode.Disabled
		};
		stack.Children.Add(CompareResultArea);

		CompareEmpty = new StackPanel
		{
			Visibility = Visibility.Visible,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			Spacing = 8.0,
			Margin = new Thickness(0.0, 40.0, 0.0, 0.0)
		};
		CompareEmpty.Children.Add(new FontIcon
		{
			Glyph = "\ue946",
			FontSize = 28.0,
			Foreground = dimText
		});
		CompareEmpty.Children.Add(new TextBlock
		{
			Text = "选择报告进行对比（最多6个）",
			FontSize = 14.0,
			Foreground = dimText,
			HorizontalAlignment = HorizontalAlignment.Center
		});
		stack.Children.Add(CompareEmpty);

		return new PivotItem
		{
			Header = "跑分对比",
			Content = stack
		};
	}

	private void AddCompareRow()
	{
		int idx = CompareCombos.Count + 1;
		var row = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Spacing = 6.0,
			VerticalAlignment = VerticalAlignment.Center
		};
		var combo = new ComboBox
		{
			MinWidth = 260.0,
			PlaceholderText = $"选择报告{idx}",
			HorizontalAlignment = HorizontalAlignment.Stretch,
			ItemsSource = _allReports
		};
		CompareCombos.Add(combo);
		row.Children.Add(combo);

		if (CompareCombos.Count > 2)
		{
			var removeBtn = new Button
			{
				Content = new FontIcon { Glyph = "\uE711", FontSize = 12.0 },
				CornerRadius = new CornerRadius(8.0),
				Padding = new Thickness(8.0, 4.0, 8.0, 4.0),
				Tag = combo
			};
			removeBtn.Click += (s, e) =>
			{
				if (CompareCombos.Count <= 2) return;
				var target = (ComboBox)((Button)s).Tag;
				CompareCombos.Remove(target);
				RebuildComparePanel();
			};
			row.Children.Add(removeBtn);
		}

		CompareComboPanel.Children.Add(row);
	}

	private void RebuildComparePanel()
	{
		var selected = CompareCombos.Select(c => c.SelectedItem as BenchmarkReportEntry).ToList();
		CompareComboPanel.Children.Clear();
		CompareCombos.Clear();
		for (int i = 0; i < Math.Max(selected.Count, 2); i++)
		{
			AddCompareRow();
			if (i < selected.Count && selected[i] != null)
			{
				CompareCombos[i].SelectedItem = selected[i];
			}
		}
		foreach (var combo in CompareCombos)
		{
			combo.ItemsSource = _allReports;
		}
	}

	private PivotItem BuildMyHistoryPivotItem(SolidColorBrush cardBg, Color borderColor, SolidColorBrush dimText)
	{
		Grid grid = new();
		grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1.0, GridUnitType.Star) });

		MyHistoryLoginHint = new TextBlock
		{
			Text = "",
			FontSize = 14.0,
			Foreground = dimText,
			Margin = new Thickness(0.0, 0.0, 0.0, 8.0)
		};
		grid.Children.Add(MyHistoryLoginHint);
		Grid.SetRow(MyHistoryLoginHint, 0);

		MyHistoryProgress = new ProgressBar
		{
			Visibility = Visibility.Collapsed,
			IsIndeterminate = true,
			Margin = new Thickness(0.0, 4.0, 0.0, 4.0)
		};
		grid.Children.Add(MyHistoryProgress);
		Grid.SetRow(MyHistoryProgress, 1);

		MyHistoryChart = new CartesianChart
		{
			Height = 280.0
		};
		MyHistoryList = new ListView
		{
			ItemTemplate = BuildReportItemTemplate()
		};
		MyHistoryList.SelectionChanged += MyHistoryList_SelectionChanged;
		DeleteMyReportBtn = new Button
		{
			Content = new StackPanel
			{
				Orientation = Orientation.Horizontal,
				Spacing = 8.0,
				Children =
				{
					(UIElement)new FontIcon { Glyph = "\ue74d", FontSize = 14.0 },
					(UIElement)new TextBlock { Text = "删除选中报告", FontSize = 14.0 }
				}
			},
			CornerRadius = new CornerRadius(8.0),
			Padding = new Thickness(12.0, 8.0, 12.0, 8.0),
			Visibility = Visibility.Collapsed,
			Margin = new Thickness(0.0, 8.0, 0.0, 0.0)
		};
		DeleteMyReportBtn.Click += DeleteMyReportBtn_Click;

		StackPanel historyContent = new() { Spacing = 8.0 };
		historyContent.Children.Add(MyHistoryChart);
		historyContent.Children.Add(MyHistoryList);
		historyContent.Children.Add(DeleteMyReportBtn);

		MyHistoryArea = new ScrollViewer
		{
			Content = historyContent,
			Visibility = Visibility.Collapsed,
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
			HorizontalScrollMode = ScrollMode.Disabled
		};
		grid.Children.Add(MyHistoryArea);
		Grid.SetRow(MyHistoryArea, 2);

		MyHistoryEmpty = new StackPanel
		{
			Visibility = Visibility.Collapsed,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center,
			Spacing = 8.0
		};
		MyHistoryEmpty.Children.Add(new FontIcon
		{
			Glyph = "\ue946",
			FontSize = 28.0,
			Foreground = dimText
		});
		MyHistoryEmpty.Children.Add(new TextBlock
		{
			Text = "暂无上传记录",
			FontSize = 14.0,
			Foreground = dimText,
			HorizontalAlignment = HorizontalAlignment.Center
		});
		grid.Children.Add(MyHistoryEmpty);
		Grid.SetRow(MyHistoryEmpty, 2);

		return new PivotItem
		{
			Header = "我的记录",
			Content = grid
		};
	}

	private DataTemplate BuildReportItemTemplate()
	{
		return (DataTemplate)Microsoft.UI.Xaml.Markup.XamlReader.Load(
			@"<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
							xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'
							xmlns:m='using:TubaWinUi3.Models'>
				<Grid Padding='4,8' ColumnSpacing='12'>
					<Grid.ColumnDefinitions>
						<ColumnDefinition Width='*'/>
						<ColumnDefinition Width='Auto'/>
						<ColumnDefinition Width='Auto'/>
					</Grid.ColumnDefinitions>
					<StackPanel Spacing='2'>
						<TextBlock FontSize='14' FontWeight='SemiBold'>
							<Run Text='@'/><Run Text='{Binding Author}'/>
						</TextBlock>
						<TextBlock FontSize='12' Foreground='{ThemeResource TextFillColorSecondaryBrush}'>
							<Run Text='{Binding CpuName}'/><Run Text=' | '/><Run Text='{Binding GpuName}'/>
						</TextBlock>
					</StackPanel>
					<StackPanel Grid.Column='1' Spacing='2' HorizontalAlignment='Right'>
						<TextBlock Text='{Binding GamingScore}' FontSize='14' FontWeight='SemiBold' HorizontalAlignment='Right'/>
						<TextBlock Text='游戏' FontSize='12' Foreground='{ThemeResource TextFillColorTertiaryBrush}' HorizontalAlignment='Right'/>
					</StackPanel>
					<StackPanel Grid.Column='2' Spacing='2' HorizontalAlignment='Right' Margin='12,0,0,0'>
						<TextBlock Text='{Binding OfficeScore}' FontSize='14' FontWeight='SemiBold' HorizontalAlignment='Right'/>
						<TextBlock Text='办公' FontSize='12' Foreground='{ThemeResource TextFillColorTertiaryBrush}' HorizontalAlignment='Right'/>
					</StackPanel>
				</Grid>
			</DataTemplate>");
	}

	private async void OnLoaded(object sender, RoutedEventArgs e)
	{
		if (!_loaded)
		{
			_loaded = true;
			await LoadDataAsync();
		}
	}

	private async void RefreshButton_Click(object sender, RoutedEventArgs e)
	{
		BenchmarkCloudService.InvalidateCache();
		await LoadDataAsync();
	}

	private void SourceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		var newSource = SourceCombo.SelectedIndex == 1 ? LeaderboardSource.GitCode : LeaderboardSource.GitHub;
		if (BenchmarkCloudService.CurrentSource != newSource)
		{
			BenchmarkCloudService.CurrentSource = newSource;
			_ = LoadDataAsync();
		}
	}

	private async Task LoadDataAsync()
	{
		LeaderboardProgress.Visibility = Visibility.Visible;
		LeaderboardEmpty.Visibility = Visibility.Collapsed;
		LoadMoreProgress.Visibility = Visibility.Collapsed;
		LoadMoreText.Visibility = Visibility.Collapsed;
		try
		{
			_allReports = await BenchmarkCloudService.GetAllReportsAsync(CancellationToken.None);
			ReportCountText.Text = $"{_allReports.Count} 份报告 · {BenchmarkCloudService.CurrentSourceName}";
			await RefreshLeaderboardAsync();
			await LoadCompareCombos();
			await LoadSameHardware();
			await LoadMyHistory();
			LeaderboardList.Visibility = _allReports.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
		}
		catch (Exception ex)
		{
			LeaderboardList.Visibility = Visibility.Collapsed;
			LeaderboardEmpty.Visibility = Visibility.Visible;
			var errorDetails = ex.Message;
			if (ex.InnerException != null)
				errorDetails += "\n" + ex.InnerException.Message;
			LeaderboardEmptyText.Text = errorDetails;
		}
		finally
		{
			LeaderboardProgress.Visibility = Visibility.Collapsed;
		}
	}

	private void SortByCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (_loaded)
		{
			_ = RefreshLeaderboardAsync();
		}
	}

	private void Filter_QuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
	{
		_ = RefreshLeaderboardAsync();
	}

	private async Task RefreshLeaderboardAsync()
	{
		string sortBy = SortByCombo.SelectedIndex switch
		{
			1 => "office", 
			2 => "cpu", 
			3 => "gpu", 
			4 => "disk", 
			5 => "browser", 
			_ => "gaming", 
		};
		_currentPage = -1;
		_hasMorePages = false;
		_allLeaderboardEntries.Clear();
		LoadMoreProgress.Visibility = Visibility.Collapsed;
		LoadMoreText.Visibility = Visibility.Collapsed;

		string filterText = CpuFilterBox.Text;

		try
		{
			_allLeaderboardEntries = await BenchmarkCloudService.GetLeaderboardAsync(sortBy, filterText, null, CancellationToken.None);
			_currentPage = 0;
			var firstPage = BenchmarkCloudService.GetLeaderboardPageSlice(_allLeaderboardEntries, _currentPage);
			_leaderboard.Clear();
			foreach (var entry in firstPage)
			{
				_leaderboard.Add(entry);
			}
			_hasMorePages = _leaderboard.Count < _allLeaderboardEntries.Count;
			LeaderboardList.ItemsSource = _leaderboard;
			LeaderboardList.Visibility = ((_leaderboard.Count <= 0) ? Visibility.Collapsed : Visibility.Visible);
			LeaderboardEmpty.Visibility = ((_leaderboard.Count != 0) ? Visibility.Collapsed : Visibility.Visible);
			HookLeaderboardScrollViewer();
			UpdateLoadMoreUI(_allLeaderboardEntries.Count);
		}
		catch (Exception ex)
		{
			LeaderboardList.Visibility = Visibility.Collapsed;
			LeaderboardEmpty.Visibility = Visibility.Visible;
			var errorDetails = ex.Message;
			if (ex.InnerException != null)
				errorDetails += "\n" + ex.InnerException.Message;
			LeaderboardEmptyText.Text = errorDetails;
		}
	}

	private async Task LoadMoreLeaderboardAsync()
	{
		if (_isLoadingMore || !_hasMorePages) return;
		_isLoadingMore = true;
		LoadMoreProgress.Visibility = Visibility.Visible;

		try
		{
			await Task.Yield();
			int nextPage = _currentPage + 1;
			var newEntries = BenchmarkCloudService.GetLeaderboardPageSlice(_allLeaderboardEntries, nextPage);
			if (newEntries.Count == 0)
			{
				_hasMorePages = false;
				return;
			}
			_currentPage = nextPage;
			_hasMorePages = _leaderboard.Count + newEntries.Count < _allLeaderboardEntries.Count;
			foreach (var entry in newEntries)
			{
				_leaderboard.Add(entry);
			}
			UpdateLoadMoreUI(_allLeaderboardEntries.Count);
		}
		catch
		{
		}
		finally
		{
			_isLoadingMore = false;
			LoadMoreProgress.Visibility = Visibility.Collapsed;
		}
	}

	private void UpdateLoadMoreUI(int totalEntries)
	{
		int loaded = _leaderboard.Count;
		if (_hasMorePages)
		{
			LoadMoreText.Text = $"已加载 {loaded} / {totalEntries} 条，继续滚动加载更多";
			LoadMoreText.Visibility = Visibility.Visible;
		}
		else if (loaded > 0)
		{
			LoadMoreText.Text = $"已全部加载，共 {totalEntries} 条";
			LoadMoreText.Visibility = Visibility.Visible;
		}
		else
		{
			LoadMoreText.Visibility = Visibility.Collapsed;
		}
	}

	private async void LeaderboardScrollViewer_ViewChanged(object? sender, ScrollViewerViewChangedEventArgs e)
	{
		if (!_hasMorePages || _isLoadingMore || _currentPage < 0) return;
		if (sender is not ScrollViewer sv) return;
		if (sv.VerticalOffset >= sv.ScrollableHeight - 200)
		{
			await LoadMoreLeaderboardAsync();
		}
	}

	private void LeaderboardList_Loaded(object sender, RoutedEventArgs e)
	{
		HookLeaderboardScrollViewer();
	}

	// ListView 初始为 Collapsed，首次 Loaded 时模板可能还没应用（拿不到内部 ScrollViewer），
	// 所以主动 ApplyTemplate 并在仍拿不到时等下一次布局完成后再试；
	// 列表变为可见后的数据刷新会再次调用本方法兜底。
	private void HookLeaderboardScrollViewer()
	{
		if (_leaderboardScrollViewer is not null) return;

		LeaderboardList.ApplyTemplate();
		var scrollViewer = FindDescendantScrollViewer(LeaderboardList);
		if (scrollViewer is null)
		{
			LeaderboardList.LayoutUpdated -= LeaderboardList_LayoutUpdated;
			LeaderboardList.LayoutUpdated += LeaderboardList_LayoutUpdated;
			return;
		}
		_leaderboardScrollViewer = scrollViewer;
		scrollViewer.ViewChanged += LeaderboardScrollViewer_ViewChanged;
	}

	private void LeaderboardList_LayoutUpdated(object? sender, object e)
	{
		LeaderboardList.LayoutUpdated -= LeaderboardList_LayoutUpdated;
		HookLeaderboardScrollViewer();
	}

	private static ScrollViewer? FindDescendantScrollViewer(DependencyObject root)
	{
		for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
		{
			var child = VisualTreeHelper.GetChild(root, i);
			if (child is ScrollViewer scrollViewer) return scrollViewer;
			var descendant = FindDescendantScrollViewer(child);
			if (descendant is not null) return descendant;
		}
		return null;
	}

	private void LeaderboardList_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (LeaderboardList.SelectedItem is BenchmarkLeaderboardEntry benchmarkLeaderboardEntry)
		{
			ShowReportDetailDialog(benchmarkLeaderboardEntry.Report);
			LeaderboardList.SelectedItem = null;
		}
	}

	private void SameHwList_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (SameHwList.SelectedItem is BenchmarkLeaderboardEntry benchmarkLeaderboardEntry)
		{
			ShowReportDetailDialog(benchmarkLeaderboardEntry.Report);
			SameHwList.SelectedItem = null;
		}
	}

	private async Task LoadCompareCombos()
	{
		foreach (var combo in CompareCombos)
		{
			combo.ItemsSource = _allReports;
		}
		if (CompareCombos.Count > 0 && _myReports.Count > 0)
		{
			CompareCombos[0].SelectedIndex = 0;
		}
		else if (CompareCombos.Count > 0 && _allReports.Count > 0)
		{
			CompareCombos[0].SelectedIndex = 0;
		}
	}

	private async void CompareButton_Click(object sender, RoutedEventArgs e)
	{
		var selected = CompareCombos
			.Select(c => c.SelectedItem as BenchmarkReportEntry)
			.Where(r => r != null)
			.Distinct()
			.OfType<BenchmarkReportEntry>()
		.ToList();
		if (selected.Count < 2)
		{
			await new ContentDialog
			{
				Title = "提示",
				Content = "请至少选择两个报告进行对比",
				CloseButtonText = "确定",
				XamlRoot = XamlRoot,
				RequestedTheme = ThemeService.CurrentElementTheme
			}.ShowAsync();
		}
		else
		{
			CompareEmpty.Visibility = Visibility.Collapsed;
			CompareResultArea.Visibility = Visibility.Visible;
			CompareViewTogglePanel.Visibility = Visibility.Visible;
			await RenderCompareViews(selected);
		}
	}

	private void CompareViewRadio_Checked(object sender, RoutedEventArgs e)
	{
		if (sender is RadioButton rb && rb.Tag is string tag)
		{
			CompareRadarChart.Visibility = tag == "radar" ? Visibility.Visible : Visibility.Collapsed;
			CompareBarChart.Visibility = tag == "bar" ? Visibility.Visible : Visibility.Collapsed;
			CompareTableGrid.Visibility = tag == "table" ? Visibility.Visible : Visibility.Collapsed;
		}
	}

	private List<BenchmarkReportEntry>? _lastCompareReports;

	private async Task RenderCompareViews(List<BenchmarkReportEntry> reports)
	{
		_lastCompareReports = reports;
		await RenderRadarChart(reports);
		await RenderBarChart(reports);
		RenderCompareTable(reports);
	}

	private Task RenderRadarChart(List<BenchmarkReportEntry> reports)
	{
		string[] axes = ["CPU单核", "CPU多核", "GPU渲染", "内存", "硬盘读", "硬盘写", "4K读", "4K写", "浏览器"];
		Func<BenchmarkReportEntry, int>[] getters =
		[
			r => r.CpuSingleCoreScore, r => r.CpuMultiCoreScore, r => r.GpuRenderScore,
			r => r.MemoryCapacityScore, r => r.DiskSeqReadScore, r => r.DiskSeqWriteScore,
			r => r.Disk4KReadScore, r => r.Disk4KWriteScore, r => r.BrowserTotalScore
		];
		var skiaColors = new SkiaSharp.SKColor[]
		{
			new SkiaSharp.SKColor(ThemeColors.Series1.R, ThemeColors.Series1.G, ThemeColors.Series1.B, 0x33),
			new SkiaSharp.SKColor(ThemeColors.AccentOrange.R, ThemeColors.AccentOrange.G, ThemeColors.AccentOrange.B, 0x33),
			new SkiaSharp.SKColor(ThemeColors.Series3.R, ThemeColors.Series3.G, ThemeColors.Series3.B, 0x33),
			new SkiaSharp.SKColor(ThemeColors.Series2.R, ThemeColors.Series2.G, ThemeColors.Series2.B, 0x33),
			new SkiaSharp.SKColor(ThemeColors.Series4.R, ThemeColors.Series4.G, ThemeColors.Series4.B, 0x33),
			new SkiaSharp.SKColor(ThemeColors.Neutral.R, ThemeColors.Neutral.G, ThemeColors.Neutral.B, 0x33)
		};
		var strokeColors = new[]
		{
			new SkiaSharp.SKColor(ThemeColors.Series1.R, ThemeColors.Series1.G, ThemeColors.Series1.B),
			new SkiaSharp.SKColor(ThemeColors.AccentOrange.R, ThemeColors.AccentOrange.G, ThemeColors.AccentOrange.B),
			new SkiaSharp.SKColor(ThemeColors.Series3.R, ThemeColors.Series3.G, ThemeColors.Series3.B),
			new SkiaSharp.SKColor(ThemeColors.Series2.R, ThemeColors.Series2.G, ThemeColors.Series2.B),
			new SkiaSharp.SKColor(ThemeColors.Series4.R, ThemeColors.Series4.G, ThemeColors.Series4.B),
			new SkiaSharp.SKColor(ThemeColors.Neutral.R, ThemeColors.Neutral.G, ThemeColors.Neutral.B)
		};

		var series = new List<ISeries>();
		for (int r = 0; r < reports.Count; r++)
		{
			var rep = reports[r];
			var values = getters.Select(g => (double)g(rep)).ToArray();
			series.Add(new PolarLineSeries<double>
			{
				Values = values,
				Name = GetCompareShortName(rep),
				Fill = new LiveChartsCore.SkiaSharpView.Painting.SolidColorPaint(skiaColors[r]),
				Stroke = new LiveChartsCore.SkiaSharpView.Painting.SolidColorPaint(strokeColors[r]) { StrokeThickness = 2 },
				GeometrySize = 4,
				GeometryStroke = new LiveChartsCore.SkiaSharpView.Painting.SolidColorPaint(strokeColors[r]) { StrokeThickness = 2 },
				GeometryFill = new LiveChartsCore.SkiaSharpView.Painting.SolidColorPaint(skiaColors[r]),
				IsClosed = true
			});
		}

		CompareRadarChart.Series = series;
		CompareRadarChart.AngleAxes = new List<PolarAxis>
		{
			new PolarAxis
			{
				Labels = axes,
				LabelsRotation = 0,
				MinStep = 1
			}
		};
		CompareRadarChart.RadiusAxes = new List<PolarAxis>
		{
			new PolarAxis
			{
				IsVisible = true
			}
		};

		return Task.CompletedTask;
	}

	private Task RenderBarChart(List<BenchmarkReportEntry> reports)
	{
		string[] labels = ["CPU单核", "CPU多核", "GPU渲染", "内存", "硬盘读", "硬盘写", "4K读", "4K写", "浏览器"];
		Func<BenchmarkReportEntry, int>[] getters =
		[
			r => r.CpuSingleCoreScore, r => r.CpuMultiCoreScore, r => r.GpuRenderScore,
			r => r.MemoryCapacityScore, r => r.DiskSeqReadScore, r => r.DiskSeqWriteScore,
			r => r.Disk4KReadScore, r => r.Disk4KWriteScore, r => r.BrowserTotalScore
		];
		var strokeColors = new[]
		{
			new SkiaSharp.SKColor(ThemeColors.Series1.R, ThemeColors.Series1.G, ThemeColors.Series1.B),
			new SkiaSharp.SKColor(ThemeColors.AccentOrange.R, ThemeColors.AccentOrange.G, ThemeColors.AccentOrange.B),
			new SkiaSharp.SKColor(ThemeColors.Series3.R, ThemeColors.Series3.G, ThemeColors.Series3.B),
			new SkiaSharp.SKColor(ThemeColors.Series2.R, ThemeColors.Series2.G, ThemeColors.Series2.B),
			new SkiaSharp.SKColor(ThemeColors.Series4.R, ThemeColors.Series4.G, ThemeColors.Series4.B),
			new SkiaSharp.SKColor(ThemeColors.Neutral.R, ThemeColors.Neutral.G, ThemeColors.Neutral.B)
		};

		var series = new List<ISeries>();
		for (int r = 0; r < reports.Count; r++)
		{
			var rep = reports[r];
			var values = getters.Select(g => (double)g(rep)).ToArray();
			series.Add(new ColumnSeries<double>
			{
				Values = values,
				Name = GetCompareShortName(rep),
				Fill = new LiveChartsCore.SkiaSharpView.Painting.SolidColorPaint(strokeColors[r]),
				Stroke = null
			});
		}

		CompareBarChart.Series = series;
		CompareBarChart.XAxes = new List<Axis>
		{
			new Axis
			{
				Labels = labels,
				LabelsRotation = 15,
				MinStep = 1
			}
		};
		CompareBarChart.YAxes = new List<Axis>
		{
			new Axis
			{
				MinLimit = 0
			}
		};

		return Task.CompletedTask;
	}

	private void RenderCompareTable(List<BenchmarkReportEntry> reports)
	{
		CompareTableGrid.Children.Clear();
		CompareTableGrid.ColumnDefinitions.Clear();
		CompareTableGrid.RowDefinitions.Clear();

		string[] labels = ["CPU单核", "CPU多核", "GPU渲染", "内存", "硬盘读", "硬盘写", "4K读", "4K写", "浏览器", "游戏总分", "办公总分"];
		Func<BenchmarkReportEntry, int>[] getters = [
			r => r.CpuSingleCoreScore, r => r.CpuMultiCoreScore, r => r.GpuRenderScore,
			r => r.MemoryCapacityScore, r => r.DiskSeqReadScore, r => r.DiskSeqWriteScore,
			r => r.Disk4KReadScore, r => r.Disk4KWriteScore, r => r.BrowserTotalScore,
			r => r.GamingScore, r => r.OfficeScore
		];

		CompareTableGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(100) });
		foreach (var _ in reports)
			CompareTableGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

		for (int row = 0; row <= labels.Length; row++)
		{
			CompareTableGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
		}

		var headerBg = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(40, ThemeColors.AccentBlue.R, ThemeColors.AccentBlue.G, ThemeColors.AccentBlue.B));
		var cellBg = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
		var altBg = new SolidColorBrush(ThemeColors.SubtleBg);

		var headerRow = new Border { Background = headerBg, Padding = new Thickness(8, 6, 8, 6) };
		headerRow.Child = new TextBlock { Text = "项目", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
		CompareTableGrid.Children.Add(headerRow);
		Grid.SetRow(headerRow, 0);
		Grid.SetColumn(headerRow, 0);

		for (int i = 0; i < reports.Count; i++)
		{
			var header = new Border { Background = headerBg, Padding = new Thickness(8, 6, 8, 6) };
			var txt = new TextBlock { Text = GetCompareShortName(reports[i]), FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
			ToolTipService.SetToolTip(txt, reports[i].CpuName + " | " + reports[i].GpuName);
			header.Child = txt;
			CompareTableGrid.Children.Add(header);
			Grid.SetRow(header, 0);
			Grid.SetColumn(header, i + 1);
		}

		for (int row = 0; row < labels.Length; row++)
		{
			var labelBg = row % 2 == 0 ? cellBg : altBg;
			var labelBorder = new Border { Background = labelBg, Padding = new Thickness(8, 4, 8, 4) };
			labelBorder.Child = new TextBlock { Text = labels[row], VerticalAlignment = VerticalAlignment.Center };
			CompareTableGrid.Children.Add(labelBorder);
			Grid.SetRow(labelBorder, row + 1);
			Grid.SetColumn(labelBorder, 0);

			int[] scores = reports.Select(getters[row]).ToArray();
			int maxScore = scores.Max();

			for (int col = 0; col < reports.Count; col++)
			{
				var cellBorder = new Border { Background = row % 2 == 0 ? cellBg : altBg, Padding = new Thickness(8, 4, 8, 4) };
				var score = scores[col];
				var txt = new TextBlock { Text = score.ToString(), VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
				if (score == maxScore && maxScore > 0)
				{
					txt.Foreground = new SolidColorBrush(ThemeColors.AccentGreen);
					txt.FontWeight = Microsoft.UI.Text.FontWeights.SemiBold;
				}
				cellBorder.Child = txt;
				CompareTableGrid.Children.Add(cellBorder);
				Grid.SetRow(cellBorder, row + 1);
				Grid.SetColumn(cellBorder, col + 1);
			}
		}
	}

	private static string GetCompareShortName(BenchmarkReportEntry rep)
	{
		return rep.Author + " · " + rep.CpuName.Split(' ').FirstOrDefault();
	}

	private async Task LoadSameHardware()
	{
		SameHwProgress.Visibility = Visibility.Visible;
		try
		{
			string text = "";
			string text2 = "";
			if (_myReports.Count > 0)
			{
				text = _myReports[0].CpuName;
				text2 = _myReports[0].GpuName;
			}
			else
			{
				List<PerformanceBenchmarkResult> list = PerformanceBenchmarkService.LoadHistory();
				if (list.Count > 0)
				{
					text = list[0].CpuName;
					text2 = list[0].GpuName;
				}
			}
			SameHwCpuText.Text = "CPU: " + text;
			SameHwGpuText.Text = "GPU: " + text2;
			if (string.IsNullOrWhiteSpace(text) && string.IsNullOrWhiteSpace(text2))
			{
				SameHwList.Visibility = Visibility.Collapsed;
				SameHwEmpty.Visibility = Visibility.Visible;
				return;
			}
			List<BenchmarkLeaderboardEntry> list2 = BenchmarkCloudService.ComputeSameHardwareLeaderboard(_allReports, text, text2);
			SameHwList.ItemsSource = list2;
			SameHwList.Visibility = ((list2.Count <= 0) ? Visibility.Collapsed : Visibility.Visible);
			SameHwEmpty.Visibility = ((list2.Count != 0) ? Visibility.Collapsed : Visibility.Visible);
		}
		finally
		{
			SameHwProgress.Visibility = Visibility.Collapsed;
		}
	}

	private async Task LoadMyHistory()
	{
		if (!GitHubAuthService.IsLoggedIn)
		{
			MyHistoryLoginHint.Text = "请先登录 GitHub 查看你的上传记录";
			MyHistoryArea.Visibility = Visibility.Collapsed;
			MyHistoryEmpty.Visibility = Visibility.Visible;
			return;
		}
		MyHistoryProgress.Visibility = Visibility.Visible;
		try
		{
			_myReports = await BenchmarkCloudService.GetMyReportsAsync(CancellationToken.None);
			MyHistoryLoginHint.Text = $"已登录，共 {_myReports.Count} 条上传记录";
			if (_myReports.Count == 0)
			{
				MyHistoryArea.Visibility = Visibility.Collapsed;
				MyHistoryEmpty.Visibility = Visibility.Visible;
				return;
			}
			MyHistoryArea.Visibility = Visibility.Visible;
			MyHistoryEmpty.Visibility = Visibility.Collapsed;
			MyHistoryList.ItemsSource = _myReports;
			await RenderHistoryChart(_myReports);
		}
		finally
		{
			MyHistoryProgress.Visibility = Visibility.Collapsed;
		}
	}

	private void MyHistoryList_SelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		DeleteMyReportBtn.Visibility = ((MyHistoryList.SelectedItem == null) ? Visibility.Collapsed : Visibility.Visible);
	}

	private async void DeleteMyReportBtn_Click(object sender, RoutedEventArgs e)
	{
		if (MyHistoryList.SelectedItem is BenchmarkReportEntry report)
		{
			await DeleteReportAsync(report);
		}
	}

	private Task RenderHistoryChart(List<BenchmarkReportEntry> reports)
	{
		var sorted = reports.OrderBy(r => r.SubmittedAt).ToList();
		var dateLabels = sorted.Select(r => r.SubmittedAt.LocalDateTime.ToString("MM/dd")).ToArray();

		var gamingSeries = new LineSeries<double>
		{
			Values = sorted.Select(r => (double)r.GamingScore).ToArray(),
			Name = "游戏性能",
			Fill = null,
			Stroke = new LiveChartsCore.SkiaSharpView.Painting.SolidColorPaint(new SkiaSharp.SKColor(ThemeColors.Series1.R, ThemeColors.Series1.G, ThemeColors.Series1.B)) { StrokeThickness = 2.5f },
			GeometrySize = 6,
			GeometryStroke = new LiveChartsCore.SkiaSharpView.Painting.SolidColorPaint(new SkiaSharp.SKColor(ThemeColors.Series1.R, ThemeColors.Series1.G, ThemeColors.Series1.B)) { StrokeThickness = 2f },
			GeometryFill = new LiveChartsCore.SkiaSharpView.Painting.SolidColorPaint(new SkiaSharp.SKColor(ThemeColors.Series1.R, ThemeColors.Series1.G, ThemeColors.Series1.B)),
		};

		var officeSeries = new LineSeries<double>
		{
			Values = sorted.Select(r => (double)r.OfficeScore).ToArray(),
			Name = "办公性能",
			Fill = null,
			Stroke = new LiveChartsCore.SkiaSharpView.Painting.SolidColorPaint(new SkiaSharp.SKColor(ThemeColors.AccentOrange.R, ThemeColors.AccentOrange.G, ThemeColors.AccentOrange.B)) { StrokeThickness = 2.5f },
			GeometrySize = 6,
			GeometryStroke = new LiveChartsCore.SkiaSharpView.Painting.SolidColorPaint(new SkiaSharp.SKColor(ThemeColors.AccentOrange.R, ThemeColors.AccentOrange.G, ThemeColors.AccentOrange.B)) { StrokeThickness = 2f },
			GeometryFill = new LiveChartsCore.SkiaSharpView.Painting.SolidColorPaint(new SkiaSharp.SKColor(ThemeColors.AccentOrange.R, ThemeColors.AccentOrange.G, ThemeColors.AccentOrange.B)),
		};

		MyHistoryChart.Series = new List<ISeries> { gamingSeries, officeSeries };
		MyHistoryChart.XAxes = new List<Axis>
		{
			new Axis
			{
				Labels = dateLabels,
				LabelsRotation = 0,
				MinStep = 1
			}
		};
		MyHistoryChart.YAxes = new List<Axis>
		{
			new Axis
			{
				MinLimit = 0
			}
		};

		return Task.CompletedTask;
	}

	private async void ShowReportDetailDialog(BenchmarkReportEntry report)
	{
		// 详情可能要走网络加载，期间用户再点卡片会再次进入本方法并弹出第二个
		// ContentDialog（COMException: Only a single ContentDialog can be open at any time），
		// 因此加载与弹窗期间禁止再次点击卡片
		if (_isReportDetailBusy) return;
		_isReportDetailBusy = true;
		LeaderboardList.IsEnabled = false;
		SameHwList.IsEnabled = false;
		try
		{
			await ShowReportDetailDialogCoreAsync(report);
		}
		finally
		{
			_isReportDetailBusy = false;
			LeaderboardList.IsEnabled = true;
			SameHwList.IsEnabled = true;
		}
	}

	private async Task ShowReportDetailDialogCoreAsync(BenchmarkReportEntry report)
	{
		// 新 leaderboard.json 的条目只含摘要，硬件详情拆到了详情文件：
		// 摘要缺 OS 字段时按需加载补全（旧格式字段齐全则直接跳过）
		if (string.IsNullOrWhiteSpace(report.OsName) && !string.IsNullOrWhiteSpace(report.DetailsPath))
		{
			var detail = await BenchmarkCloudService.GetReportDetailAsync(report);
			if (detail is not null) report = detail;
		}

		StackPanel stackPanel = new StackPanel
		{
			Spacing = 8.0
		};
		stackPanel.Children.Add(new TextBlock
		{
			Text = "CPU: " + report.CpuName,
			Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
		});
		stackPanel.Children.Add(new TextBlock
		{
			Text = "GPU: " + report.GpuName,
			Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
		});
		stackPanel.Children.Add(new TextBlock
		{
			Text = "主板: " + report.MotherboardName,
			Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
		});
		stackPanel.Children.Add(new TextBlock
		{
			Text = "内存: " + report.MemoryInfo,
			Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
		});
		stackPanel.Children.Add(new TextBlock
		{
			Text = "硬盘: " + report.DiskInfo,
			Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
		});
		stackPanel.Children.Add(new TextBlock
		{
			Text = "显示器: " + report.DisplayInfo,
			Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
		});
		stackPanel.Children.Add(new TextBlock
		{
			Text = "OS: " + report.OsName,
			Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
		});
		stackPanel.Children.Add(new Border
		{
			Height = 1.0,
			Background = new SolidColorBrush(ThemeColors.Separator),
			Margin = new Thickness(0.0, 4.0, 0.0, 4.0)
		});
		stackPanel.Children.Add(new TextBlock
		{
			Text = $"游戏性能: {report.GamingScore} ({report.GamingGrade})",
			FontWeight = FontWeights.SemiBold
		});
		stackPanel.Children.Add(new TextBlock
		{
			Text = $"办公性能: {report.OfficeScore} ({report.OfficeGrade})",
			FontWeight = FontWeights.SemiBold
		});
		stackPanel.Children.Add(new Border
		{
			Height = 1.0,
			Background = new SolidColorBrush(ThemeColors.Separator),
			Margin = new Thickness(0.0, 4.0, 0.0, 4.0)
		});
		stackPanel.Children.Add(new TextBlock
		{
			Text = $"CPU单核: {report.CpuSingleCoreScore}",
			Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
		});
		stackPanel.Children.Add(new TextBlock
		{
			Text = $"CPU多核: {report.CpuMultiCoreScore}",
			Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
		});
		stackPanel.Children.Add(new TextBlock
		{
			Text = $"GPU渲染: {report.GpuRenderScore}",
			Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
		});
		stackPanel.Children.Add(new TextBlock
		{
			Text = $"内存: {report.MemoryCapacityScore}",
			Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
		});
		stackPanel.Children.Add(new TextBlock
		{
			Text = $"硬盘顺序读: {report.DiskSeqReadScore}",
			Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
		});
		stackPanel.Children.Add(new TextBlock
		{
			Text = $"硬盘顺序写: {report.DiskSeqWriteScore}",
			Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
		});
		stackPanel.Children.Add(new TextBlock
		{
			Text = $"硬盘4K读: {report.Disk4KReadScore}",
			Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
		});
		stackPanel.Children.Add(new TextBlock
		{
			Text = $"硬盘4K写: {report.Disk4KWriteScore}",
			Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
		});
		stackPanel.Children.Add(new TextBlock
		{
			Text = $"浏览器: {report.BrowserTotalScore}",
			Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
		});
		bool num = GitHubAuthService.IsLoggedIn && GitHubAuthService.GetToken() != null && _myReports.Any((BenchmarkReportEntry r) => r.Id == report.Id);
		ContentDialog contentDialog = new ContentDialog
		{
			Title = "@" + report.Author + " 的测试报告",
			Content = new ScrollViewer
			{
				Content = stackPanel,
				MaxHeight = 500.0
			},
			CloseButtonText = "关闭",
			XamlRoot = XamlRoot,
			RequestedTheme = ThemeService.CurrentElementTheme
		};
		if (num)
		{
			contentDialog.SecondaryButtonText = "删除此报告";
			if (await contentDialog.ShowAsync() == ContentDialogResult.Secondary)
			{
				await DeleteReportAsync(report);
			}
		}
		else
		{
			await contentDialog.ShowAsync();
		}
	}

	private async Task DeleteReportAsync(BenchmarkReportEntry report)
	{
		if (await new ContentDialog
		{
			Title = "确认删除",
			Content = $"确定要删除这份报告吗？\n\nCPU: {report.CpuName}\nGPU: {report.GpuName}\n游戏: {report.GamingScore} 办公: {report.OfficeScore}\n\n将通过 PR 删除，合并后生效。",
			PrimaryButtonText = "删除",
			CloseButtonText = "取消",
			XamlRoot = XamlRoot,
			RequestedTheme = ThemeService.CurrentElementTheme
		}.ShowAsync() != ContentDialogResult.Primary)
		{
			return;
		}
		ContentDialog progressDlg = new ContentDialog
		{
			Title = "正在删除",
			Content = new ProgressBar
			{
				IsIndeterminate = true
			},
			XamlRoot = XamlRoot,
			RequestedTheme = ThemeService.CurrentElementTheme
		};
		_ = progressDlg.ShowAsync();
		try
		{
			Progress<string> progress = new Progress<string>(delegate(string msg)
			{
				DispatcherQueue.TryEnqueue(delegate
				{
					progressDlg.Content = new StackPanel
					{
						Spacing = 8.0,
						Children = 
						{
							(UIElement)new TextBlock
							{
								Text = msg
							},
							(UIElement)new ProgressBar
							{
								IsIndeterminate = true
							}
						}
					};
				});
			});
			string prUrl = await BenchmarkCloudService.DeleteReportAsync(report, progress, CancellationToken.None);
			progressDlg.Hide();
			if (await new ContentDialog
			{
				Title = "删除请求已提交",
				Content = "报告删除 PR 已创建，合并后将从排行榜移除。\n\nPR 链接：" + prUrl,
				PrimaryButtonText = "打开 PR",
				CloseButtonText = "关闭",
				XamlRoot = XamlRoot,
				RequestedTheme = ThemeService.CurrentElementTheme
			}.ShowAsync() == ContentDialogResult.Primary)
			{
				await Launcher.LaunchUriAsync(new Uri(prUrl));
			}
			BenchmarkCloudService.InvalidateCache();
			await LoadDataAsync();
		}
		catch (Exception ex)
		{
			progressDlg.Hide();
			await new ContentDialog
			{
				Title = "删除失败",
				Content = ex.Message,
				CloseButtonText = "确定",
				XamlRoot = XamlRoot,
				RequestedTheme = ThemeService.CurrentElementTheme
			}.ShowAsync();
		}
	}
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Microsoft.UI.Text;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using TubaWinUi3.Models;
using TubaWinUi3.Services;
using Windows.Graphics;
using Windows.UI;

namespace TubaWinUi3.Pages;

/// <summary>
/// 性能测试历史回顾窗口：打开即自动展示最近一次测试的完整得分明细，
/// 左侧为历史时间线列表，点击任意记录切换右侧详情，并支持与上一次成绩的对比、
/// 载回主页、单条删除与清空全部。
/// </summary>
public sealed class BenchmarkHistoryWindow : Window
{
	private readonly List<PerformanceBenchmarkResult> _entries; // 时间正序（旧→新），与 BenchmarkHistory.json 的顺序一致
	private readonly Action<PerformanceBenchmarkResult> _loadToMainPage;
	private readonly Dictionary<string, string> _heatmapCache = new();
	private int _heatmapReqId;
	private int _selectedIndex = -1; // 指向 _entries 的下标
	private bool _clearArmed;

	private ListView _list = null!;
	private TextBlock _listHeader = null!;
	private StackPanel _detailPanel = null!;
	private Button _loadBtn = null!;
	private Button _deleteBtn = null!;
	private Button _clearBtn = null!;

	private static readonly Color AccentBlue = Color.FromArgb(byte.MaxValue, 0, 99, 177);
	private static readonly Color ColorS = Color.FromArgb(byte.MaxValue, 74, 222, 128);
	private static readonly Color ColorAPlus = Color.FromArgb(byte.MaxValue, 34, 197, 94);
	private static readonly Color ColorA = Color.FromArgb(byte.MaxValue, 0, 99, 177);
	private static readonly Color ColorBPlus = Color.FromArgb(byte.MaxValue, 251, 191, 36);
	private static readonly Color ColorB = Color.FromArgb(byte.MaxValue, 251, 146, 60);
	private static readonly Color ColorC = Color.FromArgb(byte.MaxValue, 248, 113, 113);
	private static readonly Color ColorD = Color.FromArgb(byte.MaxValue, 220, 38, 38);
	private static readonly Color UpColor = Color.FromArgb(byte.MaxValue, 34, 197, 94);
	private static readonly Color DownColor = Color.FromArgb(byte.MaxValue, 248, 113, 113);
	private static readonly Color ColorsGray = Color.FromArgb(byte.MaxValue, 128, 128, 128);

	public BenchmarkHistoryWindow(List<PerformanceBenchmarkResult> entriesChronological, Action<PerformanceBenchmarkResult> loadToMainPage)
	{
		_entries = entriesChronological;
		_loadToMainPage = loadToMainPage;

		Title = "历史对比 - 性能测试记录";
		AppWindow.Title = Title;
		try { AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory, "Assets", "AppIcon.ico")); } catch { }

		var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
		var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hwnd);
		var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
		var screenArea = displayArea.WorkArea;
		int width = Math.Min(1000, (int)(screenArea.Width * 0.72));
		int height = Math.Min(720, (int)(screenArea.Height * 0.78));
		AppWindow.Resize(new SizeInt32(width, height));
		AppWindow.Move(new PointInt32((screenArea.Width - width) / 2, (screenArea.Height - height) / 2));

		Content = BuildRoot();
		if (Content is FrameworkElement root)
			root.RequestedTheme = ThemeService.CurrentElementTheme;

		// 打开即回顾最近一次测试结果
		SelectEntry(_entries.Count - 1);
	}

	private Grid BuildRoot()
	{
		Grid root = new()
		{
			Padding = new Thickness(16.0, 14.0, 16.0, 14.0),
			RowSpacing = 10.0,
			RowDefinitions =
			{
				new RowDefinition { Height = GridLength.Auto },
				new RowDefinition { Height = new GridLength(1.0, GridUnitType.Star) },
				new RowDefinition { Height = GridLength.Auto }
			}
		};

		StackPanel header = new() { Spacing = 2.0 };
		header.Children.Add(new TextBlock
		{
			Text = "历史对比",
			FontSize = 20.0,
			FontWeight = FontWeights.Bold
		});
		header.Children.Add(new TextBlock
		{
			Text = "打开即显示最近一次测试结果，点击左侧记录查看任意一次的完整成绩，并可载回主页导出报告。",
			FontSize = 12.0,
			TextWrapping = TextWrapping.Wrap,
			Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"]
		});
		root.Children.Add(header);
		Grid.SetRow(header, 0);

		// 左：历史时间线
		_listHeader = new TextBlock
		{
			Text = $"测试记录 · {_entries.Count} 条",
			FontSize = 13.0,
			FontWeight = FontWeights.Bold,
			Margin = new Thickness(0.0, 0.0, 0.0, 8.0)
		};
		_list = new ListView
		{
			SelectionMode = ListViewSelectionMode.Single
		};
		_list.SelectionChanged += OnListSelectionChanged;
		StackPanel leftPanel = new() { Spacing = 0.0 };
		leftPanel.Children.Add(_listHeader);
		leftPanel.Children.Add(_list);
		Border leftCard = MakeCard(leftPanel);

		// 右：详情面板
		_detailPanel = new StackPanel { Spacing = 12.0 };
		ScrollViewer detailScroll = new()
		{
			Content = _detailPanel,
			VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
			HorizontalScrollMode = ScrollMode.Disabled
		};
		Border rightCard = MakeCard(detailScroll);

		Grid columns = new()
		{
			ColumnSpacing = 12.0,
			ColumnDefinitions =
			{
				new ColumnDefinition { Width = new GridLength(300.0) },
				new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) }
			}
		};
		columns.Children.Add(leftCard);
		Grid.SetColumn(leftCard, 0);
		columns.Children.Add(rightCard);
		Grid.SetColumn(rightCard, 1);
		root.Children.Add(columns);
		Grid.SetRow(columns, 1);

		// 底部操作栏
		_loadBtn = MakeActionButton("\ue8f6", "载入到主页");
		_loadBtn.Style = (Style)Application.Current.Resources["AccentButtonStyle"];
		_loadBtn.Click += (_, _) => LoadSelectedToMainPage();
		_deleteBtn = MakeActionButton("\ue74d", "删除此记录");
		_deleteBtn.Click += (_, _) => DeleteSelected();
		_clearBtn = MakeActionButton("\ued60", "清空全部");
		_clearBtn.Click += (_, _) => OnClearAllClick();

		Button closeBtn = MakeActionButton("\ue711", "关闭");
		closeBtn.Click += (_, _) => Close();

		Grid btnBar = new()
		{
			ColumnDefinitions =
			{
				new ColumnDefinition { Width = GridLength.Auto },
				new ColumnDefinition { Width = GridLength.Auto },
				new ColumnDefinition { Width = GridLength.Auto },
				new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) },
				new ColumnDefinition { Width = GridLength.Auto }
			}
		};
		btnBar.Children.Add(_loadBtn);
		Grid.SetColumn(_loadBtn, 0);
		btnBar.Children.Add(_deleteBtn);
		Grid.SetColumn(_deleteBtn, 1);
		btnBar.Children.Add(_clearBtn);
		Grid.SetColumn(_clearBtn, 2);
		btnBar.Children.Add(closeBtn);
		Grid.SetColumn(closeBtn, 4);
		root.Children.Add(btnBar);
		Grid.SetRow(btnBar, 2);

		RebuildList(-1);
		return root;
	}

	private static Border MakeCard(FrameworkElement content)
	{
		return new Border
		{
			Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
			BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
			BorderThickness = new Thickness(1.0),
			CornerRadius = new CornerRadius(8.0),
			Padding = new Thickness(12.0),
			Child = content
		};
	}

	private static Button MakeActionButton(string glyph, string text)
	{
		return new Button
		{
			Content = new StackPanel
			{
				Orientation = Orientation.Horizontal,
				Spacing = 6.0,
				Children =
				{
					(UIElement)new FontIcon { Glyph = glyph, FontSize = 13.0 },
					(UIElement)new TextBlock { Text = text, FontSize = 13.0 }
				}
			},
			CornerRadius = new CornerRadius(6.0),
			Padding = new Thickness(12.0, 7.0, 12.0, 7.0)
		};
	}

	// ---------- 左侧列表 ----------

	private void RebuildList(int selectChronologicalIdx)
	{
		_list.Items.Clear();
		// 最新记录排最前
		for (int i = _entries.Count - 1; i >= 0; i--)
		{
			var r = _entries[i];
			_list.Items.Add(new ListViewItem
			{
				Tag = i,
				Padding = new Thickness(10.0, 8.0, 10.0, 8.0),
				Content = new StackPanel
				{
					Spacing = 3.0,
					Children =
					{
						(UIElement)new TextBlock
						{
							Text = r.TestTime.ToString("yyyy-MM-dd HH:mm"),
							FontSize = 13.0,
							FontWeight = FontWeights.Bold
						},
						(UIElement)new TextBlock
						{
							Text = $"游戏 {r.GamingScore} · 办公 {r.OfficeScore}" + (r.Win.FinalScore > 0 ? $" · Win {r.Win.FinalScore}" : ""),
							FontSize = 11.0,
							Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
						}
					}
				}
			});
		}
		_listHeader.Text = _entries.Count == 0 ? "测试记录" : $"测试记录 · {_entries.Count} 条";
		if (selectChronologicalIdx >= 0 && _entries.Count > 0)
		{
			int clamped = Math.Min(selectChronologicalIdx, _entries.Count - 1);
			// 列表展示为倒序，换算成 ListView 下标
			_list.SelectedIndex = _entries.Count - 1 - clamped;
		}
		else if (_entries.Count == 0)
		{
			_loadBtn.IsEnabled = false;
			_deleteBtn.IsEnabled = false;
		}
	}

	private void OnListSelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (_list.SelectedIndex < 0 || _list.SelectedIndex >= _list.Items.Count) return;
		if (_list.SelectedItem is ListViewItem item && item.Tag is int idx)
			ShowDetail(idx);
	}

	private void SelectEntry(int chronologicalIdx)
	{
		if (_entries.Count == 0) return;
		int clamped = Math.Clamp(chronologicalIdx, 0, _entries.Count - 1);
		_list.SelectedIndex = _entries.Count - 1 - clamped; // 触发 SelectionChanged → ShowDetail
	}

	// ---------- 右侧详情 ----------

	private void ShowDetail(int chronologicalIdx)
	{
		if (chronologicalIdx < 0 || chronologicalIdx >= _entries.Count)
		{
			ShowEmptyDetail("该记录不存在");
			return;
		}
		_selectedIndex = chronologicalIdx;
		_loadBtn.IsEnabled = true;
		_deleteBtn.IsEnabled = true;
		DisarmClear();

		var r = _entries[chronologicalIdx];
		_detailPanel.Children.Clear();

		// 标题行：时间 + 模式 + 总耗时
		string subtitle = $"记录 {chronologicalIdx + 1} / {_entries.Count}";
		if (!string.IsNullOrEmpty(r.DurationMode)) subtitle += $" · {r.DurationMode} 模式";
		if (r.TotalDuration > TimeSpan.Zero) subtitle += $" · 总耗时 {r.TotalDuration:mm\\mss\\s}";
		_detailPanel.Children.Add(new StackPanel
		{
			Spacing = 2.0,
			Children =
			{
				(UIElement)new TextBlock
				{
					Text = r.TestTime.ToString("yyyy-MM-dd HH:mm:ss"),
					FontSize = 18.0,
					FontWeight = FontWeights.Bold
				},
				(UIElement)new TextBlock
				{
					Text = subtitle,
					FontSize = 12.0,
					Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"]
				}
			}
		});

		// 硬件环境
		var hw = new List<string>();
		if (!string.IsNullOrWhiteSpace(r.CpuName)) hw.Add("CPU " + r.CpuName);
		if (!string.IsNullOrWhiteSpace(r.GpuName)) hw.Add("GPU " + r.GpuName);
		if (!string.IsNullOrWhiteSpace(r.MemoryInfo)) hw.Add(r.MemoryInfo);
		if (!string.IsNullOrWhiteSpace(r.OsName)) hw.Add(r.OsName);
		if (hw.Count > 0)
		{
			_detailPanel.Children.Add(new TextBlock
			{
				Text = string.Join("　", hw),
				FontSize = 11.0,
				TextWrapping = TextWrapping.Wrap,
				Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
			});
		}

		// 三大总分卡片（含与上一次的对比）
		Grid scores = new()
		{
			ColumnSpacing = 10.0,
			ColumnDefinitions =
			{
				new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) },
				new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) },
				new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) }
			}
		};
		var prev = chronologicalIdx > 0 ? _entries[chronologicalIdx - 1] : null;
		Border gamingCard = BuildScoreCard("游戏性能", r.GamingScore, r.GamingGrade, prev?.GamingScore);
		scores.Children.Add(gamingCard);
		Grid.SetColumn(gamingCard, 0);
		Border officeCard = BuildScoreCard("办公性能", r.OfficeScore, r.OfficeGrade, prev?.OfficeScore);
		scores.Children.Add(officeCard);
		Grid.SetColumn(officeCard, 1);
		Border winCard = r.Win.FinalScore > 0
			? BuildScoreCard("Win性能", r.Win.FinalScore, r.Win.Grade, prev?.Win.FinalScore)
			: BuildNotTestedCard("Win性能");
		scores.Children.Add(winCard);
		Grid.SetColumn(winCard, 2);
		_detailPanel.Children.Add(scores);

		// 分区明细
		StackPanel sections = new() { Spacing = 10.0 };

		if (r.Cpu.SingleCoreScore > 0 || r.Cpu.MultiCoreScore > 0 || r.Cpu.LatencyScore > 0)
		{
			var rows = new List<(string, string, int)>();
			if (r.Cpu.SingleCoreScore > 0) rows.Add(("单核", "", r.Cpu.SingleCoreScore));
			if (r.Cpu.MultiCoreScore > 0) rows.Add(("多核", "", r.Cpu.MultiCoreScore));
			if (r.Cpu.LatencyScore > 0)
			{
				string detail = r.Cpu.LatencyMatrix != null && r.Cpu.LatencyMatrix.AverageNs > 0
					? $"平均 {r.Cpu.LatencyMatrix.AverageNs:F0} ns"
					: "";
				rows.Add(("核间延迟", detail, r.Cpu.LatencyScore));
			}
			sections.Children.Add(BuildSectionCard("\ueea1", "CPU 性能", rows));
		}

		if (r.Gpu.RenderScore > 0)
		{
			var rows = new List<(string, string, int)>
			{
				("渲染性能", "", r.Gpu.RenderScore),
				("FurMark分数", $"平均 {r.Gpu.AvgFps:F0} FPS", r.Gpu.FurMarkScore),
				("FPS范围", $"最低 {r.Gpu.MinFps:F0} / 最高 {r.Gpu.MaxFps:F0}", (int)r.Gpu.MinFps)
			};
			sections.Children.Add(BuildSectionCard("\ue950", "GPU 性能", rows));
		}

		if (r.Memory.TotalCapacityGB > 0)
		{
			sections.Children.Add(BuildSectionCard("\ue90f", "内存性能",
				[("容量", $"{r.Memory.TotalCapacityGB:F0} GB", r.Memory.TotalScore)]));
		}

		if (r.Disk.SeqReadMBs > 0 || r.Disk.SeqWriteMBs > 0)
		{
			var rows = new List<(string, string, int)>();
			if (r.Disk.SeqReadMBs > 0) rows.Add(("顺序读取", $"{r.Disk.SeqReadMBs:F0} MB/s", r.Disk.SeqReadScore));
			if (r.Disk.SeqWriteMBs > 0) rows.Add(("顺序写入", $"{r.Disk.SeqWriteMBs:F0} MB/s", r.Disk.SeqWriteScore));
			if (r.Disk.Random4KReadIops > 0) rows.Add(("4K随机读", $"{r.Disk.Random4KReadIops / 1000.0:F0}K IOPS", r.Disk.Random4KReadScore));
			if (r.Disk.Random4KWriteIops > 0) rows.Add(("4K随机写", $"{r.Disk.Random4KWriteIops / 1000.0:F0}K IOPS", r.Disk.Random4KWriteScore));
			if (r.Disk.Temperature > 0f) rows.Add(("温度", $"{r.Disk.Temperature:F0}℃", 0));
			sections.Children.Add(BuildSectionCard("\ueda2", "硬盘性能", rows));
		}

		if (r.Browser.TotalScore > 0)
		{
			var rows = new List<(string, string, int)>
			{
				("JS 引擎", r.Browser.JsDetail, r.Browser.JsScore),
				("DOM 表格", r.Browser.DomDetail, r.Browser.DomScore),
				("DOM 卡片", r.Browser.CardDetail, r.Browser.CardScore),
				("CSS 动画", r.Browser.CssDetail, r.Browser.CssScore),
				("布局重排", r.Browser.LayoutDetail, r.Browser.LayoutScore),
				("事件处理", r.Browser.EventDetail, r.Browser.EventScore)
			};
			sections.Children.Add(BuildSectionCard("\ue774", "浏览器流畅度", rows));
		}

		if (r.Win.BestAvgMs > 0)
		{
			var rows = new List<(string, string, int)>
			{
				("列表加载", $"{r.Win.AvgListLoadMs:F0} ms", 0),
				("图片列表", $"{r.Win.AvgImageListMs:F0} ms", 0),
				("标签切换", $"{r.Win.AvgTabSwitchMs:F0} ms", 0),
				("滚动", $"{r.Win.AvgScrollMs:F0} ms", 0),
				("树形展开", $"{r.Win.AvgTreeExpandMs:F0} ms", 0),
				("排序过滤", $"{r.Win.AvgSortFilterMs:F0} ms", 0),
				("长文本", $"{r.Win.AvgTextRenderMs:F0} ms", 0),
				("平均总耗时", $"{r.Win.BestAvgMs:F0} ms", r.Win.FinalScore)
			};
			sections.Children.Add(BuildSectionCard("\ue80f", "WinUI 性能", rows));
		}

		if (sections.Children.Count > 0)
			_detailPanel.Children.Add(sections);

		// 核间延迟热力图
		if (r.Cpu.LatencyMatrix != null)
		{
			Image img = new()
			{
				MaxHeight = 340.0,
				Stretch = Stretch.Uniform,
				HorizontalAlignment = HorizontalAlignment.Center,
				Visibility = Visibility.Collapsed
			};
			Border container = new()
			{
				Visibility = Visibility.Collapsed,
				Padding = new Thickness(8.0),
				CornerRadius = new CornerRadius(6.0),
				Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
				Child = img
			};
			_detailPanel.Children.Add(new StackPanel
			{
				Spacing = 6.0,
				Children =
				{
					(UIElement)new TextBlock
					{
						Text = "核间延迟热力图",
						FontSize = 13.0,
						FontWeight = FontWeights.Bold
					},
					(UIElement)container
				}
			});
			_ = LoadHeatmapAsync(r, img, container);
		}
	}

	private void ShowEmptyDetail(string message)
	{
		_loadBtn.IsEnabled = false;
		_deleteBtn.IsEnabled = false;
		_detailPanel.Children.Clear();
		_detailPanel.Children.Add(new TextBlock
		{
			Text = message,
			FontSize = 13.0,
			HorizontalAlignment = HorizontalAlignment.Center,
			Margin = new Thickness(0.0, 40.0, 0.0, 0.0),
			Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"]
		});
	}

	private Border BuildScoreCard(string label, int score, string grade, int? prevScore)
	{
		TextBlock deltaText = new()
		{
			FontSize = 11.0,
			VerticalAlignment = VerticalAlignment.Center,
			Margin = new Thickness(8.0, 0.0, 0.0, 0.0)
		};
		if (score <= 0)
		{
			deltaText.Text = "";
		}
		else if (prevScore is not > 0)
		{
			deltaText.Text = "首次记录";
			deltaText.Foreground = new SolidColorBrush(ColorsGray);
		}
		else
		{
			int d = score - prevScore.Value;
			if (d == 0)
			{
				deltaText.Text = "与上次持平";
				deltaText.Foreground = new SolidColorBrush(ColorsGray);
			}
			else if (d > 0)
			{
				deltaText.Text = $"↑ {d}";
				deltaText.Foreground = new SolidColorBrush(UpColor);
			}
			else
			{
				deltaText.Text = $"↓ {Math.Abs(d)}";
				deltaText.Foreground = new SolidColorBrush(DownColor);
			}
		}

		Color scoreColor = score > 0 ? GradeColor(grade) : ColorsGray;
		StackPanel scoreRow = new()
		{
			Orientation = Orientation.Horizontal,
			Spacing = 4.0
		};
		scoreRow.Children.Add(new TextBlock
		{
			Text = score > 0 ? score.ToString() : "—",
			FontSize = 26.0,
			FontWeight = FontWeights.Bold,
			Foreground = new SolidColorBrush(scoreColor),
			VerticalAlignment = VerticalAlignment.Center
		});
		scoreRow.Children.Add(new TextBlock
		{
			Text = grade,
			FontSize = 14.0,
			FontWeight = FontWeights.Bold,
			Foreground = new SolidColorBrush(scoreColor),
			VerticalAlignment = VerticalAlignment.Center
		});
		scoreRow.Children.Add(deltaText);

		return new Border
		{
			Background = (Brush)Application.Current.Resources["CardBackgroundFillColorSecondaryBrush"],
			BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
			BorderThickness = new Thickness(1.0),
			CornerRadius = new CornerRadius(6.0),
			Padding = new Thickness(12.0, 10.0, 12.0, 10.0),
			Child = new StackPanel
			{
				Spacing = 2.0,
				Children =
				{
					(UIElement)new TextBlock
					{
						Text = label,
						FontSize = 12.0,
						Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
					},
					(UIElement)scoreRow
				}
			}
		};
	}

	private static Border BuildNotTestedCard(string label)
	{
		return new Border
		{
			Background = (Brush)Application.Current.Resources["CardBackgroundFillColorSecondaryBrush"],
			BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
			BorderThickness = new Thickness(1.0),
			CornerRadius = new CornerRadius(6.0),
			Padding = new Thickness(12.0, 10.0, 12.0, 10.0),
			Opacity = 0.6,
			Child = new StackPanel
			{
				Spacing = 2.0,
				Children =
				{
					(UIElement)new TextBlock
					{
						Text = label,
						FontSize = 12.0,
						Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
					},
					(UIElement)new StackPanel
					{
						Orientation = Orientation.Horizontal,
						Spacing = 4.0,
						Children =
						{
							(UIElement)new TextBlock
							{
								Text = "—",
								FontSize = 26.0,
								FontWeight = FontWeights.Bold,
								Foreground = new SolidColorBrush(ColorsGray)
							},
							(UIElement)new TextBlock
							{
								Text = "未测试",
								FontSize = 11.0,
								VerticalAlignment = VerticalAlignment.Center,
								Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"]
							}
						}
					}
				}
			}
		};
	}

	private Border BuildSectionCard(string glyph, string title, List<(string Label, string Detail, int Score)> rows)
	{
		StackPanel content = new() { Spacing = 6.0 };
		content.Children.Add(new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Spacing = 8.0,
			Children =
			{
				(UIElement)new FontIcon { Glyph = glyph, FontSize = 14.0, Foreground = new SolidColorBrush(AccentBlue) },
				(UIElement)new TextBlock { Text = title, FontSize = 14.0, FontWeight = FontWeights.Bold }
			}
		});
		foreach (var (label, detail, score) in rows)
		{
			Grid row = new()
			{
				ColumnSpacing = 8.0,
				ColumnDefinitions =
				{
					new ColumnDefinition { Width = new GridLength(100.0) },
					new ColumnDefinition { Width = GridLength.Auto },
					new ColumnDefinition { Width = new GridLength(1.0, GridUnitType.Star) }
				}
			};
			TextBlock labelBlock = new()
			{
				Text = label,
				FontSize = 12.0,
				VerticalAlignment = VerticalAlignment.Center,
				Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
			};
			row.Children.Add(labelBlock);
			Grid.SetColumn(labelBlock, 0);
			TextBlock scoreBlock = new()
			{
				Text = score > 0 ? score.ToString() : "—",
				FontSize = 13.0,
				FontWeight = FontWeights.Bold,
				VerticalAlignment = VerticalAlignment.Center,
				Foreground = new SolidColorBrush(score > 0 ? ScoreColor(score) : ColorsGray)
			};
			row.Children.Add(scoreBlock);
			Grid.SetColumn(scoreBlock, 1);
			TextBlock detailBlock = new()
			{
				Text = detail,
				FontSize = 11.0,
				VerticalAlignment = VerticalAlignment.Center,
				Foreground = (Brush)Application.Current.Resources["TextFillColorTertiaryBrush"]
			};
			row.Children.Add(detailBlock);
			Grid.SetColumn(detailBlock, 2);
			content.Children.Add(row);
		}
		return MakeCard(content);
	}

	private async Task LoadHeatmapAsync(PerformanceBenchmarkResult r, Image img, Border container)
	{
		try
		{
			int reqId = ++_heatmapReqId;
			if (r.Cpu.LatencyMatrix == null) return;
			string key = r.TestTime.ToString("yyyyMMddHHmmssfff");
			if (!_heatmapCache.TryGetValue(key, out string? path))
			{
				var matrix = r.Cpu.LatencyMatrix;
				path = await Task.Run(() => PerformanceBenchmarkService.GenerateLatencyHeatmap(matrix)) ?? "";
				_heatmapCache[key] = path;
			}
			if (_heatmapReqId != reqId || string.IsNullOrEmpty(path)) return;
			img.Source = new BitmapImage(new Uri(path));
			img.Visibility = Visibility.Visible;
			container.Visibility = Visibility.Visible;
		}
		catch { }
	}

	// ---------- 操作 ----------

	private void LoadSelectedToMainPage()
	{
		if (_selectedIndex < 0 || _selectedIndex >= _entries.Count) return;
		var r = _entries[_selectedIndex];
		_loadToMainPage(r);
		Close();
	}

	private void DeleteSelected()
	{
		if (_selectedIndex < 0 || _selectedIndex >= _entries.Count) return;
		DisarmClear();
		int removedIdx = _selectedIndex;
		PerformanceBenchmarkService.DeleteHistory(removedIdx);
		_entries.RemoveAt(removedIdx);
		_selectedIndex = -1;
		if (_entries.Count == 0)
		{
			_list.Items.Clear();
			_list.SelectedIndex = -1;
			_listHeader.Text = "测试记录";
			ShowEmptyDetail("暂无历史测试记录");
			return;
		}
		// 优先选中紧随其后的记录（没有则选最后一条）
		int select = Math.Min(removedIdx, _entries.Count - 1);
		RebuildList(select);
		// 若换算后的 ListView 下标与删除前相同，SelectionChanged 不会触发，这里显式刷新详情
		if (_list.SelectedIndex == _entries.Count - 1 - select)
			ShowDetail(select);
	}

	private void OnClearAllClick()
	{
		if (!_clearArmed)
		{
			// 两步确认：第一次点击进入待确认状态，4 秒内再点才真正清空
			_clearArmed = true;
			_clearBtn.Content = new StackPanel
			{
				Orientation = Orientation.Horizontal,
				Spacing = 6.0,
				Children =
				{
					(UIElement)new FontIcon { Glyph = "\ued60", FontSize = 13.0 },
					(UIElement)new TextBlock { Text = "再点一次确认清空", FontSize = 13.0 }
				}
			};
			var timer = DispatcherQueue.CreateTimer();
			timer.Interval = TimeSpan.FromSeconds(4);
			timer.Tick += (_, _) => DisarmClear();
			timer.Start();
			return;
		}
		DisarmClear();
		PerformanceBenchmarkService.ClearHistory();
		_entries.Clear();
		_selectedIndex = -1;
		_heatmapCache.Clear();
		RebuildList(-1);
		ShowEmptyDetail("已清空全部历史测试记录");
	}

	private void DisarmClear()
	{
		_clearArmed = false;
		if (_clearBtn == null) return;
		_clearBtn.Content = new StackPanel
		{
			Orientation = Orientation.Horizontal,
			Spacing = 6.0,
			Children =
			{
				(UIElement)new FontIcon { Glyph = "\ued60", FontSize = 13.0 },
				(UIElement)new TextBlock { Text = "清空全部", FontSize = 13.0 }
			}
		};
	}

	// ---------- 颜色 ----------

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
}

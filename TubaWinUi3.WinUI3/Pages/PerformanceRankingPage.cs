using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using TubaWinUi3.Controls;
using TubaWinUi3.Services;

namespace TubaWinUi3.Pages;

/// <summary>
/// 「性能排行」入口：把 CPU / GPU 天梯图、社区跑分排行和核间延迟查询收进同一个页面，
/// 用顶部的 SelectorBar 切换。四个子页面保持各自的数据与交互，不重复维护实现。
/// </summary>
public sealed partial class PerformanceRankingPage : Page
{
    private readonly Dictionary<string, Page> _pages = new(StringComparer.Ordinal);
    private readonly ContentControl _host = new()
    {
        HorizontalContentAlignment = HorizontalAlignment.Stretch,
        VerticalContentAlignment = VerticalAlignment.Stretch
    };
    private SelectorBar _tabs = null!;

    private static string? _pendingTab;

    /// <summary>旧快捷方式或合并入口导航前指定初始标签，由下一次创建的页面消费。</summary>
    public static void PrepareTab(string? tab)
    {
        if (tab is not ("benchmark" or "cpu" or "gpu" or "latency"))
            return;
        _pendingTab = tab;
    }

    /// <summary>页面已经打开时直接切换标签，避免重复导航把它重新创建成默认标签。</summary>
    public bool SwitchTab(string? tab)
    {
        if (tab is not ("benchmark" or "cpu" or "gpu" or "latency"))
            return false;
        ShowTab(tab);
        return true;
    }

    public PerformanceRankingPage()
    {
        InitializeComponent();

        var tabs = new SelectorBar
        {
            HorizontalAlignment = HorizontalAlignment.Left
        };
        _tabs = tabs;
        AutomationProperties.SetAutomationId(tabs, "PerformanceRankingTabs");
        tabs.Items.Add(CreateTab("benchmark", LocalizationService.L("PerformanceRanking_TabBenchmark", "跑分排行"), selected: true));
        tabs.Items.Add(CreateTab("cpu", LocalizationService.L("PerformanceRanking_TabCpu", "CPU 天梯图")));
        tabs.Items.Add(CreateTab("gpu", LocalizationService.L("PerformanceRanking_TabGpu", "GPU 天梯图")));
        tabs.Items.Add(CreateTab("latency", LocalizationService.L("PerformanceRanking_TabLatency", "核间延迟")));

        var tabsHost = new Border
        {
            Padding = new Thickness(24, 16, 24, 4),
            Child = tabs
        };

        var root = new Grid();
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.Children.Add(tabsHost);
        root.Children.Add(_host);
        Grid.SetRow(_host, 1);

        tabs.SelectionChanged += (sender, _) =>
        {
            if ((sender.SelectedItem as SelectorBarItem)?.Tag is string tag)
                ShowPage(tag);
        };

        Content = root;

        Loaded += (_, _) =>
        {
            // 独立工具窗口使用扩展标题栏，标签条要避开 caption 按钮。
            if (BuiltinToolWindow.IsInToolWindow(this))
                tabsHost.Padding = new Thickness(24, 64, 24, 4);

            if (_pendingTab is { } pending)
            {
                _pendingTab = null;
                ShowTab(pending);
            }
        };

        var initial = _pendingTab ?? "benchmark";
        _pendingTab = null;
        ShowTab(initial);
    }

    private void ShowTab(string tag)
    {
        SelectTab(_tabs, tag);
        ShowPage(tag);
    }

    private static SelectorBarItem CreateTab(string tag, string text, bool selected = false)
    {
        var item = new SelectorBarItem
        {
            Text = text,
            Tag = tag,
            IsSelected = selected
        };
        AutomationProperties.SetAutomationId(item, "PerformanceRankingTab_" + tag);
        return item;
    }

    private static void SelectTab(SelectorBar tabs, string tag)
    {
        foreach (var item in tabs.Items.OfType<SelectorBarItem>())
        {
            if (item.Tag as string == tag)
            {
                item.IsSelected = true;
                break;
            }
        }
    }

    private void ShowPage(string tag)
    {
        if (!_pages.TryGetValue(tag, out var page))
        {
            page = CreatePage(tag);
            _pages[tag] = page;
        }

        if (!ReferenceEquals(_host.Content, page))
            _host.Content = page;
    }

    private static Page CreatePage(string tag) => tag switch
    {
        "cpu" => new CpuRankingPage(),
        "gpu" => new GpuRankingPage(),
        "latency" => new LatencyImageQueryPage(),
        _ => new BenchmarkCloudPage()
    };
}

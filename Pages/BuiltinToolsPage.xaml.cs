using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Collections.ObjectModel;
using TubaWinUi3.Services;

namespace TubaWinUi3.Pages;

public sealed partial class BuiltinToolsPage : Page
{
    private readonly ObservableCollection<BuiltinToolViewModel> _tools = [];
    private CancellationTokenSource? _activeCts;

    public BuiltinToolsPage()
    {
        InitializeComponent();
        ToolsGrid.ItemsSource = _tools;
        PopulateCategoryFilter();
        LoadTools(null);
    }

    private void ToolsGrid_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        var panel = ToolsGrid.ItemsPanelRoot as ItemsWrapGrid;
        if (panel is null) return;

        double minItemWidth = 280;
        double spacing = 12;
        double availableWidth = ToolsGrid.ActualWidth - ToolsGrid.Padding.Left - ToolsGrid.Padding.Right;

        if (availableWidth <= 0) return;

        int columns = Math.Max(1, (int)((availableWidth + spacing) / (minItemWidth + spacing)));
        double itemWidth = (availableWidth - (columns - 1) * spacing) / columns;
        panel.ItemWidth = Math.Max(minItemWidth, itemWidth);
    }

    private void PopulateCategoryFilter()
    {
        CategoryFilter.Items.Add("全部分类");
        foreach (var category in BuiltinToolRegistry.GetCategories())
        {
            CategoryFilter.Items.Add(category);
        }
        CategoryFilter.SelectedIndex = 0;
    }

    private void CategoryFilter_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        var selected = CategoryFilter.SelectedItem as string;
        LoadTools(selected == "全部分类" ? null : selected);
    }

    private void LoadTools(string? category)
    {
        _tools.Clear();
        var tools = category is null
            ? BuiltinToolRegistry.Tools
            : BuiltinToolRegistry.GetByCategory(category);

        foreach (var tool in tools)
        {
            _tools.Add(new BuiltinToolViewModel(tool));
        }

        ToolCountText.Text = $"{_tools.Count} 个内置工具";
    }

    private void ToolsGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is BuiltinToolViewModel vm)
        {
            _ = ExecuteToolAsync(vm);
        }
    }

    private void RunButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button { DataContext: BuiltinToolViewModel vm })
        {
            _ = ExecuteToolAsync(vm);
        }
    }

    private async Task ExecuteToolAsync(BuiltinToolViewModel vm)
    {
        _activeCts?.Cancel();
        _activeCts = new CancellationTokenSource();

        var context = new BuiltinToolContext
        {
            XamlRoot = XamlRoot,
            OnProgress = msg => DispatcherQueue.TryEnqueue(() =>
            {
                StatusBar.Title = vm.Name;
                StatusBar.Message = msg;
                StatusBar.Severity = InfoBarSeverity.Informational;
                StatusBar.IsOpen = true;
            }),
            CancellationToken = _activeCts.Token
        };

        try
        {
            await vm.Tool.ExecuteAsync(context);
            StatusBar.IsOpen = false;
        }
        catch (OperationCanceledException)
        {
            ShowStatus("已取消", vm.Name, InfoBarSeverity.Warning);
        }
        catch (Exception ex)
        {
            ShowStatus("执行失败", ex.Message, InfoBarSeverity.Error);
        }
    }

    private void ShowStatus(string title, string message, InfoBarSeverity severity)
    {
        StatusBar.Title = title;
        StatusBar.Message = message;
        StatusBar.Severity = severity;
        StatusBar.IsOpen = true;
    }
}

public sealed class BuiltinToolViewModel
{
    public IBuiltinTool Tool { get; }

    public BuiltinToolViewModel(IBuiltinTool tool)
    {
        Tool = tool;
    }

    public string Id => Tool.Id;
    public string Name => Tool.Name;
    public string Description => Tool.Description;
    public string Glyph => Tool.Glyph;
    public string Category => Tool.Category;
    public string KindText => Tool.Kind switch
    {
        BuiltinToolKind.Dialog => "弹窗",
        BuiltinToolKind.BackgroundTask => "后台任务",
        BuiltinToolKind.ProgressTask => "进度任务",
        BuiltinToolKind.InstantAction => "即时操作",
        _ => "未知"
    };
}
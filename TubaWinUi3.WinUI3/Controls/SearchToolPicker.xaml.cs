using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TubaWinUi3.Models;
using TubaWinUi3.Services;

namespace TubaWinUi3.Controls;

/// <summary>新手引导与设置共用的搜索注册选择器；保存前所有修改只在当前控件内暂存。</summary>
public sealed partial class SearchToolPicker : UserControl
{
    private bool _updatingSelection;
    private bool _loadingTools = true;
    private int _loadVersion;
    private SetupSearchSelection? _selection;
    private CancellationTokenSource? _iconCts;

    public bool IsBuiltinEnablement { get; set; }
    public event EventHandler? StateChanged;
    public bool CanApply => !_loadingTools && _selection is not null;

    public SearchToolPicker() => InitializeComponent();

    public void CancelPendingLoad()
    {
        ++_loadVersion;
        _iconCts?.Cancel();
        _iconCts?.Dispose();
        _iconCts = null;
        if (_selection is not null)
            foreach (var tool in _selection.Tools) tool.StopIconUpdates();
    }

    public async Task InitializeAsync()
    {
        CancelPendingLoad();
        var version = ++_loadVersion;
        _loadingTools = true;
        ToolsLoadingPanel.Visibility = Visibility.Visible;
        UpdateSearchState();
        SearchRegistrationHint.Visibility = IsBuiltinEnablement ? Visibility.Collapsed : Visibility.Visible;
        if (IsBuiltinEnablement)
        {
            ToolSearchBox.Header = LocalizationService.L("SearchPicker_EnableHeader", "选择要启用的内置工具");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(ToolSearchBox, "BuiltinEnableSearch");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(SelectAllButton, "BuiltinEnableAll");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(ClearSelectionButton, "BuiltinEnableNone");
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(SearchToolsList, "BuiltinEnableList");
        }
        var candidates = BuiltinToolRegistry.AllTools.Select(t => new SetupSearchCandidate(
            "builtin:" + t.Id, t.Name, string.Format(LocalizationService.L("SearchPicker_BuiltinCategory", "内置工具 · {0}"), LocalizationService.GetCategoryDisplayName(t.Category)), t.Id)).ToList();
        string? error = null;
        try
        {
            if (!IsBuiltinEnablement)
            {
                var tools = await ToolCatalog.GetAllToolsAsync();
                candidates.AddRange(tools.Where(t => !t.IsBuiltinLink)
                    .Select(t => new SetupSearchCandidate(ToolVisibilityService.SearchKey(t), t.Name,
                        string.Format(LocalizationService.L("SearchPicker_LocalCategory", "本地工具 · {0}"), t.CategoryDisplay)
                        + (t.NeedsDownload ? LocalizationService.L("SearchPicker_PendingDownload", " · 下载后自动注册") : ""),
                        IconPath: t.IconPath, ExternalTool: t)));
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[Setup] Tool list: {ex}");
            error = LocalizationService.L("SearchPicker_LocalError", "本地工具读取失败，当前仅列出内置工具。稍后可在设置中调整。");
        }
        if (version != _loadVersion) return;
        var selected = ToolVisibilityService.LoadSearchSelection(candidates.Select(c => c.Key));
        var excluded = IsBuiltinEnablement
            ? new HashSet<string>(ToolVisibilityService.Parse(AppSettings.Get(ToolVisibilityService.DisabledBuiltinsKey)).Select(id => "builtin:" + id), StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(candidates.Select(c => c.Key).Where(key => !selected.Contains(key)), StringComparer.OrdinalIgnoreCase);
        _selection = new SetupSearchSelection(candidates, excluded);
        foreach (var tool in _selection.Tools) tool.ObserveIconChanges();
        _iconCts = new CancellationTokenSource();
        _ = ToolIconService.LoadIconsAsync(_selection.Tools.Where(t => t.ExternalTool is not null)
            .Select(t => t.ExternalTool!).ToList(), DispatcherQueue, _iconCts.Token);
        _loadingTools = false;
        ToolsLoadingPanel.Visibility = Visibility.Collapsed;
        ToolsMessage.Message = error ?? "";
        FilterTools();
    }

    private void ToolSearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        if (args.Reason == AutoSuggestionBoxTextChangeReason.UserInput) FilterTools();
    }

    private void FilterTools()
    {
        if (_selection is null) return;
        _updatingSelection = true;
        try
        {
            var visible = _selection.Filter(ToolSearchBox.Text?.Trim() ?? "");
            SearchToolsList.ItemsSource = visible;
            foreach (var tool in visible.Where(_selection.IsSelected)) SearchToolsList.SelectedItems.Add(tool);
            ToolsEmptyText.Visibility = visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }
        finally { _updatingSelection = false; }
        UpdateSearchState();
    }

    private void SearchToolsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingSelection || _selection is null) return;
        foreach (var tool in e.AddedItems.OfType<SetupSearchTool>()) _selection.SetSelected(tool, true);
        foreach (var tool in e.RemovedItems.OfType<SetupSearchTool>()) _selection.SetSelected(tool, false);
        UpdateSearchState();
    }

    private void SelectAll_Click(object sender, RoutedEventArgs e) => SelectAll(true);
    private void ClearSelection_Click(object sender, RoutedEventArgs e) => SelectAll(false);

    private void SelectAll(bool selected)
    {
        if (_selection is null) return;
        foreach (var tool in _selection.Tools) _selection.SetSelected(tool, selected);
        FilterTools();
    }

    private void UpdateSearchState()
    {
        var count = _selection?.Tools.Count(_selection.IsSelected) ?? 0;
        ToolSearchBox.IsEnabled = !_loadingTools;
        SelectAllButton.IsEnabled = !_loadingTools;
        ClearSelectionButton.IsEnabled = !_loadingTools;
        SearchToolsList.IsEnabled = !_loadingTools;
        SelectionSummary.Text = _loadingTools
            ? LocalizationService.L("SearchPicker_LoadingSummary", "正在读取工具…")
            : string.Format(LocalizationService.L("SearchPicker_Summary", "已选择 {0} / {1} 个工具"), count, _selection?.Tools.Count ?? 0);

        ToolsMessage.IsOpen = !string.IsNullOrEmpty(ToolsMessage.Message);
        ToolsMessage.Title = "";
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    internal void ShowRegistrationErrors(IReadOnlyList<string> errors)
    {
        ToolsMessage.Severity = InfoBarSeverity.Warning;
        ToolsMessage.Message = string.Join("\n", errors);
        ToolsMessage.IsOpen = true;
    }

    internal async Task<SearchRegistrationResult> ApplySettingsAsync()
    {
        if (!CanApply) throw new InvalidOperationException(LocalizationService.L("SearchPicker_LoadingSummary", "正在读取工具…"));
        if (IsBuiltinEnablement)
        {
            var disabled = _selection!.BuildExclusions(ToolVisibilityService.Parse(AppSettings.Get(ToolVisibilityService.DisabledBuiltinsKey))
                .Select(id => "builtin:" + id)).Select(key => key["builtin:".Length..]);
            ToolVisibilityService.Save(disabled, ToolVisibilityService.Parse(AppSettings.Get(ToolVisibilityService.SearchExcludedKey)));
            return new(0, 0, []);
        }
        var excluded = _selection!.BuildExclusions(ToolVisibilityService.Parse(AppSettings.Get(ToolVisibilityService.SearchExcludedKey)));
        ToolVisibilityService.Save(ToolVisibilityService.Parse(AppSettings.Get(ToolVisibilityService.DisabledBuiltinsKey)), excluded);
        ToolVisibilityService.SaveSearchSelection(_selection.BuildSelections());
        return await WindowsSearchIndexService.RefreshAsync();
    }
}

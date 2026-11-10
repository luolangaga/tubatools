using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using TubaWinUi3.Models;
using TubaWinUi3.Services;

namespace TubaWinUi3.Pages;

public sealed partial class DashboardPage
{
    private bool _updatingToolItems;
    private readonly DispatcherTimer _filterDebounce = new() { Interval = TimeSpan.FromMilliseconds(150) };
    private IReadOnlyList<ToolItem>? _baseCatalog;
    private IReadOnlyList<ToolItem>? _baseFavoriteCatalog;
    private string[] _baseFavoritePaths = [];
    private IReadOnlyList<ToolItem> _sortedTools = [];
    private IReadOnlyList<ToolItem> _favoriteTools = [];
    private string? _baseLanguage;
    private bool _historyDirty = true;

    private void OnLaunchHistoryChanged()
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            _historyDirty = true;
            if (!_favorites) ApplyToolFilter();
        });
    }

    private void ToolSearchBox_LosingFocus(UIElement sender, LosingFocusEventArgs e)
    {
        // 集合更新中的焦点迁移来自 GridView；在离开搜索框前取消，避免打断输入法组合。
        if (_updatingToolItems) e.TryCancel();
    }

    private void ToolSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (!_ready) return;
        _filterDebounce.Stop();
        if (ToolSearchBox.Text.Length == 0) ApplyToolFilter();
        else _filterDebounce.Start();
    }

    private void EnsureBaseToolLists()
    {
        var paths = FavoritesService.GetFavorites();
        if (ReferenceEquals(_baseCatalog, _catalog) && ReferenceEquals(_baseFavoriteCatalog, _favoriteCatalog) && !_historyDirty &&
            _baseLanguage == LocalizationService.CurrentLanguage && paths.SequenceEqual(_baseFavoritePaths, StringComparer.OrdinalIgnoreCase)) return;
        _baseCatalog = _catalog;
        _baseFavoriteCatalog = _favoriteCatalog;
        _baseFavoritePaths = paths.ToArray();
        _baseLanguage = LocalizationService.CurrentLanguage;
        _historyDirty = false;
        var catalog = _catalog ?? [];
        var favorites = paths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var byPath = new Dictionary<string, ToolItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var tool in catalog.Concat(_favoriteCatalog).Distinct()) tool.IsFavorite = favorites.Contains(tool.Path);
        // Prefer the original category placement, so saved favorite paths keep their identity.
        foreach (var tool in _favoriteCatalog.Concat(catalog)) byPath.TryAdd(tool.Path, tool);
        _favoriteTools = paths.Select(path => byPath.GetValueOrDefault(path)).OfType<ToolItem>().ToList();
        _sortedTools = AllToolsSortService.Sort(catalog, LaunchHistoryService.GetRecords());
    }

    private void ApplyToolFilter()
    {
        if (!_ready) return;
        bool searchWasFocused = ToolSearchBox.FocusState != FocusState.Unfocused;
        int selectionStart = ToolSearchBox.SelectionStart;
        int selectionLength = ToolSearchBox.SelectionLength;
        _filterDebounce.Stop();
        EnsureBaseToolLists();
        IEnumerable<ToolItem> result = _favorites ? _favoriteTools : _sortedTools;
        result = result.Where(t => !t.IsBuiltinLink ||
            (ToolVisibilityService.IsBuiltinEnabled(t.BuiltinToolId!) && HomeDashboardContent.IsVisible(new() { Target = "builtin:" + t.BuiltinToolId }, RuntimeHelper.IsPackagedContext)));
        string query = ToolSearchBox.Text.Trim();
        if (query.Length > 0) result = result
            .Select(t => (Tool: t, Score: ToolSearchMatcher.Score(query, t.Name,
                new[] { t.Description, t.RelativePath, t.CategoryDisplay }.Concat(t.Tags ?? []))))
            .Where(hit => hit.Score > 0).OrderByDescending(hit => hit.Score).Select(hit => hit.Tool);
        // 搜索框位于 GridView.Header；Clear 发出的 Reset 会重置虚拟化面板，
        // 导致正在编辑的 Header 搜索框失焦。保留集合，按差异更新可见项。
        var visibleTools = result.ToList();
        _updatingToolItems = true;
        try
        {
            // Remove filtered-out items first: narrowing a search now takes a linear pass,
            // rather than moving every surviving row across all removed rows.
            var wanted = visibleTools.ToHashSet();
            for (int i = _tools.Count - 1; i >= 0; i--)
                if (!wanted.Contains(_tools[i])) _tools.RemoveAt(i);
            var existing = _tools.ToHashSet();
            for (int index = 0; index < visibleTools.Count; index++)
            {
                var tool = visibleTools[index];
                if (index < _tools.Count && ReferenceEquals(_tools[index], tool)) continue;
                int oldIndex = existing.Contains(tool) ? _tools.IndexOf(tool) : -1;
                if (oldIndex >= 0) _tools.Move(oldIndex, index);
                else _tools.Insert(index, tool);
            }
            while (_tools.Count > visibleTools.Count) _tools.RemoveAt(_tools.Count - 1);
        }
        finally { _updatingToolItems = false; }
        ToolsGrid.CanDragItems = ToolsGrid.CanReorderItems = ToolsGrid.AllowDrop = _favorites && query.Length == 0;
        ToolCountText.Text = _loading ? S("Loading", "正在加载…") : string.Format(S("CatalogCount", "{0} · {1} 个工具"),
            _favorites ? S("Favorites", "收藏") : S("AllTools", "全部工具"), _tools.Count);
        EmptyText.Visibility = !_loading && _tools.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyText.Text = query.Length > 0 ? S("NoMatchingTools", "没有找到匹配的工具") : _favorites
            ? S("FavoritesEmpty", "还没有收藏的工具。在工具卡片上点击星标即可收藏。") : S("CatalogEmpty", "工具目录中暂无工具。");
        UpdateCardLayout();
        // WinUI 也可能在移除最后一个已实现项时重新分配 Header 焦点。
        // 仅保留刷新前正在编辑的搜索框，不抢回用户已主动移走的焦点。
        if (searchWasFocused && ToolSearchBox.FocusState == FocusState.Unfocused)
        {
            ToolSearchBox.Focus(FocusState.Programmatic);
            ToolSearchBox.Select(selectionStart, selectionLength);
        }
    }

    private async void ToolsGrid_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ToolItem tool) await OpenCatalogToolAsync(tool);
    }

    private async Task OpenCatalogToolAsync(ToolItem tool)
    {
        try
        {
            if (tool.IsBuiltinLink)
                await OpenCardAsync(new() { Target = "builtin:" + tool.BuiltinToolId, Featured = true });
            else App.MainWindow?.LaunchCatalogTool(tool);
        }
        catch (Exception ex) { ShowStatus(S("OpenFailed", "打开失败"), ex.Message, InfoBarSeverity.Error); }
    }

    private void ToolFavorite_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ToolItem tool }) return;
        FavoritesService.ToggleFavorite(tool.Path);
        tool.IsFavorite = FavoritesService.IsFavorite(tool.Path);
        if (_favorites) ApplyToolFilter();
    }

    private void ToolsGrid_DragItemsCompleted(ListViewBase sender, DragItemsCompletedEventArgs args)
    {
        if (_favorites && ToolSearchBox.Text.Length == 0) FavoritesService.SaveOrder(_tools.Select(t => t.Path));
    }

    private void ToolCard_RightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: ToolItem tool } anchor) return;
        var menu = new MenuFlyout();
        var open = new MenuFlyoutItem { Text = tool.LaunchButtonText, Icon = new SymbolIcon(Symbol.OpenFile) };
        open.Click += async (_, _) => await OpenCatalogToolAsync(tool); menu.Items.Add(open);
        var favorite = new MenuFlyoutItem { Text = FavoritesService.IsFavorite(tool.Path)
            ? S("RemoveFavorite", "取消收藏") : S("AddFavorite", "收藏"), Icon = new SymbolIcon(Symbol.Favorite) };
        favorite.Click += (_, _) => { FavoritesService.ToggleFavorite(tool.Path); tool.IsFavorite = FavoritesService.IsFavorite(tool.Path); if (_favorites) ApplyToolFilter(); };
        menu.Items.Add(favorite);
        if (tool.ArchOptions.Count > 1)
        {
            var architectures = new MenuFlyoutSubItem { Text = LocalizationService.L("Common_SelectArch", "选择架构") };
            foreach (var option in tool.ArchOptions)
            {
                var item = new ToggleMenuFlyoutItem { Text = option.Name, IsChecked = Equals(tool.SelectedArch, option) };
                item.Click += (_, _) => tool.SelectedArch = option;
                architectures.Items.Add(item);
            }
            menu.Items.Add(architectures);
        }
        var shortcut = new MenuFlyoutItem { Text = LocalizationService.L("Common_SendToDesktop", "发送到桌面快捷方式") };
        shortcut.Click += (_, _) =>
        {
            try { WindowsSearchIndexService.CreateDesktopShortcut(tool); }
            catch (Exception ex) { ShowStatus(S("OpenFailed", "操作失败"), ex.Message, InfoBarSeverity.Error); }
        };
        menu.Items.Add(shortcut);
        if (!tool.IsBuiltinLink && Directory.Exists(tool.EffectiveWorkingDir))
        {
            var directory = new MenuFlyoutItem { Text = LocalizationService.L("Common_OpenLocation", "打开工具所在目录") };
            directory.Click += (_, _) =>
            {
                try { Process.Start(new ProcessStartInfo("explorer.exe", tool.EffectiveWorkingDir) { UseShellExecute = true }); }
                catch (Exception ex) { ShowStatus(S("OpenFailed", "操作失败"), ex.Message, InfoBarSeverity.Error); }
            };
            menu.Items.Add(directory);
        }
        menu.ShowAt(anchor, e.GetPosition(anchor)); e.Handled = true;
    }
}

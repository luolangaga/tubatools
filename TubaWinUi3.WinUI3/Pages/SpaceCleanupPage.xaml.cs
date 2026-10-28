using FluentCleaner.Models;
using FluentCleaner.Services;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Windowing;
using System.Collections.ObjectModel;
using System.Text;
using TubaWinUi3.Services;
using TubaWinUi3.Services.SpaceCleanup;
using Windows.Foundation;
using Windows.System;

namespace TubaWinUi3.Services.SpaceCleanup
{
    public sealed record SpaceDriveChoice(string Path, string Label);
}

namespace TubaWinUi3.Pages
{
public sealed partial class SpaceCleanupPage : Page
{
    private SpaceScanSession? _session;
    private SpaceNode? _view, _selection;
    private CancellationTokenSource? _scanCancellation, _cleanupCancellation;
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromMilliseconds(500) };
    private readonly DispatcherTimer _filterDebounce = new() { Interval = TimeSpan.FromMilliseconds(300) };
    private FileSystemWatcher? _watcher;
    private volatile bool _diskChanged;
    private long _lastDiskChange;
    private bool _scanning, _paused, _cleanupBusy, _leaving, _updatingList;
    private int _depth = 3, _mode;
    private string _cleanupVolume = "";
    private List<CleanupRow> _junkRows = [], _windowsRows = [];
    private readonly HashSet<CleanupRow> _checked = [];
    private SpaceFilter _filter = new("");
    private readonly ObservableCollection<SpaceFileRow> _treeRoots = [];
    private readonly Dictionary<SpaceNode, SpaceFileRow> _treeRows = [];
    private SpaceNode? _treeRoot;
    private sealed class ViewState(SpaceScanSession session, SpaceNode node, string filter, int depth)
    { public SpaceScanSession Session = session; public SpaceNode Node = node; public string Filter = filter; public int Depth = depth; }
    private TabViewItem? _activeTab;
    private bool _changingView;
    private AppWindow? _fullscreenWindow;
    private AppWindowPresenter? _previousPresenter;
    private Action? _restoreHostChrome;
    private TreeView FileList = null!;

    private void RecreateFileTree()
    {
        if (FileList is { } previous)
        {
            previous.SelectionChanged -= FileSelected; previous.ItemInvoked -= FileInvoked;
            previous.Expanding -= FileExpanding; previous.RightTapped -= FileRightTapped; previous.KeyDown -= FileKeyDown;
            previous.SelectedItem = null; previous.ItemsSource = null; previous.RootNodes.Clear();
            FileListHost.Content = null;
        }
        FileList = new TreeView
        {
            SelectionMode = TreeViewSelectionMode.Single, CanDragItems = false, CanReorderItems = false,
            ItemTemplate = (DataTemplate)Resources["SpaceFileRowTemplate"], ItemsSource = _treeRoots
        };
        FileList.SelectionChanged += FileSelected; FileList.ItemInvoked += FileInvoked;
        FileList.Expanding += FileExpanding; FileList.RightTapped += FileRightTapped; FileList.KeyDown += FileKeyDown;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(FileList, "SpaceFileList");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(FileList, "文件树，箭头展开目录，双击进入，Shift+F10 打开菜单");
        FileListHost.Content = FileList;
    }

    public SpaceCleanupPage()
    {
        InitializeComponent();
        var fullscreenKey = new KeyboardAccelerator { Key = VirtualKey.F11 };
        fullscreenKey.Invoked += (_, args) => { ToggleFullscreen(this, new RoutedEventArgs()); args.Handled = true; };
        KeyboardAccelerators.Add(fullscreenKey);
        var escapeKey = new KeyboardAccelerator { Key = VirtualKey.Escape };
        escapeKey.Invoked += (_, args) => { if (_fullscreenWindow is not null) { ExitFullscreen(); args.Handled = true; } };
        KeyboardAccelerators.Add(escapeKey);
        RecreateFileTree();
        Treemap.Selected += SelectNode;
        Treemap.ZoomRequested += node => Navigate(node);
        Treemap.UpRequested += () => Navigate(_view?.Parent);
        Treemap.NodeContextRequested += (node, point) => ContextMenu(node).ShowAt(Treemap, point);
        FileList.DoubleTapped += (_, _) => { if (_selection is { IsDirectory: true, IsLink: false }) Navigate(_selection); };
        _refresh.Tick += RefreshTick;
        _filterDebounce.Tick += (_, _) => { _filterDebounce.Stop(); ApplyFilter(); };
        Loaded += async (_, _) =>
        {
            _leaving = false;
            if (DrivePicker.Items.Count == 0) await LoadDrivesAsync();
            if (App.MainWindow is not null)
            { Win32DropHelper.EnsureInstalled(WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow)); Win32DropHelper.FilesDropped += OnFilesDropped; }
            _refresh.Start();
        };
        Unloaded += (_, _) =>
        {
            _leaving = true; _scanCancellation?.Cancel(); _session?.Pause(false); _cleanupCancellation?.Cancel();
            _refresh.Stop(); _filterDebounce.Stop(); DisposeWatcher();
            ExitFullscreen();
            if (!_scanning && !_refreshing) { _session?.ReleaseIndex(); Treemap.Reset(); _treeRoots.Clear(); _treeRows.Clear(); }
            Win32DropHelper.FilesDropped -= OnFilesDropped;
        };
        ActualThemeChanged += (_, _) => { Treemap.Refresh(); if (_session is not null) UpdateMap(); };
    }

    private void ToggleFullscreen(object sender, RoutedEventArgs e)
    {
        if (_fullscreenWindow is not null) { ExitFullscreen(); return; }
        try
        {
            var window = AppWindow.GetFromWindowId(XamlRoot.ContentIslandEnvironment.AppWindowId);
            _previousPresenter = window.Presenter;
            _fullscreenWindow = window;
            if (App.MainWindow is { } main && ReferenceEquals(main.Content.XamlRoot, XamlRoot))
                _restoreHostChrome = main.HideChromeForToolView();
            window.SetPresenter(AppWindowPresenterKind.FullScreen);
            FullscreenText.Text = "退出全屏"; FullscreenIcon.Glyph = "\uE73F";
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(FullscreenButton, "退出全屏工具视图");
        }
        catch (Exception ex) { ExitFullscreen(); Report("无法进入全屏：" + ex.Message, false); }
    }
    private void ExitFullscreen()
    {
        var window = _fullscreenWindow;
        _fullscreenWindow = null;
        try { if (window is not null && _previousPresenter is not null) window.SetPresenter(_previousPresenter); }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine("Restore space tool window: " + ex.Message); }
        finally
        {
            _previousPresenter = null; _restoreHostChrome?.Invoke(); _restoreHostChrome = null;
            FullscreenText.Text = "全屏"; FullscreenIcon.Glyph = "\uE740";
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(FullscreenButton, "全屏工具视图");
        }
    }

    private async Task LoadDrivesAsync()
    {
        var choices = await Task.Run(() => DriveInfo.GetDrives().Select(d =>
        {
            try { return d.IsReady && d.DriveType is DriveType.Fixed or DriveType.Removable
                ? new SpaceDriveChoice(d.RootDirectory.FullName, $"{d.Name}  {d.VolumeLabel} · 可用 {ScanResult.FormatBytes(d.AvailableFreeSpace)} / {ScanResult.FormatBytes(d.TotalSize)}") : null; }
            catch { return null; }
        }).OfType<SpaceDriveChoice>().ToList());
        if (_leaving) return;
        foreach (var drive in choices) DrivePicker.Items.Add(drive);
        DrivePicker.SelectedIndex = choices.Count > 0 ? 0 : -1;
        UpdateMode();
    }

    private async void ChooseFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            var folder = await Win32Dialogs.PickFolderAsync();
            if (folder is null || _leaving) return;
            var choice = new SpaceDriveChoice(folder, folder); DrivePicker.Items.Add(choice); DrivePicker.SelectedItem = choice;
        }
        catch (Exception ex) { Report(ex.Message, true); }
    }
    private async void StartScan(object sender, RoutedEventArgs e)
    { if (DrivePicker.SelectedItem is SpaceDriveChoice drive) await ScanAsync(drive.Path); }

    private async Task ScanAsync(string path)
    {
        if (_scanning || _cleanupBusy || _refreshing) return;
        var previousSession = _session;
        bool releasePrevious = _session?.EstimatedTreeBytes >= 128L * 1024 * 1024;
        SaveView();
        DisposeWatcher(); _scanCancellation?.Dispose(); _scanCancellation = new();
        Treemap.Reset();
        if (releasePrevious)
        {
            // Native TreeView recycling retains removed rows (and their parent chains).
            // Drop the control/pool when replacing a large index, not just ItemsSource.
            _treeRoots.Clear(); _treeRows.Clear(); _treeRoot = null; RecreateFileTree();
        }
        _session = new SpaceScanSession(path); _view = _session.Root; _selection = null;
        // Tabs are alternate views of one scan. Re-scanning must release the old full-volume
        // tree instead of silently keeping another tree alive with every press of the button.
        foreach (var tab in ViewTabs.TabItems.OfType<TabViewItem>())
            if (tab.Tag is ViewState state) { state.Session = _session; state.Node = _view; }
        previousSession?.ReleaseIndex();
        lock (_changeLock) _changedDirectories.Clear();
        if (_activeTab is null) AddTab(new ViewState(_session, _view, FilterBox.Text, _depth));
        _scanning = true; _paused = false; Notice.IsOpen = false; _diskChanged = false;
        SetScanUi(); UpdateMap();
        try
        {
            if (releasePrevious)
            {
                FileList.UpdateLayout();
                await Task.Run(() => GC.Collect(2, GCCollectionMode.Forced, blocking: true, compacting: true));
                if (_leaving) return;
            }
            await SpaceScanService.ScanAsync(_session, StreamsToggle.IsChecked, _scanCancellation.Token);
            if (_leaving) return;
            if (_session.LimitReason is { } reason) Report(reason + " 当前显示部分结果，不会自动重试。", false);
            else if (_session.Skipped > 0) Report($"有 {_session.Skipped:N0} 项因权限、目录链接或读取失败被跳过；可在“视图 → 查看扫描日志”中检查。", false);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Report($"扫描失败：{ex.Message}", true); }
        finally
        {
            _scanning = false; Treemap.Scanning = false;
            if (!_leaving) { SetScanUi(); UpdateMap(); UpdateScanStatus(); StartWatcher(); }
            else _session?.ReleaseIndex();
        }
    }
    private void SetScanUi()
    {
        ScanButton.IsEnabled = !_scanning && !_cleanupBusy && !_refreshing;
        DrivePicker.IsEnabled = FolderButton.IsEnabled = !_scanning && !_cleanupBusy && !_refreshing;
        ViewTabs.IsEnabled = !_scanning && !_cleanupBusy && !_refreshing;
        ScanButton.Content = _session is null ? "开始扫描" : "重新扫描";
        ScanButton.Visibility = FolderButton.Visibility = _scanning ? Visibility.Collapsed : Visibility.Visible;
        PauseButton.Visibility = StopButton.Visibility = _scanning ? Visibility.Visible : Visibility.Collapsed;
        PauseButton.Content = _paused ? "继续" : "暂停";
        Treemap.Scanning = _scanning && !_paused;
        BusyProgress.Visibility = _cleanupBusy ? Visibility.Visible : Visibility.Collapsed;
        BusyProgress.IsIndeterminate = _cleanupBusy;
        UpdateScanProgress();
    }
    private void UpdateScanProgress()
    {
        ScanProgressPane.Visibility = _session is null ? Visibility.Collapsed : Visibility.Visible;
        if (_session is null) return;
        lock (_session.SyncRoot)
        {
            var percent = _session.ProgressPercent;
            ScanProgressBar.IsIndeterminate = _scanning && !_paused && percent is null;
            ScanProgressBar.Value = percent ?? (_session.Complete ? 100 : 0);
            string state = _scanning ? (_paused ? "已暂停" : _session.Phase) : _session.Complete ? "扫描完成" : _session.LimitReason is not null ? "已停止（部分结果）" : _session.Cancelled ? "已停止" : "扫描未完成";
            ScanProgressText.Text = percent is { } value ? $"{state} · {value:F1}%" : $"{state} · {_session.Files:N0} 个文件";
            ToolTipService.SetToolTip(ScanProgressText, _session.CurrentPath);
        }
    }
    private void PauseScan(object sender, RoutedEventArgs e)
    { _paused = !_paused; _session?.Pause(_paused); SetScanUi(); }
    private void StopScan(object sender, RoutedEventArgs e)
    { _scanCancellation?.Cancel(); _session?.Pause(false); }
    private void RefreshTick(object? sender, object e)
    {
        if (_leaving) return;
        if (_scanning) { if (!_paused) UpdateMap(); UpdateScanStatus(); }
        else if (_diskChanged && !_cleanupBusy && !_refreshing && Environment.TickCount64 - Interlocked.Read(ref _lastDiskChange) > 2000)
        {
            // File notifications never cause writes or an endless whole-volume rescan.
            _diskChanged = false;
            _ = RefreshChangedFoldersAsync();
        }
    }
    private void UpdateScanStatus()
    {
        if (_session is null || _mode != 0 || _cleanupBusy) return;
        UpdateScanProgress();
        lock (_session.SyncRoot)
        {
            string state = _scanning ? (_paused ? "已暂停" : "正在扫描") : _session.LimitReason is not null ? "内存保护已停止（部分结果）" : _session.Cancelled ? "已停止（部分结果）" : "扫描完成";
            StatusText.Text = $"{state} · {_session.Method} · {ScanResult.FormatBytes(_session.Root.Bytes)} · {_session.Files:N0} 个文件 · {_session.Folders:N0} 个文件夹 · 跳过 {_session.Skipped:N0} 项 · 索引内存约 {ScanResult.FormatBytes(_session.EstimatedTreeBytes)}" +
                (_session.UsesDiskCache ? $" · 压缩缓存 {ScanResult.FormatBytes(_session.CacheBytes)}（退出工具自动删除）" : "");
            ToolTipService.SetToolTip(StatusText, _session.CurrentPath + (_session.CachePath is { } cachePath ? "\n临时缓存：" + cachePath : ""));
        }
    }
    private void UpdateMap(bool zoom = false)
    {
        if (_session is null || _view is null || _leaving) return;
        List<SpaceTile> tiles; List<SpaceNode> rows;
        lock (_session.SyncRoot)
        {
            tiles = SpaceTreemapLayout.Build(_view, new MapRect(0, 0, Math.Max(0, Treemap.ActualWidth), Math.Max(0, Treemap.ActualHeight)), _depth, _filter);
            _session.PinVisible(tiles.Select(t => t.Node));
            var visibleTop = tiles.Where(t => t.Depth == 0).ToDictionary(t => t.Node, t => t.Bytes);
            rows = SpaceScanService.Largest(_view.Children, 1000, n => visibleTop.GetValueOrDefault(n, n.Bytes), out _, out _,
                n => _filter.IsEmpty || visibleTop.ContainsKey(n) || _filter.Matches(n));
        }
        Treemap.Update(tiles, zoom, _view);
        EmptyMap.Visibility = tiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_scanning)
        {
            EmptyTitle.Text = "正在读取目录…";
            EmptyDescription.Text = "方块会随着扫描逐步铺开，已发现的文件夹可以直接点进去。";
        }
        else if (_filter.IsEmpty)
        {
            EmptyTitle.Text = "这个位置还没有可显示的文件";
            EmptyDescription.Text = "换一个位置，或回到上一级看看；零字节文件与跳过的目录在右侧列表里。";
        }
        else
        {
            EmptyTitle.Text = "没有匹配的文件";
            EmptyDescription.Text = "清空或调整筛选条件再试一次。";
        }
        _updatingList = true;
        if (_treeRoot != _view) { _treeRoots.Clear(); _treeRows.Clear(); _treeRoot = _view; }
        lock (_session.SyncRoot) SyncTreeRows(_treeRoots, rows);
        var retainedRows = new HashSet<SpaceNode>();
        var pendingRows = new Stack<SpaceFileRow>(_treeRoots);
        while (pendingRows.TryPop(out var retainedRow)) { retainedRows.Add(retainedRow.Node); foreach (var child in retainedRow.Children) pendingRows.Push(child); }
        foreach (var key in _treeRows.Keys.Where(n => !retainedRows.Contains(n)).ToArray()) _treeRows.Remove(key);
        _session.PinVisible((_selection is { } selected ? new[] { selected } : []).Concat(tiles.Select(t => t.Node)).Concat(_treeRows.Keys));
        if (_selection is not null && _treeRows.TryGetValue(_selection, out var selectedRow)) FileList.SelectedItem = selectedRow;
        _updatingList = false;
        var trail = new List<SpaceNode>(); for (var node = _view; node is not null; node = node.Parent) trail.Add(node);
        trail.Reverse(); Breadcrumb.ItemsSource = trail.Select(n => n.Name).ToList();
        UpButton.IsEnabled = _view.Parent is not null;
        if (_activeTab is not null) _activeTab.Header = _view.Parent is null ? _view.Name : _view.Name + " · " + Path.GetPathRoot(_view.FullPath);
        ListHeading.Text = $"文件树 · 占当前目录{(rows.Count == 1000 ? "（前 1000 项）" : "")}";
        DiskSummary.Text = $"当前目录 {ScanResult.FormatBytes(_view.Bytes)} · {_view.FileCount:N0} 个文件。面积按文件大小计算；硬链接可能重复，系统保留空间及未读取目录不计入。";
    }
    private void Navigate(SpaceNode? node)
    { if (node is not { IsDirectory: true, IsLink: false, IsReleased: false }) return; _view = node; _selection = null; UpdateMap(true); }
    private void SelectNode(SpaceNode node)
    {
        _selection = node;
        SelectionText.Text = $"{node.FullPath}  ·  {ScanResult.FormatBytes(node.Bytes)}  ·  {node.FileCount:N0} 个文件";
        ToolTipService.SetToolTip(SelectionText, SelectionText.Text);
    }
    private void GoUp(object sender, RoutedEventArgs e) => Navigate(_view?.Parent);
    private void GoHome(object sender, RoutedEventArgs e) => Navigate(_session?.Root);
    private void BreadcrumbClicked(BreadcrumbBar sender, BreadcrumbBarItemClickedEventArgs e)
    { var nodes = new List<SpaceNode>(); for (var n = _view; n is not null; n = n.Parent) nodes.Add(n); nodes.Reverse(); if (e.Index < nodes.Count) Navigate(nodes[e.Index]); }
    private void MapSizeChanged(object sender, SizeChangedEventArgs e) { if (_session is not null) UpdateMap(); }
    private void AnalysisSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (e.NewSize.Width < 760) ListToggle.IsChecked = false;
        ListColumn.Width = new GridLength(ListToggle.IsChecked ? 320 : 0);
        FileListPane.Visibility = ListColumn.Width.Value > 0 ? Visibility.Visible : Visibility.Collapsed;
        RootButton.Visibility = FilterLabel.Visibility = e.NewSize.Width < 600 ? Visibility.Collapsed : Visibility.Visible;
        FilterBox.Width = e.NewSize.Width < 640 ? 120 : 180;
    }
    private void ToggleList(object sender, RoutedEventArgs e)
    {
        // Explicitly enabling the accessible list at narrow widths gives it the available pane.
        bool narrow = MapAndList.ActualWidth < 760;
        if (narrow) { ListColumn.Width = new GridLength(ListToggle.IsChecked ? Math.Min(320, MapAndList.ActualWidth * .55) : 0); }
        else ListColumn.Width = new GridLength(ListToggle.IsChecked ? 320 : 0);
        FileListPane.Visibility = ListToggle.IsChecked ? Visibility.Visible : Visibility.Collapsed;
    }
    private void ToggleAnimations(object sender, RoutedEventArgs e) { Treemap.AnimationEnabled = AnimationToggle.IsChecked; Treemap.Refresh(); }
    private void ColorByBranch(object sender, RoutedEventArgs e) { Treemap.ColorByFileType = false; UpdateMap(); }
    private void ColorByType(object sender, RoutedEventArgs e) { Treemap.ColorByFileType = true; UpdateMap(); }
    private void LessDetail(object sender, RoutedEventArgs e) { _depth = Math.Max(1, _depth - 1); UpdateMap(); }
    private void MoreDetail(object sender, RoutedEventArgs e) { _depth = Math.Min(8, _depth + 1); UpdateMap(); }
    private void FilterChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args) { _filterDebounce.Stop(); _filterDebounce.Start(); }
    private void ApplyFilter()
    {
        var filter = new SpaceFilter(FilterBox.Text);
        if (filter.Error is { } error) { Report(error, true); return; }
        _filter = filter; Notice.IsOpen = false; UpdateMap();
    }
    private void FileSelected(TreeView sender, TreeViewSelectionChangedEventArgs e)
    { if (!_updatingList && FileList.SelectedItem is SpaceFileRow row) { SelectNode(row.Node); Treemap.Select(row.Node); } }
    private void FileInvoked(TreeView sender, TreeViewItemInvokedEventArgs e) { if (e.InvokedItem is SpaceFileRow row) SelectNode(row.Node); }
    private void FileExpanding(TreeView sender, TreeViewExpandingEventArgs e)
    {
        if (_session is null || e.Node.Content is not SpaceFileRow row) return;
        if (!row.CanExpand) { e.Node.IsExpanded = false; return; }
        lock (_session.SyncRoot) LoadTreeChildren(row);
    }
    private void SyncTreeRows(ObservableCollection<SpaceFileRow> target, IReadOnlyList<SpaceNode> nodes)
    {
        var wanted = nodes.ToHashSet();
        for (int i = target.Count - 1; i >= 0; i--) if (!wanted.Contains(target[i].Node)) target.RemoveAt(i);
        for (int i = 0; i < nodes.Count; i++)
        {
            var node = nodes[i];
            if (!_treeRows.TryGetValue(node, out var row)) _treeRows[node] = row = new SpaceFileRow(node);
            row.Update(_view?.Bytes ?? 0, ActualTheme == ElementTheme.Dark, Treemap.ColorByFileType, _view);
            int current = target.IndexOf(row);
            if (current < 0) target.Insert(i, row);
            else if (current != i)
            {
                // TreeView's native node projection does not reliably apply collection
                // Move notifications during live scanning. Remove/insert preserves the
                // row (and its expanded children) while updating the native ordering.
                target.RemoveAt(current); target.Insert(i, row);
            }
            if (row.ChildrenLoaded) LoadTreeChildren(row);
        }
    }
    private void LoadTreeChildren(SpaceFileRow row)
    {
        row.ChildrenLoaded = true;
        bool Match(SpaceNode node) => _filter.IsEmpty || _filter.Matches(node) ||
            (node.IsDirectory && SpaceScanService.Descendants(node).Any(n => !n.IsDirectory && _filter.Matches(n)));
        var children = SpaceScanService.Largest(row.Node.Children, 1000, n => n.Bytes, out _, out _, Match);
        SyncTreeRows(row.Children, children); row.NotifyChildrenLoaded();
    }
    private void FileRightTapped(object sender, RightTappedRoutedEventArgs e)
    {
        for (DependencyObject? element = e.OriginalSource as DependencyObject; element is not null; element = VisualTreeHelper.GetParent(element))
            if (element is FrameworkElement { DataContext: SpaceFileRow row }) { SelectNode(row.Node); ContextMenu(row.Node).ShowAt(FileList, e.GetPosition(FileList)); e.Handled = true; return; }
    }
    private void FileKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (FileList.SelectedItem is not SpaceFileRow row) return;
        if (e.Key == VirtualKey.Enter && row.Node.IsDirectory) { Navigate(row.Node); e.Handled = true; }
        else if (e.Key == VirtualKey.F10 || e.Key == VirtualKey.Application) { ContextMenu(row.Node).ShowAt(FileList); e.Handled = true; }
        else if (e.Key == VirtualKey.Back) { Navigate(_view?.Parent); e.Handled = true; }
    }
    private MenuFlyout ContextMenu(SpaceNode node)
    {
        var menu = new MenuFlyout();
        void Add(string text, Action action, bool enabled = true)
        { var item = new MenuFlyoutItem { Text = text, IsEnabled = enabled }; item.Click += (_, _) => action(); menu.Items.Add(item); }
        Add("进入文件夹", () => Navigate(node), node.IsDirectory && !node.IsLink);
        Add("在资源管理器中显示", () => { try { SpaceFileOperations.Reveal(node.FullPath); } catch (Exception ex) { Report(ex.Message, true); } });
        Add("复制完整路径", () => ClipboardService.TrySetText(node.FullPath));
        if (node.IsAggregate) return menu;
        Add("刷新此文件夹", () => _ = RefreshNodeAsync(node.IsDirectory ? node : node.Parent!), !_scanning && !_cleanupBusy);
        var tags = new MenuFlyoutSubItem { Text = "颜色标记" };
        string[] names = ["清除标记", "红色", "橙色", "绿色", "蓝色"];
        for (int i = 0; i < names.Length; i++)
        {
            int tag = i; var item = new MenuFlyoutItem { Text = names[i] };
            item.Click += (_, _) => { if (_session is { } session) lock (session.SyncRoot) node.Tag = tag; UpdateMap(); };
            tags.Items.Add(item);
        }
        menu.Items.Add(tags); menu.Items.Add(new MenuFlyoutSeparator());
        bool canDelete = !_scanning && !_cleanupBusy && !_refreshing && node.Parent is not null && !node.IsLink && !node.IsReleased;
        Add("删除（移动到回收站）", () => _ = DeleteNodeAsync(node, true), canDelete);
        Add("删除（永久删除）", () => _ = DeleteNodeAsync(node, false), canDelete);
        return menu;
    }
    private async Task DeleteNodeAsync(SpaceNode node, bool recycle)
    {
        if (_session is null || _scanning || _cleanupBusy || _refreshing || node.IsReleased) return;
        var session = _session;
        string target = node.FullPath;
        var dialog = Dialog(recycle ? "移动到回收站？" : "永久删除？", $"{node.FullPath}\n\n{ScanResult.FormatBytes(node.Bytes)} · {node.FileCount:N0} 个文件\n" +
            (recycle ? "Windows 将处理回收站操作；无法回收时会另行提示。" : "此操作无法通过回收站恢复。"), recycle ? "移动到回收站" : "永久删除");
        if (await dialog.ShowAsync() != ContentDialogResult.Primary || _leaving || node.IsReleased || _session != session || _scanning || _cleanupBusy || _refreshing) return;
        _cleanupBusy = true; DisposeWatcher(); SetScanUi();
        try
        {
            var owner = App.MainWindow is null ? IntPtr.Zero : WinRT.Interop.WindowNative.GetWindowHandle(App.MainWindow);
            await SpaceFileOperations.DeleteAsync(session.Root.FullPath, target, recycle, owner);
            SpaceScanService.RemoveNode(session, node); _selection = null; Treemap.Reset();
            foreach (var tab in ViewTabs.TabItems.OfType<TabViewItem>())
                if (tab.Tag is ViewState state)
                    for (var ancestor = state.Node; ancestor is not null; ancestor = ancestor.Parent)
                        if (ReferenceEquals(ancestor, node)) { state.Node = node.Parent!; break; }
            Report(recycle ? "已移动到回收站。回收站清空前仍占用磁盘空间。" : "已永久删除。", false);
        }
        catch (Exception ex) { Report(ex.Message, true); }
        finally { _cleanupBusy = false; if (!_leaving) { SetScanUi(); UpdateMap(); StartWatcher(); } }
    }
    private async void RescanFolder(object sender, RoutedEventArgs e) { if (_view is not null) await RefreshNodeAsync(_view); }
    private async void ExportReport(object sender, RoutedEventArgs e)
    {
        if (_session is null || _view is null) return;
        var session = _session; var view = _view; var filter = _filter;
        try
        {
            string? path = Win32Dialogs.PickSave("CSV 报告\0*.csv\0\0", "csv", "空间分析.csv"); if (path is null) return;
            string root = view.FullPath;
            await Task.Run(() =>
            {
                using var writer = new StreamWriter(path, false, new UTF8Encoding(true));
                writer.WriteLine("路径,大小（字节）,文件数,类型,修改时间,标记");
                lock (session.SyncRoot)
                    foreach (var row in SpaceScanService.Descendants(view))
                        if (filter.IsEmpty || filter.Matches(row)) writer.WriteLine($"{Csv(row.FullPath)},{row.Bytes},{row.FileCount},{(row.IsDirectory ? "目录" : "文件")},{row.Modified:O},{row.Tag}");
            });
            Report($"报告已导出：{root}", false);
        }
        catch (Exception ex) { Report(ex.Message, true); }
    }
    private static string Csv(string value) => "\"" + (value.Length > 0 && "=+-@".Contains(value[0]) ? "'" : "") + value.Replace("\"", "\"\"") + "\"";
    private async void ShowScanLog(object sender, RoutedEventArgs e)
    {
        if (_session is null) return;
        string text; lock (_session.SyncRoot) text = _session.Errors.Count > 0 ? string.Join("\n", _session.Errors) : "没有读取错误。目录链接会跳过，不展开实际目标。";
        await ShowTextAsync("扫描日志（仅保存在内存中，最多 200 条）", text);
    }

    private void ModeChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    { if (CleanupPane is null) return; _mode = sender.SelectedItem == JunkMode ? 1 : sender.SelectedItem == WindowsMode ? 2 : 0; UpdateMode(); }
    private void UpdateMode()
    {
        AnalysisPane.Visibility = _mode == 0 ? Visibility.Visible : Visibility.Collapsed;
        CleanupPane.Visibility = _mode == 0 ? Visibility.Collapsed : Visibility.Visible;
        bool windows = _mode == 2;
        CleanupTitle.Text = windows ? "Windows 简易清理" : "应用缓存与临时文件";
        CleanupDescription.Text = windows ? "直接调用 Windows 磁盘清理 API，扫描系统提供的清理项目。清理范围在下方选择；执行前可预览说明。" : "使用内置 Winapp2 规则库。双击项目可预览文件与注册表明细；含警告的项目默认不选中。";
        CleanupScanButton.Content = windows ? "扫描 Windows 清理项" : "扫描垃圾";
        UpdateRulesItem.Visibility = AppxItem.Visibility = windows ? Visibility.Collapsed : Visibility.Visible;
        _checked.Clear();
        CleanupCategory.Items.Clear();
        if (windows)
        {
            foreach (var drive in DrivePicker.Items.OfType<SpaceDriveChoice>().GroupBy(d => Path.GetPathRoot(d.Path)).Select(g => g.First()))
                CleanupCategory.Items.Add(Path.GetPathRoot(drive.Path)!);
            if (_cleanupVolume.Length > 0 && CleanupCategory.Items.Contains(_cleanupVolume)) CleanupCategory.SelectedItem = _cleanupVolume;
            else if (CleanupCategory.Items.Count > 0) CleanupCategory.SelectedIndex = 0;
        }
        else
        {
            CleanupCategory.Items.Add("全部分类"); foreach (var category in _junkRows.Select(r => r.Category).Distinct()) CleanupCategory.Items.Add(category);
            CleanupCategory.SelectedIndex = 0;
        }
        RenderCleanup();
        if (_mode == 0) { UpdateMap(); UpdateScanStatus(); }
    }
    private List<CleanupRow> CurrentRows => _mode == 2 ? _windowsRows : _junkRows;
    private void CleanupCategoryChanged(object sender, SelectionChangedEventArgs e)
    {
        if (CleanupList is null) return;
        if (_mode == 2 && CleanupCategory.SelectedItem is string volume && volume != _cleanupVolume)
        { _cleanupVolume = volume; _windowsRows = []; _checked.Clear(); }
        RenderCleanup();
    }
    private void RenderCleanup()
    {
        if (_mode == 0) return;
        var rows = CurrentRows.Where(r => _mode == 2 || CleanupCategory.SelectedIndex <= 0 || r.Category == CleanupCategory.SelectedItem as string).ToList();
        _updatingList = true; CleanupList.ItemsSource = rows;
        foreach (var row in rows.Where(_checked.Contains)) CleanupList.SelectedItems.Add(row);
        _updatingList = false;
        CleanupEmpty.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateCleanupSelection();
    }
    private async void ScanCleanup(object sender, RoutedEventArgs e) => await ScanCleanupAsync();
    private async Task ScanCleanupAsync()
    {
        if (_cleanupBusy || _scanning) { Report("请先停止空间扫描，再扫描清理项目。", false); return; }
        int mode = _mode; string volume = _cleanupVolume;
        if (mode == 2 && volume.Length == 0) { Report("请先选择一个磁盘。", true); return; }
        await RunCleanupOperation(async token =>
        {
            var progress = OperationProgress();
            var rows = mode == 2 ? (await WindowsCleanupService.ScanAsync(volume, progress, token)).Select(item => new CleanupRow
            { Name = item.Name, Category = "Windows", Detail = item.Description, Bytes = item.Bytes, Windows = item,
                Warning = item.Error is null ? "" : "无法扫描：" + item.Error, DefaultSelected = false }).ToList()
                : await JunkCleanupFacade.ScanAsync(progress, token);
            if (_leaving) return;
            if (mode == 2) _windowsRows = rows; else _junkRows = rows;
            _checked.Clear(); foreach (var row in rows.Where(r => r.DefaultSelected)) _checked.Add(row);
            if (mode == 1)
            { CleanupCategory.Items.Clear(); CleanupCategory.Items.Add("全部分类"); foreach (var category in rows.Select(r => r.Category).Distinct()) CleanupCategory.Items.Add(category); CleanupCategory.SelectedIndex = 0; }
            RenderCleanup();
            StatusText.Text = $"扫描完成 · {rows.Count:N0} 项 · 估算 {ScanResult.FormatBytes(rows.Sum(r => r.Bytes))}（不同规则可能重叠）";
        });
    }
    private async Task RunCleanupOperation(Func<CancellationToken, Task> action)
    {
        if (_cleanupBusy) return;
        _cleanupBusy = true; _cleanupCancellation?.Dispose(); _cleanupCancellation = new(); DisposeWatcher();
        CleanupScanButton.IsEnabled = CleanupCategory.IsEnabled = CleanupMore.IsEnabled = Modes.IsEnabled = false;
        CancelCleanupButton.Visibility = Visibility.Visible; Notice.IsOpen = false; SetScanUi(); UpdateCleanupSelection();
        try { await action(_cleanupCancellation.Token); }
        catch (OperationCanceledException) { if (!_leaving) { StatusText.Text = "操作已取消；已完成的清理不会撤销。"; } }
        catch (Exception ex) { if (!_leaving) Report(ex.Message, true); }
        finally
        {
            _cleanupBusy = false;
            if (!_leaving)
            { CleanupScanButton.IsEnabled = CleanupCategory.IsEnabled = CleanupMore.IsEnabled = Modes.IsEnabled = true; CancelCleanupButton.Visibility = Visibility.Collapsed; SetScanUi(); UpdateCleanupSelection(); StartWatcher(); }
        }
    }
    private IProgress<string> OperationProgress()
    {
        // Throttle before dispatching, rather than enqueueing one UI callback per file.
        long last = 0;
        return new ThrottledProgress(text => { long now = Environment.TickCount64; if (now - Interlocked.Read(ref last) < 120) return; Interlocked.Exchange(ref last, now);
            DispatcherQueue.TryEnqueue(() => { if (!_leaving) StatusText.Text = text; }); });
    }
    private sealed class ThrottledProgress(Action<string> report) : IProgress<string> { public void Report(string value) => report(value); }
    private void CancelCleanup(object sender, RoutedEventArgs e) => _cleanupCancellation?.Cancel();
    private void CleanupSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_updatingList) return;
        foreach (var row in e.RemovedItems.OfType<CleanupRow>()) _checked.Remove(row);
        foreach (var row in e.AddedItems.OfType<CleanupRow>()) _checked.Add(row);
        UpdateCleanupSelection();
    }
    private void UpdateCleanupSelection()
    {
        CleanupSelectionText.Text = $"已选 {_checked.Count:N0} 项 · 估算 {ScanResult.FormatBytes(_checked.Sum(r => r.Bytes))}";
        CleanButton.IsEnabled = _checked.Any(r => r.Junk is not null || r.Windows is { Error: null }) && !_cleanupBusy && !_scanning;
    }
    private void SelectAllCleanup(object sender, RoutedEventArgs e) => CleanupList.SelectAll();
    private void ClearCleanupSelection(object sender, RoutedEventArgs e) { _checked.Clear(); CleanupList.SelectedItems.Clear(); UpdateCleanupSelection(); }
    private async void PreviewCleanup(object sender, DoubleTappedRoutedEventArgs e) => await PreviewCleanupAsync();
    private async void PreviewCleanupClick(object sender, RoutedEventArgs e) => await PreviewCleanupAsync();
    private async Task PreviewCleanupAsync()
    {
        var rows = _checked.ToList();
        if (rows.Count == 0) { Report("先勾选要查看的项目，再预览明细。", false); return; }
        var text = new StringBuilder();
        foreach (var row in rows)
        {
            text.AppendLine($"{row.Name} · {row.SizeText}\n{row.Detail}\n{row.Warning}");
            if (row.Junk is { } result)
            {
                foreach (var path in result.FilesToDelete.Take(1000)) text.AppendLine(path);
                if (result.FilesToDelete.Count > 1000) text.AppendLine($"… 共 {result.FilesToDelete.Count:N0} 个文件，预览显示前 1000 个。");
                foreach (var registry in result.RegistryToDelete.Take(1000)) text.AppendLine("注册表：" + registry);
            }
            text.AppendLine();
        }
        await ShowTextAsync("清理明细", text.ToString());
    }
    private async void CleanSelected(object sender, RoutedEventArgs e)
    {
        if (_cleanupBusy || _scanning || _checked.Count == 0) return;
        var rows = _checked.ToList(); bool windows = _mode == 2; string volume = _cleanupVolume;
        string warnings = string.Join("\n", rows.Where(r => r.Warning.Length > 0).Select(r => $"{r.Name}：{r.Warning}"));
        if (await Dialog("清理选中的项目？", $"{rows.Count:N0} 项，估算 {ScanResult.FormatBytes(rows.Sum(r => r.Bytes))}。\n清理会永久删除文件或注册表项目。请先关闭相关应用。\n\n" + string.Join("、", rows.Take(15).Select(r => r.Name)) + "\n\n" + warnings, "开始清理").ShowAsync() != ContentDialogResult.Primary) return;
        await RunCleanupOperation(async token =>
        {
            if (windows)
            {
                await WindowsCleanupService.PurgeAsync(volume, rows.Where(r => r.Windows is { Error: null }).Select(r => r.Windows!), OperationProgress(), token);
                StatusText.Text = "Windows 清理请求已完成，请重新扫描确认剩余项目。";
            }
            else
            {
                var cleaner = new CleaningService(); long bytes = 0; int count = 0;
                foreach (var row in rows)
                {
                    token.ThrowIfCancellationRequested(); if (row.Junk is null) continue;
                    var cleaned = await cleaner.CleanAsync(row.Junk, OperationProgress(), token); bytes += cleaned.bytes; count += cleaned.count;
                }
                StatusText.Text = $"已清理 {count:N0} 个项目，释放 {ScanResult.FormatBytes(bytes)}；占用中或无法删除的项目已跳过，请重新扫描。";
            }
            // Invalidate analysis after cleanup instead of leaving stale rectangles actionable.
            if (_session is not null) { lock (_changeLock) _changedDirectories.Add(_session.Root.FullPath); _diskChanged = true; }
            if (windows) _windowsRows = []; else _junkRows = [];
            _checked.Clear(); RenderCleanup();
        });
    }
    private async void UpdateRules(object sender, RoutedEventArgs e)
    {
        await RunCleanupOperation(async token =>
        { await JunkCleanerDatabase.UpdateAllFromRepoAsync(OperationProgress(), token); _junkRows = []; _checked.Clear(); RenderCleanup(); StatusText.Text = "规则库已更新，请重新扫描。"; });
    }
    private async void ManageAppx(object sender, RoutedEventArgs e)
    {
        List<AppxEntry> entries = [];
        await RunCleanupOperation(async token =>
        { var all = await AppxService.ParseDatabaseAsync(JunkCleanerDatabase.GetEffectivePath(JunkDatabaseKind.Winappx)); entries = await AppxService.ScanInstalledAsync(all); token.ThrowIfCancellationRequested(); });
        if (_leaving || entries.Count == 0) { if (!_leaving) Report("没有发现可卸载的预装应用，或扫描已取消。", false); return; }
        var list = new ListView { SelectionMode = ListViewSelectionMode.Multiple, IsMultiSelectCheckBoxEnabled = true, MaxHeight = 380 };
        foreach (var entry in entries) list.Items.Add(entry.Name + (entry.Warning is null ? "" : " · " + entry.Warning));
        var dialog = Dialog("预装应用（默认不勾选）", "", "卸载选中的应用"); dialog.Content = list;
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        var chosen = list.SelectedItems.Cast<string>().Select(name => entries[list.Items.IndexOf(name)]).ToList();
        if (chosen.Count == 0) return;
        if (await Dialog("确认卸载？", string.Join("\n", chosen.Select(e => e.Name + "：" + e.Warning)), "卸载应用").ShowAsync() != ContentDialogResult.Primary) return;
        await RunCleanupOperation(async token =>
        { int count = 0; foreach (var entry in chosen) { token.ThrowIfCancellationRequested(); if (await AppxService.RemoveAsync(entry)) count++; } StatusText.Text = $"已卸载 {count}/{chosen.Count} 个预装应用。"; });
    }
    private ContentDialog Dialog(string title, string text, string? primary = null) => new()
    { Title = title, Content = new ScrollViewer { Content = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }, MaxHeight = 360 },
        PrimaryButtonText = primary ?? "", CloseButtonText = primary is null ? "关闭" : "取消", DefaultButton = ContentDialogButton.Close,
        XamlRoot = XamlRoot, RequestedTheme = ActualTheme };
    private async Task ShowTextAsync(string title, string text)
    {
        var box = new TextBox { Text = text, IsReadOnly = true, AcceptsReturn = true, TextWrapping = TextWrapping.NoWrap, MaxHeight = 420, MinWidth = 400 };
        var dialog = Dialog(title, "", "复制全部"); dialog.Content = box;
        dialog.PrimaryButtonClick += (_, e) => { ClipboardService.TrySetText(text); e.Cancel = true; };
        await dialog.ShowAsync();
    }
    private void Report(string message, bool error)
    { if (_leaving) return; Notice.Message = message; Notice.Severity = error ? InfoBarSeverity.Error : InfoBarSeverity.Informational; Notice.IsOpen = true; }

    private readonly HashSet<string> _changedDirectories = new(StringComparer.OrdinalIgnoreCase);
    private readonly object _changeLock = new();
    private bool _refreshing;
    private void DisposeWatcher() { _watcher?.Dispose(); _watcher = null; }
    private void StartWatcher()
    {
        DisposeWatcher();
        if (_session is not { Complete: true } || !WatchToggle.IsChecked || _leaving || _scanning) return;
        try
        {
            _watcher = new FileSystemWatcher(_session.Root.FullPath) { IncludeSubdirectories = true, NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Size, InternalBufferSize = 32768 };
            void Changed(string path)
            {
                string directory = Path.GetDirectoryName(path) ?? _session.Root.FullPath;
                lock (_changeLock) { if (_changedDirectories.Count < 64) _changedDirectories.Add(directory); Interlocked.Exchange(ref _lastDiskChange, Environment.TickCount64); _diskChanged = true; }
            }
            _watcher.Created += (_, e) => Changed(e.FullPath); _watcher.Deleted += (_, e) => Changed(e.FullPath); _watcher.Changed += (_, e) => Changed(e.FullPath);
            _watcher.Renamed += (_, e) => { Changed(e.OldFullPath); Changed(e.FullPath); };
            _watcher.Error += (_, _) => DispatcherQueue.TryEnqueue(() => Report("目录变化过多，实时监视可能遗漏了项目；请手动重新扫描。", false));
            _watcher.EnableRaisingEvents = true;
        }
        catch (Exception ex) { Report("实时监视不可用：" + ex.Message, false); }
    }
    private void ToggleWatching(object sender, RoutedEventArgs e) { if (WatchToggle.IsChecked) StartWatcher(); else DisposeWatcher(); }
    private async Task RefreshChangedFoldersAsync()
    {
        if (_refreshing || _session is null) return;
        string[] paths; lock (_changeLock) { paths = _changedDirectories.ToArray(); _changedDirectories.Clear(); }
        // Resolve notifications to cached parents; a newly created/deleted directory is scanned through its nearest surviving ancestor.
        var nodes = new HashSet<SpaceNode>();
        lock (_session.SyncRoot)
            foreach (var path in paths)
            { var node = SpaceScanService.FindNearestDirectory(_session.Root, path); if (node is not null) nodes.Add(node); }
        if (nodes.Count == 0 && _diskChanged) nodes.Add(_session.Root);
        foreach (var node in nodes.Where(n => !nodes.Any(other => other != n && n.FullPath.StartsWith(other.FullPath.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase))).Take(16))
        { if (_leaving || _scanning) break; await RefreshNodeAsync(node); }
    }
    private async Task RefreshNodeAsync(SpaceNode node)
    {
        if (node.IsAggregate && node.Parent is not null) node = node.Parent;
        if (_session is null || _scanning || _cleanupBusy || _refreshing || node.IsReleased) return;
        _refreshing = true; SetScanUi();
        try
        {
            string? viewPath = _view?.FullPath;
            await SpaceScanService.RefreshAsync(_session, node, StreamsToggle.IsChecked, _scanCancellation?.Token ?? default);
            if (_leaving) return;
            lock (_session.SyncRoot) _view = SpaceScanService.FindNearestDirectory(_session.Root, viewPath ?? _session.Root.FullPath) ?? _session.Root;
            _selection = null; Treemap.Reset(); _treeRoots.Clear(); _treeRows.Clear(); _treeRoot = null;
            UpdateMap(); UpdateScanStatus();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Report("刷新失败：" + ex.Message, false); }
        finally { _refreshing = false; if (!_leaving) SetScanUi(); else _session?.ReleaseIndex(); }
    }

    private void SaveView()
    {
        if (_activeTab?.Tag is ViewState state && _view is not null)
        { state.Node = _view; state.Filter = FilterBox.Text; state.Depth = _depth; }
    }
    private void AddTab(ViewState state)
    {
        _changingView = true;
        var tab = new TabViewItem { Header = Path.GetFileName(Path.TrimEndingDirectorySeparator(state.Node.FullPath)), Tag = state, Content = new Grid() };
        ViewTabs.TabItems.Add(tab); ViewTabs.SelectedItem = tab; _activeTab = tab;
        ViewTabs.Visibility = ViewTabs.TabItems.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        _changingView = false;
    }
    private void AddView(TabView sender, object args)
    {
        if (_session is null || _view is null) return;
        SaveView(); AddTab(new ViewState(_session, _view, FilterBox.Text, _depth)); UpdateMap();
    }
    private void NewView(object sender, RoutedEventArgs args) => AddView(ViewTabs, args);
    private void ViewChanged(object sender, SelectionChangedEventArgs args)
    {
        if (_changingView || ViewTabs.SelectedItem is not TabViewItem { Tag: ViewState state } tab) return;
        SaveView(); DisposeWatcher(); _activeTab = tab; _session = state.Session;
        lock (_session.SyncRoot) _view = SpaceScanService.FindNearestDirectory(_session.Root, state.Node.FullPath) ?? _session.Root;
        _depth = state.Depth; FilterBox.Text = state.Filter; _filter = new SpaceFilter(state.Filter); _selection = null;
        UpdateMap(); UpdateScanStatus(); SetScanUi(); StartWatcher();
    }
    private void CloseView(TabView sender, TabViewTabCloseRequestedEventArgs args)
    {
        if (_scanning || _refreshing || sender.TabItems.Count <= 1) return;
        sender.TabItems.Remove(args.Tab);
        ViewTabs.Visibility = ViewTabs.TabItems.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
    }
    private void OnFilesDropped(IReadOnlyList<string> files)
    {
        if (_leaving || _mode != 0 || _scanning || _cleanupBusy) return;
        string? path = files.FirstOrDefault(Directory.Exists);
        if (path is null && files.FirstOrDefault(File.Exists) is { } file) path = Path.GetDirectoryName(file);
        if (path is null) return;
        string target = path;
        DispatcherQueue.TryEnqueue(async () =>
        { var choice = new SpaceDriveChoice(target, target); DrivePicker.Items.Add(choice); DrivePicker.SelectedItem = choice; await ScanAsync(target); });
    }
}
}

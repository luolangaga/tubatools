using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.Foundation;
using Windows.Graphics;
using Windows.UI;

namespace TubaWinUi3.Services;

public sealed class DiskSpaceAnalyzerTool : IBuiltinTool
{
    public string Id => "disk-space-analyzer";
    public string Name => "磁盘分析";
    public string Description => "可视化磁盘空间占用，以树状图展示文件夹大小，类似 SpaceSniffer。";
    public string Glyph => "\uEDA2";
    public string Category => "系统工具";
    public BuiltinToolKind Kind => BuiltinToolKind.Dialog;

    public async Task ExecuteAsync(BuiltinToolContext context)
    {
        var drives = DriveInfo.GetDrives()
            .Where(d => d.IsReady && d.DriveType == DriveType.Fixed)
            .ToList();

        if (drives.Count == 0)
        {
            var d = new ContentDialog { Title = "磁盘分析", Content = "未检测到可用磁盘。", CloseButtonText = "关闭", XamlRoot = context.XamlRoot, RequestedTheme = ThemeService.CurrentElementTheme };
            await d.ShowAsync();
            return;
        }

        if (drives.Count == 1) { OpenWindow(drives[0].RootDirectory.FullName); return; }

        var sp = new StackPanel { Spacing = 12, Padding = new Thickness(4) };
        sp.Children.Add(new TextBlock { Text = "选择要分析的磁盘：", FontSize = 14, Opacity = 0.8 });
        var list = new StackPanel { Spacing = 8 };
        foreach (var drive in drives)
        {
            var txt = $"{drive.Name}  {Fmt(drive.AvailableFreeSpace)} 可用 / {Fmt(drive.TotalSize)} 总计";
            var btn = new Button { Content = new TextBlock { Text = txt, FontSize = 13 }, HorizontalAlignment = HorizontalAlignment.Stretch, Padding = new Thickness(16, 10, 16, 10), Tag = drive.RootDirectory.FullName };
            btn.Click += (s, _) => { if (s is Button b) OpenWindow((string)b.Tag); };
            list.Children.Add(btn);
        }
        sp.Children.Add(list);
        var dlg = new ContentDialog { Title = "磁盘分析", Content = sp, CloseButtonText = "取消", XamlRoot = context.XamlRoot, RequestedTheme = ThemeService.CurrentElementTheme };
        dlg.Resources["ContentDialogMaxWidth"] = 500;
        await dlg.ShowAsync();
    }

    private static void OpenWindow(string path)
    {
        var w = new Window();
        w.AppWindow.Title = "磁盘分析";
        w.AppWindow.Resize(new SizeInt32(1000, 700));
        w.AppWindow.Move(new PointInt32(60, 60));
        w.Content = new AnalyzerPage(path, w);
        w.Activate();
    }

    internal static string Fmt(long bytes)
    {
        string[] u = ["B", "KB", "MB", "GB", "TB"];
        double s = bytes; int i = 0;
        while (s >= 1024 && i < u.Length - 1) { s /= 1024; i++; }
        return $"{s:0.#} {u[i]}";
    }
}

file sealed class AnalyzerPage : Page
{
    private readonly Window _win;
    private TNode? _root;
    private TNode? _cur;
    private readonly Stack<TNode> _nav = [];
    private long _diskTotal;
    private long _diskFree;
    private Canvas _cv = null!;
    private StackPanel _bcPanel = null!;
    private TextBox _bcEdit = null!;
    private TextBlock _st = null!;
    private TextBlock _tip = null!;
    private ProgressBar _pb = null!;
    private Border _tipBox = null!;
    private MenuFlyout _ctxMenu = null!;
    private CancellationTokenSource? _cts;
    private long _pBytes;
    private int _pItems;
    private TNode? _hoveredNode;
    private bool _isEditingBc;

    private static readonly SolidColorBrush NormalBorder = new(Color.FromArgb(30, 0, 0, 0));
    private static readonly SolidColorBrush HoverBorder = new(Color.FromArgb(220, ThemeColors.PrimaryText.R, ThemeColors.PrimaryText.G, ThemeColors.PrimaryText.B));
    private static readonly SolidColorBrush TipBg = new(Color.FromArgb(240, ThemeColors.CardBg.R, ThemeColors.CardBg.G, ThemeColors.CardBg.B));
    private static readonly SolidColorBrush TipBorder = new(ThemeColors.BorderColor);
    private static readonly SolidColorBrush TipFg = new(ThemeColors.PrimaryText);
    private static readonly SolidColorBrush CanvasBg = new(ThemeColors.KeyboardBg);
    private static readonly SolidColorBrush LabelMain = new(Color.FromArgb(240, ThemeColors.PrimaryText.R, ThemeColors.PrimaryText.G, ThemeColors.PrimaryText.B));
    private static readonly SolidColorBrush LabelSub = new(Color.FromArgb(170, ThemeColors.PrimaryText.R, ThemeColors.PrimaryText.G, ThemeColors.PrimaryText.B));
    private static readonly SolidColorBrush FreeSpaceBg = new(ThemeColors.CardBg);
    private static readonly SolidColorBrush FreeSpaceLabel = new(Color.FromArgb(150, ThemeColors.PrimaryText.R, ThemeColors.PrimaryText.G, ThemeColors.PrimaryText.B));
    private static readonly SolidColorBrush ToolbarBg = new(ThemeColors.HeaderBg);
    private static readonly SolidColorBrush StatusBg = new(ThemeColors.HeaderBg);

    private static readonly Color[] Palette =
    [
        Color.FromArgb(255, 82, 145, 245), Color.FromArgb(255, 56, 190, 96),
        Color.FromArgb(255, 240, 82, 68),  Color.FromArgb(255, 250, 196, 28),
        Color.FromArgb(255, 178, 88, 200), Color.FromArgb(255, 18, 188, 212),
        Color.FromArgb(255, 255, 124, 76), Color.FromArgb(255, 92, 196, 104),
        Color.FromArgb(255, 136, 148, 220), Color.FromArgb(255, 255, 180, 56),
        Color.FromArgb(255, 100, 200, 160), Color.FromArgb(255, 200, 120, 180),
    ];

    public AnalyzerPage(string path, Window win)
    {
        _win = win;
        GetDiskInfo(path);
        InitUi();
        win.Closed += (_, _) => _cts?.Cancel();
        _ = ScanAsync(path);
    }

    private void GetDiskInfo(string path)
    {
        try
        {
            var root = System.IO.Path.GetPathRoot(path);
            if (root != null)
            {
                var di = new DriveInfo(root);
                _diskTotal = di.TotalSize;
                _diskFree = di.AvailableFreeSpace;
            }
        }
        catch { }
    }

    private void InitUi()
    {
        var bk = new Button
        {
            Content = new FontIcon { Glyph = "\uE72B", FontSize = 14 },
            Padding = new Thickness(8, 4, 8, 4),
            VerticalAlignment = VerticalAlignment.Center,
            CornerRadius = new CornerRadius(6),
        };
        bk.Click += (_, _) => GoBack();

        _bcPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        _bcEdit = new TextBox
        {
            FontSize = 13,
            Visibility = Visibility.Collapsed,
            VerticalAlignment = VerticalAlignment.Center,
            Padding = new Thickness(6, 2, 6, 2),
            CornerRadius = new CornerRadius(4),
        };
        _bcEdit.KeyDown += BcEdit_KeyDown;
        _bcEdit.LostFocus += BcEdit_LostFocus;

        var bcWrap = new Grid { VerticalAlignment = VerticalAlignment.Center };
        bcWrap.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bcWrap.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        bcWrap.Children.Add(_bcPanel);
        Grid.SetColumn(_bcEdit, 1); bcWrap.Children.Add(_bcEdit);

        _bcPanel.PointerPressed += BcPanel_PointerPressed;

        var rf = new Button
        {
            Content = new FontIcon { Glyph = "\uE72C", FontSize = 14 },
            Padding = new Thickness(8, 4, 8, 4),
            VerticalAlignment = VerticalAlignment.Center,
            CornerRadius = new CornerRadius(6),
        };
        rf.Click += (_, _) => { if (_cur != null) _ = ScanAsync(_cur.Path, true); };

        var top = new Grid
        {
            ColumnSpacing = 10,
            Padding = new Thickness(14, 10, 14, 8),
            Background = ToolbarBg,
            CornerRadius = new CornerRadius(0),
        };
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.Children.Add(bk);
        Grid.SetColumn(bcWrap, 1); top.Children.Add(bcWrap);
        Grid.SetColumn(rf, 2); top.Children.Add(rf);

        _cv = new Canvas { Background = CanvasBg };
        _cv.SizeChanged += (_, _) => Render();
        _cv.PointerPressed += OnPointerPressed;

        _pb = new ProgressBar { IsIndeterminate = true, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 1, 0, 1) };
        _st = new TextBlock { FontSize = 12, Opacity = 0.6, Padding = new Thickness(14, 6, 14, 8) };

        _tip = new TextBlock { FontSize = 12.5, Foreground = TipFg };
        _tipBox = new Border
        {
            Background = TipBg,
            BorderBrush = TipBorder,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 6, 10, 6),
            Child = _tip,
            Visibility = Visibility.Collapsed,
        };

        _ctxMenu = new MenuFlyout();
        var openItem = new MenuFlyoutItem { Text = "打开", Icon = new FontIcon { Glyph = "\uE8E5" } };
        openItem.Click += CtxOpen_Click;
        _ctxMenu.Items.Add(openItem);
        var delItem = new MenuFlyoutItem { Text = "删除", Icon = new FontIcon { Glyph = "\uE74D" } };
        delItem.Click += CtxDelete_Click;
        _ctxMenu.Items.Add(delItem);

        var wrap = new Grid();
        wrap.Children.Add(_cv);
        var tipCanvas = new Canvas { IsHitTestVisible = false };
        tipCanvas.Children.Add(_tipBox);
        wrap.Children.Add(tipCanvas);

        var g = new Grid();
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        g.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        g.Children.Add(top);
        Grid.SetRow(_pb, 1); g.Children.Add(_pb);
        Grid.SetRow(wrap, 2); g.Children.Add(wrap);

        var statusBorder = new Border { Background = StatusBg, Child = _st, Padding = new Thickness(0) };
        Grid.SetRow(statusBorder, 3); g.Children.Add(statusBorder);

        Content = g;
    }

    private void BcPanel_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(_bcPanel).Properties.IsMiddleButtonPressed) return;
        StartBcEdit();
        e.Handled = true;
    }

    private void StartBcEdit()
    {
        if (_cur == null || _isEditingBc) return;
        _isEditingBc = true;
        _bcEdit.Text = _cur.Path;
        _bcPanel.Visibility = Visibility.Collapsed;
        _bcEdit.Visibility = Visibility.Visible;
        _bcEdit.SelectAll();
        _bcEdit.Focus(FocusState.Programmatic);
    }

    private void BcEdit_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == Windows.System.VirtualKey.Enter)
        {
            CommitBcEdit();
            e.Handled = true;
        }
        else if (e.Key == Windows.System.VirtualKey.Escape)
        {
            CancelBcEdit();
            e.Handled = true;
        }
    }

    private void BcEdit_LostFocus(object sender, RoutedEventArgs e)
    {
        CommitBcEdit();
    }

    private void CommitBcEdit()
    {
        if (!_isEditingBc) return;
        _isEditingBc = false;
        _bcPanel.Visibility = Visibility.Visible;
        _bcEdit.Visibility = Visibility.Collapsed;

        var path = _bcEdit.Text.Trim().Trim('"');
        if (!string.IsNullOrWhiteSpace(path) && System.IO.Directory.Exists(path))
        {
            _ = ScanAsync(path);
        }
    }

    private void CancelBcEdit()
    {
        _isEditingBc = false;
        _bcPanel.Visibility = Visibility.Visible;
        _bcEdit.Visibility = Visibility.Collapsed;
    }

    private void BcPart_Click(object sender, RoutedEventArgs e)
    {
        if (sender is HyperlinkButton btn && btn.Tag is TNode node)
        {
            var list = _nav.ToList();
            list.Reverse();
            var idx = list.IndexOf(node);
            if (idx < 0) return;
            for (int i = list.Count - 1; i >= idx; i--) _nav.Pop();
            _cur = node;
            _hoveredNode = null;
            SyncUi();
            if (_win?.AppWindow != null) _win.AppWindow.Title = $"磁盘分析 - {node.Path}";
            RenderWithAnimation(null);
        }
    }

    private TNode? _ctxNode;

    private void CtxOpen_Click(object sender, RoutedEventArgs e)
    {
        if (_ctxNode == null) return;
        try
        {
            if (_ctxNode.IsFile)
            {
                var dir = System.IO.Path.GetDirectoryName(_ctxNode.Path);
                if (dir != null)
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(dir) { UseShellExecute = true, Verb = "open" });
            }
            else
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_ctxNode.Path) { UseShellExecute = true, Verb = "open" });
            }
        }
        catch { }
    }

    private async void CtxDelete_Click(object sender, RoutedEventArgs e)
    {
        if (_ctxNode == null || _cur == null) return;

        var typeText = _ctxNode.IsFile ? "文件" : "文件夹";
        var dlg = new ContentDialog
        {
            Title = "确认删除",
            Content = $"确定要删除{typeText}「{_ctxNode.Name}」吗？\n\n{_ctxNode.Path}\n\n此操作不可撤销。",
            PrimaryButtonText = "删除",
            CloseButtonText = "取消",
            DefaultButton = ContentDialogButton.Close,
            XamlRoot = Content.XamlRoot,
            RequestedTheme = ThemeService.CurrentElementTheme,
        };
        dlg.Resources["ContentDialogMaxWidth"] = 480;

        var result = await dlg.ShowAsync();
        if (result != ContentDialogResult.Primary) return;

        try
        {
            if (_ctxNode.IsFile)
                System.IO.File.Delete(_ctxNode.Path);
            else
                System.IO.Directory.Delete(_ctxNode.Path, true);
            _cur.Children.Remove(_ctxNode);
            if (_ctxNode == _hoveredNode) _hoveredNode = null;
            SyncUi();
            Render();
        }
        catch (Exception ex)
        {
            var errDlg = new ContentDialog
            {
                Title = "删除失败",
                Content = ex.Message,
                CloseButtonText = "关闭",
                XamlRoot = Content.XamlRoot,
                RequestedTheme = ThemeService.CurrentElementTheme,
            };
            await errDlg.ShowAsync();
        }
    }

    private volatile int _scanDirty;

    private async Task ScanAsync(string path, bool keepNav = false)
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var tk = _cts.Token;

        if (_pb != null) _pb.Visibility = Visibility.Visible;
        if (_st != null) _st.Text = "正在扫描…";
        if (_cv != null) _cv.Children.Clear();
        _hoveredNode = null;
        _pBytes = 0; _pItems = 0;
        _scanDirty = 0;

        GetDiskInfo(path);

        var name = System.IO.Path.GetFileName(path);
        if (string.IsNullOrEmpty(name)) name = path.TrimEnd('\\');
        var node = new TNode(name, path);
        _root = node;
        _cur = node;
        if (!keepNav) _nav.Clear();

        var renderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        renderTimer.Tick += (_, _) =>
        {
            if (tk.IsCancellationRequested) { renderTimer.Stop(); return; }
            if (Interlocked.Exchange(ref _scanDirty, 0) > 0)
                RenderIncremental();
            if (_st != null) _st.Text = $"正在扫描…  {_pItems:N0} 项  ·  {DiskSpaceAnalyzerTool.Fmt(_pBytes)}";
        };
        renderTimer.Start();

        try
        {
            var opts = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = false, ReturnSpecialDirectories = false };
            await Task.Run(() => BuildTreeIncremental(path, node, opts, tk), tk);
        }
        catch (OperationCanceledException)
        {
            renderTimer.Stop();
            if (_pb != null) _pb.Visibility = Visibility.Collapsed;
            if (_st != null) _st.Text = "扫描已取消";
            return;
        }
        catch { renderTimer.Stop(); return; }

        renderTimer.Stop();
        node.Children.Sort((a, b) => b.Size.CompareTo(a.Size));
        if (_pb != null) _pb.Visibility = Visibility.Collapsed;
        SyncUi();
        if (_win?.AppWindow != null) _win.AppWindow.Title = $"磁盘分析 - {path}";
        Render();
    }

    private void BuildTreeIncremental(string path, TNode node, EnumerationOptions opts, CancellationToken tk)
    {
        tk.ThrowIfCancellationRequested();

        var fileNodes = new List<TNode>();
        try
        {
            foreach (var f in new DirectoryInfo(path).EnumerateFiles("*", opts))
            {
                tk.ThrowIfCancellationRequested();
                try
                {
                    node.Size += f.Length;
                    node.FileCount++;
                    Interlocked.Increment(ref _pItems);
                    Interlocked.Add(ref _pBytes, f.Length);
                    var fn = new TNode(f.Name, f.FullName) { Size = f.Length, FileCount = 1, IsFile = true };
                    fileNodes.Add(fn);
                }
                catch { }
            }
        }
        catch { }

        lock (node.Children)
        {
            foreach (var fn in fileNodes) node.Children.Add(fn);
        }
        if (fileNodes.Count > 0) Interlocked.Increment(ref _scanDirty);

        try
        {
            foreach (var d in new DirectoryInfo(path).EnumerateDirectories("*", opts))
            {
                tk.ThrowIfCancellationRequested();
                try
                {
                    var child = new TNode(d.Name, d.FullName);
                    BuildTreeIncremental(d.FullName, child, opts, tk);
                    lock (node.Children)
                    {
                        node.Children.Add(child);
                    }
                    node.DirCount++;
                    node.FileCount += child.FileCount;
                    node.DirCount += child.DirCount;
                    node.Size += child.Size;
                    Interlocked.Increment(ref _scanDirty);
                }
                catch { }
            }
        }
        catch { }
    }

    private void RenderIncremental()
    {
        if (_cur == null || _cv == null) return;
        var W = _cv.ActualWidth;
        var H = _cv.ActualHeight;
        if (W <= 0 || H <= 0) return;

        var existing = new Dictionary<TNode, Border>();
        foreach (var c in _cv.Children)
        {
            if (c is Border b && b.Tag is TNode n) existing[n] = b;
        }

        var items = new List<(TNode? Node, double Ratio)>();
        List<TNode> sortedChildren;
        lock (_cur.Children)
        {
            sortedChildren = _cur.Children.OrderByDescending(c => c.Size).ToList();
        }
        var totalSize = _cur.Size;
        var showFree = _cur == _root && _diskTotal > 0;
        if (showFree) totalSize = _diskTotal;
        if (totalSize <= 0) return;

        foreach (var c in sortedChildren)
        {
            if (c.Size > 0) items.Add((c, (double)c.Size / totalSize));
        }
        var freeSize = showFree ? Math.Max(0, _diskTotal - _cur.Size) : 0;
        if (showFree && freeSize > 0) items.Add((null, (double)freeSize / totalSize));
        if (items.Count == 0) return;

        const double gap = 3;
        var rects = DoSquarifyEx(items, new Rect(gap, gap, W - gap * 2, H - gap * 2));

        var usedNodes = new HashSet<TNode?>();
        foreach (var (node, rect) in rects)
        {
            if (rect.Width < 1.5 || rect.Height < 1.5) continue;
            usedNodes.Add(node);

            var isFree = node == null;
            if (node != null && existing.TryGetValue(node, out var oldBrd))
            {
                oldBrd.Width = rect.Width;
                oldBrd.Height = rect.Height;
                Canvas.SetLeft(oldBrd, rect.X);
                Canvas.SetTop(oldBrd, rect.Y);
                var big = rect.Width > 80 && rect.Height > 42;
                var med = rect.Width > 50 && rect.Height > 26;
                if (med && oldBrd.Child is StackPanel sp)
                {
                    sp.Children.Clear();
                    sp.Children.Add(new TextBlock { Text = node.Name, FontSize = big ? 13 : 10, Foreground = LabelMain, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap });
                    sp.Children.Add(new TextBlock { Text = DiskSpaceAnalyzerTool.Fmt(node.Size), FontSize = big ? 11 : 9, Foreground = LabelSub });
                }
                else if (med)
                {
                    var sp2 = new StackPanel { Margin = new Thickness(5, 3, 5, 3) };
                    sp2.Children.Add(new TextBlock { Text = node.Name, FontSize = big ? 13 : 10, Foreground = LabelMain, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap });
                    sp2.Children.Add(new TextBlock { Text = DiskSpaceAnalyzerTool.Fmt(node.Size), FontSize = big ? 11 : 9, Foreground = LabelSub });
                    oldBrd.Child = sp2;
                }
            }
            else
            {
                var color = isFree ? ThemeColors.CardBg : NodeColor(node!);
                var brd = new Border
                {
                    Tag = node,
                    Background = new SolidColorBrush(color),
                    BorderBrush = NormalBorder,
                    BorderThickness = new Thickness(0.5),
                    CornerRadius = new CornerRadius(4),
                    Width = rect.Width,
                    Height = rect.Height,
                    Opacity = 0,
                };

                var big = rect.Width > 80 && rect.Height > 42;
                var med = rect.Width > 50 && rect.Height > 26;
                if (med)
                {
                    var sp = new StackPanel { Margin = new Thickness(5, 3, 5, 3) };
                    if (isFree)
                    {
                        sp.Children.Add(new TextBlock { Text = "空闲空间", FontSize = big ? 13 : 10, Foreground = FreeSpaceLabel, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap });
                        if (big) sp.Children.Add(new TextBlock { Text = DiskSpaceAnalyzerTool.Fmt(freeSize), FontSize = 11, Foreground = FreeSpaceLabel });
                    }
                    else
                    {
                        sp.Children.Add(new TextBlock { Text = node!.Name, FontSize = big ? 13 : 10, Foreground = LabelMain, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap });
                        sp.Children.Add(new TextBlock { Text = DiskSpaceAnalyzerTool.Fmt(node.Size), FontSize = big ? 11 : 9, Foreground = LabelSub });
                    }
                    brd.Child = sp;
                }

                Canvas.SetLeft(brd, rect.X);
                Canvas.SetTop(brd, rect.Y);
                _cv.Children.Add(brd);

                FadeInBorder(brd);
            }
        }

        var toRemove = new List<Border>();
        foreach (var kv in existing)
        {
            if (!usedNodes.Contains(kv.Key)) toRemove.Add(kv.Value);
        }
        foreach (var b in toRemove) _cv.Children.Remove(b);
    }

    private static void FadeInBorder(Border brd)
    {
        if (FastModeService.IsFastModeEnabled())
        {
            brd.Opacity = 1.0;
            return;
        }
        const int steps = 12;
        const int intervalMs = 16;
        var step = 0;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(intervalMs) };
        timer.Tick += (_, _) =>
        {
            step++;
            var t = (double)step / steps;
            brd.Opacity = t;
            if (step >= steps) { timer.Stop(); brd.Opacity = 1.0; }
        };
        timer.Start();
    }

    private TNode? FindNodeAt(Point p)
    {
        foreach (var c in _cv.Children)
        {
            if (c is not Border b || b.Tag is not TNode n) continue;
            var x = Canvas.GetLeft(b); var y = Canvas.GetTop(b);
            if (p.X >= x && p.X <= x + b.Width && p.Y >= y && p.Y <= y + b.Height) return n;
        }
        return null;
    }

    private void OnPointerPressed(object sender, PointerRoutedEventArgs e)
    {
        var props = e.GetCurrentPoint(_cv).Properties;
        var pos = e.GetCurrentPoint(_cv).Position;

        if (props.IsRightButtonPressed)
        {
            var node = FindNodeAt(pos);
            if (node != null)
            {
                _ctxNode = node;
                _ctxMenu.ShowAt(_cv, new FlyoutShowOptions { Position = pos });
            }
            e.Handled = true;
            return;
        }

        if (props.IsLeftButtonPressed)
        {
            var node = FindNodeAt(pos);
            if (node != null) DrillIn(node);
        }
    }

    protected override void OnPointerMoved(PointerRoutedEventArgs e)
    {
        base.OnPointerMoved(e);
        var pos = e.GetCurrentPoint(_cv).Position;
        var node = FindNodeAt(pos);

            if (node != _hoveredNode)
            {
                _hoveredNode = node;

                foreach (var c in _cv.Children)
                {
                    if (c is not Border b) continue;
                    if (b.Tag == node)
                    {
                        b.BorderBrush = HoverBorder;
                        b.BorderThickness = new Thickness(1.5);
                        b.CornerRadius = new CornerRadius(4);
                        if (b.Background is SolidColorBrush sb && b.Tag is TNode)
                        {
                            var bc = sb.Color;
                            b.Background = new SolidColorBrush(Color.FromArgb(bc.A, (byte)Math.Min(255, bc.R + 18), (byte)Math.Min(255, bc.G + 18), (byte)Math.Min(255, bc.B + 18)));
                        }
                    }
                    else
                    {
                        b.BorderBrush = NormalBorder;
                        b.BorderThickness = new Thickness(0.5);
                        b.CornerRadius = new CornerRadius(4);
                        if (b.Tag is TNode tn)
                        {
                            b.Background = new SolidColorBrush(NodeColor(tn));
                        }
                        else if (b.Background is SolidColorBrush sb2)
                        {
                            b.Background = new SolidColorBrush(FreeSpaceBg.Color);
                        }
                    }
                }

            if (node != null)
            {
                var pct = _cur != null ? (double)node.Size / _cur.Size * 100 : 0;
                var typeTag = node.IsFile ? "文件" : "文件夹";
                _tip.Text = $"{node.Name}  ·  {typeTag}  ·  {DiskSpaceAnalyzerTool.Fmt(node.Size)}  ·  {pct:0.#}%";
                _tipBox.Visibility = Visibility.Visible;
            }
            else
            {
                _tipBox.Visibility = Visibility.Collapsed;
            }
        }

        if (_tipBox.Visibility == Visibility.Visible)
        {
            var p = e.GetCurrentPoint(_cv).Position;
            Canvas.SetLeft(_tipBox, Math.Max(4, Math.Min(p.X + 14, _cv.ActualWidth - 260)));
            Canvas.SetTop(_tipBox, Math.Max(4, Math.Min(p.Y + 14, _cv.ActualHeight - 40)));
        }
    }

    private void DrillIn(TNode node)
    {
        if (node.IsFile) return;
        if (node.DirCount == 0 && node.FileCount == 0 && node.Children.Count == 0) return;

        Rect? source = null;
        foreach (var c in _cv.Children)
        {
            if (c is Border b && b.Tag is TNode n && n == node)
            {
                source = new Rect(Canvas.GetLeft(b), Canvas.GetTop(b), b.Width, b.Height);
                break;
            }
        }

        if (_cur != null) _nav.Push(_cur);
        _cur = node;
        _hoveredNode = null;
        SyncUi();
        if (_win?.AppWindow != null) _win.AppWindow.Title = $"磁盘分析 - {node.Path}";
        RenderWithAnimation(source);
    }

    private void GoBack()
    {
        if (_nav.Count == 0) return;
        _cur = _nav.Pop();
        _hoveredNode = null;
        SyncUi();
        if (_win?.AppWindow != null && _cur != null) _win.AppWindow.Title = $"磁盘分析 - {_cur.Path}";
        RenderWithAnimation(null);
    }

    private void SyncUi()
    {
        if (_cur == null) return;

        _bcPanel.Children.Clear();
        var navList = _nav.ToList();
        navList.Reverse();
        for (int i = 0; i < navList.Count; i++)
        {
            if (i > 0)
            {
                _bcPanel.Children.Add(new TextBlock { Text = " › ", FontSize = 13, Opacity = 0.5, VerticalAlignment = VerticalAlignment.Center });
            }
            var btn = new HyperlinkButton
            {
                Content = new TextBlock { Text = navList[i].Name, FontSize = 13, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 160 },
                Padding = new Thickness(4, 0, 4, 0),
                VerticalAlignment = VerticalAlignment.Center,
                Tag = navList[i],
            };
            btn.Click += BcPart_Click;
            _bcPanel.Children.Add(btn);
        }

        if (navList.Count > 0)
        {
            _bcPanel.Children.Add(new TextBlock { Text = " › ", FontSize = 13, Opacity = 0.5, VerticalAlignment = VerticalAlignment.Center });
        }

        var curTb = new TextBlock
        {
            Text = _cur.Name,
            FontSize = 13,
            Opacity = 0.85,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis,
        };
        ToolTipService.SetToolTip(curTb, _cur.Path);
        curTb.PointerPressed += (s, e) =>
        {
            if (e.GetCurrentPoint(curTb).Properties.IsMiddleButtonPressed) { StartBcEdit(); e.Handled = true; }
        };
        _bcPanel.Children.Add(curTb);

        var parts = new List<string>();
        if (_diskTotal > 0)
        {
            var used = _diskTotal - _diskFree;
            var usedPct = (double)used / _diskTotal * 100;
            parts.Add($"磁盘 {DiskSpaceAnalyzerTool.Fmt(used)} / {DiskSpaceAnalyzerTool.Fmt(_diskTotal)} ({usedPct:0.#}%)");
        }
        parts.Add($"{_cur.FileCount:N0} 文件  ·  {_cur.DirCount:N0} 文件夹");
        _st.Text = string.Join("  ·  ", parts);
    }

    private void Render()
    {
        RenderWithAnimation(null);
    }

    private void RenderWithAnimation(Rect? originRect)
    {
        _cv.Children.Clear();
        _hoveredNode = null;
        if (_cur == null) return;

        var W = _cv.ActualWidth;
        var H = _cv.ActualHeight;
        if (W <= 0 || H <= 0) return;

        const double gap = 3;

        var totalSize = _cur.Size;
        var freeSize = 0L;
        var showFree = _cur == _root && _diskTotal > 0;
        if (showFree)
        {
            totalSize = _diskTotal;
            freeSize = Math.Max(0, _diskTotal - _cur.Size);
        }

        if (totalSize == 0) return;

        var items = new List<(TNode? Node, double Ratio)>();
        foreach (var c in _cur.Children) items.Add((c, (double)c.Size / totalSize));
        if (showFree && freeSize > 0) items.Add((null, (double)freeSize / totalSize));
        if (items.Count == 0) return;

        var rects = DoSquarifyEx(items, new Rect(gap, gap, W - gap * 2, H - gap * 2));

        var animate = originRect.HasValue && !FastModeService.IsFastModeEnabled();
        var ox = originRect?.X ?? W / 2;
        var oy = originRect?.Y ?? H / 2;

        foreach (var (node, rect) in rects)
        {
            if (rect.Width < 1.5 || rect.Height < 1.5) continue;

            var isFree = node == null;
            var color = isFree ? ThemeColors.CardBg : NodeColor(node!);
            var brd = new Border
            {
                Tag = isFree ? null : node!,
                Background = new SolidColorBrush(color),
                BorderBrush = NormalBorder,
                BorderThickness = new Thickness(0.5),
                CornerRadius = new CornerRadius(4),
            };

            var big = rect.Width > 80 && rect.Height > 42;
            var med = rect.Width > 50 && rect.Height > 26;

            if (med)
            {
                var sp = new StackPanel { Margin = new Thickness(5, 3, 5, 3) };
                if (isFree)
                {
                    sp.Children.Add(new TextBlock { Text = "空闲空间", FontSize = big ? 13 : 10, Foreground = FreeSpaceLabel, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap });
                    if (big) sp.Children.Add(new TextBlock { Text = DiskSpaceAnalyzerTool.Fmt(freeSize), FontSize = 11, Foreground = FreeSpaceLabel });
                }
                else
                {
                    sp.Children.Add(new TextBlock { Text = node!.Name, FontSize = big ? 13 : 10, Foreground = LabelMain, TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap });
                    sp.Children.Add(new TextBlock { Text = DiskSpaceAnalyzerTool.Fmt(node.Size), FontSize = big ? 11 : 9, Foreground = LabelSub });
                }
                brd.Child = sp;
            }

            if (animate)
            {
                double sx, sy, sw, sh;
                if (originRect.HasValue)
                {
                    sx = originRect.Value.X;
                    sy = originRect.Value.Y;
                    sw = originRect.Value.Width;
                    sh = originRect.Value.Height;
                    var cx = sx + sw / 2;
                    var cy = sy + sh / 2;
                    var blend = 0.15;
                    sx = cx - (cx - rect.X) * blend;
                    sy = cy - (cy - rect.Y) * blend;
                    sw = rect.Width * blend;
                    sh = rect.Height * blend;
                }
                else
                {
                    var cx = W / 2;
                    var cy = H / 2;
                    var blend = 0.25;
                    sx = cx - (cx - rect.X) * blend;
                    sy = cy - (cy - rect.Y) * blend;
                    sw = rect.Width * blend;
                    sh = rect.Height * blend;
                }
                Canvas.SetLeft(brd, sx);
                Canvas.SetTop(brd, sy);
                brd.Width = sw;
                brd.Height = sh;
                brd.Opacity = 0.3;
                brd.Tag = isFree ? new AnimState { Node = null, StartX = sx, StartY = sy, TargetX = rect.X, TargetY = rect.Y, StartW = sw, StartH = sh, TargetW = rect.Width, TargetH = rect.Height }
                                 : new AnimState { Node = node!, StartX = sx, StartY = sy, TargetX = rect.X, TargetY = rect.Y, StartW = sw, StartH = sh, TargetW = rect.Width, TargetH = rect.Height };
            }
            else
            {
                Canvas.SetLeft(brd, rect.X);
                Canvas.SetTop(brd, rect.Y);
                brd.Width = rect.Width;
                brd.Height = rect.Height;
            }

            _cv.Children.Add(brd);
        }

        if (animate) RunZoomAnimation();
    }

    private void RunZoomAnimation()
    {
        const int steps = 28;
        const int intervalMs = 16;
        var step = 0;
        var borders = _cv.Children.OfType<Border>().Where(b => b.Tag is AnimState).ToList();

        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(intervalMs) };
        timer.Tick += (_, _) =>
        {
            step++;
            var t = (double)step / steps;
            var eased = t < 0.5
                ? 4 * t * t * t
                : 1 - Math.Pow(-2 * t + 2, 3) / 2;

            foreach (var b in borders)
            {
                var s = (AnimState)b.Tag;
                Canvas.SetLeft(b, s.StartX + (s.TargetX - s.StartX) * eased);
                Canvas.SetTop(b, s.StartY + (s.TargetY - s.StartY) * eased);
                b.Width = s.StartW + (s.TargetW - s.StartW) * eased;
                b.Height = s.StartH + (s.TargetH - s.StartH) * eased;
                b.Opacity = 0.3 + 0.7 * eased;
            }

            if (step >= steps)
            {
                timer.Stop();
                foreach (var b in borders)
                {
                    b.Tag = ((AnimState)b.Tag!).Node;
                    b.Opacity = 1.0;
                }
            }
        };
        timer.Start();
    }

    private static List<(TNode? Node, Rect Rect)> DoSquarifyEx(List<(TNode? Node, double Ratio)> items, Rect bounds)
    {
        var result = new List<(TNode? Node, Rect Rect)>();
        if (items.Count == 0) return result;
        var totalArea = bounds.Width * bounds.Height;
        var remaining = items.Select(it => (it.Node, Area: it.Ratio * totalArea)).ToList();
        LayRowEx(remaining, bounds, result);
        return result;
    }

    private static void LayRowEx(List<(TNode? Node, double Area)> items, Rect bounds, List<(TNode? Node, Rect Rect)> result)
    {
        if (items.Count == 0) return;
        if (bounds.Width <= 0 || bounds.Height <= 0) return;
        if (items.Count == 1) { result.Add((items[0].Node, bounds)); return; }

        var shortSide = Math.Min(bounds.Width, bounds.Height);
        if (shortSide <= 0) return;

        var row = new List<(TNode? Node, double Area)> { items[0] };
        var bestW = WorstAspect(row, shortSide);

        for (int i = 1; i < items.Count; i++)
        {
            var test = new List<(TNode? Node, double Area)>(row) { items[i] };
            var tw = WorstAspect(test, shortSide);
            if (tw <= bestW) { row.Add(items[i]); bestW = tw; }
            else
            {
                EmitRowEx(row, bounds, result);
                var rowArea = row.Sum(r => r.Area);
                var totalArea = bounds.Width * bounds.Height;
                if (totalArea <= 0) return;
                var frac = Math.Min(1.0, rowArea / totalArea);
                if (frac >= 1.0) return;
                var wide = bounds.Width >= bounds.Height;
                var nb = wide
                    ? new Rect(bounds.X + bounds.Width * frac, bounds.Y, Math.Max(0, bounds.Width * (1 - frac)), bounds.Height)
                    : new Rect(bounds.X, bounds.Y + bounds.Height * frac, bounds.Width, Math.Max(0, bounds.Height * (1 - frac)));
                LayRowEx(items.Skip(i).ToList(), nb, result);
                return;
            }
        }
        EmitRowEx(row, bounds, result);
    }

    private static void EmitRowEx(List<(TNode? Node, double Area)> row, Rect bounds, List<(TNode? Node, Rect Rect)> result)
    {
        var rowArea = row.Sum(r => r.Area);
        if (rowArea <= 0) return;
        var wide = bounds.Width >= bounds.Height;
        var rowLen = wide ? rowArea / bounds.Height : rowArea / bounds.Width;
        if (rowLen <= 0) return;

        var off = 0.0;
        foreach (var (node, area) in row)
        {
            if (area <= 0) continue;
            Rect r;
            if (wide) { var h = area / rowLen; r = new Rect(bounds.X, bounds.Y + off, rowLen, Math.Max(0, h)); off += h; }
            else { var w = area / rowLen; r = new Rect(bounds.X + off, bounds.Y, Math.Max(0, w), rowLen); off += w; }
            result.Add((node, r));
        }
    }

    private static double WorstAspect(List<(TNode? Node, double Area)> row, double side)
    {
        if (row.Count == 0 || side <= 0) return double.MaxValue;
        var total = row.Sum(r => r.Area);
        if (total <= 0) return double.MaxValue;
        var rowLen = total / side;
        if (rowLen <= 0) return double.MaxValue;
        var worst = 0.0;
        foreach (var (_, area) in row)
        {
            if (area <= 0) continue;
            var other = area / rowLen;
            worst = Math.Max(worst, Math.Max(rowLen / other, other / rowLen));
        }
        return worst;
    }

    private static Color NodeColor(TNode node)
    {
        var h = 0;
        foreach (var c in node.Name) h = (h * 31 + c) & 0x7FFFFFFF;
        var baseC = Palette[h % Palette.Length];
        var sf = Math.Min(1.0, Math.Log10(Math.Max(node.Size, 1)) / 10.5);
        var br = 0.42 + sf * 0.58;
        return Color.FromArgb(255, (byte)Math.Clamp(baseC.R * br, 0, 255), (byte)Math.Clamp(baseC.G * br, 0, 255), (byte)Math.Clamp(baseC.B * br, 0, 255));
    }
}

file sealed class TNode
{
    public string Name { get; }
    public string Path { get; }
    public long Size { get; set; }
    public int FileCount { get; set; }
    public int DirCount { get; set; }
    public bool IsFile { get; set; }
    public List<TNode> Children { get; } = [];
    public TNode(string name, string path) { Name = name; Path = path; }
}

file sealed class AnimState
{
    public TNode? Node;
    public double StartX, StartY, TargetX, TargetY;
    public double StartW, StartH, TargetW, TargetH;
}

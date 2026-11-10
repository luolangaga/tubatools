using System.Runtime.InteropServices;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using TubaWinUi3.Services;
using Windows.Graphics;

namespace TubaWinUi3.Pages;

public sealed partial class WindowControlInspector : Window
{
    private readonly WindowManagerService.WindowInfo _target;
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private readonly Dictionary<TreeViewNode, WindowControlTree.Node> _nodes = [];
    private WindowControlTree.Snapshot? _snapshot;
    private WindowControlOverlay? _overlay;
    private CancellationTokenSource? _read;
    private string _status = "";
    private bool _closed, _stale, _rebuildingTree;
    private long? _rescanAt;
    private (int Width, int Height, uint Dpi)? _lastLayout;

    internal WindowControlInspector(WindowManagerService.WindowInfo target)
    {
        _target = target;
        InitializeComponent();
        AppWindow.Title = "控件检查器";
        BackdropService.ApplyBackdrop(this);
        ApplyTheme(ThemeService.CurrentElementTheme);
        ThemeService.ThemeChanged += ApplyTheme;
        TargetLabel.Text = target.Label;
        ToolTipService.SetToolTip(TargetLabel, target.Label);
        _timer.Tick += (_, _) => UpdateOverlay();
        Closed += OnClosed;
    }

    internal void Open()
    {
        var state = WindowManagerService.GetInspectionState(_target);
        var bounds = state?.Bounds ?? _target.Bounds;
        var display = DisplayArea.GetFromWindowId(Win32Interop.GetWindowIdFromWindow(_target.Handle), DisplayAreaFallback.Primary);
        if (display is not null)
        {
            var work = display.WorkArea;
            // Size the inspector for its monitor even if the inspected app is DPI-unaware.
            AppWindow.Move(new PointInt32(work.X, work.Y));
            uint dpi = GetDpiForWindow(Win32Interop.GetWindowFromWindowId(AppWindow.Id));
            double scale = (dpi == 0 ? 96 : dpi) / 96.0;
            int gap = (int)(10 * scale);
            int width = Math.Min(work.Width, (int)(420 * scale));
            int height = Math.Min(work.Height, (int)(650 * scale));
            int right = work.X + work.Width;
            int x = right - width;
            if (bounds.Right + gap + width <= right) x = bounds.Right + gap;
            else if (bounds.Left - gap - width >= work.X) x = bounds.Left - gap - width;
            else if (bounds.Left - work.X > right - bounds.Right) x = work.X;
            int y = Math.Clamp(bounds.Top, work.Y, work.Y + work.Height - height);
            AppWindow.MoveAndResize(new RectInt32(Math.Clamp(x, work.X, right - width), y, width, height));
        }

        if (state is { Visible: true }) SetForegroundWindow(_target.Handle);
        Activate();
        _timer.Start();
        _ = RefreshAsync();
    }

    private void ApplyTheme(ElementTheme theme)
    {
        Layout.RequestedTheme = theme;
        BuiltinWindowHelper.ApplyTitleBarTheme(this);
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private async Task RefreshAsync()
    {
        if (_read is not null || _closed || WindowManagerService.GetInspectionState(_target) is not { Visible: true }) return;
        using var read = new CancellationTokenSource();
        _read = read;
        _rescanAt = null;
        RefreshButton.IsEnabled = false;
        _overlay?.Hide();
        Status.Text = "正在读取控件树…";
        try
        {
            var snapshot = await WindowControlTree.ReadAsync(_target, read.Token);
            if (read.IsCancellationRequested) return;
            _snapshot = snapshot;
            _stale = false;
            _rescanAt = null;
            _lastLayout = GetLayout(snapshot.Window);
            ReplaceTree(snapshot.Root);
            _status = snapshot.Partial ? $"已读取 {snapshot.Count} 个节点，部分层级未能读取。" :
                snapshot.Count == 1 ? "仅发现窗口根节点。目标应用可能没有公开内部控件。" :
                $"{snapshot.Count} 个节点 · 点击节点查看控件位置和名称。";
            UpdateOutlines();
        }
        catch (OperationCanceledException) when (read.IsCancellationRequested) { }
        catch (WindowControlTree.WindowChangedException)
        {
            _stale = true;
            _rescanAt = Environment.TickCount64 + 250;
        }
        catch (Exception ex) when (ex is COMException or InvalidOperationException)
        {
            if (!read.IsCancellationRequested)
            {
                _stale = true;
                _status = ex is COMException ? "无法读取控件树。目标应用可能未响应或限制了访问。" : ex.Message;
                System.Diagnostics.Debug.WriteLine(ex);
            }
        }
        finally
        {
            _read = null;
            if (!_closed) UpdateOverlay();
        }
    }

    private void ReplaceTree(WindowControlTree.Node root)
    {
        var previousSelection = ControlsTree.SelectedNode;
        TreeViewNode? newSelection = null;
        var newNodes = new Dictionary<TreeViewNode, WindowControlTree.Node>();
        var tree = CreateNode(root, ControlsTree.RootNodes.FirstOrDefault(), isRoot: true);
        _rebuildingTree = true;
        try
        {
            ControlsTree.RootNodes.Clear();
            _nodes.Clear();
            foreach (var pair in newNodes)
                _nodes.Add(pair.Key, pair.Value);
            ControlsTree.RootNodes.Add(tree);
            if (newSelection is not null) ControlsTree.SelectedNode = newSelection;
        }
        finally { _rebuildingTree = false; }

        TreeViewNode CreateNode(WindowControlTree.Node node, TreeViewNode? previous, bool isRoot = false)
        {
            if (previous is not null && (!_nodes.TryGetValue(previous, out var old) ||
                node.Type != old.Type || node.ClassName != old.ClassName ||
                node.AutomationId != old.AutomationId || node.Name != old.Name))
                previous = null;

            var item = new TreeViewNode { Content = node.Label, IsExpanded = previous?.IsExpanded ?? isRoot };
            newNodes.Add(item, node);
            if (previous is not null && previous.Equals(previousSelection))
                newSelection = item;
            for (int i = 0; i < node.Children.Count; i++)
            {
                var oldChild = previous is not null && i < previous.Children.Count ? previous.Children[i] : null;
                item.Children.Add(CreateNode(node.Children[i], oldChild));
            }
            return item;
        }
    }

    private WindowControlTree.Node? SelectedNode =>
        ControlsTree.SelectedNode is { } selected && _nodes.TryGetValue(selected, out var node) ? node : null;

    private void ControlsTree_SelectionChanged(TreeView sender, TreeViewSelectionChangedEventArgs args) => UpdateOutlines();
    private void ShowAll_Changed(object sender, RoutedEventArgs args) => UpdateOutlines();

    private void UpdateOutlines()
    {
        if (_rebuildingTree || _snapshot is not { } snapshot || _stale) return;
        try
        {
            _overlay ??= new WindowControlOverlay();
            _overlay.SetOutlines(snapshot, SelectedNode, ShowAll.IsChecked == true);
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(ex);
            _status = "控件树已读取，无法创建边框覆盖层。";
        }
        UpdateOverlay();
    }

    private void UpdateOverlay()
    {
        var state = WindowManagerService.GetInspectionState(_target);
        RefreshButton.IsEnabled = _read is null && state is { Visible: true };
        if (state is not { } current)
        {
            _read?.Cancel();
            _timer.Stop();
            _overlay?.Dispose();
            _overlay = null;
            SetStatus("目标窗口已关闭。");
        }
        else if (!current.Visible)
        {
            _overlay?.Hide();
            SetStatus("目标窗口当前不可见。");
        }
        else
        {
            var layout = GetLayout(current);
            if (_lastLayout is { } previous && previous != layout)
            {
                _stale = true;
                _rescanAt = Environment.TickCount64 + 250;
            }
            _lastLayout = layout;

            if (_stale || _read is not null) _overlay?.Hide();
            else _overlay?.UpdatePosition(_target.Handle, current);

            if (_read is null && _rescanAt is long due && Environment.TickCount64 >= due)
                _ = RefreshAsync();

            SetStatus(_read is not null ? "正在读取控件树…" :
                _rescanAt is not null ? "窗口布局发生变化，正在等待自动刷新…" : _status);
        }

        if (SelectedNode is { } node && _snapshot is { } data && state is { } geometry)
        {
            string location = node.Offscreen || !geometry.Visible || node.Bounds.Width <= 0 || node.Bounds.Height <= 0 ? "无可见边界" :
                _stale ? "位置待刷新" :
                $"位置 {node.Bounds.X + geometry.Bounds.Left - data.Window.Bounds.Left}, " +
                $"{node.Bounds.Y + geometry.Bounds.Top - data.Window.Bounds.Top} · {node.Bounds.Width} × {node.Bounds.Height} px";
            string detail = $"{node.Label}\n{location}\n类名：{node.ClassName}    ID：{node.AutomationId}";
            if (ControlDetails.Text != detail) ControlDetails.Text = detail;
        }
        else if (ControlDetails.Text.Length > 0) ControlDetails.Text = "";
    }

    private static (int Width, int Height, uint Dpi) GetLayout(WindowManagerService.InspectionState state) =>
        (state.Bounds.Width, state.Bounds.Height, state.Dpi);

    private void SetStatus(string message)
    {
        if (Status.Text != message) Status.Text = message;
    }

    private void OnClosed(object sender, WindowEventArgs args)
    {
        _closed = true;
        _read?.Cancel();
        _timer.Stop();
        _overlay?.Dispose();
        ThemeService.ThemeChanged -= ApplyTheme;
    }

    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(nint hwnd);
    [DllImport("user32.dll")] private static extern uint GetDpiForWindow(nint hwnd);
}

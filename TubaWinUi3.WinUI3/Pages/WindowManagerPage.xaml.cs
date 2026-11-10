using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media.Imaging;
using TubaWinUi3.Services;
using Windows.ApplicationModel.DataTransfer;

namespace TubaWinUi3.Pages;

public sealed partial class WindowManagerPage : Page
{
    private WindowManagerService.WindowInfo? _selected;
    private WindowControlInspector? _controlInspector;
    private readonly DispatcherTimer _pickerTimer = new() { Interval = TimeSpan.FromMilliseconds(60) };
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private nint _candidate;
    private bool _refreshing;
    private readonly Dictionary<(nint Handle, uint Pid, string Path), Task<byte[]?>> _iconLoads = new();

    public WindowManagerPage()
    {
        InitializeComponent();
        Picker.AddHandler(PointerPressedEvent, new PointerEventHandler(Picker_PointerPressed), true);
        _pickerTimer.Tick += Picker_Tick;
        _refreshTimer.Tick += (_, _) => { _refreshTimer.Stop(); RefreshSelected(); };
        Loaded += (_, _) => RefreshList();
    }
    private void Refresh_Click(object sender, RoutedEventArgs e)
    {
        _iconLoads.Clear();
        RefreshList();
        RefreshSelected();
        if (_selected is { } selected) _ = UpdateSelectedIconAsync(selected);
    }
    private void RefreshList()
    {
        _refreshing = true;
        try
        {
            var items = WindowManagerService.List();
            WindowList.ItemsSource = items;
            WindowList.SelectedItem = items.FirstOrDefault(w => w.Handle == _selected?.Handle);
        }
        finally { _refreshing = false; }
    }
    private void WindowList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!_refreshing && WindowList.SelectedItem is WindowManagerService.WindowInfo info) SelectWindow(info.Handle);
    }
    private void SelectWindow(nint h)
    {
        try
        {
            var next = WindowManagerService.Read(h);
            if (_selected is null || _selected.Handle != next.Handle || _selected.ProcessId != next.ProcessId ||
                _selected.ThreadId != next.ThreadId || _selected.ClassName != next.ClassName)
                _controlInspector?.Close();
            _selected = next;
            Details.Text = _selected.Details;
            _ = UpdateSelectedIconAsync(_selected);
            Actions.IsEnabled = CopyButton.IsEnabled = InspectButton.IsEnabled = true;
            TopmostButton.Content = _selected.Topmost ? "取消置顶" : "置顶";
            StatusBar.IsOpen = false;
            RefreshList();
        }
        catch (Exception ex) { Report(ex.Message, InfoBarSeverity.Warning); }
    }
    private void RefreshSelected()
    {
        if (_selected is null) return;
        try
        {
            var current = WindowManagerService.Read(_selected.Handle);
            if (current.ProcessId != _selected.ProcessId || current.ThreadId != _selected.ThreadId || current.ClassName != _selected.ClassName)
                throw new InvalidOperationException("原窗口已关闭，请重新选择。");
            _selected = current;
            Details.Text = current.Details;
            TopmostButton.Content = current.Topmost ? "取消置顶" : "置顶";
        }
        catch (Exception ex)
        {
            _selected = null;
            _controlInspector?.Close();
            SelectedIcon.Source = null;
            SelectedIconHost.Visibility = Visibility.Collapsed;
            Actions.IsEnabled = CopyButton.IsEnabled = InspectButton.IsEnabled = false;
            Details.Text = "窗口已不可用，请重新选择。";
            Report(ex.Message, InfoBarSeverity.Warning);
        }
    }
    private Task<byte[]?> GetIconAsync(WindowManagerService.WindowInfo info)
    {
        var key = (info.Handle, info.ProcessId, info.Path);
        if (!_iconLoads.TryGetValue(key, out var task))
        {
            // Limit the page cache while retaining icons across list rebuilds.
            if (_iconLoads.Count >= 256) _iconLoads.Clear();
            _iconLoads[key] = task = WindowIconService.LoadPngAsync(info.Handle, info.Path);
        }
        return task;
    }
    private static async Task<BitmapImage?> DecodeIconAsync(byte[]? png)
    {
        if (png is null) return null;
        using var stream = new MemoryStream(png);
        var image = new BitmapImage();
        await image.SetSourceAsync(stream.AsRandomAccessStream());
        return image;
    }
    private void WindowIcon_Loaded(object sender, RoutedEventArgs e) { if (sender is Grid grid) _ = LoadRowIconAsync(grid); }
    private void WindowIcon_DataContextChanged(FrameworkElement sender, DataContextChangedEventArgs e) { if (sender is Grid grid && grid.IsLoaded) _ = LoadRowIconAsync(grid); }
    private async Task LoadRowIconAsync(Grid grid)
    {
        if (grid.DataContext is not WindowManagerService.WindowInfo info) return;
        var fallback = (FontIcon)grid.Children[0];
        var image = (Image)grid.Children[1];
        image.Source = null;
        fallback.Visibility = Visibility.Visible;
        try
        {
            var source = await DecodeIconAsync(await GetIconAsync(info));
            if (!grid.IsLoaded || !ReferenceEquals(grid.DataContext, info)) return;
            image.Source = source;
            fallback.Visibility = source is null ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"窗口列表图标加载失败：{ex.Message}"); }
    }
    private async Task UpdateSelectedIconAsync(WindowManagerService.WindowInfo info)
    {
        SelectedIcon.Source = null;
        SelectedIconHost.Visibility = Visibility.Visible;
        SelectedIconFallback.Visibility = Visibility.Visible;
        try
        {
            var source = await DecodeIconAsync(await GetIconAsync(info));
            if (!IsLoaded || _selected?.Handle != info.Handle || _selected.ProcessId != info.ProcessId) return;
            SelectedIcon.Source = source;
            SelectedIconFallback.Visibility = source is null ? Visibility.Visible : Visibility.Collapsed;
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"选中窗口图标加载失败：{ex.Message}"); }
    }
    private void Picker_PointerPressed(object sender, PointerRoutedEventArgs e)
    {
        if (!e.GetCurrentPoint(Picker).Properties.IsLeftButtonPressed) return;
        _candidate = 0;
        ProtectedCursor = InputSystemCursor.Create(InputSystemCursorShape.Cross);
        PickerHint.Text = "正在选择窗口，松开鼠标确认，按 Esc 取消。";
        _pickerTimer.Start();
        e.Handled = true;
    }
    private void Picker_Tick(object? sender, object e)
    {
        if (WindowManagerService.EscapePressed) { StopPicking(); return; }
        if (!WindowManagerService.LeftPressed)
        {
            var h = WindowManagerService.UnderCursor();
            StopPicking();
            SelectWindow(h);
            return;
        }
        var candidate = WindowManagerService.UnderCursor();
        if (candidate == _candidate) return;
        _candidate = candidate;
        try { PickerHint.Text = WindowManagerService.Read(candidate).Label; }
        catch { PickerHint.Text = "将十字拖到其他应用窗口，松开选择。"; }
    }
    private void StopPicking()
    {
        _pickerTimer.Stop();
        ProtectedCursor = null;
        PickerHint.Text = "按 Esc 取消。也可以在下方列表选择窗口。";
    }
    private async void Action_Click(object sender, RoutedEventArgs e)
    {
        if (_selected is null || sender is not Button { Tag: string action }) return;
        var target = _selected;
        if (action == "close")
        {
            var dialog = new ContentDialog
            {
                XamlRoot = XamlRoot, Title = "关闭此窗口？",
                Content = $"{target.Title}\n{target.ProcessName}（PID {target.ProcessId}）\n\n将向应用发送正常关闭请求，请先保存工作。",
                PrimaryButtonText = "关闭窗口", CloseButtonText = "取消", DefaultButton = ContentDialogButton.Close
            };
            if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        }
        try
        {
            WindowManagerService.Apply(target, action);
            Report("已发送窗口操作。若窗口未变化，可能是目标应用限制了此操作。", InfoBarSeverity.Success);
            _refreshTimer.Start();
        }
        catch (Exception ex) { Report($"操作失败：{ex.Message}", InfoBarSeverity.Error); }
    }
    private void Copy_Click(object sender, RoutedEventArgs e)
    {
        RefreshSelected();
        if (_selected is null) return;
        try { var copied = ClipboardService.TrySetText(_selected.Details); Report(copied.Success ? "窗口信息已复制。" : "复制失败，请稍后重试。", copied.Success ? InfoBarSeverity.Success : InfoBarSeverity.Error); }
        catch (Exception ex) { Report(ex.Message, InfoBarSeverity.Error); }
    }
    private void Report(string message, InfoBarSeverity severity) { StatusBar.Message = message; StatusBar.Severity = severity; StatusBar.IsOpen = true; }
    private void Inspect_Click(object sender, RoutedEventArgs e)
    {
        RefreshSelected();
        if (_selected is not { } target) return;
        if (_controlInspector is { } inspector)
        {
            inspector.Activate();
            return;
        }

        _controlInspector = new WindowControlInspector(target);
        _controlInspector.Closed += (_, _) => _controlInspector = null;
        _controlInspector.Open();
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        StopPicking();
        _refreshTimer.Stop();
        _controlInspector?.Close();
    }
}

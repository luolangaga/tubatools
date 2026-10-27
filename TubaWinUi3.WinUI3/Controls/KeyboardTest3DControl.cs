using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;
using TubaWinUi3.Services;

namespace TubaWinUi3.Controls;

/// <summary>Local 3D keyboard with a lazily-created, original 2D fallback.</summary>
public sealed class KeyboardTest3DControl : UserControl
{
    private readonly Grid _stage = new();
    private readonly TextBlock _status = new() { Text = "正在准备 3D 键盘…", FontSize = 12, TextWrapping = TextWrapping.Wrap };
    private readonly ToggleSwitch _legacySwitch = new() { OffContent = "3D 键盘", OnContent = "2D 备用版", IsTabStop = false, MinWidth = 120 };
    private readonly ComboBox _layout = new() { Width = 140, IsTabStop = false };
    private readonly TextBlock _coverage = new() { Text = "已检测 0 / 105 键", FontSize = 14 };
    private readonly TextBlock _lastKey = new() { Text = "最近按键：等待按键", FontSize = 14 };
    private readonly TextBlock _cameraState = new() { Text = "半俯视 · 全景", FontSize = 14 };
    private readonly StackPanel _statistics = new() { Orientation = Orientation.Horizontal, Spacing = 24 };
    private WebView2? _web;
    private KeyboardTestControl? _legacy;
    private KeyboardTestInput? _input;
    private Microsoft.UI.Dispatching.DispatcherQueueTimer? _loadTimeout;
    private bool _ready, _closed;
    private int _generation;

    public KeyboardTest3DControl()
    {
        _layout.Items.Add("全尺寸键盘");
        _layout.Items.Add("紧凑键盘 · TKL");
        _layout.SelectedIndex = 0;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_layout, "键盘布局");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(_legacySwitch, "切换 2D 备用版");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(_legacySwitch, "KeyboardLegacyMode");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(_layout, "KeyboardLayout");
        _layout.SelectionChanged += (_, _) => Post(new { type = "layout", compact = _layout.SelectedIndex == 1 });
        var panorama = new Button { Content = "全景视角", IsTabStop = false };
        panorama.Click += (_, _) => Post(new { type = "panorama" });
        var reset = new Button { Content = "重新检测", IsTabStop = false };
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(panorama, "KeyboardPanorama");
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAutomationId(reset, "KeyboardReset");
        reset.Click += (_, _) => Post(new { type = "reset" });
        var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
        actions.Children.Add(_layout); actions.Children.Add(panorama); actions.Children.Add(reset);
        var toolbar = new Grid { ColumnSpacing = 12 };
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        toolbar.Children.Add(actions); Grid.SetColumn(_legacySwitch, 1); toolbar.Children.Add(_legacySwitch);
        _statistics.Children.Add(_coverage); _statistics.Children.Add(_lastKey); _statistics.Children.Add(_cameraState);
        var sceneSurface = (Border)Microsoft.UI.Xaml.Markup.XamlReader.Load("<Border xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' Background='{ThemeResource CardBackgroundFillColorDefaultBrush}' BorderBrush='{ThemeResource CardStrokeColorDefaultBrush}' BorderThickness='1' CornerRadius='8' />");
        sceneSurface.Child = _stage;
        var root = new Grid { Margin = new Thickness(24, 0, 24, 20), RowSpacing = 12 };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.Children.Add(toolbar); Grid.SetRow(_statistics, 1); root.Children.Add(_statistics);
        Grid.SetRow(sceneSurface, 2); root.Children.Add(sceneSurface);
        Grid.SetRow(_status, 3); root.Children.Add(_status);
        Content = root;
        _legacySwitch.Toggled += (_, _) => { _layout.IsEnabled = panorama.IsEnabled = reset.IsEnabled = !_legacySwitch.IsOn; SwitchMode(); };
        Loaded += (_, _) => { if (!_closed) SwitchMode(); };
        Unloaded += (_, _) => Cleanup();
        ActualThemeChanged += (_, _) => SendTheme();
    }

    private async void SwitchMode()
    {
        int generation = ++_generation;
        Stop3D();
        _legacy?.Cleanup(); _legacy = null;
        _stage.Children.Clear();
        _statistics.Visibility = _legacySwitch.IsOn ? Visibility.Collapsed : Visibility.Visible;
        if (_closed) return;
        if (_legacySwitch.IsOn)
        {
            _legacy = new KeyboardTestControl();
            _stage.Children.Add(new ScrollViewer { Content = _legacy, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
            _status.Text = "2D 备用版 · 使用原版键盘测试，可随时切回 3D。";
            return;
        }
        _status.Text = "正在准备 3D 键盘…";
        var web = new WebView2 { DefaultBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0) };
        var loaded = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        RoutedEventHandler? onLoaded = null;
        onLoaded = (_, _) => { web.Loaded -= onLoaded; loaded.TrySetResult(); };
        web.Loaded += onLoaded;
        _web = web; _stage.Children.Add(web);
        try
        {
            // A newly inserted WebView2 can complete Ensure before its native host exists.
            // Wait for this WebView's Loaded event, rather than just its parent's event.
            await loaded.Task.WaitAsync(TimeSpan.FromSeconds(10));
            if (_closed || generation != _generation) return;
            await web.EnsureCoreWebView2Async(await WebView2EnvironmentService.GetAsync());
            if (_closed || generation != _generation) return;
            var core = web.CoreWebView2 ?? throw new InvalidOperationException("WebView2 未完成初始化");
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.SetVirtualHostNameToFolderMapping("keyboard-test.local", Path.Combine(AppContext.BaseDirectory, "Assets", "KeyboardTest"), CoreWebView2HostResourceAccessKind.DenyCors);
            core.WebMessageReceived += OnMessage;
            core.NavigationCompleted += OnNavigationCompleted;
            core.ProcessFailed += OnProcessFailed;
            _loadTimeout = DispatcherQueue.CreateTimer();
            _loadTimeout.Interval = TimeSpan.FromSeconds(15);
            _loadTimeout.IsRepeating = false;
            _loadTimeout.Tick += (_, _) => Fail("3D 场景加载超时");
            _loadTimeout.Start();
            web.Source = new Uri("https://keyboard-test.local/index.html");
        }
        catch (Exception ex) { if (generation == _generation && !_closed) Fail(ex.ToString()); }
        finally { web.Loaded -= onLoaded; }
    }

    private void OnNavigationCompleted(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    { if (!args.IsSuccess) Fail("3D 页面无法加载"); }
    private void OnProcessFailed(CoreWebView2 sender, CoreWebView2ProcessFailedEventArgs args) => Fail("3D 渲染进程已停止");

    private void OnMessage(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        if (_closed || !args.Source.StartsWith("https://keyboard-test.local/", StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            using var json = JsonDocument.Parse(args.WebMessageAsJson);
            switch (json.RootElement.GetProperty("type").GetString())
            {
                case "ready":
                    if (_ready) break;
                    _loadTimeout?.Stop();
                    _ready = true; SendTheme();
                    Post(new { type = "layout", compact = _layout.SelectedIndex == 1 });
                    _input ??= new KeyboardTestInput(DispatcherQueue);
                    _input.KeyChanged += OnKey;
                    _status.Text = "按实体键盘检测 · 松开后 1 秒回到全景 · 点击模型仅预览 · 检测期间请用鼠标操作界面";
                    break;
                case "error": Fail("当前设备无法创建 3D 场景"); break;
                case "stats":
                    var stats = json.RootElement;
                    _coverage.Text = $"已检测 {stats.GetProperty("count").GetInt32()} / {stats.GetProperty("total").GetInt32()} 键";
                    int held = stats.GetProperty("held").GetInt32();
                    _lastKey.Text = $"最近按键：{stats.GetProperty("last").GetString()}" + (held > 0 ? $" · 同时按住 {held} 键" : "");
                    _cameraState.Text = stats.GetProperty("view").GetString();
                    break;
            }
        }
        catch (Exception ex) { Fail(ex.Message); }
    }

    private void OnKey(int key, bool down, bool repeat) => Post(new { type = "key", key, down, repeat });
    private void SendTheme() => Post(new { type = "theme", dark = ActualTheme == ElementTheme.Dark, accent = $"#{ThemeColors.AccentBlue.R:X2}{ThemeColors.AccentBlue.G:X2}{ThemeColors.AccentBlue.B:X2}" });
    private void Post(object message)
    {
        if (!_ready || _closed) return;
        try { _web?.CoreWebView2?.PostWebMessageAsJson(JsonSerializer.Serialize(message)); }
        catch (System.Runtime.InteropServices.COMException) { Fail("3D 场景连接已断开"); }
    }

    private void Fail(string reason)
    {
        System.Diagnostics.Debug.WriteLine($"[KeyboardTest3D] {reason}");
        if (_closed || _legacySwitch.IsOn) return;
        _legacySwitch.IsOn = true;
        _status.Text = "3D 暂不可用，已切换到 2D 备用版。可关闭右上角开关重试。";
    }

    private void Stop3D()
    {
        _ready = false;
        _loadTimeout?.Stop(); _loadTimeout = null;
        if (_input is { } input) { input.KeyChanged -= OnKey; input.Dispose(); _input = null; }
        if (_web is { } web)
        {
            if (web.CoreWebView2 is { } core)
            { core.WebMessageReceived -= OnMessage; core.NavigationCompleted -= OnNavigationCompleted; core.ProcessFailed -= OnProcessFailed; }
            web.Close(); _web = null;
        }
    }

    public void Cleanup()
    {
        if (_closed) return;
        _closed = true; ++_generation;
        Stop3D(); _legacy?.Cleanup();
    }
}

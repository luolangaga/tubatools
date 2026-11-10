using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using TubaWinUi3.Services;
using TubaWinUi3.Services.AiAgent;
using Colors = Microsoft.UI.Colors;

namespace TubaWinUi3.Controls;

/// <summary>
/// A canvas *message*: one page the assistant sent, shown inline in the conversation exactly
/// like a chat bubble. Height follows the page's own content height (with a min/max window),
/// and the same bubble can expand to a full-window overlay.
/// </summary>
internal sealed class CanvasBubble : UserControl
{
    private const double MinInlineHeight = 180;
    private const double FallbackInlineHeight = 460;
    private const double ViewportMargin = 190;

    private readonly Border _frame;
    private readonly WebView2 _view = new() { HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Stretch };
    private readonly Grid _stage;
    // A Grid (not a StackPanel): only a Grid hands leftover height to the page area in
    // full screen, which is what makes the WebView2 fill the window.
    private readonly Grid _card = new();
    private readonly ProgressRing _loading = new() { Width = 20, Height = 20, IsActive = false, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly StackPanel _empty = new() { Spacing = 8, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(32) };
    private readonly TextBlock _emptyTitle = new() { FontSize = 13, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _emptyHint = new() { FontSize = 12, TextAlignment = TextAlignment.Center, TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock _title = new() { FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
    private readonly FontIcon _titleIcon = new() { Glyph = "\uE7F4", FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _status = new() { FontSize = 11, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Button _retry = new() { Visibility = Visibility.Collapsed, FontSize = 11, Padding = new Thickness(10, 4, 10, 4), MinHeight = 28 };
    private readonly Button _expand;
    private readonly DispatcherTimer _reload = new() { Interval = TimeSpan.FromMilliseconds(400) };

    private FileSystemWatcher? _watcher;
    private string? _sessionId;
    private string? _directory;
    private string? _relativePath;
    private string _titleText = "";
    private bool _initialized, _disposed, _pendingReload, _fullScreen;
    private DateTime? _lastStamp;
    private double _contentHeight = FallbackInlineHeight;
    private double _inlineMax = 560;

    public event Action<string>? PromptRequested;
    public event Action<string>? InsertRequested;
    public event Action<string>? OpenExternalRequested;
    public event Action? InsertImageRequested;
    public event Action? RefreshRequested;

    /// <summary>Raised after entering/leaving the full-window view so the page can show its host.</summary>
    public event Action<bool>? FullScreenChanged;

    private static Brush Brush(string key) => (Brush)Application.Current.Resources[key];
    private static string L(string key, string fallback) => AgentText.L(key, fallback);

    public CanvasBubble()
    {
        _title.Foreground = Brush("TextFillColorSecondaryBrush");
        _titleIcon.Foreground = Brush("TextFillColorSecondaryBrush");
        _status.Foreground = Brush("TextFillColorTertiaryBrush");
        _emptyTitle.Foreground = Brush("TextFillColorTertiaryBrush");
        _emptyHint.Foreground = Brush("TextFillColorTertiaryBrush");
        _empty.Children.Add(new FontIcon { Glyph = "\uE7F4", FontSize = 28, Foreground = Brush("TextFillColorTertiaryBrush"), HorizontalAlignment = HorizontalAlignment.Center });
        _empty.Children.Add(_emptyTitle);
        _empty.Children.Add(_emptyHint);
        _retry.Content = L("CanvasRetry", "刷新画布");
        _retry.Click += (_, _) => { ReattachWatcher(); _ = ReloadAsync(true); };
        _empty.Children.Add(_retry);

        _view.DefaultBackgroundColor = Colors.Transparent;
        // No ScrollViewer around the WebView2: a ScrollViewer measures its content with an
        // infinite extent, which leaves the browser at zero height. The page scrolls itself.
        _stage = new Grid
        {
            Height = FallbackInlineHeight,
            Background = Brush("CardBackgroundFillColorDefaultBrush"),
            Children = { _view, _loading, _empty }
        };

        _expand = IconButton("CanvasFullScreen", "全屏", "\uE740", "AgentCanvasFullScreen", (_, _) => ToggleFullScreen());
        _card.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _card.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        _card.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _card.Children.Add(BuildHeader());
        Grid.SetRow(_stage, 1);
        _card.Children.Add(_stage);
        var footer = BuildFooter();
        Grid.SetRow(footer, 2);
        _card.Children.Add(footer);

        _frame = new Border
        {
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = Brush("CardStrokeColorDefaultBrush"),
            Background = Brush("CardBackgroundFillColorDefaultBrush"),
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Top,
            Child = _card
        };

        _root.Children.Add(_frame);
        Content = _root;
        _reload.Tick += (_, _) => { _reload.Stop(); if (_pendingReload) _ = ReloadAsync(false); };
        Unloaded += (_, _) => { _reload.Stop(); DetachWatcher(); };
        // Esc leaves the full-window view; the page itself never sees the key.
        PreviewKeyDown += (_, e) =>
        {
            if (_fullScreen && e.Key == Windows.System.VirtualKey.Escape) { e.Handled = true; ExitFullScreen(); }
        };
    }

    private FrameworkElement BuildHeader()
    {
        var grid = new Grid { ColumnSpacing = 6, Padding = new Thickness(12, 3, 6, 3), MinHeight = 34 };
        grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new() { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new() { Width = GridLength.Auto });

        var caption = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
        caption.Children.Add(_titleIcon);
        caption.Children.Add(_title);
        grid.Children.Add(caption);
        Grid.SetColumn(_status, 2);
        grid.Children.Add(_status);

        var actions = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        actions.Children.Add(IconButton("CanvasRefresh", "刷新画布", "\uE72C", "AgentCanvasRefresh", (_, _) => { ReattachWatcher(); _ = ReloadAsync(true); }));
        actions.Children.Add(IconButton("CanvasImage", "插入图片", "\uEB9F", "AgentCanvasImage", (_, _) => InsertImageRequested?.Invoke()));
        actions.Children.Add(MenuButton());
        actions.Children.Add(_expand);
        Grid.SetColumn(actions, 3);
        grid.Children.Add(actions);

        return new StackPanel
        {
            Children =
            {
                grid,
                new Border { BorderThickness = new Thickness(0, 0, 0, 1), BorderBrush = Brush("CardStrokeColorDefaultBrush") }
            }
        };
    }

    private FrameworkElement BuildFooter() => new Border
    {
        BorderThickness = new Thickness(0, 1, 0, 0),
        BorderBrush = Brush("CardStrokeColorDefaultBrush"),
        Padding = new Thickness(12, 5, 12, 6),
        Child = new TextBlock
        {
            Text = L("CanvasHint", "页面里的按钮可以直接和助手对话"),
            FontSize = 11,
            TextWrapping = TextWrapping.Wrap,
            Foreground = Brush("TextFillColorTertiaryBrush")
        }
    };

    private static void StyleIcon(Button button)
    {
        button.Background = new SolidColorBrush(Colors.Transparent);
        button.BorderThickness = new Thickness(0);
        button.Padding = new Thickness(6, 0, 6, 0);
        button.MinWidth = 28;
        button.MinHeight = 28;
        button.HorizontalContentAlignment = HorizontalAlignment.Center;
        button.VerticalContentAlignment = VerticalAlignment.Center;
    }

    private Button IconButton(string key, string fallback, string glyph, string automationId, RoutedEventHandler click)
    {
        var button = new Button { Content = new FontIcon { Glyph = glyph, FontSize = 12 } };
        StyleIcon(button);
        ToolTipService.SetToolTip(button, L(key, fallback));
        AutomationProperties.SetName(button, L(key, fallback));
        AutomationProperties.SetAutomationId(button, automationId);
        button.Click += click;
        return button;
    }

    private Button MenuButton()
    {
        var button = new Button { Content = new FontIcon { Glyph = "\uE712", FontSize = 12 } };
        StyleIcon(button);
        ToolTipService.SetToolTip(button, L("CanvasMenu", "画布选项"));
        AutomationProperties.SetName(button, L("CanvasMenu", "画布选项"));
        AutomationProperties.SetAutomationId(button, "AgentCanvasMenu");
        var menu = new MenuFlyout();
        menu.Items.Add(new MenuFlyoutItem { Text = L("CanvasOpenFolder", "打开画布文件夹"), Icon = new FontIcon { Glyph = "\uE8B7" } });
        ((MenuFlyoutItem)menu.Items[^1]).Click += (_, _) => OpenFolder();
        menu.Items.Add(new MenuFlyoutItem { Text = L("CanvasOpenBrowser", "在浏览器中打开"), Icon = new FontIcon { Glyph = "\uE774" } });
        ((MenuFlyoutItem)menu.Items[^1]).Click += (_, _) => OpenInBrowser();
        button.Flyout = menu;
        return button;
    }

    // ---- full screen -----------------------------------------------------------------------

    private Grid? _overlay;
    private readonly Grid _root = new();

    /// <summary>
    /// Panel supplied by the page (a Grid spanning the whole page). The card moves here for
    /// full screen. A host that is part of the same visual tree is reliable; probing
    /// <c>XamlRoot.Content</c> is not, and retrying it in a dispatcher loop locks the UI thread.
    /// </summary>
    public Panel? OverlayHost { get; set; }

    public bool IsFullScreen => _fullScreen;

    /// <summary>
    /// Lifts the card into the page's overlay panel. The WebView2 is re-parented, never rebuilt,
    /// so the page keeps its scroll position and interactive state; exiting puts it back.
    /// </summary>
    private void ToggleFullScreen()
    {
        if (_fullScreen) { ExitFullScreen(); return; }
        if (OverlayHost is not { } host) return;

        _root.Children.Remove(_frame);
        _overlay = new Grid
        {
            Background = Brush("SolidBackgroundFillColorBaseBrush"),
            Padding = new Thickness(24, 18, 24, 20),
            Children = { _frame }
        };
        _frame.CornerRadius = new CornerRadius(10);
        _frame.VerticalAlignment = VerticalAlignment.Stretch;
        _fullScreen = true;
        host.Children.Add(_overlay);
        // The card grid's star row supplies the height; the page area must not pin one.
        _stage.Height = double.NaN;
        UpdateExpandGlyph();
        FullScreenChanged?.Invoke(true);
        _view.Focus(FocusState.Programmatic);
    }

    private void ExitFullScreen()
    {
        ExitFullScreenCore();
    }

    /// <summary>Leaves full screen; called by the page too because WebView2 swallows Esc.</summary>
    public void ExitFullScreenPublic() => ExitFullScreenCore();

    private void ExitFullScreenCore()
    {
        if (!_fullScreen) return;
        _fullScreen = false;
        if (_overlay is not null)
        {
            _overlay.Children.Clear();
            if (_overlay.Parent is Panel overlayHost) overlayHost.Children.Remove(_overlay);
            _overlay = null;
        }
        _frame.CornerRadius = new CornerRadius(8);
        _frame.VerticalAlignment = VerticalAlignment.Top;
        if (!_root.Children.Contains(_frame)) _root.Children.Add(_frame);
        ApplyContentHeight();
        UpdateExpandGlyph();
        FullScreenChanged?.Invoke(false);
    }

    private void UpdateExpandGlyph()
    {
        if (_expand.Content is FontIcon icon) icon.Glyph = _fullScreen ? "\uE73F" : "\uE740";
        var text = _fullScreen ? L("CanvasExitFullScreen", "退出全屏") : L("CanvasFullScreen", "全屏");
        ToolTipService.SetToolTip(_expand, text);
        AutomationProperties.SetName(_expand, text);
    }

    // ---- sizing ----------------------------------------------------------------------------

    /// <summary>Inline height = content height, clamped so a single bubble never eats the viewport.</summary>
    private void ApplyContentHeight()
    {
        if (_fullScreen) return;
        var max = Math.Max(MinInlineHeight + 60, _inlineMax);
        _stage.Height = Math.Clamp(_contentHeight, MinInlineHeight, max);
    }

    /// <summary>Called by the page when the surrounding window resizes.</summary>
    public void SetViewportHeight(double viewportHeight)
    {
        _inlineMax = Math.Max(MinInlineHeight + 60, viewportHeight - ViewportMargin);
        ApplyContentHeight();
    }

    private void OnReportedHeight(double height)
    {
        if (_disposed || height <= 0) return;
        _contentHeight = height;
        ApplyContentHeight();
    }

    // ---- page ------------------------------------------------------------------------------

    public void Bind(string sessionId, string relativePath, string title)
    {
        var samePage = string.Equals(_sessionId, sessionId, StringComparison.Ordinal) &&
                       string.Equals(_relativePath, relativePath, StringComparison.OrdinalIgnoreCase);
        _sessionId = sessionId;
        _relativePath = relativePath;
        _directory = AgentCanvasStore.DirectoryFor(sessionId);
        _titleText = title;
        _title.Text = title;
        var page = AgentCanvasStore.ResolvePage(sessionId, relativePath);
        ToolTipService.SetToolTip(_title, page ?? relativePath);
        AutomationProperties.SetAutomationId(this, "AgentCanvasBubble:" + sessionId + ":" + relativePath);
        ReattachWatcher();
        if (page is null)
        {
            _status.Text = "";
            ShowEmpty(L("CanvasMissing", "这条画布消息的内容已被清理。"), "", false);
            return;
        }
        if (samePage && _view.CoreWebView2 is not null) return;
        _contentHeight = FallbackInlineHeight;
        ApplyContentHeight();
        _empty.Visibility = Visibility.Collapsed;
        _view.Visibility = Visibility.Visible;
        _ = ReloadAsync(true);
    }

    public void UpdateTitle(string title)
    {
        _titleText = title;
        _title.Text = title;
    }

    public bool Matches(string? sessionId, string? relativePath) =>
        string.Equals(_sessionId, sessionId, StringComparison.Ordinal) &&
        string.Equals(_relativePath, relativePath, StringComparison.OrdinalIgnoreCase);

    private void ShowEmpty(string title, string hint, bool showRetry)
    {
        _emptyTitle.Text = title;
        _emptyHint.Text = hint;
        _emptyHint.Visibility = hint.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        _retry.Visibility = showRetry ? Visibility.Visible : Visibility.Collapsed;
        _empty.Visibility = Visibility.Visible;
        _view.Visibility = Visibility.Collapsed;
        _loading.IsActive = false;
        _loading.Visibility = Visibility.Collapsed;
    }

    private async Task EnsureInitializedAsync()
    {
        if (_initialized || _disposed) return;
        _initialized = true;
        try
        {
            await _view.EnsureCoreWebView2Async(await WebView2EnvironmentService.GetAsync());
            if (_disposed) return;
            var core = _view.CoreWebView2 ?? throw new InvalidOperationException(L("CanvasFailed", "画布未能启动，请检查 WebView2 运行时。"));
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = true;
            core.Settings.IsStatusBarEnabled = false;
            core.WebMessageReceived += OnWebMessageReceived;
            core.NavigationStarting += OnNavigationStarting;
            core.NavigationCompleted += OnNavigationCompleted;
            core.NewWindowRequested += OnNewWindowRequested;
            core.ProcessFailed += OnProcessFailed;
            await core.AddScriptToExecuteOnDocumentCreatedAsync(CanvasShim.Script);
        }
        catch (Exception ex)
        {
            _initialized = false;
            _status.Text = "";
            ShowEmpty(L("CanvasFailed", "画布未能启动，请检查 WebView2 运行时。"), "", true);
            ToolTipService.SetToolTip(_retry, ex.Message);
        }
    }

    private void OnNavigationStarting(Microsoft.Web.WebView2.Core.CoreWebView2 sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationStartingEventArgs args)
    {
        if (_disposed) return;
        if (args.Uri.StartsWith(CanvasShim.Origin, StringComparison.OrdinalIgnoreCase) || args.Uri == "about:blank") return;
        if (CanvasBridge.IsAllowedUrl(args.Uri)) OpenExternalRequested?.Invoke(args.Uri);
        args.Cancel = true;
    }

    private void OnNavigationCompleted(Microsoft.Web.WebView2.Core.CoreWebView2 sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationCompletedEventArgs args)
    {
        if (_disposed) return;
        _loading.IsActive = false;
        _loading.Visibility = Visibility.Collapsed;
        if (!args.IsSuccess)
        {
            _status.Text = "";
            // A removed snapshot is a normal case; anything else is a runtime problem worth a retry.
            var missing = AgentCanvasStore.ResolvePage(_sessionId ?? "", _relativePath ?? "") is null;
            ShowEmpty(missing ? L("CanvasMissing", "这条画布消息的内容已被清理。") : L("CanvasFailed", "画布未能启动，请检查 WebView2 运行时。"), "", !missing);
            return;
        }
        _retry.Visibility = Visibility.Collapsed;
    }

    private void OnNewWindowRequested(Microsoft.Web.WebView2.Core.CoreWebView2 sender, Microsoft.Web.WebView2.Core.CoreWebView2NewWindowRequestedEventArgs args)
    {
        args.Handled = true;
        if (CanvasBridge.IsAllowedUrl(args.Uri)) OpenExternalRequested?.Invoke(args.Uri);
    }

    private void OnProcessFailed(Microsoft.Web.WebView2.Core.CoreWebView2 sender, Microsoft.Web.WebView2.Core.CoreWebView2ProcessFailedEventArgs args)
    {
        if (_disposed) return;
        // Never auto-retry: a crashing page would loop forever.
        _status.Text = "";
        ShowEmpty(L("CanvasCrashed", "画布已停止，请点刷新重试。"), "", true);
        _initialized = false;
    }

    private void OnWebMessageReceived(Microsoft.Web.WebView2.Core.CoreWebView2 sender, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs args)
    {
        if (_disposed || !CanvasBridge.IsAllowedSource(args.Source)) return;
        var request = CanvasBridge.Parse(args.WebMessageAsJson);
        switch (request.Kind)
        {
            case CanvasRequestKind.Prompt: PromptRequested?.Invoke(request.Text); break;
            case CanvasRequestKind.Insert: InsertRequested?.Invoke(request.Text); break;
            case CanvasRequestKind.Title: UpdateTitle(request.Text); break;
            case CanvasRequestKind.Open: OpenExternalRequested?.Invoke(request.Text); break;
            case CanvasRequestKind.Height: OnReportedHeight(request.Height); break;
            case CanvasRequestKind.Ready:
                _loading.IsActive = false;
                _loading.Visibility = Visibility.Collapsed;
                PushImages();
                break;
        }
    }

    private async Task ReloadAsync(bool force)
    {
        if (_disposed || _sessionId is null || _directory is null) return;
        _pendingReload = false;
        await EnsureInitializedAsync();
        if (_disposed || _view.CoreWebView2 is null) return;
        var page = AgentCanvasStore.ResolvePage(_sessionId, _relativePath ?? "");
        if (page is null)
        {
            ShowEmpty(L("CanvasMissing", "这条画布消息的内容已被清理。"), "", false);
            return;
        }
        _empty.Visibility = Visibility.Collapsed;
        _view.Visibility = Visibility.Visible;
        try
        {
            _view.CoreWebView2.SetVirtualHostNameToFolderMapping(CanvasShim.HostName,
                _directory, Microsoft.Web.WebView2.Core.CoreWebView2HostResourceAccessKind.DenyCors);
            _lastStamp = LastWrite();
            _loading.Visibility = Visibility.Visible;
            _loading.IsActive = true;
            var relative = (_relativePath ?? AgentCanvasStore.IndexName).Replace('\\', '/');
            _view.CoreWebView2.Navigate(CanvasShim.Origin + relative + "?v=" + DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException or ArgumentException)
        {
            _status.Text = "";
            ShowEmpty(L("CanvasFailed", "画布未能启动，请检查 WebView2 运行时。"), "", true);
            ToolTipService.SetToolTip(_retry, ex.Message);
        }
    }

    /// <summary>Reloads only when the file really changed and is a complete document again.</summary>
    public void RefreshIfChanged()
    {
        if (_disposed || _sessionId is null || _relativePath is null) return;
        // Archived snapshots never change; only the live page does.
        if (!string.Equals(_relativePath, AgentCanvasStore.IndexName, StringComparison.OrdinalIgnoreCase)) return;
        var stamp = LastWrite();
        if (stamp is null || stamp == _lastStamp) return;
        try
        {
            if (!AgentCanvasStore.LooksComplete(File.ReadAllText(AgentCanvasStore.IndexPath(_sessionId)))) return;
        }
        catch (IOException) { return; }
        catch (UnauthorizedAccessException) { return; }
        _lastStamp = stamp;
        _pendingReload = true;
        _reload.Stop();
        _reload.Start();
    }

    private DateTime? LastWrite()
    {
        try
        {
            if (_sessionId is null) return null;
            var path = AgentCanvasStore.ResolvePage(_sessionId, _relativePath ?? "");
            if (path is null) return null;
            var info = new FileInfo(path);
            return info.Exists ? info.LastWriteTimeUtc : null;
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private void ReattachWatcher()
    {
        DetachWatcher();
        if (_disposed || _directory is null) return;
        if (!string.Equals(_relativePath, AgentCanvasStore.IndexName, StringComparison.OrdinalIgnoreCase)) return;
        try
        {
            if (!Directory.Exists(_directory)) return;
            _lastStamp = LastWrite();
            _watcher = new FileSystemWatcher(_directory)
            {
                Filter = AgentCanvasStore.IndexName,
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size | NotifyFilters.FileName | NotifyFilters.CreationTime,
                EnableRaisingEvents = true
            };
            _watcher.Changed += OnFileTouched;
            _watcher.Created += OnFileTouched;
            _watcher.Renamed += OnFileTouched;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _watcher = null;
        }
    }

    private void DetachWatcher()
    {
        if (_watcher is null) return;
        try
        {
            _watcher.EnableRaisingEvents = false;
            _watcher.Changed -= OnFileTouched;
            _watcher.Created -= OnFileTouched;
            _watcher.Renamed -= OnFileTouched;
            _watcher.Dispose();
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException) { }
        _watcher = null;
    }

    private void OnFileTouched(object sender, FileSystemEventArgs args)
    {
        if (_disposed) return;
        _pendingReload = true;
        DispatcherQueue.TryEnqueue(() => { _reload.Stop(); _reload.Start(); });
    }

    public void PushState(bool running, string phase, string detail) => Post(CanvasBridge.BuildState(running, phase, detail));
    public void PushMessage(string role, string text) => Post(CanvasBridge.BuildMessage(role, text));
    public void Notice(string text, bool error) => Post(CanvasBridge.BuildNotice(text, error));
    public void PushImages() => Post(CanvasBridge.BuildImages(_sessionId is null ? [] : AgentCanvasStore.ListImages(_sessionId)));

    private void Post(string json)
    {
        if (_disposed || _view.CoreWebView2 is null) return;
        try { _view.CoreWebView2.PostWebMessageAsJson(json); }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException) { }
    }

    private void OpenFolder()
    {
        if (_sessionId is null) return;
        try
        {
            var directory = AgentCanvasStore.EnsureDirectory(_sessionId);
            _ = Windows.System.Launcher.LaunchFolderPathAsync(directory);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private void OpenInBrowser()
    {
        if (_sessionId is null || _relativePath is null) return;
        if (AgentCanvasStore.ResolvePage(_sessionId, _relativePath) is not { } path) return;
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = new Uri(path).AbsoluteUri,
                UseShellExecute = true
            });
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ExitFullScreen();
        DetachWatcher();
        _reload.Stop();
        try
        {
            if (_view.CoreWebView2 is { } core)
            {
                core.WebMessageReceived -= OnWebMessageReceived;
                core.NavigationStarting -= OnNavigationStarting;
                core.NavigationCompleted -= OnNavigationCompleted;
                core.NewWindowRequested -= OnNewWindowRequested;
                core.ProcessFailed -= OnProcessFailed;
            }
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException) { }
        try { _view.Close(); }
        catch (Exception ex) when (ex is InvalidOperationException or System.Runtime.InteropServices.COMException) { }
        Content = null;
    }
}

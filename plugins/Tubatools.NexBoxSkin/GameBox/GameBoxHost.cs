using System.Text.Json;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.Web.WebView2.Core;
using TubaWinUi3.PluginSdk;
using TubaWinUi3.Services;
using Windows.System;
using DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue;

namespace Tubatools.NexBoxSkin.GameBox;

internal sealed class GameBoxHost : UserControl
{
    private const string VirtualHost = "https://gamebox.local/";

    private readonly bool _skinMode;
    private readonly string _route;
    private readonly IPluginServices? _services;
    private readonly Func<XamlRoot?> _root;
    private readonly CancellationToken _lifetime;
    private readonly Grid _surface;
    private readonly WebView2 _webView;
    private readonly StackPanel _loading;
    private GameBoxCommands? _commands;
    private bool _initialized;
    private bool _initializing;
    private bool _disposed;

    private GameBoxHost(
        IPluginServices? services,
        Func<XamlRoot?>? root,
        CancellationToken lifetime,
        bool skinMode,
        string route)
    {
        _services = services;
        _root = root ?? (() => XamlRoot);
        _lifetime = lifetime;
        _skinMode = skinMode;
        _route = route;

        _surface = new Grid { Background = new SolidColorBrush(Colors.Black) };
        if (!skinMode)
        {
            _surface.Margin = new Thickness(-24, 0, -24, -24);
        }

        _webView = new WebView2
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            VerticalAlignment = VerticalAlignment.Stretch,
            DefaultBackgroundColor = Colors.Black,
        };
        _surface.Children.Add(_webView);

        _loading = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 12,
        };
        _loading.Children.Add(new ProgressRing { IsActive = true, Width = 40, Height = 40 });
        _loading.Children.Add(new TextBlock { Text = "正在加载游戏盒…", Opacity = 0.8 });
        _surface.Children.Add(_loading);

        Content = _surface;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    public static GameBoxHost Create(PluginExecutionContext context, string route) =>
        new(context.Services, null, context.CancellationToken, skinMode: false, route);

    public static GameBoxHost CreateSkin(Func<XamlRoot?> root, string route = "/") =>
        new(null, root, CancellationToken.None, skinMode: true, route);

    public async Task InitializeAsync()
    {
        if (_disposed || _initializing) return;
        _initializing = true;

        if (!GameBoxAssets.IsReady)
        {
            ShowError($"游戏盒前端资源缺失：{GameBoxAssets.WebRoot}");
            _initializing = false;
            return;
        }

        try
        {
            var environment = await WebView2EnvironmentService.GetAsync();
            await _webView.EnsureCoreWebView2Async(environment);
            if (_disposed || _webView.CoreWebView2 is not { } core)
            {
                return;
            }

            core.Settings.AreDefaultContextMenusEnabled = false;
            core.Settings.IsStatusBarEnabled = false;
            core.Settings.AreDevToolsEnabled = true;
            core.Settings.AreBrowserAcceleratorKeysEnabled = false;
            core.SetVirtualHostNameToFolderMapping(
                "gamebox.local",
                GameBoxAssets.WebRoot,
                CoreWebView2HostResourceAccessKind.DenyCors);

            core.WebMessageReceived += OnWebMessageReceived;
            core.NavigationStarting += OnNavigationStarting;
            core.NewWindowRequested += OnNewWindowRequested;
            core.NavigationCompleted += OnNavigationCompleted;

            _commands = new GameBoxCommands(_services, _root, _skinMode, DispatcherQueue, _lifetime);
            var mode = _skinMode ? "skin" : "embedded";
            core.Navigate($"{VirtualHost}index.html?mode={mode}#{_route}");
        }
        catch (Exception ex)
        {
            ShowError($"WebView2 初始化失败：{ex.Message}");
        }
        finally
        {
            _initializing = false;
        }
    }

    public void CloseWebView()
    {
        if (_disposed) return;
        _disposed = true;
        Loaded -= OnLoaded;
        Unloaded -= OnUnloaded;

        try
        {
            if (_webView.CoreWebView2 is { } core)
            {
                core.WebMessageReceived -= OnWebMessageReceived;
                core.NavigationStarting -= OnNavigationStarting;
                core.NewWindowRequested -= OnNewWindowRequested;
                core.NavigationCompleted -= OnNavigationCompleted;
            }
        }
        catch
        {
            // WebView2 may already be shutting down.
        }

        try
        {
            _webView.Close();
        }
        catch
        {
            // Ignore close failures during host shutdown.
        }
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_initialized || _disposed) return;
        _initialized = true;
        _ = InitializeAsync();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (_skinMode) return;
        CloseWebView();
    }

    private void OnNavigationStarting(CoreWebView2 sender, CoreWebView2NavigationStartingEventArgs args)
    {
        if (args.Uri.StartsWith(VirtualHost, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        args.Cancel = true;
        _ = OpenExternalAsync(args.Uri);
    }

    private void OnNewWindowRequested(CoreWebView2 sender, CoreWebView2NewWindowRequestedEventArgs args)
    {
        args.Handled = true;
        _ = OpenExternalAsync(args.Uri);
    }

    private void OnNavigationCompleted(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
    {
        if (_disposed) return;
        _loading.Visibility = Visibility.Collapsed;
        if (!args.IsSuccess)
        {
            ShowError($"游戏盒页面加载失败（{args.WebErrorStatus}）。");
        }
    }

    private async void OnWebMessageReceived(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        if (_disposed || _commands is null) return;
        if (args.Source is not { } source || !source.StartsWith(VirtualHost, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        try
        {
            await HandleMessageAsync(args.WebMessageAsJson);
        }
        catch
        {
            // Never let a malformed page message tear down the host.
        }
    }

    private async Task HandleMessageAsync(string? json)
    {
        if (_commands is null || !GameBoxBridge.TryParse(json, out var id, out var command, out var args))
        {
            return;
        }

        try
        {
            var data = await _commands.ExecuteAsync(command, args, _lifetime);
            Post(GameBoxBridge.Result(id, data));
        }
        catch (OperationCanceledException)
        {
            Post(GameBoxBridge.Error(id, "操作已取消。"));
        }
        catch (Exception ex)
        {
            Post(GameBoxBridge.Error(id, ex.Message));
        }
    }

    private void Post(string payload)
    {
        void Send()
        {
            if (_disposed) return;
            try
            {
                _webView.CoreWebView2?.PostWebMessageAsJson(payload);
            }
            catch
            {
                // The page may have navigated away.
            }
        }

        if (DispatcherQueue.HasThreadAccess)
        {
            Send();
        }
        else
        {
            DispatcherQueue.TryEnqueue(Send);
        }
    }

    private static async Task OpenExternalAsync(string url)
    {
        if (!GameBoxBridge.IsAllowedHttp(url))
        {
            return;
        }

        try
        {
            await Launcher.LaunchUriAsync(new Uri(url));
        }
        catch
        {
            // Ignore shell launch failures.
        }
    }

    private void ShowError(string message)
    {
        if (_disposed) return;
        _loading.Visibility = Visibility.Collapsed;
        _webView.Visibility = Visibility.Collapsed;

        var panel = new StackPanel { Spacing = 8, Margin = new Thickness(24), MaxWidth = 720 };
        panel.Children.Add(new InfoBar
        {
            IsOpen = true,
            IsClosable = false,
            Severity = InfoBarSeverity.Error,
            Title = "游戏盒无法启动",
            Message = message,
        });
        panel.Children.Add(new TextBlock
        {
            Text = "请确认 WebView2 运行时已安装，并重新打开此工具。",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.8,
        });
        _surface.Children.Add(panel);
    }
}

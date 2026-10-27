using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Web.WebView2.Core;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace TubaWinUi3.Services;

// The hardware page only holds this native-type interface. The WebView2 implementation
// is created explicitly after selecting the diagram; classic mode never constructs it.
internal interface IHardwareScene : IDisposable
{
    FrameworkElement View { get; }
    event Action<string>? MessageReceived;
    Task InitializeAsync(Windows.UI.Color background);
    void Post(string json);
    void SetBackground(Windows.UI.Color color);
}

internal static class HardwareSceneRuntime
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static IHardwareScene Create() => new Core();

    private sealed class Core : IHardwareScene
    {
        private readonly WebView2 _view = new();
        private bool _disposed;
        public FrameworkElement View => _view;
        public event Action<string>? MessageReceived;

        public async Task InitializeAsync(Windows.UI.Color background)
        {
            // 透明背景必须在 EnsureCoreWebView2Async 之前设置——控制器创建时取该值生效，
            // 之后再设无效（bot 吉祥物「透明三件套」的实测结论：Ensure 前设 + HTML 根元素透明）。
            _view.DefaultBackgroundColor = background;
            var environment = await WebView2EnvironmentService.GetAsync();
            if (_disposed) return;
            await _view.EnsureCoreWebView2Async(environment);
            if (_disposed) return;
            var core = _view.CoreWebView2;
            core.SetVirtualHostNameToFolderMapping("hardware-scene.local",
                Path.Combine(AppContext.BaseDirectory, "Assets", "HardwareScene"), CoreWebView2HostResourceAccessKind.DenyCors);
            core.Settings.AreDevToolsEnabled = false;
            core.Settings.AreDefaultContextMenusEnabled = false;
            core.WebMessageReceived += OnMessage;
            core.NavigationCompleted += OnNavigationCompleted;
            core.ProcessFailed += OnProcessFailed;
            _view.Source = new Uri("https://hardware-scene.local/index.html");
        }

        private void OnMessage(CoreWebView2 sender, CoreWebView2WebMessageReceivedEventArgs args)
        {
            if (!_disposed && args.Source.StartsWith("https://hardware-scene.local/", StringComparison.OrdinalIgnoreCase))
                MessageReceived?.Invoke(args.WebMessageAsJson);
        }

        private void OnNavigationCompleted(CoreWebView2 sender, CoreWebView2NavigationCompletedEventArgs args)
        {
            if (!_disposed && !args.IsSuccess) MessageReceived?.Invoke("{\"type\":\"error\",\"message\":\"3D 页面加载失败\"}");
        }
        private void OnProcessFailed(CoreWebView2 sender, CoreWebView2ProcessFailedEventArgs args)
        {
            if (!_disposed) MessageReceived?.Invoke("{\"type\":\"error\",\"message\":\"3D 渲染进程已停止\"}");
        }

        public void Post(string json)
        {
            if (_disposed) return;
            try { _view.CoreWebView2?.PostWebMessageAsJson(json); }
            catch (COMException ex) when (ex.HResult == unchecked((int)0x8007139F)) { }
        }

        public void SetBackground(Windows.UI.Color color)
        {
            if (!_disposed) _view.DefaultBackgroundColor = color;
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                if (_view.CoreWebView2 is { } core)
                {
                    core.WebMessageReceived -= OnMessage;
                    core.NavigationCompleted -= OnNavigationCompleted;
                    core.ProcessFailed -= OnProcessFailed;
                }
                _view.Close();
            }
            catch (COMException ex) when (ex.HResult == unchecked((int)0x8007139F)) { }
        }
    }
}

using System.Runtime.InteropServices;
using System.Text.Json;
using TubaWinUi3.PluginSdk;

namespace TubaWinUi3.Services.Plugins;

internal sealed partial class PluginHostServices
{
    public T? GetService<T>() where T : class
    {
        object? service = typeof(T) == typeof(IPluginServices) ? this : typeof(T) == typeof(IPluginBackgroundService) ? new BackgroundFacade(this) :
            typeof(T) == typeof(IPluginNavigationService) ? new NavigationFacade(this) : typeof(T) == typeof(IPluginHostInfoService) ? new InfoFacade() :
            typeof(T) == typeof(IPluginDiagnosticsService) ? new DiagnosticsFacade() :
            typeof(T) == typeof(IPluginHostAccess) && fullTrust ? new HostAccessFacade(this) : null;
        return service as T;
    }
    private sealed class BackgroundFacade(PluginHostServices host) : IPluginBackgroundService
    {
        public async Task<PluginBackground> GetAsync(CancellationToken ct = default)
        {
            PluginBackground? result = null;
            await host.OnUi(() => { result = new(BackgroundService.GetBackgroundPath(), BackgroundService.GetBackgroundOpacity()); return Task.CompletedTask; }, ct);
            return result!;
        }
        public async Task SetAsync(string? imagePath, double opacity = 0.15, CancellationToken ct = default)
        {
            ValidateOpacity(opacity); ct.ThrowIfCancellationRequested();
            var imported = imagePath is null ? null : await PluginCustomizationFiles.ImportBackgroundAsync(host.PackageDirectory, imagePath, ct);
            await host.OnUi(() => { BackgroundService.SetBackgroundOpacity(opacity); BackgroundService.SetBackgroundPath(imported); return Task.CompletedTask; }, ct);
        }
        public Task SetOpacityAsync(double opacity, CancellationToken ct = default)
        { ValidateOpacity(opacity); return host.OnUi(() => { BackgroundService.SetBackgroundOpacity(opacity); return Task.CompletedTask; }, ct); }
        private static void ValidateOpacity(double opacity) { if (!double.IsFinite(opacity) || opacity is < 0 or > 1) throw new ArgumentOutOfRangeException(nameof(opacity)); }
    }
    private sealed class NavigationFacade(PluginHostServices host) : IPluginNavigationService
    {
        private static MainWindow Window => App.MainWindow as MainWindow ?? throw new InvalidOperationException("主窗口不可用。");
        public async Task<IReadOnlyList<PluginNavigationItem>> GetItemsAsync(CancellationToken ct = default)
        {
            IReadOnlyList<PluginNavigationItem> result = [];
            await host.OnUi(() => { result = Window.GetPluginNavigationItems(); return Task.CompletedTask; }, ct); return result;
        }
        public async Task UpdateAsync(PluginNavigationChange change, CancellationToken ct = default)
        {
            PluginNavigationStore.Validate(change);
            if (!string.IsNullOrEmpty(change.IconPath)) change = change with { IconPath = await PluginCustomizationFiles.ImportNavigationIconAsync(host.PackageDirectory, change.IconPath, ct) };
            await host.OnUi(() =>
            {
                if (!Window.GetPluginNavigationItems().Any(i => i.Id == change.Id)) throw new KeyNotFoundException("导航项不存在，请先读取导航数据。");
                var changes = PluginNavigationStore.Read().ToList(); var old = changes.FindIndex(c => c.Id == change.Id);
                var merged = PluginNavigationStore.Merge(old >= 0 ? changes[old] : null, change);
                if (old >= 0) changes[old] = merged; else changes.Add(merged);
                PluginNavigationStore.Save(changes); Window.ApplyPluginNavigationCustomization(); return Task.CompletedTask;
            }, ct);
        }
        public Task SetOrderAsync(IReadOnlyList<string> ids, CancellationToken ct = default) => host.OnUi(() =>
        {
            ArgumentNullException.ThrowIfNull(ids);
            var current = Window.GetPluginNavigationItems().Where(i => !i.IsFooter).Select(i => i.Id).ToArray();
            if (ids.Distinct(StringComparer.Ordinal).Count() != ids.Count || ids.Any(id => !current.Contains(id, StringComparer.Ordinal))) throw new ArgumentException("导航顺序包含重复或不存在的菜单 ID。");
            var changes = PluginNavigationStore.Read().ToDictionary(c => c.Id, StringComparer.Ordinal);
            var order = ids.Concat(current.Except(ids, StringComparer.Ordinal)).ToArray();
            for (var i = 0; i < order.Length; i++) changes[order[i]] = PluginNavigationStore.Merge(changes.GetValueOrDefault(order[i]), new(order[i], Order: i));
            PluginNavigationStore.Save(changes.Values.ToArray()); Window.ApplyPluginNavigationCustomization(); return Task.CompletedTask;
        }, ct);
        public Task ResetAsync(string? id = null, CancellationToken ct = default) => host.OnUi(() =>
        {
            PluginNavigationStore.Save(id is null ? [] : PluginNavigationStore.Read().Where(c => c.Id != id).ToArray()); Window.ApplyPluginNavigationCustomization(); return Task.CompletedTask;
        }, ct);
    }
    private sealed class InfoFacade : IPluginHostInfoService
    {
        public PluginHostInfo GetInfo() => new(typeof(App).Assembly.GetName().Version?.ToString() ?? "", 2,
            LocalizationService.CurrentLanguage, RuntimeInformation.OSArchitecture.ToString(), RuntimeHelper.IsPackagedContext);
    }
    private sealed class DiagnosticsFacade : IPluginDiagnosticsService
    {
        public async Task<string> ReadJsonAsync(PluginDiagnosticKind kind, CancellationToken ct = default)
        {
            var command = kind switch { PluginDiagnosticKind.Hardware => "hardware", PluginDiagnosticKind.Sensors => "sensors", PluginDiagnosticKind.Disks => "disks", PluginDiagnosticKind.CpuZ => "cpuz", _ => throw new ArgumentOutOfRangeException(nameof(kind)) };
            ct.ThrowIfCancellationRequested();
            var data = await AgentCli.AgentCliService.QueryLocalAsync(command, disposeSensors: false).WaitAsync(ct);
            return JsonSerializer.Serialize(new { schemaVersion = 1, command, capturedAt = DateTimeOffset.UtcNow, data }, AgentCli.AgentCliService.Json);
        }
    }

    /// <summary>fullTrust 插件的宿主完全访问。调用方负责在 UI 线程修改界面（RunOnUiThreadAsync）。</summary>
    private sealed class HostAccessFacade(PluginHostServices host) : IPluginHostAccess
    {
        private Action? _themeChanged;
        private Action? _languageChanged;
        public object? MainWindow => App.MainWindow;
        public object? RootVisual => App.MainWindow?.PluginRoot;
        public object? NavigationView => App.MainWindow?.PluginNavigationView;
        public object? NavigationFrame => App.MainWindow?.PluginNavigationFrame;
        public object? CurrentPage => App.MainWindow?.PluginNavigationFrame.Content;
        public Task RunOnUiThreadAsync(Func<Task> action, CancellationToken ct = default) => host.OnUi(action, ct);
        public Task NavigateAsync(Type pageType, object? parameter = null, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(pageType);
            return host.OnUi(() =>
            {
                var frame = App.MainWindow?.PluginNavigationFrame ?? throw new InvalidOperationException("主窗口不可用。");
                if (!frame.Navigate(pageType, parameter)) throw new InvalidOperationException("页面导航失败：" + pageType.FullName);
                return Task.CompletedTask;
            }, ct);
        }
        public Task NavigateBackAsync(CancellationToken ct = default) => host.OnUi(() =>
        {
            var frame = App.MainWindow?.PluginNavigationFrame;
            if (frame?.CanGoBack == true) frame.GoBack();
            return Task.CompletedTask;
        }, ct);
        public Task<string?> GetSettingAsync(string key, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(AppSettings.Get(key));
        }
        public Task SetSettingAsync(string key, string? value, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            if (value is null) AppSettings.Remove(key); else AppSettings.Set(key, value);
            return Task.CompletedTask;
        }
        public event Action? ThemeChanged
        {
            add { if (_themeChanged is null) ThemeService.ThemeChanged += ForwardTheme; _themeChanged += value; }
            remove { _themeChanged -= value; if (_themeChanged is null) ThemeService.ThemeChanged -= ForwardTheme; }
        }
        public event Action? LanguageChanged
        {
            add { if (_languageChanged is null) LocalizationService.LanguageChanged += ForwardLanguage; _languageChanged += value; }
            remove { _languageChanged -= value; if (_languageChanged is null) LocalizationService.LanguageChanged -= ForwardLanguage; }
        }
        private void ForwardTheme(Microsoft.UI.Xaml.ElementTheme _) => _themeChanged?.Invoke();
        private void ForwardLanguage() => _languageChanged?.Invoke();
    }
}

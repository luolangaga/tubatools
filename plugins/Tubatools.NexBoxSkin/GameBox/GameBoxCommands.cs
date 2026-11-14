using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using TubaWinUi3.Models;
using TubaWinUi3.Pages;
using TubaWinUi3.PluginSdk;
using TubaWinUi3.Plugins;
using TubaWinUi3.Services;
using Windows.ApplicationModel.DataTransfer;
using Windows.System;
using DispatcherQueue = Microsoft.UI.Dispatching.DispatcherQueue;

namespace Tubatools.NexBoxSkin.GameBox;

internal sealed class GameBoxCommands
{
    private static readonly HttpClient Http = CreateHttpClient();

    private readonly IPluginServices? _services;
    private readonly Func<XamlRoot?> _root;
    private readonly bool _skinMode;
    private readonly DispatcherQueue _dispatcher;
    private readonly CancellationToken _lifetime;

    public GameBoxCommands(
        IPluginServices? services,
        Func<XamlRoot?> root,
        bool skinMode,
        DispatcherQueue dispatcher,
        CancellationToken lifetime)
    {
        _services = services;
        _root = root;
        _skinMode = skinMode;
        _dispatcher = dispatcher;
        _lifetime = lifetime;

        if (!string.IsNullOrEmpty(services?.DataDirectory))
        {
            DisplayFilterService.Configure(services.DataDirectory);
        }
    }

    public async Task<object?> ExecuteAsync(string command, JsonElement args, CancellationToken cancellationToken)
    {
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _lifetime);
        var token = linked.Token;

        switch (command)
        {
            case "host.info":
                return BuildHostInfo();
            case "host.username":
                return Environment.UserName;
            case "host.locale":
                return LocalizationService.CurrentLanguage.StartsWith("zh", StringComparison.OrdinalIgnoreCase) ? "zh" : "en";
            case "tools.list":
                return await ListToolsAsync(token);
            case "tools.run":
                await RunToolAsync(RequiredString(args, "id"), token);
                return null;
            case "tools.icon":
                return await GetToolIconAsync(OptionalString(args, "id"), token);
            case "host.navigate":
                await NavigateAsync(RequiredString(args, "destination"), token);
                return null;
            case "host.openUri":
                await OpenUriAsync(RequiredString(args, "url"), token);
                return null;
            case "host.copyText":
                await CopyTextAsync(RequiredString(args, "text"), token);
                return null;
            case "host.pickFolder":
                return await PickFolderAsync(OptionalString(args, "title") ?? "选择文件夹", token);
            case "host.pickFile":
                return await PickFileAsync(OptionalString(args, "title") ?? "选择文件", token);
            case "diag.hardware":
                return await HardwareInfoService.LoadForDisplayAsync();
            case "diag.sensors":
                return await ReadSensorsAsync(token);
            case "diag.disks":
                return await DiskHealthService.GetHealthAsync().WaitAsync(token);
            case "filter.displays":
                return DisplayFilterService.GetDisplays();
            case "filter.get":
                return DisplayFilterService.GetSettings(OptionalInt(args, "displayIndex") ?? 0);
            case "filter.set":
                return DisplayFilterService.SetSettings(OptionalInt(args, "displayIndex") ?? 0, args);
            case "filter.presets":
                return DisplayFilterService.GetPresets();
            case "filter.applyPreset":
                return DisplayFilterService.ApplyPreset(
                    OptionalInt(args, "displayIndex") ?? 0,
                    RequiredString(args, "id"));
            case "filter.reset":
                return DisplayFilterService.Reset(OptionalInt(args, "displayIndex") ?? 0);
            case "filter.openColorManagement":
                return DisplayFilterService.OpenColorManagement();
            case "skin.get":
                return new { enabled = NexBoxSkinRuntime.IsEnabled };
            case "skin.setEnabled":
                await NexBoxSkinRuntime.SetEnabledAsync(OptionalBool(args, "enabled"), token);
                return null;
            case "http.getJson":
                return await GetJsonAsync(RequiredString(args, "url"), token);
            case "delta.passwords":
                return Array.Empty<object>();
            case "cache_delta_image":
                return "";
            case "host.noop":
                return null;
            default:
                throw new NotSupportedException($"不支持的指令：{command}");
        }
    }

    private HostInfoDto BuildHostInfo()
    {
        var version = PluginHostBridge.HostAssembly.GetName().Version?.ToString() ?? "";
        var theme = ThemeService.CurrentElementTheme switch
        {
            ElementTheme.Light => "light",
            ElementTheme.Dark => "dark",
            _ => "system",
        };
        return new HostInfoDto(
            version,
            LocalizationService.CurrentLanguage,
            RuntimeInformation.ProcessArchitecture.ToString(),
            RuntimeHelper.IsPackagedContext,
            theme,
            _skinMode ? "skin" : "embedded");
    }

    private static async Task<string?> GetToolIconAsync(string? id, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        if (id.StartsWith("plugin:tubatools.nexboxskin:", StringComparison.OrdinalIgnoreCase))
        {
            return GameBoxAssets.ReadAssetDataUri("assets/icon.svg");
        }

        var builtin = GameBoxAssets.ReadBuiltinIconDataUri(id);
        if (builtin is not null)
        {
            return builtin;
        }

        if (!id.StartsWith("catalog:", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        var tools = await ToolCatalog.GetAllToolsAsync().WaitAsync(token);
        var tool = tools.FirstOrDefault(item => !item.IsBuiltinLink && CatalogId(item.Path) == id);
        if (tool is null)
        {
            return null;
        }

        // Bundled icon cache: the publish output ships icons for catalogued external tools
        // even when the tool itself is not installed on this machine. GetCachedIconPath
        // intentionally requires the source file, so look up the cache key directly first.
        var relative = PathResolver.MakeRelative(tool.EffectivePath);
        var cacheKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(relative)))[..16].ToLowerInvariant();
        var bundledIcon = Path.Combine(AppContext.BaseDirectory, "IconCache", cacheKey + ".png");
        if (File.Exists(bundledIcon))
        {
            var bundledBytes = await File.ReadAllBytesAsync(bundledIcon, token);
            if (bundledBytes.Length > 0)
            {
                return "data:image/png;base64," + Convert.ToBase64String(bundledBytes);
            }
        }

        if (!File.Exists(tool.EffectivePath))
        {
            return null;
        }

        var iconPath = ToolIconService.GetCachedIconPath(tool.EffectivePath);
        if (string.IsNullOrWhiteSpace(iconPath) || !File.Exists(iconPath))
        {
            iconPath = await ToolIconService.ExtractIconToCacheAsync(tool.EffectivePath, token);
        }

        if (string.IsNullOrWhiteSpace(iconPath) || !File.Exists(iconPath))
        {
            return null;
        }

        var bytes = await File.ReadAllBytesAsync(iconPath, token);
        return bytes.Length == 0 ? null : "data:image/png;base64," + Convert.ToBase64String(bytes);
    }

    private static async Task<IReadOnlyList<ToolDto>> ListToolsAsync(CancellationToken token)
    {
        var builtins = BuiltinToolRegistry.Tools
            .Select(tool => new ToolDto(tool.Id, tool.Name, tool.Category, tool.Description, true));
        var external = (await ToolCatalog.GetAllToolsAsync().WaitAsync(token))
            .Where(tool => !tool.IsBuiltinLink)
            .Select(tool => new ToolDto(CatalogId(tool.Path), tool.Name, tool.Category, tool.Description ?? "", false));
        return builtins
            .Concat(external)
            .OrderBy(tool => tool.Category, StringComparer.CurrentCulture)
            .ThenBy(tool => tool.Name, StringComparer.CurrentCulture)
            .ToArray();
    }

    private Task RunToolAsync(string id, CancellationToken token) => RunOnUiAsync(async () =>
    {
        if (BuiltinToolRegistry.GetById(id) is { } builtin)
        {
            using var scope = _skinMode ? BuiltinToolWindow.ForceWindowScope() : null;
            var root = _root() ?? throw new InvalidOperationException("主窗口不可用。");
            await builtin.ExecuteAsync(new BuiltinToolContext
            {
                XamlRoot = root,
                CancellationToken = token,
                OnProgress = _ => { },
            });
            return;
        }

        var tools = await ToolCatalog.GetAllToolsAsync();
        var tool = tools.FirstOrDefault(item => !item.IsBuiltinLink && CatalogId(item.Path) == id)
            ?? throw new KeyNotFoundException("工具不存在或已不可用，请重新打开列表。");

        if (!string.IsNullOrWhiteSpace(tool.RemoteUrl))
        {
            await OpenUriAsync(tool.RemoteUrl, token);
            return;
        }

        if (tool.NeedsDownload || !File.Exists(tool.EffectivePath))
        {
            if (!string.IsNullOrWhiteSpace(tool.DownloadUrl))
            {
                await OpenUriAsync(tool.DownloadUrl, token);
                return;
            }

            throw new InvalidOperationException("工具文件不存在，请先在宿主中下载。");
        }

        LaunchExternal(tool);
    }, token);

    private static void LaunchExternal(ToolItem tool)
    {
        var path = tool.EffectivePath;
        var extension = Path.GetExtension(path).ToLowerInvariant();
        var workingDirectory = tool.EffectiveWorkingDir;

        if (extension is ".bat" or ".cmd")
        {
            Process.Start(new ProcessStartInfo("cmd.exe", $"/c \"{path}\"")
            {
                WorkingDirectory = workingDirectory,
                UseShellExecute = true,
            });
            return;
        }

        if (extension == ".ps1")
        {
            Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -File \"{path}\"")
            {
                WorkingDirectory = workingDirectory,
                UseShellExecute = true,
            });
            return;
        }

        if (extension == ".vbs")
        {
            Process.Start(new ProcessStartInfo("wscript.exe", $"\"{path}\"")
            {
                WorkingDirectory = workingDirectory,
                UseShellExecute = true,
            });
            return;
        }

        ToolProcessLauncher.Launch(path, workingDirectory);
    }

    private async Task NavigateAsync(string destination, CancellationToken token)
    {
        if (_skinMode)
        {
            // Navigating to a host page while the skin is active would target the hidden
            // NavigationView; leave the skin first so the destination is actually visible.
            await NexBoxSkinRuntime.SetEnabledAsync(false, token);
        }

        await RunOnUiAsync(() =>
        {
            var pageType = destination switch
            {
                "home" => typeof(DashboardPage),
                "hardware" => typeof(HardwarePage),
                "builtin-tools" => typeof(BuiltinToolsPage),
                "settings" => typeof(SettingsPage),
                "plugins" => typeof(PluginsPage),
                _ => throw new ArgumentException($"不支持的页面：{destination}"),
            };

            if (!PluginHostBridge.Navigate(pageType))
            {
                throw new InvalidOperationException("主窗口当前不可导航。");
            }

            return Task.CompletedTask;
        }, token);
    }

    private async Task OpenUriAsync(string url, CancellationToken token)
    {
        if (!GameBoxBridge.IsAllowedHttp(url))
        {
            throw new InvalidOperationException("只允许打开公网 http/https 链接。");
        }

        await Launcher.LaunchUriAsync(new Uri(url));
    }

    private Task CopyTextAsync(string text, CancellationToken token) => RunOnUiAsync(async () =>
    {
        if (_services is not null)
        {
            await _services.CopyTextAsync(text, token);
            return;
        }

        var package = new DataPackage();
        package.SetText(text);
        Clipboard.SetContent(package);
    }, token);

    private Task<string?> PickFolderAsync(string title, CancellationToken token) => RunOnUiAsync(
        () => _services is not null ? _services.PickFolderAsync(title, token) : Win32Dialogs.PickFolderAsync(title),
        token);

    private Task<string?> PickFileAsync(string title, CancellationToken token) => RunOnUiAsync(
        () => _services is not null
            ? _services.PickOpenFileAsync(title, token)
            : Task.FromResult(Win32Dialogs.PickOpen("所有文件|*.*", title)),
        token);

    private static Task<MonitorSample> ReadSensorsAsync(CancellationToken token) => Task.Run(() =>
    {
        LiteMonitorService.Instance.EnsureInit();
        return LiteMonitorService.Instance.Read();
    }, token);

    private static async Task<string> GetJsonAsync(string url, CancellationToken token)
    {
        if (!GameBoxBridge.IsAllowedProxy(url))
        {
            throw new InvalidOperationException("代理只允许 df.nexbox.top 与 raw.githubusercontent.com。");
        }

        using var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, token);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(token);
    }

    private Task RunOnUiAsync(Func<Task> action, CancellationToken token)
    {
        if (_dispatcher.HasThreadAccess)
        {
            return action().WaitAsync(token);
        }

        var completion = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_dispatcher.TryEnqueue(async () =>
            {
                try
                {
                    await action();
                    completion.TrySetResult(null);
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
            }))
        {
            completion.TrySetException(new InvalidOperationException("界面已关闭。"));
        }

        return completion.Task.WaitAsync(token);
    }

    private Task<T> RunOnUiAsync<T>(Func<Task<T>> action, CancellationToken token)
    {
        if (_dispatcher.HasThreadAccess)
        {
            return action().WaitAsync(token);
        }

        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_dispatcher.TryEnqueue(async () =>
            {
                try
                {
                    completion.TrySetResult(await action());
                }
                catch (Exception ex)
                {
                    completion.TrySetException(ex);
                }
            }))
        {
            completion.TrySetException(new InvalidOperationException("界面已关闭。"));
        }

        return completion.Task.WaitAsync(token);
    }

    private static string RequiredString(JsonElement args, string name)
    {
        var value = OptionalString(args, name);
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException($"缺少参数：{name}");
        }

        return value;
    }

    private static string? OptionalString(JsonElement args, string name) =>
        args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool OptionalBool(JsonElement args, string name) =>
        args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var value) &&
        value.ValueKind is JsonValueKind.True or JsonValueKind.False && value.GetBoolean();

    private static int? OptionalInt(JsonElement args, string name) =>
        args.ValueKind == JsonValueKind.Object && args.TryGetProperty(name, out var value) &&
        value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number)
            ? number
            : null;

    private static string CatalogId(string path) =>
        "catalog:" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(path.ToUpperInvariant())));

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("TubaWinUi3-NexBoxSkin/1.0");
        return client;
    }

    private sealed record ToolDto(string Id, string Name, string Category, string Description, bool IsBuiltin);

    private sealed record HostInfoDto(string Version, string Language, string Architecture, bool IsPackaged, string Theme, string Mode);
}

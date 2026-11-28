using System.Diagnostics;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using TubaWinUi3.PluginSdk;

namespace Tubatools.Onboarding;

/// <summary>
/// 旧宿主兼容的「打开」入口注册。SDK 2.2 才有 <c>RegisterOpen</c> / <c>PluginOpenHandler</c>；
/// 旧宿主的 SDK 程序集里没有这些类型，哪怕只是 JIT 到引用它们的 lambda 也会 TypeLoadException。
/// 因此这里先反射探测宿主是否真的实现了 RegisterOpen，真正的调用放在 NoInlining 的独立方法里：
/// 旧宿主只走探测并返回，插件照常加载、安装引导照常弹出，只是插件页不会出现绿色「打开」。
/// </summary>
internal static class PluginOpenCompat
{
    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void RegisterToolEntry(IPluginHost host, string? toolId)
    {
        if (!SupportsOpen(host)) return;
        RegisterToolEntryCore(host, toolId);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static void RegisterDestinationEntry(IPluginHost host, PluginDestination destination)
    {
        if (!SupportsOpen(host)) return;
        RegisterDestinationEntryCore(host, destination);
    }

    private static bool SupportsOpen(IPluginHost host)
    {
        try { return host.GetType().GetMethod("RegisterOpen", BindingFlags.Public | BindingFlags.Instance) is not null; }
        catch { return false; }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RegisterToolEntryCore(IPluginHost host, string? toolId) =>
        host.RegisterOpen(async context => await context.OpenToolAsync(toolId));

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void RegisterDestinationEntryCore(IPluginHost host, PluginDestination destination) =>
        host.RegisterOpen(async context => await context.Services.NavigateAsync(destination));
}

/// <summary>安装引导里的一条说明。Action 为 null 时只展示文字。</summary>
internal sealed record OnboardingEntry(
    string Caption,
    string Title,
    string Description,
    string Glyph,
    string? ActionLabel,
    Func<Task>? Action);

/// <summary>
/// 插件自己的安装后引导（纯插件侧实现，不改宿主）。
///
/// 触发点刻意放在 <c>Register</c> 而不是启动钩子：安装/更新插件时宿主一定会调用 Register，
/// 但已加载插件的启动钩子不会重跑（这正是「装完没反应、重启才弹」的原因）。Register 会把
/// 显示动作投递到 UI 线程，等主窗口 XamlRoot 就绪后弹出；同时监视包目录，后续更新版本落地时
/// 也能立即提示。每个版本只提示一次，标记写在插件数据目录。
/// </summary>
internal static class PluginOnboardingDialog
{
    private const string HostAssemblyName = "TubaWinUi3";
    private static readonly object Gate = new();
    private static readonly HashSet<string> Queued = new(StringComparer.OrdinalIgnoreCase);
    private static string? _packageRoot;
    private static string? _dataDirectory;
    private static FileSystemWatcher? _watcher;

    /// <summary>
    /// 在 Register 里调用：本次会话刚安装（或监听到新版本目录）时显示一次引导。
    /// 返回是否已经为当前版本排队；垃圾清理用它决定要不要抑制启动钩子里的教程。
    /// </summary>
    public static bool ScheduleInstallGuide(Func<XamlRoot, Task> show)
    {
        try
        {
            var root = FindPackageRoot();
            if (root is null) return false;
            var data = PluginDataDirectory(root);
            if (data is null) return false;
            _packageRoot = root;
            _dataDirectory = data;

            var current = Path.GetFileName(root);
            var queued = false;
            if (WasInstalledThisSession(root) && !MarkerExists(current))
                queued = Queue(current, show);
            StartWatcher(show);
            return queued;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>标准安装引导：插件名 + 说明 + 若干条目（每条可带「打开」按钮）。</summary>
    public static Func<XamlRoot, Task> Guide(string pluginName, string summary,
        IReadOnlyList<OnboardingEntry> entries, bool chinese) => async root =>
    {
        OnboardingEntry? chosen = null;
        var dialog = Build(root, pluginName, summary, entries, chinese, entry => chosen = entry);
        await dialog.ShowAsync();
        if (chosen?.Action is { } action) await action();
    };

    /// <summary>打开本插件注册的工具页。toolId 是 PluginToolDefinition.Id（包内 Id）。</summary>
    public static Func<Task> OpenTool(string toolId) => () =>
    {
        var packageId = PackageId;
        return packageId is null
            ? Task.CompletedTask
            : NavigateAsync("TubaWinUi3.Pages.PluginToolPage",
                $"plugin:{packageId.ToLowerInvariant()}:{toolId.ToLowerInvariant()}");
    };

    /// <summary>打开宿主设置页；无工具入口的插件用它把用户带到自己的设置卡片。</summary>
    public static Func<Task> OpenSettings() => NavigateSettingsAsync;

    /// <summary>打开 AI 助手页面。</summary>
    public static Func<Task> OpenAiAssistant() => () => NavigateAsync("TubaWinUi3.Pages.AiAssistantPage", null);

    private static string? PackageId =>
        _packageRoot is null ? null : Path.GetFileName(Path.GetDirectoryName(_packageRoot));

    private static bool Queue(string version, Func<XamlRoot, Task> show)
    {
        lock (Gate)
        {
            if (!Queued.Add(version)) return false;
        }
        var dispatcher = DispatcherQueue.GetForCurrentThread() ?? TryGetHostWindow()?.DispatcherQueue;
        if (dispatcher is null)
        {
            lock (Gate) Queued.Remove(version);
            return false;
        }
        dispatcher.TryEnqueue(async () => await RunAsync(version, show));
        return true;
    }

    private static async Task RunAsync(string version, Func<XamlRoot, Task> show)
    {
        try
        {
            // 安装刚完成时 XamlRoot 可能还差一两帧；更新时新版本目录也可能先出现、后解包。
            for (var attempt = 0; attempt < 30; attempt++)
            {
                if (MarkerExists(version)) return;
                if (PackageReady(version) && TryGetRoot() is { } root)
                {
                    for (var retry = 0; retry < 4; retry++)
                    {
                        try
                        {
                            await show(root);
                            WriteMarker(version);
                            return;
                        }
                        catch (InvalidOperationException) when (retry < 3)
                        {
                            // 宿主可能正开着别的 ContentDialog：稍等再试。
                            await Task.Delay(800);
                            root = TryGetRoot() ?? root;
                        }
                    }
                    return;
                }
                await Task.Delay(300);
            }
        }
        catch
        {
            // 引导只是装饰：任何失败都不能影响插件安装或运行。
        }
        finally
        {
            lock (Gate) Queued.Remove(version);
        }
    }

    private static void StartWatcher(Func<XamlRoot, Task> show)
    {
        if (_watcher is not null || _packageRoot is null) return;
        var parent = Path.GetDirectoryName(_packageRoot);
        if (parent is null || !Directory.Exists(parent)) return;
        try
        {
            _watcher = new FileSystemWatcher(parent)
            {
                NotifyFilter = NotifyFilters.DirectoryName,
                IncludeSubdirectories = false,
                EnableRaisingEvents = true
            };
            _watcher.Created += (_, e) =>
            {
                var name = Path.GetFileName(e.FullPath);
                if (string.IsNullOrEmpty(name) || name.StartsWith('.') || MarkerExists(name)) return;
                if (_packageRoot is not null &&
                    name.Equals(Path.GetFileName(_packageRoot), StringComparison.OrdinalIgnoreCase)) return;
                Queue(name, show);
            };
        }
        catch
        {
            // 文件监视不可用（权限/路径异常）时只保留本次安装的引导。
        }
    }

    private static bool PackageReady(string version)
    {
        var parent = _packageRoot is null ? null : Path.GetDirectoryName(_packageRoot);
        return parent is not null && File.Exists(Path.Combine(parent, version, "tubatools.json"));
    }

    private static string? MarkerPath(string version) =>
        _dataDirectory is null ? null : Path.Combine(_dataDirectory, ".onboarding-" + version);

    private static bool MarkerExists(string version) => MarkerPath(version) is { } path && File.Exists(path);

    private static void WriteMarker(string version)
    {
        try
        {
            if (MarkerPath(version) is not { } path) return;
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, DateTimeOffset.Now.ToString("O"));
        }
        catch
        {
            // 标记失败最多导致下次再提示一次。
        }
    }

    /// <summary>包根目录：从插件程序集位置向上找到带 tubatools.json 的目录。</summary>
    private static string? FindPackageRoot()
    {
        var directory = Path.GetDirectoryName(typeof(PluginOnboardingDialog).Assembly.Location);
        while (!string.IsNullOrEmpty(directory))
        {
            if (File.Exists(Path.Combine(directory, "tubatools.json"))) return directory;
            directory = Path.GetDirectoryName(directory);
        }
        return null;
    }

    /// <summary>&lt;DataDir&gt;/Plugins/&lt;id&gt;/&lt;version&gt; → &lt;DataDir&gt;/PluginData/&lt;id&gt;。</summary>
    private static string? PluginDataDirectory(string packageRoot)
    {
        var pluginDir = Path.GetDirectoryName(Path.GetFullPath(packageRoot));
        var pluginsDir = pluginDir is null ? null : Path.GetDirectoryName(pluginDir);
        var dataDir = pluginsDir is null ? null : Path.GetDirectoryName(pluginsDir);
        var id = pluginDir is null ? null : Path.GetFileName(pluginDir);
        return dataDir is null || string.IsNullOrEmpty(id) ? null : Path.Combine(dataDir, "PluginData", id);
    }

    /// <summary>本次进程启动之后才创建的包目录＝这个会话里刚安装/更新的插件。</summary>
    private static bool WasInstalledThisSession(string packageRoot)
    {
        try
        {
            var started = Process.GetCurrentProcess().StartTime.ToUniversalTime();
            var created = Directory.GetCreationTimeUtc(packageRoot);
            return created >= started.AddSeconds(-5);
        }
        catch
        {
            return false;
        }
    }

    private static Window? TryGetHostWindow()
    {
        try
        {
            var appType = FindHostType("TubaWinUi3.App");
            return appType?.GetProperty("MainWindow", BindingFlags.Public | BindingFlags.Static)?.GetValue(null) as Window;
        }
        catch
        {
            return null;
        }
    }

    private static XamlRoot? TryGetRoot() => (TryGetHostWindow()?.Content as FrameworkElement)?.XamlRoot;

    private static Type? FindHostType(string fullName)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            if (assembly.GetName().Name == HostAssemblyName)
                return assembly.GetType(fullName, throwOnError: false);
        return Type.GetType(fullName + ", " + HostAssemblyName, throwOnError: false);
    }

    private static Task NavigateAsync(string pageTypeName, object? parameter)
    {
        try
        {
            var window = TryGetHostWindow();
            var pageType = FindHostType(pageTypeName);
            if (window is null || pageType is null) return Task.CompletedTask;
            var navigate = window.GetType().GetMethod("NavigateToToolPage", BindingFlags.Public | BindingFlags.Instance,
                null, [typeof(Type), typeof(object)], null);
            if (navigate is not null)
            {
                navigate.Invoke(window, [pageType, parameter]);
                return Task.CompletedTask;
            }
            if (window.GetType().GetProperty("PluginNavigationFrame", BindingFlags.Public | BindingFlags.Instance)?.GetValue(window) is Frame frame)
                frame.Navigate(pageType, parameter);
        }
        catch
        {
            // 宿主改签名时静默失败，插件本身仍然可用。
        }
        return Task.CompletedTask;
    }

    private static Task NavigateSettingsAsync()
    {
        try
        {
            var window = TryGetHostWindow();
            if (window is null) return Task.CompletedTask;
            var navigate = window.GetType().GetMethod("NavigateToSettings", BindingFlags.Public | BindingFlags.Instance,
                null, [typeof(string)], null);
            if (navigate is not null)
            {
                navigate.Invoke(window, [null]);
                return Task.CompletedTask;
            }
            if (FindHostType("TubaWinUi3.Pages.SettingsPage") is { } page &&
                window.GetType().GetProperty("PluginNavigationFrame", BindingFlags.Public | BindingFlags.Instance)?.GetValue(window) is Frame frame)
                frame.Navigate(page);
        }
        catch
        {
            // 同上：设置页跳不过去不影响插件功能。
        }
        return Task.CompletedTask;
    }

    private static ContentDialog Build(XamlRoot root, string pluginName, string summary, IReadOnlyList<OnboardingEntry> entries,
        bool chinese, Action<OnboardingEntry> choose)
    {
        var body = new StackPanel { Spacing = 14, MinWidth = 420 };
        var dialog = new ContentDialog
        {
            XamlRoot = root,
            Title = chinese ? "安装完成" : "Installed",
            Content = body,
            CloseButtonText = chinese ? "完成" : "Done",
            DefaultButton = ContentDialogButton.Close
        };

        body.Children.Add(BuildHero(pluginName, summary));
        if (entries.Count > 0)
        {
            body.Children.Add(new TextBlock
            {
                Text = chinese ? "已添加" : "What's new",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold
            });
            foreach (var entry in entries)
            {
                Button? action = null;
                if (entry.Action is not null)
                {
                    action = new Button
                    {
                        Content = entry.ActionLabel,
                        VerticalAlignment = VerticalAlignment.Center,
                        Style = AccentButtonStyle()
                    };
                    action.Click += (_, _) =>
                    {
                        choose(entry);
                        dialog.Hide();
                    };
                }
                body.Children.Add(BuildEntryCard(entry, action));
            }
        }

        body.Children.Add(new TextBlock
        {
            Text = chinese
                ? "可随时在「插件 → 已安装」中停用或卸载。"
                : "You can disable or uninstall it anytime under Plugins → Installed.",
            FontSize = 11,
            Foreground = ThemeBrush("TextFillColorTertiaryBrush"),
            TextWrapping = TextWrapping.Wrap
        });
        return dialog;
    }

    private static Grid BuildHero(string pluginName, string summary)
    {
        var grid = new Grid { ColumnSpacing = 14 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var iconHost = new Grid();
        var glyph = new FontIcon { Glyph = "\uE774", FontSize = 26, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        iconHost.Children.Add(glyph);
        if (_packageRoot is not null && LoadPackageIcon(_packageRoot) is { } icon)
        {
            glyph.Visibility = Visibility.Collapsed;
            iconHost.Children.Add(new Image { Source = icon, Stretch = Stretch.Uniform, Margin = new Thickness(6) });
        }
        grid.Children.Add(new Border
        {
            Width = 52,
            Height = 52,
            CornerRadius = new CornerRadius(12),
            Background = ThemeBrush("SubtleFillColorSecondaryBrush"),
            Child = iconHost
        });

        var text = new StackPanel { Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock
        {
            Text = pluginName,
            FontSize = 18,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        if (!string.IsNullOrWhiteSpace(summary))
            text.Children.Add(new TextBlock
            {
                Text = summary,
                FontSize = 13,
                Foreground = ThemeBrush("TextFillColorSecondaryBrush"),
                TextWrapping = TextWrapping.Wrap
            });
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);
        return grid;
    }

    private static Border BuildEntryCard(OnboardingEntry entry, Button? action)
    {
        var grid = new Grid { ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        grid.Children.Add(new FontIcon
        {
            Glyph = entry.Glyph,
            FontSize = 20,
            Width = 32,
            Height = 32,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        });

        var text = new StackPanel { Spacing = 2 };
        if (!string.IsNullOrWhiteSpace(entry.Caption))
            text.Children.Add(new TextBlock
            {
                Text = entry.Caption,
                FontSize = 11,
                Foreground = ThemeBrush("TextFillColorTertiaryBrush")
            });
        text.Children.Add(new TextBlock
        {
            Text = entry.Title,
            FontSize = 14,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        if (!string.IsNullOrWhiteSpace(entry.Description))
            text.Children.Add(new TextBlock
            {
                Text = entry.Description,
                FontSize = 12,
                Foreground = ThemeBrush("TextFillColorSecondaryBrush"),
                TextWrapping = TextWrapping.Wrap
            });
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        if (action is not null)
        {
            Grid.SetColumn(action, 2);
            grid.Children.Add(action);
        }

        return new Border
        {
            Padding = new Thickness(14, 12, 12, 12),
            CornerRadius = new CornerRadius(8),
            BorderThickness = new Thickness(1),
            BorderBrush = ThemeBrush("CardStrokeColorDefaultBrush"),
            Background = ThemeBrush("CardBackgroundFillColorDefaultBrush"),
            Child = grid
        };
    }

    private static ImageSource? LoadPackageIcon(string packageRoot)
    {
        try
        {
            var relative = ReadManifestIcon(packageRoot);
            if (string.IsNullOrWhiteSpace(relative)) return null;
            var root = Path.GetFullPath(packageRoot) + Path.DirectorySeparatorChar;
            var path = Path.GetFullPath(Path.Combine(packageRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
            if (!path.StartsWith(root, StringComparison.OrdinalIgnoreCase) || !File.Exists(path)) return null;
            return Path.GetExtension(path).Equals(".svg", StringComparison.OrdinalIgnoreCase)
                ? new SvgImageSource(new Uri(path))
                : new BitmapImage(new Uri(path));
        }
        catch
        {
            return null;
        }
    }

    private static string? ReadManifestIcon(string packageRoot)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(Path.Combine(packageRoot, "tubatools.json")));
            return document.RootElement.TryGetProperty("icon", out var icon) ? icon.GetString() : null;
        }
        catch
        {
            return null;
        }
    }

    private static Style? AccentButtonStyle() =>
        Application.Current.Resources.TryGetValue("AccentButtonStyle", out var value) ? value as Style : null;

    private static Brush ThemeBrush(string key) =>
        Application.Current.Resources.TryGetValue(key, out var value) && value is Brush brush
            ? brush
            : new SolidColorBrush(Microsoft.UI.Colors.Transparent);
}

using System.Diagnostics;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace TubaWinUi3.Services;

public sealed class ContextMenuMgrTool : IBuiltinTool
{
    public string Id => "context-menu-mgr";
    public string Name => "右键菜单管理";
    public string Description => "管理 Windows 右键菜单项，支持添加/删除/编辑，来自 ContextMenuMgr 开源项目。";
    public string Glyph => "\uE74C";
    public string Category => "系统工具";
    public BuiltinToolKind Kind => BuiltinToolKind.InstantAction;

    private const string Repo = "PLFJY/ContextMenuMgr";
    private const string HubBase = "https://hub.tubawinui3.cn";
    private const string GitHubReleaseApi = $"https://api.github.com/repos/{Repo}/releases/latest";
    private const string HubReleaseApi = $"{HubBase}/api/repos/{Repo}/releases/latest";
    private const string ProjectUrl = $"https://github.com/{Repo}";

    private static readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    static ContextMenuMgrTool()
    {
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "TubaWinUi3-ContextMenuMgr");
    }

    public async Task ExecuteAsync(BuiltinToolContext context)
    {
        if (IsInstalled())
        {
            var exe = FindInstalledExe();
            if (exe is not null)
            {
                try { Process.Start(new ProcessStartInfo { FileName = exe, UseShellExecute = true }); return; }
                catch { }
            }
        }

        await OfferDownloadAsync(context);
    }

    private static bool IsInstalled() => FindInstalledExe() is not null;

    private static string? FindInstalledExe()
    {
        try
        {
            var keys = new[]
            {
                Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
                Microsoft.Win32.Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall"),
                Microsoft.Win32.Registry.LocalMachine.OpenSubKey(@"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall")
            };
            foreach (var key in keys)
            {
                if (key is null) continue;
                foreach (var sub in key.GetSubKeyNames())
                {
                    using var subKey = key.OpenSubKey(sub);
                    var name = subKey?.GetValue("DisplayName") as string;
                    if (name is not null && IsContextMenuMgr(name))
                    {
                        var loc = subKey?.GetValue("InstallLocation") as string;
                        if (!string.IsNullOrEmpty(loc))
                        {
                            loc = loc.TrimEnd('\\');
                            if (Directory.Exists(loc))
                            {
                                var exe = FindMainExe(loc);
                                if (exe is not null) return exe;
                            }
                        }
                    }
                }
            }
        }
        catch { }

        try
        {
            var programDirs = new[] { @"C:\Program Files", @"C:\Program Files (x86)" };
            foreach (var d in programDirs)
            {
                if (!Directory.Exists(d)) continue;
                foreach (var sub in Directory.GetDirectories(d))
                {
                    if (IsContextMenuMgr(Path.GetFileName(sub)))
                    {
                        var exe = FindMainExe(sub);
                        if (exe is not null) return exe;
                    }
                }
            }
        }
        catch { }

        return null;
    }

    private static bool IsContextMenuMgr(string name) =>
        name.Contains("ContextMenuMgr", StringComparison.OrdinalIgnoreCase) ||
        name.Contains("Context Menu Manager", StringComparison.OrdinalIgnoreCase);

    private static string? FindMainExe(string dir)
    {
        var candidates = new[] { "ContextMenuManagerPlus.exe", "ContextMenuMgrPlus.exe" };
        foreach (var c in candidates)
        {
            var p = Path.Combine(dir, c);
            if (File.Exists(p)) return p;
        }
        foreach (var f in Directory.GetFiles(dir, "*.exe", SearchOption.TopDirectoryOnly))
        {
            var name = Path.GetFileName(f);
            if (name.Contains("ContextMenuManager", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("ContextMenuMgr", StringComparison.OrdinalIgnoreCase))
                return f;
        }
        return null;
    }

    private async Task OfferDownloadAsync(BuiltinToolContext context)
    {
        if (context.XamlRoot is null) return;

        var arch = RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            Architecture.X86 => "x86",
            _ => "x64"
        };

        var dialog = context.CreateDialog("安装右键菜单管理", "取消");
        dialog.PrimaryButtonText = "下载安装";
        dialog.Resources["ContentDialogMaxWidth"] = 520;

        var stack = new StackPanel { Spacing = 12 };

        var descBorder = new Border
        {
            Padding = new Thickness(16, 12, 16, 12),
            Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock
                    {
                        Text = "ContextMenuMgr — Windows 右键菜单管理器",
                        FontSize = 15,
                        FontWeight = Microsoft.UI.Text.FontWeights.SemiBold
                    },
                    new TextBlock
                    {
                        Text = "一款开源的 Windows 右键菜单管理工具，可以方便地添加、删除、编辑右键菜单项，" +
                               "支持新建菜单、Shell 菜单、Win11 经典菜单等多种类型的管理操作。",
                        FontSize = 13,
                        TextWrapping = TextWrapping.Wrap,
                        Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
                    },
                    new TextBlock
                    {
                        Text = $"项目主页：{ProjectUrl}",
                        FontSize = 12,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 96, 165, 250))
                    }
                }
            }
        };
        stack.Children.Add(descBorder);

        var warningBorder = new Border
        {
            Padding = new Thickness(14, 10, 14, 10),
            Background = new SolidColorBrush(Color.FromArgb(30, 251, 146, 60)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(80, 251, 146, 60)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 8,
                Children =
                {
                    new FontIcon
                    {
                        Glyph = "\uE7BA",
                        FontSize = 16,
                        Foreground = new SolidColorBrush(Color.FromArgb(255, 251, 146, 60))
                    },
                    new TextBlock
                    {
                        Text = "注意：此软件运行后会在后台占用约 30MB 内存（托盘驻留进程）",
                        FontSize = 13,
                        VerticalAlignment = VerticalAlignment.Center,
                        TextWrapping = TextWrapping.Wrap
                    }
                }
            }
        };
        stack.Children.Add(warningBorder);

        var sizeInfo = new TextBlock
        {
            Text = $"将下载匹配当前架构（{arch}）的独立安装版（self-contained Setup，约 50MB），无需 .NET 运行时。",
            FontSize = 12,
            Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"],
            TextWrapping = TextWrapping.Wrap
        };
        stack.Children.Add(sizeInfo);

        dialog.Content = stack;

        var result = await dialog.ShowAsync();
        if (result == ContentDialogResult.Primary)
        {
            await ShowDownloadDialogAsync(context, arch);
        }
    }

    private async Task ShowDownloadDialogAsync(BuiltinToolContext context, string arch)
    {
        if (context.XamlRoot is null) return;

        var dialog = context.CreateDialog("下载 ContextMenuMgr", "取消");
        dialog.PrimaryButtonText = "开始下载";
        dialog.IsPrimaryButtonEnabled = false;
        dialog.Resources["ContentDialogMaxWidth"] = 520;

        var fileNameText = new TextBlock { FontSize = 12, Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"], TextWrapping = TextWrapping.Wrap, Text = "正在获取版本信息..." };

        var speedList = new StackPanel { Spacing = 4 };
        var speedScroll = new ScrollViewer { Content = speedList, MaxHeight = 160, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        var speedBorder = new Border
        {
            BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(10, 8, 10, 8),
            Child = speedScroll
        };

        var speedStatus = new TextBlock { FontSize = 13, Text = "正在测速选择最佳下载源...", Opacity = 0.72 };
        var speedPanel = new StackPanel { Spacing = 6 };
        speedPanel.Children.Add(speedStatus);
        speedPanel.Children.Add(speedBorder);

        var progressBar = new ProgressBar { IsIndeterminate = false, ShowError = false, ShowPaused = false, Visibility = Visibility.Collapsed };
        var percentText = new TextBlock { FontSize = 12, FontWeight = Microsoft.UI.Text.FontWeights.SemiBold, Foreground = (Brush)Application.Current.Resources["AccentTextFillColorPrimaryBrush"], Text = "0%", Visibility = Visibility.Collapsed };
        var speedText = new TextBlock { FontSize = 12, Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"], Text = "--", Visibility = Visibility.Collapsed };
        var sizeText = new TextBlock { FontSize = 12, Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"], Text = "--", Visibility = Visibility.Collapsed };
        var timeText = new TextBlock { FontSize = 12, Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"], Text = "--", Visibility = Visibility.Collapsed };
        var progressPanel = new StackPanel { Spacing = 10, Visibility = Visibility.Collapsed };
        progressPanel.Children.Add(progressBar);
        progressPanel.Children.Add(new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, Children = { speedText, sizeText, timeText } });
        progressPanel.Children.Add(percentText);

        var errorBar = new InfoBar { IsOpen = false, IsClosable = true, Severity = InfoBarSeverity.Error, Title = "下载失败" };

        var contentStack = new StackPanel { Spacing = 12, MinWidth = 420 };
        contentStack.Children.Add(fileNameText);
        contentStack.Children.Add(speedPanel);
        contentStack.Children.Add(progressPanel);
        contentStack.Children.Add(errorBar);
        dialog.Content = contentStack;

        var assetInfo = await ResolveDownloadAssetAsync(arch);
        if (assetInfo is null)
        {
            fileNameText.Text = "";
            errorBar.Message = "无法获取下载地址，请检查网络连接。";
            errorBar.IsOpen = true;
            await dialog.ShowAsync();
            return;
        }

        fileNameText.Text = $"文件：{assetInfo.Name}（{ToolDownloaderService.FormatSize(assetInfo.Size)}）";

        var bestUrl = assetInfo.OriginalUrl;
        var cts = new CancellationTokenSource();
        var downloadStarted = false;

        _ = Task.Run(async () =>
        {
            var results = new List<(string Name, string Url, double Ms, bool Ok)>();

            var tasks = new List<Task<(string Name, string Url, double Ms, bool Ok)>>();

            tasks.Add(TestSpeedAsync("GitHub 直连", assetInfo.OriginalUrl));

            foreach (var proxy in UpdateService.ProxyList)
            {
                var proxyUrl = UpdateService.BuildProxyUrl(proxy, assetInfo.OriginalUrl);
                var host = new Uri(proxy).Host;
                tasks.Add(TestSpeedAsync(host, proxyUrl));
            }

            var remaining = tasks.ToList();
            while (remaining.Count > 0)
            {
                var finished = await Task.WhenAny(remaining);
                remaining.Remove(finished);
                try
                {
                    var r = await finished;
                    results.Add(r);
                    dialog.DispatcherQueue.TryEnqueue(() =>
                    {
                        var color = r.Ok ? Color.FromArgb(255, 74, 222, 128) : Color.FromArgb(255, 248, 113, 113);
                        speedList.Children.Add(new TextBlock
                        {
                            FontSize = 12,
                            Foreground = new SolidColorBrush(color),
                            Text = r.Ok ? $"{r.Name}  —  {r.Ms:F0}ms" : $"{r.Name}  —  不可用"
                        });
                    });
                }
                catch { }
            }

            var best = results.Where(r => r.Ok).OrderBy(r => r.Ms).FirstOrDefault();
            if (best.Url is not null)
                bestUrl = best.Url;

            dialog.DispatcherQueue.TryEnqueue(() =>
            {
                speedStatus.Text = best.Ok ? $"已选择：{best.Name}（{best.Ms:F0}ms）" : "所有源均不可用，将尝试 GitHub 直连";
                speedStatus.Opacity = 1.0;
                dialog.IsPrimaryButtonEnabled = true;
            });
        });

        dialog.PrimaryButtonClick += async (s, e) =>
        {
            if (downloadStarted) { e.Cancel = true; return; }
            var deferral = e.GetDeferral();
            e.Cancel = true;

            downloadStarted = true;
            dialog.IsPrimaryButtonEnabled = false;

            try
            {
                speedPanel.Visibility = Visibility.Collapsed;
                progressPanel.Visibility = Visibility.Visible;
                progressBar.Visibility = Visibility.Visible;
                percentText.Visibility = Visibility.Visible;
                speedText.Visibility = Visibility.Visible;
                sizeText.Visibility = Visibility.Visible;
                timeText.Visibility = Visibility.Visible;
                dialog.PrimaryButtonText = "下载中...";

                var progress = new Progress<ToolDownloadProgress>(p =>
                {
                    dialog.DispatcherQueue.TryEnqueue(() =>
                    {
                        progressBar.Value = p.Percentage;
                        percentText.Text = $"{p.Percentage:F1}%";
                        speedText.Text = ToolDownloaderService.FormatSpeed(p.SpeedMbps);
                        sizeText.Text = $"{ToolDownloaderService.FormatSize(p.BytesReceived)} / {ToolDownloaderService.FormatSize(p.TotalBytes)}";
                        timeText.Text = ToolDownloaderService.FormatTime(p.EstimatedRemaining);
                    });
                });

                var tempDir = Path.Combine(Path.GetTempPath(), "TubaWinUi3_ContextMenuMgr");
                var filePath = await ToolDownloaderService.DownloadToFileAsync(
                    bestUrl, tempDir, assetInfo.Name, progress, cts.Token);

                dialog.Hide();

                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = filePath,
                        UseShellExecute = true
                    });
                }
                catch (Exception ex)
                {
                    var tipDialog = context.CreateDialog("启动安装程序失败", "确定");
                    tipDialog.Content = $"安装程序已下载到：{filePath}\n\n请手动运行。错误：{ex.Message}";
                    await tipDialog.ShowAsync();
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                errorBar.Message = ex.Message;
                errorBar.IsOpen = true;
                progressPanel.Visibility = Visibility.Collapsed;
                dialog.IsPrimaryButtonEnabled = true;
                dialog.PrimaryButtonText = "重试";
                downloadStarted = false;
            }
            finally
            {
                try { deferral.Complete(); } catch { }
            }
        };

        await dialog.ShowAsync();
    }

    private static async Task<(string Name, string Url, double Ms, bool Ok)> TestSpeedAsync(string name, string url)
    {
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
            client.DefaultRequestHeaders.Add("User-Agent", "TubaWinUi3-ContextMenuMgr");
            var sw = Stopwatch.StartNew();
            using var response = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, url),
                HttpCompletionOption.ResponseHeadersRead);
            sw.Stop();
            return (name, url, sw.Elapsed.TotalMilliseconds, response.IsSuccessStatusCode);
        }
        catch
        {
            return (name, url, double.MaxValue, false);
        }
    }

    private static async Task<AssetInfo?> ResolveDownloadAssetAsync(string arch)
    {
        string? json = null;

        try
        {
            using var hubClient = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
            hubClient.DefaultRequestHeaders.Add("User-Agent", "TubaWinUi3-ContextMenuMgr");
            var hubResponse = await hubClient.GetAsync(HubReleaseApi);
            if (hubResponse.IsSuccessStatusCode)
                json = await hubResponse.Content.ReadAsStringAsync();
        }
        catch { }

        if (json is null)
        {
            try
            {
                var response = await _httpClient.GetAsync(GitHubReleaseApi);
                if (response.IsSuccessStatusCode)
                    json = await response.Content.ReadAsStringAsync();
            }
            catch { }
        }

        if (json is null)
        {
            foreach (var proxy in UpdateService.ProxyList)
            {
                try
                {
                    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
                    client.DefaultRequestHeaders.Add("User-Agent", "TubaWinUi3-ContextMenuMgr");
                    var proxyUrl = UpdateService.BuildProxyUrl(proxy, GitHubReleaseApi);
                    var response = await client.GetAsync(proxyUrl);
                    if (response.IsSuccessStatusCode)
                    {
                        json = await response.Content.ReadAsStringAsync();
                        break;
                    }
                }
                catch { }
            }
        }

        if (json is null) return null;

        try
        {
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (!root.TryGetProperty("assets", out var assetsEl))
                return null;

            string? bestName = null;
            string? bestUrl = null;
            long bestSize = 0;

            foreach (var asset in assetsEl.EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? "";
                var url = asset.GetProperty("browser_download_url").GetString() ?? "";
                var size = asset.TryGetProperty("size", out var sizeEl) ? sizeEl.GetInt64() : 0L;

                if (name.Contains(arch, StringComparison.OrdinalIgnoreCase) &&
                    name.Contains("self-contained", StringComparison.OrdinalIgnoreCase) &&
                    name.Contains("Setup", StringComparison.OrdinalIgnoreCase) &&
                    name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    bestName = name;
                    bestUrl = url;
                    bestSize = size;
                    break;
                }
            }

            if (bestName is null)
            {
                foreach (var asset in assetsEl.EnumerateArray())
                {
                    var name = asset.GetProperty("name").GetString() ?? "";
                    var url = asset.GetProperty("browser_download_url").GetString() ?? "";
                    var size = asset.TryGetProperty("size", out var sizeEl) ? sizeEl.GetInt64() : 0L;

                    if (name.Contains(arch, StringComparison.OrdinalIgnoreCase) &&
                        name.Contains("self-contained", StringComparison.OrdinalIgnoreCase) &&
                        name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                    {
                        bestName = name;
                        bestUrl = url;
                        bestSize = size;
                        break;
                    }
                }
            }

            if (bestName is null || bestUrl is null) return null;

            return new AssetInfo(bestName, bestUrl, bestSize);
        }
        catch
        {
            return null;
        }
    }
}

public sealed record AssetInfo(string Name, string OriginalUrl, long Size);

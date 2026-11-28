using Microsoft.UI;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using TubaWinUi3.PluginSdk;
using Tubatools.MinecraftLauncher.Services;
using Windows.System;

namespace Tubatools.MinecraftLauncher.Ui;

/// <summary>Modrinth 资源浏览：模组 / 光影 / 资源包，一键下载到当前实例对应目录。</summary>
internal sealed class McResourceBrowser
{
    private readonly McLauncherView _shell;
    private readonly McPalette _palette;
    private readonly Grid _root = new();
    private readonly TextBox _search = new() { MinWidth = 220 };
    private readonly ComboBox _sort = new() { Width = 130, Height = 40 };
    private readonly ComboBox _loader = new() { Width = 145, Height = 40 };
    private readonly Button _searchButton;
    private readonly ProgressRing _loading = new() { IsActive = true, Width = 20, Height = 20, Visibility = Visibility.Collapsed };
    private readonly TextBlock _status = new() { FontSize = 12, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private readonly ProgressBar _installBar = new() { Height = 4, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 6, 0, 0) };
    private readonly TextBlock _installDetail = new() { FontSize = 10.5, Visibility = Visibility.Collapsed, Margin = new Thickness(0, 3, 0, 0) };
    private readonly StackPanel _results = new() { Spacing = 10 };
    private readonly ScrollViewer _scroll = new();
    private readonly Button _more;
    private readonly Button _openFolder;
    private readonly Button _loaderShortcut;
    private readonly List<McResourceHit> _hits = [];
    private string? _lastVersionId;
    public Action? RequestLoaderSetup { get; set; }
    private CancellationTokenSource? _searchCts;
    private string _type = "mod";
    private int _offset;
    private int _total;
    private bool _searching;
    private string? _installingId;
    private string? _lastFolder;

    private static readonly (string Tag, string Label)[] Sorts =
    [
        ("relevance", "相关度"),
        ("downloads", "下载量"),
        ("newest", "最新")
    ];

    private static readonly (string Tag, string Label)[] Loaders =
    [
        ("auto", "自动匹配加载器"),
        ("fabric", "Fabric"),
        ("forge", "Forge"),
        ("neoforge", "NeoForge"),
        ("quilt", "Quilt")
    ];

    public McResourceBrowser(McLauncherView shell)
    {
        _shell = shell;
        _palette = shell.Palette;
        foreach (var (tag, label) in Sorts)
            _sort.Items.Add(new ComboBoxItem { Content = label, Tag = tag });
        _sort.SelectedIndex = 0;
        foreach (var (tag, label) in Loaders)
            _loader.Items.Add(new ComboBoxItem { Content = label, Tag = tag });
        _loader.SelectedIndex = 0;

        _searchButton = McUi.Button("搜索", _palette, accent: true, glyph: "\uE721", height: 40);
        _searchButton.Click += async (_, _) => await SearchAsync(true);
        _search.KeyDown += async (_, args) =>
        {
            if (args.Key != VirtualKey.Enter) return;
            args.Handled = true;
            await SearchAsync(true);
        };
        _sort.SelectionChanged += async (_, _) => await SearchAsync(true);
        _loader.SelectionChanged += async (_, _) => await SearchAsync(true);
        _more = McUi.Button("加载更多", _palette, height: 38);
        _more.Click += async (_, _) => await SearchAsync(false);
        _more.Visibility = Visibility.Collapsed;
        _openFolder = McUi.Button("打开文件夹", _palette, height: 38, glyph: "\uE838", fontSize: 12);
        _openFolder.Click += (_, _) =>
        {
            var folder = _lastFolder;
            if (string.IsNullOrWhiteSpace(folder) && _shell.SelectedVersion is { } version)
                folder = FolderFor(version);
            if (!string.IsNullOrWhiteSpace(folder)) McLauncherView.OpenFolder(folder);
        };
        _loaderShortcut = McUi.Button("去安装加载器", _palette, accent: true, height: 38, fontSize: 12);
        _loaderShortcut.Visibility = Visibility.Collapsed;
        _loaderShortcut.Click += (_, _) => RequestLoaderSetup?.Invoke();

        _status.Foreground = _palette.Secondary;
        BuildLayout();
    }

    public FrameworkElement Root => _root;

    public void RefreshTarget()
    {
        var versionId = _shell.SelectedVersion?.Id;
        UpdateStatus();
        if (!string.Equals(versionId, _lastVersionId, StringComparison.OrdinalIgnoreCase))
        {
            _lastVersionId = versionId;
            RebuildResultCards();
            UpdateStatus();
        }
    }

    public void Refresh() => _ = SearchAsync(true);

    private void RebuildResultCards()
    {
        _results.Children.Clear();
        foreach (var hit in _hits)
            _results.Children.Add(BuildResultCard(hit));
        _more.Visibility = _hits.Count < _total ? Visibility.Visible : Visibility.Collapsed;
        if (_hits.Count < _total) _results.Children.Add(_more);
        if (_hits.Count == 0 && _total == 0 && _lastVersionId is not null)
            _results.Children.Add(McUi.Text("没有找到匹配的资源，换个关键词或加载器筛选试试。", 13,
                FontWeights.Normal, _palette.Secondary, true));
    }

    public void SetType(string type)
    {
        _type = type;
        _search.PlaceholderText = type switch
        {
            "shader" => "搜索光影包…",
            "resourcepack" => "搜索资源包…",
            "modpack" => "搜索整合包…",
            _ => "搜索模组…"
        };
        _loader.Visibility = type == "mod" ? Visibility.Visible : Visibility.Collapsed;
        _ = SearchAsync(true);
    }

    public void OnShown()
    {
        UpdateStatus();
        if (_results.Children.Count == 0 && !_searching) _ = SearchAsync(true);
    }

    private void BuildLayout()
    {
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        _root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });

        var toolbar = new Grid
        {
            Margin = new Thickness(24, 12, 24, 0),
            RowSpacing = 8,
            ColumnSpacing = 10
        };
        toolbar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        toolbar.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        toolbar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _search.Height = 40;
        toolbar.Children.Add(_search);
        toolbar.Children.Add(_searchButton);
        Grid.SetColumn(_searchButton, 1);
        var filters = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 10,
            VerticalAlignment = VerticalAlignment.Center
        };
        filters.Children.Add(_sort);
        filters.Children.Add(_loader);
        filters.Children.Add(_loading);
        Grid.SetRow(filters, 1);
        Grid.SetColumnSpan(filters, 2);
        toolbar.Children.Add(filters);
        _root.Children.Add(toolbar);

        var statusHost = new Grid
        {
            Margin = new Thickness(24, 10, 24, 8),
            ColumnSpacing = 10
        };
        statusHost.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        statusHost.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        statusHost.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var statusStack = new StackPanel { Spacing = 0 };
        statusStack.Children.Add(_status);
        statusStack.Children.Add(_installBar);
        statusStack.Children.Add(_installDetail);
        var statusCard = new Border
        {
            Background = _palette.Card,
            BorderBrush = _palette.CardStroke,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12, 8, 12, 8),
            Child = statusStack
        };
        statusHost.Children.Add(statusCard);
        _loaderShortcut.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(_loaderShortcut, 1);
        statusHost.Children.Add(_loaderShortcut);
        _openFolder.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(_openFolder, 2);
        statusHost.Children.Add(_openFolder);
        Grid.SetRow(statusHost, 1);
        _root.Children.Add(statusHost);

        _scroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _scroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        _scroll.VerticalScrollMode = ScrollMode.Enabled;
        _scroll.HorizontalScrollMode = ScrollMode.Disabled;
        _scroll.Padding = new Thickness(24, 4, 24, 16);
        _scroll.Content = _results;
        Grid.SetRow(_scroll, 2);
        _root.Children.Add(_scroll);
    }

    private void ResetStatus(string message, bool error = false)
    {
        _status.Text = message;
        _status.Foreground = error ? _palette.Danger : _palette.Secondary;
    }

    private void UpdateStatus()
    {
        if (_type == "modpack")
        {
            _loaderShortcut.Visibility = Visibility.Collapsed;
            ResetStatus("Modrinth 整合包 · 点击「安装」会创建独立实例，自动安装对应 Minecraft / 加载器并下载全部内容。");
            return;
        }

        var version = _shell.SelectedVersion;
        if (version is null)
        {
            _loaderShortcut.Visibility = Visibility.Collapsed;
            ResetStatus("尚未选择版本。请先在「启动」页或「版本」页选择一个实例，安装时会自动放入该实例的目录。");
            return;
        }

        var folder = FolderFor(version);
        _lastFolder = folder;
        var loader = DerivedLoader(version);
        _loaderShortcut.Visibility = _type == "mod" && loader is null
            ? Visibility.Visible
            : Visibility.Collapsed;
        if (_type == "mod" && loader is null)
        {
            ResetStatus($"目标实例：{version.Id}（未检测到模组加载器）· 模组不会生效，请先安装 Fabric / Forge / NeoForge。");
            return;
        }

        var loaderText = _type == "mod" ? " · " + LoaderName(loader) : "";
        if (_type == "shader")
            ResetStatus($"目标实例：{version.Id} · Minecraft {version.VanillaName} · 光影需要 Iris 或 OptiFine（可在「模组」页安装 Iris） · 安装到 {folder}");
        else
            ResetStatus($"目标实例：{version.Id} · Minecraft {version.VanillaName}{loaderText} · 安装到 {folder}");
    }

    private string? DerivedLoader(McVersion version)
    {
        if (version.HasNeoForge) return "neoforge";
        if (version.HasForge) return "forge";
        if (version.HasFabric) return "fabric";
        return null;
    }

    private string? SelectedLoader()
    {
        if (_type != "mod") return null;
        var tag = (_loader.SelectedItem as ComboBoxItem)?.Tag as string ?? "auto";
        if (tag != "auto") return tag;
        return _shell.SelectedVersion is { } version ? DerivedLoader(version) : null;
    }

    private static string LoaderName(string? loader) => loader switch
    {
        "fabric" => "Fabric",
        "forge" => "Forge",
        "neoforge" => "NeoForge",
        "quilt" => "Quilt",
        _ => "未知加载器"
    };

    private string? TargetGameVersion() => _shell.SelectedVersion switch
    {
        null => null,
        { VanillaName: { Length: > 0 } name } when name != "未知" => name,
        { } version => version.Id
    };

    private string? FolderFor(McVersion version)
    {
        var gameDirectory = McVersionStore.ResolveGameDirectory(version, _shell.Settings);
        var folder = _type switch
        {
            "shader" => "shaderpacks",
            "resourcepack" => "resourcepacks",
            _ => "mods"
        };
        return Path.Combine(gameDirectory, folder);
    }

    private async Task SearchAsync(bool reset)
    {
        if (reset && _searching) _searchCts?.Cancel();
        var cts = new CancellationTokenSource();
        _searchCts = cts;
        _searching = true;
        _loading.Visibility = Visibility.Visible;
        _searchButton.IsEnabled = false;
        UpdateStatus();
        if (reset)
        {
            _offset = 0;
            _total = 0;
            _hits.Clear();
            _lastVersionId = _shell.SelectedVersion?.Id;
            _results.Children.Clear();
        }

        try
        {
            var result = await McModrinth.SearchAsync(McHttp.Client, _type, _search.Text.Trim(),
                _type == "modpack" ? null : TargetGameVersion(),
                _type == "modpack" ? null : SelectedLoader(),
                (_sort.SelectedItem as ComboBoxItem)?.Tag as string ?? "relevance",
                _offset, 20, cts.Token);
            if (cts.IsCancellationRequested) return;
            _total = result.TotalHits;
            _results.Children.Remove(_more);
            foreach (var hit in result.Hits)
            {
                _hits.Add(hit);
                _results.Children.Add(BuildResultCard(hit));
            }
            _offset += result.Hits.Count;
            _more.Visibility = _offset < _total ? Visibility.Visible : Visibility.Collapsed;
            if (_offset < _total) _results.Children.Add(_more);
            if (_total == 0)
                _results.Children.Add(McUi.Text("没有找到匹配的资源，换个关键词或加载器筛选试试。", 13,
                    FontWeights.Normal, _palette.Secondary, true));
            var version = _shell.SelectedVersion;
            if (version is not null && _type == "mod" && SelectedLoader() is null)
                ResetStatus("当前实例没有模组加载器，模组不会生效。请先在「游戏版本」页安装 Fabric / Forge / NeoForge。");
            else if (version is not null)
                ResetStatus($"目标实例：{version.Id} · 共 {_total:N0} 个结果 · 安装到 {FolderFor(version)}");
            else
                ResetStatus($"共 {_total:N0} 个结果。请先在「启动」页选择实例后再安装。");
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            ResetStatus("搜索失败：" + ex.Message, true);
            _shell.AppendLog("Modrinth 搜索失败：" + ex.Message);
        }
        finally
        {
            if (ReferenceEquals(_searchCts, cts))
            {
                _searching = false;
                _loading.Visibility = Visibility.Collapsed;
                _searchButton.IsEnabled = true;
                cts.Dispose();
            }
        }
    }

    private async Task LoadIconAsync(ImageBrush brush, McResourceHit hit)
    {
        try
        {
            var source = await McIconCache.LoadAsync(McHttp.Client, _shell.DataDirectory, hit.ProjectId,
                hit.IconUrl, CancellationToken.None);
            if (source is not null) brush.ImageSource = source;
        }
        catch
        {
            // 图标失败不影响资源列表。
        }
    }

    private Border BuildResultCard(McResourceHit hit)
    {
        var card = new Border
        {
            Background = _palette.Card,
            BorderBrush = _palette.CardStroke,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(12),
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        card.PointerEntered += (_, _) => card.Background = _palette.CardHover;
        card.PointerExited += (_, _) => card.Background = _palette.Card;

        var grid = new Grid { ColumnSpacing = 14 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var iconHost = new Grid { Width = 54, Height = 54 };
        iconHost.Children.Add(McArt.BrandIcon(40));
        var iconBrush = new ImageBrush { Stretch = Stretch.UniformToFill };
        iconHost.Children.Add(new Border
        {
            CornerRadius = new CornerRadius(4),
            Background = iconBrush
        });
        if (!string.IsNullOrWhiteSpace(hit.IconUrl))
            _ = LoadIconAsync(iconBrush, hit);

        var iconBorder = new Border
        {
            Width = 54,
            Height = 54,
            CornerRadius = new CornerRadius(4),
            Background = _palette.Slot,
            BorderBrush = _palette.CardStroke,
            BorderThickness = new Thickness(1),
            Child = iconHost,
            VerticalAlignment = VerticalAlignment.Top
        };
        grid.Children.Add(iconBorder);

        var info = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        var titleRow = new Grid { ColumnSpacing = 8 };
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        titleRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var title = McUi.Text(hit.Title, 15, FontWeights.SemiBold, _palette.Text);
        title.TextTrimming = TextTrimming.CharacterEllipsis;
        titleRow.Children.Add(title);
        var downloadChip = McUi.Chip(McModrinth.FormatDownloads(hit.Downloads) + " 下载", _palette);
        Grid.SetColumn(downloadChip, 1);
        titleRow.Children.Add(downloadChip);
        info.Children.Add(titleRow);
        var description = McUi.Text(hit.Description, 12, FontWeights.Normal, _palette.Secondary, true);
        description.MaxLines = 2;
        description.TextTrimming = TextTrimming.CharacterEllipsis;
        info.Children.Add(description);
        var categories = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6 };
        foreach (var category in hit.Categories.Take(2))
            categories.Children.Add(McUi.Chip(category, _palette));
        categories.Children.Add(McUi.Chip("Modrinth", _palette));
        info.Children.Add(categories);
        Grid.SetColumn(info, 1);
        grid.Children.Add(info);

        var version = _shell.SelectedVersion;
        var blocked = _type == "mod" && version is not null && DerivedLoader(version) is null;
        var install = McUi.Button("安装", _palette, accent: true, height: 40, glyph: "\uE896");
        install.VerticalAlignment = VerticalAlignment.Center;
        install.IsEnabled = _type == "modpack" || (version is not null && !blocked);
        if (_type == "modpack") ToolTipService.SetToolTip(install, "创建新的整合包实例");
        else if (version is null) ToolTipService.SetToolTip(install, "请先选择一个实例");
        else if (blocked) ToolTipService.SetToolTip(install, "当前实例没有模组加载器，请先安装 Fabric / Forge / NeoForge");
        install.Click += async (_, _) => await InstallAsync(hit, install);
        Grid.SetColumn(install, 2);
        grid.Children.Add(install);

        card.Child = grid;
        return card;
    }

    private async Task InstallAsync(McResourceHit hit, Button button)
    {
        if (_installingId is not null)
        {
            _shell.Notify("另一个资源正在安装，请稍候。", InfoBarSeverity.Warning);
            return;
        }

        if (_type == "modpack")
        {
            await InstallModpackAsync(hit, button);
            return;
        }

        var version = _shell.SelectedVersion;
        if (version is null)
        {
            _shell.Notify("请先在「启动」或「版本」页选择一个实例。", InfoBarSeverity.Warning, "需要实例");
            return;
        }

        var folder = FolderFor(version);
        if (folder is null) return;
        if (_type == "mod" && DerivedLoader(version) is null)
        {
            _shell.Notify("当前实例没有模组加载器，请先在「游戏版本」页安装 Fabric / Forge / NeoForge。",
                InfoBarSeverity.Warning, "无法安装模组");
            return;
        }

        _installingId = hit.ProjectId;
        button.IsEnabled = false;
        ResetStatus($"正在解析 {hit.Title} 的兼容文件…");
        try
        {
            var (target, dependencyCount) = await InstallBundleAsync(hit, folder);
            _lastFolder = folder;
            ResetStatus($"已安装：{target}" +
                        (dependencyCount > 0 ? $"（同时安装了 {dependencyCount} 个依赖）" : ""));
            _shell.Notify($"《{hit.Title}》已安装到 {Path.GetFileName(folder)}" +
                          (dependencyCount > 0 ? $"，并自动安装了 {dependencyCount} 个依赖。" : "。"),
                InfoBarSeverity.Success, "安装完成");
            _shell.AppendLog($"[资源] 已安装：{target}");
        }
        catch (Exception ex)
        {
            ResetStatus("安装失败：" + ex.Message, true);
            _shell.AppendLog("[资源] 安装失败：" + ex);
            _shell.Notify(ex.Message, InfoBarSeverity.Error, "安装失败");
        }
        finally
        {
            _installingId = null;
            button.IsEnabled = true;
        }
    }

    private async Task InstallModpackAsync(McResourceHit hit, Button button)
    {
        _installingId = hit.ProjectId;
        button.IsEnabled = false;
        ResetStatus($"正在解析整合包 {hit.Title}…");
        try
        {
            await _shell.EnsureJavaAsync(true);
            var settings = _shell.Settings;
            if (!settings.VersionIsolation)
            {
                settings.VersionIsolation = true;
                settings.Save();
            }

            var file = await McModrinth.ResolveFileAsync(McHttp.Client, hit.ProjectId, "modpack",
                null, null, CancellationToken.None)
                       ?? throw new InvalidOperationException($"没有找到 {hit.Title} 的整合包文件。");
            _installBar.Visibility = Visibility.Visible;
            _installBar.IsIndeterminate = true;
            _installDetail.Visibility = Visibility.Visible;
            _installDetail.Text = "准备下载…";
            var queueStatus = new Progress<string>(status => ResetStatus(status));
            var result = await McUnifiedDownloads.RunTaskAsync(
                "整合包 · " + hit.Title, "创建 Minecraft 整合包实例", settings.GameDirectory,
                async (status, ct) =>
                {
                    var progress = new Progress<McInstallProgress>(value =>
                    {
                        _installBar.IsIndeterminate = value.Total <= 0;
                        if (value.Total > 0)
                            _installBar.Value = Math.Clamp(value.Done * 100.0 / value.Total, 0, 100);
                        _installDetail.Text = value.Total > 0 ? $"{value.Done} / {value.Total}" : value.Stage;
                        status.Report($"{value.Stage} {value.Done}/{value.Total}");
                    });
                    return await McModpackInstaller.InstallAsync(McHttp.Client, file, hit.Title, hit.Slug,
                        settings.GameDirectory, Path.Combine(_shell.DataDirectory, "cache"), settings.Mirror,
                        _shell.Javas, settings.JavaPath, settings.GameLanguage, progress,
                        line => _shell.AppendLog(line), ct);
                }, queueStatus, CancellationToken.None);

            await _shell.RefreshVersionsAsync(true);
            var installed = _shell.Versions.FirstOrDefault(candidate =>
                string.Equals(candidate.Id, result.VersionId, StringComparison.OrdinalIgnoreCase));
            if (installed is not null) _shell.SelectVersion(installed);
            _installBar.Visibility = Visibility.Collapsed;
            _installDetail.Visibility = Visibility.Collapsed;
            ResetStatus($"整合包已安装：{result.Name}（{result.VersionId}，{result.Files} 个文件）");
            _shell.Notify($"整合包《{result.Name}》已安装为独立实例。", InfoBarSeverity.Success, "安装完成");
        }
        catch (Exception ex)
        {
            _installBar.Visibility = Visibility.Collapsed;
            _installDetail.Visibility = Visibility.Collapsed;
            ResetStatus("整合包安装失败：" + ex.Message, true);
            _shell.AppendLog("[整合包] 安装失败：" + ex);
            _shell.Notify(ex.Message, InfoBarSeverity.Error, "安装失败");
        }
        finally
        {
            _installingId = null;
            button.IsEnabled = true;
        }
    }

    private async Task<(string Target, int Dependencies)> InstallBundleAsync(McResourceHit hit, string folder)
    {
        var file = await McModrinth.ResolveFileAsync(McHttp.Client, hit.ProjectId, _type,
            TargetGameVersion(), SelectedLoader(), CancellationToken.None)
                   ?? throw new InvalidOperationException($"没有找到 {hit.Title} 适用于当前版本 / 加载器的文件。");
        Directory.CreateDirectory(folder);
        var target = await DownloadFileAsync(file, folder);

        var dependencies = 0;
        if (_type != "mod") return (target, dependencies);

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { hit.ProjectId };
        var queue = new Queue<string>(file.RequiredProjectIds);
        while (queue.Count > 0 && dependencies < 16)
        {
            var projectId = queue.Dequeue();
            if (!visited.Add(projectId)) continue;
            ResetStatus($"正在安装依赖（{dependencies + 1}）…");
            McResourceFile? dependency;
            try
            {
                dependency = await McModrinth.ResolveFileAsync(McHttp.Client, projectId, _type,
                    TargetGameVersion(), SelectedLoader(), CancellationToken.None);
            }
            catch (Exception ex)
            {
                _shell.AppendLog($"[资源] 依赖 {projectId} 解析失败，已跳过：{ex.Message}");
                continue;
            }

            if (dependency is null)
            {
                _shell.AppendLog($"[资源] 依赖 {projectId} 没有兼容文件，已跳过。");
                continue;
            }

            await DownloadFileAsync(dependency, folder);
            dependencies++;
            foreach (var nested in dependency.RequiredProjectIds)
                queue.Enqueue(nested);
        }

        return (target, dependencies);
    }

    private async Task<string> DownloadFileAsync(McResourceFile file, string folder)
    {
        var fileName = Path.GetFileName(file.FileName);
        if (string.IsNullOrWhiteSpace(fileName)) fileName = "resource.jar";
        var target = Path.Combine(folder, fileName);
        ResetStatus($"正在下载 {fileName}…（{FormatSize(file.Size)}）");
        _shell.AppendLog($"[资源] 下载 {fileName} → {target}");
        return await McUnifiedDownloads.DownloadFileAsync($"Minecraft 资源 · {fileName}", file.Url, folder,
            fileName, file.Size, CancellationToken.None);
    }

    private static string FormatSize(long bytes)
    {
        if (bytes <= 0) return "大小未知";
        string[] units = ["B", "KB", "MB", "GB"];
        double value = bytes;
        var unit = 0;
        while (value >= 1024 && unit < units.Length - 1)
        {
            value /= 1024;
            unit++;
        }

        return value.ToString(unit == 0 ? "N0" : "N1") + " " + units[unit];
    }
}

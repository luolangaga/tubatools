using System.Globalization;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Media.Imaging;
using TubaWinUi3.Services;
using TubaWinUi3.Services.Plugins;
using Windows.Foundation;
using Windows.UI;

namespace TubaWinUi3.Pages;

public sealed partial class PluginsPage : Page, ILocalizablePage
{
    private CancellationTokenSource? _lifetime;
    private List<PluginPackage> _packages = [];
    private readonly Dictionary<string, ImageSource?> _packageIcons = new(StringComparer.OrdinalIgnoreCase);
    /// <summary>包内清单预览（真名字 + 真图标）的进程内缓存，翻页或重进页面都不再重复取 Range。</summary>
    private static readonly Dictionary<string, PackagePreview?> PreviewCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object PreviewGate = new();
    /// <summary>详情页 nuspec + README 的进程内缓存；列表页不读这些字节。</summary>
    private static readonly Dictionary<string, PackageDetails?> DetailsCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly object DetailsGate = new();
    private readonly Dictionary<string, ImageSource?> _previewIcons = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, RowView> _availableRows = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Border> _heroTiles = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _previewStarted = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _previewGate = new(3);
    private readonly Windows.UI.ViewManagement.UISettings _uiSettings = new();
    private bool _busy;
    private bool _hasMore;
    private bool _showDocs;
    private bool _discover = true;
    private bool _detailOpen;
    private bool _detailBusy;
    private bool _heroFloatOn;
    private bool _animationsEnabled = true;
    private int _page;
    private int _navUnderlineX;
    private PluginPackage? _selectedPackage;
    private InstalledPlugin? _selectedInstalled;
    private Border? _detailSourceIcon;
    private static string L(string key, string fallback) => LocalizationService.L("Plugins_" + key, fallback);

    public PluginsPage()
    {
        InitializeComponent();
        _animationsEnabled = _uiSettings.AnimationsEnabled;
        NavBar.SizeChanged += (_, _) => PositionNavUnderline(false);
        AvailableList.SizeChanged += (_, _) => UpdateGridMetrics();
        InstalledList.SizeChanged += (_, _) => UpdateGridMetrics();
        ActualThemeChanged += (_, _) =>
        {
            if (_selectedPackage is not null) ApplyDetailHeroGradient(_selectedPackage);
        };
    }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _showDocs = e.Parameter is "docs";
    }

    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        _lifetime = new CancellationTokenSource();
        _animationsEnabled = _uiSettings.AnimationsEnabled;
        ApplyTooltips();
        ApplySafeModeState();
        SetTab(true, animate: false);
        RefreshInstalled();
        UpdateSectionHeaders();
        UpdateHero();
        PositionNavUnderline(false);
        UpdateGridMetrics();
        AnimatePageEntrance();
        if (_showDocs) { _showDocs = false; await ShowDocsAsync(); }
        await SearchAsync();
    }

    private void Page_Unloaded(object sender, RoutedEventArgs e)
    {
        _lifetime?.Cancel();
        StopHeroFloat();
    }

    public void ApplyLocalization()
    {
        if (!IsLoaded) return;
        ApplyTooltips();
        ApplySafeModeState();
        UpdateSectionHeaders();
        RefreshInstalled();
        RenderAvailable(0);
        if (_detailOpen && _selectedPackage is not null)
        {
            PopulateDetailHeader(_selectedPackage, _selectedInstalled);
            PopulateDetailFacts(_selectedPackage, _selectedInstalled, CachedDetails(_selectedPackage));
        }
    }

    private void ApplyTooltips()
    {
        ToolTipService.SetToolTip(RefreshButton, L("Refresh", "刷新"));
        ToolTipService.SetToolTip(DetailBackButton, L("Back", "返回"));
        ToolTipService.SetToolTip(DetailTopNuGet, L("NuGetPage", "NuGet 页面"));
    }

    private void SetBusy(bool value)
    {
        _busy = value;
        Busy.IsActive = value;
        SearchBox.IsEnabled = !value;
        RefreshButton.IsEnabled = !value;
        DiscoverTab.IsEnabled = InstalledTab.IsEnabled = !value;
        LoadMoreButton.IsEnabled = !value && _hasMore;
        if (value)
        {
            StartRefreshSpin();
            if (_discover && AvailableList.Items.Count == 0)
            {
                AvailableSkeleton.Visibility = Visibility.Visible;
                StartAvailableSkeletonPulse();
            }
            else
            {
                AnimateScalar(ElementCompositionPreview.GetElementVisual(AvailableList), "Opacity", .62f, 240);
            }
        }
        else
        {
            StopRefreshSpin();
            StopAvailableSkeletonPulse();
            AvailableSkeleton.Visibility = Visibility.Collapsed;
            AnimateScalar(ElementCompositionPreview.GetElementVisual(AvailableList), "Opacity", 1f, 240);
        }
        UpdateEmptyState();
    }

    /// <summary>GridView 的卡片槽宽度必须跟窗口联动，WrapGrid 才能稳定得到 3/2/1 列。</summary>
    private void UpdateGridMetrics()
    {
        UpdateGridMetrics(AvailableList, 126);
        UpdateGridMetrics(InstalledList, 144);
    }

    private static void UpdateGridMetrics(GridView grid, double itemHeight)
    {
        if (grid?.ItemsPanelRoot is not ItemsWrapGrid panel) return;
        var width = grid.ActualWidth;
        if (width <= 1) return;
        var columns = width >= 780 ? 3 : width >= 520 ? 2 : 1;
        var gap = 12.0;
        var itemWidth = Math.Max(180.0, (width - columns * gap) / columns);
        if (Math.Abs(panel.ItemWidth - itemWidth) < .5 && Math.Abs(panel.ItemHeight - itemHeight) < .5) return;
        panel.ItemWidth = itemWidth;
        panel.ItemHeight = itemHeight;
    }

    private void Notice(string message, bool error = false)
    {
        Status.Message = message;
        Status.Severity = error ? InfoBarSeverity.Error : InfoBarSeverity.Informational;
        Status.IsOpen = true;
    }

    private async Task SearchAsync(bool append = false)
    {
        if (_busy || _lifetime is null || _lifetime.IsCancellationRequested) return;
        if (append && !_hasMore) return;

        SetBusy(true);
        if (!append) Status.IsOpen = false;
        var previousPage = _page;
        if (append) _page++;
        try
        {
            var result = await NuGetPluginSource.SearchAsync(SearchBox.Text, _page, _lifetime.Token);
            if (append)
            {
                var existing = _packages.Select(PackageKey).ToHashSet(StringComparer.OrdinalIgnoreCase);
                var start = _packages.Count;
                _packages.AddRange(result.Packages.Where(p => existing.Add(PackageKey(p))));
                RenderAvailable(start);
            }
            else
            {
                _packages = result.Packages.ToList();
                RenderAvailable(0);
            }
            _hasMore = result.HasMore;
            UpdateSectionHeaders();
            UpdateEmptyState();
            UpdateHero();
            if (!append && _packages.Count == 0)
                AvailableEmptyHint.Text = L("Empty", "未找到插件。插件需要在 NuGet 上标记 Tubatools，且已完成索引。");
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            if (append) _page = previousPage;
            Notice(ex.Message, true);
        }
        finally
        {
            SetBusy(false);
            UpdateEmptyState();
        }
    }

    private void RenderAvailable(int startIndex)
    {
        var installedPackages = PluginManager.GetInstalled();
        if (startIndex == 0)
        {
            AvailableList.Items.Clear();
            _availableRows.Clear();
            _previewStarted.Clear();
        }

        var pending = new List<(PluginPackage Package, RowView Row)>();
        var added = new List<Border>();
        for (int i = Math.Max(0, startIndex); i < _packages.Count; i++)
        {
            var package = _packages[i];
            var installed = installedPackages.FirstOrDefault(p => p.Id.Equals(package.Id, StringComparison.OrdinalIgnoreCase));
            var state = Describe(package, installed);
            var key = PackageKey(package);
            var button = new Button { Content = state.Label, IsEnabled = state.Enabled };
            if (state.Accent) button.Style = (Style)Resources["PluginInstallButton"];
            button.Click += async (_, _) => await OpenDetailAsync(package, installed, _availableRows.GetValueOrDefault(key)?.Icon);
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, VerticalAlignment = VerticalAlignment.Center };
            actions.Children.Add(button);

            var row = Row(package.Id, AvailableMeta(package, installed), package.Description,
                PackageIcon(package.IconUrl), actions, state.Badge);
            row.Card.Tag = package;
            AvailableList.Items.Add(row.Card);
            added.Add(row.Card);
            _availableRows[key] = row;
            // NuGet 索引里没有真名字和图标，先同步套用缓存过的包内清单，避免列表闪一下包 ID。
            if (!TryApplyCachedPreview(package, row)) pending.Add((package, row));
        }

        foreach (var (package, row) in pending) QueuePreview(package, row);
        UpdateGridMetrics();
        AnimateCardsIn(added, startIndex);
        AnimateSectionHeader(AvailableSectionTitle, AvailableCount);
        UpdateSectionHeaders();
        UpdateEmptyState();
    }

    private static (string Label, bool Enabled, bool Accent, string? Badge) Describe(PluginPackage package, InstalledPlugin? installed)
    {
        if (installed is null) return (L("InstallAction", "安装"), true, true, null);
        return CompareVersions(package.Version, installed.Version) > 0
            ? (L("UpdateAction", "更新"), true, true, L("UpdateAvailable", "可更新"))
            : (L("AlreadyInstalled", "已安装"), false, false, L("AlreadyInstalled", "已安装"));
    }

    private static string AvailableMeta(PluginPackage package, InstalledPlugin? installed)
    {
        var parts = new List<string> { "v" + package.Version };
        if (installed is not null) parts.Add(string.Format(L("InstalledVersion", "已安装 {0}"), installed.Version));
        if (!string.IsNullOrWhiteSpace(package.Authors)) parts.Add(string.Format(L("AuthorFormat", "作者：{0}"), package.Authors));
        parts.Add(string.Format(L("DownloadsFormat", "下载 {0:N0} 次"), package.TotalDownloads));
        return string.Join("  ·  ", parts);
    }

    /// <summary>列表卡片：64px 图标 + 标题/元信息/描述 + 右侧动作，Store 搜索结果的排法。</summary>
    private RowView Row(string title, string meta, string description, ImageSource? icon,
        FrameworkElement actions, string? badge, string? status = null, bool statusWarning = false)
    {
        var card = new Border { Style = (Style)Resources["PluginRowCard"] };
        var root = new Grid();
        var hover = new Border
        {
            Background = ResourceBrush("SubtleFillColorSecondaryBrush"),
            CornerRadius = new CornerRadius(8),
            IsHitTestVisible = false,
            Opacity = 0
        };
        root.Children.Add(hover);

        var layout = new Grid { Style = (Style)Resources["PluginCardInner"] };
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        layout.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var tile = IconTile(icon, 48);
        layout.Children.Add(tile);

        var text = new StackPanel { Spacing = 3, VerticalAlignment = VerticalAlignment.Center };
        var head = new Grid { ColumnSpacing = 6 };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var titleText = new TextBlock
        {
            Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1,
            VerticalAlignment = VerticalAlignment.Center
        };
        head.Children.Add(titleText);
        if (!string.IsNullOrWhiteSpace(badge))
        {
            var badgeText = new TextBlock { Text = badge, FontSize = 10, FontWeight = FontWeights.SemiBold };
            var badgeBorder = new Border
            {
                Style = (Style)Resources["PluginVersionChip"],
                Child = badgeText,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(badgeBorder, 1);
            head.Children.Add(badgeBorder);
        }
        text.Children.Add(head);

        var metaText = new TextBlock
        {
            Text = meta, FontSize = 11, Foreground = ResourceBrush("TextFillColorSecondaryBrush"),
            TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1
        };
        text.Children.Add(metaText);

        if (!string.IsNullOrWhiteSpace(description))
            text.Children.Add(new TextBlock
            {
                Text = description, FontSize = 12, Foreground = ResourceBrush("TextFillColorSecondaryBrush"),
                TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis
            });

        if (!string.IsNullOrWhiteSpace(status))
        {
            var statusText = new TextBlock
            {
                Text = status, FontSize = 11, TextWrapping = TextWrapping.Wrap, MaxLines = 2,
                Foreground = ResourceBrush(statusWarning ? "SystemFillColorCautionBrush" : "TextFillColorTertiaryBrush")
            };
            ToolTipService.SetToolTip(statusText, status);
            text.Children.Add(statusText);
        }

        Grid.SetColumn(text, 1);
        layout.Children.Add(text);
        Grid.SetColumn(actions, 2);
        layout.Children.Add(actions);
        root.Children.Add(layout);
        card.Child = root;

        AttachCardMotion(card, hover, tile);
        return new RowView(card, titleText, metaText, tile, title);
    }

    /// <summary>行内需要被包内清单回填的控件引用：标题、元信息、图标框。</summary>
    private sealed record RowView(Border Card, TextBlock Title, TextBlock Meta, Border Icon, string DefaultTitle);

    /// <summary>包图标缺失/加载失败时回退到插件字形，不影响列表布局。</summary>
    private Border IconTile(ImageSource? icon, double size)
    {
        var tile = new Border { Style = (Style)Resources["PluginIconTile"], Width = size, Height = size };
        var content = new Grid();
        var glyph = new FontIcon { Glyph = "\uE774", FontSize = size * 0.5, Style = (Style)Resources["PluginIconGlyph"] };
        content.Children.Add(glyph);
        tile.Background = ResourceBrush("SubtleFillColorSecondaryBrush");
        if (icon is not null)
        {
            glyph.Visibility = Visibility.Collapsed;
            tile.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
            var image = new Image { Source = icon, Stretch = Stretch.Uniform, Margin = new Thickness(6) };
            image.ImageFailed += (_, _) =>
            {
                image.Visibility = Visibility.Collapsed;
                glyph.Visibility = Visibility.Visible;
                tile.Background = ResourceBrush("SubtleFillColorSecondaryBrush");
            };
            content.Children.Add(image);
            FadeInElement(image, 180);
        }
        tile.Child = content;
        return tile;
    }

    private void ShowIcon(Border tile, ImageSource icon, bool keepBackground = false)
    {
        if (tile.Child is not Grid content || content.Children.Count == 0) return;
        var glyph = (FontIcon)content.Children[0];
        while (content.Children.Count > 1) content.Children.RemoveAt(1);
        var image = new Image { Source = icon, Stretch = Stretch.Uniform, Margin = new Thickness(6) };
        image.ImageFailed += (_, _) =>
        {
            image.Visibility = Visibility.Collapsed;
            glyph.Visibility = Visibility.Visible;
            if (!keepBackground) tile.Background = ResourceBrush("SubtleFillColorSecondaryBrush");
        };
        glyph.Visibility = Visibility.Collapsed;
        if (!keepBackground) tile.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        content.Children.Add(image);
        FadeInElement(image, 180);
    }

    private void ResetIconTile(Border tile, bool keepBackground)
    {
        if (tile.Child is not Grid content || content.Children.Count == 0) return;
        while (content.Children.Count > 1) content.Children.RemoveAt(1);
        if (content.Children[0] is FontIcon glyph) glyph.Visibility = Visibility.Visible;
        if (!keepBackground) tile.Background = ResourceBrush("SubtleFillColorSecondaryBrush");
    }

    private ImageSource? PackageIcon(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            return null;
        if (_packageIcons.TryGetValue(url, out var cached)) return cached;
        ImageSource? image = null;
        try { image = new BitmapImage { UriSource = uri, DecodePixelWidth = 128 }; }
        catch { }
        _packageIcons[url] = image;
        return image;
    }

    private static string PackageKey(PluginPackage package) => package.Id + "|" + package.Version;

    private static PackageDetails? CachedDetails(PluginPackage package)
    {
        lock (DetailsGate) return DetailsCache.GetValueOrDefault(PackageKey(package));
    }

    private static PackagePreview? CachedPreview(PluginPackage package)
    {
        lock (PreviewGate) return PreviewCache.GetValueOrDefault(PackageKey(package));
    }

    /// <summary>命中缓存就同步回填标题/元信息，图标异步解码；未命中返回 false 交给后台补全。</summary>
    private bool TryApplyCachedPreview(PluginPackage package, RowView row)
    {
        PackagePreview? preview;
        lock (PreviewGate)
        {
            if (!PreviewCache.TryGetValue(PackageKey(package), out preview)) return false;
        }
        if (preview is not null)
        {
            ApplyPreviewText(package, row, preview);
            _ = ApplyPreviewIconAsync(package, row, preview);
        }
        return true;
    }

    /// <summary>包内清单的名字替换包 ID；包 ID 留在元信息里，不再重复出现在标题。</summary>
    private static void ApplyPreviewText(PluginPackage package, RowView row, PackagePreview preview)
    {
        if (string.IsNullOrWhiteSpace(preview.Name)) return;
        row.Title.Text = preview.Name;
    }

    private async Task ApplyPreviewIconAsync(PluginPackage package, RowView row, PackagePreview preview)
    {
        try
        {
            var icon = await PreviewIconAsync(package, preview);
            if (icon is null) return;
            if (!ReferenceEquals(_availableRows.GetValueOrDefault(PackageKey(package)), row)) return; // 已重新渲染，别把旧行写脏
            ShowIcon(row.Icon, icon);
            if (_heroTiles.TryGetValue(PackageKey(package), out var heroTile))
                ShowIcon(heroTile, icon, keepBackground: true);
        }
        catch (Exception) { /* 图标只是装饰，失败保持字形 */ }
    }

    /// <summary>包内图标字节转 ImageSource：SVG 走 SvgImageSource，其余按位图解码。</summary>
    private async Task<ImageSource?> PreviewIconAsync(PluginPackage package, PackagePreview preview)
    {
        var key = PackageKey(package);
        if (_previewIcons.TryGetValue(key, out var cached)) return cached;
        ImageSource? icon = null;
        if (preview.IconBytes is { Length: > 0 } bytes)
        {
            try
            {
                using var stream = new MemoryStream(bytes).AsRandomAccessStream();
                if (preview.IconExtension.Equals(".svg", StringComparison.OrdinalIgnoreCase))
                {
                    var svg = new SvgImageSource();
                    if (await svg.SetSourceAsync(stream) == SvgImageSourceLoadStatus.Success) icon = svg;
                }
                else
                {
                    var bitmap = new BitmapImage { DecodePixelWidth = 128 };
                    await bitmap.SetSourceAsync(stream);
                    icon = bitmap;
                }
            }
            catch { icon = null; }
        }
        _previewIcons[key] = icon;
        return icon;
    }

    /// <summary>
    /// 行真正进入可视区才去读包内清单——ListView 虚拟化只会实例化屏内几行，
    /// 所以一页 30 条不会一次性对 30 个包发请求，滚动到哪条才补哪条。
    /// </summary>
    private void QueuePreview(PluginPackage package, RowView row)
    {
        if (!_previewStarted.Add(PackageKey(package))) return;
        row.Card.Loaded += async (_, _) =>
        {
            try { await LoadPreviewAsync(package, row); }
            catch (OperationCanceledException) { }
            catch (Exception) { /* 预览只是列表装饰，取不到就保持包 ID + 字形 */ }
        };
    }

    /// <summary>单个包：后台读包内清单，回到 UI 线程回填标题/元信息/图标。</summary>
    private async Task LoadPreviewAsync(PluginPackage package, RowView row)
    {
        if (_lifetime?.Token is not { } ct) return;
        await _previewGate.WaitAsync(ct);
        PackagePreview? preview;
        try { preview = await GetPreviewAsync(package, ct); }
        finally { _previewGate.Release(); }
        if (ct.IsCancellationRequested || preview is null) return;
        if (!ReferenceEquals(_availableRows.GetValueOrDefault(PackageKey(package)), row)) return; // 已重新渲染
        ApplyPreviewText(package, row, preview);
        await ApplyPreviewIconAsync(package, row, preview);
    }

    private static async Task<PackagePreview?> GetPreviewAsync(PluginPackage package, CancellationToken ct)
    {
        var key = PackageKey(package);
        lock (PreviewGate)
        {
            if (PreviewCache.TryGetValue(key, out var cached)) return cached;
        }
        var preview = await NuGetPackagePreview.ReadAsync(package.Id, package.Version, ct);
        lock (PreviewGate) PreviewCache[key] = preview; // 失败也记住，翻页时不重复重试同一个坏包
        return preview;
    }

    private void RefreshInstalled()
    {
        try
        {
            InstalledList.Items.Clear();
            var installed = PluginManager.GetInstalled();
            var added = new List<Border>();
            foreach (var p in installed)
            {
                PluginManifest? metadata = null;
                string? iconPath = null;
                try { metadata = PluginManager.GetMetadata(p); iconPath = PluginManager.GetIconPath(p); } catch { /* Keep broken plugins manageable. */ }
                var toggle = new Button { Content = p.Enabled ? L("Disable", "停用") : L("Enable", "启用"), Style = (Style)Resources["PluginQuietButton"] };
                var remove = new Button { Content = new FontIcon { Glyph = "\uE712", FontSize = 14 }, Style = (Style)Resources["PluginIconButton"] };
                var menu = new MenuFlyout();
                var uninstall = new MenuFlyoutItem { Text = L("Uninstall", "卸载"), Icon = new FontIcon { Glyph = "\uE74D" } };
                var nuget = new MenuFlyoutItem { Text = L("NuGetPage", "NuGet 页面"), Icon = new FontIcon { Glyph = "\uE774" } };
                menu.Items.Add(uninstall);
                menu.Items.Add(nuget);
                remove.Flyout = menu;
                toggle.Click += async (_, _) => await ChangeAsync(p.Id, !p.Enabled);
                uninstall.Click += async (_, _) => await ChangeAsync(p.Id, null);
                nuget.Click += async (_, _) =>
                {
                    try { await Windows.System.Launcher.LaunchUriAsync(NuGetPluginSource.PackagePage(p.Id, p.Version)); }
                    catch (Exception ex) { Notice(ex.Message, true); }
                };

                var state = p.Enabled ? L("Enabled", "已启用") : L("Disabled", "已停用");
                if (PluginManager.IsLoaded(p.Id)) state += " · " + L("Loaded", "本次会话已加载");
                if (PluginManager.GetError(p.Id) is { } error) state += "\n" + error;
                // 安全模式下插件被整体跳过，逐条写「已启用但本次未加载」才是真实状态，
                // 否则用户会以为插件是坏的。
                var warning = false;
                if (p.Enabled && PluginSafeMode.IsActive)
                {
                    state += "\n" + L("SkippedBySafeMode", "安全模式下本次未加载，关闭安全模式并重启后恢复。");
                    warning = true;
                }
                var failures = DescribeFailures(p.Id);
                if (failures.Length > 0)
                {
                    state += failures;
                    warning = true;
                }
                if (PluginManager.GetError(p.Id) is { Length: > 0 }) warning = true;

                var meta = string.Join("  ·  ", new[] { p.Id, metadata?.Author }.Where(s => !string.IsNullOrWhiteSpace(s)));
                var title = string.IsNullOrWhiteSpace(metadata?.Name) ? p.Id : metadata.Name;
                var icon = iconPath is not null ? BuiltinIconService.LoadAsset(iconPath) : null;
                var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
                actions.Children.Add(toggle);
                actions.Children.Add(remove);
                var package = new PluginPackage(p.Id, p.Version, metadata?.Description ?? "", metadata?.Author ?? "");
                var card = Row(title, meta, metadata?.Description ?? "", icon, actions,
                    p.Enabled ? L("Enabled", "已启用") : L("Disabled", "已停用"), state, warning).Card;
                card.Tag = package;
                InstalledList.Items.Add(card);
                added.Add(card);
            }

            InstalledCount.Text = installed.Count.ToString(CultureInfo.CurrentCulture);
            InstalledEmpty.Visibility = installed.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            InstalledEmptyText.Text = L("NoInstalled", "尚未安装插件。");
            InstalledList.IsHitTestVisible = installed.Count > 0;
            UpdateGridMetrics();
            AnimateCardsIn(added, 0);
            if (PluginManager.GetError("storage") is { } storageError) Notice(storageError, true);
            UpdateHero();
        }
        catch (Exception ex) { Notice(ex.Message, true); }
    }

    /// <summary>累计失败次数只在真有记录时才追加，正常插件不显示任何噪声。</summary>
    private static string DescribeFailures(string id)
    {
        if (PluginFailureStore.Get(id) is not { } record) return "";
        var parts = new List<string>();
        if (record.LoadFailures > 0) parts.Add(string.Format(L("FailureLoad", "加载失败 {0} 次"), record.LoadFailures));
        if (record.ExecutionFailures > 0) parts.Add(string.Format(L("FailureExecution", "运行报错 {0} 次"), record.ExecutionFailures));
        if (parts.Count == 0) return "";
        var text = "\n" + string.Join(" · ", parts);
        if (!string.IsNullOrWhiteSpace(record.LastError)) text += "：" + record.LastError;
        return text;
    }

    /// <summary>安全模式状态同步：不再占用整条横幅，状态与说明收进按钮文案和 Tooltip。</summary>
    private void ApplySafeModeState()
    {
        var active = PluginSafeMode.IsActive;
        var hint = !active
            ? L("SafeModeOffHint", "插件与主程序同权限运行。若怀疑某个插件导致启动异常或界面卡死，可以跳过全部插件启动，逐个排查。")
            : PluginSafeMode.EnteredAutomatically
                ? L("SafeModeOnAuto", "最近几次启动都没能正常完成，本次已自动跳过全部插件。若工具箱现在正常，问题大概率来自插件；点按钮可恢复插件并重启。")
                : L("SafeModeOnManual", "本次启动已跳过全部已安装插件。排查完成后关闭安全模式并重启即可恢复。");
        SafeModeButton.Content = active ? L("SafeModeExit", "关闭安全模式并重启") : L("SafeModeRestart", "以安全模式重启");
        ToolTipService.SetToolTip(SafeModeButton, hint);
        SafeModeButton.IsEnabled = true;
    }

    private async void SafeMode_Click(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        SafeModeButton.IsEnabled = false;
        try
        {
            if (PluginSafeMode.IsActive) PluginSafeMode.DisableForNextLaunch();
            else PluginSafeMode.SetForNextLaunch(true);
            if (App.TryRestart()) return;
            Notice(L("SafeModeRestartFailed", "设置已保存，但自动重启失败，请手动重开工具箱。"), true);
        }
        catch (Exception ex) { Notice(ex.Message, true); }
        finally { SafeModeButton.IsEnabled = true; }
    }

    private void DiscoverTab_Click(object sender, RoutedEventArgs e) => SetTab(true, animate: true);

    private void InstalledTab_Click(object sender, RoutedEventArgs e) => SetTab(false, animate: true);

    private void SetTab(bool discover, bool animate)
    {
        var changed = _discover != discover || AvailablePane.Visibility != (discover ? Visibility.Visible : Visibility.Collapsed);
        _discover = discover;
        var from = discover ? InstalledPane : AvailablePane;
        var to = discover ? AvailablePane : InstalledPane;
        if (changed)
        {
            if (animate) AnimatePaneSwitch(from, to);
            else
            {
                AvailablePane.Visibility = discover ? Visibility.Visible : Visibility.Collapsed;
                InstalledPane.Visibility = discover ? Visibility.Collapsed : Visibility.Visible;
            }
        }
        DiscoverTab.Foreground = ResourceBrush(discover ? "TextFillColorPrimaryBrush" : "TextFillColorSecondaryBrush");
        InstalledTab.Foreground = ResourceBrush(discover ? "TextFillColorSecondaryBrush" : "TextFillColorPrimaryBrush");
        PositionNavUnderline(animate);
        UpdateHero();
        if (!discover) RefreshInstalled();
    }

    private async Task InstallAsync(PluginPackage package, InstalledPlugin? installed)
    {
        if (_busy) return;
        SetBusy(true);
        try
        {
            await PluginManager.InstallAsync(package, _lifetime!.Token);
            RefreshInstalled();
            RenderAvailable(0);
            if (PluginManager.GetError(package.Id) is { } error) Notice(error, true);
            else Notice(L("InstalledNotice", "安装完成。首次安装立即加载；更新请重启应用后使用新版本。"));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Notice(ex.Message, true); }
        finally { SetBusy(false); }
    }

    private async Task ChangeAsync(string id, bool? enabled)
    {
        if (_busy) return;
        SetBusy(true);
        try
        {
            await PluginManager.SetEnabledAsync(id, enabled);
            RefreshInstalled();
            RenderAvailable(0);
            if (PluginManager.GetError(id) is { } error) Notice(error, true);
            else if (enabled is null) Notice(L("UninstalledNotice", "已卸载，插件工具已从列表移除；重启应用后清理文件。"));
            else if (enabled == true) Notice(L("EnabledNotice", "已启用，插件工具已恢复。"));
            else Notice(L("DisabledNotice", "已停用，插件工具已从列表移除；后台代码将在重启后完全停止。"));
        }
        catch (Exception ex) { Notice(ex.Message, true); }
        finally { SetBusy(false); }
    }

    private async void Search_Submitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
    {
        _page = 0;
        await SearchAsync();
    }

    private void SearchBox_TextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
    {
        UpdateHero();
        UpdateSectionHeaders();
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        _page = 0;
        await SearchAsync();
    }

    private async void LoadMore_Click(object sender, RoutedEventArgs e) => await SearchAsync(append: true);

    private async void AvailableList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not Border { Tag: PluginPackage package }) return;
        var installed = PluginManager.GetInstalled().FirstOrDefault(p => p.Id.Equals(package.Id, StringComparison.OrdinalIgnoreCase));
        var row = _availableRows.GetValueOrDefault(PackageKey(package));
        await OpenDetailAsync(package, installed, row?.Icon);
    }

    private async void InstalledList_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not Border { Tag: PluginPackage package }) return;
        var installed = PluginManager.GetInstalled().FirstOrDefault(p => p.Id.Equals(package.Id, StringComparison.OrdinalIgnoreCase));
        await OpenDetailAsync(package, installed, null);
    }

    private void UpdateSectionHeaders()
    {
        if (AvailableSectionTitle is null) return;
        AvailableSectionTitle.Text = string.IsNullOrWhiteSpace(SearchBox.Text)
            ? L("AllPlugins", "全部插件")
            : L("SearchResults", "搜索结果");
        AvailableCount.Text = string.Format(L("CountFormat", "{0} 个"), _packages.Count);
        PageInfo.Text = string.Format(L("ShownFormat", "已显示 {0} 个"), _packages.Count);
        LoadMoreButton.Visibility = _hasMore ? Visibility.Visible : Visibility.Collapsed;
        LoadMoreButton.IsEnabled = !_busy && _hasMore;
    }

    private void UpdateEmptyState()
    {
        if (AvailableEmpty is null) return;
        var available = AvailableList.Items.Count > 0;
        AvailableEmpty.Visibility = !available && !_busy ? Visibility.Visible : Visibility.Collapsed;
        AvailableList.IsHitTestVisible = available;
        if (!available && AvailableEmptyHint.Text.Length == 0)
            AvailableEmptyHint.Text = L("Empty", "未找到插件。插件需要在 NuGet 上标记 Tubatools，且已完成索引。");
        InstalledEmpty.Visibility = InstalledList.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        InstalledList.IsHitTestVisible = InstalledList.Items.Count > 0;
    }

    private void UpdateHero()
    {
        if (HeroCard is null) return;
        var visible = _discover && string.IsNullOrWhiteSpace(SearchBox.Text);
        HeroCard.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (!visible)
        {
            StopHeroFloat();
            return;
        }

        _heroTiles.Clear();
        var installed = PluginManager.GetInstalled().Count;
        HeroPluginCount.Text = string.Format(L("HeroPluginCount", "共 {0} 个插件"), _packages.Count);
        HeroInstalledCount.Text = string.Format(L("HeroInstalledCount", "已安装 {0} 个"), installed);

        var tiles = new[] { HeroTile0, HeroTile1, HeroTile2 };
        var sample = _packages.Take(3).ToList();
        for (int i = 0; i < tiles.Length; i++)
        {
            var package = i < sample.Count ? sample[i] : null;
            SetHeroTile(tiles[i], package);
        }
        StartHeroFloat();
    }

    private void SetHeroTile(Border tile, PluginPackage? package)
    {
        if (tile.Child is not Grid content) return;
        while (content.Children.Count > 1) content.Children.RemoveAt(1);
        if (content.Children[0] is FontIcon glyph) glyph.Visibility = Visibility.Visible;
        if (package is null) return;
        _heroTiles[PackageKey(package)] = tile;
        var icon = _previewIcons.GetValueOrDefault(PackageKey(package)) ?? PackageIcon(package.IconUrl);
        if (icon is not null) ShowIcon(tile, icon, keepBackground: true);
    }

    private async Task OpenDetailAsync(PluginPackage package, InstalledPlugin? installed, Border? sourceIcon)
    {
        if (_detailOpen || _busy) return;
        _detailOpen = true;
        StopHeroFloat(); // 详情打开时暂停背景 hero 的循环漂浮，少一层持续动画
        _selectedPackage = package;
        _selectedInstalled = installed;
        _detailSourceIcon = sourceIcon;
        DetailStatus.IsOpen = false;
        DetailHost.Visibility = Visibility.Visible;
        DetailScroll.ChangeView(null, 0, null, true);
        if (sourceIcon is not null)
        {
            try { ConnectedAnimationService.GetForCurrentView().PrepareToAnimate("PluginIcon", sourceIcon); }
            catch { /* 连接动画是锦上添花，失败仍走详情页自身的淡入。 */ }
        }
        PopulateDetailHeader(package, installed);
        PopulateDetailFacts(package, installed, CachedDetails(package));
        ShowReadmeLoading();
        await AnimateDetailOpenAsync(sourceIcon is not null);
        DetailBackButton.Focus(FocusState.Programmatic);
        _ = EnsureDetailPreviewAsync(package);
        _ = LoadDetailDetailsAsync(package, force: false);
    }

    private void PopulateDetailHeader(PluginPackage package, InstalledPlugin? installed)
    {
        var preview = CachedPreview(package);
        var row = _availableRows.GetValueOrDefault(PackageKey(package));
        var title = row?.Title.Text is { Length: > 0 } rowTitle ? rowTitle
            : preview?.Name is { Length: > 0 } previewName ? previewName : package.Id;
        DetailTitle.Text = title;
        DetailBreadcrumb.Text = L("DetailBreadcrumb", "插件") + " / " + title;
        ApplyDetailHeroGradient(package);
        DetailAuthor.Text = string.IsNullOrWhiteSpace(package.Authors) ? L("UnknownAuthor", "未知") : package.Authors;
        DetailCategory.Text = L("Category", "图吧工具箱插件");
        DetailDownloads.Text = string.Format(L("DownloadsFormat", "下载 {0:N0} 次"), package.TotalDownloads);
        DetailVersion.Text = "v" + package.Version;
        DetailShortDescription.Text = package.Description;
        DetailProvideNote.Text = string.Format(L("ProvidedBy", "由 {0} 提供 · 安装位置与更新由 NuGet 管理"),
            string.IsNullOrWhiteSpace(package.Authors) ? L("UnknownAuthor", "未知") : package.Authors);

        var packagePage = NuGetPluginSource.PackagePage(package.Id, package.Version);
        DetailTopNuGet.NavigateUri = packagePage;
        DetailHeroNuGet.NavigateUri = packagePage;
        DetailHeroNuGet.Content = L("NuGetPage", "NuGet 页面");

        var icon = _previewIcons.GetValueOrDefault(PackageKey(package)) ?? PackageIcon(package.IconUrl);
        if (icon is not null) ShowIcon(DetailIconTile, icon, keepBackground: true);
        else ResetIconTile(DetailIconTile, keepBackground: true);
        UpdateDetailInstallState(package, installed);
    }

    /// <summary>
    /// Store 产品页的彩色 hero：浅色主题用强调色 + 白色调出低饱和 pastel，
    /// 深色主题用强调色 + 黑色压暗，保证两种主题下标题都有足够对比度。
    /// </summary>
    private void ApplyDetailHeroGradient(PluginPackage package)
    {
        var accent = AccentColor();
        var dark = ActualTheme == ElementTheme.Dark;
        var t = 0.54 + (StableHash(package.Id) % 9) * 0.02;
        var start = dark ? Mix(accent, Microsoft.UI.Colors.Black, 0.70) : Mix(accent, Microsoft.UI.Colors.White, t);
        var mid = dark ? Mix(accent, Microsoft.UI.Colors.Black, 0.80) : Mix(accent, Microsoft.UI.Colors.White, t - 0.10);
        var end = dark ? Mix(accent, Microsoft.UI.Colors.Black, 0.88) : Mix(accent, Microsoft.UI.Colors.White, t - 0.22);
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1)
        };
        brush.GradientStops.Add(new GradientStop { Color = start, Offset = 0 });
        brush.GradientStops.Add(new GradientStop { Color = mid, Offset = 0.48 });
        brush.GradientStops.Add(new GradientStop { Color = end, Offset = 1 });
        DetailHero.Background = brush;
    }

    private async Task EnsureDetailPreviewAsync(PluginPackage package)
    {
        var key = PackageKey(package);
        PackagePreview? preview;
        bool cached;
        lock (PreviewGate)
        {
            cached = PreviewCache.ContainsKey(key);
            preview = cached ? PreviewCache[key] : null;
        }
        try
        {
            if (!cached) preview = await GetPreviewAsync(package, _lifetime?.Token ?? CancellationToken.None);
            if (preview is null || !_detailOpen || _selectedPackage is null ||
                !PackageKey(_selectedPackage).Equals(key, StringComparison.OrdinalIgnoreCase)) return;
            if (!string.IsNullOrWhiteSpace(preview.Name))
            {
                DetailTitle.Text = preview.Name;
                DetailBreadcrumb.Text = L("DetailBreadcrumb", "插件") + " / " + preview.Name;
            }
            var icon = await PreviewIconAsync(package, preview);
            if (icon is not null && _detailOpen && _selectedPackage is not null &&
                PackageKey(_selectedPackage).Equals(key, StringComparison.OrdinalIgnoreCase))
                ShowIcon(DetailIconTile, icon, keepBackground: true);
        }
        catch (OperationCanceledException) { }
        catch { /* 预览失败保持包 ID 与字形。 */ }
    }

    private static Color AccentColor()
    {
        if (Application.Current.Resources.TryGetValue("SystemAccentColor", out var value) && value is Color color)
            return color;
        return Color.FromArgb(255, 0, 120, 212);
    }

    private static Color Mix(Color from, Color to, double amount)
    {
        amount = Math.Clamp(amount, 0, 1);
        return Color.FromArgb(255,
            (byte)Math.Round(from.R * (1 - amount) + to.R * amount),
            (byte)Math.Round(from.G * (1 - amount) + to.G * amount),
            (byte)Math.Round(from.B * (1 - amount) + to.B * amount));
    }

    private static int StableHash(string value)
    {
        unchecked
        {
            var hash = unchecked((int)2166136261);
            foreach (var c in value)
            {
                hash ^= c;
                hash *= 16777619;
            }
            return hash;
        }
    }

    private void UpdateDetailInstallState(PluginPackage package, InstalledPlugin? installed)    {
        var state = Describe(package, installed);
        DetailInstallText.Text = state.Label;
        DetailInstallIcon.Glyph = state.Enabled ? "\uE896" : "\uE73E";
        DetailInstallButton.IsEnabled = !_detailBusy && state.Enabled;
    }

    private void PopulateDetailFacts(PluginPackage package, InstalledPlugin? installed, PackageDetails? details)
    {
        DetailFacts.Children.Clear();
        AddFact(L("VersionLabel", "版本"), package.Version);
        AddFact(L("PackageIdLabel", "包 ID"), package.Id);
        AddFact(L("AuthorLabel", "作者"), string.IsNullOrWhiteSpace(package.Authors) ? L("UnknownAuthor", "未知") : package.Authors);
        AddFact(L("DownloadsLabel", "下载量"), package.TotalDownloads.ToString("N0", CultureInfo.CurrentCulture));
        if (installed is not null) AddFact(L("UpdateFrom", "当前版本"), installed.Version);
        var license = details?.License;
        if (!string.IsNullOrWhiteSpace(license)) AddFact(L("License", "许可证"), license);
        if (details?.Tags is { Length: > 0 } tags) AddFact(L("Tags", "标签"), tags.Replace(';', ' ').Trim());
    }

    private void AddFact(string label, string value)
    {
        var row = new Grid { ColumnSpacing = 16 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(146) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var labelText = new TextBlock
        {
            Text = label, FontSize = 12, Foreground = ResourceBrush("TextFillColorSecondaryBrush"),
            VerticalAlignment = VerticalAlignment.Top
        };
        var valueText = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(value) ? "—" : value, FontSize = 13,
            TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true
        };
        Grid.SetColumn(valueText, 1);
        row.Children.Add(labelText);
        row.Children.Add(valueText);
        DetailFacts.Children.Add(row);
    }

    private void ShowReadmeLoading()
    {
        ReadmeSkeleton.Visibility = Visibility.Visible;
        ReadmeView.Visibility = Visibility.Collapsed;
        ReadmeMessage.Visibility = Visibility.Collapsed;
        ReadmeView.Opacity = 1;
        StartSkeletonPulse();
    }

    private async Task LoadDetailDetailsAsync(PluginPackage package, bool force)
    {
        var key = PackageKey(package);
        PackageDetails? details;
        bool cached;
        lock (DetailsGate)
        {
            cached = DetailsCache.ContainsKey(key);
            details = cached ? DetailsCache[key] : null;
        }

        if (!cached || force)
        {
            if (force) lock (DetailsGate) DetailsCache.Remove(key);
            if (force) ShowReadmeLoading();
            try
            {
                details = await NuGetPackageDetails.ReadAsync(package.Id, package.Version, _lifetime?.Token ?? CancellationToken.None);
            }
            catch (OperationCanceledException) { return; }
            catch { details = null; }
            lock (DetailsGate) DetailsCache[key] = details;
        }

        if (!_detailOpen || _selectedPackage is null || !PackageKey(_selectedPackage).Equals(key, StringComparison.OrdinalIgnoreCase)) return;
        ApplyDetailDetails(package, details);
    }

    private void ApplyDetailDetails(PluginPackage package, PackageDetails? details)
    {
        if (!string.IsNullOrWhiteSpace(details?.Title) && DetailTitle.Text == package.Id)
        {
            DetailTitle.Text = details.Title;
            DetailBreadcrumb.Text = L("DetailBreadcrumb", "插件") + " / " + details.Title;
        }
        StopSkeletonPulse();
        PopulateDetailFacts(package, _selectedInstalled, details);
        BuildDetailLinks(details);

        if (details?.Readme is { Length: > 0 } readme)
        {
            ReadmeSkeleton.Visibility = Visibility.Collapsed;
            ReadmeMessage.Visibility = Visibility.Collapsed;
            ReadmeRetryButton.Visibility = Visibility.Collapsed;
            ReadmeFallback.Visibility = Visibility.Collapsed;
            try
            {
                ReadmeView.Markdown = readme;
                ReadmeView.Visibility = Visibility.Visible;
                AnimateReadmeReveal();
            }
            catch
            {
                ReadmeView.Visibility = Visibility.Collapsed;
                ReadmeMessage.Visibility = Visibility.Visible;
                ReadmeFallback.Visibility = Visibility.Collapsed;
                ReadmeMessageText.Text = L("ReadmeError", "无法显示这个 README。");
                ReadmeRetryButton.Visibility = Visibility.Visible;
            }
        }
        else
        {
            ReadmeSkeleton.Visibility = Visibility.Collapsed;
            ReadmeView.Visibility = Visibility.Collapsed;
            ReadmeMessage.Visibility = Visibility.Visible;
            ReadmeMessageText.Text = details is null
                ? L("ReadmeError", "无法从 NuGet 读取这个插件的说明。")
                : L("ReadmeMissing", "这个插件没有在包内附带 README。");
            ReadmeRetryButton.Visibility = details is null ? Visibility.Visible : Visibility.Collapsed;
            ReadmeFallback.Text = details is not null && !string.IsNullOrWhiteSpace(package.Description)
                ? package.Description
                : "";
            ReadmeFallback.Visibility = ReadmeFallback.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private void BuildDetailLinks(PackageDetails? details)
    {
        DetailLinks.Children.Clear();
        var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddLink(string label, string url, string glyph)
        {
            if (string.IsNullOrWhiteSpace(url) || !added.Add(url)) return;
            var content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
            var icon = new FontIcon { Glyph = glyph, FontSize = 15, VerticalAlignment = VerticalAlignment.Center };
            var text = new StackPanel { Spacing = 1 };
            text.Children.Add(new TextBlock { Text = label, FontSize = 13, FontWeight = FontWeights.SemiBold });
            text.Children.Add(new TextBlock
            {
                Text = DisplayHost(url), FontSize = 11, Foreground = ResourceBrush("TextFillColorTertiaryBrush"),
                TextTrimming = TextTrimming.CharacterEllipsis, MaxLines = 1
            });
            content.Children.Add(icon);
            content.Children.Add(text);
            var link = new HyperlinkButton
            {
                Content = content,
                NavigateUri = new Uri(url),
                Style = (Style)Resources["DetailLinkButton"]
            };
            ToolTipService.SetToolTip(link, url);
            DetailLinks.Children.Add(link);
        }

        if (details?.RepositoryUrl is { Length: > 0 } repo)
            AddLink(RepositoryLabel(repo), repo, "\uE943");
        if (details?.ProjectUrl is { Length: > 0 } project)
            AddLink(L("ProjectHome", "项目主页"), project, "\uE774");
        if (details?.LicenseUrl is { Length: > 0 } licenseUrl)
            AddLink(L("License", "许可证"), licenseUrl, "\uE8A5");

        if (DetailLinks.Children.Count == 0)
            DetailLinks.Children.Add(new TextBlock
            {
                Text = L("NoOpenSource", "这个插件没有声明开源地址。"), FontSize = 13,
                TextWrapping = TextWrapping.Wrap, Foreground = ResourceBrush("TextFillColorSecondaryBrush"),
                Margin = new Thickness(8, 6, 8, 6)
            });
    }

    private static string DisplayHost(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return url;
        var host = uri.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? uri.Host[4..] : uri.Host;
        return host + (uri.AbsolutePath == "/" ? "" : uri.AbsolutePath);
    }

    private static string RepositoryLabel(string url)
    {
        var host = Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host.ToLowerInvariant() : "";
        if (host.Contains("github")) return "GitHub";
        if (host.Contains("gitee")) return "Gitee";
        if (host.Contains("gitlab")) return "GitLab";
        return L("SourceCode", "源代码");
    }

    private void SetDetailBusy(bool value)
    {
        _detailBusy = value;
        DetailInstallRing.IsActive = value;
        DetailInstallButton.IsEnabled = !value;
        if (value)
        {
            DetailInstallText.Text = L("Installing", "正在安装…");
            DetailInstallIcon.Glyph = "\uE896";
        }
        else if (_selectedPackage is not null)
        {
            UpdateDetailInstallState(_selectedPackage, _selectedInstalled);
        }
    }

    private async void DetailInstall_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedPackage is null || _detailBusy || _busy) return;
        var package = _selectedPackage;
        SetDetailBusy(true);
        DetailStatus.IsOpen = false;
        try
        {
            await PluginManager.InstallAsync(package, _lifetime?.Token ?? CancellationToken.None);
            _selectedInstalled = PluginManager.GetInstalled().FirstOrDefault(p => p.Id.Equals(package.Id, StringComparison.OrdinalIgnoreCase));
            RefreshInstalled();
            RenderAvailable(0);
            PopulateDetailHeader(package, _selectedInstalled);
            if (CachedDetails(package) is { } details) PopulateDetailFacts(package, _selectedInstalled, details);
            if (PluginManager.GetError(package.Id) is { } error)
            {
                DetailStatus.Severity = InfoBarSeverity.Error;
                DetailStatus.Message = error;
            }
            else
            {
                DetailStatus.Severity = InfoBarSeverity.Success;
                DetailStatus.Message = L("InstalledNotice", "安装完成。首次安装立即加载；更新请重启应用后使用新版本。");
                AnimateInstallSuccess();
            }
            DetailStatus.IsOpen = true;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            DetailStatus.Severity = InfoBarSeverity.Error;
            DetailStatus.Message = ex.Message;
            DetailStatus.IsOpen = true;
        }
        finally { SetDetailBusy(false); }
    }

    private async void DetailBack_Click(object sender, RoutedEventArgs e) => await CloseDetailAsync();

    private async void DetailSurface_KeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (!_detailOpen || e.Key != Windows.System.VirtualKey.Escape) return;
        e.Handled = true;
        await CloseDetailAsync();
    }

    private async void ReadmeRetry_Click(object sender, RoutedEventArgs e)
    {
        if (_selectedPackage is null) return;
        ShowReadmeLoading();
        await LoadDetailDetailsAsync(_selectedPackage, force: true);
    }

    private async Task CloseDetailAsync()
    {
        if (!_detailOpen) return;
        await AnimateDetailCloseAsync();
        DetailHost.Visibility = Visibility.Collapsed;
        _detailOpen = false;
        _selectedPackage = null;
        _selectedInstalled = null;
        _detailSourceIcon = null;
        try { ConnectedAnimationService.GetForCurrentView().GetAnimation("PluginIcon"); } catch { }
        if (_discover && HeroCard.Visibility == Visibility.Visible) StartHeroFloat();
        AvailableList.Focus(FocusState.Programmatic);
    }

    private async void Docs_Click(object sender, RoutedEventArgs e) => await ShowDocsAsync();

    private void Develop_Click(object sender, RoutedEventArgs e) => App.MainWindow?.NavigateToToolPage(typeof(AiAssistantPage), "plugin");

    private async void Upload_Click(object sender, RoutedEventArgs e)
    {
        try { await Windows.System.Launcher.LaunchUriAsync(new Uri(NuGetPluginSource.UploadUrl)); await ShowDocsAsync(); }
        catch (Exception ex) { Notice(ex.Message, true); }
    }

    private async Task ShowDocsAsync()
    {
        try
        {
            var text = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "Metadata", "插件开发文档.md"));
            var dialog = new TubaWinUi3.Controls.FluentContentDialog
            {
                XamlRoot = XamlRoot, Title = L("DocsTitle", "插件开发文档"), CloseButtonText = L("Close", "关闭"),
                Content = new ScrollViewer { MaxHeight = 520, Content = new TubaWinUi3.Controls.MarkdownView { Markdown = text } }
            };
            await dialog.ShowAsync();
        }
        catch (Exception ex) { Notice(ex.Message, true); }
    }

    internal static Brush ResourceBrush(string key)
    {
        if (Application.Current.Resources.TryGetValue(key, out var value) && value is Brush brush) return brush;
        return new SolidColorBrush(Microsoft.UI.Colors.Transparent);
    }

    /// <summary>NuGet 版本比较：数字段逐段比较，正式版高于预发布版。</summary>
    private static int CompareVersions(string left, string right)
    {
        var (leftNumbers, leftPrerelease) = SplitVersion(left);
        var (rightNumbers, rightPrerelease) = SplitVersion(right);
        int count = Math.Max(leftNumbers.Length, rightNumbers.Length);
        for (int i = 0; i < count; i++)
        {
            int a = i < leftNumbers.Length ? leftNumbers[i] : 0;
            int b = i < rightNumbers.Length ? rightNumbers[i] : 0;
            if (a != b) return a.CompareTo(b);
        }
        if (leftPrerelease.Length == 0 && rightPrerelease.Length > 0) return 1;
        if (leftPrerelease.Length > 0 && rightPrerelease.Length == 0) return -1;
        return string.Compare(leftPrerelease, rightPrerelease, StringComparison.OrdinalIgnoreCase);
    }

    private static (int[] Numbers, string Prerelease) SplitVersion(string version)
    {
        var core = version.Split('+')[0];
        int dash = core.IndexOf('-');
        var numbers = dash >= 0 ? core[..dash] : core;
        var prerelease = dash >= 0 ? core[(dash + 1)..] : "";
        var parts = numbers.Split('.', StringSplitOptions.RemoveEmptyEntries)
            .Select(p => int.TryParse(p, out var n) ? n : 0).ToArray();
        return (parts, prerelease);
    }
}

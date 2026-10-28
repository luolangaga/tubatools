using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using TubaWinUi3.Models;
using TubaWinUi3.Services;

namespace TubaWinUi3.Pages;

public sealed partial class DashboardPage : Page, ILocalizablePage
{
    private List<HomeDashboardCard> _cards = [];
    private List<HomeDashboardCard> _features = [];
    private readonly System.Collections.ObjectModel.ObservableCollection<ToolItem> _tools = [];
    private IReadOnlyList<ToolItem>? _catalog;
    private IReadOnlyList<ToolItem> _favoriteCatalog = [];
    private readonly Dictionary<string, ImageSource> _logos = [];
    private CancellationTokenSource? _iconCts;
    private bool _ready, _updatingFilter, _favorites, _loading, _dialogOpen;
    private int _requestVersion;
    private sealed record ToolChoice(string Target, string Name, string Description, string Glyph);
    private static string S(string key, string fallback) => LocalizationService.L("Dashboard_" + key, fallback);

    public DashboardPage()
    {
        InitializeComponent();
        ToolsGrid.ItemsSource = _tools;
        ToolsGrid.Loaded += (_, _) => UpdateCardLayout();
        _ready = true;
        ActualThemeChanged += (_, _) => DispatcherQueue.TryEnqueue(RenderCards);
        StatusBar.Closed += (_, _) => StatusBar.Visibility = Visibility.Collapsed;
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _cards = HomeDashboardContent.LoadFeatured(AppSettings.Get(HomeDashboardContent.SettingsKey),
            AppSettings.Get(HomeDashboardContent.PreviousFeaturedSettingsKey) ?? AppSettings.Get(HomeDashboardContent.LegacyContentSettingsKey) ?? AppSettings.Get(HomeDashboardContent.LegacySettingsKey));
        if (AppSettings.Get(HomeDashboardContent.SettingsKey) is null)
            AppSettings.Set(HomeDashboardContent.SettingsKey, System.Text.Json.JsonSerializer.Serialize(_cards));
        _requestVersion++;
        _favorites = HomeDashboardContent.ResolveTab(e.Parameter as string, AppSettings.Get(HomeDashboardContent.SelectedTabSettingsKey)) == "favorites";
        _loading = false;
        AppSettings.Set(HomeDashboardContent.SelectedTabSettingsKey, _favorites ? "favorites" : "all");
        _updatingFilter = true; FilterBar.SelectedItem = _favorites ? FavoritesFilter : AllToolsFilter; _updatingFilter = false;
        ApplyLocalization();
        _ = LoadCatalogAsync();
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    { _requestVersion++; _iconCts?.Cancel(); base.OnNavigatedFrom(e); }

    public void ApplyLocalization()
    {
        GreetingText.Text = DateTime.Now.Hour switch
        {
            < 6 => S("GreetingNight", "夜深了，欢迎回来"), < 12 => S("GreetingMorning", "上午好，欢迎回来"),
            < 18 => S("GreetingAfternoon", "下午好，欢迎回来"), _ => S("GreetingEvening", "晚上好，欢迎回来")
        };
        AllToolsFilter.Text = S("AllTools", "全部工具"); FavoritesFilter.Text = S("Favorites", "收藏");
        ManageButton.Content = S("EditFeatured", "编辑大卡片");
        ToolSearchBox.PlaceholderText = S("SearchCatalog", "搜索工具");
        RenderCards();
        ApplyToolFilter();
    }

    private ToolChoice Describe(string target) => target switch
    {
        "hardware" => new(target, S("HardwareTitle", "硬件信息"), S("HardwareDesc", "检查你的电脑的配置"), "\uE977"),
        "qq" => new(target, S("QqTitle", "QQ 交流群"), S("QqDesc", "485079194 · 和图吧小伙伴一起交流折腾心得"), "\uE902"),
        "builtin:quick-device-check" => new(target, S("CheckTitle", "快速验机"), S("CheckDesc", "新电脑到手，快速排查问题"), "\uE9D5"),
        "builtin:space-cleanup" => new(target, S("CleanupTitle", "垃圾清理"), S("CleanupDesc", "通过多种方式，快速清理电脑空间"), "\uEDA8"),
        "builtin:game-monitor" => new(target, S("GameTitle", "游戏监控"), S("GameDesc", "检测游戏帧率等数据，随时掌握游戏表现"), "\uE9F5"),
        "builtin:winget-store" => new(target, S("StoreTitle", "正版应用商店"), S("StoreDesc", "怕下到盗版应用？这里能下到你能想到的任何软件"), "\uE719"),
        "builtin:context-menu-mgr" => new(target, S("ContextMenuTitle", "右键菜单管理"), S("ContextMenuDesc", "整理右键菜单，管理常用菜单项"), "\uE8FD"),
        "builtin:startup-manager" => new(target, S("StartupTitle", "开机自启动管理"), S("StartupDesc", "管理开机启动项目，让电脑启动更清爽"), "\uE7E8"),
        "builtin:battery-analyzer" => new(target, S("BatteryTitle", "电池消耗分析"), S("BatteryDesc", "分析电池消耗趋势，查看应用耗电排行"), "\uE8BE"),
        "builtin:stress-test" => new(target, S("StressTitle", "一键三烤"), S("StressDesc", "同时测试 CPU、GPU 和网卡，监控温度与功耗"), "\uE9D9"),
        _ => DescribeOther(target)
    };

    private ToolChoice DescribeOther(string target)
    {
        if (target.StartsWith("builtin:"))
        {
            var tool = BuiltinToolRegistry.AllTools.FirstOrDefault(t => t.Id == target[8..]);
            if (tool is not null) return new(target, tool.Name, tool.Description, tool.Glyph);
        }
        var item = _catalog?.FirstOrDefault(t => "tool:" + t.RelativePath == target);
        return new(target, item?.Name ?? Path.GetFileName(target[(target.StartsWith("builtin:") ? 8 : 5)..]),
            item?.Description ?? S("CatalogTool", "打开工具目录中的常用工具"), "\uE90F");
    }

    private bool Available(HomeDashboardCard card) => HomeDashboardContent.IsVisible(card, RuntimeHelper.IsPackagedContext) &&
        (!card.Target.StartsWith("builtin:") || ToolVisibilityService.IsBuiltinEnabled(card.Target[8..]));

    private void RenderCards()
    {
        if (!_ready) return;
        _iconCts?.Cancel(); _iconCts?.Dispose(); _iconCts = new();
        FeaturesPanel.Children.Clear();
        var visible = _cards.Where(Available).ToList();
        _features = visible.Where(c => c.Featured == true).ToList();
        for (int i = 0; i < _features.Count; i++)
        {
            var card = _features[i]; var choice = Describe(card.Target);
            var tile = new Controls.DashboardGalleryTile { Title = card.Title ?? choice.Name,
                Description = card.Description ?? choice.Description, Source = CreateLogo(card, 36, _iconCts.Token) };
            AutomationProperties.SetAutomationId(tile.ActionButton, "DashboardTile" + i);
            AutomationProperties.SetName(tile.ActionButton, tile.Title + ", " + tile.Description);
            ToolTipService.SetToolTip(tile, tile.Description);
            tile.ActionButton.ContextFlyout = ContentMenu(card);
            tile.Click += async (_, _) => await OpenCardAsync(card);
            FeaturesPanel.Children.Add(tile);
        }
        UpdateCardLayout();
    }

    private MenuFlyout ContentMenu(HomeDashboardCard card)
    {
        var menu = new MenuFlyout(); var item = new MenuFlyoutItem { Text = S("ManageContent", "编辑内容") };
        item.Click += async (_, _) => await ManageContentAsync(card); menu.Items.Add(item); return menu;
    }

    private void FilterBar_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (!_ready || _updatingFilter || sender.SelectedItem is null) return;
        _favorites = sender.SelectedItem?.Tag as string == "favorites";
        AppSettings.Set(HomeDashboardContent.SelectedTabSettingsKey, _favorites ? "favorites" : "all");
        ApplyToolFilter();
    }

    private async Task LoadCatalogAsync()
    {
        int version = ++_requestVersion;
        try
        {
            _loading = true; ApplyToolFilter();
            var catalog = await ToolCatalog.GetAllToolsAsync();
            var favoriteCatalog = await Task.Run(() => ToolCatalog.GetCategories().SelectMany(ToolCatalog.GetTools).ToList());
            if (version != _requestVersion) return;
            _catalog = catalog; _favoriteCatalog = favoriteCatalog; _loading = false; ApplyToolFilter();
            _ = ToolIconService.LoadIconsAsync(_catalog.ToList(), DispatcherQueue, _iconCts!.Token);
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { _loading = false; ApplyToolFilter(); ShowStatus(S("LoadFailed", "工具加载失败"), ex.Message, InfoBarSeverity.Error); }
    }

    private async Task OpenCardAsync(HomeDashboardCard card)
    {
        try
        {
            if (!Available(card)) return;
            if (card.Target == "hardware") { App.MainWindow?.NavigateToToolPage(typeof(HardwarePage)); return; }
            if (card.Target == "qq")
            {
                // 统一走 ClipboardService（占用重试 + 失败只留痕）：这里曾经直连 WinRT，
                // 剪贴板被别的程序占用时抛出的 COMException 会顶掉整次卡片点击。
                if (ClipboardService.TrySetText("485079194").Success)
                    ShowStatus(S("Copied", "群号已复制"), S("CopiedDesc", "在 QQ 中搜索群号 485079194，即可申请加入。"), InfoBarSeverity.Success);
                else
                    ShowStatus(S("CopyFailed", "复制失败"), S("CopyFailedDesc", "剪贴板正被其它程序占用，请稍后再试，或手动搜索群号 485079194。"), InfoBarSeverity.Error);
                return;
            }
            if (card.Target.StartsWith("builtin:"))
            {
                var tool = BuiltinToolRegistry.GetById(card.Target[8..]) ?? throw new InvalidOperationException(S("Unavailable", "此工具暂不可用。"));
                MainWindow.ActiveToolName = tool.Name; await tool.ExecuteAsync(new() { XamlRoot = XamlRoot });
                LaunchHistoryService.RecordLaunch(ToolCatalog.GetBuiltinFavoriteKey(tool)); return;
            }
            _catalog ??= await ToolCatalog.GetAllToolsAsync();
            var item = _catalog.FirstOrDefault(t => "tool:" + t.RelativePath == card.Target)
                ?? throw new InvalidOperationException(S("Unavailable", "此工具暂不可用。"));
            App.MainWindow?.LaunchCatalogTool(item);
        }
        catch (Exception ex) { ShowStatus(S("OpenFailed", "打开失败"), ex.Message, InfoBarSeverity.Error); }
    }

    private void ShowStatus(string title, string message, InfoBarSeverity severity)
    { StatusBar.Title = title; StatusBar.Message = message; StatusBar.Severity = severity; StatusBar.Visibility = Visibility.Visible; StatusBar.IsOpen = true; }

    private FrameworkElement CreateLogo(HomeDashboardCard card, double size, CancellationToken token)
    {
        var id = card.Target switch
        {
            "hardware" => "hardware", "qq" => "qq", "builtin:quick-device-check" => "quick-device-check",
            "builtin:space-cleanup" => "space-cleanup", "builtin:game-monitor" => "game-monitor",
            "builtin:winget-store" => "winget-store", _ => null
        };
        if (!_logos.TryGetValue(card.Target, out var source))
        {
            source = id is not null ? new SvgImageSource(new Uri($"ms-appx:///Assets/HomeTiles/{id}.svg")) :
                card.Target.StartsWith("builtin:") ? BuiltinIconService.Get(card.Target[8..]) : null;
            if (source is not null) _logos[card.Target] = source;
        }
        var image = new Image { Source = source, Width = size, Height = size, Stretch = Stretch.Uniform };
        if (source is not null) return image;
        var fallback = new FontIcon { Glyph = Describe(card.Target).Glyph, FontSize = size, Foreground = AccentBrushSource.Foreground };
        var host = new Grid { Width = size, Height = size }; host.Children.Add(fallback); host.Children.Add(image);
        if (card.Target.StartsWith("tool:")) _ = LoadLogoAsync(card, image, fallback, token);
        return host;
    }

    private async Task LoadLogoAsync(HomeDashboardCard card, Image image, FontIcon fallback, CancellationToken token)
    {
        try
        {
            _catalog ??= await ToolCatalog.GetAllToolsAsync(); token.ThrowIfCancellationRequested();
            var tool = _catalog.FirstOrDefault(t => "tool:" + t.RelativePath == card.Target); if (tool is null) return;
            ImageSource? source = null;
            try
            {
                var file = await Windows.Storage.StorageFile.GetFileFromPathAsync(tool.EffectivePath);
                using var thumbnail = await file.GetThumbnailAsync(Windows.Storage.FileProperties.ThumbnailMode.SingleItem, 256);
                if (thumbnail is not null) { var bitmap = new BitmapImage(); await bitmap.SetSourceAsync(thumbnail); source = bitmap; }
            }
            catch (Exception ex) when (ex is not OperationCanceledException) { System.Diagnostics.Debug.WriteLine(ex.Message); }
            if (source is null)
            {
                var path = tool.IconPath ?? await ToolIconService.ExtractIconToCacheAsync(tool.EffectivePath, token);
                if (path is not null) source = new BitmapImage(new Uri(path));
            }
            token.ThrowIfCancellationRequested(); if (source is null) return;
            _logos[card.Target] = source; image.Source = source; fallback.Visibility = Visibility.Collapsed;
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"[Dashboard logo] {ex.Message}"); }
    }
}

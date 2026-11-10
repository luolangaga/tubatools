using System.Globalization;
using Microsoft.UI.Text;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using TubaWinUi3.Services;
using TubaWinUi3.Services.Plugins;

namespace TubaWinUi3.Pages;

public sealed partial class PluginsPage : Page
{
    private CancellationTokenSource? _lifetime;
    private IReadOnlyList<PluginPackage> _packages = [];
    private readonly Dictionary<string, ImageSource?> _packageIcons = new(StringComparer.OrdinalIgnoreCase);
    private bool _busy;
    private int _page;
    private int _count;
    private bool _showDocs;
    private static string L(string key, string fallback) => LocalizationService.L("Plugins_" + key, fallback);
    public PluginsPage() { InitializeComponent(); }
    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _showDocs = e.Parameter is "docs";
    }
    private async void Page_Loaded(object sender, RoutedEventArgs e)
    {
        _lifetime = new CancellationTokenSource();
        RefreshInstalled();
        if (_showDocs) { _showDocs = false; await ShowDocsAsync(); }
        await SearchAsync();
    }
    private void Page_Unloaded(object sender, RoutedEventArgs e) { _lifetime?.Cancel(); }
    private void SetBusy(bool value)
    {
        _busy = value;
        Busy.IsActive = value;
        AvailableList.IsEnabled = InstalledList.IsEnabled = SearchBox.IsEnabled = !value;
        PreviousButton.IsEnabled = !value && _page > 0;
        NextButton.IsEnabled = !value && _count == 30;
    }
    private void Notice(string message, bool error = false)
    {
        Status.Message = message;
        Status.Severity = error ? InfoBarSeverity.Error : InfoBarSeverity.Informational;
        Status.IsOpen = true;
    }
    private async Task SearchAsync()
    {
        if (_busy || _lifetime is null || _lifetime.IsCancellationRequested) return;
        SetBusy(true);
        Status.IsOpen = false;
        try
        {
            var result = await NuGetPluginSource.SearchAsync(SearchBox.Text, _page, _lifetime.Token);
            _packages = result.Packages;
            _count = result.HasMore ? 30 : 0;
            RenderAvailable();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Notice(ex.Message, true); }
        finally { SetBusy(false); }
    }
    private void RenderAvailable()
    {
        var installedPackages = PluginManager.GetInstalled();
        AvailableList.Items.Clear();
        foreach (var package in _packages)
        {
            var installed = installedPackages.FirstOrDefault(p => p.Id.Equals(package.Id, StringComparison.OrdinalIgnoreCase));
            var state = Describe(package, installed);
            var button = new Button { Content = state.Label, IsEnabled = state.Enabled };
            if (state.Accent) button.Style = (Style)Resources["PluginInstallButton"];
            button.Click += async (_, _) => await InstallAsync(package, installed);
            AvailableList.Items.Add(Row(package.Id, "v" + package.Version, AvailableMeta(package, installed), package.Description, PackageIcon(package.IconUrl), [button]));
        }
        if (_packages.Count == 0) Notice(L("Empty", "未找到插件。插件需要在 NuGet 上标记 Tubatools，且已完成索引。"));
    }
    private static (string Label, bool Enabled, bool Accent) Describe(PluginPackage package, InstalledPlugin? installed)
    {
        if (installed is null) return (L("InstallAction", "安装"), true, true);
        return CompareVersions(package.Version, installed.Version) > 0
            ? (string.Format(L("UpdateTo", "更新到 {0}"), package.Version), true, true)
            : (L("UpToDate", "已安装 · 最新"), false, false);
    }
    private static string AvailableMeta(PluginPackage package, InstalledPlugin? installed)
    {
        var parts = new List<string>();
        if (installed is not null) parts.Add(string.Format(L("InstalledVersion", "已安装 {0}"), installed.Version));
        if (!string.IsNullOrWhiteSpace(package.Authors)) parts.Add(string.Format(L("AuthorFormat", "作者：{0}"), package.Authors));
        parts.Add(string.Format(L("DownloadsFormat", "下载 {0:N0} 次"), package.TotalDownloads));
        return string.Join("  ·  ", parts);
    }
    private Border Row(string title, string version, string meta, string description, ImageSource? icon, IReadOnlyList<Button> buttons)
    {
        var grid = new Grid { ColumnSpacing = 14 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var tile = IconTile(icon);
        grid.Children.Add(tile);

        var text = new StackPanel { Spacing = 5 };
        var head = new Grid { ColumnSpacing = 8 };
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        head.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        head.Children.Add(new TextBlock { Text = title, FontSize = 16, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center });
        var chipText = new TextBlock { Text = version, FontSize = 11, Opacity = 0.8, VerticalAlignment = VerticalAlignment.Center };
        var chip = new Border { Style = (Style)Resources["PluginVersionChip"], Child = chipText };
        Grid.SetColumn(chip, 1);
        head.Children.Add(chip);
        text.Children.Add(head);
        if (!string.IsNullOrWhiteSpace(meta))
            text.Children.Add(new TextBlock { Text = meta, FontSize = 12, Opacity = 0.72, TextWrapping = TextWrapping.Wrap });
        if (!string.IsNullOrWhiteSpace(description))
            text.Children.Add(new TextBlock { Text = description, FontSize = 13, Opacity = 0.72, TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.CharacterEllipsis });
        Grid.SetColumn(text, 1);
        grid.Children.Add(text);

        if (buttons.Count > 0)
        {
            var actions = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, VerticalAlignment = VerticalAlignment.Center };
            foreach (var button in buttons) actions.Children.Add(button);
            Grid.SetColumn(actions, 2);
            grid.Children.Add(actions);
        }

        var card = new Border { Style = (Style)Resources["PluginRowCard"] };
        card.Child = grid;
        return card;
    }

    /// <summary>包图标缺失/加载失败时回退到插件字形，不影响列表布局。</summary>
    private Border IconTile(ImageSource? icon, double size = 40)
    {
        var tile = new Border { Style = (Style)Resources["PluginIconTile"], Width = size, Height = size };
        var content = new Grid();
        var glyph = new FontIcon { Glyph = "\uE774", FontSize = size * 0.5, Style = (Style)Resources["PluginIconGlyph"] };
        if (icon is null)
        {
            content.Children.Add(glyph);
        }
        else
        {
            glyph.Visibility = Visibility.Collapsed;
            content.Children.Add(glyph);
            var image = new Image { Source = icon, Stretch = Stretch.Uniform, Margin = new Thickness(4) };
            image.ImageFailed += (_, _) =>
            {
                image.Visibility = Visibility.Collapsed;
                glyph.Visibility = Visibility.Visible;
            };
            content.Children.Add(image);
        }
        tile.Child = content;
        return tile;
    }

    private ImageSource? PackageIcon(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            return null;
        if (_packageIcons.TryGetValue(url, out var cached)) return cached;
        ImageSource? image = null;
        try { image = new BitmapImage { UriSource = uri, DecodePixelWidth = 96 }; }
        catch { }
        _packageIcons[url] = image;
        return image;
    }
    private void RefreshInstalled()
    {
        try
        {
            InstalledList.Items.Clear();
            foreach (var p in PluginManager.GetInstalled())
            {
                PluginManifest? metadata = null;
                string? iconPath = null;
                try { metadata = PluginManager.GetMetadata(p); iconPath = PluginManager.GetIconPath(p); } catch { /* Keep broken plugins manageable. */ }
                var toggle = new Button { Content = p.Enabled ? L("Disable", "停用") : L("Enable", "启用") };
                var remove = new Button { Content = L("Uninstall", "卸载") };
                toggle.Click += async (_, _) => await ChangeAsync(p.Id, !p.Enabled);
                remove.Click += async (_, _) => await ChangeAsync(p.Id, null);
                var state = p.Enabled ? L("Enabled", "已启用") : L("Disabled", "已停用");
                if (PluginManager.IsLoaded(p.Id)) state += " · " + L("Loaded", "本次会话已加载");
                if (PluginManager.GetError(p.Id) is { } error) state += "\n" + error;
                var meta = string.Join("  ·  ", new[] { p.Id, metadata?.Author, state }.Where(s => !string.IsNullOrWhiteSpace(s)));
                var buttons = new List<Button> { toggle, remove };
                var nuget = new Button { Content = L("NuGetPage", "NuGet 页面") };
                nuget.Click += async (_, _) =>
                {
                    try { await Windows.System.Launcher.LaunchUriAsync(NuGetPluginSource.PackagePage(p.Id, p.Version)); }
                    catch (Exception ex) { Notice(ex.Message, true); }
                };
                buttons.Add(nuget);
                var title = string.IsNullOrWhiteSpace(metadata?.Name) ? p.Id : metadata.Name;
                var icon = iconPath is not null ? BuiltinIconService.LoadAsset(iconPath) : null;
                InstalledList.Items.Add(Row(title, "v" + p.Version, meta, metadata?.Description ?? "", icon, buttons));
            }
            if (InstalledList.Items.Count == 0)
                InstalledList.Items.Add(new TextBlock { Text = L("NoInstalled", "尚未安装插件。"), Opacity = 0.72, Margin = new Thickness(8, 16, 8, 8) });
            if (PluginManager.GetError("storage") is { } storageError) Notice(storageError, true);
        }
        catch (Exception ex) { Notice(ex.Message, true); }
    }
    private async Task InstallAsync(PluginPackage package, InstalledPlugin? installed)
    {
        if (_busy) return;
        var updating = installed is not null;
        var dialog = new TubaWinUi3.Controls.FluentContentDialog
        {
            XamlRoot = XamlRoot,
            Title = updating ? L("UpdateTitle", "更新插件") : L("InstallTitle", "安装插件"),
            PrimaryButtonText = updating ? L("UpdateAction", "更新") : L("InstallAction", "安装"),
            CloseButtonText = L("Cancel", "取消"),
            DefaultButton = ContentDialogButton.Close,
            Content = new ScrollViewer { MaxHeight = 460, Content = PackageDetails(package, installed) }
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;
        SetBusy(true);
        try
        {
            await PluginManager.InstallAsync(package, _lifetime!.Token);
            RefreshInstalled();
            RenderAvailable();
            if (PluginManager.GetError(package.Id) is { } error) Notice(error, true);
            else Notice(L("InstalledNotice", "安装完成。首次安装立即加载；更新请重启应用后使用新版本。"));
        }
        catch (OperationCanceledException) { }
        catch (Exception ex) { Notice(ex.Message, true); }
        finally { SetBusy(false); }
    }
    private FrameworkElement PackageDetails(PluginPackage package, InstalledPlugin? installed)
    {
        var stack = new StackPanel { Spacing = 14 };
        var heading = new Grid { ColumnSpacing = 12 };
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        heading.Children.Add(IconTile(PackageIcon(package.IconUrl), 48));
        var headingText = new TextBlock { Text = package.Id, FontSize = 18, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(headingText, 1);
        heading.Children.Add(headingText);
        stack.Children.Add(heading);
        stack.Children.Add(new InfoBar
        {
            IsOpen = true,
            IsClosable = false,
            Severity = InfoBarSeverity.Warning,
            Title = L("CommunityTitle", "由社区提供"),
            Message = L("Trust", "插件会以工具箱的权限执行代码。请仅安装你信任的作者发布的插件。")
        });

        var details = new Grid { ColumnSpacing = 16, RowSpacing = 8 };
        details.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        details.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        void AddDetail(string label, string value)
        {
            int row = details.RowDefinitions.Count;
            details.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            var name = new TextBlock { Text = label, Opacity = 0.72, FontSize = 13 };
            var content = new TextBlock { Text = value, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, FontSize = 13 };
            Grid.SetRow(name, row); Grid.SetRow(content, row);
            Grid.SetColumn(content, 1);
            details.Children.Add(name);
            details.Children.Add(content);
        }
        AddDetail(L("VersionLabel", "版本"), package.Version);
        if (installed is not null) AddDetail(L("UpdateFrom", "当前版本"), installed.Version);
        AddDetail(L("AuthorLabel", "作者"), string.IsNullOrWhiteSpace(package.Authors) ? L("UnknownAuthor", "未知") : package.Authors);
        AddDetail(L("DownloadsLabel", "下载量"), package.TotalDownloads.ToString("N0", CultureInfo.CurrentCulture));
        stack.Children.Add(details);

        if (!string.IsNullOrWhiteSpace(package.Description))
            stack.Children.Add(new TextBlock { Text = package.Description, TextWrapping = TextWrapping.Wrap, IsTextSelectionEnabled = true, Opacity = 0.8, FontSize = 13 });
        var nuget = new HyperlinkButton
        {
            Content = L("NuGetPage", "NuGet 页面"),
            NavigateUri = NuGetPluginSource.PackagePage(package.Id, package.Version),
            Padding = new Thickness(0)
        };
        stack.Children.Add(nuget);
        return stack;
    }
    private async Task ChangeAsync(string id, bool? enabled)
    {
        if (_busy) return;
        SetBusy(true);
        try
        {
            await PluginManager.SetEnabledAsync(id, enabled);
            RefreshInstalled();
            RenderAvailable();
            if (PluginManager.GetError(id) is { } error) Notice(error, true);
            else if (enabled is null) Notice(L("UninstalledNotice", "已卸载，插件工具已从列表移除；重启应用后清理文件。"));
            else if (enabled == true) Notice(L("EnabledNotice", "已启用，插件工具已恢复。"));
            else Notice(L("DisabledNotice", "已停用，插件工具已从列表移除；后台代码将在重启后完全停止。"));
        }
        catch (Exception ex) { Notice(ex.Message, true); }
        finally { SetBusy(false); }
    }
    private async void Search_Submitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args) { _page = 0; await SearchAsync(); }
    private async void Previous_Click(object sender, RoutedEventArgs e) { _page--; await SearchAsync(); }
    private async void Next_Click(object sender, RoutedEventArgs e) { _page++; await SearchAsync(); }
    private void Tabs_Changed(object sender, SelectionChangedEventArgs e) { if (InstalledList is not null) { RefreshInstalled(); RenderAvailable(); } }
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

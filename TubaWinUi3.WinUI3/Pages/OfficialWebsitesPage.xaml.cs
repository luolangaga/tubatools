using System.Collections.Concurrent;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using TubaWinUi3.Services;

namespace TubaWinUi3.Pages;

public sealed partial class OfficialWebsitesPage : Page
{
    private readonly IReadOnlyList<OfficialWebsiteCategory> _categories = OfficialWebsiteCatalog.GetCategories();
    private readonly List<Button> _categoryButtons = [];
    private OfficialWebsiteCategory? _currentCategory;

    public OfficialWebsitesPage()
    {
        InitializeComponent();

        PopulateNav();
    }

    private void PopulateNav()
    {
        NavPanel.Children.Clear();

        foreach (var category in _categories)
            NavPanel.Children.Add(CreateCategoryButton(category));

        if (_categories.Count > 0)
            SelectCategory(_categories[0], null);
    }

    private Button CreateCategoryButton(OfficialWebsiteCategory category)
    {
        var grid = new Grid { ColumnSpacing = 10 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var icon = new FontIcon
        {
            Glyph = category.Glyph,
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center
        };

        var name = new TextBlock
        {
            Text = category.Name,
            FontSize = 13,
            VerticalAlignment = VerticalAlignment.Center,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        grid.Children.Add(icon);
        Grid.SetColumn(icon, 0);
        grid.Children.Add(name);
        Grid.SetColumn(name, 1);

        var btn = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Left,
            Padding = new Thickness(10, 8, 12, 8),
            Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            Tag = category.Name,
            Style = Application.Current.Resources["SubtleButtonStyle"] as Style,
            CornerRadius = new CornerRadius(6),
            Content = grid
        };

        btn.Click += CategoryButton_Click;
        _categoryButtons.Add(btn);
        return btn;
    }

    private void CategoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string categoryName)
        {
            var category = _categories.FirstOrDefault(c => c.Name == categoryName);
            if (category is not null)
                SelectCategory(category, btn);
        }
    }

    private void SelectCategory(OfficialWebsiteCategory category, Button? btn)
    {
        _currentCategory = category;
        CurrentCategoryText.Text = category.Name;
        UpdateSelectionVisual(btn);
        RefreshSiteList();
    }

    private void UpdateSelectionVisual(Button? selectedBtn)
    {
        foreach (var btn in _categoryButtons)
            btn.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);

        if (selectedBtn is not null)
            selectedBtn.Background = Application.Current.Resources["SubtleFillColorSecondaryBrush"] as SolidColorBrush
                ?? new SolidColorBrush(Microsoft.UI.Colors.LightGray);
    }

    private void RefreshSiteList()
    {
        SiteList.Items.Clear();

        var query = SearchBox.Text?.Trim() ?? "";
        IReadOnlyList<OfficialWebsite> sites;
        string label;

        if (query.Length > 0)
        {
            var matches = _categories
                .SelectMany(c => c.Sites.Select(s => new { Site = s, CategoryName = c.Name }))
                .Where(x => x.Site.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                            || x.Site.Url.Contains(query, StringComparison.OrdinalIgnoreCase))
                .ToList();

            sites = matches
                .Select(x => new OfficialWebsite($"{x.Site.Name} · {x.CategoryName}", x.Site.Url, x.Site.Description))
                .ToList();
            label = $"{matches.Count} 个结果";
        }
        else if (_currentCategory is not null)
        {
            sites = _currentCategory.Sites;
            label = $"{sites.Count} 个网站";
        }
        else
        {
            SiteCountText.Text = "";
            EmptyState.Visibility = Visibility.Visible;
            return;
        }

        foreach (var site in sites)
            SiteList.Items.Add(CreateSiteItem(site));

        SiteCountText.Text = label;
        EmptyState.Visibility = sites.Count > 0 ? Visibility.Collapsed : Visibility.Visible;
    }

    private ListViewItem CreateSiteItem(OfficialWebsite site)
    {
        var grid = new Grid { Padding = new Thickness(12, 10, 12, 10), ColumnSpacing = 12 };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var iconBorder = new Border
        {
            Width = 40,
            Height = 40,
            Background = (Brush)Application.Current.Resources["SubtleFillColorSecondaryBrush"],
            CornerRadius = new CornerRadius(8),
            Child = CreateFaviconLayer(site.FaviconUrl)
        };

        var name = new TextBlock
        {
            Text = site.Name,
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.Bold,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        var url = new TextBlock
        {
            Text = site.Url,
            FontSize = 11,
            Opacity = 0.6,
            TextTrimming = TextTrimming.CharacterEllipsis
        };

        var textStack = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        textStack.Children.Add(name);
        textStack.Children.Add(url);

        var copyBtn = new Button
        {
            Content = "复制链接",
            Tag = site,
            VerticalAlignment = VerticalAlignment.Center,
            Style = Application.Current.Resources["SubtleButtonStyle"] as Style
        };
        copyBtn.Click += CopyButton_Click;

        grid.Children.Add(iconBorder);
        Grid.SetColumn(iconBorder, 0);
        grid.Children.Add(textStack);
        Grid.SetColumn(textStack, 1);
        grid.Children.Add(copyBtn);
        Grid.SetColumn(copyBtn, 2);

        var item = new ListViewItem
        {
            Content = grid,
            Tag = site,
            Padding = new Thickness(0, 0, 0, 0),
            HorizontalContentAlignment = HorizontalAlignment.Stretch
        };
        item.Tapped += (_, e) =>
        {
            if (e.OriginalSource is not ButtonBase)
                OpenInBrowser(site.Url);
        };
        return item;
    }

    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(8) };
    private static readonly ConcurrentDictionary<string, Task<string?>> _faviconCache = new();

    private static Grid CreateFaviconLayer(string faviconUrl)
    {
        var backdrop = new FontIcon
        {
            Glyph = "\uE774",
            FontSize = 18,
            Opacity = 0.55,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };
        var image = new Image
        {
            Width = 22,
            Height = 22,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center
        };

        var layer = new Grid();
        layer.Children.Add(backdrop);
        layer.Children.Add(image);

        if (!string.IsNullOrEmpty(faviconUrl))
            _ = LoadFaviconAsync(layer, backdrop, image, faviconUrl);

        return layer;
    }

    private static async Task LoadFaviconAsync(Grid layer, FontIcon backdrop, Image image, string faviconUrl)
    {
        try
        {
            var host = new Uri(faviconUrl).Host;
            var path = await GetCachedFaviconPathAsync(host);
            if (path is null)
                return;

            var bitmap = new BitmapImage(new Uri(path));
            bitmap.ImageFailed += (s, e) =>
            {
                try { File.Delete(path); } catch { }
            };
            image.Source = bitmap;
            backdrop.Visibility = Visibility.Collapsed;
        }
        catch { }
    }

    private static Task<string?> GetCachedFaviconPathAsync(string host)
    {
        var task = _faviconCache.GetOrAdd(host, static h => DownloadFaviconAsync(h));
        return GetCachedFaviconPathCoreAsync(host, task);
    }

    private static async Task<string?> GetCachedFaviconPathCoreAsync(string host, Task<string?> task)
    {
        var path = await task;
        if (path is null)
            _faviconCache.TryRemove(new KeyValuePair<string, Task<string?>>(host, task));
        return path;
    }

    private static async Task<string?> DownloadFaviconAsync(string host)
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TubaWinUi3", "FaviconCache");
        var path = Path.Combine(dir, host + ".ico");

        try
        {
            Directory.CreateDirectory(dir);
            if (File.Exists(path) && new FileInfo(path).Length > 0)
                return path;
        }
        catch
        {
            return null;
        }

        byte[]? bytes = null;
        foreach (var url in new[]
                 {
                     $"https://{host}/favicon.ico",
                     $"https://www.google.com/s2/favicons?domain={host}&sz=64",
                     $"https://favicon.im/{host}"
                 })
        {
            try
            {
                using var response = await _httpClient
                    .GetAsync(url, HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode) continue;
                var mediaType = response.Content.Headers.ContentType?.MediaType ?? "";
                if (!mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) continue;
                bytes = await response.Content.ReadAsByteArrayAsync().ConfigureAwait(false);
                if (bytes.Length > 0) break;
            }
            catch { }
        }

        if (bytes is null) return null;

        try
        {
            var tmp = path + ".tmp";
            await File.WriteAllBytesAsync(tmp, bytes).ConfigureAwait(false);
            File.Move(tmp, path, overwrite: true);
            return path;
        }
        catch
        {
            return null;
        }
    }

    private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        RefreshSiteList();
    }

    private void CopyButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is OfficialWebsite site)
        {
            // 失败重试与诊断由 ClipboardService 统一负责（剪贴板被占用时不得崩溃）
            ClipboardService.TrySetText(site.Url);
        }
    }

    private static void OpenInBrowser(string url)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch { }
    }
}

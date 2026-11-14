using System;
using System.Threading.Tasks;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using TubaWinUi3.PluginSdk;

namespace Tubatools.CapybaraLulu;

/// <summary>
/// 美化包唯一的外部入口：设置页「工具和系统集成」分组里的一张按钮卡片。
/// 插件刻意不注册内置工具，所以「全部工具」、内置工具索引和搜索里都不会出现它。
/// </summary>
internal static class CapybaraSettingsEntry
{
    private const string EntryAutomationId = "CapybaraLuluSettingsEntry";
    private static IPluginHostAccess? _host;
    private static Frame? _frame;
    private static NavigatedEventHandler? _navigated;
    private static WeakReference<Button>? _button;
    private static Grid? _overlay;
    private static Page? _overlayPage;
    private static CapybaraPage.View? _overlayView;

    public static void Attach(IPluginHostAccess host)
    {
        if (_host == host && _frame is not null)
        {
            TryAttachToCurrentPage();
            return;
        }
        Detach();
        _host = host;
        if (host.NavigationFrame is Frame frame)
        {
            _frame = frame;
            _navigated = OnNavigated;
            frame.Navigated += _navigated;
        }
        TryAttachToCurrentPage();
    }

    public static void Detach()
    {
        if (_frame is not null && _navigated is not null) _frame.Navigated -= _navigated;
        _frame = null;
        _navigated = null;
        _host = null;
        CloseOverlay();
        if (_button?.TryGetTarget(out var button) == true && button.Parent is Panel panel)
            panel.Children.Remove(button);
        _button = null;
    }

    private static void OnNavigated(object sender, NavigationEventArgs e)
    {
        if (e.Content is Page page) HookPage(page);
    }

    private static void TryAttachToCurrentPage()
    {
        if (_host?.CurrentPage is Page page) HookPage(page);
    }

    private static void HookPage(Page page)
    {
        if (!string.Equals(page.GetType().Name, "SettingsPage", StringComparison.Ordinal)) return;
        if (page.IsLoaded) Inject(page);
        else page.Loaded += OnSettingsLoaded;
    }

    private static void OnSettingsLoaded(object sender, RoutedEventArgs e)
    {
        var page = (Page)sender;
        page.Loaded -= OnSettingsLoaded;
        Inject(page);
    }

    private static void Inject(Page page)
    {
        var section = page.FindName("IntegrationSection") as StackPanel
            ?? FindByName<StackPanel>(page, "IntegrationSection");
        if (section is null) return;
        foreach (var child in section.Children)
        {
            if (child is FrameworkElement element &&
                AutomationProperties.GetAutomationId(element) == EntryAutomationId)
                return;
        }

        var button = CreateEntryButton(page);
        section.Children.Add(button);
        _button = new WeakReference<Button>(button);
    }

    private static Button CreateEntryButton(Page page)
    {
        var icon = new Image
        {
            Source = CapybaraAssets.Load("icon.png"),
            Width = 28,
            Height = 28,
            Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var title = new TextBlock
        {
            Text = "水豚噜噜美化包",
            FontSize = 14,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
        };
        var description = new TextBlock
        {
            Text = "动态背景、侧栏页脚、角落陪伴与硬件页卡片美化",
            FontSize = 11.5,
            Opacity = 0.62,
            TextWrapping = TextWrapping.Wrap,
        };
        var text = new StackPanel { Spacing = 2, VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(title);
        text.Children.Add(description);

        var content = new Grid { ColumnSpacing = 12 };
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        content.Children.Add(icon);
        Grid.SetColumn(text, 1);
        content.Children.Add(text);

        var button = new Button
        {
            Content = content,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Padding = new Thickness(16, 12, 16, 12),
        };
        AutomationProperties.SetName(button, "水豚噜噜美化包");
        AutomationProperties.SetAutomationId(button, EntryAutomationId);
        button.Click += (_, _) =>
        {
            try { ShowOverlay(page); }
            catch (Exception ex)
            {
                CloseOverlay();
                _ = ShowErrorAsync(page, ex);
            }
        };
        return button;
    }

    private static async Task ShowErrorAsync(Page page, Exception ex)
    {
        try
        {
            var dialog = new ContentDialog
            {
                Title = "打不开美化包设置",
                Content = ex.Message,
                CloseButtonText = "知道了",
                XamlRoot = page.XamlRoot,
            };
            await dialog.ShowAsync();
        }
        catch { }
    }

    private static void ShowOverlay(Page page)
    {
        if (_overlay is not null || page.Content is not Grid host) return;
        var view = new CapybaraPage.View();
        var dark = host.ActualTheme == ElementTheme.Dark;

        var title = new TextBlock
        {
            Text = "水豚噜噜美化包",
            FontSize = 20,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center,
        };
        var subtitle = new TextBlock
        {
            Text = "只在本机生效；关掉总开关就会原样还原。",
            FontSize = 12,
            Opacity = 0.62,
            Margin = new Thickness(0, 3, 0, 0),
        };
        var heading = new StackPanel { Spacing = 0, VerticalAlignment = VerticalAlignment.Center };
        heading.Children.Add(title);
        heading.Children.Add(subtitle);

        var close = new Button
        {
            Content = "关闭",
            Padding = new Thickness(14, 6, 14, 6),
            VerticalAlignment = VerticalAlignment.Center,
        };
        close.Click += (_, _) => CloseOverlay();

        var header = new Grid { ColumnSpacing = 16, Padding = new Thickness(22, 16, 14, 12) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(heading);
        Grid.SetColumn(close, 1);
        header.Children.Add(close);

        var body = new Grid { Margin = new Thickness(22, 4, 18, 18) };
        body.Children.Add(view.Root);

        var cardRoot = new Grid();
        cardRoot.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        cardRoot.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        cardRoot.Children.Add(header);
        Grid.SetRow(body, 1);
        cardRoot.Children.Add(body);

        var card = new Border
        {
            CornerRadius = new CornerRadius(16),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Width = Math.Min(980, Math.Max(560, host.ActualWidth - 96)),
            MaxHeight = Math.Max(360, host.ActualHeight - 80),
            Background = new SolidColorBrush(dark
                ? Windows.UI.Color.FromArgb(255, 36, 34, 31)
                : Windows.UI.Color.FromArgb(255, 253, 250, 245)),
            BorderBrush = new SolidColorBrush(dark
                ? Windows.UI.Color.FromArgb(60, 255, 255, 255)
                : Windows.UI.Color.FromArgb(45, 90, 70, 45)),
            Child = cardRoot,
        };

        var overlay = new Grid
        {
            Background = new SolidColorBrush(Windows.UI.Color.FromArgb(150, 0, 0, 0)),
            IsHitTestVisible = true,
        };
        overlay.Children.Add(card);
        host.Children.Add(overlay);

        _overlay = overlay;
        _overlayPage = page;
        _overlayView = view;
        page.Unloaded += OnPageUnloaded;
    }

    private static void OnPageUnloaded(object sender, RoutedEventArgs e)
    {
        if (sender is Page page) page.Unloaded -= OnPageUnloaded;
        CloseOverlay();
    }

    private static void CloseOverlay()
    {
        var overlay = _overlay;
        if (overlay is null) return;
        if (!overlay.DispatcherQueue.HasThreadAccess)
        {
            overlay.DispatcherQueue.TryEnqueue(CloseOverlay);
            return;
        }
        if (_overlayPage is not null) _overlayPage.Unloaded -= OnPageUnloaded;
        if (overlay.Parent is Panel parent) parent.Children.Remove(overlay);
        _overlay = null;
        _overlayPage = null;
        _overlayView?.Dispose();
        _overlayView = null;
    }

    private static T? FindByName<T>(DependencyObject root, string name) where T : FrameworkElement
    {
        if (root is T match && match.Name == name) return match;
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            if (FindByName<T>(VisualTreeHelper.GetChild(root, i), name) is { } child) return child;
        }
        return null;
    }
}

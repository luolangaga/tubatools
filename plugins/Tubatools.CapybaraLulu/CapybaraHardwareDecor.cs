using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using Windows.UI;
using TubaWinUi3.PluginSdk;

namespace Tubatools.CapybaraLulu;

/// <summary>
/// 硬件信息页的重点装饰：直接改现有卡片的底色、描边和圆角，并把噜噜做成卡片里的水印。
/// 不新增卡片、不改宿主布局；关掉开关或卸载时逐项还原。
/// </summary>
internal static class CapybaraHardwareDecor
{
    private const string WatermarkAutomationId = "CapybaraLuluHwWatermark";
    private static Frame? _frame;
    private static NavigatedEventHandler? _navigated;
    private static IPluginHostAccess? _host;
    private static Action? _themeChanged;
    private static Page? _decoratedPage;
    private static readonly List<CardState> States = [];

    private sealed record CardState
    {
        public required Border Card { get; init; }
        public Brush? Background { get; init; }
        public Brush? BorderBrush { get; init; }
        public Thickness BorderThickness { get; init; }
        public CornerRadius CornerRadius { get; init; }
        public UIElement? OriginalChild { get; init; }
        public Grid? Wrapper { get; init; }
        public FrameworkElement? Watermark { get; init; }
        public bool IsDetails { get; init; }
    }

    public static void Attach(IPluginHostAccess host, CapybaraSettings settings)
    {
        Detach();
        if (!settings.HardwareDecor) return;
        _host = host;
        if (host.NavigationFrame is Frame frame)
        {
            _frame = frame;
            _navigated = OnNavigated;
            frame.Navigated += _navigated;
        }
        _themeChanged = () => ApplyPalette();
        host.ThemeChanged += _themeChanged;
        if (host.CurrentPage is Page page) Hook(page);
    }

    public static void Detach()
    {
        if (_frame is not null && _navigated is not null) _frame.Navigated -= _navigated;
        _frame = null;
        _navigated = null;
        if (_host is not null && _themeChanged is not null) _host.ThemeChanged -= _themeChanged;
        _host = null;
        _themeChanged = null;
        RestoreCards();
    }

    private static void RestoreCards()
    {
        foreach (var state in States)
        {
            state.Card.Background = state.Background;
            state.Card.BorderBrush = state.BorderBrush;
            state.Card.BorderThickness = state.BorderThickness;
            state.Card.CornerRadius = state.CornerRadius;
            if (state.Wrapper is not null && state.OriginalChild is not null)
            {
                state.Card.Child = state.OriginalChild;
            }
            else if (state.Watermark?.Parent is Panel panel)
            {
                panel.Children.Remove(state.Watermark);
            }
        }
        States.Clear();
        _decoratedPage = null;
    }

    private static void OnNavigated(object sender, NavigationEventArgs e)
    {
        if (e.Content is Page page) Hook(page);
    }

    private static void Hook(Page page)
    {
        if (!string.Equals(page.GetType().Name, "HardwarePage", StringComparison.Ordinal)) return;
        if (page.IsLoaded) Inject(page);
        else page.Loaded += OnPageLoaded;
    }

    private static void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        var page = (Page)sender;
        page.Loaded -= OnPageLoaded;
        Inject(page);
    }

    private static void Inject(Page page)
    {
        if (States.Count > 0)
        {
            if (_decoratedPage == page) return; // 同一个页面已经装饰过
            RestoreCards();
        }
        _decoratedPage = page;

        foreach (var name in new[] { "Card1", "Card2", "Card3" })
        {
            if (page.FindName(name) is Border card) DecorateMetricCard(page, card);
        }

        if (page.FindName("DetailsPanel") is StackPanel detailsPanel)
        {
            var detailsCard = FindDetailsCard(detailsPanel);
            if (detailsCard is not null) DecorateDetailsCard(page, detailsCard);
        }
    }

    private static Border? FindDetailsCard(StackPanel panel)
    {
        foreach (var child in panel.Children)
        {
            if (child is Border border && FindByName<ItemsRepeater>(border, "DetailsRepeater") is not null)
                return border;
        }
        return null;
    }

    private static void DecorateMetricCard(Page page, Border card)
    {
        var state = Capture(card, isDetails: false);
        var watermark = CreateWatermark("lulu-head.png", 74, 0.16);
        if (card.Child is Grid grid)
        {
            grid.Children.Insert(0, watermark);
        }
        else if (card.Child is Panel panel)
        {
            panel.Children.Insert(0, watermark);
        }
        else
        {
            watermark = null;
        }
        States.Add(state with { Watermark = watermark });
        ApplyCardPalette(state, page.ActualTheme);
    }

    private static void DecorateDetailsCard(Page page, Border card)
    {
        var original = card.Child;
        CardState state;
        if (original is null)
        {
            state = Capture(card, isDetails: true);
        }
        else
        {
            try
            {
                card.Child = null;
                var wrapper = new Grid();
                var watermark = CreateWatermark("lulu-wrench.png", 150, 0.11);
                wrapper.Children.Add(watermark);
                wrapper.Children.Add(original);
                card.Child = wrapper;
                state = Capture(card, isDetails: true) with
                {
                    OriginalChild = original,
                    Wrapper = wrapper,
                    Watermark = watermark,
                };
            }
            catch
            {
                try { card.Child = original; } catch { }
                state = Capture(card, isDetails: true);
            }
        }
        States.Add(state);
        ApplyCardPalette(state, page.ActualTheme);
    }

    private static CardState Capture(Border card, bool isDetails) => new()
    {
        Card = card,
        Background = card.Background,
        BorderBrush = card.BorderBrush,
        BorderThickness = card.BorderThickness,
        CornerRadius = card.CornerRadius,
        IsDetails = isDetails,
    };

    private static FrameworkElement CreateWatermark(string file, double size, double opacity)
    {
        var image = new Image
        {
            Source = CapybaraAssets.Load(file),
            Width = size,
            Stretch = Stretch.Uniform,
            Opacity = opacity,
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 10, 6),
        };
        AutomationProperties.SetAutomationId(image, WatermarkAutomationId);
        return image;
    }

    private static void ApplyPalette()
    {
        if (States.Count == 0) return;
        var page = _host?.CurrentPage as Page;
        var theme = page?.ActualTheme ?? ElementTheme.Light;
        foreach (var state in States) ApplyCardPalette(state, theme);
    }

    private static void ApplyCardPalette(CardState state, ElementTheme theme)
    {
        var dark = theme == ElementTheme.Dark;
        var card = state.Card;
        card.CornerRadius = state.IsDetails ? new CornerRadius(14) : new CornerRadius(12);
        card.BorderThickness = new Thickness(1);
        card.BorderBrush = new SolidColorBrush(dark
            ? Color.FromArgb(150, 201, 149, 91)
            : Color.FromArgb(145, 233, 185, 120));
        card.Background = new LinearGradientBrush
        {
            StartPoint = new Windows.Foundation.Point(0, 0),
            EndPoint = new Windows.Foundation.Point(1, 1),
            GradientStops =
            {
                new GradientStop
                {
                    Offset = 0,
                    Color = dark
                        ? Color.FromArgb((byte)(state.IsDetails ? 240 : 228), 58, 46, 34)
                        : Color.FromArgb((byte)(state.IsDetails ? 250 : 240), 255, 253, 246),
                },
                new GradientStop
                {
                    Offset = 1,
                    Color = dark
                        ? Color.FromArgb((byte)(state.IsDetails ? 235 : 220), 46, 36, 27)
                        : Color.FromArgb((byte)(state.IsDetails ? 245 : 232), 255, 240, 220),
                },
            },
        };
    }

    private static T? FindByName<T>(DependencyObject root, string name) where T : FrameworkElement
    {
        if (root is T typed && typed.Name == name) return typed;
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (var i = 0; i < count; i++)
        {
            var found = FindByName<T>(VisualTreeHelper.GetChild(root, i), name);
            if (found is not null) return found;
        }
        return null;
    }
}

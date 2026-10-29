using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace TubaWinUi3.Controls;

/// <summary>Small, decorative vector icons, with a native glyph in contrast themes.</summary>
public sealed class FluentColorIcon : UserControl
{
    private readonly Image _image = new() { Stretch = Stretch.Uniform };
    private readonly FontIcon _glyph = new() { FontFamily = new FontFamily("Segoe Fluent Icons") };
    private string? _asset;

    public FluentColorIcon()
    {
        var grid = new Grid();
        grid.Children.Add(_glyph);
        grid.Children.Add(_image);
        Content = grid;
        IsTabStop = false;
        Microsoft.UI.Xaml.Automation.AutomationProperties.SetAccessibilityView(this,
            Microsoft.UI.Xaml.Automation.Peers.AccessibilityView.Raw);
        Loaded += (_, _) => Refresh();
        ActualThemeChanged += (_, _) => Refresh();
        RegisterPropertyChangedCallback(FontSizeProperty, (_, _) => Refresh());
        _image.ImageFailed += (_, _) => { _image.Visibility = Visibility.Collapsed; _glyph.Visibility = Visibility.Visible; };
    }

    public static readonly DependencyProperty GlyphProperty = DependencyProperty.Register(
        nameof(Glyph), typeof(string), typeof(FluentColorIcon), new PropertyMetadata("", Changed));
    public string Glyph { get => (string)GetValue(GlyphProperty); set => SetValue(GlyphProperty, value); }
    private static void Changed(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        => ((FluentColorIcon)sender).Refresh();

    private void Refresh()
    {
        _glyph.Glyph = Glyph;
        _glyph.FontSize = FontSize;
        _image.Width = _image.Height = FontSize;
        var asset = Services.FluentIconCatalog.AssetFor(Glyph);
        var color = asset is not null && !Services.ThemeColors.IsSystemHighContrast();
        if (asset != _asset)
        {
            _asset = asset;
            _image.Source = asset is null ? null : new SvgImageSource(new Uri("ms-appx:///Assets/FluentIcons/" + asset + ".svg"));
        }
        _image.Visibility = color ? Visibility.Visible : Visibility.Collapsed;
        _glyph.Visibility = color ? Visibility.Collapsed : Visibility.Visible;
    }
}

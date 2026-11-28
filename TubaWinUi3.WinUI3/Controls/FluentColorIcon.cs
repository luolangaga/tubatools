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
    private string? _imagePath;

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

    /// <summary>自定义图片（绝对路径或 ms-appx 地址）；设置后优先于彩色矢量图标。</summary>
    public static readonly DependencyProperty ImagePathProperty = DependencyProperty.Register(
        nameof(ImagePath), typeof(string), typeof(FluentColorIcon), new PropertyMetadata("", Changed));
    public string ImagePath { get => (string)GetValue(ImagePathProperty); set => SetValue(ImagePathProperty, value); }

    /// <summary>强制只显示字体字形（Fluent 字形预设 / 高对比度）。</summary>
    public static readonly DependencyProperty GlyphOnlyProperty = DependencyProperty.Register(
        nameof(GlyphOnly), typeof(bool), typeof(FluentColorIcon), new PropertyMetadata(false, Changed));
    public bool GlyphOnly { get => (bool)GetValue(GlyphOnlyProperty); set => SetValue(GlyphOnlyProperty, value); }

    private static void Changed(DependencyObject sender, DependencyPropertyChangedEventArgs args)
        => ((FluentColorIcon)sender).Refresh();

    private void Refresh()
    {
        _glyph.Glyph = Glyph;
        _glyph.FontSize = FontSize;
        _image.Width = _image.Height = FontSize;

        var highContrast = Services.ThemeColors.IsSystemHighContrast();
        if (!string.IsNullOrWhiteSpace(ImagePath) && !highContrast)
        {
            if (_imagePath != ImagePath)
            {
                _imagePath = ImagePath;
                _image.Source = LoadImage(ImagePath);
            }
            if (_image.Source is not null)
            {
                _image.Visibility = Visibility.Visible;
                _glyph.Visibility = Visibility.Collapsed;
                return;
            }
        }
        else if (_imagePath is not null)
        {
            _imagePath = null;
            _image.Source = null;
        }

        _image.Visibility = Visibility.Collapsed;
        if (GlyphOnly || highContrast)
        {
            _glyph.Visibility = Visibility.Visible;
            return;
        }

        var asset = Services.FluentIconCatalog.AssetFor(Glyph);
        var color = asset is not null;
        if (asset != _asset || (asset is not null && _image.Source is null))
        {
            _asset = asset;
            _image.Source = asset is null ? null : new SvgImageSource(new Uri("ms-appx:///Assets/FluentIcons/" + asset + ".svg"));
        }
        _image.Visibility = color ? Visibility.Visible : Visibility.Collapsed;
        _glyph.Visibility = color ? Visibility.Collapsed : Visibility.Visible;
    }

    private static ImageSource? LoadImage(string path)
    {
        try
        {
            return path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)
                ? new SvgImageSource(new Uri(path))
                : new BitmapImage(new Uri(path));
        }
        catch
        {
            return null;
        }
    }
}

using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Tubatools.MinecraftLauncher.Ui;

/// <summary>加载包内像素贴图与图标（BitmapImage 缓存，必须在 UI 线程调用）。</summary>
internal static class McArt
{
    private static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static string _root = "";
    private static string _brandIcon = "";

    public static void Initialize(string pixelAssetRoot, string? brandIcon = null)
    {
        _root = pixelAssetRoot;
        if (!string.IsNullOrWhiteSpace(brandIcon)) _brandIcon = brandIcon;
    }

    public static Image BrandIcon(double size = 30)
    {
        var image = new Image
        {
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center
        };
        try
        {
            if (!string.IsNullOrWhiteSpace(_brandIcon) && File.Exists(_brandIcon))
                image.Source = new SvgImageSource(new Uri(_brandIcon));
        }
        catch
        {
            // 图标缺失时保留空白，不影响布局。
        }

        return image;
    }

    public static string PathOf(string name) => string.IsNullOrEmpty(_root)
        ? ""
        : Path.Combine(_root, name);

    public static ImageSource? Load(string name)
    {
        var path = PathOf(name);
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        if (Cache.TryGetValue(path, out var cached)) return cached;
        try
        {
            var bitmap = new BitmapImage { UriSource = new Uri(Path.GetFullPath(path)) };
            Cache[path] = bitmap;
            return bitmap;
        }
        catch
        {
            Cache[path] = null;
            return null;
        }
    }

    public static Image Icon(string name, double size = 18, double opacity = 1)
    {
        var image = new Image
        {
            Source = Load(name),
            Width = size,
            Height = size,
            Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Center,
            Opacity = opacity
        };
        return image;
    }

    public static ImageBrush? Brush(string name, Stretch stretch = Stretch.UniformToFill)
    {
        var source = Load(name);
        return source is null ? null : new ImageBrush { ImageSource = source, Stretch = stretch };
    }
}

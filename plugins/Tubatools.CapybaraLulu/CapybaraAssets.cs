using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace Tubatools.CapybaraLulu;

/// <summary>插件包里的美术资源。程序集位于 plugin/，资源在包根 assets/。</summary>
internal static class CapybaraAssets
{
    private static readonly string Directory = Path.GetFullPath(Path.Combine(
        Path.GetDirectoryName(typeof(CapybaraAssets).Assembly.Location) ?? AppContext.BaseDirectory, "..", "assets"));

    public static string File(string name) => Path.Combine(Directory, name);

    public static ImageSource Load(string name)
    {
        var image = new BitmapImage { DecodePixelType = DecodePixelType.Logical };
        image.UriSource = new Uri(File(name));
        return image;
    }

    public static string BackgroundFile(string theme, bool dark)
    {
        var resolved = theme switch
        {
            CapybaraSettings.ThemeBath => "bg-bath.jpg",
            CapybaraSettings.ThemePark => "bg-park.jpg",
            CapybaraSettings.ThemeDesk => "bg-desk.jpg",
            CapybaraSettings.ThemeNight => "bg-night.jpg",
            _ => dark ? "bg-night.jpg" : RotatingLight()
        };
        return resolved;
    }

    /// <summary>「自动」在浅色主题下按日期轮换三张日间图，每次启动都有一点新鲜感。</summary>
    private static string RotatingLight()
    {
        var pool = new[] { "bg-bath.jpg", "bg-park.jpg", "bg-desk.jpg" };
        return pool[DateTime.Now.DayOfYear % pool.Length];
    }
}

using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;

namespace TubaWinUi3.Services;

/// <summary>
/// 春节红包图：优先用随包的本地覆盖图（Assets/Festival/redenvelope.png），
/// 其次用数据目录里的缓存；缓存过期或缺失时才从官网拉一次。
/// 官网图之后会换成带支付宝口令的版本，所以不在安装包里写死。
/// 下载失败、返回 HTML（SPA 回退）或不是图片时全部按「没有图」处理，彩蛋改用程序化红包。
/// </summary>
public static class FestivalAssets
{
    public const string RedEnvelopeUrl = "https://tubawinui3.cn/redenvelope.png";

    private const int MaxImageBytes = 2 * 1024 * 1024;
    private static readonly TimeSpan CacheLifetime = TimeSpan.FromDays(30);

    private static string BundledPath =>
        Path.Combine(AppContext.BaseDirectory, "Assets", "Festival", "redenvelope.png");

    private static string CachePath =>
        Path.Combine(ConfigManager.GetDataDir(), "Festival", "Assets", "redenvelope.png");

    /// <summary>同步取已有图片（本地覆盖图或未过期缓存）；没有就返回 null。</summary>
    public static ImageSource? TryGetCachedSource()
    {
        if (IsImageFile(BundledPath)) return CreateSource(BundledPath);
        if (IsImageFile(CachePath)) return CreateSource(CachePath);
        return null;
    }

    /// <summary>随包的立体红包图：只作为加载失败时的视觉兜底，不含口令。</summary>
    public static ImageSource? TryGetPropSource()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "Festival", "redenvelope-prop.png");
        return IsImageFile(path) ? CreateSource(path) : null;
    }

    /// <summary>确保拿到图片：本地图 → 新鲜缓存 → 官网下载；任何失败都回退到已有结果或 null。</summary>
    public static async Task<ImageSource?> EnsureAsync()
    {
        if (IsImageFile(BundledPath)) return CreateSource(BundledPath);

        var cached = IsImageFile(CachePath) ? CreateSource(CachePath) : null;
        var fresh = File.Exists(CachePath) &&
            DateTime.UtcNow - File.GetLastWriteTimeUtc(CachePath) < CacheLifetime;
        if (cached is not null && fresh) return cached;

        try
        {
            var bytes = await DownloadAsync();
            if (bytes is null) return cached;

            var directory = Path.GetDirectoryName(CachePath);
            if (directory is not null) Directory.CreateDirectory(directory);
            var temp = CachePath + ".tmp";
            await File.WriteAllBytesAsync(temp, bytes);
            File.Move(temp, CachePath, true);
            return CreateSource(CachePath);
        }
        catch
        {
            return cached;
        }
    }

    private static async Task<byte[]?> DownloadAsync()
    {
        using var client = HttpClientFactory.CreateIpv4Preferred(TimeSpan.FromSeconds(10));
        using var response = await client.GetAsync(RedEnvelopeUrl, HttpCompletionOption.ResponseHeadersRead);
        if (!response.IsSuccessStatusCode) return null;

        var mediaType = response.Content.Headers.ContentType?.MediaType;
        if (mediaType is not null && !mediaType.StartsWith("image/", StringComparison.OrdinalIgnoreCase)) return null;
        var declared = response.Content.Headers.ContentLength;
        if (declared is > MaxImageBytes) return null;

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var buffer = new MemoryStream();
        var chunk = new byte[64 * 1024];
        while (true)
        {
            var read = await stream.ReadAsync(chunk.AsMemory());
            if (read <= 0) break;
            buffer.Write(chunk, 0, read);
            if (buffer.Length > MaxImageBytes) return null;
        }

        var bytes = buffer.ToArray();
        return IsImageBytes(bytes) ? bytes : null;
    }

    private static ImageSource CreateSource(string path)
    {
        var bitmap = new BitmapImage
        {
            DecodePixelHeight = 720,
            UriSource = new Uri(Path.GetFullPath(path))
        };
        return bitmap;
    }

    private static bool IsImageFile(string path)
    {
        if (!File.Exists(path)) return false;
        try
        {
            using var stream = File.OpenRead(path);
            var head = new byte[12];
            if (stream.Read(head, 0, head.Length) < 8) return false;
            return IsImageBytes(head);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsImageBytes(byte[] bytes)
    {
        if (bytes.Length < 8) return false;
        if (bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47) return true; // PNG
        if (bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF) return true; // JPEG
        if (bytes[0] == 0x47 && bytes[1] == 0x49 && bytes[2] == 0x46) return true; // GIF
        if (bytes[0] == 0x42 && bytes[1] == 0x4D) return true; // BMP
        if (bytes.Length >= 12 && bytes[0] == 0x52 && bytes[1] == 0x49 && bytes[2] == 0x46 && bytes[3] == 0x46
            && bytes[8] == 0x57 && bytes[9] == 0x45 && bytes[10] == 0x42 && bytes[11] == 0x50) return true; // WebP
        return false;
    }
}

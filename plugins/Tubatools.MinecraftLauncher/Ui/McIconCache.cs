using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Png;

namespace Tubatools.MinecraftLauncher.Ui;

/// <summary>Modrinth 图标缓存：下载后统一转成 PNG（WebP 在 WinUI 中可能解码失败）。</summary>
internal static class McIconCache
{
    private static readonly SemaphoreSlim Gate = new(4);

    public static async Task<ImageSource?> LoadAsync(HttpClient http, string dataDirectory, string projectId,
        string? iconUrl, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(iconUrl)) return null;
        var safeId = new string(projectId.Where(char.IsAsciiLetterOrDigit).ToArray());
        if (safeId.Length == 0) return null;
        var directory = Path.Combine(dataDirectory, "cache", "modrinth-icons");
        var path = Path.Combine(directory, safeId + ".png");
        if (!File.Exists(path))
        {
            await Gate.WaitAsync(ct);
            try
            {
                if (!File.Exists(path))
                {
                    Directory.CreateDirectory(directory);
                    var bytes = await http.GetByteArrayAsync(iconUrl, ct);
                    var png = await Task.Run(() => Normalize(bytes), ct);
                    var temp = path + ".tmp";
                    await File.WriteAllBytesAsync(temp, png, ct);
                    File.Move(temp, path, true);
                }
            }
            finally
            {
                Gate.Release();
            }
        }

        return new BitmapImage(new Uri(path));
    }

    private static byte[] Normalize(byte[] bytes)
    {
        // 已经是 PNG 时直接落盘。
        if (bytes.Length > 8 && bytes[0] == 0x89 && bytes[1] == 0x50 && bytes[2] == 0x4E && bytes[3] == 0x47)
            return bytes;
        using var image = Image.Load(bytes);
        using var output = new MemoryStream();
        image.Save(output, new PngEncoder());
        return output.ToArray();
    }
}

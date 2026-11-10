using System.Security.Cryptography;
using Windows.Graphics.Imaging;
using Windows.Storage;

namespace TubaWinUi3.Services.Plugins;

internal static class PluginCustomizationFiles
{
    internal static string ResolveLocalFile(string packageDirectory, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("图片路径不能为空。");
        var full = Path.IsPathFullyQualified(path) ? Path.GetFullPath(path) : PluginPackageRules.ResolveFile(packageDirectory, path);
        if (!File.Exists(full)) throw new FileNotFoundException("找不到图片。", full);
        return full;
    }
    internal static async Task<string> ImportBackgroundAsync(string packageDirectory, string path, CancellationToken ct)
    {
        var full = ResolveLocalFile(packageDirectory, path);
        if (!new[] { ".png", ".jpg", ".jpeg", ".bmp", ".webp" }.Contains(Path.GetExtension(full), StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("背景图支持 PNG、JPEG、BMP 和 WebP。");
        if (new FileInfo(full).Length > 64 * 1024 * 1024) throw new ArgumentException("背景图不能超过 64 MB。");
        var file = await StorageFile.GetFileFromPathAsync(full).AsTask(ct);
        using (var stream = await file.OpenReadAsync().AsTask(ct)) await BitmapDecoder.CreateAsync(stream).AsTask(ct);
        return await CopyAsync(full, ConfigManager.GetBackgroundsDir(), "plugin-bg-", ct);
    }
    internal static async Task<string> ImportNavigationIconAsync(string packageDirectory, string path, CancellationToken ct)
    {
        var full = ResolveLocalFile(packageDirectory, path);
        PluginPackageRules.ResolveIcon(Path.GetDirectoryName(full)!, Path.GetFileName(full));
        return await CopyAsync(full, Path.Combine(ConfigManager.GetDataDir(), "PluginCustomization", "NavigationIcons"), "", ct);
    }
    private static async Task<string> CopyAsync(string full, string directory, string prefix, CancellationToken ct)
    {
        Directory.CreateDirectory(directory);
        await using var input = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        var hash = Convert.ToHexString(await SHA256.HashDataAsync(input, ct)); input.Position = 0;
        var destination = Path.Combine(directory, prefix + hash + Path.GetExtension(full).ToLowerInvariant());
        if (File.Exists(destination)) return destination;
        var temp = destination + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await using (var output = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true)) await input.CopyToAsync(output, ct);
            ct.ThrowIfCancellationRequested(); File.Move(temp, destination, true); return destination;
        }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
}

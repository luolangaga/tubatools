using System.Diagnostics;
using System.Drawing;
using System.Security.Cryptography;
using System.Text;
using TubaWinUi3.Models;

namespace TubaWinUi3.Services;

public static class ToolIconService
{
    private static readonly string CacheRoot = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TubaWinUi3",
        "IconCache");

    private static readonly TimeSpan CacheMaxAge = TimeSpan.FromDays(90);

    private static readonly Dictionary<string, string> ExtensionGlyphs = new(StringComparer.OrdinalIgnoreCase)
    {
        [".bat"] = "\uE756",
        [".cmd"] = "\uE756",
        [".ps1"] = "\uE943",
        [".vbs"] = "\uE943",
        [".msc"] = "\uEC7A",
    };

    public static string? GetIconGlyph(string toolPath)
    {
        var extension = Path.GetExtension(toolPath);
        if (extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
            extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }
        return ExtensionGlyphs.TryGetValue(extension, out var glyph) ? glyph : "\uE8B7";
    }

    public static string? GetCachedIconPath(string toolPath)
    {
        if (!File.Exists(toolPath))
            return null;

        var extension = Path.GetExtension(toolPath);
        if (!extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
            return null;

        Directory.CreateDirectory(CacheRoot);
        var iconPath = Path.Combine(CacheRoot, $"{Hash(toolPath)}.png");

        if (!File.Exists(iconPath))
            return null;

        var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(iconPath);
        if (age >= CacheMaxAge)
        {
            try { File.Delete(iconPath); } catch { }
            return null;
        }

        return iconPath;
    }

    public static async Task<string?> ExtractIconToCacheAsync(string toolPath)
    {
        if (!File.Exists(toolPath))
            return null;

        var extension = Path.GetExtension(toolPath);
        if (!extension.Equals(".exe", StringComparison.OrdinalIgnoreCase) &&
            !extension.Equals(".lnk", StringComparison.OrdinalIgnoreCase))
            return null;

        return await Task.Run(() =>
        {
            Directory.CreateDirectory(CacheRoot);
            var iconPath = Path.Combine(CacheRoot, $"{Hash(toolPath)}.png");

            if (File.Exists(iconPath))
            {
                var age = DateTime.UtcNow - File.GetLastWriteTimeUtc(iconPath);
                if (age < CacheMaxAge)
                    return iconPath;

                try { File.Delete(iconPath); } catch { return iconPath; }
            }

            try
            {
                using var icon = Icon.ExtractAssociatedIcon(toolPath);
                if (icon is null)
                    return null;

                using var bitmap = icon.ToBitmap();
                bitmap.Save(iconPath, System.Drawing.Imaging.ImageFormat.Png);
                return iconPath;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Unable to extract icon for {toolPath}: {ex.Message}");
                return null;
            }
        });
    }

    public static async Task LoadIconsAsync(
        IReadOnlyList<ToolItem> tools,
        Microsoft.UI.Dispatching.DispatcherQueue? dispatcher)
    {
        var itemsToLoad = tools
            .Where(t => t.IconPath is null && !string.IsNullOrWhiteSpace(t.Path))
            .Where(t =>
            {
                var ext = System.IO.Path.GetExtension(t.Path);
                return ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
                       ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase);
            })
            .ToList();

        if (itemsToLoad.Count == 0)
            return;

        foreach (var tool in itemsToLoad)
        {
            var cached = GetCachedIconPath(tool.Path);
            if (cached is not null)
            {
                if (dispatcher is not null)
                    dispatcher.TryEnqueue(() => tool.IconPath = cached);
                else
                    tool.IconPath = cached;
                continue;
            }

            var iconPath = await ExtractIconToCacheAsync(tool.Path);
            if (iconPath is not null)
            {
                if (dispatcher is not null)
                    dispatcher.TryEnqueue(() => tool.IconPath = iconPath);
                else
                    tool.IconPath = iconPath;
            }
        }
    }

    public static void CleanExpiredCache()
    {
        if (!Directory.Exists(CacheRoot))
            return;

        var cutoff = DateTime.UtcNow - CacheMaxAge;

        foreach (var file in Directory.EnumerateFiles(CacheRoot, "*.png"))
        {
            try
            {
                if (File.GetLastWriteTimeUtc(file) < cutoff)
                    File.Delete(file);
            }
            catch { }
        }
    }

    public static void CleanAllCache()
    {
        if (!Directory.Exists(CacheRoot))
            return;

        try
        {
            Directory.Delete(CacheRoot, true);
        }
        catch { }
    }

    private static string Hash(string value)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(value));
        return Convert.ToHexString(bytes)[..16].ToLowerInvariant();
    }
}

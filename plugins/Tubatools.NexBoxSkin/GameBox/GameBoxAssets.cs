using System.IO;

namespace Tubatools.NexBoxSkin.GameBox;

internal static class GameBoxAssets
{
    public static string PackageRoot { get; } = ResolvePackageRoot();

    public static string WebRoot => Path.Combine(PackageRoot, "assets", "web");

    public static bool IsReady => File.Exists(Path.Combine(WebRoot, "index.html"));

    private static string ResolvePackageRoot()
    {
        var assemblyDirectory = Path.GetDirectoryName(typeof(GameBoxAssets).Assembly.Location);
        if (string.IsNullOrWhiteSpace(assemblyDirectory))
        {
            return AppContext.BaseDirectory;
        }

        // Plugin layout: <package>/plugin/Tubatools.NexBoxSkin.dll
        var candidate = Path.GetFullPath(Path.Combine(assemblyDirectory, ".."));
        if (Directory.Exists(Path.Combine(candidate, "assets", "web")))
        {
            return candidate;
        }

        // Local/dev fallback: <repo>/plugins/Tubatools.NexBoxSkin/assets/web
        var local = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "plugins", "Tubatools.NexBoxSkin"));
        return Directory.Exists(Path.Combine(local, "assets", "web")) ? local : candidate;
    }

    public static string? ReadBuiltinIconDataUri(string? id)
    {
        if (string.IsNullOrWhiteSpace(id) || id.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || id.Contains(".."))
        {
            return null;
        }

        var path = Path.Combine(AppContext.BaseDirectory, "Assets", "BuiltinIcons", id + ".svg");
        return File.Exists(path) ? ToDataUri("image/svg+xml", path) : null;
    }

    public static string? ReadAssetDataUri(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath) || Path.IsPathRooted(relativePath) || relativePath.Contains(".."))
        {
            return null;
        }

        var path = Path.Combine(PackageRoot, relativePath);
        return File.Exists(path) ? ToDataUri("image/svg+xml", path) : null;
    }

    private static string? ToDataUri(string mimeType, string path)
    {
        try
        {
            var bytes = File.ReadAllBytes(path);
            return bytes.Length == 0 ? null : $"data:{mimeType};base64,{Convert.ToBase64String(bytes)}";
        }
        catch
        {
            return null;
        }
    }
}

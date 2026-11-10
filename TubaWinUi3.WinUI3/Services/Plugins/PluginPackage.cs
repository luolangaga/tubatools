using System.Text.Json;
using System.Text.RegularExpressions;

namespace TubaWinUi3.Services.Plugins;

public sealed record PluginPackage(string Id, string Version, string Description, string Authors,
    long TotalDownloads = 0, string IconUrl = "");
public sealed record PluginManifest(int ApiVersion, string Assembly, string EntryType,
    string Name = "", string Description = "", string Author = "", string Website = "", string Icon = "", bool FullTrust = false);
public sealed record InstalledPlugin(string Id, string Version, bool Enabled = true);

internal static class PluginPackageRules
{
    internal static string Segment(string value)
    {
        if (!Regex.IsMatch(value, @"\A[a-zA-Z0-9][a-zA-Z0-9._+-]{0,199}\z") || value.Contains(".."))
            throw new InvalidDataException("无效的 NuGet 包 ID 或版本。");
        return value.ToLowerInvariant();
    }

    internal static string ResolveFile(string root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.Contains(':'))
            throw new InvalidDataException("插件路径必须是包内相对路径。");
        var full = Path.GetFullPath(Path.Combine(root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(Path.GetFullPath(root) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("插件路径超出包目录。");
        return full;
    }

    internal static PluginManifest ReadManifest(string root)
    {
        var manifest = JsonSerializer.Deserialize<PluginManifest>(File.ReadAllText(Path.Combine(root, "tubatools.json")),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? throw new InvalidDataException("缺少插件清单。");
        manifest = manifest with { Name = manifest.Name ?? "", Description = manifest.Description ?? "", Author = manifest.Author ?? "", Website = manifest.Website ?? "", Icon = manifest.Icon ?? "" };
        if (manifest.ApiVersion != 2 || string.IsNullOrWhiteSpace(manifest.EntryType) ||
            string.IsNullOrWhiteSpace(manifest.Assembly) || !manifest.Assembly.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
            !File.Exists(ResolveFile(root, manifest.Assembly)))
            throw new InvalidDataException("需要 API v2 插件，请使用新版 SDK 重新编译，并检查入口类型和程序集。");
        if (manifest.Website.Length > 0 && (!Uri.TryCreate(manifest.Website, UriKind.Absolute, out var website) || website.Scheme is not ("https" or "http")))
            throw new InvalidDataException("插件主页必须是 HTTP/HTTPS 地址。");
        if (manifest.Icon.Length > 0) ResolveIcon(root, manifest.Icon);
        return manifest;
    }

    internal static string ResolveIcon(string root, string relative)
    {
        var path = ResolveFile(root, relative);
        if (!new[] { ".svg", ".png", ".jpg", ".jpeg", ".bmp" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase) || !File.Exists(path))
            throw new InvalidDataException("插件图标必须是包内存在的 SVG/PNG/JPEG/BMP 文件。");
        if (new FileInfo(path).Length > 2 * 1024 * 1024) throw new InvalidDataException("插件图标超过 2 MB。");
        if (Path.GetExtension(path).Equals(".svg", StringComparison.OrdinalIgnoreCase))
        {
            using var reader = System.Xml.XmlReader.Create(path, new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null });
            if (System.Xml.Linq.XDocument.Load(reader).Root?.Name.LocalName != "svg") throw new InvalidDataException("SVG 图标格式无效。");
        }
        return path;
    }
}

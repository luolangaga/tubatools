using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;

namespace TubaWinUi3.Services.Plugins;

/// <summary>
/// 包内 <c>tubatools.json</c> 挑出来的展示信息。NuGet 搜索索引只有包 ID、描述和可选的
/// <c>iconUrl</c>（社区包大多没设 <c>PackageIcon</c>），所以未安装时想显示插件自己的名字和图标，
/// 只能回到包里读清单——这也是「已安装」列表显示同一套名字/图标的原因。
/// </summary>
public sealed record PackagePreview(string Name, string Description, string Author,
    string IconEntry, string IconExtension, byte[]? IconBytes);

public static class NuGetPackagePreview
{
    private const string ManifestEntry = "tubatools.json";
    private const int MaxManifestBytes = 64 * 1024;
    private const int MaxIconBytes = 2 * 1024 * 1024;
    private static readonly string[] IconExtensions = [".svg", ".png", ".jpg", ".jpeg", ".bmp"];

    /// <summary>只读 Range，取不到任何东西都返回 null——列表回退到包 ID + 字形，不打扰用户。</summary>
    public static async Task<PackagePreview?> ReadAsync(string id, string version, CancellationToken ct)
    {
        try
        {
            var url = await NuGetPluginSource.PackageContentUrlAsync(id, version, ct);
            // ZipArchive 是同步读，Range 请求必须落在后台线程，不能卡 UI。
            return await Task.Run(() => ReadRemote(url, ct), ct);
        }
        catch (Exception) { return null; }
    }

    private static PackagePreview? ReadRemote(string url, CancellationToken ct)
    {
        using var http = ProxyService.CreateClient(TimeSpan.FromSeconds(20));
        using var source = HttpRangeSource.Open(http, url, ct);
        using var stream = new RangeReadStream(source);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        return ReadArchive(archive);
    }

    /// <summary>从已打开的包读清单和图标；单测直接喂本地 nupkg。</summary>
    internal static PackagePreview? ReadArchive(ZipArchive archive)
    {
        var manifest = FindEntry(archive, ManifestEntry);
        if (manifest is null || manifest.Length > MaxManifestBytes) return null;

        var text = ReadEntry(manifest, MaxManifestBytes);
        using var document = JsonDocument.Parse(System.Text.Encoding.UTF8.GetString(text));
        if (document.RootElement.ValueKind != JsonValueKind.Object) return null;
        var root = document.RootElement;
        var name = Text(root, "name");
        var description = Text(root, "description");
        var author = Text(root, "author");
        var iconEntry = Normalize(Text(root, "icon"));

        byte[]? icon = null;
        string extension = "";
        if (iconEntry.Length > 0 && FindEntry(archive, iconEntry) is { } entry && entry.Length is > 0 and <= MaxIconBytes)
        {
            extension = Path.GetExtension(iconEntry).ToLowerInvariant();
            if (IconExtensions.Contains(extension))
            {
                var bytes = ReadEntry(entry, MaxIconBytes);
                if (IsPlausibleIcon(bytes, extension)) icon = bytes;
            }
        }
        return new PackagePreview(name, description, author, iconEntry, extension, icon);
    }

    private static ZipArchiveEntry? FindEntry(ZipArchive archive, string relative)
    {
        var target = Normalize(relative);
        if (!IsValidRelative(target)) return null;
        return archive.Entries.FirstOrDefault(e => Normalize(e.FullName).Equals(target, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>包内路径必须停在包内：清单永远不覆盖磁盘，但拒绝越界/绝对路径仍是既定口径。</summary>
    private static string Normalize(string value)
    {
        var path = value.Replace('\\', '/').Trim();
        while (path.StartsWith('/')) path = path[1..];
        return path;
    }

    private static bool IsValidRelative(string path) =>
        path.Length > 0 && !Path.IsPathRooted(path) && !path.Contains(':') && !path.Split('/').Any(part => part == "..");

    private static byte[] ReadEntry(ZipArchiveEntry entry, int limit)
    {
        using var stream = entry.Open();
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
        {
            if (buffer.Length + read > limit) throw new InvalidDataException("插件包条目超出大小上限。");
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    /// <summary>和安装路径同一套校验：只认扁平图形格式，SVG 必须是可解析的 svg 根元素。</summary>
    private static bool IsPlausibleIcon(byte[] bytes, string extension)
    {
        if (bytes.Length == 0) return false;
        if (extension != ".svg") return true;
        try
        {
            using var reader = System.Xml.XmlReader.Create(new MemoryStream(bytes),
                new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null });
            return XDocument.Load(reader).Root?.Name.LocalName == "svg";
        }
        catch { return false; }
    }

    private static string Text(JsonElement parent, string name)
    {
        if (!parent.TryGetProperty(name, out var value)) return "";
        return value.ValueKind == JsonValueKind.String ? (value.GetString() ?? "").Trim() : "";
    }
}

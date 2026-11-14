using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace TubaWinUi3.Services.Plugins;

/// <summary>
/// NuGet 包内 nuspec + README 挑出来的详情页信息。列表页只读清单和图标，
/// 详情页才按需读取本记录——README 动辄几十 KB，不该跟着列表一起抓。
/// </summary>
public sealed record PackageDetails(
    string Readme,
    string ProjectUrl,
    string RepositoryUrl,
    string RepositoryType,
    string License,
    string LicenseUrl,
    string Tags,
    string Title)
{
    public bool HasLinks => ProjectUrl.Length > 0 || RepositoryUrl.Length > 0 || LicenseUrl.Length > 0;
}

/// <summary>
/// 详情页的包内信息读取器。与 <see cref="NuGetPackagePreview"/> 共用 Range 读取口径：
/// 只取 nuspec 和 README 所在区间，不下载整个插件包。
/// </summary>
public static class NuGetPackageDetails
{
    private const int MaxNuspecBytes = 64 * 1024;
    private const int MaxReadmeBytes = 256 * 1024;
    private const int MaxReadmeChars = 200_000;
    private const string ReadmeFallback = "README.md";

    /// <summary>读取失败返回 null（详情页回退到包描述，不阻塞安装）。</summary>
    public static async Task<PackageDetails?> ReadAsync(string id, string version, CancellationToken ct)
    {
        try
        {
            var url = await NuGetPluginSource.PackageContentUrlAsync(id, version, ct);
            // ZipArchive 是同步读，Range 请求必须落在后台线程，不能卡 UI。
            return await Task.Run(() => ReadRemote(url, ct), ct);
        }
        catch (Exception) { return null; }
    }

    private static PackageDetails? ReadRemote(string url, CancellationToken ct)
    {
        using var http = ProxyService.CreateClient(TimeSpan.FromSeconds(20));
        using var source = HttpRangeSource.Open(http, url, ct);
        using var stream = new RangeReadStream(source);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        return ReadArchive(archive);
    }

    /// <summary>从已打开的包读 nuspec 与 README；单测直接喂本地 nupkg。</summary>
    internal static PackageDetails? ReadArchive(ZipArchive archive)
    {
        var nuspec = archive.Entries.FirstOrDefault(entry =>
            entry.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase) && !entry.FullName.Contains('/'));
        if (nuspec is null || nuspec.Length > MaxNuspecBytes) return null;

        XElement metadata;
        try
        {
            using var stream = nuspec.Open();
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            });
            var document = XDocument.Load(reader);
            metadata = document.Root?.Elements().FirstOrDefault(e => e.Name.LocalName == "metadata") ?? document.Root!;
            if (metadata is null) return null;
        }
        catch { return null; }

        string Value(string name) => metadata.Elements()
            .FirstOrDefault(e => e.Name.LocalName.Equals(name, StringComparison.OrdinalIgnoreCase))?.Value.Trim() ?? "";

        var repository = metadata.Elements()
            .FirstOrDefault(e => e.Name.LocalName.Equals("repository", StringComparison.OrdinalIgnoreCase));
        var projectUrl = SafeUrl(Value("projectUrl"));
        var repositoryUrl = SafeUrl(repository?.Attribute("url")?.Value ?? "");
        var repositoryType = (repository?.Attribute("type")?.Value ?? "").Trim();
        var license = Value("license");
        var licenseUrl = SafeUrl(Value("licenseUrl"));
        var tags = Value("tags");
        var title = Value("title");

        var readmeEntry = Value("readme");
        var readme = "";
        ZipArchiveEntry? entry = null;
        if (readmeEntry.Length > 0) entry = FindEntry(archive, readmeEntry);
        entry ??= FindEntry(archive, ReadmeFallback);
        if (entry is null && readmeEntry.Length == 0)
        {
            // 有些包没有声明 <readme>，但把 README 放在任意一级目录里；只认根目录下的常见名字，避免乱抓。
            entry = archive.Entries.FirstOrDefault(e =>
                !e.FullName.Contains('/') &&
                e.FullName.StartsWith("readme", StringComparison.OrdinalIgnoreCase) &&
                e.FullName.EndsWith(".md", StringComparison.OrdinalIgnoreCase));
        }
        if (entry is not null && entry.Length is > 0 and <= MaxReadmeBytes)
        {
            try { readme = CleanReadme(DecodeText(ReadEntry(entry)), title); }
            catch { readme = ""; }
        }

        return new PackageDetails(readme, projectUrl, repositoryUrl, repositoryType,
            license, licenseUrl, tags, title);
    }

    private static ZipArchiveEntry? FindEntry(ZipArchive archive, string relative)
    {
        var target = relative.Replace('\\', '/').Trim().TrimStart('/');
        if (target.Length == 0 || target.Contains(':') || target.Split('/').Any(part => part == "..")) return null;
        return archive.Entries.FirstOrDefault(e =>
            e.FullName.Replace('\\', '/').Equals(target, StringComparison.OrdinalIgnoreCase));
    }

    private static byte[] ReadEntry(ZipArchiveEntry entry)
    {
        using var stream = entry.Open();
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        int read;
        while ((read = stream.Read(chunk, 0, chunk.Length)) > 0)
        {
            if (buffer.Length + read > MaxReadmeBytes) break;
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    /// <summary>README 常见 UTF-8 带 BOM；少数老包是 UTF-16，按 BOM 判断，避免整篇乱码。</summary>
    private static string DecodeText(byte[] bytes)
    {
        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
            return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
            return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);
        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);
        return Encoding.UTF8.GetString(bytes);
    }

    private static string SafeUrl(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "";
        if (!Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)) return "";
        return uri.Scheme is "http" or "https" ? uri.ToString() : "";
    }

    /// <summary>
    /// README 是给 NuGet 网页渲染的，直接塞进 MarkdownView 会出现图片残留、徽章链接和重复 H1。
    /// 这里只做显示层清洗：不执行 HTML、不解析图片，保留正文、代码块和链接。
    /// </summary>
    internal static string CleanReadme(string markdown, string? title = null)
    {
        if (string.IsNullOrWhiteSpace(markdown)) return "";
        markdown = markdown.Replace("\r\n", "\n").Replace('\r', '\n');
        if (markdown.Length > MaxReadmeChars) markdown = markdown[..MaxReadmeChars];

        // YAML front matter（不少 README 用它放标题/徽章）。
        var trimmedStart = markdown.TrimStart();
        if (trimmedStart.StartsWith("---\n", StringComparison.Ordinal))
        {
            var end = trimmedStart.IndexOf("\n---", 4, StringComparison.Ordinal);
            if (end >= 0)
            {
                var after = end + 4;
                markdown = after < trimmedStart.Length ? trimmedStart[(after)..] : "";
            }
        }

        markdown = Regex.Replace(markdown, @"<!--.*?-->", "", RegexOptions.Singleline);
        markdown = Regex.Replace(markdown, @"<(script|style|svg|picture)\b[^>]*>.*?</\1\s*>", "", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        markdown = Regex.Replace(markdown, @"<img\b[^>]*>", "", RegexOptions.IgnoreCase);
        markdown = Regex.Replace(markdown, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);

        // <a href="url">文字</a> → [文字](url)，其余 HTML 标签剥掉。
        markdown = Regex.Replace(markdown, @"<a\b[^>]*href\s*=\s*[""']([^""']+)[""'][^>]*>(.*?)</a\s*>",
            match => $"[{Regex.Replace(match.Groups[2].Value, @"<[^>]+>", "")}]({match.Groups[1].Value})",
            RegexOptions.Singleline | RegexOptions.IgnoreCase);

        // Markdown 图片（含徽章）：整段移除；徽章定义行也移除。
        markdown = Regex.Replace(markdown, @"!\[[^\]]*\]\([^)]*\)", "", RegexOptions.Singleline);
        markdown = Regex.Replace(markdown, @"^\s*\[[^\]]+\]:\s*https?://\S+\s*$", "", RegexOptions.Multiline);
        markdown = Regex.Replace(markdown, @"^\s*\[!\[[^\]]*\]\([^)]*\)\]\([^)]*\)\s*$", "", RegexOptions.Multiline);

        // 只保留 <https://...> 这种自动链接，其余尖括号标签全部剥掉。
        var autolinks = new List<string>();
        markdown = Regex.Replace(markdown, @"<(https?://[^>\s]+)>", match =>
        {
            autolinks.Add(match.Value);
            return $"\u0001AUTOLINK{autolinks.Count - 1}\u0001";
        });
        markdown = Regex.Replace(markdown, @"<[^>]+>", "");
        markdown = Regex.Replace(markdown, @"\u0001AUTOLINK(\d+)\u0001",
            match => autolinks[int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture)]);

        // README 的第一行 H1 通常就是插件名，和详情页大标题重复。
        var lines = markdown.Split('\n');
        var first = lines.FirstOrDefault(line => !string.IsNullOrWhiteSpace(line));
        if (first is not null && Regex.IsMatch(first.Trim(), @"^#\s+"))
        {
            var index = Array.IndexOf(lines, first);
            markdown = string.Join('\n', lines.Where((_, i) => i != index));
        }

        markdown = Regex.Replace(markdown, @"\n{3,}", "\n\n").Trim();

        if (markdown.Length > MaxReadmeChars)
        {
            var cut = markdown.LastIndexOf("\n\n", MaxReadmeChars - 32, StringComparison.Ordinal);
            if (cut < MaxReadmeChars / 2) cut = MaxReadmeChars - 32;
            markdown = markdown[..cut].TrimEnd() + "\n\n…";
        }
        return markdown;
    }
}

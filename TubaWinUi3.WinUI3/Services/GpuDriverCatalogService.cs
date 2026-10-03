using System.IO.Compression;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using HtmlAgilityPack;

namespace TubaWinUi3.Services;

public sealed record GpuDriverRelease(
    string Vendor,
    string Model,
    string Version,
    string ReleaseDate,
    string FileName,
    Uri DownloadUri,
    Uri CatalogUri);

public sealed record GpuDriverVendor(string Name, Uri CatalogUri);

public static partial class GpuDriverCatalogService
{
    private static readonly GpuDriverVendor[] Vendors =
    [
        new("NVIDIA", new Uri("https://www.nvidia.com/Download/index.aspx?lang=cn")),
        new("AMD", new Uri("https://www.amd.com/en/support/downloads/drivers.html")),
        new("Intel", new Uri("https://www.intel.com/content/www/us/en/support/detect.html")),
    ];

    private static readonly string[] AllowedExtensions = [".exe", ".zip"];

    public static IReadOnlyList<GpuDriverVendor> GetVendors() => Vendors;

    public static string GetDownloadDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Downloads", "图吧工具箱", "显卡驱动");

    public static async Task<IReadOnlyList<GpuDriverRelease>> FindDriversAsync(
        string vendor,
        string model,
        string hardwareId,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(model))
            return [];

        using var client = ProxyService.CreateClient(TimeSpan.FromSeconds(30));
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64)");
        return vendor switch
        {
            "NVIDIA" => await FindNvidiaDriversAsync(client, model, cancellationToken),
            "AMD" => await FindAmdDriversAsync(client, model, cancellationToken),
            "Intel" => await FindIntelDriversAsync(client, hardwareId, cancellationToken),
            _ => [],
        };
    }

    public static bool TryGetDownloadDestination(
        string downloadUri,
        string suggestedPath,
        string destinationDirectory,
        out string destinationPath)
    {
        destinationPath = "";
        if (!Uri.TryCreate(downloadUri, UriKind.Absolute, out var uri) ||
            uri.Scheme != Uri.UriSchemeHttps ||
            !IsOfficialHost(uri.Host))
            return false;

        var fileName = Path.GetFileName(suggestedPath);
        if (string.IsNullOrWhiteSpace(fileName))
            fileName = Path.GetFileName(uri.LocalPath);
        if (string.IsNullOrWhiteSpace(fileName))
            return false;

        var extension = Path.GetExtension(fileName);
        if (!AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
            return false;

        var invalidChars = Path.GetInvalidFileNameChars();
        fileName = string.Concat(fileName.Select(c => invalidChars.Contains(c) ? '_' : c)).Trim(' ', '.');
        if (string.IsNullOrWhiteSpace(fileName))
            return false;

        var nameWithoutExtension = Path.GetFileNameWithoutExtension(fileName);
        var candidate = Path.Combine(destinationDirectory, fileName);
        for (var index = 1; File.Exists(candidate); index++)
            candidate = Path.Combine(destinationDirectory, $"{nameWithoutExtension} ({index}){extension}");

        destinationPath = candidate;
        return true;
    }

    internal static IReadOnlyList<GpuDriverRelease> ParseNvidiaDrivers(string json, string model, Uri catalogUri)
    {
        var releases = new List<GpuDriverRelease>();
        using var document = JsonDocument.Parse(json);
        var ids = FindProperty(document.RootElement, "IDS");
        if (ids.ValueKind != JsonValueKind.Array)
            return releases;

        foreach (var item in ids.EnumerateArray())
        {
            var downloadInfo = FindProperty(item, "downloadInfo");
            var url = GetString(downloadInfo, "DownloadURL");
            if (!TryValidateDownloadUrl(url, "nvidia.com", out var downloadUri))
                continue;

            var version = GetString(downloadInfo, "Version") ??
                          GetString(downloadInfo, "VersionString") ??
                          VersionFromFile(downloadUri);
            var releaseDate = GetString(downloadInfo, "ReleaseDateTime") ??
                              GetString(downloadInfo, "ReleaseDate") ?? "";
            releases.Add(new GpuDriverRelease(
                "NVIDIA", model, version ?? "未知版本", releaseDate,
                Path.GetFileName(downloadUri.LocalPath), downloadUri, catalogUri));
        }

        return releases;
    }

    internal static IReadOnlyList<GpuDriverRelease> ParseAmdDrivers(string html, string model, Uri catalogUri)
    {
        var document = new HtmlDocument();
        document.LoadHtml(html);
        var packages = new List<GpuDriverRelease>();

        var links = document.DocumentNode.SelectNodes("//a[@href]");
        if (links is null)
            return packages;

        foreach (var link in links)
        {
            var href = HtmlEntity.DeEntitize(link.GetAttributeValue("href", ""));
            if (!TryValidateDownloadUrl(href, "amd.com", out var uri) ||
                !Path.GetExtension(uri.LocalPath).Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
                !Path.GetFileName(uri.LocalPath).Contains("adrenalin", StringComparison.OrdinalIgnoreCase))
                continue;

            var fileName = Path.GetFileName(uri.LocalPath);
            var version = VersionFromFile(uri);
            packages.Add(new GpuDriverRelease(
                "AMD", model, version ?? link.InnerText.Trim(), "",
                fileName, uri, catalogUri));
        }

        return packages
            .DistinctBy(item => item.DownloadUri.AbsoluteUri, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(item => ParseVersion(item.Version))
            .ToList();
    }

    internal static IReadOnlyList<GpuDriverRelease> ParseIntelCatalog(string configurations, string hardwareId, Uri catalogUri)
    {
        if (!TryGetIntelDeviceId(hardwareId, out var deviceId))
            return [];

        using var document = JsonDocument.Parse(configurations);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
            return [];

        var releases = new List<GpuDriverRelease>();
        foreach (var entry in document.RootElement.EnumerateArray())
        {
            if (!SupportsIntelDevice(entry, deviceId) ||
                IsTrue(FindProperty(entry, "IsBeta")))
                continue;

            var version = GetString(entry, "Version");
            var name = GetString(entry, "Name") ?? "Intel 显卡驱动";
            var date = GetString(entry, "DisplayReleaseDate") ?? "";
            var files = FindProperty(entry, "Files");
            if (files.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var file in files.EnumerateArray())
            {
                var rawUrl = GetString(file, "Url");
                if (!TryValidateDownloadUrl(rawUrl, "intel.com", out var uri) ||
                    !Path.GetExtension(uri.LocalPath).Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
                    !SupportsWindows64(file))
                    continue;

                releases.Add(new GpuDriverRelease(
                    "Intel", name, version ?? "未知版本", date,
                    Path.GetFileName(uri.LocalPath), uri, catalogUri));
            }
        }

        return releases
            .DistinctBy(item => item.DownloadUri.AbsoluteUri, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(item => ParseVersion(item.Version))
            .Take(20)
            .ToList();
    }

    private static async Task<IReadOnlyList<GpuDriverRelease>> FindNvidiaDriversAsync(
        HttpClient client, string model, CancellationToken cancellationToken)
    {
        var (seriesId, productId) = await FindNvidiaProductAsync(client, model, cancellationToken);
        var osId = await FindNvidiaOsAsync(client, cancellationToken);
        if (seriesId is null || productId is null || osId is null)
            return [];

        var query = new UriBuilder("https://gfwsl.geforce.com/services_toolkit/services/com/nvidia/services/AjaxDriverService.php")
        {
            Query = $"func=DriverManualLookup&psid={Uri.EscapeDataString(seriesId)}&pfid={Uri.EscapeDataString(productId)}&osID={Uri.EscapeDataString(osId)}&languageCode=1033&beta=0&isWHQL=1&dltype=-1&dch=1&upCRD=0&qnf=0&sort1=0&numberOfResults=20",
        };
        var json = await client.GetStringAsync(query.Uri, cancellationToken);
        return ParseNvidiaDrivers(json, model, Vendors[0].CatalogUri);
    }

    private static async Task<(string? Series, string? Product)> FindNvidiaProductAsync(
        HttpClient client, string model, CancellationToken cancellationToken)
    {
        var seriesDocument = XDocument.Parse(await client.GetStringAsync(
            "https://www.nvidia.com/Download/API/lookupValueSearch.aspx?TypeID=2", cancellationToken));
        var cleanModel = NormalizeModel(model);
        var generation = NvidiaGeneration(cleanModel);
        var series = seriesDocument.Descendants()
            .Where(element => element.Name.LocalName == "LookupValue")
            .Select(element => new
            {
                Name = ChildValue(element, "Name"),
                Value = ChildValue(element, "Value"),
            })
            .Where(item => item.Name is not null && item.Value is not null &&
                           item.Name.Contains("GeForce", StringComparison.OrdinalIgnoreCase) &&
                           (generation is null || item.Name.Contains(generation, StringComparison.OrdinalIgnoreCase)) &&
                           item.Name.Contains("Notebook", StringComparison.OrdinalIgnoreCase) ==
                           cleanModel.Contains("laptop", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault();
        if (series is null)
            return (null, null);

        var products = XDocument.Parse(await client.GetStringAsync(
            $"https://www.nvidia.com/Download/API/lookupValueSearch.aspx?TypeID=3&ParentID={Uri.EscapeDataString(series.Value!)}",
            cancellationToken));
        var product = products.Descendants()
            .Where(element => element.Name.LocalName == "LookupValue")
            .Select(element => new { Name = ChildValue(element, "Name"), Value = ChildValue(element, "Value") })
            .Where(item => item.Name is not null && item.Value is not null)
            .OrderByDescending(item => string.Equals(NormalizeModel(item.Name!), cleanModel, StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault(item =>
                string.Equals(NormalizeModel(item.Name!), cleanModel, StringComparison.OrdinalIgnoreCase) ||
                NormalizeModel(item.Name!).Contains(cleanModel, StringComparison.OrdinalIgnoreCase));

        return (series.Value, product?.Value);
    }

    private static async Task<string?> FindNvidiaOsAsync(HttpClient client, CancellationToken cancellationToken)
    {
        var document = XDocument.Parse(await client.GetStringAsync(
            "https://www.nvidia.com/Download/API/lookupValueSearch.aspx?TypeID=4", cancellationToken));
        var target = Environment.OSVersion.Version.Build >= 22000 ? "Windows 11" : "Windows 10";
        return document.Descendants()
            .Where(element => element.Name.LocalName == "LookupValue")
            .Where(element => (ChildValue(element, "Name") ?? "")
                .Contains(target, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(element => (ChildValue(element, "Name") ?? "")
                .Contains("64", StringComparison.OrdinalIgnoreCase))
            .FirstOrDefault()
            is { } match ? ChildValue(match, "Value") : null;
    }

    private static async Task<IReadOnlyList<GpuDriverRelease>> FindAmdDriversAsync(
        HttpClient client, string model, CancellationToken cancellationToken)
    {
        var productUri = BuildAmdProductPage(model);
        var html = await client.GetStringAsync(productUri, cancellationToken);
        return ParseAmdDrivers(html, model, productUri);
    }

    private static async Task<IReadOnlyList<GpuDriverRelease>> FindIntelDriversAsync(
        HttpClient client, string hardwareId, CancellationToken cancellationToken)
    {
        if (!TryGetIntelDeviceId(hardwareId, out _))
            return [];

        using var response = await client.GetAsync(
            "https://dsadata.intel.com/data/en",
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > 10 * 1024 * 1024)
            return [];

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        using var stream = new MemoryStream(bytes);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);
        var entry = archive.GetEntry("software-configurations.json");
        if (entry is null || entry.Length is <= 0 or > 25 * 1024 * 1024)
            return [];

        using var reader = new StreamReader(entry.Open());
        return ParseIntelCatalog(await reader.ReadToEndAsync(cancellationToken), hardwareId, Vendors[2].CatalogUri);
    }

    private static Uri BuildAmdProductPage(string model)
    {
        var rx = Regex.Match(model, @"RX\s*(\d{4})\s*(XTX|XT)?", RegexOptions.IgnoreCase);
        if (!rx.Success)
            return Vendors[1].CatalogUri;

        var number = rx.Groups[1].Value;
        var variant = rx.Groups[2].Value;
        var family = number[..1] switch
        {
            "9" => "9000",
            "8" => "8000",
            "7" => "7000",
            "6" => "6000",
            "5" => "5000",
            _ => "",
        };
        if (family.Length == 0)
            return Vendors[1].CatalogUri;

        var modelSlug = $"amd-radeon-rx-{number}{(variant.Length == 0 ? "" : "-" + variant)}";
        var url = $"https://www.amd.com/en/support/downloads/drivers.html/graphics/radeon-rx/radeon-rx-{family}-series/{modelSlug}.html";
        return new Uri(url);
    }

    private static string NormalizeModel(string name) =>
        Regex.Replace(name, @"^(?:(?:NVIDIA|GeForce)\s+)+", "", RegexOptions.IgnoreCase).Trim();

    private static string? NvidiaGeneration(string name)
    {
        var match = Regex.Match(name, @"(?:RTX|GTX)\s*(\d{2})", RegexOptions.IgnoreCase);
        return match.Success ? match.Value : null;
    }

    private static string? ChildValue(XElement element, string name) =>
        element.Elements().FirstOrDefault(child => child.Name.LocalName == name)?.Value;

    private static bool TryGetIntelDeviceId(string hardwareId, out string deviceId)
    {
        var match = IntelDevicePattern().Match(hardwareId ?? "");
        deviceId = match.Success ? match.Groups[1].Value.ToUpperInvariant() : "";
        return match.Success;
    }

    private static bool SupportsIntelDevice(JsonElement entry, string deviceId)
    {
        var components = FindProperty(entry, "Components");
        if (components.ValueKind != JsonValueKind.Array)
            return false;

        foreach (var component in components.EnumerateArray())
        {
            if (!string.Equals(GetString(component, "Category"), "Graphics", StringComparison.OrdinalIgnoreCase))
                continue;
            var values = FindProperty(component, "DetectionValues");
            if (values.ValueKind == JsonValueKind.Array &&
                values.EnumerateArray().Any(value =>
                {
                    var detection = value.GetString() ?? "";
                    var prefix = $"VEN_8086&DEV_{deviceId}";
                    return detection.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) &&
                           (detection.Length == prefix.Length || detection[prefix.Length] == '&');
                }))
                return true;
        }
        return false;
    }

    private static bool SupportsWindows64(JsonElement file)
    {
        var systems = FindProperty(file, "OperatingSystems");
        if (systems.ValueKind != JsonValueKind.Array)
            return false;
        var target = Environment.OSVersion.Version.Build >= 22000 ? "windows-11-" : "windows-10-";
        return systems.EnumerateArray().Any(value =>
        {
            var os = value.GetString() ?? "";
            return os.StartsWith(target, StringComparison.OrdinalIgnoreCase) &&
                   os.EndsWith("-64", StringComparison.OrdinalIgnoreCase);
        });
    }

    private static bool TryValidateDownloadUrl(string? raw, string vendorDomain, out Uri uri)
    {
        uri = null!;
        return Uri.TryCreate(raw, UriKind.Absolute, out uri) &&
               uri.Scheme == Uri.UriSchemeHttps &&
               IsDomainOrSubdomain(uri.Host, vendorDomain) &&
               AllowedExtensions.Contains(Path.GetExtension(uri.LocalPath), StringComparer.OrdinalIgnoreCase);
    }

    private static bool IsOfficialHost(string host) =>
        IsDomainOrSubdomain(host, "nvidia.com") ||
        IsDomainOrSubdomain(host, "amd.com") ||
        IsDomainOrSubdomain(host, "intel.com");

    private static bool IsDomainOrSubdomain(string host, string domain) =>
        host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase);

    private static string? VersionFromFile(Uri uri)
    {
        var match = VersionPattern().Match(Path.GetFileName(uri.LocalPath));
        return match.Success ? match.Groups[1].Value : null;
    }

    private static Version ParseVersion(string value)
    {
        var normalized = Regex.Match(value, @"\d+(?:\.\d+){1,3}").Value;
        return Version.TryParse(normalized, out var version) ? version : new Version(0, 0);
    }

    private static JsonElement FindProperty(JsonElement element, string name)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return property.Value;
            }
        }
        return default;
    }

    private static string? GetString(JsonElement element, string name)
    {
        var property = FindProperty(element, name);
        return property.ValueKind == JsonValueKind.String ? property.GetString() : null;
    }

    private static bool IsTrue(JsonElement element) => element.ValueKind == JsonValueKind.True;

    [GeneratedRegex(@"VEN_8086&DEV_([0-9A-F]{4})", RegexOptions.IgnoreCase)]
    private static partial Regex IntelDevicePattern();

    [GeneratedRegex(@"(\d{2,4}\.\d+(?:\.\d+)?)")]
    private static partial Regex VersionPattern();
}

namespace TubaWinUi3.Services;

public sealed record GpuDriverVendor(string Name, Uri CatalogUri);

public static class GpuDriverCatalogService
{
    private static readonly GpuDriverVendor[] Vendors =
    [
        new("NVIDIA", new Uri("https://www.nvidia.com/Download/index.aspx?lang=cn")),
        new("AMD", new Uri("https://www.amd.com/en/support/download/drivers.html")),
        new("Intel", new Uri("https://www.intel.com/content/www/us/en/download-center/home.html")),
    ];

    private static readonly string[] AllowedExtensions = [".exe", ".zip"];

    public static IReadOnlyList<GpuDriverVendor> GetVendors() => Vendors;

    public static string GetDownloadDirectory() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        "Downloads", "图吧工具箱", "显卡驱动");

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

    private static bool IsOfficialHost(string host) =>
        IsDomainOrSubdomain(host, "nvidia.com") ||
        IsDomainOrSubdomain(host, "nvidia.cn") ||
        IsDomainOrSubdomain(host, "amd.com") ||
        IsDomainOrSubdomain(host, "intel.com");

    private static bool IsDomainOrSubdomain(string host, string domain) =>
        host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase);
}

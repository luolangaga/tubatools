using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

public class GpuDriverCatalogTests
{
    [Fact]
    public void GetVendors_ReturnsOfficialCatalogPages()
    {
        var vendors = GpuDriverCatalogService.GetVendors();

        Assert.Equal(new[] { "NVIDIA", "AMD", "Intel" }, vendors.Select(vendor => vendor.Name));
        Assert.All(vendors, vendor =>
        {
            Assert.Equal(Uri.UriSchemeHttps, vendor.CatalogUri.Scheme);
            Assert.True(new[] { "nvidia.com", "amd.com", "intel.com" }.Any(domain =>
                vendor.CatalogUri.Host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
                vendor.CatalogUri.Host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase)));
        });
    }

    [Theory]
    [InlineData("https://download.nvidia.com/driver.exe")]
    [InlineData("https://drivers.amd.com/driver.zip")]
    [InlineData("https://cdrdv2.intel.com/driver.exe")]
    public void TryGetDownloadDestination_AcceptsOfficialDriverFiles(string uri)
    {
        var destination = Path.Combine(Path.GetTempPath(), "GpuDriverCatalogTests");

        Assert.True(GpuDriverCatalogService.TryGetDownloadDestination(
            uri, "driver.exe", destination, out var path));
        Assert.Equal(destination, Path.GetDirectoryName(path));
        Assert.Equal(".exe", Path.GetExtension(path));
    }

    [Fact]
    public void ParseNvidiaDrivers_ExtractsVersionAndOfficialDirectUrl()
    {
        const string response = """
            {
              "IDS": [
                {
                  "downloadInfo": {
                    "Version": "591.86",
                    "ReleaseDateTime": "2026-10-01",
                    "DownloadURL": "https://us.download.nvidia.com/Windows/591.86/591.86-desktop.exe"
                  }
                }
              ]
            }
            """;

        var results = GpuDriverCatalogService.ParseNvidiaDrivers(
            response, "GeForce RTX 5090", new Uri("https://www.nvidia.com/Download/index.aspx"));

        var release = Assert.Single(results);
        Assert.Equal("591.86", release.Version);
        Assert.Equal("2026-10-01", release.ReleaseDate);
        Assert.Equal("https://us.download.nvidia.com/Windows/591.86/591.86-desktop.exe", release.DownloadUri.AbsoluteUri);
    }

    [Fact]
    public void ParseNvidiaDrivers_RejectsNonVendorLinks()
    {
        const string response = """
            {"IDS":[{"downloadInfo":{"Version":"1.0","DownloadURL":"https://evil.example/driver.exe"}}]}
            """;

        Assert.Empty(GpuDriverCatalogService.ParseNvidiaDrivers(
            response, "GeForce RTX 5090", new Uri("https://www.nvidia.com/Download/index.aspx")));
    }

    [Fact]
    public void ParseAmdDrivers_ExtractsDirectInstallerLinksAndVersions()
    {
        const string html = """
            <html><body>
              <a href="https://drivers.amd.com/drivers/whql-amd-software-adrenalin-edition-26.5.2-fullinstall-260514.exe">Full package</a>
              <a href="https://example.org/whql-amd-software-adrenalin-edition-27.1.0.exe">Fake package</a>
            </body></html>
            """;

        var results = GpuDriverCatalogService.ParseAmdDrivers(
            html, "Radeon RX 9070 XT", new Uri("https://www.amd.com/en/support"));

        var release = Assert.Single(results);
        Assert.Equal("26.5.2", release.Version);
        Assert.Equal("whql-amd-software-adrenalin-edition-26.5.2-fullinstall-260514.exe", release.FileName);
        Assert.Equal("drivers.amd.com", release.DownloadUri.Host);
    }

    [Fact]
    public void ParseIntelCatalog_UsesMatchingGraphicsDeviceAndOfficialExe()
    {
        const string catalog = """
            [
              {
                "Name": "Intel Graphics Driver",
                "Version": "32.0.101.7088 WHQL",
                "DisplayReleaseDate": "2026-06-22",
                "IsBeta": false,
                "Files": [{
                  "Url": "https://downloadmirror.intel.com/101/gfx_win_101.7088.exe",
                  "OperatingSystems": ["windows-11-24h2-64"]
                }],
                "Components": [{
                  "Category": "Graphics",
                  "DetectionValues": ["VEN_8086&DEV_9A49&SUBSYS_00000000"]
                }]
              }
            ]
            """;

        var results = GpuDriverCatalogService.ParseIntelCatalog(
            catalog, @"PCI\VEN_8086&DEV_9A49&SUBSYS_12345678", new Uri("https://www.intel.com/support"));

        var release = Assert.Single(results);
        Assert.Equal("32.0.101.7088 WHQL", release.Version);
        Assert.Equal("https://downloadmirror.intel.com/101/gfx_win_101.7088.exe", release.DownloadUri.AbsoluteUri);
    }

    [Theory]
    [InlineData("http://download.nvidia.com/driver.exe", "driver.exe")]
    [InlineData("https://nvidia.com.example.org/driver.exe", "driver.exe")]
    [InlineData("https://example.org/driver.exe", "driver.exe")]
    [InlineData("https://drivers.amd.com/readme.txt", "readme.txt")]
    public void TryGetDownloadDestination_RejectsUntrustedOrNonInstallerDownloads(string uri, string fileName)
    {
        Assert.False(GpuDriverCatalogService.TryGetDownloadDestination(
            uri, fileName, Path.GetTempPath(), out _));
    }

    [Fact]
    public void TryGetDownloadDestination_UsesOnlySuggestedFileName()
    {
        var directory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var suggestedPath = Path.Combine(Path.GetTempPath(), "outside.exe");

        Assert.True(GpuDriverCatalogService.TryGetDownloadDestination(
            "https://download.nvidia.com/driver.exe", suggestedPath, directory, out var destination));
        Assert.Equal(Path.Combine(directory, "outside.exe"), destination);
    }
}

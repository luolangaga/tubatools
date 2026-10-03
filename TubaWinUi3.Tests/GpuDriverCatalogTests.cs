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

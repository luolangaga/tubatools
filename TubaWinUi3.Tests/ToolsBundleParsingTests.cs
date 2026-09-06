using System.Text.Json;
using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

public class ToolsBundleParsingTests
{
    private static JsonElement Parse(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.Clone();
    }

    [Fact]
    public void ScanReleasesForTools_LatestReleaseHasToolsZip_ReturnsIt()
    {
        var json = """
            [
              {
                "tag_name": "v2.5.0",
                "prerelease": false,
                "draft": false,
                "assets": [
                  { "name": "TubaWinUi3_Setup_x64.exe", "size": 100 },
                  { "name": "Tools.zip", "size": 2048, "browser_download_url": "https://example.com/v2.5.0/Tools.zip" }
                ]
              }
            ]
            """;

        var result = ToolsBundleService.ScanReleasesForTools(Parse(json));

        Assert.NotNull(result);
        Assert.Equal("2.5.0", result!.Version);
        Assert.Equal("https://example.com/v2.5.0/Tools.zip", result!.FullUrl);
        Assert.Equal(2048, result!.FullSize);
        // 旧发行版未附带精简包：精简版不可选
        Assert.Null(result!.LiteUrl);
        Assert.Equal(0, result!.LiteSize);
    }

    [Fact]
    public void ScanReleasesForTools_LatestReleaseHasBothZips_CapturesLiteAsset()
    {
        var json = """
            [
              {
                "tag_name": "v2.5.0",
                "prerelease": false,
                "draft": false,
                "assets": [
                  { "name": "Tools.zip", "size": 2048, "browser_download_url": "https://example.com/v2.5.0/Tools.zip" },
                  { "name": "Tools_Lite.zip", "size": 512, "browser_download_url": "https://example.com/v2.5.0/Tools_Lite.zip" }
                ]
              }
            ]
            """;

        var result = ToolsBundleService.ScanReleasesForTools(Parse(json));

        Assert.NotNull(result);
        Assert.Equal("2.5.0", result!.Version);
        Assert.Equal("https://example.com/v2.5.0/Tools.zip", result!.FullUrl);
        Assert.Equal("https://example.com/v2.5.0/Tools_Lite.zip", result!.LiteUrl);
        Assert.Equal(512, result!.LiteSize);
    }

    [Fact]
    public void ScanReleasesForTools_LatestReleaseWithoutToolsZip_FallsBackToOlderRelease()
    {
        // 最新版只有安装包（纯应用更新），Tools.zip 在更早的 v2.4.0 上
        var json = """
            [
              {
                "tag_name": "v2.5.0",
                "prerelease": false,
                "draft": false,
                "assets": [
                  { "name": "TubaWinUi3_Setup_x64.exe", "size": 100 }
                ]
              },
              {
                "tag_name": "v2.4.0",
                "prerelease": false,
                "draft": false,
                "assets": [
                  { "name": "Tools.zip", "size": 4096, "browser_download_url": "https://example.com/v2.4.0/Tools.zip" }
                ]
              }
            ]
            """;

        var result = ToolsBundleService.ScanReleasesForTools(Parse(json));

        Assert.NotNull(result);
        Assert.Equal("2.4.0", result!.Version);
        Assert.Equal("https://example.com/v2.4.0/Tools.zip", result!.FullUrl);
        Assert.Equal(4096, result!.FullSize);
    }

    [Fact]
    public void ScanReleasesForTools_SkipsPrereleaseAndDraftWithToolsZip()
    {
        // 预发布和草稿即使带 Tools.zip 也不参与（与 /releases/latest 语义一致）
        var json = """
            [
              {
                "tag_name": "v2.6.0-beta1",
                "prerelease": true,
                "draft": false,
                "assets": [
                  { "name": "Tools.zip", "size": 1, "browser_download_url": "https://example.com/beta/Tools.zip" }
                ]
              },
              {
                "tag_name": "v2.5.0",
                "prerelease": false,
                "draft": true,
                "assets": [
                  { "name": "Tools.zip", "size": 2, "browser_download_url": "https://example.com/draft/Tools.zip" }
                ]
              },
              {
                "tag_name": "v2.4.0",
                "prerelease": false,
                "draft": false,
                "assets": [
                  { "name": "Tools.zip", "size": 4096, "browser_download_url": "https://example.com/v2.4.0/Tools.zip" }
                ]
              }
            ]
            """;

        var result = ToolsBundleService.ScanReleasesForTools(Parse(json));

        Assert.NotNull(result);
        Assert.Equal("2.4.0", result!.Version);
        Assert.Equal("https://example.com/v2.4.0/Tools.zip", result!.FullUrl);
    }

    [Fact]
    public void ScanReleasesForTools_NoReleaseHasToolsZip_ReturnsNull()
    {
        var json = """
            [
              {
                "tag_name": "v2.5.0",
                "prerelease": false,
                "draft": false,
                "assets": [
                  { "name": "TubaWinUi3_Setup_x64.exe", "size": 100 }
                ]
              },
              {
                "tag_name": "v2.4.0",
                "prerelease": false,
                "draft": false,
                "assets": []
              }
            ]
            """;

        Assert.Null(ToolsBundleService.ScanReleasesForTools(Parse(json)));
    }

    [Fact]
    public void ScanReleasesForTools_NonArrayJson_ReturnsNull()
    {
        Assert.Null(ToolsBundleService.ScanReleasesForTools(Parse("{\"tag_name\":\"v1.0\"}")));
    }

    [Fact]
    public void ScanReleasesForTools_MultipleNoToolsReleasesInARow_FallsThrough()
    {
        // 连续多个发行版都没有 Tools.zip，一路回退到第一个带 Tools.zip 的
        var json = """
            [
              { "tag_name": "v3.2.0", "prerelease": false, "draft": false, "assets": [ { "name": "x.zip", "browser_download_url": "u" } ] },
              { "tag_name": "v3.1.0", "prerelease": false, "draft": false, "assets": [ { "name": "y.zip", "browser_download_url": "u" } ] },
              { "tag_name": "v3.0.0", "prerelease": false, "draft": false, "assets": [ { "name": "Tools.zip", "browser_download_url": "https://example.com/v3.0.0/Tools.zip", "size": 123 } ] }
            ]
            """;

        var result = ToolsBundleService.ScanReleasesForTools(Parse(json));

        Assert.NotNull(result);
        Assert.Equal("3.0.0", result!.Version);
        Assert.Equal("https://example.com/v3.0.0/Tools.zip", result!.FullUrl);
        Assert.Equal(123, result!.FullSize);
    }
}

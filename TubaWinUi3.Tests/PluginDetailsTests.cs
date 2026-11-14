using System.IO.Compression;
using System.Text;
using TubaWinUi3.Services.Plugins;
using Xunit;

namespace TubaWinUi3.Tests;

/// <summary>
/// 详情页只读 nupkg 内的 nuspec + README，不请求 nuget.org 的网页渲染结果。
/// 这些用例锁住 nuspec 字段映射、README 定位和 Markdown 清洗规则。
/// </summary>
public sealed class PluginDetailsTests
{
    private const string Nuspec = """
        <?xml version="1.0" encoding="utf-8"?>
        <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
          <metadata>
            <id>Test.Plugin</id>
            <version>1.2.3</version>
            <title>Test Plugin</title>
            <authors>Alice</authors>
            <projectUrl>https://example.com/project</projectUrl>
            <repository type="git" url="https://github.com/example/test" />
            <license type="expression">MIT</license>
            <readme>README.md</readme>
            <tags>Tubatools demo</tags>
          </metadata>
        </package>
        """;

    [Fact]
    public void ReadArchive_MapsNuspecFieldsAndReadsDeclaredReadme()
    {
        using var stream = BuildZip(
            ("Test.Plugin.nuspec", Nuspec),
            ("README.md", """
                ---
                title: ignored front matter
                ---
                # Test Plugin

                ![banner](https://example.com/banner.png)

                Some **bold** text and <https://example.com/auto>.

                <a href="https://example.com/docs">Docs</a>
                """));
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        var details = NuGetPackageDetails.ReadArchive(archive);

        Assert.NotNull(details);
        Assert.Equal("https://example.com/project", details.ProjectUrl);
        Assert.Equal("https://github.com/example/test", details.RepositoryUrl);
        Assert.Equal("git", details.RepositoryType);
        Assert.Equal("MIT", details.License);
        Assert.Equal("Tubatools demo", details.Tags);
        Assert.Equal("Test Plugin", details.Title);
        Assert.True(details.HasLinks);
        Assert.Contains("Some **bold** text", details.Readme);
        Assert.Contains("<https://example.com/auto>", details.Readme);
        Assert.Contains("[Docs](https://example.com/docs)", details.Readme);
        Assert.DoesNotContain("banner", details.Readme);
        Assert.DoesNotContain("ignored front matter", details.Readme);
        Assert.DoesNotContain("# Test Plugin", details.Readme);
    }

    [Fact]
    public void ReadArchive_WithoutReadmeReturnsLinksOnly()
    {
        var nuspec = Nuspec.Replace("<readme>README.md</readme>", "");
        using var stream = BuildZip(("Test.Plugin.nuspec", nuspec));
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        var details = NuGetPackageDetails.ReadArchive(archive);

        Assert.NotNull(details);
        Assert.Equal("", details.Readme);
        Assert.True(details.HasLinks);
        Assert.Equal("MIT", details.License);
    }

    [Fact]
    public void ReadArchive_DecodesUtf16Readme()
    {
        var utf16Readme = "# 测试插件\n\n这是 UTF-16 编码的说明。";
        using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            WriteEntry(archive, "Test.Plugin.nuspec", Nuspec, new UTF8Encoding(false));
            WriteEntry(archive, "README.md", utf16Readme, Encoding.Unicode);
        }
        stream.Position = 0;
        using var readArchive = new ZipArchive(stream, ZipArchiveMode.Read);

        var details = NuGetPackageDetails.ReadArchive(readArchive);

        Assert.NotNull(details);
        Assert.Contains("这是 UTF-16 编码的说明。", details.Readme);
    }

    [Fact]
    public void ReadArchive_RejectsMissingNuspec()
    {
        using var stream = BuildZip(("README.md", "# no nuspec"));
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read);

        Assert.Null(NuGetPackageDetails.ReadArchive(archive));
    }

    [Fact]
    public void CleanReadme_RemovesHtmlImagesAndBadgeDefinitions()
    {
        var cleaned = NuGetPackageDetails.CleanReadme("""
            <!-- hidden -->
            # Title
            <p align="center"><img src="https://example.com/logo.png"></p>
            [![build](https://img.shields.io/badge.svg)](https://ci.example.com)
            [badge]: https://example.com/badge.svg
            Plain text with <https://example.com/auto> link.
            <script>alert(1)</script>
            """);

        Assert.DoesNotContain("# Title", cleaned);
        Assert.DoesNotContain("<img", cleaned);
        Assert.DoesNotContain("badge.svg", cleaned);
        Assert.DoesNotContain("<script", cleaned);
        Assert.Contains("<https://example.com/auto>", cleaned);
        Assert.Contains("Plain text", cleaned);
    }

    private static MemoryStream BuildZip(params (string Name, string Content)[] entries)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                WriteEntry(archive, name, content, new UTF8Encoding(false));
            }
        }
        stream.Position = 0;
        return stream;
    }

    private static void WriteEntry(ZipArchive archive, string name, string content, Encoding encoding)
    {
        var entry = archive.CreateEntry(name);
        using var writer = new StreamWriter(entry.Open(), encoding);
        writer.Write(content);
    }
}

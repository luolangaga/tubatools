using System.IO.Compression;
using TubaWinUi3.Services.Plugins;

namespace TubaWinUi3.Tests;

/// <summary>
/// 插件商店的未安装预览：从包内 tubatools.json + icon 取真名字和真图标，
/// 只走 Range，不默认下载整包。
/// </summary>
public sealed class PluginMarketPreviewTests
{
    private const string Svg = "<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"32\" height=\"32\"><rect width=\"32\" height=\"32\"/></svg>";

    [Fact]
    public void Preview_ReadsNameAndIcon_FromManifest()
    {
        var bytes = BuildPackage(new()
        {
            ["tubatools.json"] = """
                {"apiVersion":2,"assembly":"plugin/Test.dll","entryType":"Test.Entry","name":"测试插件","description":"样例","author":"作者","icon":"assets/icon.svg"}
                """,
            ["assets/icon.svg"] = Svg
        });
        var preview = Read(bytes);
        Assert.NotNull(preview);
        Assert.Equal("测试插件", preview!.Name);
        Assert.Equal("作者", preview.Author);
        Assert.Equal("assets/icon.svg", preview.IconEntry);
        Assert.Equal(".svg", preview.IconExtension);
        Assert.NotNull(preview.IconBytes);
        Assert.Equal(Svg, System.Text.Encoding.UTF8.GetString(preview.IconBytes!));
    }

    [Fact]
    public void Preview_OnlyReadsSmallRanges_NotWholePackage()
    {
        // 包里塞一个 1 MB 的无关条目：预览只能取到清单/图标附近的字节。
        var bytes = BuildPackage(new()
        {
            ["tubatools.json"] = """{"name":"测试插件","icon":"assets/icon.svg"}""",
            ["assets/icon.svg"] = Svg,
            ["plugin/payload.bin"] = new string('x', 1024 * 1024)
        });
        var source = new MemoryRangeSource(bytes);
        using var archive = Open(source);
        var preview = NuGetPackagePreview.ReadArchive(archive);
        Assert.Equal("测试插件", preview!.Name);
        Assert.True(source.TotalRequested < 200 * 1024,
            $"预览应只取少量区间，实际取了 {source.TotalRequested} 字节（包大小 {bytes.Length}）");
        Assert.All(source.Requests, r => Assert.True(r.End - r.Start + 1 <= 16 * 1024));
    }

    [Fact]
    public void Preview_MissingManifest_IsIgnored()
    {
        var bytes = BuildPackage(new() { ["plugin/Other.dll"] = "x" });
        Assert.Null(Read(bytes));
    }

    [Fact]
    public void Preview_RejectsIconPathOutsidePackage()
    {
        var preview = Read(BuildPackage(new()
        {
            ["tubatools.json"] = """{"name":"测试插件","icon":"../outside.svg"}""",
            ["assets/icon.svg"] = Svg
        }));
        Assert.Equal("测试插件", preview!.Name);
        Assert.Null(preview.IconBytes);
    }

    [Fact]
    public void Preview_RejectsNonSvgPayload()
    {
        var preview = Read(BuildPackage(new()
        {
            ["tubatools.json"] = """{"name":"测试插件","icon":"assets/icon.svg"}""",
            ["assets/icon.svg"] = "<html></html>"
        }));
        Assert.NotNull(preview);
        Assert.Null(preview!.IconBytes);
    }

    [Fact]
    public void Preview_WithoutName_KeepsEmptyName()
    {
        var preview = Read(BuildPackage(new() { ["tubatools.json"] = """{"icon":"assets/icon.svg"}""" }));
        Assert.NotNull(preview);
        Assert.Equal("", preview!.Name);
    }

    private static PackagePreview? Read(byte[] bytes)
    {
        using var archive = Open(new MemoryRangeSource(bytes));
        return NuGetPackagePreview.ReadArchive(archive);
    }

    private static ZipArchive Open(MemoryRangeSource source) =>
        new(new RangeReadStream(source), ZipArchiveMode.Read);

    private static byte[] BuildPackage(Dictionary<string, string> entries)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, text) in entries)
            {
                using var writer = new StreamWriter(zip.CreateEntry(name).Open());
                writer.Write(text);
            }
        }
        return buffer.ToArray();
    }

    /// <summary>本地字节冒充远程 Range 源：记录请求，用来证明预览不会拉整包。</summary>
    private sealed class MemoryRangeSource(byte[] bytes) : IRangeSource
    {
        public List<(long Start, long End)> Requests { get; } = [];
        public long TotalRequested { get; private set; }
        public long Length => bytes.Length;
        public byte[] Read(long start, long end)
        {
            Requests.Add((start, end));
            start = Math.Clamp(start, 0, bytes.Length - 1);
            end = Math.Clamp(end, start, bytes.Length - 1);
            var slice = bytes.AsSpan((int)start, (int)(end - start + 1)).ToArray();
            TotalRequested += slice.Length;
            return slice;
        }
    }
}

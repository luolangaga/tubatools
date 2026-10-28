using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;

namespace TubaWinUi3.Services;

/// <summary>
/// WinGet 官方源（cdn.winget.microsoft.com/cache）的 HTTP 访问层。
/// 该地址就是本机 winget 的默认源：索引包 source2.msix、包级 versionData.mszyml、
/// 合并清单 manifests/... 均为公开 HTTPS 资源，无需安装 winget 即可读取。
/// 统一走 IPv4 优先的 HttpClient（国内到国际 CDN 的 IPv6 路径常被静默丢弃）。
/// </summary>
internal static class WingetSourceClient
{
    internal const string BaseUrl = "https://cdn.winget.microsoft.com/cache/";

    private static readonly HttpClient Http = HttpClientFactory.CreateIpv4Preferred(TimeSpan.FromSeconds(30));
    private static readonly HttpClient HeavyHttp = HttpClientFactory.CreateIpv4Preferred(TimeSpan.FromMinutes(2));

    /// <summary>小文件的 Last-Modified（用于判断源索引是否需要重新下载）；失败返回 null。</summary>
    internal static async Task<string?> GetLastModifiedAsync(string relativeUrl, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, BaseUrl + relativeUrl);
            using var response = await Http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
            return response.IsSuccessStatusCode
                ? response.Content.Headers.LastModified?.ToUniversalTime().ToString("R")
                : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>下载源索引包（source2.msix）并取出其中的 Public/index.db。</summary>
    internal static async Task<byte[]> DownloadIndexAsync(CancellationToken ct)
    {
        using var response = await HeavyHttp.GetAsync(BaseUrl + "source2.msix",
            HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        await using var msix = new MemoryStream();
        await using (var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false))
            await stream.CopyToAsync(msix, ct).ConfigureAwait(false);

        msix.Position = 0;
        using var archive = new ZipArchive(msix, ZipArchiveMode.Read);
        var entry = archive.Entries.FirstOrDefault(e =>
            e.FullName.Replace('\\', '/').EndsWith("Public/index.db", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("WinGet 源索引包结构异常（缺少 Public/index.db）。");

        await using var source = entry.Open();
        await using var buffer = new MemoryStream(entry.Length > 0 ? (int)entry.Length : 0);
        await source.CopyToAsync(buffer, ct).ConfigureAwait(false);
        return buffer.ToArray();
    }

    /// <summary>下载并校验 SHA-256（源里的每个清单/版本数据都带哈希）。</summary>
    internal static async Task<byte[]> GetVerifiedAsync(string relativeUrl, string expectedSha256,
        CancellationToken ct)
    {
        using var response = await Http.GetAsync(BaseUrl + relativeUrl,
            HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var bytes = await response.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
        var actual = Convert.ToHexString(SHA256.HashData(bytes));
        if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"WinGet 源文件校验失败：{relativeUrl}");

        return bytes;
    }
}

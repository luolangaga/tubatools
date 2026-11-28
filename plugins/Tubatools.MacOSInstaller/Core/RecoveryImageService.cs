using System.Net;
using System.Net.Http.Headers;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;

namespace Tubatools.MacOSInstaller.Core;

/// <summary>
/// Apple 恢复镜像下载：会话 cookie → RecoveryImage 查询 → 下载 dmg/chunklist → 校验 chunklist 与分块 SHA-256。
/// 协议与 chunklist 格式参考 OpenCore 的公开实现；不包含任何 Apple 系统文件。
/// </summary>
internal sealed class RecoveryImageService
{
    private const string AppleEfiRomKey =
        "C3E748CAD9CD384329E10E25A91E43E1A762FF529ADE578C935BDDF9B13F2179" +
        "D4855E6FC89E9E29CA12517D17DFA1EDCE0BEBF0EA7B461FFE61D94E2BDF72C1" +
        "96F89ACD3536B644064014DAE25A15DB6BB0852ECBD120916318D1CCDEA3C84C" +
        "92ED743FC176D0BACA920D3FCF3158AFF731F88CE0623182A8ED67E650515F75" +
        "745909F07D415F55FC15A35654D118C55A462D37A3ACDA08612F3F3F6571761E" +
        "FCCBCC299AEE99B3A4FD6212CCFFF5EF37A2C334E871191F7E1C31960E010A54" +
        "E86FA3F62E6D6905E1CD57732410A3EB0C6B4DEFDABE9F59BF1618758C751CD5" +
        "6CEF851D1C0EAA1C558E37AC108DA9089863D20E2E7E4BF475EC66FE6B3EFDCF";

    private const string UserAgent = "InternetRecovery/1.0";
    private const string MlbZero = "00000000000000000";

    public static async Task<RecoveryDownloadResult> DownloadAsync(
        MacTarget target,
        string destination,
        Action<string> log,
        Action<string, double?> progress,
        string? proxy,
        CancellationToken ct)
    {
        var result = new RecoveryDownloadResult { Directory = destination };
        try
        {
            Directory.CreateDirectory(destination);
            string chunkPath = Path.Combine(destination, "BaseSystem.chunklist");
            string dmgPath = Path.Combine(destination, "BaseSystem.dmg");
            string partPath = dmgPath + ".part";

            var handler = new HttpClientHandler { AllowAutoRedirect = true };
            if (!string.IsNullOrWhiteSpace(proxy))
            {
                if (!Uri.TryCreate(proxy.Trim(), UriKind.Absolute, out Uri? proxyUri)
                    || (proxyUri.Scheme != "http" && proxyUri.Scheme != "https"))
                    throw new InvalidOperationException("代理地址无效，请填写 http://127.0.0.1:7890 这类地址。");
                handler.Proxy = new WebProxy(proxyUri);
                handler.UseProxy = true;
                log("使用代理下载：" + proxyUri.GetLeftPart(UriPartial.Authority));
            }
            using var client = new HttpClient(handler)
            {
                Timeout = Timeout.InfiniteTimeSpan
            };

            progress("连接 Apple 恢复服务器…", null);
            log("GET http://osrecovery.apple.com/");
            using var sessionRequest = new HttpRequestMessage(HttpMethod.Get, "http://osrecovery.apple.com/");
            sessionRequest.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
            sessionRequest.Headers.TryAddWithoutValidation("Connection", "close");
            using var sessionResponse = await client.SendAsync(sessionRequest, ct);
            string cookie = FindSessionCookie(sessionResponse);
            if (string.IsNullOrEmpty(cookie))
                throw new InvalidOperationException("Apple 恢复服务器没有返回会话 cookie。");

            progress("查询恢复镜像信息…", null);
            var post = new Dictionary<string, string>
            {
                ["cid"] = RandomHex(16),
                ["sn"] = MlbZero,
                ["bid"] = target.RecoveryBoardId,
                ["k"] = RandomHex(64),
                ["fg"] = RandomHex(64),
                ["os"] = "default"
            };
            string body = string.Join('\n', post.Select(kv => kv.Key + "=" + kv.Value));
            using var infoRequest = new HttpRequestMessage(HttpMethod.Post, "http://osrecovery.apple.com/InstallationPayload/RecoveryImage")
            {
                Content = new ByteArrayContent(Encoding.ASCII.GetBytes(body))
            };
            // Apple 只接受裸 text/plain；StringContent(..., "text/plain") 会附加 charset=us-ascii 并返回 400。
            infoRequest.Content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
            infoRequest.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
            infoRequest.Headers.TryAddWithoutValidation("Cookie", cookie);
            using var infoResponse = await client.SendAsync(infoRequest, ct);
            string infoText = await infoResponse.Content.ReadAsStringAsync(ct);
            var info = ParseInfo(infoText);

            string imageUrl = info["AU"];
            string imageToken = info["AT"];
            string chunkUrl = info["CU"];
            string chunkToken = info["CT"];

            await DownloadFileAsync(
                client, chunkUrl, "AssetToken=" + chunkToken, chunkPath,
                "chunklist", expectedTotal: null, allowResume: false, log, progress, ct);

            progress("校验 chunklist 签名…", null);
            List<(long Size, byte[] Hash)> chunks = ReadAndVerifyChunklist(chunkPath);
            long expectedDmgSize = SumChunkSizes(chunks);
            log($"chunklist 有效，共 {chunks.Count} 块，镜像大小约 {FormatSize(expectedDmgSize)}。");

            if (File.Exists(dmgPath))
            {
                if (File.Exists(partPath)) TryDelete(partPath);
                if (new FileInfo(dmgPath).Length == expectedDmgSize
                    && await VerifyDmgAsync(dmgPath, chunks, null, ct))
                {
                    result.Success = true;
                    result.DmgPath = dmgPath;
                    result.ChunklistPath = chunkPath;
                    result.Size = expectedDmgSize;
                    result.VerifiedChunks.AddRange(chunks.Select((c, i) => $"#{i + 1} {c.Size} bytes"));
                    progress($"恢复镜像已存在且校验通过（{FormatSize(expectedDmgSize)}）", 1.0);
                    log("复用已缓存且校验通过的恢复镜像。");
                    return result;
                }
                log("缓存中的 BaseSystem.dmg 校验失败，将重新下载。");
                TryDelete(dmgPath);
            }

            if (File.Exists(partPath))
            {
                long existing = new FileInfo(partPath).Length;
                if (existing > expectedDmgSize)
                {
                    TryDelete(partPath);
                }
                else if (existing == expectedDmgSize)
                {
                    if (await VerifyDmgAsync(partPath, chunks, null, ct))
                    {
                        File.Move(partPath, dmgPath, overwrite: true);
                        return Complete(result, dmgPath, chunkPath, expectedDmgSize, chunks, progress);
                    }
                    log("未完成的 BaseSystem.dmg.part 校验失败，将从头下载。");
                    TryDelete(partPath);
                }
                else
                {
                    log($"发现未完成的恢复镜像，已下载 {FormatSize(existing)}，将从断点继续。");
                }
            }

            await DownloadFileAsync(
                client, imageUrl, "AssetToken=" + imageToken, partPath,
                "BaseSystem.dmg", expectedTotal: expectedDmgSize, allowResume: true, log, progress, ct);

            progress("校验下载的恢复镜像…", null);
            if (new FileInfo(partPath).Length != expectedDmgSize)
                throw new InvalidOperationException("下载完成，但文件大小与 chunklist 不一致。");
            if (!await VerifyDmgAsync(partPath, chunks, progress, ct))
            {
                TryDelete(partPath);
                throw new InvalidOperationException("BaseSystem.dmg 分块哈希校验失败，文件已损坏，请重试。");
            }

            File.Move(partPath, dmgPath, overwrite: true);
            return Complete(result, dmgPath, chunkPath, expectedDmgSize, chunks, progress);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
            progress("", null);
            log("恢复镜像下载失败：" + ex.Message + "（已下载的部分会保留，下次可继续。）");
            return result;
        }
    }

    /// <summary>离线校验缓存，不访问网络。返回 true 表示 dmg 与 chunklist 均完整。</summary>
    public static async Task<bool> VerifyCacheAsync(MacTarget target, string destination, CancellationToken ct)
    {
        string dmgPath = Path.Combine(destination, "BaseSystem.dmg");
        string chunkPath = Path.Combine(destination, "BaseSystem.chunklist");
        if (!File.Exists(dmgPath) || !File.Exists(chunkPath)) return false;
        try
        {
            return await Task.Run(async () =>
            {
                List<(long Size, byte[] Hash)> chunks = ReadAndVerifyChunklist(chunkPath);
                long expected = SumChunkSizes(chunks);
                return new FileInfo(dmgPath).Length == expected
                    && await VerifyDmgAsync(dmgPath, chunks, null, ct);
            }, ct);
        }
        catch
        {
            return false;
        }
    }

    private static RecoveryDownloadResult Complete(
        RecoveryDownloadResult result,
        string dmgPath,
        string chunkPath,
        long size,
        List<(long Size, byte[] Hash)> chunks,
        Action<string, double?> progress)
    {
        result.Success = true;
        result.DmgPath = dmgPath;
        result.ChunklistPath = chunkPath;
        result.Size = size;
        result.VerifiedChunks.AddRange(chunks.Select((c, i) => $"#{i + 1} {c.Size} bytes"));
        progress($"恢复镜像就绪（{FormatSize(size)}）", 1.0);
        return result;
    }

    private static async Task DownloadFileAsync(
        HttpClient client,
        string url,
        string cookie,
        string path,
        string label,
        long? expectedTotal,
        bool allowResume,
        Action<string> log,
        Action<string, double?> progress,
        CancellationToken ct)
    {
        // Apple CDN 对非零偏移的 Range 请求会返回 206 头但立刻断流（实测），
        // 因此续传失败时必须丢掉 .part 并从 0 重下，避免永远卡在坏断点上。
        for (int attempt = 0; attempt < 2; attempt++)
        {
            long existing = allowResume && attempt == 0 && File.Exists(path)
                ? new FileInfo(path).Length
                : 0;
            if (expectedTotal is > 0 && existing == expectedTotal)
            {
                progress($"{label} 已下载完整（{FormatSize(existing)}）", 1.0);
                return;
            }
            if (expectedTotal is > 0 && existing > expectedTotal)
            {
                TryDelete(path);
                existing = 0;
            }
            if (attempt == 1) TryDelete(path);

            log($"下载 {label}：{url}");
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
            request.Headers.TryAddWithoutValidation("Cookie", cookie);
            if (existing > 0)
                request.Headers.Range = new RangeHeaderValue(existing, null);

            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (response.StatusCode == HttpStatusCode.RequestedRangeNotSatisfiable && existing > 0)
            {
                log($"{label} 的断点续传范围无效，将从 0 重新下载。");
                TryDelete(path);
                continue;
            }
            response.EnsureSuccessStatusCode();

            bool resumed = existing > 0 && response.StatusCode == HttpStatusCode.PartialContent;
            if (!resumed) existing = 0;

            long? total = expectedTotal
                ?? response.Content.Headers.ContentRange?.Length
                ?? (resumed
                    ? existing + response.Content.Headers.ContentLength
                    : response.Content.Headers.ContentLength);
            if (total is > 0 && total.Value > 128L * 1024 * 1024 * 1024)
                throw new InvalidOperationException($"{label} 的大小异常：{FormatSize(total.Value)}。");

            FileMode mode = resumed ? FileMode.Append : FileMode.Create;
            long done = existing;
            bool resumeLost = false;
            try
            {
                await using var source = await response.Content.ReadAsStreamAsync(ct);
                await using var target = new FileStream(path, mode, FileAccess.Write, FileShare.None, 1024 * 1024, FileOptions.SequentialScan);
                byte[] buffer = new byte[1024 * 1024];
                while (true)
                {
                    int read = await source.ReadAsync(buffer, ct);
                    if (read <= 0) break;
                    await target.WriteAsync(buffer.AsMemory(0, read), ct);
                    done += read;
                    double? fraction = total is > 0 ? Math.Clamp((double)done / total.Value, 0, 1) : null;
                    progress($"{label}  {FormatSize(done)} / {(total is > 0 ? FormatSize(total.Value) : "?")}", fraction);
                }
                // FileStream 默认 1 MB 缓冲；不先 Flush，小文件（chunklist）在句柄关闭前长度仍是 0。
                await target.FlushAsync(ct);
            }
            catch (IOException)
            {
                if (!resumed) throw;
                resumeLost = true;
            }

            if (resumeLost || (resumed && total is > 0 && done == existing))
            {
                log($"{label} 的断点未被服务器正确响应，将从 0 重新下载。");
                TryDelete(path);
                continue;
            }

            if (total is > 0)
            {
                long actual = new FileInfo(path).Length;
                if (actual != total.Value)
                {
                    if (resumed)
                    {
                        log($"{label} 的断点续传结果不完整，将从 0 重新下载。");
                        TryDelete(path);
                        continue;
                    }
                    throw new InvalidOperationException($"{label} 下载不完整：期望 {FormatSize(total.Value)}，实际 {FormatSize(actual)}。");
                }
            }
            return;
        }

        throw new InvalidOperationException($"{label} 无法完成下载，请稍后重试。");
    }

    private static async Task<bool> VerifyDmgAsync(
        string path,
        List<(long Size, byte[] Hash)> chunks,
        Action<string, double?>? progress,
        CancellationToken ct)
    {
        long expectedSize = SumChunkSizes(chunks);
        var info = new FileInfo(path);
        if (!info.Exists || info.Length != expectedSize) return false;

        await using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1024 * 1024, FileOptions.SequentialScan);
        for (int i = 0; i < chunks.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            (long size, byte[] expected) = chunks[i];
            if (size < 0 || size > 64L * 1024 * 1024)
                throw new InvalidOperationException($"chunklist 第 {i + 1} 块大小异常：{size}");
            byte[] buffer = new byte[(int)size];
            int read = await fs.ReadAtLeastAsync(buffer, buffer.Length, throwOnEndOfStream: false, ct);
            if (read != buffer.Length)
                throw new InvalidOperationException($"BaseSystem.dmg 在 chunk {i + 1} 处提前结束。");
            byte[] actual = SHA256.HashData(buffer);
            if (!CryptographicOperations.FixedTimeEquals(actual, expected))
                return false;
            if (progress is not null && (i % 64 == 0 || i == chunks.Count - 1))
                progress($"校验分块 {i + 1}/{chunks.Count}", chunks.Count == 0 ? null : (double)(i + 1) / chunks.Count);
        }
        if (fs.ReadByte() != -1)
            return false;
        return true;
    }

    private static string FindSessionCookie(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Set-Cookie", out IEnumerable<string>? values))
            return "";
        foreach (string header in values)
        {
            foreach (string part in header.Split(';'))
            {
                string trimmed = part.Trim();
                if (trimmed.StartsWith("session=", StringComparison.OrdinalIgnoreCase))
                    return trimmed;
            }
        }
        return "";
    }

    private static Dictionary<string, string> ParseInfo(string text)
    {
        var info = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string line in text.Split('\n'))
        {
            int idx = line.IndexOf(": ", StringComparison.Ordinal);
            if (idx <= 0) continue;
            info[line[..idx].Trim()] = line[(idx + 2)..].Trim();
        }
        foreach (string required in new[] { "AP", "AU", "AH", "AT", "CU", "CH", "CT" })
        {
            if (!info.ContainsKey(required))
                throw new InvalidOperationException($"Apple 恢复服务器返回缺少 {required} 字段。");
        }
        return info;
    }

    private static List<(long Size, byte[] Hash)> ReadAndVerifyChunklist(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        using var hash = SHA256.Create();
        byte[] header = new byte[0x24];
        fs.ReadExactly(header);

        if (Encoding.ASCII.GetString(header, 0, 4) != "CNKL")
            throw new InvalidOperationException("chunklist 魔术字不是 CNKL。");
        uint headerSize = BitConverter.ToUInt32(header, 4);
        byte fileVersion = header[8];
        byte chunkMethod = header[9];
        byte signatureMethod = header[10];
        ulong chunkCount = BitConverter.ToUInt64(header, 12);
        ulong chunkOffset = BitConverter.ToUInt64(header, 20);
        ulong signatureOffset = BitConverter.ToUInt64(header, 28);

        if (headerSize != 0x24 || fileVersion != 1 || chunkMethod != 1)
            throw new InvalidOperationException("chunklist 版本或分块方法不受支持。");
        if (chunkCount == 0 || chunkCount > int.MaxValue || chunkOffset != 0x24
            || signatureOffset != chunkOffset + 0x24UL * chunkCount)
            throw new InvalidOperationException("chunklist 头部字段不一致。");

        hash.TransformBlock(header, 0, header.Length, null, 0);
        var chunks = new List<(long, byte[])>((int)chunkCount);
        byte[] descriptor = new byte[0x24];
        for (ulong i = 0; i < chunkCount; i++)
        {
            fs.ReadExactly(descriptor);
            hash.TransformBlock(descriptor, 0, descriptor.Length, null, 0);
            uint size = BitConverter.ToUInt32(descriptor, 0);
            byte[] digest = descriptor.AsSpan(4, 32).ToArray();
            chunks.Add((size, digest));
        }
        hash.TransformFinalBlock([], 0, 0);
        byte[] listDigest = hash.Hash!;

        if (signatureMethod == 1)
        {
            byte[] signatureBytes = new byte[256];
            fs.ReadExactly(signatureBytes);
            BigInteger signature = new(signatureBytes, isUnsigned: true, isBigEndian: false);
            // PKCS#1 v1.5：00 01 FF…FF 00 30 31 30 0d 06 09 60 86 48 01 65 03 04 02 01 05 00 04 20 <SHA-256>
            var block = new byte[255];
            block[0] = 0x01;
            for (int i = 1; i <= 202; i++) block[i] = 0xFF;
            block[203] = 0x00;
            byte[] digestInfo = Convert.FromHexString("3031300d060960864801650304020105000420");
            Buffer.BlockCopy(digestInfo, 0, block, 204, digestInfo.Length);
            Buffer.BlockCopy(listDigest, 0, block, 223, listDigest.Length);
            BigInteger expected = new(block, isUnsigned: true, isBigEndian: true);
            BigInteger modulus = new(Convert.FromHexString(AppleEfiRomKey), isUnsigned: true, isBigEndian: true);
            if (BigInteger.ModPow(signature, 0x10001, modulus) != expected)
                throw new InvalidOperationException("chunklist 的 Apple 签名无效。");
        }
        else if (signatureMethod == 2)
        {
            byte[] inline = new byte[32];
            fs.ReadExactly(inline);
            if (!CryptographicOperations.FixedTimeEquals(inline, listDigest))
                throw new InvalidOperationException("chunklist 的摘要不匹配。");
        }
        else
        {
            throw new InvalidOperationException("未知的 chunklist 签名方式。");
        }

        if (fs.ReadByte() != -1)
            throw new InvalidOperationException("chunklist 末尾有多余数据。");
        return chunks;
    }

    private static long SumChunkSizes(List<(long Size, byte[] Hash)> chunks)
    {
        long total = 0;
        foreach ((long size, _) in chunks)
        {
            if (size < 0 || size > int.MaxValue)
                throw new InvalidOperationException("chunk 大小超出支持范围。");
            total = checked(total + size);
            if (total > 128L * 1024 * 1024 * 1024)
                throw new InvalidOperationException("恢复镜像总大小异常。");
        }
        return total;
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private static string RandomHex(int length)
    {
        const string chars = "0123456789ABCDEF";
        var sb = new StringBuilder(length);
        for (int i = 0; i < length; i++) sb.Append(chars[Random.Shared.Next(chars.Length)]);
        return sb.ToString();
    }

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1L << 30) return $"{bytes / (double)(1L << 30):0.00} GB";
        if (bytes >= 1L << 20) return $"{bytes / (double)(1L << 20):0.0} MB";
        if (bytes >= 1L << 10) return $"{bytes / 1024.0:0.0} KB";
        return bytes + " B";
    }
}

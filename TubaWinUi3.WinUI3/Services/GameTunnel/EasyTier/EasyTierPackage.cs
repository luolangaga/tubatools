using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using TubaWinUi3.Models;

namespace TubaWinUi3.Services;

/// <summary>
/// 联机组件 EasyTier（github.com/EasyTier/EasyTier，LGPL-3.0）：
/// 首次联机时按需下载官方发行包，每个文件都与写死在代码里的 SHA-256 比对，
/// 任何镜像都无法替换成修改过的程序。文件缺失或校验失败会在下次联机时重新下载。
/// </summary>
public static class EasyTierPackage
{
    public const string Version = "2.6.4";

    private const string CoreFile = "easytier-core.exe";
    private const string CliFile = "easytier-cli.exe";
    private const string PacketFile = "Packet.dll";

    private static readonly string[] Files = [CoreFile, CliFile, PacketFile];

    /// <summary>官方发行包内三个文件的 SHA-256，顺序与 <see cref="Files"/> 一致。</summary>
    private static readonly Dictionary<string, string[]> Hashes = new(StringComparer.OrdinalIgnoreCase)
    {
        ["x86_64"] =
        [
            "da7eb2d24b5416f3d3407636949e964a0750e3f9dc53a828cb6799a57ead445d",
            "d8783e851e944b44a9b71b39fd02f227ec0a2a82b3165c55ead5dd32dcde53a1",
            "c7c03a87eac7243ccbe331554624b18803010b740e311fc8cfddb573096eacac",
        ],
        ["arm64"] =
        [
            "da912e2befd4c59c81228c0dac7e19f0190aa4221475117108ad2f0045b3065d",
            "295852b6d68ab5c675437f19f37e975e90d41814ec76eabc135d3eb367bfccd0",
            "fe56bf638c1f05751a4f36377d71f48fb7f43446e92475eda32dc0f4f2ebd60c",
        ],
        ["i686"] =
        [
            "657400c756aa4e2868b9505f669527733ba872dff3b8877cee24d7772616c76b",
            "e1d6a617e4cb3b9bf77c9d32616dace5d5715fabcbcb0f5f9969a98f3626d69a",
            "f0ab4f5457bd48958b72d6849148e1ece6dbc1059435dfe695051fc32b8cdc19",
        ],
    };

    private static readonly object Sync = new();
    private static bool _verified;
    private static DownloadItem? _installItem;

    /// <summary>与发行包架构后缀一致："x86_64" / "arm64" / "i686"。</summary>
    public static string Architecture => RuntimeInformation.ProcessArchitecture switch
    {
        System.Runtime.InteropServices.Architecture.Arm64 => "arm64",
        System.Runtime.InteropServices.Architecture.X86 => "i686",
        _ => "x86_64"
    };

    public static string InstallDirectory => Path.Combine(
        GameTunnelCatalog.DataDir, "EasyTier", Version + "-" + Architecture);

    public static string CorePath => Path.Combine(InstallDirectory, CoreFile);

    public static string CliPath => Path.Combine(InstallDirectory, CliFile);

    public static string AssetName(string architecture) => $"easytier-windows-{architecture}-v{Version}.zip";

    public static IReadOnlyList<string> FileNames => Files;

    public static string ExpectedHash(string architecture, string file)
        => Hashes[architecture][Array.IndexOf(Files, file)];

    /// <summary>三个文件都在且哈希正确（每次运行只校验一次）。</summary>
    public static bool IsReady
    {
        get
        {
            lock (Sync)
            {
                if (_verified) return _verified;
                _verified = Files.All(IsFileValid);
                return _verified;
            }
        }
    }

    /// <summary>强制重新校验（组件更新或清理后调用）。</summary>
    public static void Invalidate()
    {
        lock (Sync) _verified = false;
    }

    /// <summary>删除已下载的组件；下次联机时重新下载。</summary>
    public static void Uninstall()
    {
        lock (Sync) _verified = false;
        try
        {
            var root = Path.GetDirectoryName(InstallDirectory);
            if (root is not null && Directory.Exists(root)) Directory.Delete(root, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    /// <summary>
    /// 确保组件就绪：未安装时入全局下载队列，下载官方 zip 并只解出需要的三个文件。
    /// 进度约定：percent &lt; 0 表示当前阶段无法给出百分比（解包中）。
    /// </summary>
    public static async Task EnsureInstalledAsync(
        IProgress<(int Percent, string Message)>? progress, CancellationToken ct = default)
    {
        if (IsReady) return;

        var item = _installItem;
        if (item is null || item.State is DownloadItemState.Completed or DownloadItemState.Failed or DownloadItemState.Cancelled)
        {
            Directory.CreateDirectory(InstallDirectory);
            var architecture = Architecture;
            item = DownloadQueueService.EnqueueWithResolver(
                "EasyTier 联机组件",
                cancellation => ResolveSourceAsync(architecture, cancellation),
                InstallDirectory,
                new EasyTierZipProcessor(architecture),
                description: "游戏联机组件（免登录 P2P 虚拟网络）",
                glyph: "\uE968");
            _installItem = item;
        }

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        void Handler(object? sender, System.ComponentModel.PropertyChangedEventArgs args)
        {
            if (args.PropertyName != nameof(DownloadItem.State)) return;
            switch (item.State)
            {
                case DownloadItemState.Completed:
                    tcs.TrySetResult(true);
                    break;
                case DownloadItemState.Failed:
                    tcs.TrySetException(new InvalidOperationException(item.ErrorMessage ?? "联机组件下载失败"));
                    break;
                case DownloadItemState.Cancelled:
                    tcs.TrySetException(new OperationCanceledException());
                    break;
            }
        }

        item.PropertyChanged += Handler;
        try
        {
            while (!tcs.Task.IsCompleted)
            {
                ct.ThrowIfCancellationRequested();
                await Task.WhenAny(tcs.Task, Task.Delay(200, ct)).ConfigureAwait(false);
                if (tcs.Task.IsCompleted) break;

                if (item.State == DownloadItemState.Processing)
                {
                    progress?.Report((-1, item.ProcessingStatus ?? "正在解包联机组件…"));
                    continue;
                }

                if (item.Progress is { } p)
                {
                    var detail = p.TotalBytes > 0
                        ? $"{DownloadQueueService.FormatSize(p.BytesReceived)} / {DownloadQueueService.FormatSize(p.TotalBytes)}"
                        : DownloadQueueService.FormatSize(p.BytesReceived);
                    var speed = p.SpeedMbps > 0 ? $" · {DownloadQueueService.FormatSpeed(p.SpeedMbps)}" : "";
                    progress?.Report(((int)p.Percentage, $"正在下载联机组件 · {detail}{speed}"));
                }
            }

            await tcs.Task.ConfigureAwait(false);
            Invalidate();
            if (!IsReady)
                throw new InvalidOperationException("联机组件解包后校验失败，请重试或检查安全软件是否拦截了下载。");
            progress?.Report((100, "联机组件就绪"));
        }
        finally
        {
            item.PropertyChanged -= Handler;
        }
    }

    /// <summary>下载源，按顺序探测：Gitee 官方镜像 → GitHub → 两个 GitHub 加速服务。</summary>
    internal static IEnumerable<string> SourceUrls(string architecture)
    {
        var asset = AssetName(architecture);
        var github = $"https://github.com/EasyTier/EasyTier/releases/download/v{Version}/{asset}";
        yield return $"https://gitee.com/easytier/EasyTier/releases/download/v{Version}/{asset}";
        yield return github;
        yield return "https://ghfast.top/" + github;
        yield return "https://gh-proxy.com/" + github;
    }

    private static async Task<ResolvedDownloadUrl> ResolveSourceAsync(string architecture, CancellationToken ct)
    {
        var asset = AssetName(architecture);
        foreach (var url in SourceUrls(architecture))
        {
            var size = await ProbeAsync(url, ct).ConfigureAwait(false);
            if (size is not null)
                return new ResolvedDownloadUrl(url, asset, size.Value);
        }
        throw new IOException("联机组件下载源都不可达，请检查网络或代理后重试。");
    }

    private static async Task<long?> ProbeAsync(string url, CancellationToken ct)
    {
        try
        {
            using var client = ProxyService.CreateClient(TimeSpan.FromSeconds(15));
            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            // 只取开头 4KB：既避免整包下载，也能确认拿到的确实是 zip（错误页/登录页都以 PK 之外开头）。
            request.Headers.Range = new System.Net.Http.Headers.RangeHeaderValue(0, 4095);
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;

            var length = response.Content.Headers.ContentRange?.Length
                         ?? response.Content.Headers.ContentLength
                         ?? 0;

            await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var header = new byte[4];
            var read = 0;
            while (read < header.Length)
            {
                var n = await stream.ReadAsync(header.AsMemory(read, header.Length - read), ct).ConfigureAwait(false);
                if (n == 0) break;
                read += n;
            }
            if (read < 2 || header[0] != (byte)'P' || header[1] != (byte)'K') return null;

            return length > 0 ? length : 0;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>zip 内条目匹配：允许带目录前缀（官方包里文件在根目录，仍做兼容）。</summary>
    internal static bool MatchesEntry(string entryName, string file)
    {
        var name = entryName.Replace('\\', '/');
        return name.EndsWith("/" + file, StringComparison.OrdinalIgnoreCase)
               || string.Equals(name, file, StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsFileValid(string file)
    {
        var path = Path.Combine(InstallDirectory, file);
        try
        {
            if (!File.Exists(path)) return false;
            return string.Equals(ComputeHash(path), ExpectedHash(Architecture, file), StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    internal static string ComputeHash(string path)
    {
        using var sha = SHA256.Create();
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Convert.ToHexString(sha.ComputeHash(stream)).ToLowerInvariant();
    }

    /// <summary>下载后处理：只解出三个文件，逐个校验哈希后原子落盘，最后删掉 zip。</summary>
    private sealed class EasyTierZipProcessor(string architecture) : IDownloadPostProcessor
    {
        public string DisplayName => "解包联机组件";

        public Task ExecuteAsync(string downloadedFilePath, string destinationPath,
            IProgress<string>? statusProgress, CancellationToken ct)
        {
            return Task.Run(() =>
            {
                ct.ThrowIfCancellationRequested();
                statusProgress?.Report("正在解包联机组件…");
                Directory.CreateDirectory(destinationPath);

                using (var zip = ZipFile.OpenRead(downloadedFilePath))
                {
                    foreach (var file in Files)
                    {
                        ct.ThrowIfCancellationRequested();
                        var entry = zip.Entries.FirstOrDefault(e => MatchesEntry(e.FullName, file))
                            ?? throw new InvalidDataException($"联机组件压缩包里缺少 {file}");
                        var temp = Path.Combine(destinationPath, file + ".tmp");
                        entry.ExtractToFile(temp, overwrite: true);

                        var expected = ExpectedHash(architecture, file);
                        var actual = ComputeHash(temp);
                        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
                        {
                            TryDelete(temp);
                            throw new InvalidDataException($"联机组件 {file} 校验失败（可能与官方发行包不一致）");
                        }

                        var target = Path.Combine(destinationPath, file);
                        TryDelete(target);
                        File.Move(temp, target);
                    }
                }

                TryDelete(downloadedFilePath);
                statusProgress?.Report("联机组件已就绪");
            }, ct);
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }
}

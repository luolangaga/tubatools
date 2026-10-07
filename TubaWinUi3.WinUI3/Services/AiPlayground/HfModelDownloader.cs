using TubaWinUi3.Models;

namespace TubaWinUi3.Services.AiPlayground;

/// <summary>
/// 模型下载：Hugging Face 多源解析（官方源 / hf-mirror 镜像，HEAD 探测取可用源与大小）
/// 并接入全局下载队列（断点续传 / 暂停 / 进度 / 并发与代理全部由队列负责）。
/// </summary>
public static class HfModelDownloader
{
    public const string OfficialHost = "huggingface.co";
    public const string MirrorHost = "hf-mirror.com";

    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(10);

    // ── 下载源设置 ─────────────────────────────────────────────────

    public static string GetSourceSetting() => AppSettings.Get("AiPlayground_DownloadSource") ?? "auto";

    public static void SetSourceSetting(string value) => AppSettings.Set("AiPlayground_DownloadSource", value);

    /// <summary>主机尝试顺序（auto = 镜像优先，国内直连体验最好）。</summary>
    public static IReadOnlyList<string> GetHostOrder() => HostOrderFor(GetSourceSetting());

    internal static IReadOnlyList<string> HostOrderFor(string source) => source switch
    {
        "official" => [OfficialHost, MirrorHost],
        "mirror" => [MirrorHost, OfficialHost],
        _ => [MirrorHost, OfficialHost],
    };

    public static string BuildFileUrl(string host, AiModelPreset preset, AiModelFile file)
    {
        var repoPath = preset.GetRepoPath(file);
        var encoded = string.Join('/', repoPath.Split('/').Select(Uri.EscapeDataString));
        return $"https://{host}/{preset.Repo}/resolve/{preset.Revision}/{encoded}";
    }

    // ── 解析（HEAD 探测）──────────────────────────────────────────

    /// <summary>
    /// 解析预设的全部文件：逐个文件并发 HEAD 多主机，取第一个成功者——不等待慢/挂掉的主机，
    /// 网络级失败（超时/连不上）的主机在本轮解析中跳过，避免每给出一个文件都重付一次超时。
    /// 失败抛 InvalidOperationException（含排障提示）。
    /// </summary>
    public static async Task<List<ResolvedDownloadUrl>> ResolveFilesAsync(
        AiModelPreset preset,
        IProgress<string>? status,
        CancellationToken ct)
    {
        var allHosts = GetHostOrder();
        var results = new List<ResolvedDownloadUrl>(preset.Files.Count);
        var unreachableHosts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        using var client = CreateProbeClient();
        for (int i = 0; i < preset.Files.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var file = preset.Files[i];
            status?.Report($"正在解析下载地址 {i + 1}/{preset.Files.Count}：{file.TargetName}");

            var hosts = allHosts.Where(h => !unreachableHosts.Contains(h)).ToList();
            if (hosts.Count == 0)
            {
                // 全部主机都被标记为不可达：重新尝试全部，避免一次偶发失败永久跳过
                hosts.AddRange(allHosts);
            }

            var resolved = await ResolveFirstAvailableAsync(
                host => ProbeAsync(client, host, preset, file, ct), hosts, unreachableHosts, ct);

            if (resolved is null)
            {
                throw new InvalidOperationException(
                    $"无法连接模型源（{preset.Repo} / {file.Path}）。" +
                    "请检查网络或代理设置；国内网络可尝试在「下载源」中切换官方源 / 镜像源。");
            }
            results.Add(resolved);
        }
        return results;
    }

    /// <summary>
    /// 并发探测候选主机，返回第一个成功者的结果（其余探测不再等待）；
    /// 网络级失败的主机记入 <paramref name="unreachableHosts"/>。
    /// </summary>
    internal static async Task<ResolvedDownloadUrl?> ResolveFirstAvailableAsync(
        Func<string, Task<ProbeResult>> probe,
        IReadOnlyList<string> hosts,
        ISet<string> unreachableHosts,
        CancellationToken ct)
    {
        var pending = hosts.Select(host => (Host: host, Probe: probe(host))).ToList();

        while (pending.Count > 0)
        {
            ct.ThrowIfCancellationRequested();

            var completed = await Task.WhenAny(pending.Select(p => p.Probe));
            var entry = pending.First(p => ReferenceEquals(p.Probe, completed));
            pending.Remove(entry);

            var result = await completed;
            if (result.HostUnreachable) unreachableHosts.Add(entry.Host);
            if (result.Url is not null) return result.Url;
        }

        ct.ThrowIfCancellationRequested();
        return null;
    }

    /// <summary>单个主机的 HEAD 探测。<c>HostUnreachable</c> = 网络级失败（区别于 404 等文件级失败）。</summary>
    internal readonly record struct ProbeResult(ResolvedDownloadUrl? Url, bool HostUnreachable);

    /// <summary>
    /// 探测用客户端：与下载链路一致走 IPv4 优先连接（CDN 域名解析出不可达 IPv6 时，
    /// 默认 handler 会挂满整个超时周期），并保留用户配置的代理。
    /// </summary>
    private static HttpClient CreateProbeClient()
    {
        var handler = HttpClientFactory.CreateIpv4PreferredHandler();
        if (ProxyService.GetWebProxy() is { } proxy)
        {
            handler.Proxy = proxy;
        }
        return new HttpClient(handler) { Timeout = ProbeTimeout };
    }

    private static async Task<ProbeResult> ProbeAsync(
        HttpClient client,
        string host,
        AiModelPreset preset,
        AiModelFile file,
        CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, BuildFileUrl(host, preset, file));
            using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode) return new ProbeResult(null, false);
            var size = response.Content.Headers.ContentLength ?? 0;
            return new ProbeResult(
                new ResolvedDownloadUrl(BuildFileUrl(host, preset, file), file.TargetName, size),
                false);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return new ProbeResult(null, false);
        }
        catch
        {
            return new ProbeResult(null, true);
        }
    }

    // ── 入队（全局下载队列）────────────────────────────────────────

    /// <summary>查找该模型的队列项（含已完成），用于避免重复入队 / 显示进度。</summary>
    public static DownloadItem? FindQueueItem(AiModelEntry entry)
    {
        var dir = AiModelLibrary.GetStoredModelDir(entry.Id);
        return DownloadQueueService.Queue
            .FirstOrDefault(item =>
                item.DestinationPath == dir &&
                item.DisplayName == entry.DisplayName);
    }

    /// <summary>把模型加入全局下载队列；已存在进行中/completed 项时直接返回该项。</summary>
    public static DownloadItem EnqueueModel(AiModelEntry entry, IProgress<string>? status)
    {
        var preset = entry.Preset
            ?? throw new InvalidOperationException("自定义导入的模型不能下载。");
        var dir = AiModelLibrary.GetStoredModelDir(preset.Id);
        Directory.CreateDirectory(dir);

        var existing = FindQueueItem(entry);
        if (existing is not null &&
            existing.State is not (DownloadItemState.Failed or DownloadItemState.Cancelled))
        {
            return existing;
        }

        return DownloadQueueService.EnqueueMultiFile(
            displayName: entry.DisplayName,
            multiFileResolver: ct => ResolveFilesAsync(preset, status, ct),
            destinationPath: dir,
            postProcessor: null,
            description: $"本地 AI 试炼场 · {preset.Task} · {preset.License}",
            glyph: "\uE950",
            tag: null);
    }
}

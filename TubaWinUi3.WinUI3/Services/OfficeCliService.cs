using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using TubaWinUi3.Models;

namespace TubaWinUi3.Services;

/// <summary>
/// OfficeCLI 渲染引擎（github.com/iOfficeAI/OfficeCLI，Apache 2.0）：
/// 单文件 33MB 自包含 .NET 程序，把 docx/xlsx/pptx 高保真渲染为分页 HTML
/// （自研排版引擎：页边距/字体度量/合并单元格/复选框/页眉页脚/水印），
/// 截图与打印走系统 WebView2，无任何额外依赖。下载/就绪模式对齐 FfmpegService。
/// </summary>
public static class OfficeCliService
{
    private const int ViewTimeoutSeconds = 180;

    /// <summary>GitHub 的 latest 直链不需要 API，作为 GitCode 不可用时的兜底（配合仓库惯用的 gh-proxy）。</summary>
    private const string ReleaseBase = "https://github.com/iOfficeAI/OfficeCLI/releases/latest/download";

    /// <summary>主下载源：作者自建的 GitCode 镜像 release（officecli-win-arm64.exe / officecli-win-x64.exe）。</summary>
    private const string GitCodeReleaseApi = "https://api.gitcode.com/api/v5/repos/luolangaga/tubatoolr/releases/12";

    public static string OfficeCliDir => Path.Combine(ConfigManager.GetDataDir(), "officecli");
    public static string ExePath => Path.Combine(OfficeCliDir, "officecli.exe");

    public static bool IsReady => File.Exists(ExePath);

    private static DownloadItem? _downloadItem;
    public static DownloadItem? DownloadItem => _downloadItem;

    // ══════════════ 下载 ══════════════

    public static DownloadItem EnsureOfficeCliViaQueue()
    {
        if (IsReady) return _downloadItem!;

        if (_downloadItem is not null && _downloadItem.State is
            DownloadItemState.Queued or DownloadItemState.Resolving or DownloadItemState.Downloading or DownloadItemState.Processing)
            return _downloadItem;

        Directory.CreateDirectory(OfficeCliDir);

        Func<CancellationToken, Task<ResolvedDownloadUrl>> urlResolver = async ct =>
        {
            // win-x64 固定存在；arm64 优先原生资产，缺失时回退 x64（Windows on ARM 的 x64 模拟可运行）
            var archTokens = RuntimeInformation.ProcessArchitecture switch
            {
                Architecture.Arm64 => new[] { "arm64", "x64" },
                Architecture.X86 => new[] { "x86", "x64" },
                _ => new[] { "x64" }
            };

            // 主源：GitCode 自建镜像。经 API 拿 browser_download_url，无需 HEAD 验证
            // （该端点对 HEAD 返回 401 假阴性，GET 会 302 到 file-cdn.gitcode.com 签名 CDN 正常下载）。
            try
            {
                using var client = ProxyService.CreateClient(TimeSpan.FromSeconds(20));
                var json = await client.GetStringAsync(GitCodeReleaseApi, ct);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("assets", out var assets))
                {
                    foreach (var arch in archTokens)
                    {
                        var assetName = $"officecli-win-{arch}.exe";
                        foreach (var asset in assets.EnumerateArray())
                        {
                            var name = asset.TryGetProperty("name", out var n) ? n.GetString() : null;
                            var url = asset.TryGetProperty("browser_download_url", out var u) ? u.GetString() : null;
                            if (string.Equals(name, assetName, StringComparison.OrdinalIgnoreCase)
                                && !string.IsNullOrEmpty(url))
                                return new ResolvedDownloadUrl(url, "officecli.exe", 0);
                        }
                    }
                }
            }
            catch { }

            // 兜底：GitHub latest 直链 + gh-proxy（这些端点 HEAD 探测可靠）
            foreach (var arch in archTokens)
            {
                var asset = $"officecli-win-{arch}.exe";
                var candidates = new[]
                {
                    $"https://gh-proxy.com/{ReleaseBase}/{asset}",
                    $"https://ghproxy.053000.xyz/{ReleaseBase}/{asset}",
                    $"{ReleaseBase}/{asset}",
                };
                foreach (var url in candidates)
                {
                    var length = await ProbeUrlAsync(url, ct);
                    if (length.HasValue)
                        return new ResolvedDownloadUrl(url, "officecli.exe", length.Value);
                }
            }
            return new ResolvedDownloadUrl($"https://gh-proxy.com/{ReleaseBase}/officecli-win-x64.exe", "officecli.exe", 0);
        };

        _downloadItem = DownloadQueueService.EnqueueWithResolver(
            "OfficeCLI",
            urlResolver,
            OfficeCliDir,
            postProcessor: null,
            description: "文档真实渲染引擎 (GitCode 镜像优先)",
            glyph: "\uE8A5");

        return _downloadItem;
    }

    public static async Task EnsureOfficeCliAsync(IProgress<(int percent, string message)>? progress = null)
    {
        if (IsReady) return;

        var item = EnsureOfficeCliViaQueue();

        var tcs = new TaskCompletionSource<bool>();
        void handler(object? s, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(DownloadItem.State))
            {
                if (item.State == DownloadItemState.Completed)
                    tcs.TrySetResult(true);
                else if (item.State == DownloadItemState.Failed)
                    tcs.TrySetException(new Exception(item.ErrorMessage ?? "下载失败"));
                else if (item.State == DownloadItemState.Cancelled)
                    tcs.TrySetException(new OperationCanceledException());
            }
        }
        item.PropertyChanged += handler;

        try
        {
            while (!tcs.Task.IsCompleted)
            {
                await Task.WhenAny(tcs.Task, Task.Delay(200));

                var p = item.Progress;
                if (p is not null)
                {
                    var pct = (int)p.Percentage;
                    var speed = p.SpeedMbps > 0 ? $" {DownloadQueueService.FormatSpeed(p.SpeedMbps)}" : "";
                    var eta = p.EstimatedRemaining.HasValue ? $" 剩余 {DownloadQueueService.FormatTime(p.EstimatedRemaining)}" : "";
                    var downloaded = DownloadQueueService.FormatSize(p.BytesReceived);
                    var total = p.TotalBytes > 0 ? $" / {DownloadQueueService.FormatSize(p.TotalBytes)}" : "";
                    progress?.Report((pct, $"正在下载 OfficeCLI 渲染引擎... {downloaded}{total}{speed}{eta}"));
                }

                if (tcs.Task.IsCompleted) break;
            }
            await tcs.Task;

            // 首次运行自检：officecli 会把自身复制到 %LOCALAPPDATA%/OfficeCli 并初始化配置
            var probe = await RunAsync("--version", TimeSpan.FromSeconds(60), CancellationToken.None);
            if (probe.ExitCode != 0)
                throw new InvalidOperationException($"OfficeCLI 自检失败：{probe.Stderr}");
            progress?.Report((100, "OfficeCLI 就绪"));
        }
        finally
        {
            item.PropertyChanged -= handler;
        }
    }

    private static async Task<long?> ProbeUrlAsync(string url, CancellationToken ct)
    {
        try
        {
            using var client = ProxyService.CreateClient(TimeSpan.FromSeconds(15));
            using var req = new HttpRequestMessage(HttpMethod.Head, url);
            using var resp = await client.SendAsync(req, ct);
            if (resp.IsSuccessStatusCode)
                return resp.Content.Headers.ContentLength ?? 0;
        }
        catch { }
        return null;
    }

    // ══════════════ 执行 ══════════════

    private sealed record RunResult(int ExitCode, string Stdout, string Stderr);

    private static async Task<RunResult> RunAsync(string arguments, TimeSpan timeout, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = ExePath,
            Arguments = arguments,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };
        using var proc = new Process { StartInfo = psi };
        proc.Start();
        var stdoutTask = proc.StandardOutput.ReadToEndAsync(ct);
        var stderrTask = proc.StandardError.ReadToEndAsync(ct);

        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(timeout);
        try
        {
            await proc.WaitForExitAsync(timeoutCts.Token);
        }
        catch (OperationCanceledException)
        {
            try { proc.Kill(entireProcessTree: true); } catch { }
            if (!ct.IsCancellationRequested)
                throw new TimeoutException($"OfficeCLI 执行超时（{timeout.TotalSeconds:N0} 秒）");
            throw;
        }

        return new RunResult(proc.ExitCode, await stdoutTask, await stderrTask);
    }

    /// <summary>docx/xlsx/pptx → 高保真分页 HTML（自研排版引擎）。pageRange 为 --page 原生语法（如 "1-3,5"），空 = 全部。</summary>
    public static async Task HtmlAsync(string sourcePath, string outHtmlPath, string? pageRange = null,
        IProgress<string>? progress = null, CancellationToken ct = default)
    {
        var outDir = Path.GetDirectoryName(outHtmlPath);
        if (!string.IsNullOrEmpty(outDir)) Directory.CreateDirectory(outDir);
        var sb = new StringBuilder($"view \"{sourcePath}\" html");
        if (!string.IsNullOrWhiteSpace(pageRange)) sb.Append(" --page \"").Append(pageRange).Append('"');
        sb.Append(" -o \"").Append(outHtmlPath).Append('"');
        var result = await RunAsync(sb.ToString(), TimeSpan.FromSeconds(ViewTimeoutSeconds), ct);
        if (!File.Exists(outHtmlPath) || new FileInfo(outHtmlPath).Length == 0)
        {
            var output = result.Stderr.Trim();
            var hint = output.Length == 0
                ? (result.Stdout.Trim().Length > 0 ? result.Stdout.Trim() : "")
                : output;
            throw new InvalidOperationException(
                $"OfficeCLI 渲染 {Path.GetFileName(sourcePath)} 未产生输出{(hint.Length > 0 ? $"：{hint[^Math.Min(hint.Length, 300)]..}" : "")}");
        }
    }

    /// <summary>OfficeCLI 页码超出范围的错误（消息里带 "total pages/slides: N" 时可解析出总页数）。</summary>
    public sealed class PageRangeException : Exception
    {
        public int? TotalPages { get; }
        public PageRangeException(string message, int? totalPages) : base(message) => TotalPages = totalPages;
    }

    internal static int? ParseTotalPages(string? output)
    {
        if (string.IsNullOrEmpty(output)) return null;
        var m = Regex.Match(output, @"total\s+\w+:\s*(\d+)", RegexOptions.IgnoreCase);
        return m.Success ? int.Parse(m.Groups[1].Value) : null;
    }

    /// <summary>
    /// 原生逐页截图（无头浏览器渲染，输出 PNG）。page 为 --page 原生语法（单页 "2" / 范围 "1-3,5"），空 = 第 1 页。
    /// screenshotWidth 对应 --screenshot-width（清晰度）；renderMode 对应 --render（auto/native/html，仅 docx/pptx）。
    /// </summary>
    public static async Task ScreenshotAsync(string sourcePath, string page, int screenshotWidth,
        string? renderMode, string outPngPath, CancellationToken ct = default)
    {
        var outDir = Path.GetDirectoryName(outPngPath);
        if (!string.IsNullOrEmpty(outDir)) Directory.CreateDirectory(outDir);

        var sb = new StringBuilder($"view \"{sourcePath}\" screenshot");
        if (!string.IsNullOrWhiteSpace(page)) sb.Append(" --page \"").Append(page).Append('"');
        sb.Append(" --screenshot-width ").Append(Math.Clamp(screenshotWidth, 400, 8000));
        if (!string.IsNullOrWhiteSpace(renderMode)) sb.Append(" --render ").Append(renderMode.Trim().ToLowerInvariant());
        sb.Append(" -o \"").Append(outPngPath).Append('"');

        var result = await RunAsync(sb.ToString(), TimeSpan.FromSeconds(ViewTimeoutSeconds), ct);
        if (!File.Exists(outPngPath) || new FileInfo(outPngPath).Length == 0)
        {
            var output = (result.Stderr + "\n" + result.Stdout).Trim();
            var total = ParseTotalPages(output);
            if (total.HasValue)
                throw new PageRangeException(output.Length > 300 ? output[..300] : output, total);
            var hint = output.Length > 0 ? output[^Math.Min(output.Length, 300)..] : "（无输出）";
            throw new InvalidOperationException($"OfficeCLI 截图 {Path.GetFileName(sourcePath)} 失败：{hint}");
        }
    }

    /// <summary>提取纯文本（docx 的 text 模式输出到 stdout）。</summary>
    public static async Task<string> TextAsync(string sourcePath, CancellationToken ct = default)
    {
        var result = await RunAsync($"view \"{sourcePath}\" text", TimeSpan.FromSeconds(ViewTimeoutSeconds), ct);
        if (result.ExitCode != 0 && string.IsNullOrWhiteSpace(result.Stdout))
            throw new InvalidOperationException($"OfficeCLI 文本提取失败：{result.Stderr.Trim()}");
        return result.Stdout;
    }

    // ══════════════ 管理 ══════════════

    public static string GetOfficeCliSize()
    {
        try
        {
            if (!File.Exists(ExePath)) return "0 B";
            return DownloadQueueService.FormatSize(new FileInfo(ExePath).Length);
        }
        catch { return "未知"; }
    }

    public static void DeleteOfficeCli()
    {
        try
        {
            if (Directory.Exists(OfficeCliDir))
                Directory.Delete(OfficeCliDir, true);
        }
        catch { }
    }
}

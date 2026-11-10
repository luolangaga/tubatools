using System.IO.Compression;
using System.Runtime.InteropServices;
using TubaWinUi3.Models;

namespace TubaWinUi3.Services;

public static class FfmpegService
{
    private static readonly string FfmpegDir = Path.Combine(ConfigManager.GetDataDir(), "ffmpeg");

    public static string FfmpegPath => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.Arm64 => Path.Combine(FfmpegDir, "ffmpeg-arm64.exe"),
        Architecture.X86 => Path.Combine(FfmpegDir, "ffmpeg-x86.exe"),
        _ => Path.Combine(FfmpegDir, "ffmpeg-x64.exe")
    };

    public static string FfprobePath => RuntimeInformation.ProcessArchitecture switch
    {
        Architecture.Arm64 => Path.Combine(FfmpegDir, "ffprobe-arm64.exe"),
        Architecture.X86 => Path.Combine(FfmpegDir, "ffprobe-x86.exe"),
        _ => Path.Combine(FfmpegDir, "ffprobe-x64.exe")
    };

    public static bool IsFfmpegReady => File.Exists(FfmpegPath);

    private static DownloadItem? _downloadItem;
    public static DownloadItem? DownloadItem => _downloadItem;

    public static DownloadItem EnsureFfmpegViaQueue()
    {
        if (IsFfmpegReady) return _downloadItem!;

        if (_downloadItem is not null && _downloadItem.State is
            DownloadItemState.Queued or DownloadItemState.Resolving or DownloadItemState.Downloading or DownloadItemState.Processing)
            return _downloadItem;

        Directory.CreateDirectory(FfmpegDir);

        var postProcessor = new FfmpegExtractProcessor(FfmpegPath, FfprobePath);
        Func<CancellationToken, Task<ResolvedDownloadUrl>> urlResolver = async ct =>
        {
            var asset = await GetTrustedFfmpegAsync(ct);
            postProcessor.ExpectedSha256 = asset.Sha256;
            return new ResolvedDownloadUrl(asset.Url, asset.Name, asset.Size);
        };

        _downloadItem = DownloadQueueService.EnqueueWithResolver(
            "FFmpeg",
            urlResolver,
            FfmpegDir,
            postProcessor,
            description: "格式转换核心组件（官方源 · SHA-256 校验）",
            glyph: "\uE8B2");

        return _downloadItem;
    }

    internal static async Task<TrustedReleaseAsset> GetTrustedFfmpegAsync(CancellationToken ct)
    {
        var suffix = RuntimeInformation.ProcessArchitecture == Architecture.Arm64
            ? "_portable_winarm64-clang-gpl.zip" : "_portable_win64-clang-gpl.zip";
        var assets = await TrustedDownloads.LatestAssetsAsync("jellyfin/jellyfin-ffmpeg", ct);
        return assets.FirstOrDefault(a => a.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("官方 FFmpeg 发布缺少适用的已校验组件，请稍后重试");
    }

    public static async Task EnsureFfmpegAsync(IProgress<(int percent, string message)>? progress = null)
    {
        if (IsFfmpegReady) return;

        var item = EnsureFfmpegViaQueue();

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
                    progress?.Report((pct, $"正在下载 FFmpeg... {downloaded}{total}{speed}{eta}"));
                }

                if (item.State == DownloadItemState.Processing && item.ProcessingStatus is not null)
                    progress?.Report((95, item.ProcessingStatus));

                if (tcs.Task.IsCompleted) break;
            }
            await tcs.Task;
            progress?.Report((100, "FFmpeg 就绪"));
        }
        finally
        {
            item.PropertyChanged -= handler;
        }
    }

    public static async Task<string> RunFfmpegAsync(string arguments, IProgress<(int percent, string message)>? progress = null, CancellationToken ct = default)
    {
        if (!IsFfmpegReady) throw new InvalidOperationException("FFmpeg 未就绪");

        // 提取第一个 -i 的输入文件，检查是否存在且非空
        var inputMatch = System.Text.RegularExpressions.Regex.Match(arguments, @"-i\s+""([^""]+)""");
        if (inputMatch.Success)
        {
            var inputPath = inputMatch.Groups[1].Value;
            if (!File.Exists(inputPath))
                throw new FileNotFoundException($"输入文件不存在：{inputPath}");
            if (new FileInfo(inputPath).Length == 0)
                throw new InvalidOperationException($"输入文件为空（0 字节），无法转换：{Path.GetFileName(inputPath)}");
        }

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = FfmpegPath,
            Arguments = arguments,
            CreateNoWindow = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = System.Text.Encoding.UTF8,
            StandardErrorEncoding = System.Text.Encoding.UTF8
        };

        using var proc = new System.Diagnostics.Process { StartInfo = psi };
        proc.Start();

        var stderrTask = proc.StandardError.ReadToEndAsync();
        var stdoutTask = proc.StandardOutput.ReadToEndAsync();

        await Task.WhenAll(stdoutTask, stderrTask, proc.WaitForExitAsync(ct));

        var stderr = await stderrTask;
        var stdout = await stdoutTask;

        if (proc.ExitCode != 0)
        {
            var exitCode = proc.ExitCode;
            var isCrash = FormatConvertPlanner.IsFfmpegCrash(exitCode);
            var hint = isCrash
                ? "\n\nFFmpeg 进程异常终止（崩溃），可能原因：\n" +
                  "① 源文件损坏或格式异常（尝试用其他播放器确认文件正常）\n" +
                  "② FFmpeg 二进制不完整（设置页 → 删除 FFmpeg 后重新下载）\n" +
                  "③ 系统缺少解码器依赖（安装 Visual C++ 运行库）"
                : "";
            throw new Exception($"FFmpeg 退出码 {exitCode}: {stderr}{hint}");
        }

        return stdout + stderr;
    }

    public static async Task<VideoFileInfo?> ProbeAsync(string filePath)
    {
        if (!File.Exists(FfprobePath) && !File.Exists(FfmpegPath)) return null;
        var probePath = File.Exists(FfprobePath) ? FfprobePath : FfmpegPath;
        var isFfprobe = probePath == FfprobePath;

        var args = isFfprobe
            ? $"-v quiet -print_format json -show_format -show_streams \"{filePath}\""
            : $"-i \"{filePath}\" -hide_banner";

        try
        {
            if (isFfprobe)
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = probePath,
                    Arguments = args,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                    StandardErrorEncoding = System.Text.Encoding.UTF8
                };

                using var proc = System.Diagnostics.Process.Start(psi)!;
                var json = await proc.StandardOutput.ReadToEndAsync();
                await proc.WaitForExitAsync();
                return ParseProbeJson(json);
            }
            else
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = probePath,
                    Arguments = args,
                    CreateNoWindow = true,
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = System.Text.Encoding.UTF8,
                    StandardErrorEncoding = System.Text.Encoding.UTF8
                };

                using var proc = System.Diagnostics.Process.Start(psi)!;
                var stderr = await proc.StandardError.ReadToEndAsync();
                await proc.WaitForExitAsync();
                return ParseFfmpegInfo(stderr);
            }
        }
        catch
        {
            return null;
        }
    }

    private static VideoFileInfo? ParseProbeJson(string json)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            var root = doc.RootElement;

            var info = new VideoFileInfo();

            if (root.TryGetProperty("format", out var fmt))
            {
                if (fmt.TryGetProperty("duration", out var dur) && double.TryParse(dur.GetString(), out var d))
                    info.Duration = TimeSpan.FromSeconds(d);
                if (fmt.TryGetProperty("size", out var sz) && long.TryParse(sz.GetString(), out var s))
                    info.FileSize = s;
                if (fmt.TryGetProperty("bit_rate", out var br) && long.TryParse(br.GetString(), out var b))
                    info.BitRate = b;
                if (fmt.TryGetProperty("format_name", out var fn))
                    info.Format = fn.GetString() ?? "";
            }

            if (root.TryGetProperty("streams", out var streams))
            {
                foreach (var st in streams.EnumerateArray())
                {
                    var codecType = st.TryGetProperty("codec_type", out var ct) ? ct.GetString() : "";
                    if (codecType == "video" && info.VideoCodec == null)
                    {
                        info.VideoCodec = st.TryGetProperty("codec_name", out var cn) ? cn.GetString() : null;
                        if (st.TryGetProperty("width", out var w)) info.Width = w.GetInt32();
                        if (st.TryGetProperty("height", out var h)) info.Height = h.GetInt32();
                        if (st.TryGetProperty("r_frame_rate", out var rfr))
                        {
                            var parts = rfr.GetString()?.Split('/');
                            if (parts?.Length == 2 && int.TryParse(parts[0], out var num) && int.TryParse(parts[1], out var den) && den > 0)
                                info.FrameRate = (double)num / den;
                        }
                    }
                    else if (codecType == "audio" && info.AudioCodec == null)
                    {
                        info.AudioCodec = st.TryGetProperty("codec_name", out var cn) ? cn.GetString() : null;
                        if (st.TryGetProperty("sample_rate", out var sr)) info.SampleRate = sr.GetInt32();
                        if (st.TryGetProperty("channels", out var ch)) info.Channels = ch.GetInt32();
                    }
                }
            }

            return info;
        }
        catch
        {
            return null;
        }
    }

    private static VideoFileInfo? ParseFfmpegInfo(string stderr)
    {
        try
        {
            var info = new VideoFileInfo();

            var durMatch = System.Text.RegularExpressions.Regex.Match(stderr, @"Duration:\s*(\d+):(\d+):(\d+)(?:\.(\d+))?");
            if (durMatch.Success)
            {
                var h = int.Parse(durMatch.Groups[1].Value);
                var m = int.Parse(durMatch.Groups[2].Value);
                var s = int.Parse(durMatch.Groups[3].Value);
                info.Duration = new TimeSpan(h, m, s);
            }

            var brMatch = System.Text.RegularExpressions.Regex.Match(stderr, @"bitrate:\s*(\d+)");
            if (brMatch.Success)
                info.BitRate = int.Parse(brMatch.Groups[1].Value) * 1000;

            var vidMatch = System.Text.RegularExpressions.Regex.Match(stderr, @"Video:\s*(\w+)");
            if (vidMatch.Success)
                info.VideoCodec = vidMatch.Groups[1].Value;

            var resMatch = System.Text.RegularExpressions.Regex.Match(stderr, @"(\d{3,5})x(\d{3,5})");
            if (resMatch.Success)
            {
                info.Width = int.Parse(resMatch.Groups[1].Value);
                info.Height = int.Parse(resMatch.Groups[2].Value);
            }

            var audMatch = System.Text.RegularExpressions.Regex.Match(stderr, @"Audio:\s*(\w+)");
            if (audMatch.Success)
                info.AudioCodec = audMatch.Groups[1].Value;

            return info;
        }
        catch
        {
            return null;
        }
    }

    public static void CleanFfmpeg()
    {
        try
        {
            if (Directory.Exists(FfmpegDir))
                Directory.Delete(FfmpegDir, true);
        }
        catch { }
    }

    public static void DeleteFfmpeg()
    {
        try
        {
            if (Directory.Exists(FfmpegDir))
                Directory.Delete(FfmpegDir, true);
        }
        catch { }
    }

    public static string GetFfmpegSize()
    {
        try
        {
            if (!Directory.Exists(FfmpegDir)) return "0 B";
            long size = 0;
            foreach (var f in Directory.GetFiles(FfmpegDir))
                try { size += new FileInfo(f).Length; } catch { }
            return FormatSize(size);
        }
        catch { return "未知"; }
    }

    private static string FormatSize(long bytes)
    {
        if (bytes >= 1L << 30) return $"{(double)bytes / (1L << 30):F2} GB";
        if (bytes >= 1L << 20) return $"{(double)bytes / (1L << 20):F1} MB";
        if (bytes >= 1L << 10) return $"{(double)bytes / (1L << 10):F1} KB";
        return $"{bytes} B";
    }
}

public class VideoFileInfo
{
    public TimeSpan Duration { get; set; }
    public long FileSize { get; set; }
    public long BitRate { get; set; }
    public string Format { get; set; } = "";
    public string? VideoCodec { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public double FrameRate { get; set; }
    public string? AudioCodec { get; set; }
    public int SampleRate { get; set; }
    public int Channels { get; set; }

    public string Resolution => Width > 0 && Height > 0 ? $"{Width}×{Height}" : "未知";
    public string DurationText => Duration > TimeSpan.Zero ? Duration.ToString(@"hh\:mm\:ss") : "未知";
    public string BitRateText => BitRate > 0 ? FormatBitRate(BitRate) : "未知";

    private static string FormatBitRate(long bps)
    {
        if (bps >= 1_000_000) return $"{(double)bps / 1_000_000:F1} Mbps";
        if (bps >= 1_000) return $"{(double)bps / 1_000:F0} Kbps";
        return $"{bps} bps";
    }
}

public enum VideoOperation
{
    ConvertFormat,
    Compress,
    ExtractAudio,
    Trim,
    ChangeSpeed,
    Resize,
    ExtractFrames,
    Rotate,
    RemoveAudio,
    AdjustVolume
}

public static class VideoOperationExtensions
{
    public static string DisplayName(this VideoOperation op) => op switch
    {
        VideoOperation.ConvertFormat => "格式转换",
        VideoOperation.Compress => "压缩视频",
        VideoOperation.ExtractAudio => "提取音频",
        VideoOperation.Trim => "裁剪片段",
        VideoOperation.ChangeSpeed => "调整速度",
        VideoOperation.Resize => "调整分辨率",
        VideoOperation.ExtractFrames => "提取帧",
        VideoOperation.Rotate => "旋转视频",
        VideoOperation.RemoveAudio => "移除音频",
        VideoOperation.AdjustVolume => "调整音量",
        _ => op.ToString()
    };

    public static string Glyph(this VideoOperation op) => op switch
    {
        VideoOperation.ConvertFormat => "\uE8AC",
        VideoOperation.Compress => "\uE710",
        VideoOperation.ExtractAudio => "\uEA69",
        VideoOperation.Trim => "\uE9A3",
        VideoOperation.ChangeSpeed => "\uEC4A",
        VideoOperation.Resize => "\uE739",
        VideoOperation.ExtractFrames => "\uEB9F",
        VideoOperation.Rotate => "\uE7AD",
        VideoOperation.RemoveAudio => "\uE7E8",
        VideoOperation.AdjustVolume => "\uE767",
        _ => "\uE712"
    };
}

public sealed class FfmpegExtractProcessor : IDownloadPostProcessor
{
    private readonly string _ffmpegDest;
    private readonly string _ffprobeDest;
    internal string? ExpectedSha256 { get; set; }

    public string DisplayName => "解压 FFmpeg";

    public FfmpegExtractProcessor(string ffmpegDest, string ffprobeDest)
    {
        _ffmpegDest = ffmpegDest;
        _ffprobeDest = ffprobeDest;
    }

    public async Task ExecuteAsync(string downloadedFilePath, string destinationPath,
        IProgress<string>? statusProgress, CancellationToken ct)
    {
        statusProgress?.Report("正在解压 FFmpeg...");

        var hash = ExpectedSha256 ?? (await FfmpegService.GetTrustedFfmpegAsync(ct)).Sha256;
        await using var archiveFile = new FileStream(downloadedFilePath, FileMode.Open, FileAccess.Read, FileShare.Read, 65536, true);
        await DownloadIntegrityPolicy.VerifyAsync(archiveFile, hash, Path.GetFileName(downloadedFilePath), ct, DownloadIntegrityDialog.ConfirmAsync);
        await Task.Run(() =>
        {
            using var archive = new ZipArchive(archiveFile, ZipArchiveMode.Read, leaveOpen: true);

            var ffmpegEntry = archive.Entries.FirstOrDefault(e =>
                    e.FullName.EndsWith("bin/ffmpeg.exe", StringComparison.OrdinalIgnoreCase))
                ?? archive.Entries.FirstOrDefault(e =>
                    e.FullName.Equals("ffmpeg.exe", StringComparison.OrdinalIgnoreCase))
                ?? archive.Entries.FirstOrDefault(e =>
                    e.FullName.EndsWith("/ffmpeg.exe", StringComparison.OrdinalIgnoreCase)
                    && !e.FullName.Contains("doc", StringComparison.OrdinalIgnoreCase));

            var ffprobeEntry = archive.Entries.FirstOrDefault(e =>
                    e.FullName.EndsWith("bin/ffprobe.exe", StringComparison.OrdinalIgnoreCase))
                ?? archive.Entries.FirstOrDefault(e =>
                    e.FullName.Equals("ffprobe.exe", StringComparison.OrdinalIgnoreCase))
                ?? archive.Entries.FirstOrDefault(e =>
                    e.FullName.EndsWith("/ffprobe.exe", StringComparison.OrdinalIgnoreCase)
                    && !e.FullName.Contains("doc", StringComparison.OrdinalIgnoreCase));

            if (ffmpegEntry is null || ffprobeEntry is null)
                throw new FileNotFoundException("官方压缩包中未找到 ffmpeg.exe 或 ffprobe.exe");

            Directory.CreateDirectory(Path.GetDirectoryName(_ffmpegDest)!);
            ffmpegEntry.ExtractToFile(_ffmpegDest, true);
            ffprobeEntry.ExtractToFile(_ffprobeDest, true);

            statusProgress?.Report("正在清理临时文件...");
        }, ct);

        await archiveFile.DisposeAsync();
        try { if (File.Exists(downloadedFilePath)) File.Delete(downloadedFilePath); } catch { }
    }
}

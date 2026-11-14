using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Tubatools.AiJunkCleanup.Models;
using Tubatools.AiJunkCleanup.Runtime;

namespace Tubatools.AiJunkCleanup.Services;

/// <summary>进入垃圾清理模式时后台调用宿主 NTFS/MFT 扫描，生成根目录下三层的 shallow-scan.json 和深层 scan-index.json。</summary>
internal sealed class ShallowScanJob
{
    private readonly CancellationTokenSource _lifetime = new();
    private readonly string? _workspace;
    private readonly JunkScanDepth _depth;
    private bool _started;

    internal ShallowScanJob(string root, string? workspace = null) : this(root, JunkScanDepth.Standard, workspace) { }

    internal ShallowScanJob(string root, JunkScanDepth depth, string? workspace = null)
    {
        RootPath = Path.GetFullPath(root);
        _depth = depth;
        _workspace = workspace;
    }

    internal string RootPath { get; }
    internal Task Completion { get; private set; } = Task.CompletedTask;
    internal string? Error { get; private set; }
    internal string? ResultPath { get; private set; }
    internal string? IndexPath { get; private set; }
    internal ShallowScanResult? Result { get; private set; }

    internal void Start(IProgress<ShallowScanProgress>? progress)
    {
        lock (this)
        {
            if (_started) return;
            _started = true;
        }
        Completion = Task.Run(() => RunAsync(progress));
    }

    internal void Cancel()
    {
        try { _lifetime.Cancel(); } catch (ObjectDisposedException) { }
    }

    internal static string WorkspaceDirectory()
    {
        var workspace = HostBridge.TryGetAgentWorkspacePath();
        if (!string.IsNullOrWhiteSpace(workspace)) return workspace;
        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "TubaWinUi3", "AiAssistant", "Workspace");
    }

    internal static string ResultPathFor(string? workspace = null) =>
        Path.Combine(string.IsNullOrWhiteSpace(workspace) ? WorkspaceDirectory() : workspace, "shallow-scan.json");

    internal static string IndexPathFor(string? workspace = null) =>
        Path.Combine(string.IsNullOrWhiteSpace(workspace) ? WorkspaceDirectory() : workspace, "scan-index.json");

    private async Task RunAsync(IProgress<ShallowScanProgress>? progress)
    {
        try
        {
            progress?.Report(new(true, false, false, null, "正在启动 NTFS 浅层扫描", 0, TimeSpan.Zero, null));
            try { File.Delete(ResultPathFor(_workspace)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            try { File.Delete(IndexPathFor(_workspace)); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            using var host = HostSpaceScan.TryCreate(RootPath, _lifetime.Token, out var error);
            if (host is null)
            {
                Fail(progress, error);
                return;
            }

            while (!host.ScanTask.IsCompleted)
            {
                var snapshot = host.Snapshot();
                progress?.Report(snapshot);
                if (_lifetime.IsCancellationRequested) break;
                try { await Task.WhenAny(host.ScanTask, Task.Delay(300, _lifetime.Token)); }
                catch (OperationCanceledException) { break; }
            }

            if (_lifetime.IsCancellationRequested)
            {
                Fail(progress, "扫描已取消。");
                return;
            }
            if (host.ScanTask.IsFaulted)
            {
                await host.ScanTask;
                Fail(progress, "扫描失败。");
                return;
            }

            progress?.Report(host.Snapshot() with { Phase = "正在整理三层目录", Percent = 98 });
            var data = host.BuildShallow(3, 300);
            var profile = JunkScanDepths.Index(_depth);
            progress?.Report(host.Snapshot() with { Phase = "正在整理深度索引（目录 + 大文件）", Percent = null, Indexing = true });
            var indexData = host.BuildIndex(profile.Depth, profile.MinDirectoryBytes, profile.MinFileBytes,
                profile.MaxDirectories, profile.MaxFiles);
            var index = new ScanIndex(
                1,
                RootPath,
                profile.Depth,
                profile.MinDirectoryBytes,
                profile.MinFileBytes,
                indexData.Directories,
                indexData.Files,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            IndexPath = WriteIndex(index);
            var result = new ShallowScanResult(
                1,
                RootPath,
                3,
                data.Method,
                data.TotalBytes,
                data.Files,
                data.Folders,
                data.ElapsedSeconds,
                data.Directories,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
                profile.Depth,
                profile.MinDirectoryBytes,
                profile.MinFileBytes,
                indexData.Directories.Count,
                indexData.Files.Count,
                IndexPath);
            Result = result;
            ResultPath = WriteResult(result);
            var failedWrite = ResultPath is null;
            var message = failedWrite
                ? "结果写入失败。"
                : IndexPath is null ? "深度索引写入失败，浅层结果已保存。" : "浅层扫描完成";
            progress?.Report(new(true, true, failedWrite, 100, message,
                data.Files, TimeSpan.FromSeconds(data.ElapsedSeconds), failedWrite ? message : null));
        }
        catch (Exception ex)
        {
            Fail(progress, ex.InnerException?.Message ?? ex.Message);
        }
    }

    private void Fail(IProgress<ShallowScanProgress>? progress, string message)
    {
        Error = message;
        progress?.Report(new(true, true, true, null, "浅层扫描失败", 0, TimeSpan.Zero, message));
    }

    private string? WriteResult(ShallowScanResult result)
    {
        try
        {
            var workspace = string.IsNullOrWhiteSpace(_workspace) ? WorkspaceDirectory() : _workspace;
            Directory.CreateDirectory(workspace);
            var path = Path.Combine(workspace, "shallow-scan.json");
            var temporary = path + ".tmp";
            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            File.WriteAllText(temporary, JsonSerializer.Serialize(result, options), new UTF8Encoding(false));
            File.Move(temporary, path, true);
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Error = "结果文件写入失败：" + ex.Message;
            return null;
        }
    }

    private string? WriteIndex(ScanIndex index)
    {
        try
        {
            var workspace = string.IsNullOrWhiteSpace(_workspace) ? WorkspaceDirectory() : _workspace;
            Directory.CreateDirectory(workspace);
            var path = Path.Combine(workspace, "scan-index.json");
            var temporary = path + ".tmp";
            var options = new JsonSerializerOptions
            {
                WriteIndented = false,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            File.WriteAllText(temporary, JsonSerializer.Serialize(index, options), new UTF8Encoding(false));
            File.Move(temporary, path, true);
            return path;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            Error = "深度索引写入失败：" + ex.Message;
            return null;
        }
    }
}

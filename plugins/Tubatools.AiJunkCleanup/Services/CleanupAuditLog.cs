using System.Text;
using System.Text.Json;
using Tubatools.AiJunkCleanup.Models;

namespace Tubatools.AiJunkCleanup.Services;

/// <summary>把每一次真实删除写入插件数据目录的 cleanup-audit.jsonl，方便事后核对。</summary>
internal static class CleanupAuditLog
{
    private const long MaxBytes = 8L * 1024 * 1024;
    private static readonly object Gate = new();

    internal static string PathFor(string? dataDirectory) =>
        Path.Combine(string.IsNullOrWhiteSpace(dataDirectory)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TubaWinUi3", "Plugins", "Tubatools.AiJunkCleanup")
            : dataDirectory, "cleanup-audit.jsonl");

    internal static void Append(string? dataDirectory, JunkScanDepth depth, string? planSummary,
        string path, bool recycle, bool success, long bytes, string message)
    {
        try
        {
            var file = PathFor(dataDirectory);
            lock (Gate)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                if (File.Exists(file) && new FileInfo(file).Length > MaxBytes)
                {
                    var backup = file + ".1";
                    try { File.Copy(file, backup, true); } catch (IOException) { }
                    File.WriteAllText(file, "", new UTF8Encoding(false));
                }
                var line = JsonSerializer.Serialize(new
                {
                    time = DateTimeOffset.Now.ToString("O"),
                    depth = JunkScanDepths.Key(depth),
                    plan = planSummary ?? "",
                    path,
                    method = recycle ? "recycle" : "permanent",
                    success,
                    bytes,
                    message = message ?? ""
                });
                File.AppendAllText(file, line + Environment.NewLine, new UTF8Encoding(false));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException) { }
    }
}

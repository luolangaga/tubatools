using System.Security.Cryptography;
using System.Text.RegularExpressions;
using TubaWinUi3.Services.Plugins;

namespace TubaWinUi3.Services.AiAgent;

internal sealed record AgentPluginLoadResult(string Path, bool Success, string Message);

/// <summary>只接收本轮成功打包后新增或改变的工作区产物；不扫描下载目录，不重放历史包。</summary>
internal sealed class AgentPluginAutoLoader
{
    private readonly string _root;
    private readonly Dictionary<string, string> _before;
    private readonly HashSet<string> _previousTools;
    private readonly Func<string, CancellationToken, Task> _install;

    private AgentPluginAutoLoader(string root, Dictionary<string, string> before, HashSet<string> previousTools,
        Func<string, CancellationToken, Task> install)
    { _root = root; _before = before; _previousTools = previousTools; _install = install; }

    internal static async Task<AgentPluginAutoLoader> CaptureAsync(IReadOnlyList<AgentMessage> previous, CancellationToken ct,
        string? root = null, Func<string, CancellationToken, Task>? install = null)
    {
        root = Path.GetFullPath(root ?? AgentWorkspace.PackagesDirectory);
        var before = await SnapshotAsync(root, ct);
        var ids = Operations(previous).Select(o => o.Id).ToHashSet(StringComparer.Ordinal);
        return new(root, before, ids, install ?? PluginManager.InstallLocalAsync);
    }

    private static IEnumerable<AgentOperation> Operations(IReadOnlyList<AgentMessage> messages) => messages
        .SelectMany(m => m.Content ?? []).Where(p => p.Operation is not null).Select(p => p.Operation!);

    internal async Task<IReadOnlyList<AgentPluginLoadResult>> LoadAsync(IReadOnlyList<AgentMessage> messages,
        Action<AgentOperation> progress, CancellationToken ct)
    {
        // 只接受本轮真实完成且退出码为零的打包命令，不能以助手说“已完成”作为依据。
        var builds = Operations(messages).Where(o => !_previousTools.Contains(o.Id) && o.Kind == "command" &&
            o.Status == "completed" && o.ExitCode == 0 && IsPackCommand(o.Command)).ToList();
        if (builds.Count == 0) return [];
        var current = await SnapshotAsync(_root, ct);
        var results = new List<AgentPluginLoadResult>();
        var candidates = new Dictionary<string, KeyValuePair<string, string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in current.OrderBy(p => File.GetLastWriteTimeUtc(p.Key)))
        {
            if (_before.TryGetValue(candidate.Key, out var old) && old == candidate.Value) continue;
            if (!builds.Any(build => build.Output.Contains(Path.GetFileName(candidate.Key), StringComparison.OrdinalIgnoreCase))) continue;
            try { candidates[PluginManager.ReadPackageIdentity(candidate.Key).Id] = candidate; }
            catch (Exception ex) when (ex is IOException or InvalidDataException or InvalidOperationException or System.Xml.XmlException)
            {
                var message = AgentText.L("AutoLoadFailed", "插件自动加载失败，原版本保持可用") + "\n" + ex.Message;
                progress(new("host:load:" + candidate.Value, "plugin-load", "load", "error", Path.GetFileName(candidate.Key), "", message, ""));
                results.Add(new(candidate.Key, false, message));
            }
        }
        // 同一轮多次重试打包时，每个包 ID 只加载最后产出的版本。
        foreach (var (path, hash) in candidates.Values)
        {
            ct.ThrowIfCancellationRequested();
            var operation = new AgentOperation("host:load:" + hash, "plugin-load", "load", "running", Path.GetFileName(path), "", "", "");
            progress(operation);
            try
            {
                await _install(path, ct);
                var message = AgentText.L("AutoLoaded", "插件已自动加载") + " · " + Path.GetFileName(path);
                progress(operation with { Status = "completed", Output = message });
                results.Add(new(path, true, message));
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                var message = AgentText.L("AutoLoadFailed", "插件自动加载失败，原版本保持可用") + "\n" + ex.Message;
                progress(operation with { Status = "error", Output = message });
                results.Add(new(path, false, message));
            }
        }
        return results;
    }

    internal static bool IsPackCommand(string command) => Regex.IsMatch(command,
        "\\bdotnet(?:\\.exe)?[\"']?\\s+pack\\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static async Task<Dictionary<string, string>> SnapshotAsync(string root, CancellationToken ct)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (!Directory.Exists(root)) return result;
        // 每级都拒绝联接/符号链接，产物路径必须始终留在原工作区。
        for (var directory = new DirectoryInfo(root); directory is not null; directory = directory.Parent)
            if ((directory.Attributes & FileAttributes.ReparsePoint) != 0) throw new IOException("插件产物目录不能是链接。");
        foreach (var path in Directory.GetFiles(root, "*.nupkg"))
        {
            ct.ThrowIfCancellationRequested();
            var file = new FileInfo(path);
            if ((file.Attributes & FileAttributes.ReparsePoint) != 0 || file.Length > 100L * 1024 * 1024) continue;
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
            result[path] = Convert.ToHexString(await SHA256.HashDataAsync(stream, ct));
        }
        return result;
    }
}

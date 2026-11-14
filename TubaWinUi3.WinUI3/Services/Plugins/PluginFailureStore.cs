using System.Text.Json;

namespace TubaWinUi3.Services.Plugins;

internal sealed record PluginFailureRecord(
    int LoadFailures = 0,
    int ExecutionFailures = 0,
    string LastError = "",
    string Version = "",
    DateTimeOffset LastUtc = default);

/// <summary>
/// 插件故障计数。加载/启动失败连续达到阈值就自动停用该插件（保留原因），
/// 这样单个坏插件不会每次启动都拖垮整个工具箱；用户手动启用会清零重试。
/// 执行期异常只累计、供插件页展示，不触发停用——工具报错多为输入或环境问题。
/// </summary>
internal static class PluginFailureStore
{
    internal const int AutoDisableAfter = 3;

    private static readonly object Gate = new();

    internal static string DefaultPath => Path.Combine(PluginManager.Root, "failures.json");
    internal static string PathOverride { get; set; } = "";

    private static string FilePath => PathOverride.Length > 0 ? PathOverride : DefaultPath;

    internal static PluginFailureRecord? Get(string id)
    {
        lock (Gate) return Read().GetValueOrDefault(id);
    }

    /// <summary>记录一次加载/启动失败；返回 true 表示达到阈值，调用方应停用该插件。</summary>
    internal static bool RecordLoadFailure(string id, string version, string error)
    {
        lock (Gate)
        {
            var data = Read();
            var previous = data.GetValueOrDefault(id) ?? new PluginFailureRecord();
            // 换了版本就是新的代码，旧版本的失败记录不再适用。
            var count = previous.Version.Equals(version, StringComparison.OrdinalIgnoreCase) ? previous.LoadFailures + 1 : 1;
            data[id] = previous with { LoadFailures = count, LastError = error, Version = version, LastUtc = DateTimeOffset.UtcNow };
            Write(data);
            return count >= AutoDisableAfter;
        }
    }

    internal static void RecordExecutionFailure(string id, string error)
    {
        lock (Gate)
        {
            var data = Read();
            var previous = data.GetValueOrDefault(id) ?? new PluginFailureRecord();
            data[id] = previous with
            {
                ExecutionFailures = previous.ExecutionFailures + 1,
                LastError = error,
                LastUtc = DateTimeOffset.UtcNow
            };
            Write(data);
        }
    }

    internal static void Clear(string id)
    {
        lock (Gate)
        {
            var data = Read();
            if (data.Remove(id)) Write(data);
        }
    }

    private static Dictionary<string, PluginFailureRecord> Read()
    {
        try
        {
            if (!File.Exists(FilePath)) return new(StringComparer.OrdinalIgnoreCase);
            return JsonSerializer.Deserialize<Dictionary<string, PluginFailureRecord>>(File.ReadAllText(FilePath))
                ?? new(StringComparer.OrdinalIgnoreCase);
        }
        catch
        {
            return new(StringComparer.OrdinalIgnoreCase);
        }
    }

    private static void Write(Dictionary<string, PluginFailureRecord> data)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath + ".tmp", JsonSerializer.Serialize(data));
            File.Move(FilePath + ".tmp", FilePath, true);
        }
        catch { }
    }
}

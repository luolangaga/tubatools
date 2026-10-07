using System.Diagnostics;
using System.Text.Json;

namespace TubaWinUi3.Services;

/// <summary>一个社区工具的本地安装记录（更新检测 / 卸载 / 状态展示用）。</summary>
public sealed record CommunityInstallRecord(
    string ToolId,
    string Category,
    string? Sha,
    string? Version,
    string? Author,
    string? RepoPath,
    string? FileName,
    string? LaunchTarget,
    DateTimeOffset InstalledAt);

/// <summary>
/// 社区工具安装记录（数据目录 CommunityTools/installed.json）。
/// 与 tools.json 的分工：tools.json 负责让 ToolCatalog 收录工具（名称/描述/标签/启动目标），
/// 本记录负责社区特有信息（远端文件 sha → 更新检测、作者/仓库、字符串版本号）。
/// 读取容错（损坏按空表处理）；写入尽力而为（失败只记日志，不影响已完成的安装）。
/// </summary>
public static class CommunityToolRegistry
{
    private static readonly object _lock = new();
    private static List<CommunityInstallRecord>? _cache;
    private static string? _rootOverride;

    internal static void SetRootForTests(string? dir)
    {
        lock (_lock)
        {
            _rootOverride = dir;
            _cache = null;
        }
    }

    private static string GetDirectory() =>
        _rootOverride ?? Path.Combine(ConfigManager.GetDataDir(), "CommunityTools");

    private static string GetFilePath() => Path.Combine(GetDirectory(), "installed.json");

    public static CommunityInstallRecord? TryGet(string toolId)
    {
        if (string.IsNullOrWhiteSpace(toolId)) return null;
        lock (_lock)
        {
            return LoadAllLocked().FirstOrDefault(r =>
                r.ToolId.Equals(toolId, StringComparison.OrdinalIgnoreCase));
        }
    }

    public static void Upsert(CommunityInstallRecord record)
    {
        if (string.IsNullOrWhiteSpace(record.ToolId)) return;
        lock (_lock)
        {
            var list = LoadAllLocked();
            list.RemoveAll(r => r.ToolId.Equals(record.ToolId, StringComparison.OrdinalIgnoreCase));
            list.Add(record);
            SaveLocked(list);
        }
    }

    public static void Remove(string toolId)
    {
        if (string.IsNullOrWhiteSpace(toolId)) return;
        lock (_lock)
        {
            var list = LoadAllLocked();
            if (list.RemoveAll(r => r.ToolId.Equals(toolId, StringComparison.OrdinalIgnoreCase)) > 0)
                SaveLocked(list);
        }
    }

    /// <summary>清理工具目录已不存在的记录（工具被外部删除后不留脏数据），返回清理数量。</summary>
    public static int PruneStale()
    {
        lock (_lock)
        {
            var list = LoadAllLocked();
            var removed = list.RemoveAll(r =>
            {
                try
                {
                    var dir = Path.Combine(ToolCatalog.ToolsRoot, r.Category, r.ToolId);
                    return !Directory.Exists(dir);
                }
                catch
                {
                    return false;
                }
            });
            if (removed > 0) SaveLocked(list);
            return removed;
        }
    }

    private static List<CommunityInstallRecord> LoadAllLocked()
    {
        if (_cache is not null) return _cache;

        var path = GetFilePath();
        if (!File.Exists(path))
        {
            _cache = [];
            return _cache;
        }

        try
        {
            _cache = JsonSerializer.Deserialize<List<CommunityInstallRecord>>(File.ReadAllText(path)) ?? [];
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CommunityToolRegistry] 读取安装记录失败，按空表处理: {ex.Message}");
            _cache = [];
        }
        return _cache;
    }

    private static void SaveLocked(List<CommunityInstallRecord> records)
    {
        try
        {
            Directory.CreateDirectory(GetDirectory());
            var path = GetFilePath();
            var tempPath = path + ".tmp";
            File.WriteAllText(tempPath, JsonSerializer.Serialize(records, new JsonSerializerOptions { WriteIndented = true }));
            if (File.Exists(path))
                File.Replace(tempPath, path, null);
            else
                File.Move(tempPath, path);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"[CommunityToolRegistry] 保存安装记录失败（不影响安装结果）: {ex.Message}");
        }
    }
}

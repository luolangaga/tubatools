using System.Text.Json;
using TubaWinUi3.Services.ActiveIntercept.Runtime.Models;

namespace TubaWinUi3.Services.ActiveIntercept.Runtime;

/// <summary>
/// 期望状态持久化（state.json）：记录每个右键项的期望状态（屏蔽/放行）与审核状态。
/// 写入采用 ContextMenuMgr 的原子落盘手法：同目录临时文件 → 序列化（WriteThrough）
/// → 重读校验 → File.Replace 原子替换，并保留一份 .bak 最近可用代。当前文件损坏时
/// 自动隔离并回退 .bak，杜绝"半写文件"导致的丢状态。
/// </summary>
public sealed class StateStore
{
    private readonly string _dataDir;
    private readonly string _path;
    private readonly string _backupPath;
    private readonly string _quarantineDir;
    private readonly object _gate = new();
    private InterceptStateFile _state;

    public StateStore(string dataDir)
    {
        _dataDir = dataDir;
        Directory.CreateDirectory(dataDir);
        _path = Path.Combine(dataDir, "state.json");
        _backupPath = _path + ".bak";
        _quarantineDir = Path.Combine(dataDir, "quarantine");
        _state = LoadOrCreate();
    }

    public InterceptStateFile State => _state;

    /// <summary>当前所有期望状态条目（按 Id 索引，大小写不敏感）。</summary>
    public Dictionary<string, InterceptStateEntry> ById()
    {
        lock (_gate)
        {
            var map = new Dictionary<string, InterceptStateEntry>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in _state.Entries)
            {
                if (!string.IsNullOrWhiteSpace(e.Id)) map[e.Id] = e;
            }
            return map;
        }
    }

    public void Upsert(InterceptStateEntry entry)
    {
        lock (_gate)
        {
            var map = ById();
            map[entry.Id] = entry;
            _state.Entries = map.Values.OrderBy(e => e.Id, StringComparer.OrdinalIgnoreCase).ToList();
        }
    }

    public void Remove(string id)
    {
        lock (_gate)
        {
            _state.Entries.RemoveAll(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>按谓词移除条目（如已消失的注册项）。</summary>
    public void RemoveWhere(Func<string, bool> idPredicate)
    {
        lock (_gate)
        {
            _state.Entries.RemoveAll(e => idPredicate(e.Id));
        }
    }

    public void SetBaselineEstablished(bool value)
    {
        lock (_gate) _state.BaselineEstablished = value;
    }

    public bool BaselineEstablished
    {
        get { lock (_gate) return _state.BaselineEstablished; }
    }

    /// <summary>条目总数（含已删除/已忽略标记的）。</summary>
    public int Count
    {
        get { lock (_gate) return _state.Entries.Count; }
    }

    /// <summary>消化 SuppressNextDetection 标记（命中返回 true 并清除）。</summary>
    public bool TryConsumeSuppressedDetection(string id)
    {
        lock (_gate)
        {
            var entry = _state.Entries.FirstOrDefault(e => string.Equals(e.Id, id, StringComparison.OrdinalIgnoreCase));
            if (entry is null || !entry.SuppressNextDetection) return false;
            entry.SuppressNextDetection = false;
            return true;
        }
    }

    /// <summary>原子保存（临时文件 + 校验 + File.Replace + .bak）。</summary>
    public void Save()
    {
        InterceptStateFile snapshot;
        lock (_gate)
        {
            _state.SchemaVersion = 1;
            _state.SavedAtUtc = DateTime.UtcNow.ToString("o");
            // 深拷贝，避免序列化过程中被并发修改
            snapshot = CloneState(_state);
        }

        try
        {
            SaveAtomically(snapshot);
        }
        catch (Exception ex)
        {
            BackEndLog.Error($"保存状态失败：{ex.Message}");
        }
    }

    // ---------- 原子落盘 ----------

    private void SaveAtomically(InterceptStateFile snapshot)
    {
        var tmp = _path + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            using (var stream = new FileStream(tmp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920,
                       FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                JsonSerializer.Serialize(stream, snapshot, BackEndJsonContext.Default.InterceptStateFile);
                stream.Flush(flushToDisk: true);
            }

            // 落盘后立即用生产读取器校验，确保永远不会把坏文件当权威文件
            _ = ReadValidated(tmp);
            BackEndLog.Info($"状态保存（校验通过）：{snapshot.Entries.Count} 条 -> {_path}");

            if (!File.Exists(_path))
            {
                File.Move(tmp, _path);
                return;
            }

            File.Replace(tmp, _path, _backupPath, ignoreMetadataErrors: true);
        }
        finally
        {
            TryDelete(tmp);
        }
    }

    private InterceptStateFile LoadOrCreate()
    {
        if (!File.Exists(_path))
        {
            return new InterceptStateFile();
        }

        try
        {
            var parsed = ReadValidated(_path);
            return parsed;
        }
        catch (Exception ex)
        {
            BackEndLog.Warn($"读取状态失败，尝试回退 .bak：{ex.Message}");
            if (File.Exists(_backupPath))
            {
                try
                {
                    var backup = ReadValidated(_backupPath);
                    BackEndLog.Warn("已从 .bak 恢复状态文件。");
                    TryMoveToQuarantine(_path, "corrupt-state");
                    File.Copy(_backupPath, _path, overwrite: true);
                    return backup;
                }
                catch (Exception backupEx)
                {
                    BackEndLog.Error($"状态 .bak 也损坏：{backupEx.Message}");
                }
            }

            TryMoveToQuarantine(_path, "corrupt-state");
            BackEndLog.Warn("状态重建为空基线。");
            return new InterceptStateFile();
        }
    }

    private static InterceptStateFile ReadValidated(string path)
    {
        var json = File.ReadAllText(path);
        var parsed = JsonSerializer.Deserialize(json, BackEndJsonContext.Default.InterceptStateFile);
        if (parsed is null)
        {
            throw new InvalidDataException("state.json 反序列化结果为 null。");
        }

        foreach (var entry in parsed.Entries)
        {
            if (string.IsNullOrWhiteSpace(entry.Id))
            {
                throw new InvalidDataException("state.json 含空 Id 条目。");
            }
        }

        if (parsed.SchemaVersion == 0)
        {
            // 旧格式（无 schemaVersion）：迁移为新版字段默认值即可
            parsed.SchemaVersion = 1;
        }
        else if (parsed.SchemaVersion > 1)
        {
            BackEndLog.Warn($"state.json 版本 {parsed.SchemaVersion} 高于当前支持，按最新可读版本尝试加载。");
        }

        return parsed;
    }

    private void TryMoveToQuarantine(string path, string reason)
    {
        try
        {
            Directory.CreateDirectory(_quarantineDir);
            var target = Path.Combine(_quarantineDir,
                $"{Path.GetFileName(path)}-{DateTime.Now:yyyyMMdd-HHmmss}-{reason}");
            File.Move(path, target, overwrite: false);
        }
        catch
        {
            // 隔离失败不致命
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // 临时文件可丢弃，后续由清理兜底
        }
    }

    private static InterceptStateFile CloneState(InterceptStateFile state)
    {
        var copy = new InterceptStateFile
        {
            SchemaVersion = state.SchemaVersion,
            SavedAtUtc = state.SavedAtUtc,
            BaselineEstablished = state.BaselineEstablished,
        };
        foreach (var e in state.Entries)
        {
            copy.Entries.Add(new InterceptStateEntry
            {
                Id = e.Id,
                Hive = e.Hive,
                View = e.View,
                SubKey = e.SubKey,
                Kind = e.Kind,
                Clsid = e.Clsid,
                Name = e.Name,
                Command = e.Command,
                ExePath = e.ExePath,
                DesiredState = e.DesiredState,
                ObservedBlocked = e.ObservedBlocked,
                IsPendingApproval = e.IsPendingApproval,
                PendingChangeKind = e.PendingChangeKind,
                IsDeleted = e.IsDeleted,
                BackupFilePath = e.BackupFilePath,
                DeletedAtUtc = e.DeletedAtUtc,
                SuppressNextDetection = e.SuppressNextDetection,
                ConsecutiveMissingSnapshots = e.ConsecutiveMissingSnapshots,
                UpdatedAtUtc = e.UpdatedAtUtc,
                Note = e.Note,
                FirstSeenUtc = e.FirstSeenUtc,
                Source = e.Source,
            });
        }
        return copy;
    }
}

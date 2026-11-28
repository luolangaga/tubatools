using System.Text.Json;
using TubaWinUi3.Services.ActiveIntercept.Runtime.Models;

namespace TubaWinUi3.Services.ActiveIntercept.Runtime;

/// <summary>
/// 拦截事件日志（events.jsonl）：每行一条 JSON，主程序从这里读取展示。
/// 追加写、线程安全；支持按行 ID / 条目 ID 删除单条或多条记录、
/// 清空与压缩（删除最旧记录），供审核页「删除记录 / 停止追踪 / 清空记录」使用。
/// </summary>
public sealed class EventLog
{
    private readonly string _path;
    private readonly object _lock = new();

    public EventLog(string dataDir)
    {
        Directory.CreateDirectory(dataDir);
        _path = Path.Combine(dataDir, "events.jsonl");
    }

    public string FilePath => _path;

    /// <summary>当前行数（文件不存在返回 0）。</summary>
    public int LineCount
    {
        get
        {
            try
            {
                if (!File.Exists(_path)) return 0;
                var count = 0;
                foreach (var line in File.ReadLines(_path))
                {
                    if (!string.IsNullOrWhiteSpace(line)) count++;
                }
                return count;
            }
            catch { return 0; }
        }
    }

    public void Append(InterceptEvent evt)
    {
        evt.TimestampUtc = DateTime.UtcNow.ToString("o");
        if (string.IsNullOrWhiteSpace(evt.RowId)) evt.RowId = Guid.NewGuid().ToString("N");
        lock (_lock)
        {
            try
            {
                var line = JsonSerializer.Serialize(evt, BackEndJsonContext.Default.InterceptEvent);
                File.AppendAllText(_path, line + Environment.NewLine);
            }
            catch (Exception ex)
            {
                BackEndLog.Error($"写事件失败：{ex.Message}");
            }
        }
    }

    /// <summary>按行 ID 删除记录（原子重写文件）。返回实际删除条数。</summary>
    public int RemoveRows(IReadOnlyCollection<string> rowIds)
    {
        if (rowIds is null || rowIds.Count == 0) return 0;
        var set = new HashSet<string>(rowIds, StringComparer.OrdinalIgnoreCase);
        lock (_lock)
        {
            return Rewrite(lines => lines.Where(l => !set.Contains(RowIdOf(l))).ToList());
        }
    }

    /// <summary>按条目 ID 删除其全部事件记录（用于「停止追踪」的清理）。返回实际删除条数。</summary>
    public int RemoveByIds(IReadOnlyCollection<string> ids)
    {
        if (ids is null || ids.Count == 0) return 0;
        var set = new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
        lock (_lock)
        {
            return Rewrite(lines => lines.Where(l => !set.Contains(ItemIdOf(l))).ToList());
        }
    }

    /// <summary>清空全部事件记录。返回删除条数。</summary>
    public int Clear()
    {
        lock (_lock)
        {
            if (!File.Exists(_path)) return 0;
            var count = LineCount;
            try { File.Delete(_path); } catch { }
            return count;
        }
    }

    /// <summary>压缩：行数超过上限时删除最旧的行。返回删除条数。</summary>
    public int Compact(int maxLines)
    {
        if (maxLines <= 0) return 0;
        lock (_lock)
        {
            return Rewrite(lines => lines.Count <= maxLines ? lines : lines.Skip(lines.Count - maxLines).ToList());
        }
    }

    /// <summary>读取全部事件（倒序，最新在前）。文件损坏时跳过坏行。</summary>
    public static List<InterceptEvent> ReadAll(string dataDir)
    {
        var result = new List<InterceptEvent>();
        var path = Path.Combine(dataDir, "events.jsonl");
        if (!File.Exists(path)) return result;

        try
        {
            foreach (var line in File.ReadLines(path))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                try
                {
                    var evt = JsonSerializer.Deserialize(line, BackEndJsonContext.Default.InterceptEvent);
                    if (evt is not null)
                    {
                        if (string.IsNullOrWhiteSpace(evt.RowId)) evt.RowId = Guid.NewGuid().ToString("N");
                        result.Add(evt);
                    }
                }
                catch { }
            }
        }
        catch { }
        result.Reverse();
        return result;
    }

    // ---------- 内部 ----------

    private static string RowIdOf(string line)
    {
        try
        {
            var evt = JsonSerializer.Deserialize(line, BackEndJsonContext.Default.InterceptEvent);
            return evt?.RowId ?? "";
        }
        catch { return ""; }
    }

    private static string ItemIdOf(string line)
    {
        try
        {
            var evt = JsonSerializer.Deserialize(line, BackEndJsonContext.Default.InterceptEvent);
            return evt?.Id ?? "";
        }
        catch { return ""; }
    }

    /// <summary>原子重写：读取全部行 → 变换 → 写临时文件 → 替换。须在 _lock 内调用。</summary>
    private int Rewrite(Func<List<string>, List<string>> transform)
    {
        try
        {
            if (!File.Exists(_path)) return 0;
            var lines = File.ReadAllLines(_path).Where(l => !string.IsNullOrWhiteSpace(l)).ToList();
            var kept = transform(lines);
            var removed = lines.Count - kept.Count;
            if (removed == 0) return 0;

            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, kept.Count == 0
                ? ""
                : string.Join(Environment.NewLine, kept) + Environment.NewLine);
            File.Move(tmp, _path, overwrite: true);
            return removed;
        }
        catch (Exception ex)
        {
            BackEndLog.Error($"重写事件日志失败：{ex.Message}");
            return 0;
        }
    }
}

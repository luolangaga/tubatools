using System.Text.Json;
using TubaWinUi3.Services.ActiveIntercept.Runtime.Models;

namespace TubaWinUi3.Services.ActiveIntercept.Runtime;

/// <summary>
/// 通知去重状态持久化（notify_state.json）。
/// 记录每个被拦截条目最近一次通知时间：同一条目在冷却期内重复出现（第三方反复重写）
/// 不再弹通知，从根本上消除「一直在发通知」的提醒风暴。
/// </summary>
public sealed class NotifyStateStore
{
    private readonly string _path;
    private NotifyStateFile _file;

    public NotifyStateStore(string dataDir)
    {
        Directory.CreateDirectory(dataDir);
        _path = Path.Combine(dataDir, "notify_state.json");
        _file = LoadOrCreate();
    }

    /// <summary>该条目是否在冷却期内（距上次通知不足 cooldown）。</summary>
    public bool WasNotifiedRecently(string id, TimeSpan cooldown, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        var entry = _file.Items.FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));
        if (entry is null) return false;
        if (DateTime.TryParse(entry.LastNotifiedUtc, null, System.Globalization.DateTimeStyles.RoundtripKind, out var last))
        {
            return nowUtc - last < cooldown;
        }
        return false;
    }

    public void MarkNotified(string id, DateTime nowUtc)
    {
        if (string.IsNullOrWhiteSpace(id)) return;
        var entry = _file.Items.FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));
        if (entry is null)
        {
            _file.Items.Add(new NotifyStateEntry { Id = id, LastNotifiedUtc = nowUtc.ToString("o") });
        }
        else
        {
            entry.LastNotifiedUtc = nowUtc.ToString("o");
        }
        Save();
    }

    /// <summary>清除条目的通知记录（用户放行/恢复追踪后重新武装，下次真实拦截可再提醒）。</summary>
    public void Clear(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return;
        var removed = _file.Items.RemoveAll(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));
        if (removed > 0) Save();
    }

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_file, BackEndJsonContext.Default.NotifyStateFile);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, _path, overwrite: true);
        }
        catch (Exception ex)
        {
            BackEndLog.Error($"保存通知状态失败：{ex.Message}");
        }
    }

    private NotifyStateFile LoadOrCreate()
    {
        try
        {
            if (File.Exists(_path))
            {
                var json = File.ReadAllText(_path);
                var parsed = JsonSerializer.Deserialize(json, BackEndJsonContext.Default.NotifyStateFile);
                if (parsed is not null) return parsed;
            }
        }
        catch (Exception ex)
        {
            BackEndLog.Error($"读取通知状态失败，重建：{ex.Message}");
        }
        return new NotifyStateFile();
    }
}

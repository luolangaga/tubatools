using System.Text.Json;
using TubaWinUi3.Services.ActiveIntercept.Runtime.Models;

namespace TubaWinUi3.Services.ActiveIntercept.Runtime;

/// <summary>
/// 「停止追踪」列表持久化（ignored.json）。
/// 用户删除记录并选择「停止追踪」后，该条目被加入忽略列表：
/// 扫描时跳过（不再拦截、不再提醒），直到用户「恢复追踪」。
/// 这是终结拦截/通知循环的关键：删除 ≠ 停止追踪，二者行为分离。
/// </summary>
public sealed class IgnoreStore
{
    private readonly string _path;
    private IgnoreFile _file;

    public IgnoreStore(string dataDir)
    {
        Directory.CreateDirectory(dataDir);
        _path = Path.Combine(dataDir, "ignored.json");
        _file = LoadOrCreate();
    }

    public bool Contains(string id)
    {
        if (string.IsNullOrWhiteSpace(id)) return false;
        return _file.Items.Any(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    public void Add(string id, string name = "", string subKey = "", string exePath = "", string note = "")
    {
        if (string.IsNullOrWhiteSpace(id)) return;

        var existing = _file.Items.FirstOrDefault(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            if (!string.IsNullOrWhiteSpace(name)) existing.Name = name;
            if (!string.IsNullOrWhiteSpace(subKey)) existing.SubKey = subKey;
            if (!string.IsNullOrWhiteSpace(exePath)) existing.ExePath = exePath;
            if (!string.IsNullOrWhiteSpace(note)) existing.Note = note;
        }
        else
        {
            _file.Items.Add(new IgnoreEntry
            {
                Id = id,
                Name = name,
                SubKey = subKey,
                ExePath = exePath,
                CreatedUtc = DateTime.UtcNow.ToString("o"),
                Note = string.IsNullOrWhiteSpace(note) ? "用户停止追踪" : note,
            });
        }
        Save();
    }

    public void Remove(string id)
    {
        var removed = _file.Items.RemoveAll(i => string.Equals(i.Id, id, StringComparison.OrdinalIgnoreCase));
        if (removed > 0) Save();
    }

    public void Clear()
    {
        if (_file.Items.Count == 0) return;
        _file.Items.Clear();
        Save();
    }

    public List<IgnoreEntry> GetAll() => _file.Items;

    public int Count => _file.Items.Count;

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_file, BackEndJsonContext.Default.IgnoreFile);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, _path, overwrite: true);
        }
        catch (Exception ex)
        {
            BackEndLog.Error($"保存忽略列表失败：{ex.Message}");
        }
    }

    private IgnoreFile LoadOrCreate()
    {
        try
        {
            if (File.Exists(_path))
            {
                var json = File.ReadAllText(_path);
                var parsed = JsonSerializer.Deserialize(json, BackEndJsonContext.Default.IgnoreFile);
                if (parsed is not null) return parsed;
            }
        }
        catch (Exception ex)
        {
            BackEndLog.Error($"读取忽略列表失败，重建：{ex.Message}");
        }
        return new IgnoreFile();
    }
}

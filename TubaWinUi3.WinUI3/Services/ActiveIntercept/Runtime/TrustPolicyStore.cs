using System.Text.Json;
using TubaWinUi3.Services.ActiveIntercept.Runtime.Models;

namespace TubaWinUi3.Services.ActiveIntercept.Runtime;

/// <summary>
/// 信任策略持久化（trust_policies.json）。
/// 用户可为每个程序标记：每次都通过 / 每次都拦截 / 每次都询问。
/// </summary>
public sealed class TrustPolicyStore
{
    private readonly string _path;
    private TrustPolicyFile _file;

    public TrustPolicyStore(string dataDir)
    {
        Directory.CreateDirectory(dataDir);
        _path = Path.Combine(dataDir, "trust_policies.json");
        _file = LoadOrCreate();
    }

    /// <summary>查找程序的信任策略（未找到返回 Ask）。</summary>
    public TrustPolicyKind GetPolicy(string exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath)) return TrustPolicyKind.Ask;
        var entry = _file.Policies.FirstOrDefault(p =>
            string.Equals(p.ExePath, exePath, StringComparison.OrdinalIgnoreCase));
        return entry?.Policy ?? TrustPolicyKind.Ask;
    }

    /// <summary>设置程序的信任策略。</summary>
    public void SetPolicy(string exePath, TrustPolicyKind policy, string note = "")
    {
        var existing = _file.Policies.FirstOrDefault(p =>
            string.Equals(p.ExePath, exePath, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            existing.Policy = policy;
            if (!string.IsNullOrWhiteSpace(note)) existing.Note = note;
        }
        else
        {
            _file.Policies.Add(new TrustPolicyEntry
            {
                ExePath = exePath,
                Policy = policy,
                Note = note,
                CreatedUtc = DateTime.UtcNow.ToString("o"),
            });
        }
        Save();
    }

    /// <summary>删除程序的信任策略。</summary>
    public void RemovePolicy(string exePath)
    {
        _file.Policies.RemoveAll(p =>
            string.Equals(p.ExePath, exePath, StringComparison.OrdinalIgnoreCase));
        Save();
    }

    /// <summary>获取所有策略。</summary>
    public List<TrustPolicyEntry> GetAll() => _file.Policies;

    public void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(_file, BackEndJsonContext.Default.TrustPolicyFile);
            var tmp = _path + ".tmp";
            File.WriteAllText(tmp, json);
            File.Move(tmp, _path, overwrite: true);
        }
        catch (Exception ex)
        {
            BackEndLog.Error($"保存信任策略失败：{ex.Message}");
        }
    }

    private TrustPolicyFile LoadOrCreate()
    {
        try
        {
            if (File.Exists(_path))
            {
                var json = File.ReadAllText(_path);
                var parsed = JsonSerializer.Deserialize(json, BackEndJsonContext.Default.TrustPolicyFile);
                if (parsed is not null) return parsed;
            }
        }
        catch { }
        return new TrustPolicyFile();
    }
}


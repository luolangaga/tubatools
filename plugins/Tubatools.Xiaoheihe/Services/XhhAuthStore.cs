using System.Text.Json;

namespace Tubatools.Xiaoheihe.Services;

internal sealed class XhhSession
{
    public string Cookie { get; set; } = "";
    public string HeyboxId { get; set; } = "";
    public string Nickname { get; set; } = "";
    public string Avatar { get; set; } = "";
    public int Level { get; set; }
    public long SavedAt { get; set; }
}

/// <summary>登录态落盘：插件私有目录下的 session.json，升级与卸载不会删除。</summary>
internal sealed class XhhAuthStore
{
    private readonly string _path;

    public XhhAuthStore(string dataDirectory)
    {
        Directory.CreateDirectory(dataDirectory);
        _path = Path.Combine(dataDirectory, "session.json");
    }

    public string FilePath => _path;

    public XhhSession? Load()
    {
        try
        {
            if (!File.Exists(_path)) return null;
            string json = File.ReadAllText(_path);
            return JsonSerializer.Deserialize<XhhSession>(json);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void Save(XhhSession session)
    {
        try
        {
            string json = JsonSerializer.Serialize(session, new JsonSerializerOptions { WriteIndented = true });
            string temp = _path + ".tmp";
            File.WriteAllText(temp, json);
            File.Move(temp, _path, true);
        }
        catch (Exception)
        {
            // 登录态写盘失败不应打断当前会话。
        }
    }

    public void Clear()
    {
        try { if (File.Exists(_path)) File.Delete(_path); }
        catch (Exception) { }
    }
}

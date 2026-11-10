using System.Text;
using Microsoft.Win32;

namespace Tubatools.SteamToolbox;

// Valve KeyValues text format. Never deserialize login tokens or write Steam configuration.
internal sealed class SteamVdf
{
    public Dictionary<string, SteamVdf> Children { get; } = new(StringComparer.OrdinalIgnoreCase);
    public string Value { get; private set; } = "";
    public SteamVdf this[string key] => Children.GetValueOrDefault(key) ?? new();
    public long Number(string key) => long.TryParse(this[key].Value, out var n) ? Math.Max(0, n) : 0;
    public static SteamVdf Parse(string text)
    {
        int pos = 0;
        string? Token()
        {
            while (pos < text.Length)
            {
                if (char.IsWhiteSpace(text[pos]) || text[pos] == '\uFEFF') { pos++; continue; }
                if (pos + 1 < text.Length && text[pos] == '/' && text[pos + 1] == '/')
                { while (pos < text.Length && text[pos] != '\n') pos++; continue; }
                break;
            }
            if (pos >= text.Length) return null;
            char c = text[pos++];
            if (c is '{' or '}') return c.ToString();
            var s = new StringBuilder();
            if (c == '"')
            {
                while (pos < text.Length)
                {
                    c = text[pos++];
                    if (c == '"') return s.ToString();
                    if (c == '\\' && pos < text.Length && text[pos] is '\\' or '"') c = text[pos++];
                    s.Append(c);
                }
                throw new FormatException("VDF 字符串未闭合");
            }
            s.Append(c);
            while (pos < text.Length && !char.IsWhiteSpace(text[pos]) && text[pos] is not ('{' or '}')) s.Append(text[pos++]);
            return s.ToString();
        }
        SteamVdf Read(bool nested, int depth)
        {
            if (depth > 64) throw new FormatException("VDF 嵌套过深");
            var node = new SteamVdf();
            while (Token() is { } key)
            {
                if (key == "}") { if (!nested) throw new FormatException("VDF 多余括号"); return node; }
                var value = Token() ?? throw new FormatException("VDF 缺少值");
                if (key == "{" || value == "}") throw new FormatException("VDF 格式无效");
                node.Children[key] = value == "{" ? Read(true, depth + 1) : new SteamVdf { Value = value };
            }
            if (nested) throw new FormatException("VDF 对象未闭合");
            return node;
        }
        return Read(false, 0);
    }
}

internal sealed record SteamAccount(string Id, string Login, string Name, bool Recent, string Avatar, string LastLogin)
{
    public string Details => $"{Login}  ·  SteamID {Id}";
    public string Status => Recent ? "最近使用的账号" : "本机登录记录";
}
internal sealed record SteamGame(uint AppId, string Name, long Bytes, string Folder, string Art, string Updated, string Playtime, bool Installed)
{
    public string Details => $"AppID {AppId}  ·  {SteamCatalog.Size(Bytes)}";
    public string State => Installed ? "已安装" : "文件缺失 / 库离线";
}
internal sealed record SteamLibrary(string Path, IReadOnlyList<SteamGame> Games)
{
    public string Header => $"{Path}  ·  {Games.Count} 个游戏  ·  {SteamCatalog.Size(Games.Sum(g => g.Bytes))}";
}
internal sealed record SteamSnapshot(string Root, IReadOnlyList<SteamAccount> Accounts, IReadOnlyList<SteamLibrary> Libraries, IReadOnlyList<string> Warnings);

internal static class SteamCatalog
{
    private const string FallbackArt = "https://cdn.akamai.steamstatic.com/steam/apps/{0}/header.jpg";

    public static string Size(long bytes) => bytes >= 1073741824 ? $"{bytes / 1073741824d:0.0} GB" : $"{bytes / 1048576d:0.0} MB";

    public static string? FindRoot(string? configured)
    {
        foreach (var candidate in new[] { configured,
            Registry.GetValue(@"HKEY_CURRENT_USER\Software\Valve\Steam", "SteamPath", null) as string,
            Registry.GetValue(@"HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath", null) as string,
            System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Steam") })
            if (!string.IsNullOrEmpty(candidate) && File.Exists(System.IO.Path.Combine(candidate, "steam.exe"))) return System.IO.Path.GetFullPath(candidate);
        return null;
    }

    public static SteamSnapshot Scan(string root)
    {
        var warnings = new List<string>();
        SteamVdf Read(string path)
        {
            try { return SteamVdf.Parse(File.ReadAllText(path)); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or FormatException)
            { warnings.Add($"{System.IO.Path.GetFileName(path)}：{e.Message}"); return new(); }
        }
        string LocalImage(params string[] paths) => paths.FirstOrDefault(File.Exists) is { } p ? new Uri(p).AbsoluteUri : "";
        var users = Read(System.IO.Path.Combine(root, "config", "loginusers.vdf"))["users"];
        var accounts = users.Children.Where(p => ulong.TryParse(p.Key, out _)).Select(p => new SteamAccount(p.Key,
            p.Value["AccountName"].Value, string.IsNullOrEmpty(p.Value["PersonaName"].Value) ? p.Value["AccountName"].Value : p.Value["PersonaName"].Value,
            p.Value["MostRecent"].Value == "1", LocalImage(System.IO.Path.Combine(root, "config", "avatarcache", p.Key + ".png")), Date(p.Value.Number("Timestamp"))))
            .OrderByDescending(a => a.Recent).ToList();
        var play = new SteamVdf();
        var recent = accounts.FirstOrDefault();
        if (recent != null && ulong.TryParse(recent.Id, out var steamId) && steamId >= 76561197960265728)
        {
            string local = System.IO.Path.Combine(root, "userdata", (steamId - 76561197960265728).ToString(), "config", "localconfig.vdf");
            if (File.Exists(local)) play = Read(local)["UserLocalConfigStore"]["Software"]["Valve"]["Steam"]["apps"];
        }
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { System.IO.Path.GetFullPath(root) };
        var libraryFile = System.IO.Path.Combine(root, "steamapps", "libraryfolders.vdf");
        if (File.Exists(libraryFile))
            foreach (var p in Read(libraryFile)["libraryfolders"].Children.Where(p => int.TryParse(p.Key, out _)))
            {
                var path = p.Value["path"].Value;
                if (string.IsNullOrEmpty(path)) path = p.Value.Value; // legacy format
                try { if (System.IO.Path.IsPathFullyQualified(path)) paths.Add(System.IO.Path.GetFullPath(path)); }
                catch (ArgumentException) { warnings.Add("游戏库路径无效"); }
            }
        var libraries = new List<SteamLibrary>();
        foreach (var path in paths)
        {
            var games = new List<SteamGame>();
            var apps = System.IO.Path.Combine(path, "steamapps");
            try
            {
                if (!Directory.Exists(apps)) { warnings.Add($"游戏库不可访问：{path}"); libraries.Add(new(path, games)); continue; }
                foreach (var file in Directory.EnumerateFiles(apps, "appmanifest_*.acf"))
                {
                    var app = Read(file)["AppState"];
                    if (!uint.TryParse(app["appid"].Value, out var id) || id == 0) continue;
                    var installDir = app["installdir"].Value;
                    var common = System.IO.Path.GetFullPath(System.IO.Path.Combine(apps, "common")) + System.IO.Path.DirectorySeparatorChar;
                    var folder = System.IO.Path.GetFullPath(System.IO.Path.Combine(common, installDir));
                    if (!folder.StartsWith(common, StringComparison.OrdinalIgnoreCase)) { warnings.Add($"游戏 {id} 的目录无效"); continue; }
                    var info = play[id.ToString()];
                    var minutes = info.Number("Playtime");
                    var art = LocalImage(System.IO.Path.Combine(root, "appcache", "librarycache", id + "_header.jpg"),
                        System.IO.Path.Combine(root, "appcache", "librarycache", id.ToString(), "header.jpg"));
                    if (art.Length == 0) art = string.Format(FallbackArt, id);
                    games.Add(new(id, app["name"].Value, app.Number("SizeOnDisk"), folder, art, Date(app.Number("LastUpdated")),
                        minutes > 0 ? $"{minutes / 60} 小时 {minutes % 60} 分钟 · 最近账号本机记录" : "暂无本机时长记录", Directory.Exists(folder)));
                }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException or ArgumentException) { warnings.Add($"{path}：{e.Message}"); }
            libraries.Add(new(path, games.OrderBy(g => g.Name, StringComparer.CurrentCultureIgnoreCase).DistinctBy(g => g.AppId).ToList()));
        }
        return new(root, accounts, libraries, warnings);
    }

    private static string Date(long unix)
    {
        try { return unix == 0 ? "未知" : DateTimeOffset.FromUnixTimeSeconds(unix).ToLocalTime().ToString("yyyy/MM/dd"); }
        catch (ArgumentOutOfRangeException) { return "未知"; }
    }
}

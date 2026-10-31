using System.ComponentModel;
using System.Text.Json;

namespace TubaWinUi3.Services;

public sealed class SteamAccelerationRule : INotifyPropertyChanged
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Category { get; init; } = "";
    public string[] Domains { get; init; } = [];
    public string Forward { get; init; } = "";
    public string Sni { get; init; } = "";
    public int ProxyType { get; init; }
    public bool Available => ProxyType != 4;
    public string Details => Available ? $"{Domains.Length} 个域名 · {(ProxyType == 1 ? "资源重定向" : "本地反向代理")}" : "需要 Watt 服务器授权，当前不可用";
    public string DomainSummary => string.Join("\n", Domains);
    private bool _selected;
    public bool IsSelected { get => _selected; set { if (_selected == value) return; _selected = value; PropertyChanged?.Invoke(this, new(nameof(IsSelected))); } }
    public event PropertyChangedEventHandler? PropertyChanged;
}
public sealed record SteamAccelerationGroup(string Name, string Glyph, IReadOnlyList<SteamAccelerationRule> Rules)
{
    public string CountLabel => $"{Rules.Count} 项服务";
}
public static class SteamAccelerationCatalog
{
    public static IReadOnlyList<SteamAccelerationGroup> Load()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Assets", "SteamToolbox", "watt-projectgroups.json")));
        return Parse(json.RootElement);
    }
    internal static IReadOnlyList<SteamAccelerationGroup> Parse(JsonElement root)
    {
        var data = root.GetProperty("🦓");
        var groups = new List<SteamAccelerationGroup>();
        foreach (var group in data.EnumerateArray())
        {
            string category = group.GetProperty("Name").GetString() ?? "";
            var rules = new List<SteamAccelerationRule>();
            void AddRules(JsonElement items)
            {
                foreach (var rule in items.EnumerateArray())
                {
                    string Text(string key) => rule.TryGetProperty(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
                    var domains = Text("ListenDomainNames").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .Where(d => Uri.CheckHostName(d) == UriHostNameType.Dns && !d.Contains('*')).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                    int type = rule.TryGetProperty("ProxyType", out var p) ? p.GetInt32() : 0;
                    if (domains.Length > 0)
                        rules.Add(new() { Id = Text("Id"), Name = Text("Name"), Category = category, Domains = domains, Forward = Text("ForwardDomainNames"), Sni = Text("FakeServerName"), ProxyType = type,
                            IsSelected = type != 4 && (category.Contains("Steam") || category.Equals("Github", StringComparison.OrdinalIgnoreCase)) && !Text("Name").Contains("解锁") && !Text("Name").Contains("IPv6") });
                    if (rule.TryGetProperty("Items", out var children) && children.ValueKind == JsonValueKind.Array) AddRules(children);
                }
            }
            AddRules(group.GetProperty("Items"));
            groups.Add(new(category, category switch { "Steam 服务" => "\uE7FC", "Github" => "\uE943", "Twitch 直播" => "\uE714", "网盘服务" => "\uE753", "公共 CDN" => "\uE774", "国外验证码平台" => "\uE73E", _ => "\uE12B" }, rules));
        }
        return groups;
    }
}

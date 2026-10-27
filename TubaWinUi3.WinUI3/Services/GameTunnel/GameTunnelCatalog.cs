using System.Text.Json;
using System.Text.Json.Serialization;
using TubaWinUi3.Models;

namespace TubaWinUi3.Services;

/// <summary>
/// 游戏档案目录：内置预设 + 用户自定义游戏，以及联机记录的本地存储。
/// 纯逻辑，无 UI 依赖，可单测。
/// </summary>
public static class GameTunnelCatalog
{
    /// <summary>测试用：把数据目录指到临时目录。</summary>
    public static string? DataDirOverride { get; set; }

    public static string DataDir
    {
        get
        {
            var dir = DataDirOverride ?? Path.Combine(ConfigManager.GetDataDir(), "GameTunnel");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }

    private static string CustomGamesPath => Path.Combine(DataDir, "custom_games.json");
    private static string SettingsPath => Path.Combine(DataDir, "settings.json");
    private static string ScriptsDir => Path.Combine(DataDir, "Scripts");

    public static bool IsValidPort(int port) => port is >= 1 and <= 65535;

    public static string BuildAddress(string host, int port) => $"{host}:{port}";

    // ══════════════════════ 官方 Logo 素材 ══════════════════════

    /// <summary>
    /// Steam 官方素材：logo.png 是透明底的原版字标。
    /// 两个 CDN 互为备份——国内网络环境下总有一个通。
    /// </summary>
    private static string[] SteamLogos(int appId) =>
    [
        $"https://cdn.cloudflare.steamstatic.com/steam/apps/{appId}/logo.png",
        $"https://cdn.akamai.steamstatic.com/steam/apps/{appId}/logo.png",
    ];

    /// <summary>我的世界不在 Steam 上，用官网自己的 logo 资源。</summary>
    private static string[] MinecraftLogos() =>
    [
        "https://www.minecraft.net/content/dam/minecraftnet/games/minecraft/logos/logo-minecraft.svg",
    ];

    // ══════════════════════ 内置游戏档案 ══════════════════════

    private static readonly GamePreset[] _presets =
    [
        new()
        {
            Id = "minecraft-java",
            LogoUrls = MinecraftLogos(),
            Name = "我的世界（Java 版）",
            Glyph = "\uE7FC",
            DefaultPort = 25565,
            Protocol = GameTunnelProtocol.Tcp,
            UsesLanDiscovery = true,
            Tagline = "单人世界对局域网开放，或启动服务端",
            HostAction = "进入世界后按 Esc →「对局域网开放」；专用服务器默认 25565",
            HostSteps =
            [
                "进入单人世界后按 Esc，点「对局域网开放」",
                "聊天栏会显示「本地游戏已在端口 XXXXX 上开放」——把它填进本页端口框",
                "如果你是开专用服务器（server.jar），端口默认 25565，不用改",
            ],
            GuestSteps =
            [
                "打开游戏 →「多人游戏」，稍等两秒，房间会直接出现在底部的「扫描局域网世界」里",
                "没出现时再点一次「扫描」；仍然没有就点「直接连接」填 {host}:{port}",
                "提示「无效会话」时，请从局域网列表进入（房主用正版账号开放时，离线账号只能走这条路）",
            ],
            GuestEntryPoint = "多人游戏 → 局域网世界（自动出现）",
            Note = "Java 版要求朋友和你的游戏版本一致；装了 Mod 的话两边 Mod 也要一致。",
        },
        new()
        {
            Id = "minecraft-bedrock",
            LogoUrls = MinecraftLogos(),
            Name = "我的世界（基岩版）",
            Glyph = "\uE7F4",
            DefaultPort = 19132,
            Protocol = GameTunnelProtocol.Udp,
            Tagline = "Win10 / 手机 / 主机版世界，可被局域网发现",
            HostAction = "打开世界 → 编辑设置里打开「对局域网游戏可见」",
            HostSteps =
            [
                "选择要联机的世界 → 编辑 → 游戏设置",
                "打开「对局域网游戏可见」并保存，进入世界",
                "官方服务端（bedrock_server.exe）端口默认 19132，不用改",
            ],
            GuestSteps =
            [
                "打开游戏 →「游戏」→「好友」页签，稍等几秒会自动出现主机的房间",
                "没看到房间时，选「添加服务器」→ 地址填 {host}:{port}",
                "手机版同样可以连（同一个网络里就能互相看见）",
            ],
            GuestEntryPoint = "游戏 → 好友（或添加服务器）",
            Note = "基岩版走 UDP，和朋友要登录同一个 Xbox 账号家族以外没有额外要求。",
        },
        new()
        {
            Id = "terraria",
            LogoUrls = SteamLogos(105600),
            Name = "泰拉瑞亚",
            Glyph = "\uE945",
            DefaultPort = 7777,
            Protocol = GameTunnelProtocol.Tcp,
            Tagline = "开服并开始游戏，端口默认 7777",
            HostAction = "主菜单 →「多人游戏」→「开服并开始游戏」",
            HostSteps =
            [
                "主菜单点「多人游戏」→「开服并开始游戏」",
                "选择角色和世界，点「开始游戏」",
                "端口默认 7777，不用改；被别人占用时会提示，改成别的再填进本页",
            ],
            GuestSteps =
            [
                "主菜单点「多人游戏」→「加入游戏」",
                "选角色后点「通过 IP 加入」",
                "地址填 {host}:{port}，端口就是 {port}",
            ],
            GuestEntryPoint = "多人游戏 → 通过 IP 加入",
            Note = "朋友需要先创建好同版本的角色；跨版本（1.4 与 1.3）无法互相加入。",
        },
        new()
        {
            Id = "tmodloader",
            LogoUrls = SteamLogos(1281930),
            Name = "泰拉瑞亚 tModLoader",
            Glyph = "\uE9F5",
            DefaultPort = 7777,
            Protocol = GameTunnelProtocol.Tcp,
            Tagline = "Mod 联机，端口同样 7777",
            HostAction = "主菜单 →「多人游戏」→「开服并开始游戏」",
            HostSteps =
            [
                "主菜单点「多人游戏」→「开服并开始游戏」",
                "选择角色与世界开始游戏",
                "端口默认 7777，可在「设置 → 服务器」里查看",
            ],
            GuestSteps =
            [
                "主菜单点「多人游戏」→「加入游戏」→「通过 IP 加入」",
                "地址填 {host}:{port}",
                "进入时会自动下载服务器要求的 Mod（需要两边 tModLoader 版本一致）",
            ],
            GuestEntryPoint = "多人游戏 → 通过 IP 加入",
            Note = "两边 tModLoader 版本必须一致，Mod 版本不一致会连不上。",
        },
        new()
        {
            Id = "stardew",
            LogoUrls = SteamLogos(413150),
            Name = "星露谷物语",
            Glyph = "\uE735",
            DefaultPort = 24642,
            Protocol = GameTunnelProtocol.Udp,
            Tagline = "合作模式主持农场，端口 24642",
            HostAction = "主菜单 →「合作」→「主持」→ 选择农场",
            HostSteps =
            [
                "主菜单点「合作」→「主持」→ 新建或加载农场",
                "进入游戏后按 Esc →「选项」可在右上角看到 IP 与端口（默认 24642）",
                "把「仅限好友」的勾保持默认即可，本工具不依赖 Steam 好友",
            ],
            GuestSteps =
            [
                "主菜单点「合作」→「加入局域网游戏」",
                "在列表里选择主机的农场；列表为空时点「输入 IP」填 {host}:{port}",
                "同一台机器第一次加入可能需要先在游戏里开启一次联机",
            ],
            GuestEntryPoint = "合作 → 加入局域网游戏 / 输入 IP",
            Note = "星露谷用 UDP 24642，Mod（SMAPI）联机需要两边 Mod 列表一致。",
        },
        new()
        {
            Id = "dst",
            LogoUrls = SteamLogos(322330),
            Name = "饥荒联机版",
            Glyph = "\uECAD",
            DefaultPort = 10999,
            Protocol = GameTunnelProtocol.Udp,
            Tagline = "创建世界后直接联机，端口 10999",
            HostAction = "主菜单 →「创建世界」→ 生成后「开始游戏」",
            HostSteps =
            [
                "主菜单点「创建世界」，选好角色与地图后开始游戏",
                "进入游戏后按 Esc 可以邀请；端口默认 10999",
                "如果要开洞穴，额外需要 10888 / 10889 端口，建议先用「自定义游戏」把 10888 也加进来",
            ],
            GuestSteps =
            [
                "主菜单点「浏览世界」→「加入游戏」",
                "在「局域网」页签里应能直接看到主机的世界",
                "没有出现时切到「直连」页签，地址填 {host}:{port}",
            ],
            GuestEntryPoint = "浏览世界 → 局域网 / 直连",
            Note = "饥荒的世界会持续运行，主机退出后世界会暂停。",
        },
        new()
        {
            Id = "valheim",
            LogoUrls = SteamLogos(892970),
            Name = "英灵神殿",
            Glyph = "\uEA18",
            DefaultPort = 2456,
            Protocol = GameTunnelProtocol.Udp,
            Tagline = "直接开世界，端口 2456",
            HostAction = "世界列表里选择世界 →「开始游戏」（需要先在 Steam 里启动游戏）",
            HostSteps =
            [
                "在 Steam 里启动英灵神殿，选择世界后点「开始游戏」",
                "进入游戏后按 Esc → 记下左下角「加入 IP」里的端口（默认 2456）",
                "把端口填进本页即可",
            ],
            GuestSteps =
            [
                "游戏主菜单点「加入游戏」→「加入 IP」",
                "地址填 {host}:{port}",
                "连接超时通常是主机还没进入世界，让主机先进游戏",
            ],
            GuestEntryPoint = "加入游戏 → 加入 IP",
            Note = "英灵神殿支持 2456-2458 三个端口，一般只用 2456。",
        },
        new()
        {
            Id = "palworld",
            LogoUrls = SteamLogos(1623730),
            Name = "幻兽帕鲁",
            Glyph = "\uE7FC",
            DefaultPort = 8211,
            Protocol = GameTunnelProtocol.Udp,
            Tagline = "多人世界或专用服务端，端口 8211",
            HostAction = "主菜单 →「开始游戏」（多人）→ 世界设置里确认多人已开启",
            HostSteps =
            [
                "主菜单点「开始游戏」，选择世界进入（多人模式）",
                "专用服务端（PalServer.exe）端口默认 8211，配置在 PalWorldSettings.ini",
                "把端口填进本页",
            ],
            GuestSteps =
            [
                "主菜单点「加入多人游戏（专用服务器）」",
                "地址填 {host}:{port}，点「联系」后加入",
                "进不去时确认游戏版本与主机一致",
            ],
            GuestEntryPoint = "加入多人游戏（专用服务器）",
            Note = "幻兽帕鲁是 UDP 8211；专用服务端需要额外的 query 端口 27015。",
        },
        new()
        {
            Id = "starbound",
            LogoUrls = SteamLogos(211820),
            Name = "星界边境",
            Glyph = "\uE8B2",
            DefaultPort = 21025,
            Protocol = GameTunnelProtocol.Tcp,
            Tagline = "创建服务器，端口 21025",
            HostAction = "主菜单 →「多人游戏」→「创建服务器」",
            HostSteps =
            [
                "主菜单点「多人游戏」→「创建服务器」",
                "选择角色后开始，端口默认 21025",
                "把端口填进本页",
            ],
            GuestSteps =
            [
                "主菜单点「多人游戏」→「加入服务器」",
                "地址填 {host}:{port}，密码留空",
            ],
            GuestEntryPoint = "多人游戏 → 加入服务器",
        },
        new()
        {
            Id = "factorio",
            LogoUrls = SteamLogos(427520),
            Name = "异星工厂",
            Glyph = "\uE90F",
            DefaultPort = 34197,
            Protocol = GameTunnelProtocol.Udp,
            Tagline = "局域网可见的多人游戏，端口 34197",
            HostAction = "主菜单 →「多人游戏」→ 新建/载入后勾选「通过局域网可见」",
            HostSteps =
            [
                "主菜单点「多人游戏」→「新建游戏」或「载入游戏」",
                "在配置界面勾选「通过局域网（LAN）游戏可见」",
                "端口默认 34197，可在设置 →「其他」里查看",
            ],
            GuestSteps =
            [
                "主菜单点「多人游戏」→「加入游戏」",
                "在局域网列表里选择主机的游戏；没有出现时点「直接连接」填 {host}:{port}",
            ],
            GuestEntryPoint = "多人游戏 → 加入游戏 / 直接连接",
            Note = "异星工厂对延迟敏感，建议先用本页的「网络检测」确认走的是直连。",
        },
    ];

    public static IReadOnlyList<GamePreset> Presets => _presets;

    public static GamePreset? FindPreset(string? id)
        => id is null ? null : _presets.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

    // ══════════════════════ 联机节点 ══════════════════════

    /// <summary>
    /// 帮同一房间的玩家互相发现的公共节点。玩家只有在共用至少一个节点时才能找到对方，
    /// 所以这个列表应当只增不改。
    /// </summary>
    public static readonly string[] DefaultNodes =
    [
        "tcp://public.easytier.top:11010",
        "tcp://public2.easytier.cn:54321",
        "https://etnode.zkitefly.eu.org/node1",
        "https://etnode.zkitefly.eu.org/node2",
    ];

    /// <summary>自定义节点在前，再补内置节点，去重。</summary>
    public static IReadOnlyList<string> GetNodes(GameTunnelSettings settings)
    {
        var nodes = ParseNodes(settings.CustomNodes, out _);
        return (settings.UsePublicNodes ? nodes.Concat(DefaultNodes) : nodes)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// 把用户输入拆成节点地址：空格/逗号/分号/换行分隔，裸 host:port 按 TCP 处理。
    /// 非法项通过 <paramref name="invalid"/> 返回。
    /// </summary>
    public static IReadOnlyList<string> ParseNodes(string? text, out IReadOnlyList<string> invalid)
    {
        var nodes = new List<string>();
        var rejected = new List<string>();
        invalid = rejected;
        if (string.IsNullOrWhiteSpace(text)) return nodes;

        var separators = new[] { ' ', '\t', '\r', '\n', ',', ';', '，', '；', '、' };
        foreach (var part in text.Split(separators, StringSplitOptions.RemoveEmptyEntries))
        {
            var node = part.Contains("://") ? part : "tcp://" + part;
            if (Uri.TryCreate(node, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host)
                && uri.Scheme is "tcp" or "udp" or "ws" or "wss" or "quic" or "http" or "https")
            {
                nodes.Add(node);
            }
            else
            {
                rejected.Add(part);
            }
        }
        return nodes;
    }

    // ══════════════════════ 自定义游戏 ══════════════════════

    public static List<CustomGame> LoadCustomGames()
    {
        try
        {
            if (!File.Exists(CustomGamesPath)) return [];
            var list = JsonSerializer.Deserialize<List<CustomGame>>(File.ReadAllText(CustomGamesPath)) ?? [];
            return list.Where(g => !string.IsNullOrWhiteSpace(g.Name) && IsValidPort(g.Port)).ToList();
        }
        catch
        {
            return [];
        }
    }

    public static void SaveCustomGames(List<CustomGame> games)
    {
        try
        {
            File.WriteAllText(CustomGamesPath, JsonSerializer.Serialize(games, JsonOptions));
        }
        catch
        {
        }
    }

    public static CustomGame? FindCustomGame(string? id)
        => id is null ? null : LoadCustomGames().FirstOrDefault(g => g.Id == id);

    public static string NewCustomGameId() => "custom-" + Guid.NewGuid().ToString("N")[..8];

    // ══════════════════════ 本次联机 ══════════════════════

    /// <summary>
    /// 本次运行里最近一次建好的联机信息——主页「查看当前邀请」用它直接回到邀请那一步。
    /// 不落盘：邀请密钥两小时就过期，重启后重新生成比翻出旧的更靠谱。
    /// </summary>
    public static InviteInfo? CurrentInvite { get; set; }

    // ══════════════════════ 设置 ══════════════════════

    public static GameTunnelSettings LoadSettings()
    {
        try
        {
            if (!File.Exists(SettingsPath)) return new GameTunnelSettings();
            return JsonSerializer.Deserialize<GameTunnelSettings>(File.ReadAllText(SettingsPath)) ?? new GameTunnelSettings();
        }
        catch
        {
            return new GameTunnelSettings();
        }
    }

    public static void SaveSettings(GameTunnelSettings settings)
    {
        try
        {
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
        }
        catch
        {
        }
    }

    /// <summary>生成的加入脚本落盘目录（同时也会复制到用户选择的位置）。</summary>
    public static string GetScriptsDir()
    {
        Directory.CreateDirectory(ScriptsDir);
        return ScriptsDir;
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };
}

/// <summary>本工具自己的持久化设置（与全局 AppSettings 分开，避免污染）。</summary>
public sealed class GameTunnelSettings
{
    public string? LastPresetId { get; set; }

    public int LastPort { get; set; }

    /// <summary>直连打不通时允许经中转节点传输。</summary>
    public bool AllowRelay { get; set; } = true;

    /// <summary>不直连任何成员，全部流量经中转，其他玩家无从得知本机 IP。</summary>
    public bool HideAddress { get; set; }

    /// <summary>使用内置公共发现节点。</summary>
    public bool UsePublicNodes { get; set; } = true;

    /// <summary>额外节点，空格/逗号/换行分隔；裸 host:port 按 TCP 处理。</summary>
    public string? CustomNodes { get; set; }

    /// <summary>把房间也分享给本机所在局域网的其他设备（默认只在真正需要时打开）。</summary>
    public bool ShareWithLan { get; set; }

    /// <summary>加入时使用的本地端口，0 = 自动。</summary>
    public int LocalPort { get; set; }

    /// <summary>找房主的超时时间（秒）。</summary>
    public int SearchTimeoutSeconds { get; set; } = 60;

    /// <summary>首次联机时自动为联机组件添加防火墙入站规则。</summary>
    public bool AutoFirewall { get; set; } = true;
}

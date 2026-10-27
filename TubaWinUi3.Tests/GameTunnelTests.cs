using System.Text;
using TubaWinUi3.Models;
using TubaWinUi3.Pages;
using TubaWinUi3.Services;

namespace TubaWinUi3.Tests;

/// <summary>
/// 游戏联机助手的纯逻辑回归：房间码、EasyTier 配置、设备解析、邀请码、脚本生成、游戏档案。
/// 不依赖本机是否装了联机组件，也不会真的发起联机。
/// </summary>
public class GameTunnelTests : IDisposable
{
    private readonly string _tempDir;

    public GameTunnelTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "TubaGameTunnel_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        GameTunnelCatalog.DataDirOverride = _tempDir;
        try { Encoding.RegisterProvider(CodePagesEncodingProvider.Instance); } catch { }
    }

    public void Dispose()
    {
        GameTunnelCatalog.DataDirOverride = null;
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    // ══════════════════ 房间码 ══════════════════

    [Fact]
    public void RoomCode_CreateProducesUsableCode()
    {
        var code = TunnelRoomCode.Create();

        Assert.StartsWith("U/", code.Code);
        Assert.Equal(21, code.Code.Length);
        Assert.Equal(18, code.Code.Count(c => c != '-'));

        // 房间码同时就是网络名与密钥：前半段进网络名，后半段进密钥。
        Assert.StartsWith("tuba-", code.NetworkName);
        Assert.Equal(14, code.NetworkName.Length);
        Assert.Equal(9, code.NetworkSecret.Length);
        Assert.Contains('-', code.NetworkSecret);

        Assert.Equal(code.Code, TunnelRoomCode.Find(code.Code)?.Code);
        Assert.Equal(code.Code, TunnelRoomCode.TryParse(code.Code)?.Code);
    }

    [Theory]
    [InlineData("U/0000-0000-0000-0001")] // 不是 7 的倍数
    [InlineData("U/0000-0000-0000-0002")]
    [InlineData("U/0000-0000-0000-0003")]
    public void RoomCode_RejectsBadChecksum(string text)
        => Assert.Null(TunnelRoomCode.TryParse(text));

    [Fact]
    public void RoomCode_ToleratesLookAlikeLetters()
    {
        // 全 0 是 7 的倍数；I/O 必须按 1/0 读
        var code = TunnelRoomCode.Find("U/OOOO-OOOO-OOOO-OOOO");

        Assert.NotNull(code);
        Assert.Equal("U/0000-0000-0000-0000", code!.Code);
        Assert.Equal("U/0000-0000-0000-0000", TunnelRoomCode.Find("我的房间 U/0000-0000-0000-0000 快来")?.Code);
    }

    [Fact]
    public void RoomCode_FindRejectsGarbage()
    {
        Assert.Null(TunnelRoomCode.Find(""));
        Assert.Null(TunnelRoomCode.Find("没有房间码"));
        Assert.Null(TunnelRoomCode.Find("U/0000-0000-0000-0002"));
    }

    // ══════════════════ EasyTier 配置 ══════════════════

    private static EasyTierConfig HostConfig(int port = 25565) => new()
    {
        NetworkName = "tuba-ABCD-EFGH",
        NetworkSecret = "IJKL-MNOP",
        Hostname = TunnelSession.HostNamePrefix + port,
        IPv4 = InviteInfo.HostAddress,
        TcpWhitelist = { port.ToString() },
        UdpWhitelist = { port.ToString() },
        Peers = { "tcp://public.easytier.top:11010" }
    };

    [Fact]
    public void EasyTierConfig_HostHasFixedAddressAndOpenPorts()
    {
        var toml = HostConfig().ToToml();

        Assert.Contains("hostname = \"tuba-host-25565\"", toml);
        Assert.Contains("ipv4 = \"10.144.144.1\"", toml);
        Assert.Contains("tcp_whitelist = [\"25565\"]", toml);
        Assert.Contains("udp_whitelist = [\"25565\"]", toml);
        Assert.Contains("network_name = \"tuba-ABCD-EFGH\"", toml);
        Assert.Contains("network_secret = \"IJKL-MNOP\"", toml);
        Assert.Contains("uri = \"tcp://public.easytier.top:11010\"", toml);
    }

    [Fact]
    public void EasyTierConfig_UsesNoTunAndPrivateMode()
    {
        var toml = HostConfig().ToToml();

        Assert.Contains("no_tun = true", toml);
        Assert.Contains("private_mode = true", toml);
        Assert.Contains("latency_first = true", toml);
        Assert.Contains("enable_kcp_proxy = true", toml);
        Assert.Contains("enable_quic_proxy = true", toml);
        Assert.Contains("data_compress_algo = 2", toml);
        Assert.Contains("listeners = [\"tcp://0.0.0.0:0\", \"udp://0.0.0.0:0\"]", toml);
        Assert.Contains("p2p_only = false", toml);
    }

    [Fact]
    public void EasyTierConfig_GuestBlocksEveryInboundPort()
    {
        var config = new EasyTierConfig
        {
            NetworkName = "tuba-ABCD-EFGH",
            NetworkSecret = "IJKL-MNOP",
            Hostname = "tuba-guest-1a2b",
            TcpWhitelist = { "0" },
            UdpWhitelist = { "0" }
        };

        var toml = config.ToToml();

        Assert.Contains("dhcp = true", toml);
        Assert.DoesNotContain("ipv4 =", toml);
        Assert.Contains("tcp_whitelist = [\"0\"]", toml);
        Assert.Contains("udp_whitelist = [\"0\"]", toml);
    }

    [Fact]
    public void EasyTierConfig_DirectOnlySetsP2pOnly()
    {
        var config = HostConfig();
        config.DirectOnly = true;

        Assert.Contains("p2p_only = true", config.ToToml());
    }

    [Fact]
    public void EasyTierConfig_HideAddressDisablesEveryDirectPath()
    {
        var config = HostConfig();
        config.HideAddress = true;
        // 隐藏地址时即使勾了「仅直连」也不能真的只直连（根本没有直连）
        config.DirectOnly = true;

        var toml = config.ToToml();
        Assert.Contains("listeners = []", toml);
        Assert.Contains("disable_p2p = true", toml);
        Assert.Contains("disable_udp_hole_punching = true", toml);
        Assert.Contains("disable_tcp_hole_punching = true", toml);
        Assert.Contains("disable_sym_hole_punching = true", toml);
        Assert.Contains("disable_upnp = true", toml);
        Assert.Contains("p2p_only = false", toml);
    }

    [Fact]
    public void EasyTierConfig_QuotesAreEscaped()
    {
        var config = HostConfig();
        config.Hostname = "tuba-\"x\"\\y";

        var toml = config.ToToml();
        Assert.Contains("hostname = \"tuba-\\\"x\\\"\\\\y\"", toml);
    }

    // ══════════════════ EasyTier 设备解析 ══════════════════

    [Fact]
    public void EasyTierPeer_ParsesRealPeerJson()
    {
        const string json = """
        [
          {
            "hostname": "tuba-host-25565",
            "ipv4": "10.144.144.1",
            "cost": "p2p",
            "lat_ms": 12.5,
            "tunnel_proto": "udp",
            "nat_type": "FullCone"
          },
          {
            "hostname": "tuba-guest-1a2b",
            "ipv4": "10.144.144.214",
            "cost": "relay",
            "lat_ms": 88.0,
            "tunnel_proto": "tcp",
            "nat_type": "Symmetric"
          },
          {
            "hostname": "my-pc",
            "ipv4": "10.144.144.99",
            "cost": "Local",
            "lat_ms": 0.0,
            "tunnel_proto": ""
          },
          {
            "hostname": "public.easytier.top",
            "ipv4": "",
            "cost": "relay"
          }
        ]
        """;

        var peers = EasyTierPeer.ParseList(json);

        Assert.Equal(4, peers.Count);
        Assert.True(peers[0].IsDirect);
        Assert.Equal(12.5, peers[0].LatencyMs);
        Assert.Equal(NatKind.FullCone, peers[0].Nat);
        Assert.True(peers[0].IsMember);

        Assert.False(peers[1].IsDirect);
        Assert.Equal(NatKind.Symmetric, peers[1].Nat);

        Assert.True(peers[2].IsLocal);
        Assert.Equal("本机", peers[2].Describe());

        // 公共节点没有虚拟地址，不算网络成员
        Assert.False(peers[3].IsMember);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("{}")]
    [InlineData("null")]
    public void EasyTierPeer_RejectsGarbage(string json)
        => Assert.Empty(EasyTierPeer.ParseList(json));

    [Fact]
    public void EasyTierPeer_DescribesConnectionKind()
    {
        var direct = EasyTierPeer.ParseList("""[{"ipv4":"10.0.0.2","cost":"p2p","lat_ms":9}]""")[0];
        Assert.Contains("直连", direct.Describe());

        var relay = EasyTierPeer.ParseList("""[{"ipv4":"10.0.0.2","cost":"relay","lat_ms":120}]""")[0];
        Assert.Contains("中转", relay.Describe());
    }

    [Fact]
    public void HostPort_IsEncodedInHostname()
    {
        Assert.Equal(25565, TunnelSession.PortOfHost(new EasyTierPeer { Hostname = "tuba-host-25565", IPv4 = "10.144.144.1" }));
        Assert.Equal(7777, TunnelSession.PortOfHost(new EasyTierPeer { Hostname = "tuba-host-7777", IPv4 = "10.144.144.1" }));
        Assert.Equal(0, TunnelSession.PortOfHost(new EasyTierPeer { Hostname = "tuba-guest-1a2b" }));
        Assert.Equal(0, TunnelSession.PortOfHost(new EasyTierPeer { Hostname = "tuba-host-nope" }));
    }

    [Fact]
    public void NatKinds_AreTranslated()
    {
        Assert.Equal(NatKind.Open, EasyTierPeer.ParseNat("OpenInternet"));
        Assert.Equal(NatKind.Symmetric, EasyTierPeer.ParseNat("SymmetricEasyInc"));
        Assert.Equal(NatKind.Unknown, EasyTierPeer.ParseNat("???"));
        Assert.Contains("对称", NatKind.Symmetric.Describe());
    }

    // ══════════════════ 联机组件包 ══════════════════

    [Theory]
    [InlineData("x86_64")]
    [InlineData("arm64")]
    [InlineData("i686")]
    public void PackageAssetName_FollowsOfficialRelease(string architecture)
    {
        Assert.Equal($"easytier-windows-{architecture}-v{EasyTierPackage.Version}.zip",
            EasyTierPackage.AssetName(architecture));
    }

    [Fact]
    public void PackageShipsExactlyThreeVerifiedFiles()
    {
        Assert.Equal(["easytier-core.exe", "easytier-cli.exe", "Packet.dll"], EasyTierPackage.FileNames);

        foreach (var architecture in new[] { "x86_64", "arm64", "i686" })
        {
            foreach (var file in EasyTierPackage.FileNames)
            {
                var hash = EasyTierPackage.ExpectedHash(architecture, file);
                Assert.Equal(64, hash.Length);
                Assert.True(hash.All(Uri.IsHexDigit), $"{architecture}/{file} 的哈希不是 16 进制");
            }
        }
    }

    [Theory]
    [InlineData("easytier-core.exe", "easytier-core.exe", true)]
    [InlineData("folder/easytier-core.exe", "easytier-core.exe", true)]
    [InlineData("folder\\easytier-core.exe", "easytier-core.exe", true)]
    [InlineData("folder/easytier-cli.exe", "easytier-core.exe", false)]
    public void PackageEntryMatcher_AcceptsNestedPaths(string entry, string file, bool expected)
        => Assert.Equal(expected, EasyTierPackage.MatchesEntry(entry, file));

    [Fact]
    public void PackageSourceUrls_PreferMirrors()
    {
        var urls = EasyTierPackage.SourceUrls("x86_64").ToList();

        Assert.Contains(urls, url => url.Contains("gitee.com/easytier/EasyTier", StringComparison.Ordinal));
        Assert.Contains(urls, url => url.Contains("github.com/EasyTier/EasyTier", StringComparison.Ordinal));
        Assert.Contains(urls, url => url.Contains("ghfast.top", StringComparison.Ordinal));
        Assert.Contains(urls, url => url.Contains("gh-proxy.com", StringComparison.Ordinal));
        Assert.All(urls, url => Assert.Contains($"easytier-windows-x86_64-v{EasyTierPackage.Version}.zip", url));
    }

    // ══════════════════ 联机节点 ══════════════════

    [Fact]
    public void Nodes_DefaultListIsReachableAndComplete()
    {
        var settings = new GameTunnelSettings();
        var nodes = GameTunnelCatalog.GetNodes(settings);

        Assert.Equal(4, nodes.Count);
        Assert.Contains("tcp://public.easytier.top:11010", nodes);
    }

    [Fact]
    public void Nodes_CustomEntriesComeFirstAndBareHostBecomesTcp()
    {
        var settings = new GameTunnelSettings { CustomNodes = "1.2.3.4:11010, quic://node.example:11011" };
        var nodes = GameTunnelCatalog.GetNodes(settings);

        Assert.Equal("tcp://1.2.3.4:11010", nodes[0]);
        Assert.Contains("quic://node.example:11011", nodes);
        Assert.Contains("tcp://public.easytier.top:11010", nodes);
    }

    [Fact]
    public void Nodes_PublicNodesCanBeDisabled()
    {
        var settings = new GameTunnelSettings { UsePublicNodes = false, CustomNodes = "tcp://mine:11010" };
        Assert.Equal(["tcp://mine:11010"], GameTunnelCatalog.GetNodes(settings));
    }

    [Fact]
    public void Nodes_ParseReportsInvalidEntries()
    {
        GameTunnelCatalog.ParseNodes("bad!!, tcp://ok:1", out var invalid);
        Assert.Single(invalid);
    }

    // ══════════════════ 内置游戏档案 ══════════════════

    [Fact]
    public void Presets_AreConsistent()
    {
        var presets = GameTunnelCatalog.Presets;

        Assert.NotEmpty(presets);
        Assert.Equal(presets.Count, presets.Select(p => p.Id).Distinct().Count());
        Assert.Equal(presets.Count, presets.Select(p => p.Name).Distinct().Count());

        foreach (var preset in presets)
        {
            Assert.True(GameTunnelCatalog.IsValidPort(preset.DefaultPort), $"{preset.Id} 端口非法");
            Assert.NotEmpty(preset.HostSteps);
            Assert.NotEmpty(preset.GuestSteps);
            Assert.NotEmpty(preset.Glyph);
            Assert.False(string.IsNullOrWhiteSpace(preset.Tagline), $"{preset.Id} 缺少一句话说明");
            Assert.Contains("{host}", string.Join('\n', preset.GuestSteps));
        }
    }

    [Fact]
    public void Presets_CoverMinecraftAndTerraria()
    {
        Assert.Equal(25565, GameTunnelCatalog.FindPreset("minecraft-java")!.DefaultPort);
        Assert.Equal(7777, GameTunnelCatalog.FindPreset("terraria")!.DefaultPort);
        Assert.Equal(GameTunnelProtocol.Udp, GameTunnelCatalog.FindPreset("minecraft-bedrock")!.Protocol);
        Assert.Null(GameTunnelCatalog.FindPreset("nope"));
    }

    [Fact]
    public void MinecraftJavaUsesLanDiscovery()
    {
        Assert.True(GameTunnelCatalog.FindPreset("minecraft-java")!.UsesLanDiscovery);
        Assert.False(GameTunnelCatalog.FindPreset("terraria")!.UsesLanDiscovery);
    }

    [Theory]
    [InlineData(1, true)]
    [InlineData(25565, true)]
    [InlineData(65535, true)]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(65536, false)]
    public void IsValidPort_Validates(int port, bool expected)
        => Assert.Equal(expected, GameTunnelCatalog.IsValidPort(port));

    [Fact]
    public void ProtocolExtensions_DescribeTransports()
    {
        Assert.True(GameTunnelProtocol.Tcp.UsesTcp());
        Assert.False(GameTunnelProtocol.Tcp.UsesUdp());
        Assert.True(GameTunnelProtocol.TcpAndUdp.UsesTcp());
        Assert.True(GameTunnelProtocol.TcpAndUdp.UsesUdp());
        Assert.Equal("TCP + UDP", GameTunnelProtocol.TcpAndUdp.Describe());
    }

    // ══════════════════ 本地存储 ══════════════════

    [Fact]
    public void CustomGames_RoundTrip()
    {
        Assert.Empty(GameTunnelCatalog.LoadCustomGames());

        GameTunnelCatalog.SaveCustomGames(
        [
            new CustomGame { Id = "custom-1", Name = "测试游戏", Port = 9000, Protocol = GameTunnelProtocol.Udp }
        ]);

        var loaded = GameTunnelCatalog.LoadCustomGames();
        var game = Assert.Single(loaded);
        Assert.Equal("测试游戏", game.Name);
        Assert.Equal(9000, game.Port);
        Assert.Equal(GameTunnelProtocol.Udp, game.Protocol);
        Assert.Equal("测试游戏", GameTunnelCatalog.FindCustomGame("custom-1")!.Name);
    }

    [Fact]
    public void CustomGames_DropsInvalidEntries()
    {
        GameTunnelCatalog.SaveCustomGames(
        [
            new CustomGame { Id = "ok", Name = "合法", Port = 1234 },
            new CustomGame { Id = "bad", Name = "", Port = 1234 },
            new CustomGame { Id = "bad2", Name = "端口越界", Port = 99999 }
        ]);

        Assert.Single(GameTunnelCatalog.LoadCustomGames());
    }

    [Fact]
    public void Settings_RoundTrip()
    {
        var settings = GameTunnelCatalog.LoadSettings();
        Assert.True(settings.AllowRelay);
        Assert.True(settings.UsePublicNodes);
        Assert.True(settings.AutoFirewall);
        Assert.False(settings.HideAddress);
        Assert.False(settings.ShareWithLan);
        Assert.Equal(60, settings.SearchTimeoutSeconds);

        settings.AllowRelay = false;
        settings.HideAddress = true;
        settings.CustomNodes = "tcp://mine:11010";
        settings.ShareWithLan = true;
        settings.LocalPort = 25570;
        settings.SearchTimeoutSeconds = 30;
        settings.LastPresetId = "terraria";
        settings.LastPort = 7777;
        GameTunnelCatalog.SaveSettings(settings);

        var reloaded = GameTunnelCatalog.LoadSettings();
        Assert.False(reloaded.AllowRelay);
        Assert.True(reloaded.HideAddress);
        Assert.Equal("tcp://mine:11010", reloaded.CustomNodes);
        Assert.True(reloaded.ShareWithLan);
        Assert.Equal(25570, reloaded.LocalPort);
        Assert.Equal(30, reloaded.SearchTimeoutSeconds);
        Assert.Equal("terraria", reloaded.LastPresetId);
        Assert.Equal(7777, reloaded.LastPort);
    }

    // ══════════════════ 邀请码 ══════════════════

    private static InviteInfo MakeInvite()
    {
        var room = TunnelRoomCode.Create();
        return new InviteInfo
        {
            RoomCode = room.Code,
            NetworkName = room.NetworkName,
            NetworkSecret = room.NetworkSecret,
            Port = 25565,
            Game = "我的世界（Java 版）",
            Protocol = GameTunnelProtocol.Udp,
            UsesLanDiscovery = true
        };
    }

    [Fact]
    public void InviteCode_RoundTrips()
    {
        var info = MakeInvite();
        var code = GameTunnelInvite.Encode(info);

        Assert.StartsWith(GameTunnelInvite.CodePrefix, code);

        var decoded = GameTunnelInvite.Decode(code);
        Assert.NotNull(decoded);
        Assert.Equal(info.RoomCode, decoded!.RoomCode);
        Assert.Equal(info.NetworkName, decoded.NetworkName);
        Assert.Equal(info.NetworkSecret, decoded.NetworkSecret);
        Assert.Equal(info.Port, decoded.Port);
        Assert.Equal(info.Game, decoded.Game);
        Assert.Equal(GameTunnelProtocol.Udp, decoded.Protocol);
        Assert.True(decoded.UsesLanDiscovery);
        Assert.Equal($"{InviteInfo.HostAddress}:25565", decoded.Address);
    }

    [Fact]
    public void InviteCode_DecodesFromFullInviteMessage()
    {
        var info = MakeInvite();
        var text = GameTunnelInvite.BuildInviteText(info, GameTunnelInvite.Encode(info), hasScript: true);

        var decoded = GameTunnelInvite.Decode(text);
        Assert.NotNull(decoded);
        Assert.Equal(info.RoomCode, decoded!.RoomCode);
        Assert.Equal(25565, decoded.Port);
    }

    [Fact]
    public void InviteCode_AcceptsBareRoomCode()
    {
        var room = TunnelRoomCode.Create();
        var decoded = GameTunnelInvite.Decode("房间码 " + room.Code + " 快来");

        Assert.NotNull(decoded);
        Assert.Equal(room.Code, decoded!.RoomCode);
        Assert.Equal(room.NetworkName, decoded.NetworkName);
        Assert.Equal(0, decoded.Port); // 端口由房主主机名在运行时给出
        Assert.Equal(GameTunnelProtocol.TcpAndUdp, decoded.Protocol); // 协议未知时两条转发都建
    }

    [Theory]
    [InlineData("")]
    [InlineData("随便一段话")]
    [InlineData("TBG2:not-base64!!!")]
    [InlineData("TBG1:eyJoIjoiMTAwLjEifQ")]
    [InlineData("100.101.102.103:25565")] // EasyTier 下纯地址没有意义
    public void InviteCode_RejectsGarbageAndPlainAddress(string text)
        => Assert.Null(GameTunnelInvite.Decode(text));

    [Fact]
    public void BuildInviteText_MentionsRoomCodeAndNoAccount()
    {
        var info = MakeInvite();
        var code = GameTunnelInvite.Encode(info);

        var text = GameTunnelInvite.BuildInviteText(info, code, hasScript: true);

        Assert.Contains(info.RoomCode, text);
        Assert.Contains(InviteInfo.HostAddress + ":25565", text);
        Assert.Contains(code, text);
        Assert.Contains("一键加入联机.cmd", text);
        Assert.Contains("我的世界", text);
        Assert.Contains("不需要我批准", text);
        Assert.DoesNotContain("Tailscale", text);
        Assert.DoesNotContain("tskey", text);
    }

    [Fact]
    public void BuildInviteText_UsesLanDiscoveryHintForMinecraft()
    {
        var info = MakeInvite();
        var text = GameTunnelInvite.BuildInviteText(info, null, hasScript: false);
        Assert.Contains("扫描局域网世界", text);
    }

    // ══════════════════ 一键加入脚本 ══════════════════

    [Fact]
    public void BuildScript_DownloadsComponentAndForwardsPort()
    {
        var info = MakeInvite();
        var script = GameTunnelInvite.BuildScript(info);

        Assert.Contains($"set \"NETNAME={info.NetworkName}\"", script);
        Assert.Contains($"set \"NETSEC={info.NetworkSecret}\"", script);
        Assert.Contains("set \"PORT=25565\"", script);
        Assert.Contains("set \"HOSTIP=10.144.144.1\"", script);
        Assert.Contains($"easytier-windows-%ARCH%-v{EasyTierPackage.Version}.zip", script);
        Assert.Contains("Expand-Archive", script);
        // 官方 zip 解包后在 easytier-windows-<架构>\ 子目录里，脚本必须把它提回根目录
        Assert.Contains("for /d %%D in (\"%WORKDIR%\\easytier-windows-*\")", script);
        Assert.Contains("easytier-core.exe", script);
        Assert.Contains("easytier-cli.exe", script);
        Assert.Contains("port-forward add tcp 127.0.0.1:25565 10.144.144.1:25565", script);
        Assert.Contains("port-forward add udp 127.0.0.1:25565 10.144.144.1:25565", script);

        // 自提权 + UTF-8 脚本必须显式切到 65001（ACP=65001 的机器用 GBK 会乱码）
        Assert.Contains("net session >nul 2>&1", script);
        Assert.Contains("Start-Process -FilePath '%~f0' -Verb RunAs", script);
        Assert.Contains("chcp 65001 >nul", script);
        Assert.DoesNotContain("tskey", script);
    }

    [Fact]
    public void BuildScript_EmbedsHostNodes()
    {
        var script = GameTunnelInvite.BuildScript(MakeInvite(), ["tcp://my.node:11010"]);

        Assert.Contains(">>\"%CFG%\" echo [peer]", script);
        Assert.Contains(">>\"%CFG%\" echo uri = \"tcp://my.node:11010\"", script);
        Assert.DoesNotContain("public.easytier.top", script);
    }

    [Fact]
    public void BuildScript_TcpOnlyGameHasNoUdpForward()
    {
        var info = MakeInvite();
        info.Protocol = GameTunnelProtocol.Tcp;
        var script = GameTunnelInvite.BuildScript(info);

        Assert.Contains("port-forward add tcp", script);
        Assert.DoesNotContain("port-forward add udp", script);
    }

    [Theory]
    [InlineData("我的世界（Java 版）", "我的世界（Java 版）-一键加入联机.cmd")]
    [InlineData("a/b:c*d", "a_b_c_d-一键加入联机.cmd")]
    [InlineData("", "游戏-一键加入联机.cmd")]
    public void ScriptFileName_Sanitizes(string input, string expected)
        => Assert.Equal(expected, GameTunnelInvite.ScriptFileName(input));

    [Fact]
    public void WriteScript_UsesUtf8WithoutBom()
    {
        var info = MakeInvite();
        var path = GameTunnelInvite.WriteScriptToDataDir(info);

        Assert.True(File.Exists(path));
        Assert.Contains(info.NetworkName, File.ReadAllText(path));

        var bytes = File.ReadAllBytes(path);
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);

        // 中文必须按 UTF-8 落盘：ACP=65001（UTF-8 beta）的机器用 GBK 会全部变成替换字符。
        var utf8Marker = new UTF8Encoding(false).GetBytes("一键加入联机");
        Assert.True(ContainsSequence(bytes, utf8Marker));
        Assert.False(ContainsSequence(bytes, Encoding.GetEncoding(936).GetBytes("一键加入联机")));
    }

    /// <summary>
    /// 批处理必须是 CRLF：脚本正文来自源码里的多行字符串，源码存成 LF 时正文就是 LF，
    /// 这时 cmd.exe 会把命令行拆成碎片执行（「'o' 不是内部或外部命令」「系统找不到指定的路径」）。
    /// </summary>
    [Fact]
    public void WriteScript_UsesCrlfLineEndings()
    {
        var path = GameTunnelInvite.WriteScriptToDataDir(MakeInvite());

        var bytes = File.ReadAllBytes(path);
        Assert.Contains((byte)13, bytes);

        for (var i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] != 10) continue;
            Assert.True(i > 0 && bytes[i - 1] == 13, $"第 {i} 字节是裸 LF——cmd.exe 会拆碎命令行");
        }
    }

    [Fact]
    public void ToCrlf_NormalizesMixedLineEndings()
    {
        Assert.Equal("a\r\nb\r\nc", GameTunnelInvite.ToCrlf("a\nb\r\nc"));
        Assert.Equal("a\r\nb", GameTunnelInvite.ToCrlf("a\rb"));
    }

    // ══════════════════ LAN 广播 ══════════════════

    [Fact]
    public void LanAnnouncement_MatchesMinecraftFormat()
    {
        Assert.Equal("[MOTD]图吧工具箱 · 我的世界[/MOTD][AD]25565[/AD]",
            LanWorldAnnouncer.Format("图吧工具箱 · 我的世界", 25565));
    }

    [Fact]
    public void LanSenders_EnumerateLocalAddresses()
    {
        var addresses = LanWorldAnnouncer.GetIPv4Addresses();
        Assert.NotEmpty(addresses);
        Assert.Contains(System.Net.IPAddress.Loopback, addresses);
    }

    private static bool ContainsSequence(byte[] haystack, byte[] needle)
    {
        for (var i = 0; i + needle.Length <= haystack.Length; i++)
        {
            var match = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j]) { match = false; break; }
            }
            if (match) return true;
        }
        return false;
    }
}

/// <summary>游戏 Logo：素材来源、镜像备份、缓存命名、拿不到时的兜底。</summary>
public class GameLogoServiceTests
{
    [Fact]
    public void EveryPresetHasAnOfficialLogoUrl()
    {
        foreach (var preset in GameTunnelCatalog.Presets)
        {
            Assert.NotEmpty(preset.LogoUrls);
            foreach (var url in preset.LogoUrls)
            {
                Assert.True(Uri.TryCreate(url, UriKind.Absolute, out var uri), $"{preset.Id} 的 Logo 地址无效：{url}");
                Assert.Equal(Uri.UriSchemeHttps, uri!.Scheme);
            }
        }
    }

    [Theory]
    [InlineData("terraria", 105600)]
    [InlineData("tmodloader", 1281930)]
    [InlineData("stardew", 413150)]
    [InlineData("dst", 322330)]
    [InlineData("valheim", 892970)]
    [InlineData("palworld", 1623730)]
    [InlineData("starbound", 211820)]
    [InlineData("factorio", 427520)]
    public void SteamPresetsPointAtTheirOwnAppId(string id, int appId)
    {
        var preset = GameTunnelCatalog.FindPreset(id);
        Assert.NotNull(preset);
        Assert.Contains(preset!.LogoUrls, url => url.Contains($"/steam/apps/{appId}/logo.png", StringComparison.Ordinal));
    }

    [Fact]
    public void MinecraftPresetsUseTheOfficialSiteLogo()
    {
        foreach (var id in new[] { "minecraft-java", "minecraft-bedrock" })
        {
            var preset = GameTunnelCatalog.FindPreset(id);
            Assert.NotNull(preset);
            Assert.All(preset!.LogoUrls, url => Assert.StartsWith("https://www.minecraft.net/", url));
        }
    }

    [Fact]
    public void SteamLogosShipAMirrorForMainlandNetworks()
    {
        var preset = GameTunnelCatalog.FindPreset("terraria");
        Assert.NotNull(preset);
        Assert.True(preset!.LogoUrls.Count >= 2, "Steam Logo 要有两个 CDN 互为备份");
        Assert.Contains(preset.LogoUrls, url => url.Contains("akamai.steamstatic.com", StringComparison.Ordinal));
    }

    [Fact]
    public void CachePathFollowsSourceExtensionAndSanitizesId()
    {
        var dir = Path.Combine(Path.GetTempPath(), "tubalogo");

        Assert.Equal(
            Path.Combine(dir, "dst.png"),
            GameLogoService.CachePath(dir, "dst", "https://cdn.cloudflare.steamstatic.com/steam/apps/322330/logo.png"));

        Assert.Equal(
            Path.Combine(dir, "minecraft-java.svg"),
            GameLogoService.CachePath(dir, "minecraft-java", "https://www.minecraft.net/content/dam/minecraftnet/games/minecraft/logos/logo-minecraft.svg"));

        Assert.Equal(
            Path.Combine(dir, "custom_1.png"),
            GameLogoService.CachePath(dir, "custom:1", "https://example.com/a/logo.png"));
    }

    [Fact]
    public void CustomGamesHaveNoLogoAndKeepTheGlyph()
    {
        var card = TunnelGameCard.FromCustom(new CustomGame { Id = "custom-1", Name = "自建服", Port = 25565 });

        Assert.Empty(card.LogoUrls);
        Assert.Null(card.Logo);
        Assert.Equal("\uE7FC", card.Glyph);
    }

    [Fact]
    public void CardFallsBackToGlyphUntilALogoArrives()
    {
        var card = TunnelGameCard.FromPreset(GameTunnelCatalog.FindPreset("terraria")!);

        Assert.Null(card.Logo);
        Assert.Equal(Microsoft.UI.Xaml.Visibility.Visible, card.GlyphVisibility);
    }
}

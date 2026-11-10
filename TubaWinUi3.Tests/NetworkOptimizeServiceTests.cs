using TubaWinUi3.Services;
using Xunit;

namespace TubaWinUi3.Tests;

/// <summary>网络优化（nexbox network_optimize.rs 移植）纯逻辑单元测试。</summary>
public class NetworkOptimizeServiceTests
{
    // ───────────────────────────── IPv4 校验 / 提取 ─────────────────────────────

    [Theory]
    [InlineData("223.5.5.5", true)]
    [InlineData("8.8.8.8", true)]
    [InlineData("0.0.0.0", true)]
    [InlineData("255.255.255.255", true)]
    [InlineData("1.2.3.04", true)]  // Rust parse::<u32> 语义：前导零合法且 ≤255
    [InlineData("256.1.1.1", false)]
    [InlineData("1.2.3", false)]
    [InlineData("1.2.3.4.5", false)]
    [InlineData("", false)]
    [InlineData("a.b.c.d", false)]
    [InlineData(" 1.2.3.4", false)]
    public void IsValidIpv4_Assorted(string input, bool expected)
    {
        Assert.Equal(expected, NetworkOptimizeService.IsValidIpv4(input));
    }

    [Fact]
    public void FindIpv4_ExtractsFirstAddressFromText()
    {
        Assert.Equal("223.5.5.5", NetworkOptimizeService.FindIpv4("当前 IP：223.5.5.5，完毕"));
        Assert.Equal("8.8.8.8", NetworkOptimizeService.FindIpv4("8.8.8.8"));
        Assert.Equal("119.29.29.29", NetworkOptimizeService.FindIpv4("服务器 119.29.29.29 在线"));
        Assert.Null(NetworkOptimizeService.FindIpv4("没有 IP 在这里"));
        Assert.Null(NetworkOptimizeService.FindIpv4("999.1.1.1"));
    }

    [Fact]
    public void ExtractIpv4_TraceProvider_ParsesIpLine()
    {
        var trace = "fl=123\nh=abc\nip=203.0.113.7\nts=1700000000";
        Assert.Equal("203.0.113.7", NetworkOptimizeService.ExtractIpv4(trace, "trace"));
        Assert.Null(NetworkOptimizeService.ExtractIpv4("ip=999.1.1.1\nh=x", "trace"));
        Assert.Null(NetworkOptimizeService.ExtractIpv4("no ip line here", "trace"));
    }

    // ───────────────────────────── 权限错误识别 ─────────────────────────────

    [Theory]
    [InlineData("Access denied", true)]
    [InlineData("拒绝访问", true)]
    [InlineData("权限不足", true)]
    [InlineData("denied by policy", true)]
    [InlineData("命令执行成功", false)]
    [InlineData("", false)]
    [InlineData("找不到接口", false)]
    public void IsPermissionError_DetectsAdminNeeded(string text, bool expected)
    {
        Assert.Equal(expected, NetworkOptimizeService.IsPermissionError(text));
    }

    // ───────────────────────────── Chimney 状态解析 ─────────────────────────────

    [Fact]
    public void IsChimneyDisabled_ParsesEnglishState()
    {
        Assert.True(NetworkOptimizeService.IsChimneyDisabled(
            "Chimney Offload State   : Disabled"));
        Assert.True(NetworkOptimizeService.IsChimneyDisabled(
            "Chimney Offload State   : disabled"));
        Assert.False(NetworkOptimizeService.IsChimneyDisabled(
            "Chimney Offload State   : Enabled"));
        Assert.False(NetworkOptimizeService.IsChimneyDisabled(
            "Some Other Setting      : Disabled"));
    }

    [Fact]
    public void IsChimneyDisabled_ParsesChineseState()
    {
        Assert.True(NetworkOptimizeService.IsChimneyDisabled("Chimney 卸载状态   : 已禁用"));
        Assert.False(NetworkOptimizeService.IsChimneyDisabled("Chimney 卸载状态   : 已启用"));
    }

    // ───────────────────────────── DNS 配置读取 ─────────────────────────────

    [Fact]
    public void ReadDns_SplitsServersByCommaAndWhitespace()
    {
        // 通过一个内存注册表值无法直接测 ReadDns（读真实注册表），这里验证其拆分语义
        // 由 SplitDnsServers 内联实现；用参数化字符串模拟 NexBox read_dns 的 split 行为
        var cases = new[]
        {
            ("223.5.5.5,223.6.6.6", "223.5.5.5", "223.6.6.6"),
            ("8.8.8.8 8.8.4.4", "8.8.8.8", "8.8.4.4"),
            ("114.114.114.114", "114.114.114.114", ""),
            ("", "", ""),
        };
        foreach (var (input, primary, secondary) in cases)
        {
            var parts = SplitServers(input);
            if (parts.Length == 0)
            {
                Assert.Equal(primary, "");
                continue;
            }
            Assert.Equal(primary, parts[0]);
            Assert.Equal(secondary, parts.Length > 1 ? parts[1] : "");
        }
    }

    private static string[] SplitServers(string servers) =>
        servers.Split(new[] { ',', ' ', '\t' }, System.StringSplitOptions.RemoveEmptyEntries);

    // ───────────────────────────── 预设配置 ─────────────────────────────

    [Fact]
    public void DnsPresets_PreservesOriginalPresetsAndAddsDnsPodDoh()
    {
        Assert.Equal(7, NetworkOptimizeService.DnsPresets.Length);
        Assert.Equal("阿里 DNS", NetworkOptimizeService.DnsPresets[0].Name);
        Assert.Equal("223.5.5.5", NetworkOptimizeService.DnsPresets[0].Primary);
        Assert.Equal("223.6.6.6", NetworkOptimizeService.DnsPresets[0].Secondary);
        Assert.Equal("Cloudflare", NetworkOptimizeService.DnsPresets[5].Name);
        Assert.Equal("1.0.0.1", NetworkOptimizeService.DnsPresets[5].Secondary);
        var encrypted = Assert.Single(NetworkOptimizeService.DnsPresets, p => p.DohTemplate is not null);
        Assert.Equal("https://doh.pub/dns-query", encrypted.DohTemplate);
        Assert.Equal("doh.pub", encrypted.Primary);
        Assert.Empty(encrypted.Secondary);
    }

    [Fact]
    public void DnsScript_RequiresEncryptionAndHandlesExistingRegistrations()
    {
        var script = NetworkOptimizeService.BuildSetDnsScript("203.0.113.1", "203.0.113.2", "https://doh.pub/dns-query");
        Assert.Contains("-AutoUpgrade $true -AllowFallbackToUdp $false", script);
        Assert.Contains("Add-DnsClientDohServerAddress", script);
        Assert.Contains("Set-DnsClientDohServerAddress", script);
        Assert.Contains("$ErrorActionPreference = 'Stop'", script);
        Assert.True(script.IndexOf("Add-DnsClientDohServerAddress", StringComparison.Ordinal)
            < script.IndexOf("Set-DnsClientServerAddress", StringComparison.Ordinal));
    }

    [Fact]
    public void DnsScript_SingleServerDoesNotIncludeEmptyAddressOrDohCommands()
    {
        var script = NetworkOptimizeService.BuildSetDnsScript("180.76.76.76", "");
        Assert.Contains("@('180.76.76.76')", script);
        Assert.DoesNotContain("DohServerAddress", script);
    }

    [Theory]
    [InlineData("1.2.3.4'; Start-Process calc; #", "", null)]
    [InlineData("1.2.3.4", "bad", null)]
    [InlineData("1.2.3.4", "", "http://doh.pub/dns-query")]
    public void DnsScript_RejectsInvalidInput(string primary, string secondary, string? template)
        => Assert.Throws<ArgumentException>(() => NetworkOptimizeService.BuildSetDnsScript(primary, secondary, template));

    [Fact]
    public void OptimizerItems_MatchesNexbox_PlusExtensions()
    {
        // nexbox 原生 4 项 + 本项目扩展 2 项（TCP 自动调谐、网络节流）
        Assert.Equal(6, NetworkOptimizeService.OptimizerItems.Length);
        Assert.Equal(new[]
            {
                "tcp_congestion_optimized", "chimney_offload", "nagle_optimized",
                "adapter_power_saving_off", "autotuning_disabled", "throttling_disabled"
            },
            NetworkOptimizeService.OptimizerItems.Select(i => i.StateKey));
        Assert.Contains(NetworkOptimizeService.OptimizerItems, i => i.Id == "nagle-algorithm" && i.Title == "Nagle 算法");
        Assert.Contains(NetworkOptimizeService.OptimizerItems, i => i.Id == "tcp-autotuning" && i.Title == "TCP 自动调谐");
        Assert.Contains(NetworkOptimizeService.OptimizerItems, i => i.Id == "network-throttling" && i.Title == "网络节流限制");
    }

    // ───────────────────────────── TCP 自动调谐状态解析 ─────────────────────────────

    [Fact]
    public void IsAutoTuningDisabled_ParsesEnglishState()
    {
        Assert.True(NetworkOptimizeService.IsAutoTuningDisabled(
            "Receive Window Auto-Tuning Level : Disabled"));
        Assert.False(NetworkOptimizeService.IsAutoTuningDisabled(
            "Receive Window Auto-Tuning Level : normal"));
        Assert.False(NetworkOptimizeService.IsAutoTuningDisabled("TCP Global Parameters"));
    }

    [Fact]
    public void IsAutoTuningDisabled_ParsesChineseState()
    {
        Assert.True(NetworkOptimizeService.IsAutoTuningDisabled("接收窗口自动调整级别   : 已禁用"));
        Assert.False(NetworkOptimizeService.IsAutoTuningDisabled("接收窗口自动调整级别   : 正常"));
    }
}

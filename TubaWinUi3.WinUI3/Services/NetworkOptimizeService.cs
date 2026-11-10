using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace TubaWinUi3.Services;

/// <summary>单项操作结果（对照 nexbox PerfTweakResult）。</summary>
public sealed record PerfTweakResult(bool Success, string Message);

/// <summary>DNS 延迟探测结果（对照 nexbox DnsProbeResult）。</summary>
public sealed record DnsProbeResult(
    double LatencyMs,
    string Responder,
    string? ViaInterface);

/// <summary>网络优化状态快照（对照 nexbox NetworkTweakState）。</summary>
public sealed record NetworkTweakState(
    bool TcpCongestionOptimized,
    bool ChimneyOffload,
    bool NagleOptimized,
    bool AdapterPowerSavingOff,
    bool AutoTuningDisabled,
    bool ThrottlingDisabled,
    string DnsPrimary,
    string DnsSecondary);

/// <summary>DNS 预设（对照 nexbox dnsPresets 配置）。</summary>
public sealed record DnsPreset(string Id, string Name, string Primary, string Secondary, string ColorHex,
    string? DohTemplate = null);

/// <summary>网络优化项（对照 nexbox networkOptimizerItems 配置）。</summary>
public sealed record NetworkOptimizerItem(string Id, string StateKey, string Glyph, string ColorHex, string Title, string Description);

/// <summary>
/// 网络优化服务：照搬 nexbox network_optimize.rs 的实现方式——
/// netsh 原生调用（GBK 解码 + 权限错误识别）、Tcpip/网卡类注册表直写（Nagle、省电）、
/// UDP 直连 DNS 延迟探测（GetBestInterface + GetAdaptersAddresses 路由网卡）、
/// PowerShell 设置 DNS、多源公网 IP 查询。
/// </summary>
public static class NetworkOptimizeService
{
    // ============ 常量（对照 nexbox） ============

    /// <summary>网卡设备注册表类键（用于禁用/恢复网卡省电与收集 NetCfgInstanceId）。</summary>
    private const string NicClassKey = @"SYSTEM\CurrentControlSet\Control\Class\{4d36e972-e325-11ce-bfc1-08002be10318}";
    private const string TcpipParamsKey = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters";
    private const string TcpipInterfacesKey = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces";
    /// <summary>多媒体系统配置（网络节流 NetworkThrottlingIndex 所在，HKCU 无需管理员）。</summary>
    private const string MultimediaSystemProfileKey = @"Software\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile";
    private const string NeedAdminMessage = "需要管理员权限，请以管理员身份运行图吧工具箱CE";

    /// <summary>DNS 延迟分档阈值（对照 nexbox 前端 latencyColor 逻辑）。</summary>
    public const double DnsLatencyGoodMs = 80;
    public const double DnsLatencyFairMs = 200;

    /// <summary>普通 DNS 及加密 DNS 预设；DNSPod 加密节点按官方域名动态解析，不固化已下线的测试 IP。</summary>
    public static readonly DnsPreset[] DnsPresets =
    [
        new("alidns", "阿里 DNS", "223.5.5.5", "223.6.6.6", "#FF6A00"),
        new("dnspod", "DNSPod", "119.29.29.29", "119.28.28.28", "#007FFF"),
        new("114dns", "114 DNS", "114.114.114.114", "114.114.115.115", "#3182CE"),
        new("baidu", "百度 DNS", "180.76.76.76", "", "#DE2910"),
        new("google", "Google DNS", "8.8.8.8", "8.8.4.4", "#4285F4"),
        new("cloudflare", "Cloudflare", "1.1.1.1", "1.0.0.1", "#F6821F"),
        new("dnspod-doh", "DNSPod 加密 DNS（DoH）", "doh.pub", "", "#007FFF", "https://doh.pub/dns-query"),
    ];

    /// <summary>网络优化项（照搬 nexbox networkOptimizerItems，另扩展 TCP 自动调谐与网络节流两项）。</summary>
    public static readonly NetworkOptimizerItem[] OptimizerItems =
    [
        new("tcp-congestion", "tcp_congestion_optimized", "\uE968", "#38A169", "TCP 拥塞控制", "启用 CTCP/CUBIC 拥塞控制算法，提升网络吞吐量"),
        new("chimney-offload", "chimney_offload", "\uE968", "#DD6B20", "TCP Chimney Offload", "关闭 TCP Chimney Offload 降低网络延迟"),
        new("nagle-algorithm", "nagle_optimized", "\uE968", "#805AD5", "Nagle 算法", "禁用 Nagle 算法，减少小包延迟（适合游戏）"),
        new("adapter-power", "adapter_power_saving_off", "\uE945", "#FF6B9D", "网卡节能禁用", "关闭网卡电源节省模式，降低延迟抖动"),
        new("tcp-autotuning", "autotuning_disabled", "\uE968", "#00A0A0", "TCP 自动调谐", "禁用接收窗口自动调谐，减少游戏延迟抖动"),
        new("network-throttling", "throttling_disabled", "\uE945", "#E0408A", "网络节流限制", "禁用多媒体网络节流（NetworkThrottlingIndex），提升游戏网络响应"),
    ];

    // ============ 控制台解码（对照 nexbox decode_console：中文系统 netsh 输出为 GBK/CP936） ============

    private static readonly Encoding Gbk = CreateGbk();

    private static Encoding CreateGbk()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding("GBK");
    }

    private static string DecodeConsole(byte[] bytes) => Gbk.GetString(bytes);

    /// <summary>权限类错误识别（对照 nexbox 各命令的 lower.contains 判定）。</summary>
    internal static bool IsPermissionError(string text)
    {
        var lower = text.ToLowerInvariant();
        return lower.Contains("access denied")
            || lower.Contains("denied")
            || lower.Contains("拒绝访问")
            || lower.Contains("权限不足");
    }

    // ============ 原生进程执行（对照 nexbox Command::new + output()） ============

    /// <summary>执行 netsh 命令并返回解码输出；失败时识别权限错误（对照 run_netsh_result）。</summary>
    private static string RunNetshResult(params string[] args)
    {
        var output = RunNative("netsh", args);
        if (output.ExitCode == 0)
            return output.Text;
        var lower = output.Text.ToLowerInvariant();
        throw IsPermissionError(lower)
            ? new InvalidOperationException(NeedAdminMessage)
            : new InvalidOperationException($"命令执行失败: {output.Text.Trim()}");
    }

    private sealed record NativeOutput(int ExitCode, string Text);

    /// <summary>启动原生进程并以 GBK 解码（stdout 优先，空则 stderr），无窗口。参数经 ArgumentList 防注入。</summary>
    private static NativeOutput RunNative(string fileName, string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"无法启动 {fileName}");
        var stdout = ReadAllBytes(process.StandardOutput.BaseStream);
        var stderr = ReadAllBytes(process.StandardError.BaseStream);
        if (!process.WaitForExit(60_000))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException($"{fileName} 执行超时（60 秒）");
        }
        var text = stdout.Length > 0 ? DecodeConsole(stdout) : DecodeConsole(stderr);
        return new NativeOutput(process.ExitCode, text);
    }

    private static byte[] ReadAllBytes(Stream stream)
    {
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>执行命令但忽略退出码（对照 run_netsh：失败返回空串）。</summary>
    private static string RunNetsh(params string[] args)
    {
        try
        {
            return RunNetshResult(args);
        }
        catch
        {
            return string.Empty;
        }
    }

    // ============ Nagle 算法（注册表直写，对照 set_nagle_native / restore_nagle_native） ============

    private static RegistryKey? OpenWritable(string path)
    {
        try { return Registry.LocalMachine.CreateSubKey(path); }
        catch { return null; }
    }

    private static void SetNagleNative()
    {
        using var parameters = OpenWritable(TcpipParamsKey)
            ?? throw new InvalidOperationException("打开 Tcpip 参数键失败");
        parameters.SetValue("TcpAckFrequency", 1, RegistryValueKind.DWord);

        using var ifaces = OpenWritable(TcpipInterfacesKey)
            ?? throw new InvalidOperationException("打开 Tcpip 接口键失败");
        foreach (var name in TryGetSubKeyNames(ifaces))
        {
            try
            {
                using var key = ifaces.OpenSubKey(name, writable: true);
                if (key is null || !HasIpAddress(key))
                    continue;
                key.SetValue("TCPNoDelay", 1, RegistryValueKind.DWord);
                key.SetValue("TcpAckFrequency", 1, RegistryValueKind.DWord);
                key.SetValue("TcpDelAckTicks", 0, RegistryValueKind.DWord);
            }
            catch { }
        }
    }

    private static void RestoreNagleNative()
    {
        using var parameters = OpenWritable(TcpipParamsKey)
            ?? throw new InvalidOperationException("打开 Tcpip 参数键失败");
        try { parameters.DeleteValue("TcpAckFrequency", throwOnMissingValue: false); } catch { }

        using var ifaces = OpenWritable(TcpipInterfacesKey)
            ?? throw new InvalidOperationException("打开 Tcpip 接口键失败");
        foreach (var name in TryGetSubKeyNames(ifaces))
        {
            try
            {
                using var key = ifaces.OpenSubKey(name, writable: true);
                if (key is null || !HasIpAddress(key))
                    continue;
                key.DeleteValue("TCPNoDelay", throwOnMissingValue: false);
                key.DeleteValue("TcpAckFrequency", throwOnMissingValue: false);
                key.DeleteValue("TcpDelAckTicks", throwOnMissingValue: false);
            }
            catch { }
        }
    }

    /// <summary>接口是否有 IPAddress 值（REG_MULTI_SZ 或 REG_SZ，对照 nexbox）。</summary>
    private static bool HasIpAddress(RegistryKey key)
    {
        try
        {
            var value = key.GetValue("IPAddress");
            return value switch
            {
                string[] arr => arr.Length > 0,
                string s => s.Length > 0,
                _ => false
            };
        }
        catch
        {
            return false;
        }
    }

    // ============ 网卡省电（注册表直写，对照 set_power_saving_native） ============

    private static void SetPowerSavingNative(bool off)
    {
        using var classKey = OpenWritable(NicClassKey)
            ?? throw new InvalidOperationException("打开网卡类键失败");
        foreach (var name in TryGetSubKeyNames(classKey))
        {
            try
            {
                using var key = classKey.OpenSubKey(name, writable: true);
                if (key is null || key.GetValue("DriverDesc") is null)
                    continue; // 仅处理网卡设备
                uint cap;
                try { cap = Convert.ToUInt32(key.GetValue("PnPCapabilities") ?? 0u); }
                catch { cap = 0; }
                var newCap = off ? cap | 0x100u : cap & ~0x100u;
                if (newCap != cap)
                    key.SetValue("PnPCapabilities", newCap, RegistryValueKind.DWord);
            }
            catch { }
        }
    }

    // ============ 1. TCP 拥塞控制 ============

    public static PerfTweakResult SetTcpCongestion()
    {
        RunNetshResult("int", "tcp", "set", "supplemental", "Internet", "congestionprovider=ctcp");
        return new PerfTweakResult(true, "TCP 拥塞控制已优化");
    }

    public static PerfTweakResult RestoreTcpCongestion()
    {
        RunNetshResult("int", "tcp", "set", "supplemental", "Internet", "congestionprovider=newreno");
        return new PerfTweakResult(true, "TCP 拥塞控制已恢复");
    }

    // ============ 2. TCP Chimney Offload ============

    public static PerfTweakResult SetChimneyOff()
    {
        RunNetshResult("int", "tcp", "set", "global", "chimney=disabled");
        return new PerfTweakResult(true, "TCP Chimney Offload 已禁用");
    }

    public static PerfTweakResult RestoreChimney()
    {
        RunNetshResult("int", "tcp", "set", "global", "chimney=enabled");
        return new PerfTweakResult(true, "TCP Chimney Offload 已恢复");
    }

    // ============ 3. Nagle ============

    public static PerfTweakResult SetNagleOptimization()
    {
        SetNagleNative();
        return new PerfTweakResult(true, "Nagle 低延迟优化已应用");
    }

    public static PerfTweakResult RestoreNagleOptimization()
    {
        RestoreNagleNative();
        return new PerfTweakResult(true, "Nagle 低延迟优化已恢复");
    }

    // ============ 4. 网卡省电 ============

    public static PerfTweakResult SetAdapterPowerSavingOff()
    {
        SetPowerSavingNative(true);
        return new PerfTweakResult(true, "网卡省电模式已禁用");
    }

    public static PerfTweakResult RestoreAdapterPowerSaving()
    {
        SetPowerSavingNative(false);
        return new PerfTweakResult(true, "网卡省电模式已恢复");
    }

    // ============ 4b. TCP 自动调谐（Receive Window Auto-Tuning） ============

    public static PerfTweakResult SetAutoTuningDisabled()
    {
        RunNetshResult("int", "tcp", "set", "global", "autotuninglevel=disabled");
        return new PerfTweakResult(true, "TCP 自动调谐已禁用");
    }

    public static PerfTweakResult RestoreAutoTuning()
    {
        RunNetshResult("int", "tcp", "set", "global", "autotuninglevel=normal");
        return new PerfTweakResult(true, "TCP 自动调谐已恢复");
    }

    /// <summary>自动调谐状态解析：输出含「Receive Window Auto-Tuning Level」且为 disabled/禁用（对齐 Chimney 判定风格）。</summary>
    internal static bool IsAutoTuningDisabled(string output)
    {
        var hasLevel = output.Contains("Receive Window Auto-Tuning Level") || output.Contains("接收窗口自动调整级别");
        return hasLevel && (output.ToLowerInvariant().Contains("disabled") || output.Contains("禁用"));
    }

    // ============ 4c. 网络节流限制（NetworkThrottlingIndex，HKCU） ============

    public static PerfTweakResult SetThrottlingDisabled()
    {
        using var key = Registry.CurrentUser.CreateSubKey(MultimediaSystemProfileKey)
            ?? throw new InvalidOperationException("打开多媒体系统配置键失败");
        key.SetValue("NetworkThrottlingIndex", 0xFFFFFFFF, RegistryValueKind.DWord);
        return new PerfTweakResult(true, "网络节流限制已禁用");
    }

    public static PerfTweakResult RestoreThrottling()
    {
        using var key = Registry.CurrentUser.OpenSubKey(MultimediaSystemProfileKey, writable: true);
        if (key is not null)
            key.DeleteValue("NetworkThrottlingIndex", throwOnMissingValue: false);
        return new PerfTweakResult(true, "网络节流限制已恢复");
    }

    /// <summary>网络节流当前状态：值存在且为 0xFFFFFFFF 即已禁用节流。</summary>
    internal static bool CheckThrottlingDisabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(MultimediaSystemProfileKey);
            return key is not null
                && key.GetValue("NetworkThrottlingIndex") is object value
                && Convert.ToInt64(value) == 0xFFFFFFFFL;
        }
        catch
        {
            return false;
        }
    }

    // ============ 5. DNS 延迟探测（UDP 直连，对照 test_dns_latency） ============

    /// <summary>
    /// 测量指定 DNS 服务器的查询往返延迟：发送最小 DNS 查询（根域 "." 的 NS 记录）到 UDP 53，
    /// 以收到合法应答的时间差作为延迟；并探测应答来源 IP 与路由网卡（本地劫持会现形）。
    /// 超时返回 null。
    /// </summary>
    public static DnsProbeResult? TestDnsLatency(string ip)
    {
        if (!IPAddress.TryParse(ip, out var addr))
            throw new ArgumentException($"无效的 DNS 地址: {ip}");

        string? viaInterface = null;
        if (addr.AddressFamily == AddressFamily.InterNetwork)
            viaInterface = RouteInterfaceName(addr);

        using var socket = new Socket(addr.AddressFamily, SocketType.Dgram, ProtocolType.Udp);
        socket.ReceiveTimeout = 1000;
        socket.SendTimeout = 1000;

        // 报文：事务 ID(2) + 标志 RD=1(2) + QDCOUNT=1(2) + 其余计数 0(6) + 根域 "." (1) + QTYPE=NS(2) + QCLASS=IN(2)
        var query = new byte[17];
        query[0] = 0x4E;
        query[1] = 0x58;
        query[2] = 0x01;
        query[5] = 0x01;
        query[13] = 0x02;
        query[16] = 0x01;

        var started = Stopwatch.GetTimestamp();
        socket.SendTo(query, new IPEndPoint(addr, 53));
        var buffer = new byte[512];
        EndPoint remote = addr.AddressFamily == AddressFamily.InterNetwork
            ? new IPEndPoint(IPAddress.Any, 0)
            : new IPEndPoint(IPAddress.IPv6Any, 0);
        int received;
        try
        {
            received = socket.ReceiveFrom(buffer, ref remote);
        }
        catch (SocketException)
        {
            return null; // 超时/无响应
        }
        var latencyMs = Stopwatch.GetElapsedTime(started).TotalMicroseconds / 1000.0;
        if (received < 12 || buffer[0] != query[0] || buffer[1] != query[1])
            throw new InvalidOperationException("DNS 应答无效");

        return new DnsProbeResult(latencyMs, ((IPEndPoint)remote).Address.ToString(), viaInterface);
    }

    // ============ 路由网卡名（对照 route_interface_name：GetBestInterface + GetAdaptersAddresses） ============

    private static readonly HttpClient DohProbeClient = new(new HttpClientHandler
    {
        UseProxy = false,
        AllowAutoRedirect = false
    }) { Timeout = TimeSpan.FromSeconds(3) };

    /// <summary>通过 HTTPS 发送真实 DNS wire-format 查询，不将 UDP 延迟冒充加密 DNS 延迟。</summary>
    public static async Task<DnsProbeResult?> TestDohLatencyAsync(string template, CancellationToken cancellationToken)
    {
        byte[] query = [0, 0, 1, 0, 0, 1, 0, 0, 0, 0, 0, 0, 0, 0, 2, 0, 1];
        using var request = new HttpRequestMessage(HttpMethod.Post, template);
        request.Content = new ByteArrayContent(query);
        request.Content.Headers.ContentType = new("application/dns-message");
        request.Headers.Accept.ParseAdd("application/dns-message");
        var started = Stopwatch.GetTimestamp();
        using var response = await DohProbeClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (response.Content.Headers.ContentType?.MediaType != "application/dns-message"
            || bytes.Length < 12 || bytes[0] != 0 || bytes[1] != 0 || (bytes[2] & 0x80) == 0
            || (bytes[3] & 0x0F) != 0)
            throw new InvalidOperationException("加密 DNS 应答无效");
        return new DnsProbeResult(Stopwatch.GetElapsedTime(started).TotalMilliseconds,
            new Uri(template).Host, null);
    }

    private const uint ErrorBufferOverflow = 111;
    private const uint AfInet = 2;

    [DllImport("iphlpapi.dll")]
    private static extern uint GetBestInterface(uint destAddr, out uint bestIfIndex);

    [DllImport("iphlpapi.dll")]
    private static extern uint GetAdaptersAddresses(uint family, uint flags, IntPtr reserved, IntPtr adapterAddresses, ref uint sizePointer);

    /// <summary>IP_ADAPTER_ADDRESSES 前缀结构（Sequential + IntPtr 自动适配 x86/x64 布局；FriendlyName 之前正好 11 个字段）。</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct IpAdapterAddressesPrefix
    {
        public uint Length;
        public uint IfIndex;
        public IntPtr Next;
        public IntPtr AdapterName;
        public IntPtr FirstUnicastAddress;
        public IntPtr FirstAnycastAddress;
        public IntPtr FirstMulticastAddress;
        public IntPtr FirstDnsServerAddress;
        public IntPtr DnsSuffix;
        public IntPtr Description;
        public IntPtr FriendlyName;
    }

    private static string? RouteInterfaceName(IPAddress ipv4)
    {
        // GetBestInterface 需要网络字节序的 IPv4 地址
        var bytes = ipv4.GetAddressBytes();
        var dest = ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
        if (GetBestInterface(dest, out var bestIf) != 0)
            return null;

        // 标准两次调用模式：缓冲区不足（111）时按返回大小重试
        var size = 16 * 1024u;
        while (true)
        {
            var buffer = Marshal.AllocHGlobal((int)size);
            try
            {
                var rc = GetAdaptersAddresses(AfInet, 0, IntPtr.Zero, buffer, ref size);
                if (rc == 0)
                {
                    var node = buffer;
                    while (node != IntPtr.Zero)
                    {
                        var adapter = Marshal.PtrToStructure<IpAdapterAddressesPrefix>(node);
                        if (adapter.IfIndex == bestIf && adapter.FriendlyName != IntPtr.Zero)
                            return Marshal.PtrToStringUni(adapter.FriendlyName);
                        node = adapter.Next;
                    }
                    return null;
                }
                if (rc == ErrorBufferOverflow)
                    continue;
                return null;
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }
    }

    // ============ 6. DNS 优化（PowerShell，对照 set_dns_servers / restore_dns_servers） ============

    private static string GetPowerShellPath()
    {
        var sysroot = Environment.GetEnvironmentVariable("SystemRoot");
        if (!string.IsNullOrEmpty(sysroot))
        {
            var ps = Path.Combine(sysroot, @"System32\WindowsPowerShell\v1.0\powershell.exe");
            if (File.Exists(ps))
                return ps;
        }
        return "powershell.exe";
    }

    /// <summary>执行 PowerShell 脚本；失败时识别权限错误（对照 nexbox set_dns_servers 实现）。</summary>
    private static void RunPowerShellScript(string script)
    {
        var psi = new ProcessStartInfo
        {
            FileName = GetPowerShellPath(),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-ExecutionPolicy");
        psi.ArgumentList.Add("Bypass");
        psi.ArgumentList.Add("-Command");
        psi.ArgumentList.Add(script);

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("执行命令失败");
        var stdout = ReadAllBytes(process.StandardOutput.BaseStream);
        var stderr = ReadAllBytes(process.StandardError.BaseStream);
        if (!process.WaitForExit(60_000))
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException("PowerShell 执行超时（60 秒）");
        }
        if (process.ExitCode != 0)
        {
            var stderrText = new UTF8Encoding(false, true).GetString(stderr.Length > 0 ? stderr : stdout);
            if (IsPermissionError(stderrText))
                throw new InvalidOperationException(NeedAdminMessage);
            throw new InvalidOperationException($"DNS 设置失败: {stderrText.Trim()}");
        }
    }

    public static async Task<(string Primary, string Secondary)> ResolveDnsPresetAsync(DnsPreset preset)
    {
        if (preset.DohTemplate is null)
            return (preset.Primary, preset.Secondary);
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 22000))
            throw new NotSupportedException("加密 DNS 预设需要 Windows 11 或更新版本；当前系统可使用普通 DNS 预设。");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var addresses = (await Dns.GetHostAddressesAsync(new Uri(preset.DohTemplate).DnsSafeHost, timeout.Token))
            .Where(a => a.AddressFamily == AddressFamily.InterNetwork)
            .Select(a => a.ToString()).Distinct().Take(2).ToArray();
        if (addresses.Length == 0)
            throw new InvalidOperationException("无法解析加密 DNS 服务的 IPv4 节点，请检查当前网络后重试。");
        return (addresses[0], addresses.Length > 1 ? addresses[1] : "");
    }

    /// <summary>构造脚本前验证输入；任何配置错误必须传播到页面，不能显示虚假的成功状态。</summary>
    internal static string BuildSetDnsScript(string primary, string secondary, string? dohTemplate = null)
    {
        if (!IsValidIpv4(primary) || (secondary.Length > 0 && !IsValidIpv4(secondary)))
            throw new ArgumentException("请输入有效的 IPv4 DNS 地址。");
        if (dohTemplate is not null && (!Uri.TryCreate(dohTemplate, UriKind.Absolute, out var uri)
            || uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length > 0 || uri.Fragment.Length > 0))
            throw new ArgumentException("加密 DNS 模板必须是有效的 HTTPS 地址。");
        var servers = secondary.Length > 0 ? $"@('{primary}', '{secondary}')" : $"@('{primary}')";
        var encryption = dohTemplate is null ? "" : $$"""
            if (!(Get-Command Add-DnsClientDohServerAddress -ErrorAction SilentlyContinue)) {
                throw '当前系统不支持原生加密 DNS，请使用 Windows 11 或更新版本。'
            }
            foreach ($server in $servers) {
                $existing = Get-DnsClientDohServerAddress | Where-Object { $_.ServerAddress -eq $server }
                if ($existing) {
                    Set-DnsClientDohServerAddress -ServerAddress $server -DohTemplate '{{dohTemplate.Replace("'", "''")}}' -AutoUpgrade $true -AllowFallbackToUdp $false
                } else {
                    Add-DnsClientDohServerAddress -ServerAddress $server -DohTemplate '{{dohTemplate.Replace("'", "''")}}' -AutoUpgrade $true -AllowFallbackToUdp $false
                }
            }
            """;
        return $$"""
            $ErrorActionPreference = 'Stop'
            $servers = {{servers}}
            $adapters = @(Get-NetAdapter -Physical | Where-Object { $_.Status -eq 'Up' })
            if ($adapters.Count -eq 0) { throw '没有已连接的物理网卡，未修改 DNS。' }
            {{encryption}}
            foreach ($adapter in $adapters) {
                Set-DnsClientServerAddress -InterfaceIndex $adapter.ifIndex -ServerAddresses $servers
            }
            Write-Output 'OK'
            """;
    }

    public static PerfTweakResult SetDnsServers(string dnsPrimary, string dnsSecondary, string? dohTemplate = null)
    {
        RunPowerShellScript(BuildSetDnsScript(dnsPrimary, dnsSecondary, dohTemplate));
        return new PerfTweakResult(true, dohTemplate is null
            ? $"DNS 已切换到 {dnsPrimary}{(dnsSecondary.Length > 0 ? " / " + dnsSecondary : "")}"
            : $"已启用加密 DNS：{dohTemplate}（禁止回退到明文 DNS）");
    }

    public static PerfTweakResult RestoreDnsServers()
    {
        // 非插值原始字符串直接原样输出（无 Format），单花括号无需转义
        var script = """
            $ErrorActionPreference = 'Stop'
            $adapters = @(Get-NetAdapter -Physical | Where-Object { $_.Status -eq "Up" })
            if ($adapters.Count -eq 0) { throw '没有已连接的物理网卡，未修改 DNS。' }
            foreach ($adapter in $adapters) {
                Set-DnsClientServerAddress -InterfaceIndex $adapter.ifIndex -ResetServerAddresses | Out-Null
            }
            Write-Output 'OK'
            """;
        RunPowerShellScript(script);
        return new PerfTweakResult(true, "DNS 已恢复为自动获取");
    }

    // ============ 清理 DNS 缓存（对照 clear_dns_cache） ============

    public static PerfTweakResult ClearDnsCache()
    {
        var output = RunNative("ipconfig", ["/flushdns"]);
        var text = output.Text;
        if (output.ExitCode == 0)
            return new PerfTweakResult(true, "DNS 缓存已清理");
        if (IsPermissionError(text))
            throw new InvalidOperationException(NeedAdminMessage);
        throw new InvalidOperationException($"清理 DNS 缓存失败: {text.Trim()}");
    }

    // ============ 重置网络（对照 reset_network：winsock + int ip） ============

    public static PerfTweakResult ResetNetwork()
    {
        var winsock = RunNative("netsh", ["winsock", "reset"]);
        var ip = RunNative("netsh", ["int", "ip", "reset"]);
        var combined = (winsock.Text + "\n" + ip.Text).Trim();
        if (IsPermissionError(combined))
            throw new InvalidOperationException(NeedAdminMessage);
        return new PerfTweakResult(true, "网络已重置，建议重启电脑后生效");
    }

    // ============ 修复 DHCP（对照 fix_dhcp） ============

    public static PerfTweakResult FixDhcp()
    {
        // 1. 从网卡设备类键收集 NetCfgInstanceId（DHCP 网卡在 Tcpip\Interfaces 下可能无 IPAddress 值）
        var physical = new List<string>();
        using (var classKey = TryOpenRead(NicClassKey))
        {
            if (classKey is not null)
            {
                foreach (var name in TryGetSubKeyNames(classKey))
                {
                    try
                    {
                        using var key = classKey.OpenSubKey(name);
                        var id = key?.GetValue("NetCfgInstanceId") as string;
                        if (!string.IsNullOrWhiteSpace(id))
                            physical.Add(id.Trim());
                    }
                    catch { }
                }
            }
        }
        if (physical.Count == 0)
            throw new InvalidOperationException("未发现物理网络接口");

        // 2. 逐个恢复 IP/DNS 为自动获取（DHCP）；找不到接口错误可忽略，权限错误中断
        foreach (var guid in physical)
        {
            foreach (var args in new[]
            {
                new[] { "interface", "ipv4", "set", "address", "name", guid, "source=dhcp" },
                new[] { "interface", "ipv4", "set", "dnsservers", "name", guid, "source=dhcp" }
            })
            {
                try
                {
                    RunNetshResult(args);
                }
                catch (InvalidOperationException ex) when (IsPermissionError(ex.Message))
                {
                    throw new InvalidOperationException(NeedAdminMessage);
                }
                catch (InvalidOperationException)
                {
                    // 未连接网卡报"找不到接口"等错误，可忽略
                }
            }
        }

        // 3. 刷新 DNS 缓存（不做 /release /renew，避免长时间阻塞）
        var flush = RunNative("ipconfig", ["/flushdns"]);
        if (flush.ExitCode != 0)
        {
            if (IsPermissionError(flush.Text))
                throw new InvalidOperationException(NeedAdminMessage);
            throw new InvalidOperationException($"ipconfig /flushdns: {flush.Text.Trim()}");
        }

        return new PerfTweakResult(true, "已恢复 DHCP 自动获取，DNS 缓存已刷新");
    }

    // ============ 7. 状态检测（对照 check_network_tweak_states：两个 netsh 并行 + 注册表毫秒级读取） ============

    /// <summary>Chimney 状态文本解析（对照 is_chimney_disabled）。</summary>
    internal static bool IsChimneyDisabled(string output)
    {
        var hasChimney = output.Contains("Chimney Offload State") || output.Contains("Chimney 卸载状态");
        return hasChimney && (output.ToLowerInvariant().Contains("disabled") || output.Contains("禁用"));
    }

    /// <summary>以只读方式打开注册表键；任何访问失败（权限/不存在）返回 null——与 Rust winreg 的 Result 容错一致。</summary>
    private static RegistryKey? TryOpenRead(string path)
    {
        try
        {
            return Registry.LocalMachine.OpenSubKey(path);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>枚举子键名，失败返回空数组（对齐 Rust enum_keys().flatten() 跳过 Err）。</summary>
    private static string[] TryGetSubKeyNames(RegistryKey key)
    {
        try
        {
            return key.GetSubKeyNames();
        }
        catch
        {
            return [];
        }
    }

    /// <summary>Nagle：读取有 IPAddress 的接口中是否存在 TCPNoDelay=1（对照 check_nagle）。</summary>
    internal static bool CheckNagle()
    {
        using var interfaces = TryOpenRead(TcpipInterfacesKey);
        if (interfaces is null)
            return false;
        foreach (var name in TryGetSubKeyNames(interfaces))
        {
            using var key = TryOpenRead(TcpipInterfacesKey + "\\" + name);
            if (key is null || !HasIpAddress(key))
                continue;
            try
            {
                if (Convert.ToInt32(key.GetValue("TCPNoDelay") ?? 0) == 1)
                    return true;
            }
            catch { }
        }
        return false;
    }

    /// <summary>网卡省电：任何网卡 PnPCapabilities 含 0x100 位即已禁用省电（对照 check_power_saving）。</summary>
    internal static bool CheckPowerSaving()
    {
        using var adapters = TryOpenRead(NicClassKey);
        if (adapters is null)
            return false;
        foreach (var name in TryGetSubKeyNames(adapters))
        {
            using var key = TryOpenRead(NicClassKey + "\\" + name);
            if (key is null)
                continue;
            uint cap;
            try { cap = Convert.ToUInt32(key.GetValue("PnPCapabilities") ?? 0u); }
            catch { continue; }
            if ((cap & 0x100u) != 0)
                return true;
        }
        return false;
    }

    /// <summary>读取当前 DNS：优先 NameServer（手动），否则 DhcpNameServer（对照 read_dns）。</summary>
    internal static (string Primary, string Secondary) ReadDns()
    {
        using var interfaces = TryOpenRead(TcpipInterfacesKey);
        if (interfaces is null)
            return ("", "");
        foreach (var name in TryGetSubKeyNames(interfaces))
        {
            using var key = TryOpenRead(TcpipInterfacesKey + "\\" + name);
            if (key is null || !HasIpAddress(key))
                continue;
            var servers = key.GetValue("NameServer") as string ?? key.GetValue("DhcpNameServer") as string;
            if (string.IsNullOrWhiteSpace(servers))
                continue;
            var parts = servers
                .Split(new[] { ',', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0)
                return (parts[0], parts.Length > 1 ? parts[1] : "");
        }
        return ("", "");
    }

    public static NetworkTweakState CheckStates()
    {
        // 两个 netsh 查询并行执行（各约 0.3~1 秒），避免串行等待
        var suppTask = Task.Run(() => RunNetsh("int", "tcp", "show", "supplemental"));
        var globalTask = Task.Run(() => RunNetsh("int", "tcp", "show", "global"));
        Task.WaitAll(suppTask, globalTask);
        var suppOut = suppTask.IsCompletedSuccessfully ? suppTask.Result : string.Empty;
        var globalOut = globalTask.IsCompletedSuccessfully ? globalTask.Result : string.Empty;

        var suppLower = suppOut.ToLowerInvariant();
        var tcpCongestionOptimized = suppLower.Contains("ctcp") || suppLower.Contains("cubic");
        var chimneyOffload = IsChimneyDisabled(globalOut);
        var autoTuningDisabled = IsAutoTuningDisabled(globalOut);
        var nagleOptimized = CheckNagle();
        var adapterPowerSavingOff = CheckPowerSaving();
        var throttlingDisabled = CheckThrottlingDisabled();
        var (dnsPrimary, dnsSecondary) = ReadDns();

        return new NetworkTweakState(
            tcpCongestionOptimized, chimneyOffload, nagleOptimized,
            adapterPowerSavingOff, autoTuningDisabled, throttlingDisabled,
            dnsPrimary, dnsSecondary);
    }

    // ============ 8. 批量优化 / 恢复（对照 batch_network_enable / disable） ============

    public static PerfTweakResult BatchEnable()
    {
        var errors = new List<string>();
        TryCollect(errors, "TCP 拥塞控制", () => RunNetshResult("int", "tcp", "set", "supplemental", "Internet", "congestionprovider=ctcp"));
        TryCollect(errors, "Chimney Offload", () => RunNetshResult("int", "tcp", "set", "global", "chimney=disabled"));
        TryCollect(errors, "TCP 自动调谐", () => RunNetshResult("int", "tcp", "set", "global", "autotuninglevel=disabled"));
        TryCollect(errors, "Nagle", SetNagleNative);
        TryCollect(errors, "网卡省电", () => SetPowerSavingNative(true));
        TryCollect(errors, "网络节流", () =>
        {
            using var key = Registry.CurrentUser.CreateSubKey(MultimediaSystemProfileKey);
            key?.SetValue("NetworkThrottlingIndex", 0xFFFFFFFF, RegistryValueKind.DWord);
        });
        return errors.Count == 0
            ? new PerfTweakResult(true, "网络优化已全部应用")
            : throw new InvalidOperationException(string.Join("; ", errors));
    }

    public static PerfTweakResult BatchDisable()
    {
        var errors = new List<string>();
        TryCollect(errors, "TCP 拥塞控制", () => RunNetshResult("int", "tcp", "set", "supplemental", "Internet", "congestionprovider=newreno"));
        TryCollect(errors, "Chimney Offload", () => RunNetshResult("int", "tcp", "set", "global", "chimney=enabled"));
        TryCollect(errors, "TCP 自动调谐", () => RunNetshResult("int", "tcp", "set", "global", "autotuninglevel=normal"));
        TryCollect(errors, "Nagle", RestoreNagleNative);
        TryCollect(errors, "网卡省电", () => SetPowerSavingNative(false));
        TryCollect(errors, "网络节流", () =>
        {
            using var key = Registry.CurrentUser.OpenSubKey(MultimediaSystemProfileKey, writable: true);
            key?.DeleteValue("NetworkThrottlingIndex", throwOnMissingValue: false);
        });
        return errors.Count == 0
            ? new PerfTweakResult(true, "网络优化已全部恢复")
            : throw new InvalidOperationException(string.Join("; ", errors));
    }

    private static void TryCollect(List<string> errors, string name, Action action)
    {
        try
        {
            action();
        }
        catch (Exception ex)
        {
            errors.Add($"{name}: {ex.Message}");
        }
    }

    // ============ 9. 公网 IP 查询（对照 get_public_ip：多源 fallback） ============

    private enum PublicIpProvider { Plain, Trace }

    private static readonly (string Url, PublicIpProvider Kind)[] PublicIpProviders =
    [
        ("https://4.ipw.cn", PublicIpProvider.Plain),
        ("https://ip.3322.net", PublicIpProvider.Plain),
        ("https://myip.ipip.net", PublicIpProvider.Plain),
        ("https://api.ip.sb/ip", PublicIpProvider.Plain),
        ("https://api.ipify.org", PublicIpProvider.Plain),
        ("https://cloudflare.com/cdn-cgi/trace", PublicIpProvider.Trace),
    ];

    /// <summary>校验是否为合法 IPv4 地址（对照 is_valid_ipv4）。</summary>
    internal static bool IsValidIpv4(string s)
    {
        var parts = s.Split('.');
        if (parts.Length != 4)
            return false;
        foreach (var p in parts)
        {
            if (p.Length == 0 || p.Length > 3)
                return false;
            foreach (var c in p)
                if (!char.IsAsciiDigit(c))
                    return false;
            if (!uint.TryParse(p, out var n) || n > 255)
                return false;
        }
        return true;
    }

    /// <summary>从任意文本提取第一个 IPv4 地址（对照 find_ipv4）。</summary>
    internal static string? FindIpv4(string text)
    {
        var current = new System.Text.StringBuilder();
        foreach (var c in text)
        {
            if (char.IsAsciiDigit(c) || c == '.')
                current.Append(c);
            else
            {
                if (current.Length > 0)
                {
                    if (IsValidIpv4(current.ToString()))
                        return current.ToString();
                    current.Clear();
                }
            }
        }
        return current.Length > 0 && IsValidIpv4(current.ToString()) ? current.ToString() : null;
    }

    /// <summary>按提供商类型提取 IPv4（对照 extract_ipv4：Trace 需从 ip= 行取）。</summary>
    internal static string? ExtractIpv4(string text, string providerKind)
    {
        if (providerKind == "trace")
        {
            foreach (var line in text.Split('\n'))
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("ip=", StringComparison.Ordinal))
                {
                    var ip = FindIpv4(trimmed[3..]);
                    if (ip is not null)
                        return ip;
                }
            }
            return null;
        }
        return FindIpv4(text);
    }

    public static async Task<string> GetPublicIpAsync()
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        foreach (var (url, kind) in PublicIpProviders)
        {
            try
            {
                var text = await client.GetStringAsync(url);
                var ip = ExtractIpv4(text, kind == PublicIpProvider.Trace ? "trace" : "plain");
                if (ip is not null)
                    return ip;
            }
            catch
            {
                // 该源不可达，尝试下一个
            }
        }
        throw new InvalidOperationException("无法获取公网 IPv4 地址，请检查网络连接");
    }
}

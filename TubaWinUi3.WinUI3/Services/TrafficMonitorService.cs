using System.Diagnostics;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace TubaWinUi3.Services;

/// <summary>单条在线 TCP 连接及其流量统计（数据来自 GetPerTcpConnectionEStats）。</summary>
public sealed class TrafficConnectionInfo
{
    public string Key { get; init; } = "";
    public string ProcessName { get; init; } = "";
    public int ProcessId { get; init; }
    public string LocalAddress { get; init; } = "";
    public int LocalPort { get; init; }
    public string RemoteAddress { get; init; } = "";
    public int RemotePort { get; init; }
    public string Protocol { get; init; } = "TCP";
    /// <summary>远程域名（来自系统 DNS 缓存或 PTR 反向解析；无则空串）。</summary>
    public string RemoteDomain { get; init; } = "";
    /// <summary>该连接自建立以来累计下行字节数。</summary>
    public long TotalIn { get; init; }
    /// <summary>该连接自建立以来累计上行字节数。</summary>
    public long TotalOut { get; init; }
    /// <summary>本次采样区间下行速率（B/s）。</summary>
    public long SpeedIn { get; init; }
    /// <summary>本次采样区间上行速率（B/s）。</summary>
    public long SpeedOut { get; init; }
    /// <summary>统计不可用时不能把零误显示为无流量。</summary>
    public bool HasTrafficStats { get; init; } = true;
    public string DisplayRemote => FormatEndpoint(RemoteAddress, RemotePort);
    public static string FormatEndpoint(string address, int port)
        => address.Contains(':') ? $"[{address}]:{port}" : $"{address}:{port}";
}

/// <summary>一次采样快照：整卡会话统计 + 各连接明细。</summary>
public sealed class TrafficSnapshot
{
    public int AdapterIndex { get; init; }
    public int ConnectionCount { get; init; }
    public string ErrorMessage { get; init; } = "";
    public DateTime Time { get; init; }
    /// <summary>本次监控会话累计下行字节（自服务启动起）。</summary>
    public long TotalIn { get; init; }
    /// <summary>本次监控会话累计上行字节（自服务启动起）。</summary>
    public long TotalOut { get; init; }
    /// <summary>整卡下行速率（B/s，含 UDP 等所有协议）。</summary>
    public long SpeedIn { get; init; }
    /// <summary>整卡上行速率（B/s）。</summary>
    public long SpeedOut { get; init; }
    public IReadOnlyList<TrafficConnectionInfo> Connections { get; init; } = [];
}

/// <summary>快照录制器：每秒追加一条，超出上限丢弃最旧（内存回放，不落盘）。</summary>
public sealed class TrafficSnapshotRecorder
{
    public const int MaxSnapshots = 3600;

    private readonly List<TrafficSnapshot> _items = [];
    private readonly object _lock = new();

    public int Count
    {
        get { lock (_lock) return _items.Count; }
    }

    public void Add(TrafficSnapshot snapshot)
    {
        lock (_lock)
        {
            if (_items.Count >= MaxSnapshots) _items.RemoveAt(0);
            _items.Add(snapshot);
        }
    }

    public bool TryGet(int index, out TrafficSnapshot snapshot)
    {
        lock (_lock)
        {
            if (index < 0 || index >= _items.Count) { snapshot = null!; return false; }
            snapshot = _items[index];
            return true;
        }
    }

    public TrafficSnapshot? Latest
    {
        get { lock (_lock) return _items.Count > 0 ? _items[^1] : null; }
    }

    public void Clear()
    {
        lock (_lock) _items.Clear();
    }

    public TrafficSnapshot[] ToArray()
    {
        lock (_lock) return _items.ToArray();
    }
}

/// <summary>
/// 网卡流量监控服务：每秒轮询一次所选网卡的整卡速率与各在线 TCP 连接
/// 的累计流量（GetPerTcpConnectionEStats）、实时速率，通过 <see cref="Tick"/> 事件推送。
/// </summary>
public static class TrafficMonitorService
{
    // 逐连接统计较昂贵，控制池大小：池内最多统计 200 条，展示排序后前 150 条
    private const int StatsCap = 200;
    private const int DisplayCap = 150;

    private const int AF_INET = 2;
    private const int AF_INET6 = 23;
    private const int TCP_TABLE_OWNER_PID_ALL = 5;
    private const int ESTABLISHED = 5;
    // TCP_ESTATS_TYPE 枚举：SynOpts=0, Data=1（26100 SDK 中 Data 不是 0，传 0 会导致 Set 返回 ERROR_INVALID_USER_BUFFER）
    private const int TCP_ESTATS_TYPE_DATA = 1;

    [DllImport("iphlpapi.dll")]
    private static extern int GetExtendedTcpTable(IntPtr pTcpTable, ref int pdwSize, [MarshalAs(UnmanagedType.Bool)] bool bOrder, int ulAf, int TableClass, int reserved);

    [DllImport("iphlpapi.dll")]
    private static extern int GetPerTcpConnectionEStats(ref Native.MIB_TCPROW row, int estatsType, ref Native.TCP_ESTATS_DATA_RW_v0 rw, uint rwVersion, uint rwSize, IntPtr ros, uint rosVersion, uint rosSize, ref Native.TCP_ESTATS_DATA_ROD_v0 rod, uint rodVersion, uint rodSize);

    [DllImport("iphlpapi.dll")]
    private static extern int GetPerTcp6ConnectionEStats(ref Native.MIB_TCP6ROW row, int estatsType, ref Native.TCP_ESTATS_DATA_RW_v0 rw, uint rwVersion, uint rwSize, IntPtr ros, uint rosVersion, uint rosSize, ref Native.TCP_ESTATS_DATA_ROD_v0 rod, uint rodVersion, uint rodSize);

    [DllImport("iphlpapi.dll")]
    private static extern int SetPerTcpConnectionEStats(ref Native.MIB_TCPROW row, int estatsType, ref Native.TCP_ESTATS_DATA_RW_v0 rw, uint rwVersion, uint rwSize, uint offset);

    [DllImport("iphlpapi.dll")]
    private static extern int SetPerTcp6ConnectionEStats(ref Native.MIB_TCP6ROW row, int estatsType, ref Native.TCP_ESTATS_DATA_RW_v0 rw, uint rwVersion, uint rwSize, uint offset);

    [DllImport("ws2_32.dll")]
    private static extern ushort ntohs(uint netshort);

    /// <summary>iphlpapi 相关原生结构，嵌套以避免与项目其他服务的结构重名。</summary>
    private static class Native
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct MIB_TCPROW
        {
            public uint dwState;
            public uint dwLocalAddr;
            public uint dwLocalPort;
            public uint dwRemoteAddr;
            public uint dwRemotePort;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct MIB_TCP6ROW
        {
            public uint dwState;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] LocalAddr;
            public uint dwLocalScopeId;
            public uint dwLocalPort;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] RemoteAddr;
            public uint dwRemoteScopeId;
            public uint dwRemotePort;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct MIB_TCPROW_OWNER_PID
        {
            public uint dwState;
            public uint dwLocalAddr;
            public uint dwLocalPort;
            public uint dwRemoteAddr;
            public uint dwRemotePort;
            public uint dwOwningPid;
        }

        [StructLayout(LayoutKind.Sequential)]
        internal struct MIB_TCP6ROW_OWNER_PID
        {
            // OWNER_PID 与 MIB_TCP6ROW 的字段顺序不同，大小固定为 56 字节。
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] LocalAddr;
            public uint dwLocalScopeId;
            public uint dwLocalPort;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)] public byte[] RemoteAddr;
            public uint dwRemoteScopeId;
            public uint dwRemotePort;
            public uint dwState;
            public uint dwOwningPid;
        }

        /// <summary>读/写配置：EnableCollection 用于启用/关闭该连接的统计收集（Set 传入）。</summary>
        [StructLayout(LayoutKind.Sequential)]
        internal struct TCP_ESTATS_DATA_RW_v0
        {
            [MarshalAs(UnmanagedType.U1)] public bool EnableCollection;
        }

        // 26100 SDK 的 tcpestats.h 布局（96 字节）：6×ULONG64 + 5×ULONG + ULONG64 + ULONG + ULONG64
        [StructLayout(LayoutKind.Sequential)]
        internal struct TCP_ESTATS_DATA_ROD_v0
        {
            public ulong DataBytesOut;
            public ulong DataSegsOut;
            public ulong DataBytesIn;
            public ulong DataSegsIn;
            public ulong SegsOut;
            public ulong SegsIn;
            public uint SoftErrors;
            public uint SoftErrorReason;
            public uint SndUna;
            public uint SndNxt;
            public uint SndMax;
            public ulong ThruBytesAcked;
            public uint RcvNxt;
            public ulong ThruBytesReceived;
        }
    }

    /// <summary>后台轮询线程每完成一次采样推送一条快照（后台线程触发，UI 侧需编组）。</summary>
    public static event Action<TrafficSnapshot>? Tick;

    private static CancellationTokenSource? _cts;

    public static bool IsRunning => _cts is not null;

    public static void Start(int ifIndex)
    {
        Stop();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        // 不向 Task.Run 传 token：避免任务尚未开始即被取消时产生未观察的 Canceled 任务；
        // 循环内部通过 Task.Delay(token) 响应取消，且整体 try/catch 保证任务总是正常完成。
        _ = Task.Run(async () =>
        {
            AdapterScope? scope = null;
            long prevIn = -1, prevOut = -1;
            long previousTimestamp = Stopwatch.GetTimestamp();
            long sessionIn = 0, sessionOut = 0;
            var prevStats = new Dictionary<string, ConnStat>();
            // 每次运行拥有独立状态；切网卡时旧任务可能还在结束，不能共用 HashSet。
            var statsEnabled = new HashSet<string>();
            int loop = 0;

            while (!token.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(1000, token);
                    if (scope is null)
                    {
                        scope = ResolveScope(ifIndex);
                        if (scope is null)
                        {
                            if (!token.IsCancellationRequested)
                                Tick?.Invoke(new TrafficSnapshot { AdapterIndex = ifIndex, Time = DateTime.Now, ErrorMessage = "网卡已移除，正在等待重新连接…" });
                            continue;
                        }
                    }
                    // 网卡可能重连/IP 变化，定期重新解析
                    if (++loop % 5 == 0) scope = ResolveScope(ifIndex);
                    if (scope is null) continue;
                    if (scope.Nic.OperationalStatus != OperationalStatus.Up)
                    {
                        prevIn = prevOut = -1;
                        prevStats.Clear();
                        if (!token.IsCancellationRequested)
                            Tick?.Invoke(new TrafficSnapshot { AdapterIndex = ifIndex, Time = DateTime.Now, ErrorMessage = "网卡未连接，正在等待网络恢复…" });
                        continue;
                    }

                    long totIn, totOut;
                    try
                    {
                        var st = scope.Nic.GetIPv4Statistics();
                        totIn = st.BytesReceived;
                        totOut = st.BytesSent;
                    }
                    catch
                    {
                        scope = null;
                        continue;
                    }

                    var timestamp = Stopwatch.GetTimestamp();
                    var elapsed = Stopwatch.GetElapsedTime(previousTimestamp, timestamp).TotalSeconds;
                    var deltaIn = prevIn >= 0 ? Math.Max(0, totIn - prevIn) : 0;
                    var deltaOut = prevOut >= 0 ? Math.Max(0, totOut - prevOut) : 0;
                    var speedIn = CalculateRate(deltaIn, elapsed);
                    var speedOut = CalculateRate(deltaOut, elapsed);
                    prevIn = totIn;
                    prevOut = totOut;
                    previousTimestamp = timestamp;
                    sessionIn += deltaIn;
                    sessionOut += deltaOut;

                    var (conns, next, totalCount) = BuildConnections(scope, prevStats, statsEnabled, StatsCap, DisplayCap);
                    prevStats = next;

                    RefreshDomains(conns, token);

                    var sample = new TrafficSnapshot
                    {
                        AdapterIndex = ifIndex,
                        ConnectionCount = totalCount,
                        Time = DateTime.Now,
                        TotalIn = sessionIn,
                        TotalOut = sessionOut,
                        SpeedIn = speedIn,
                        SpeedOut = speedOut,
                        Connections = conns
                    };

                    if (!token.IsCancellationRequested)
                        try { Tick?.Invoke(sample); } catch { }
                }
                catch (OperationCanceledException) { break; }
                catch { }
            }
        });
    }

    public static void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
    }

    internal static long CalculateRate(long deltaBytes, double elapsedSeconds)
        => deltaBytes <= 0 || !double.IsFinite(elapsedSeconds) || elapsedSeconds <= 0
            ? 0 : (long)Math.Min(long.MaxValue, deltaBytes / elapsedSeconds);

    /// <summary>ICMP Ping 远程地址，返回毫秒延迟；失败/超时返回 null。</summary>
    public static async Task<long?> PingAsync(IPAddress address, int timeoutMs = 2000)
    {
        if (address is null) return null;
        try
        {
            using var ping = new Ping();
            var reply = await ping.SendPingAsync(address, timeoutMs);
            return reply.Status == IPStatus.Success ? reply.RoundtripTime : null;
        }
        catch { return null; }
    }

    /// <summary>按实时活跃度（下载+上传速率）降序并截断，供展示使用。</summary>
    public static List<TrafficConnectionInfo> SortAndCap(IEnumerable<TrafficConnectionInfo> source, int cap)
    {
        return source
            .OrderByDescending(c => c.SpeedIn + c.SpeedOut)
            .ThenBy(c => c.Key)
            .Take(cap)
            .ToList();
    }

    #region 域名解析

    // 域名来源优先级：系统 DNS 缓存表（ipconfig /displaydns）> PTR 反向解析
    private const int DnsTableRefreshEveryTicks = 5; // 每 5 秒刷一次系统 DNS 缓存表
    private const int MaxPtrCacheEntries = 4096;     // PTR 结果软缓存上限（超限整体清空，按需重建）
    private static readonly Dictionary<string, string> s_domainCache = new(); // ip -> 域名（缓存表）
    private static readonly Dictionary<string, string> s_ptrCache = new();   // ip -> 域名（PTR 结果）
    private static readonly HashSet<string> s_ptrPending = new();             // 正在 PTR 解析的 ip
    private static readonly object s_domainLock = new();
    private static int s_dnsTick;
    private static int s_dnsRefreshing;

    /// <summary>查询远程 IP 的已知域名（缓存表优先，其次 PTR），无则返回 null。</summary>
    public static string? GetDomainForIp(string? ip)
    {
        if (string.IsNullOrEmpty(ip)) return null;
        var key = ip.Split('%')[0]; // 去掉 IPv6 链路本地 scope
        lock (s_domainLock)
        {
            if (s_domainCache.TryGetValue(key, out var d)) return d;
            if (s_ptrCache.TryGetValue(key, out var p)) return p == key ? null : p;
            return null;
        }
    }

    private static void RefreshDomains(IReadOnlyList<TrafficConnectionInfo> conns, CancellationToken token)
    {
        // 定期刷新系统 DNS 缓存表（ipconfig /displaydns，无需权限）
        if (Interlocked.Increment(ref s_dnsTick) % DnsTableRefreshEveryTicks == 1
            && Interlocked.CompareExchange(ref s_dnsRefreshing, 1, 0) == 0)
        {
            _ = Task.Run(async () =>
            {
                try { await RefreshDnsCacheTableAsync(token); } catch { }
                finally { Interlocked.Exchange(ref s_dnsRefreshing, 0); }
            });
        }

        // 对缓存表未命中的 IP 异步发起 PTR 反向解析（限并发、失败不重试）
        foreach (var c in conns)
        {
            var ip = c.RemoteAddress.Split('%')[0];
            if (string.IsNullOrEmpty(ip)) continue;

            lock (s_domainLock)
            {
                if (s_domainCache.ContainsKey(ip) || s_ptrCache.ContainsKey(ip) || s_ptrPending.Contains(ip) || s_ptrPending.Count >= 8) continue;
                s_ptrPending.Add(ip);
            }

            _ = Task.Run(async () =>
            {
                string? domain = null;
                try
                {
                    if (IPAddress.TryParse(ip, out var addr))
                    {
                        var hostTask = Dns.GetHostEntryAsync(addr);
                        // 显式观察原始任务异常，避免超时/取消后其异常成为未观察异常（finalizer 线程重抛）
                        _ = hostTask.ContinueWith(t => _ = t.Exception, CancellationToken.None,
                            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                            TaskScheduler.Default);
                        var done = await Task.WhenAny(hostTask, Task.Delay(2000, token));
                        if (done == hostTask)
                        {
                            var entry = await hostTask;
                            domain = entry.HostName;
                        }
                    }
                }
                catch { }

                lock (s_domainLock)
                {
                    s_ptrPending.Remove(ip);
                    if (s_ptrCache.Count >= MaxPtrCacheEntries) s_ptrCache.Clear();
                    if (!string.IsNullOrWhiteSpace(domain)) s_ptrCache[ip] = domain;
                    else s_ptrCache[ip] = ip; // 占位防重试：解析失败记原 IP
                }
            });
        }
    }

    /// <summary>解析 ipconfig /displaydns 输出：域名块 -> A/AAAA 记录 IP。纯函数，可测试。</summary>
    public static Dictionary<string, string> ParseDisplayDnsOutput(string output)
    {
        var result = new Dictionary<string, string>();
        string? currentName = null;

        foreach (var rawLine in output.Split('\n'))
        {
            var line = rawLine.Trim();
            var nameMatch = Regex.Match(line, @"(?:记录名称|Record Name)[\s.]*:\s*(\S+)", RegexOptions.IgnoreCase);
            if (nameMatch.Success)
            {
                currentName = nameMatch.Groups[1].Value;
                continue;
            }
            if (currentName is null) continue;

            var aMatch = Regex.Match(line, @"(?:A \(主机\)记录|A \(Host\) Record)[\s.]*:\s*(\d+\.\d+\.\d+\.\d+)", RegexOptions.IgnoreCase);
            if (aMatch.Success)
            {
                result.TryAdd(aMatch.Groups[1].Value, currentName);
                currentName = null;
                continue;
            }

            var aaaaMatch = Regex.Match(line, @"(?:AAAA 记录|AAAA Record)[\s.]*:\s*([0-9a-fA-F:]+)", RegexOptions.IgnoreCase);
            if (aaaaMatch.Success)
            {
                result.TryAdd(aaaaMatch.Groups[1].Value, currentName);
                currentName = null;
            }
        }
        return result;
    }

    private static async Task RefreshDnsCacheTableAsync(CancellationToken token)
    {
        using var proc = new Process
        {
            StartInfo = new ProcessStartInfo("ipconfig", "/displaydns")
            {
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                StandardOutputEncoding = GetOemEncoding()
            }
        };
        proc.Start();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
        timeout.CancelAfter(TimeSpan.FromSeconds(3));
        string output;
        try
        {
            output = await proc.StandardOutput.ReadToEndAsync(timeout.Token);
            await proc.WaitForExitAsync(timeout.Token);
        }
        catch
        {
            // 仅回收本次创建的 ipconfig 子进程，避免超时后遗留。
            try { if (!proc.HasExited) proc.Kill(); } catch { }
            throw;
        }

        var map = ParseDisplayDnsOutput(output);
        lock (s_domainLock)
        {
            s_domainCache.Clear();
            foreach (var kv in map) s_domainCache[kv.Key] = kv.Value;
        }
    }

    /// <summary>ipconfig 输出使用系统 OEM/ANSI 代码页（中文系统 GBK）；.NET 内置不含这些代码页，需注册提供程序。</summary>
    private static Encoding GetOemEncoding()
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            return Encoding.GetEncoding(System.Globalization.CultureInfo.CurrentCulture.TextInfo.ANSICodePage);
        }
        catch
        {
            return Encoding.UTF8;
        }
    }

    #endregion

    #region 后台实现

    private sealed class AdapterScope
    {
        public required NetworkInterface Nic { get; init; }
        public required HashSet<IPAddress> LocalAddrs { get; init; }
        public uint V6ScopeId { get; init; }
    }

    private sealed class RawConn
    {
        public required string Key { get; init; }
        public required string LocalAddress { get; init; }
        public required string RemoteAddress { get; init; }
        public int LocalPort { get; init; }
        public int RemotePort { get; init; }
        public int Pid { get; init; }
        public bool IsV6 { get; init; }
        public Native.MIB_TCPROW Row4 { get; init; }
        public Native.MIB_TCP6ROW Row6 { get; init; }
    }

    private sealed record ConnStat(long BytesIn, long BytesOut, long Timestamp);

    private static AdapterScope? ResolveScope(int ifIndex)
    {
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            var props = nic.GetIPProperties();
            int v4 = 0, v6 = 0;
            try { v4 = props.GetIPv4Properties()?.Index ?? 0; } catch { }
            try { v6 = props.GetIPv6Properties()?.Index ?? 0; } catch { }
            if (ifIndex != v4 && ifIndex != v6) continue;

            var addrs = new HashSet<IPAddress>();
            foreach (var u in props.UnicastAddresses)
            {
                if (u.Address.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6)
                    addrs.Add(u.Address);
            }

            return new AdapterScope { Nic = nic, LocalAddrs = addrs, V6ScopeId = (uint)v6 };
        }
        return null;
    }

    private static (List<TrafficConnectionInfo> conns, Dictionary<string, ConnStat> next, int totalCount) BuildConnections(
        AdapterScope scope, Dictionary<string, ConnStat> prev, HashSet<string> statsEnabled, int statsCap, int displayCap)
    {
        var raw = ReadTcpRows(scope);

        // 池策略：保持上一轮已跟踪的连接（保证活跃连接持续统计），新连接最多补充 statsCap 条
        var pool = raw.OrderByDescending(c => prev.ContainsKey(c.Key)).Take(statsCap).ToList();
        statsEnabled.IntersectWith(pool.Select(c => c.Key));

        var procCache = new Dictionary<int, string>();
        var next = new Dictionary<string, ConnStat>(pool.Count);
        var result = new List<TrafficConnectionInfo>(pool.Count);

        foreach (var c in pool)
        {
            var stat = QueryConnStats(c, statsEnabled);
            if (stat is null)
            {
                // 统计不可用（收集未启用/权限不足/连接关闭）——连接仍展示，流量按 0 计
                result.Add(MakeConnection(c, procCache, 0, 0, 0, 0, false));
                continue;
            }
            next[c.Key] = stat;

            var old = prev.TryGetValue(c.Key, out var o) ? o : null;
            var elapsed = old is null ? 0 : Stopwatch.GetElapsedTime(old.Timestamp, stat.Timestamp).TotalSeconds;
            var speedIn = old is null ? 0 : CalculateRate(stat.BytesIn - old.BytesIn, elapsed);
            var speedOut = old is null ? 0 : CalculateRate(stat.BytesOut - old.BytesOut, elapsed);

            result.Add(MakeConnection(c, procCache, stat.BytesIn, stat.BytesOut, speedIn, speedOut));
        }

        return (SortAndCap(result, displayCap), next, raw.Count);
    }

    private static TrafficConnectionInfo MakeConnection(RawConn c, Dictionary<int, string> procCache,
        long totalIn, long totalOut, long speedIn, long speedOut, bool hasTrafficStats = true)
    {
        return new TrafficConnectionInfo
        {
            Key = c.Key,
            ProcessName = GetProcessName(c.Pid, procCache),
            ProcessId = c.Pid,
            LocalAddress = c.LocalAddress,
            LocalPort = c.LocalPort,
            RemoteAddress = c.RemoteAddress,
            RemotePort = c.RemotePort,
            Protocol = c.IsV6 ? "TCP6" : "TCP",
            RemoteDomain = GetDomainForIp(c.RemoteAddress) ?? "",
            TotalIn = totalIn,
            TotalOut = totalOut,
            SpeedIn = speedIn,
            SpeedOut = speedOut,
            HasTrafficStats = hasTrafficStats
        };
    }

    private static List<RawConn> ReadTcpRows(AdapterScope scope)
    {
        var result = new List<RawConn>();
        ReadTcpTable4(scope, result);
        ReadTcpTable6(scope, result);
        return result;
    }

    private static void ReadTcpTable4(AdapterScope scope, List<RawConn> result)
    {
        // 第一次调用仅用于获取表大小，必然返回 ERROR_INSUFFICIENT_BUFFER(122)，不能当作失败
        int size = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref size, false, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0);
        if (size == 0) return;

        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedTcpTable(ptr, ref size, false, AF_INET, TCP_TABLE_OWNER_PID_ALL, 0) != 0)
                return;

            var count = Marshal.ReadInt32(ptr);
            var rowPtr = ptr + 4;
            var rowSize = Marshal.SizeOf<Native.MIB_TCPROW_OWNER_PID>();

            for (int i = 0; i < count; i++)
            {
                var row = Marshal.PtrToStructure<Native.MIB_TCPROW_OWNER_PID>(rowPtr);
                rowPtr += rowSize;
                if (row.dwState != ESTABLISHED) continue;

                var local = new IPAddress(row.dwLocalAddr);
                if (!scope.LocalAddrs.Contains(local)) continue;

                var remote = new IPAddress(row.dwRemoteAddr);
                var localPort = ntohs(row.dwLocalPort);
                var remotePort = ntohs(row.dwRemotePort);

                result.Add(new RawConn
                {
                    Key = MakeKey(local.ToString(), localPort, remote.ToString(), remotePort),
                    LocalAddress = local.ToString(),
                    RemoteAddress = remote.ToString(),
                    LocalPort = localPort,
                    RemotePort = remotePort,
                    Pid = (int)row.dwOwningPid,
                    IsV6 = false,
                    Row4 = new Native.MIB_TCPROW
                    {
                        dwState = row.dwState,
                        dwLocalAddr = row.dwLocalAddr,
                        dwLocalPort = row.dwLocalPort,
                        dwRemoteAddr = row.dwRemoteAddr,
                        dwRemotePort = row.dwRemotePort
                    }
                });
            }
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    private static void ReadTcpTable6(AdapterScope scope, List<RawConn> result)
    {
        // 第一次调用仅用于获取表大小，必然返回 ERROR_INSUFFICIENT_BUFFER(122)，不能当作失败
        int size = 0;
        GetExtendedTcpTable(IntPtr.Zero, ref size, false, AF_INET6, TCP_TABLE_OWNER_PID_ALL, 0);
        if (size == 0) return;

        var ptr = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedTcpTable(ptr, ref size, false, AF_INET6, TCP_TABLE_OWNER_PID_ALL, 0) != 0)
                return;

            var count = Marshal.ReadInt32(ptr);
            var rowPtr = ptr + 4;
            var rowSize = Marshal.SizeOf<Native.MIB_TCP6ROW_OWNER_PID>();

            for (int i = 0; i < count; i++)
            {
                var row = Marshal.PtrToStructure<Native.MIB_TCP6ROW_OWNER_PID>(rowPtr);
                rowPtr += rowSize;
                if (row.dwState != ESTABLISHED) continue;

                var local = new IPAddress(row.LocalAddr, row.dwLocalScopeId);
                if (!scope.LocalAddrs.Contains(local)) continue;

                var remote = new IPAddress(row.RemoteAddr, row.dwRemoteScopeId);
                var localPort = ntohs(row.dwLocalPort);
                var remotePort = ntohs(row.dwRemotePort);

                result.Add(new RawConn
                {
                    Key = MakeKey(FormatV6Addr(local, row.dwLocalScopeId), localPort, FormatV6Addr(remote, row.dwRemoteScopeId), remotePort),
                    LocalAddress = FormatV6Addr(local, row.dwLocalScopeId),
                    RemoteAddress = FormatV6Addr(remote, row.dwRemoteScopeId),
                    LocalPort = localPort,
                    RemotePort = remotePort,
                    Pid = (int)row.dwOwningPid,
                    IsV6 = true,
                    Row6 = new Native.MIB_TCP6ROW
                    {
                        dwState = row.dwState,
                        LocalAddr = row.LocalAddr,
                        dwLocalScopeId = row.dwLocalScopeId,
                        dwLocalPort = row.dwLocalPort,
                        RemoteAddr = row.RemoteAddr,
                        dwRemoteScopeId = row.dwRemoteScopeId,
                        dwRemotePort = row.dwRemotePort
                    }
                });
            }
        }
        finally
        {
            Marshal.FreeHGlobal(ptr);
        }
    }

    private static ConnStat? QueryConnStats(RawConn c, HashSet<string> statsEnabled)
    {
        // 连接首次出现（或定期重刷）时启用该连接的统计收集；需要管理员权限
        if (!statsEnabled.Contains(c.Key))
        {
            var rwEnable = new Native.TCP_ESTATS_DATA_RW_v0 { EnableCollection = true };
            int sr = EnableCollection(c, ref rwEnable);
            if (sr != 0) return null; // 启用失败（非管理员等），该连接无统计
            statsEnabled.Add(c.Key);
        }

        var rw = new Native.TCP_ESTATS_DATA_RW_v0();
        var rod = default(Native.TCP_ESTATS_DATA_ROD_v0);
        var rodSize = (uint)Marshal.SizeOf<Native.TCP_ESTATS_DATA_ROD_v0>();
        int rc = ReadStats(c, ref rw, ref rod, rodSize);
        if (rc != 0) return null;

        // 若收集意外被关闭（例如被其他程序禁用），重新启用
        if (!rw.EnableCollection)
        {
            var rwEnable = new Native.TCP_ESTATS_DATA_RW_v0 { EnableCollection = true };
            EnableCollection(c, ref rwEnable);
        }

        return rw.EnableCollection ? new ConnStat((long)rod.DataBytesIn, (long)rod.DataBytesOut, Stopwatch.GetTimestamp()) : null;
    }

    // 属性不能按 ref 传递，先拷贝到局部变量
    private static int EnableCollection(RawConn c, ref Native.TCP_ESTATS_DATA_RW_v0 rw)
    {
        if (c.IsV6)
        {
            var row = c.Row6;
            return SetPerTcp6ConnectionEStats(ref row, TCP_ESTATS_TYPE_DATA, ref rw, 0, 1, 0);
        }
        var row4 = c.Row4;
        return SetPerTcpConnectionEStats(ref row4, TCP_ESTATS_TYPE_DATA, ref rw, 0, 1, 0);
    }

    private static int ReadStats(RawConn c, ref Native.TCP_ESTATS_DATA_RW_v0 rw, ref Native.TCP_ESTATS_DATA_ROD_v0 rod, uint rodSize)
    {
        if (c.IsV6)
        {
            var row = c.Row6;
            return GetPerTcp6ConnectionEStats(ref row, TCP_ESTATS_TYPE_DATA, ref rw, 0, 1, IntPtr.Zero, 0, 0, ref rod, 0, rodSize);
        }
        var row4 = c.Row4;
        return GetPerTcpConnectionEStats(ref row4, TCP_ESTATS_TYPE_DATA, ref rw, 0, 1, IntPtr.Zero, 0, 0, ref rod, 0, rodSize);
    }

    private static string GetProcessName(int pid, Dictionary<int, string> cache)
    {
        if (pid == 0 || pid == 4) return "System";
        if (cache.TryGetValue(pid, out var n)) return n;
        try { using var p = Process.GetProcessById(pid); return cache[pid] = p.ProcessName; }
        catch { return cache[pid] = $"PID:{pid}"; }
    }

    private static string MakeKey(string local, int localPort, string remote, int remotePort)
        => $"{local}:{localPort}|{remote}:{remotePort}";

    private static string FormatV6Addr(IPAddress addr, uint scopeId)
    {
        var s = addr.ToString();
        if (addr.IsIPv6LinkLocal && scopeId > 0 && !s.Contains('%')) return $"{s}%{scopeId}";
        return s;
    }

    #endregion
}


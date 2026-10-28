using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using TubaWinUi3.Models;

namespace TubaWinUi3.Services;

/// <summary>手机端聊天消息（局域网「连接手机」）。</summary>
public sealed class PhoneChatMessage
{
    public long Seq { get; set; }
    public string Id { get; set; } = "";
    /// <summary>"phone" 或 "pc"。</summary>
    public string From { get; set; } = "";
    /// <summary>"text" / "image" / "file"。</summary>
    public string Type { get; set; } = "text";
    public string Text { get; set; } = "";
    public string FileName { get; set; } = "";
    public long Size { get; set; }
    public long Time { get; set; }
}

/// <summary>
/// 「连接手机」局域网控制服务：HttpListener + JSON。
/// 配对码（或二维码里携带的同一配对码）换取会话令牌，之后所有接口都要带 X-Token。
/// 接口：info / hardware(电脑配置) / monitor(实时数据，?fps=1 含帧率) / screenshot / exec(PowerShell) / winget / chat。
/// </summary>
public static class PhoneLinkService
{
    public const int DefaultPort = 18765;
    /// <summary>单文件上传上限 20 GB（局域网直传、边收边落盘，不经过内存缓存）。</summary>
    internal const long MaxUploadBytes = 20L * 1024 * 1024 * 1024;
    /// <summary>文件传输 I/O 缓冲 1 MB：把局域网直传吞吐拉到接近链路理论速度。</summary>
    private const int IoBufferSize = 1 << 20;

    private static HttpListener? _listener;
    private static PhoneLinkDiscovery? _discovery;
    private static CancellationTokenSource? _cts;
    private static readonly object _stateLock = new();
    private static readonly SemaphoreSlim _monitorLock = new(1, 1);
    private static readonly object _jobsLock = new();
    private static readonly List<PhoneJob> _jobList = [];
    private static readonly Dictionary<string, PhoneJob> _jobs = [];
    private static int _failedPairs;
    private static DateTime _lockedUntil = DateTime.MinValue;

    /// <summary>接口版本：手机端与电脑端解耦升级时据此协商能力。</summary>
    public const int ApiVersion = 2;

    public static bool IsRunning { get; private set; }
    public static int Port { get; private set; } = DefaultPort;
    internal const string PairCodeSettingKey = "PhoneLinkPairCode";
    public static string PairCode { get; private set; } = LoadPairCode();
    public static event Action? StateChanged;
    public static event Action<PhoneChatMessage>? MessageAdded;
    public static event Action? JobsChanged;
    public static event Action<PhoneJob>? JobStarted;
    public static event Action<PhoneJob>? JobFinished;
    public static event Action<PhoneTransferProgress>? TransferProgress;

    private const string ReceiveDirSettingKey = "PhoneLinkReceiveDir";
    private static string? _defaultReceiveDir;

    /// <summary>用户自定义的接收位置；未设置时为 null（用 Windows「下载」文件夹）。</summary>
    public static string? CustomReceiveDir
    {
        get
        {
            var value = AppSettings.Get(ReceiveDirSettingKey);
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
    }

    /// <summary>设置接收位置（null/空 = 恢复默认的 Windows「下载」文件夹）。</summary>
    public static void SetReceiveDir(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) AppSettings.Remove(ReceiveDirSettingKey);
        else AppSettings.Set(ReceiveDirSettingKey, path);
    }

    /// <summary>接收文件目录：用户自定义位置优先，否则 Windows「下载」文件夹（手机发来的文件只存这一份）。</summary>
    public static string ReceiveDir
    {
        get
        {
            var custom = CustomReceiveDir;
            if (custom is not null)
            {
                try
                {
                    Directory.CreateDirectory(custom);
                    return custom;
                }
                catch
                {
                    // 自定义位置已不可用（目录被删/磁盘移除）→ 回退默认
                }
            }
            return _defaultReceiveDir ??= DownloadsFolder.Resolve();
        }
    }

    public static IReadOnlyList<string> PairedDeviceNames => PairedDeviceStore.Snapshot().Select(d => d.Name).Distinct().ToList();

    /// <summary>已配对设备（含签发/过期时间），电脑端可逐台断开。</summary>
    public static IReadOnlyList<PhoneLinkDeviceInfo> PairedDevices => PairedDeviceStore.Snapshot();

    public static IReadOnlyList<PhoneChatMessage> GetMessages() => PhoneLinkChatStore.Snapshot();

    /// <summary>取某条文件/图片消息在本机的文件路径（聊天弹窗缩略图与「另存为」用）。</summary>
    public static string? GetMessageFilePath(string messageId) => PhoneLinkChatStore.GetFilePath(messageId);

    // ───────────────────────── 地址 / 配对 ─────────────────────────

    /// <summary>所有可用于局域网连接的 IPv4 地址（排除回环、APIPA、未启用网卡及隧道）。</summary>
    public static List<string> GetLocalIps()
    {
        var result = new List<string>();
        try
        {
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (nic.OperationalStatus != OperationalStatus.Up) continue;
                if (nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                foreach (var ua in nic.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    var s = ua.Address.ToString();
                    if (s.StartsWith("169.254.", StringComparison.Ordinal)) continue;
                    result.Add(s);
                }
            }
        }
        catch { }
        if (result.Count == 0) result.Add("127.0.0.1");
        return result.Distinct().ToList();
    }

    /// <summary>二维码内容：tubalink://IP:端口?code=配对码&amp;name=电脑名。</summary>
    public static string BuildQrPayload(string ip) =>
        $"tubalink://{ip}:{Port}?code={PairCode}&name={Uri.EscapeDataString(Environment.MachineName)}";

    internal static string NewPairCode() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

    internal static string LoadPairCode()
    {
        var saved = AppSettings.Get(PairCodeSettingKey);
        if (saved is { Length: 6 } && saved.All(c => c is >= '0' and <= '9')) return saved;
        var code = NewPairCode();
        AppSettings.Set(PairCodeSettingKey, code);
        AppSettings.Flush();
        return code;
    }

    // Caller holds _stateLock. Explicit rotations must never leave the old code valid.
    private static void RotatePairCode()
    {
        string next;
        do { next = NewPairCode(); } while (next == PairCode);
        PairCode = next;
        AppSettings.Set(PairCodeSettingKey, next);
        AppSettings.Flush();
    }

    public static void RefreshPairCode()
    {
        lock (_stateLock) RotatePairCode();
        StateChanged?.Invoke();
    }

    /// <summary>校验配对码并签发令牌。连续输错 5 次锁定 30 秒，防止局域网内暴力枚举。</summary>
    internal static (string? Token, string? Error) TryPair(string? code, string? deviceName)
    {
        string token;
        string name;
        lock (_stateLock)
        {
            if (DateTime.UtcNow < _lockedUntil) return (null, "尝试次数过多，请稍后再试");
            name = string.IsNullOrWhiteSpace(deviceName) ? "手机" : deviceName.Trim();
            if (name.Length > 40) name = name[..40];
            if (string.IsNullOrEmpty(code) || !FixedTimeEquals(code.Trim(), PairCode))
            {
                if (++_failedPairs >= 5)
                {
                    _failedPairs = 0;
                    _lockedUntil = DateTime.UtcNow.AddSeconds(30);
                    RotatePairCode();
                    StateChanged?.Invoke();
                    LogService.Warn(LogService.PhoneLink, $"配对连续失败 5 次，已轮换配对码并锁定 30 秒（设备「{name}」）");
                }
                else
                {
                    LogService.Warn(LogService.PhoneLink, $"配对失败：配对码错误（设备「{name}」）");
                }
                return (null, "配对码错误");
            }
            _failedPairs = 0;
            token = PairedDeviceStore.Issue(name, out _);
        }
        StateChanged?.Invoke();
        return (token, null);
    }

    internal static bool FixedTimeEquals(string a, string b) =>
        CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));

    // ───────────────────────── 首次连接授权 ─────────────────────────

    private const string ApprovedDevicesSettingKey = "PhoneLinkApprovedDevices";
    private static readonly HashSet<string> _approvedDevices = LoadApprovedDevices();

    /// <summary>电脑端首次连接授权回调（App 注册并弹窗；未注册时新设备一律拒绝）。</summary>
    internal static Func<string, Task<bool>>? PairApprovalHandler;

    private static HashSet<string> LoadApprovedDevices()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            var json = AppSettings.Get(ApprovedDevicesSettingKey);
            if (!string.IsNullOrWhiteSpace(json))
            {
                foreach (var name in System.Text.Json.JsonSerializer.Deserialize<List<string>>(json) ?? [])
                    if (!string.IsNullOrWhiteSpace(name)) set.Add(name);
            }
        }
        catch
        {
        }
        return set;
    }

    /// <summary>只有电脑端明确记住的设备才可免再次询问；待授权令牌不算授权。</summary>
    internal static bool IsDeviceApproved(string deviceName)
    {
        lock (_approvedDevices)
        {
            if (_approvedDevices.Contains(deviceName)) return true;
        }
        return false;
    }

    private static void RememberApprovedDevice(string deviceName)
    {
        lock (_approvedDevices)
        {
            if (!_approvedDevices.Add(deviceName)) return;
            try { AppSettings.Set(ApprovedDevicesSettingKey, System.Text.Json.JsonSerializer.Serialize(_approvedDevices.ToList())); } catch { }
        }
    }

    /// <summary>新设备连接前必须经电脑端弹窗授权；拒绝/超时/无 UI 都视为拒绝。</summary>
    internal static async Task<bool> EnsureDeviceApprovedAsync(string deviceName, CancellationToken ct = default)
    {
        int generation;
        lock (_stateLock)
        {
            generation = _approvalGeneration;
            if (IsDeviceApproved(deviceName)) return true;
        }
        var handler = PairApprovalHandler;
        var approved = false;
        if (handler is not null)
        {
            try { approved = await handler(deviceName).WaitAsync(ct).ConfigureAwait(false); } catch { approved = false; }
        }
        lock (_stateLock)
        {
            if (ct.IsCancellationRequested || generation != _approvalGeneration) return false;
            if (approved) RememberApprovedDevice(deviceName);
            return approved;
        }
    }

    private static void RevokeToken(string token)
    {
        PairedDeviceStore.RevokeByToken(token);
        StateChanged?.Invoke();
    }

    /// <summary>校验请求令牌；成功时返回设备名，供审计日志使用。</summary>
    private static bool TryAuthorize(HttpListenerRequest req, out string deviceName)
    {
        deviceName = "";
        var token = req.Headers["X-Token"];
        if (string.IsNullOrEmpty(token)) return false;
        if (!PairedDeviceStore.TryAuthorize(token, out var record)) return false;
        deviceName = record.Name;
        return true;
    }

    private static string RemoteOf(HttpListenerRequest req) =>
        req.RemoteEndPoint?.Address.ToString() ?? "未知地址";

    private static int _approvalGeneration;

    public static void RevokeAll()
    {
        lock (_stateLock)
        {
            PairedDeviceStore.RevokeAll();
            _approvalGeneration++;
            lock (_approvedDevices)
            {
                _approvedDevices.Clear();
                AppSettings.Remove(ApprovedDevicesSettingKey);
            }
            RotatePairCode();
        }
        LogService.Info(LogService.PhoneLink, "已撤销全部配对授权（配对码同时轮换）");
        StateChanged?.Invoke();
    }

    /// <summary>撤销单台已配对设备：只断开这一台，其他手机不受影响。</summary>
    public static bool RevokeDevice(string deviceId)
    {
        var name = PairedDevices.FirstOrDefault(d => string.Equals(d.Id, deviceId, StringComparison.Ordinal))?.Name ?? deviceId;
        if (!PairedDeviceStore.Revoke(deviceId)) return false;
        LogService.Info(LogService.PhoneLink, $"已断开设备「{name}」");
        StateChanged?.Invoke();
        return true;
    }

    // ───────────────────────── 启停 ─────────────────────────

    private static readonly object _lifecycleLock = new();

    public static void Start()
    {
        lock (_lifecycleLock) StartCore();
    }

    private static void StartCore()
    {
        if (IsRunning) return;
        try { Directory.CreateDirectory(ReceiveDir); } catch { }
        _cts = new CancellationTokenSource();
        var listener = new HttpListener();
        for (var attempt = 0; ; attempt++)
        {
            try
            {
                listener.Prefixes.Clear();
                listener.Prefixes.Add($"http://+:{Port}/");
                listener.Start();
                break;
            }
            catch when (attempt < 9)
            {
                listener.Close();
                listener = new HttpListener();
                Port++;
            }
            catch
            {
                listener.Close();
                _cts.Dispose();
                _cts = null;
                throw;
            }
        }
        _listener = listener;
        IsRunning = true;
        _discovery = PhoneLinkDiscovery.TryStart(Port);
        var ct = _cts.Token;
        _ = Task.Run(() => ListenLoop(listener, ct));
        LogService.Info(LogService.PhoneLink,
            $"服务已启动：http://+:{Port}/（局域网发现 UDP {PhoneLinkDiscovery.DiscoveryPort}）");
        PhoneLinkFirewall.EnsureInBackground(Port);
        StateChanged?.Invoke();
    }

    public static void Stop()
    {
        lock (_lifecycleLock) StopCore();
    }

    private static void StopCore()
    {
        if (!IsRunning) return;
        IsRunning = false;
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;
        _discovery?.Dispose();
        _discovery = null;
        lock (_stateLock) _approvalGeneration++;
        foreach (var job in GetJobs().Where(j => j.IsRunning)) CancelJob(job.Id);
        try { _listener?.Stop(); _listener?.Close(); } catch { }
        _listener = null;
        PairedDeviceStore.RevokeAll();
        LogService.Info(LogService.PhoneLink, "服务已停止，所有已配对设备授权已清空");
        StateChanged?.Invoke();
    }

    // ───────────────────────── 手机任务（exec / winget）─────────────────────────

    public static IReadOnlyList<PhoneJob> GetJobs()
    {
        lock (_jobsLock) return _jobList.ToList();
    }

    public static PhoneJob? GetJob(string id)
    {
        lock (_jobsLock) return _jobs.GetValueOrDefault(id);
    }

    /// <summary>终止运行中的任务（exec 取消 CTS 并杀进程树；winget 杀进程树）。</summary>
    public static bool CancelJob(string id)
    {
        var job = GetJob(id);
        if (job is null || !job.IsRunning) return false;
        job.CancelRequestedByUser = true;
        try { job.Cts?.Cancel(); } catch { }
        try { job.Proc?.Kill(true); } catch { }
        JobsChanged?.Invoke();
        return true;
    }

    private static PhoneJob CreateJob(PhoneJobKind kind, string title)
    {
        var job = new PhoneJob { Id = Guid.NewGuid().ToString("N"), Kind = kind, Title = title };
        lock (_jobsLock)
        {
            _jobs[job.Id] = job;
            _jobList.Add(job);
            TrimJobsLocked();
        }
        JobStarted?.Invoke(job);
        JobsChanged?.Invoke();
        return job;
    }

    internal static void FinishJob(PhoneJob job, PhoneJobStatus status, int exitCode)
    {
        job.Status = status;
        job.ExitCode = exitCode;
        job.FinishedAt = DateTime.Now;
        job.Proc = null;
        JobFinished?.Invoke(job);
        JobsChanged?.Invoke();
    }

    /// <summary>上限 50 条：只丢已结束的最旧任务，运行中的永不丢（锁内调用）。</summary>
    private static void TrimJobsLocked()
    {
        while (_jobList.Count > 50)
        {
            var index = _jobList.FindIndex(j => !j.IsRunning);
            if (index < 0) return;
            _jobs.Remove(_jobList[index].Id);
            _jobList.RemoveAt(index);
        }
    }

    private static async Task ListenLoop(HttpListener listener, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && listener.IsListening)
        {
            HttpListenerContext ctx;
            try { ctx = await listener.GetContextAsync().ConfigureAwait(false); }
            catch { break; }
            _ = Task.Run(() => HandleSafe(ctx, ct), ct);
        }
    }

    private static async Task HandleSafe(HttpListenerContext ctx, CancellationToken ct)
    {
        try { await Handle(ctx, ct).ConfigureAwait(false); }
        catch (Exception ex)
        {
            try { await WriteJson(ctx, 500, new { error = ex.Message }).ConfigureAwait(false); } catch { }
        }
        finally { try { ctx.Response.Close(); } catch { } }
    }

    // ───────────────────────── 路由 ─────────────────────────

    private static async Task Handle(HttpListenerContext ctx, CancellationToken ct)
    {
        var req = ctx.Request;
        var path = req.Url?.AbsolutePath.TrimEnd('/') ?? "";
        var method = req.HttpMethod;

        if (path == "/api/ping")
        {
            await WriteJson(ctx, 200, new { app = "TubaLink", host = Environment.MachineName, apiVersion = ApiVersion });
            return;
        }

        if (path == "/api/pair" && method == "POST")
        {
            var body = await ReadJson(req);
            var deviceName = Str(body, "deviceName");
            var (token, error) = TryPair(Str(body, "code"), deviceName);
            if (token is null)
            {
                await WriteJson(ctx, 403, new { error });
                return;
            }
            // 首次连接需要电脑端弹窗授权（拒绝则立即吊销刚签发的令牌）
            var name = string.IsNullOrWhiteSpace(deviceName) ? "手机" : deviceName.Trim();
            if (!await EnsureDeviceApprovedAsync(name, ct) || !PairedDeviceStore.Contains(token))
            {
                RevokeToken(token);
                LogService.Warn(LogService.PhoneLink, $"设备「{name}」连接被拒绝或授权已失效（{RemoteOf(req)}）");
                await WriteJson(ctx, 403, new { error = "电脑端未允许本次连接，请在电脑上确认后再试" });
                return;
            }
            LogService.Info(LogService.PhoneLink, $"设备「{name}」已配对连接（{RemoteOf(req)}）");
            await WriteJson(ctx, 200, new { token, host = Environment.MachineName });
            return;
        }

        if (!TryAuthorize(req, out var device)) { await WriteJson(ctx, 401, new { error = "未授权，请重新配对" }); return; }

        switch (path)
        {
            case "/api/info":
                await WriteJson(ctx, 200, new
                {
                    host = Environment.MachineName,
                    os = RuntimeInformation.OSDescription,
                    user = Environment.UserName,
                    arch = RuntimeInformation.OSArchitecture.ToString(),
                    apiVersion = ApiVersion
                });
                return;
            case "/api/monitor":
                await WriteJson(ctx, 200, await ReadMonitorAsync(ParseQuery(req).GetValueOrDefault("fps") == "1"));
                return;
            case "/api/hardware":
                {
                    var sections = await HardwareInfoService.LoadAsync();
                    await WriteJson(ctx, 200, new
                    {
                        sections = sections.Select(sec => new
                        {
                            title = sec.Title,
                            items = sec.Items.Select(i => new { label = i.Label, value = i.Value })
                        })
                    });
                    return;
                }
            case "/api/screenshot":
                {
                    var q = ParseQuery(req);
                    int.TryParse(q.GetValueOrDefault("width"), out var w);
                    var jpg = CaptureScreenJpeg(w is >= 320 and <= 3840 ? w : 1280);
                    ctx.Response.ContentType = "image/jpeg";
                    ctx.Response.ContentLength64 = jpg.Length;
                    await ctx.Response.OutputStream.WriteAsync(jpg, ct);
                    return;
                }
            case "/api/exec" when method == "POST":
                {
                    var body = await ReadJson(req);
                    var cmd = Str(body, "command");
                    if (string.IsNullOrWhiteSpace(cmd)) { await WriteJson(ctx, 400, new { error = "命令为空" }); return; }
                    var timeout = Math.Clamp(Int(body, "timeoutSec", 60), 1, 600);
                    var job = CreateJob(PhoneJobKind.Exec, cmd!);
                    LogService.Info(LogService.PhoneLink, $"设备「{device}」执行命令（{RemoteOf(req)}）：{PhoneLinkNotifier.Truncate(cmd!, 200)}");
                    if (Bool(body, "background"))
                    {
                        // 手机切后台/断网也不丢结果：立即返回任务号，客户端轮询 /api/jobs/{id}
                        var serviceToken = _cts?.Token ?? CancellationToken.None;
                        _ = Task.Run(async () =>
                        {
                            var (code, _) = await RunPowerShellAsync(cmd!, timeout, serviceToken, job);
                            LogService.Info(LogService.PhoneLink,
                                $"命令任务 {job.Id} 结束（设备「{device}」，退出码 {code}）");
                        });
                        await WriteJson(ctx, 200, new { jobId = job.Id });
                        return;
                    }
                    var (code, output) = await RunPowerShellAsync(cmd!, timeout, ct, job);
                    LogService.Info(LogService.PhoneLink,
                        $"命令执行完成（设备「{device}」，退出码 {code}）：{PhoneLinkNotifier.Truncate(cmd!, 200)}");
                    await WriteJson(ctx, 200, new { exitCode = code, output });
                    return;
                }
            case "/api/winget/search":
                {
                    var q = ParseQuery(req).GetValueOrDefault("q") ?? "";
                    var r = await WingetStoreService.SearchOnlineAsync(q, ct: ct);
                    await WriteJson(ctx, 200, new
                    {
                        error = r.Error,
                        results = r.Results.Take(50).Select(x => new
                        {
                            id = x.PackageIdentifier,
                            name = x.PackageName,
                            version = x.LatestVersion,
                            publisher = x.Publisher
                        })
                    });
                    return;
                }
            case "/api/winget/install" when method == "POST":
                {
                    var body = await ReadJson(req);
                    var id = Str(body, "id");
                    if (!IsValidWingetId(id)) { await WriteJson(ctx, 400, new { error = "软件包 ID 不合法" }); return; }
                    LogService.Info(LogService.PhoneLink, $"设备「{device}」请求安装 {id}");
                    await WriteJson(ctx, 200, new { jobId = StartWingetInstall(id!, Str(body, "name"), device) });
                    return;
                }
            case "/api/winget/job":
                {
                    var jid = ParseQuery(req).GetValueOrDefault("id") ?? "";
                    var job = GetJob(jid);
                    if (job is null) { await WriteJson(ctx, 404, new { error = "任务不存在" }); return; }
                    await WriteJson(ctx, 200, new { done = !job.IsRunning, exitCode = job.ExitCode, output = job.Output });
                    return;
                }
            case "/api/jobs" when method == "GET":
                {
                    await WriteJson(ctx, 200, new { jobs = GetJobs().Select(JobPayload) });
                    return;
                }
            case "/api/chat" when method == "GET":
                {
                    var q = ParseQuery(req);
                    long.TryParse(q.GetValueOrDefault("after"), out var after);
                    long.TryParse(q.GetValueOrDefault("before"), out var before);
                    int.TryParse(q.GetValueOrDefault("limit"), out var limit);
                    var list = PhoneLinkChatStore.Page(PhoneLinkChatStore.Snapshot(), after, before, limit);
                    await WriteJson(ctx, 200, new { messages = list });
                    return;
                }
            case "/api/chat/text" when method == "POST":
                {
                    var text = Str(await ReadJson(req), "text");
                    if (string.IsNullOrWhiteSpace(text)) { await WriteJson(ctx, 400, new { error = "消息为空" }); return; }
                    var m = AddMessage("phone", "text", text!, "", 0, null);
                    await WriteJson(ctx, 200, m);
                    return;
                }
            case "/api/chat/upload" when method == "POST":
                {
                    var q = ParseQuery(req);
                    var name = SanitizeFileName(q.GetValueOrDefault("name"));
                    var isImage = q.GetValueOrDefault("kind") == "image";
                    if (req.ContentLength64 > MaxUploadBytes) { await WriteJson(ctx, 413, new { error = "文件过大" }); return; }
                    Directory.CreateDirectory(ReceiveDir);
                    var dest = UniquePath(ReceiveDir, name);
                    var reporter = new TransferSpeedReporter(Guid.NewGuid().ToString("N"), Path.GetFileName(dest),
                        PhoneTransferDirection.Upload, req.ContentLength64, p => TransferProgress?.Invoke(p));
                    long size;
                    long received = 0;
                    var tooLarge = false;
                    try
                    {
                        // 1 MB 缓冲 + 异步顺序写：接收大文件时吞吐贴近链路理论速度
                        await using (var fs = new FileStream(dest, FileMode.Create, FileAccess.Write, FileShare.None,
                                     IoBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan))
                        {
                            var buffer = new byte[IoBufferSize];
                            int read;
                            while ((read = await req.InputStream.ReadAsync(buffer, ct)) > 0)
                            {
                                received += read;
                                // chunked 传输没有 Content-Length，按实际收到的字节数兜底
                                if (received > MaxUploadBytes) { tooLarge = true; break; }
                                await fs.WriteAsync(buffer.AsMemory(0, read), ct);
                                reporter.Report(received);
                            }
                            size = fs.Length;
                        }
                        if (tooLarge) reporter.Fail(received);
                        else reporter.Complete(received);
                    }
                    catch
                    {
                        reporter.Fail(received);
                        throw;
                    }
                    if (tooLarge)
                    {
                        try { File.Delete(dest); } catch { }
                        LogService.Warn(LogService.PhoneLink,
                            $"设备「{device}」上传被拒：文件超过 {MaxUploadBytes / (1024 * 1024 * 1024)} GB 上限");
                        await WriteJson(ctx, 413, new { error = "文件过大" });
                        return;
                    }
                    LogService.Info(LogService.PhoneLink,
                        $"设备「{device}」上传文件：{Path.GetFileName(dest)}（{size} 字节）");
                    var m = AddMessage("phone", isImage ? "image" : "file", "", Path.GetFileName(dest), size, dest);
                    await WriteJson(ctx, 200, m);
                    return;
                }
        }

        if (path.StartsWith("/api/jobs/", StringComparison.Ordinal) && method == "GET")
        {
            var jobId = path["/api/jobs/".Length..];
            var job = GetJob(jobId);
            if (job is null) { await WriteJson(ctx, 404, new { error = "任务不存在" }); return; }
            await WriteJson(ctx, 200, JobPayload(job));
            return;
        }

        if (path.StartsWith("/api/chat/file/", StringComparison.Ordinal) && method == "GET")
        {
            var id = path["/api/chat/file/".Length..];
            var file = PhoneLinkChatStore.GetFilePath(id);
            if (file is null || !File.Exists(file)) { await WriteJson(ctx, 404, new { error = "文件不存在" }); return; }
            var fi = new FileInfo(file);
            var range = ParseRange(req.Headers["Range"], fi.Length, out var rangeStart, out var rangeEnd);
            if (range == RangeParseResult.Unsatisfiable)
            {
                ctx.Response.StatusCode = 416;
                ctx.Response.AddHeader("Content-Range", $"bytes */{fi.Length}");
                ctx.Response.ContentLength64 = 0;
                return;
            }
            var offset = range == RangeParseResult.Valid ? rangeStart : 0;
            var length = range == RangeParseResult.Valid ? rangeEnd - rangeStart + 1 : fi.Length;
            ctx.Response.ContentType = "application/octet-stream";
            ctx.Response.ContentLength64 = length;
            ctx.Response.AddHeader("Accept-Ranges", "bytes");
            if (range == RangeParseResult.Valid)
                ctx.Response.AddHeader("Content-Range", $"bytes {rangeStart}-{rangeEnd}/{fi.Length}");
            ctx.Response.AddHeader("Content-Disposition", "attachment; filename*=UTF-8''" + Uri.EscapeDataString(fi.Name));
            var downloadReporter = new TransferSpeedReporter(id, fi.Name, PhoneTransferDirection.Download,
                length, p => TransferProgress?.Invoke(p));
            long sent = 0;
            try
            {
                // 1 MB 缓冲 + 异步顺序读：发送大文件时吞吐贴近链路理论速度
                await using var fs = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read,
                    IoBufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);
                if (offset > 0) fs.Seek(offset, SeekOrigin.Begin);
                var buffer = new byte[IoBufferSize];
                while (sent < length)
                {
                    var want = (int)Math.Min(buffer.Length, length - sent);
                    var read = await fs.ReadAsync(buffer.AsMemory(0, want), ct);
                    if (read <= 0) break;
                    await ctx.Response.OutputStream.WriteAsync(buffer.AsMemory(0, read), ct);
                    sent += read;
                    downloadReporter.Report(sent);
                }
                downloadReporter.Complete(sent);
            }
            catch
            {
                downloadReporter.Fail(sent);
                throw;
            }
            return;
        }

        await WriteJson(ctx, 404, new { error = "未找到" });
    }

    // ───────────────────────── 聊天 ─────────────────────────

    public static PhoneChatMessage AddPcText(string text) => AddMessage("pc", "text", text, "", 0, null);

    public static PhoneChatMessage AddPcFile(string path)
    {
        var fi = new FileInfo(path);
        var ext = fi.Extension.ToLowerInvariant();
        var isImage = ext is ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp";
        return AddMessage("pc", isImage ? "image" : "file", "", fi.Name, fi.Length, fi.FullName);
    }

    private static PhoneChatMessage AddMessage(string from, string type, string text, string fileName, long size, string? filePath)
    {
        var m = PhoneLinkChatStore.Add(from, type, text, fileName, size, filePath);
        MessageAdded?.Invoke(m);
        return m;
    }

    internal static string SanitizeFileName(string? name)
    {
        var n = Path.GetFileName((name ?? "").Replace('\\', '/'));
        foreach (var c in Path.GetInvalidFileNameChars()) n = n.Replace(c, '_');
        n = n.Trim().TrimEnd('.');
        return string.IsNullOrEmpty(n) ? "file" : n.Length > 120 ? n[..120] : n;
    }

    internal static string UniquePath(string dir, string name)
    {
        var p = Path.Combine(dir, name);
        if (!File.Exists(p)) return p;
        var stem = Path.GetFileNameWithoutExtension(name);
        var ext = Path.GetExtension(name);
        for (var i = 1; ; i++)
        {
            p = Path.Combine(dir, $"{stem} ({i}){ext}");
            if (!File.Exists(p)) return p;
        }
    }

    // ───────────────────────── 监控 / 截图 ─────────────────────────

    private static async Task<MonitorSample> ReadMonitorAsync(bool withFps)
    {
        await _monitorLock.WaitAsync();
        try
        {
            return await Task.Run(() =>
            {
                LiteMonitorService.Instance.EnsureInit();
                return LiteMonitorService.Instance.Read(withFps);
            });
        }
        finally { _monitorLock.Release(); }
    }

    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);

    /// <summary>截图缓存时长：多台手机/连点刷新共用同一帧，避免重复截屏编码。</summary>
    private const int ScreenCacheMs = 150;
    private static readonly object _screenLock = new();
    private static (int Width, long Tick, byte[] Bytes) _screenCache;

    public static byte[] CaptureScreenJpeg(int maxWidth)
    {
        lock (_screenLock)
        {
            var now = Environment.TickCount64;
            if (_screenCache.Bytes is not null && _screenCache.Width == maxWidth &&
                now - _screenCache.Tick < ScreenCacheMs)
            {
                return _screenCache.Bytes;
            }
            var bytes = CaptureScreenJpegCore(maxWidth);
            _screenCache = (maxWidth, now, bytes);
            return bytes;
        }
    }

    private static byte[] CaptureScreenJpegCore(int maxWidth)
    {
        int w = GetSystemMetrics(0), h = GetSystemMetrics(1);
        if (w <= 0 || h <= 0) { w = 1920; h = 1080; }
        using var full = new Bitmap(w, h, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(full))
            g.CopyFromScreen(0, 0, 0, 0, new Size(w, h));

        Bitmap output = full;
        Bitmap? scaled = null;
        if (w > maxWidth)
        {
            var nh = (int)Math.Round(h * (maxWidth / (double)w));
            scaled = new Bitmap(maxWidth, nh, PixelFormat.Format24bppRgb);
            using var g = Graphics.FromImage(scaled);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(full, 0, 0, maxWidth, nh);
            output = scaled;
        }
        try
        {
            var codec = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
            using var ep = new EncoderParameters(1);
            ep.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 70L);
            using var ms = new MemoryStream();
            output.Save(ms, codec, ep);
            return ms.ToArray();
        }
        finally { scaled?.Dispose(); }
    }

    // ───────────────────────── PowerShell / winget ─────────────────────────

    internal static ProcessStartInfo BuildPowerShellStartInfo(string command)
    {
        // 统一 UTF-8 输出；使用 EncodedCommand 避免任何引号/转义问题
        var script = "[Console]::OutputEncoding=[System.Text.Encoding]::UTF8;$ProgressPreference='SilentlyContinue';" + command;
        var psi = new ProcessStartInfo
        {
            FileName = "powershell.exe",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        psi.ArgumentList.Add("-NoProfile");
        psi.ArgumentList.Add("-NonInteractive");
        psi.ArgumentList.Add("-ExecutionPolicy");
        psi.ArgumentList.Add("Bypass");
        psi.ArgumentList.Add("-EncodedCommand");
        psi.ArgumentList.Add(Convert.ToBase64String(Encoding.Unicode.GetBytes(script)));
        return psi;
    }

    internal static async Task<(int ExitCode, string Output)> RunPowerShellAsync(string command, int timeoutSec, CancellationToken ct, PhoneJob job)
    {
        using var p = Process.Start(BuildPowerShellStartInfo(command));
        if (p is null)
        {
            job.AppendLine("无法启动 powershell");
            FinishJob(job, PhoneJobStatus.Failed, -1);
            return (-1, job.Output);
        }
        job.Proc = p;
        p.OutputDataReceived += (_, e) => job.AppendLine(e.Data);
        p.ErrorDataReceived += (_, e) => job.AppendLine(e.Data);
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        job.Cts = cts;
        cts.CancelAfter(TimeSpan.FromSeconds(timeoutSec));
        try
        {
            await p.WaitForExitAsync(cts.Token);
            p.WaitForExit();
        }
        catch (OperationCanceledException)
        {
            try { p.Kill(true); } catch { }
            var reason = job.CancelRequestedByUser ? "[已由电脑端终止]"
                : ct.IsCancellationRequested ? "[已停止服务]"
                : $"[超时 {timeoutSec} 秒，已终止]";
            job.AppendLine(reason);
            FinishJob(job, job.CancelRequestedByUser || ct.IsCancellationRequested ? PhoneJobStatus.Cancelled : PhoneJobStatus.Failed, -1);
            return (-1, job.Output);
        }
        catch (Exception ex)
        {
            job.AppendLine("执行失败：" + ex.Message);
            FinishJob(job, PhoneJobStatus.Failed, -1);
            return (-1, job.Output);
        }
        finally
        {
            job.Cts = null;
        }
        var exitCode = p.ExitCode;
        if (job.CancelRequestedByUser) job.AppendLine("[已由电脑端终止]");
        FinishJob(job, job.CancelRequestedByUser ? PhoneJobStatus.Cancelled
            : exitCode == 0 ? PhoneJobStatus.Done : PhoneJobStatus.Failed, exitCode);
        return (exitCode, job.Output);
    }

    private static readonly Regex WingetIdRegex = new(@"^[A-Za-z0-9][A-Za-z0-9._+\-]{0,127}$", RegexOptions.Compiled);

    internal static bool IsValidWingetId(string? id) => !string.IsNullOrEmpty(id) && WingetIdRegex.IsMatch(id);

    private static string StartWingetInstall(string id, string? displayName, string device = "")
    {
        var job = CreateJob(PhoneJobKind.Winget, string.IsNullOrWhiteSpace(displayName) ? id : displayName.Trim());
        _ = Task.Run(async () =>
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "winget",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                foreach (var a in new[] { "install", "--id", id, "-e", "--source", "winget", "--silent",
                             "--accept-package-agreements", "--accept-source-agreements" })
                    psi.ArgumentList.Add(a);
                using var p = Process.Start(psi);
                if (p is null)
                {
                    job.AppendLine("无法启动 winget");
                    FinishJob(job, PhoneJobStatus.Failed, -1);
                    LogService.Error(LogService.PhoneLink, $"winget 安装失败：无法启动进程（{id}）");
                    return;
                }
                job.Proc = p;
                p.OutputDataReceived += (_, e) => job.AppendLine(e.Data);
                p.ErrorDataReceived += (_, e) => job.AppendLine(e.Data);
                p.BeginOutputReadLine();
                p.BeginErrorReadLine();
                await p.WaitForExitAsync();
                p.WaitForExit();
                var status = p.ExitCode == 0 ? PhoneJobStatus.Done
                    : job.CancelRequestedByUser ? PhoneJobStatus.Cancelled
                    : PhoneJobStatus.Failed;
                if (status == PhoneJobStatus.Cancelled) job.AppendLine("[已由电脑端终止]");
                FinishJob(job, status, p.ExitCode);
                LogService.Info(LogService.PhoneLink,
                    $"winget 安装结束（设备「{device}」，{id}，退出码 {p.ExitCode}）");
            }
            catch (Exception ex)
            {
                job.AppendLine("安装失败：" + ex.Message);
                FinishJob(job, PhoneJobStatus.Failed, -1);
                LogService.Error(LogService.PhoneLink, $"winget 安装异常（{id}）：{ex.Message}");
            }
        });
        return job.Id;
    }

    // ───────────────────────── HTTP 辅助 ─────────────────────────

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, IncludeFields = true };

    private static object JobPayload(PhoneJob job) => new
    {
        id = job.Id,
        kind = job.Kind.ToString().ToLowerInvariant(),
        title = job.Title,
        running = job.IsRunning,
        status = job.Status.ToString().ToLowerInvariant(),
        exitCode = job.ExitCode,
        output = job.Output,
        startedAt = job.StartedAt,
        finishedAt = job.FinishedAt
    };

    /// <summary>单区间 Range 解析：支持 <c>bytes=start-</c>、<c>bytes=start-end</c>、<c>bytes=-suffix</c>。</summary>
    internal enum RangeParseResult
    {
        /// <summary>没有 Range 头或格式不认识：按完整文件响应（RFC 允许忽略）。</summary>
        None,
        Valid,
        /// <summary>区间越界：应返回 416。</summary>
        Unsatisfiable
    }

    internal static RangeParseResult ParseRange(string? header, long length, out long start, out long end)
    {
        start = 0;
        end = length - 1;
        if (string.IsNullOrWhiteSpace(header) || length <= 0) return RangeParseResult.None;

        var value = header.Trim();
        if (!value.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase)) return RangeParseResult.None;
        value = value["bytes=".Length..];
        if (value.Contains(',')) return RangeParseResult.None; // 多区间不支持，退回完整响应

        var dash = value.IndexOf('-');
        if (dash < 0) return RangeParseResult.None;

        var startText = value[..dash].Trim();
        var endText = value[(dash + 1)..].Trim();

        if (startText.Length == 0)
        {
            // bytes=-N：最后 N 个字节
            if (!long.TryParse(endText, out var suffix) || suffix <= 0) return RangeParseResult.None;
            if (suffix >= length) { start = 0; end = length - 1; return RangeParseResult.Valid; }
            start = length - suffix;
            end = length - 1;
            return RangeParseResult.Valid;
        }

        if (!long.TryParse(startText, out start) || start < 0)
        {
            start = 0;
            end = length - 1;
            return RangeParseResult.None;
        }
        if (start >= length) return RangeParseResult.Unsatisfiable;

        if (endText.Length == 0)
        {
            end = length - 1;
            return RangeParseResult.Valid;
        }
        if (!long.TryParse(endText, out end) || end < start)
        {
            start = 0;
            end = length - 1;
            return RangeParseResult.None;
        }
        if (end >= length) end = length - 1;
        return RangeParseResult.Valid;
    }

    private static async Task WriteJson(HttpListenerContext ctx, int status, object obj)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(obj, JsonOpts);
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        ctx.Response.ContentLength64 = bytes.Length;
        await ctx.Response.OutputStream.WriteAsync(bytes);
    }

    private static async Task<JsonElement> ReadJson(HttpListenerRequest req)
    {
        using var sr = new StreamReader(req.InputStream, Encoding.UTF8);
        var s = await sr.ReadToEndAsync();
        if (string.IsNullOrWhiteSpace(s)) return default;
        try { return JsonDocument.Parse(s).RootElement.Clone(); }
        catch { return default; }
    }

    private static string? Str(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int Int(JsonElement e, string name, int def) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) && v.TryGetInt32(out var i) ? i : def;

    private static bool Bool(JsonElement e, string name) =>
        e.ValueKind == JsonValueKind.Object && e.TryGetProperty(name, out var v) &&
        v.ValueKind is JsonValueKind.True or JsonValueKind.False && v.GetBoolean();

    private static Dictionary<string, string> ParseQuery(HttpListenerRequest req)
    {
        var d = new Dictionary<string, string>();
        var q = req.Url?.Query ?? "";
        if (q.StartsWith('?')) q = q[1..];
        foreach (var part in q.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var i = part.IndexOf('=');
            var k = Uri.UnescapeDataString((i < 0 ? part : part[..i]).Replace('+', ' '));
            var v = i < 0 ? "" : Uri.UnescapeDataString(part[(i + 1)..].Replace('+', ' '));
            d[k] = v;
        }
        return d;
    }
}

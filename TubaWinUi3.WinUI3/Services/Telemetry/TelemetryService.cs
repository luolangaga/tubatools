using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.Win32;
using TubaWinUi3.Services.Agent;
using AiStackFrame = Microsoft.ApplicationInsights.DataContracts.StackFrame;

namespace TubaWinUi3.Services.Telemetry;

/// <summary>
/// 匿名遥测：在线用户统计（心跳）与错误日志收集，直接上报 Axiom（不经过 Cloudflare Worker）。
/// 设计约束：
/// <list type="bullet">
/// <item>只收集匿名运行信息（版本/架构/系统版本/语言）与异常，不收集用户名、文件路径等可识别信息；
/// 设备标识为「机器 GUID + 固定盐」的 SHA-256 截断值，不可反查，可随时在设置中关闭。</item>
/// <item>上传全程静默：不弹窗、不阻塞界面、失败落盘续传，绝不影响主流程。</item>
/// <item>采集连接优先 IPv4，地址容错由 <see cref="TelemetryEndpointResolver"/> 兜底。</item>
/// </list>
/// </summary>
public static class TelemetryService
{
    /// <summary>设置页开关使用的键；默认开启，用户可随时关闭（关闭后停止采集并清空待传数据）。</summary>
    public const string SettingKey = "TelemetryEnabled";

    private const string DeviceIdSettingKey = "TelemetryDeviceId";

    // 保留 SDK 的本地异常构造/序列化能力；SDK 默认通道不会被使用。
    private const string SerializerConnectionString =
        "InstrumentationKey=ff9e238f-9642-4aa9-a217-734434e79872";

    /// <summary>心跳间隔：在线用户 = 最近 15 分钟内有心跳/启动事件的设备数。</summary>
    private static readonly TimeSpan HeartbeatInterval = TimeSpan.FromMinutes(10);

    private const int MaxReportsPerFingerprint = 3;
    private const int MaxExceptionReportsPerSession = 30;
    private const int MaxTextLength = 4000;

    /// <summary>异常链最大深度（AggregateException 展开后可能非常多）。</summary>
    private const int MaxExceptionChain = 8;

    /// <summary>每条异常最多收录的调用栈帧数。</summary>
    private const int MaxStackFrames = 32;

    private static readonly Regex UserProfileRegex = new(
        @"([A-Za-z]:\\Users\\)([^\\\r\n""']+)|(/home/|/Users/)([^/\s""']+)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly object Gate = new();
    private static readonly ConcurrentDictionary<string, int> ExceptionFingerprints = new(StringComparer.Ordinal);

    private static TelemetryClient? _client;
    private static TelemetryChannel? _channel;
    private static TelemetryConfiguration? _configuration;
    private static Timer? _heartbeatTimer;
    private static string? _deviceId;
    private static string _sessionId = Guid.NewGuid().ToString("N");
    private static int _exceptionReportCount;

    /// <summary>遥测是否启用（读取设置为准，未设置时默认开启）。</summary>
    public static bool IsEnabled => AppSettings.GetBool(SettingKey, true);

    /// <summary>启动时调用：按设置决定是否开始采集（幂等，重复调用无副作用）。</summary>
    public static void Initialize()
    {
        if (!IsEnabled) return;
        Start();
    }

    /// <summary>设置页开关调用：持久化并立即生效。</summary>
    public static void SetEnabled(bool enabled)
    {
        AppSettings.Set(SettingKey, enabled);
        if (enabled) Start();
        else Stop(discardPending: true);
    }

    /// <summary>记录自定义事件（失败静默）。</summary>
    public static void TrackEvent(string name, IReadOnlyDictionary<string, string>? properties = null)
    {
        var client = _client;
        if (client is null) return;

        try
        {
            var telemetry = new EventTelemetry(name);
            if (properties is not null)
            {
                foreach (var (key, value) in properties)
                {
                    if (!string.IsNullOrEmpty(key))
                        telemetry.Properties[key] = Sanitize(value, 512);
                }
            }
            client.TrackEvent(telemetry);
        }
        catch (Exception ex)
        {
            AgentDebugLog.Error("[Telemetry] TrackEvent 失败", ex);
        }
    }

    /// <summary>记录工具启动（工具名 = 目录名或内置工具 id，不含路径）。</summary>
    public static void TrackToolLaunch(string toolId, bool builtin)
    {
        if (string.IsNullOrWhiteSpace(toolId)) return;
        TrackEvent("tool_launch", new Dictionary<string, string>
        {
            ["tool"] = toolId,
            ["builtin"] = builtin ? "1" : "0",
        });
    }

    /// <summary>启动器只传入本地路径；上报前提取目录名/文件名，不上传路径。</summary>
    internal static void TrackExternalToolLaunch(string executablePath)
    {
        if (_client is null) return;
        try { TrackToolLaunch(BuildExternalToolId(executablePath, ToolCatalog.ToolsRoot), builtin: false); }
        catch { } // 遥测不能影响工具启动
    }

    internal static string BuildExternalToolId(string executablePath, string toolsRoot)
    {
        if (string.IsNullOrWhiteSpace(executablePath)) return string.Empty;
        var relative = Path.GetRelativePath(toolsRoot, executablePath);
        if (!Path.IsPathRooted(relative) && relative != ".."
            && !relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
        {
            var parts = relative.Split([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar],
                StringSplitOptions.RemoveEmptyEntries);
            // Tools / 分类 / 工具目录 / 架构 / 程序，合并架构变体与程序名。
            if (parts.Length >= 3) return parts[1];
        }
        return Path.GetFileNameWithoutExtension(executablePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
    }

    /// <summary>
    /// 记录异常。同一指纹（类型 + 消息）每次会话最多 3 条、整场最多 30 条，
    /// 防止某个高频异常把配额打满、淹没其他问题。
    /// </summary>
    public static void TrackException(Exception? ex, string source, bool fatal = false)
    {
        var client = _client;
        if (ex is null || client is null) return;

        try
        {
            var fingerprint = BuildFingerprint(ex);
            var seen = ExceptionFingerprints.AddOrUpdate(fingerprint, 1, (_, count) => count + 1);
            if (seen > MaxReportsPerFingerprint) return;
            if (Interlocked.Increment(ref _exceptionReportCount) > MaxExceptionReportsPerSession) return;

            var properties = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["source"] = source,
                ["fatal"] = fatal ? "1" : "0",
                ["session_occurrence"] = seen.ToString(CultureInfo.InvariantCulture),
            };

            client.TrackException(BuildSanitizedTelemetry(ex, properties));
        }
        catch (Exception inner)
        {
            AgentDebugLog.Error("[Telemetry] TrackException 失败", inner);
        }
    }

    /// <summary>
    /// 构造脱敏后的异常遥测（纯逻辑，可单测）：自行拼装异常链与调用栈，
    /// 每条消息、堆栈文本与栈帧文件路径都先过 <see cref="Sanitize"/>，
    /// 因此上报内容里不会出现用户名与用户目录（含内部异常与解析后的栈帧）。
    /// 类型名保持原样，后台仍可按异常类型分组统计。
    /// </summary>
    internal static ExceptionTelemetry BuildSanitizedTelemetry(Exception ex, IDictionary<string, string> properties)
    {
        var chain = FlattenExceptionChain(ex, MaxExceptionChain);
        var details = new List<ExceptionDetailsInfo>(chain.Count);

        for (var i = 0; i < chain.Count; i++)
        {
            var current = chain[i];
            details.Add(new ExceptionDetailsInfo(
                id: i + 1,
                outerId: i == 0 ? 0 : i,
                typeName: current.GetType().FullName ?? current.GetType().Name,
                message: Sanitize(current.Message),
                hasFullStack: false,
                stack: Sanitize(current.StackTrace),
                parsedStack: BuildParsedStack(current)));
        }

        return new ExceptionTelemetry(
            details,
            severityLevel: null,
            problemId: null,
            properties,
            new Dictionary<string, double>());
    }

    /// <summary>按「本体 → 内部异常」展开异常链；AggregateException 按其子异常逐个展开。</summary>
    internal static List<Exception> FlattenExceptionChain(Exception root, int max)
    {
        var result = new List<Exception>(max);
        var queue = new Queue<Exception>();
        queue.Enqueue(root);

        while (queue.Count > 0 && result.Count < max)
        {
            var current = queue.Dequeue();
            result.Add(current);

            if (current is AggregateException aggregate)
            {
                foreach (var inner in aggregate.InnerExceptions)
                    queue.Enqueue(inner);
            }
            else if (current.InnerException is not null)
            {
                queue.Enqueue(current.InnerException);
            }
        }

        return result;
    }

    /// <summary>构造解析后的调用栈（文件路径脱敏）；读取失败时返回空栈而不是抛出。</summary>
    private static IReadOnlyList<AiStackFrame> BuildParsedStack(Exception ex)
    {
        try
        {
            var trace = new System.Diagnostics.StackTrace(ex, true);
            var count = Math.Min(trace.FrameCount, MaxStackFrames);
            var frames = new List<AiStackFrame>(count);

            for (var i = 0; i < count; i++)
            {
                var frame = trace.GetFrame(i);
                var method = frame?.GetMethod();
                frames.Add(new AiStackFrame(
                    assembly: method?.DeclaringType?.Assembly?.FullName,
                    fileName: Sanitize(frame?.GetFileName()),
                    level: i,
                    line: frame?.GetFileLineNumber() ?? 0,
                    method: method?.ToString()));
            }

            return frames;
        }
        catch
        {
            return [];
        }
    }

    /// <summary>尽力把队列中的数据发出去（有界等待），供退出前调用。</summary>
    public static void Flush()
    {
        try { _channel?.Flush(); } catch { }
    }

    /// <summary>停止采集并释放资源（关闭开关、退出时调用）。</summary>
    public static void Shutdown()
    {
        try { Flush(); } catch { }
        Stop(discardPending: false);
    }

    /// <summary>异常指纹：类型 + 消息前 200 字符（用于会话内限流）。</summary>
    internal static string BuildFingerprint(Exception ex)
    {
        var message = ex.Message ?? string.Empty;
        if (message.Length > 200) message = message[..200];
        return ex.GetType().FullName + "|" + message;
    }

    /// <summary>
    /// 文本脱敏（纯函数，可单测）：抹掉 Windows/类 Unix 用户目录中的用户名，并截断超长文本。
    /// 采集侧其他字段均不含路径，这里主要覆盖异常消息与堆栈。
    /// </summary>
    internal static string Sanitize(string? text, int maxLength = MaxTextLength)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;

        var result = UserProfileRegex.Replace(text, match =>
            match.Groups[1].Success ? match.Groups[1].Value + "<用户>"
            : match.Groups[3].Value + "<用户>");

        if (result.Length > maxLength)
            result = result[..maxLength] + "…";
        return result;
    }

    /// <summary>
    /// 匿名设备标识（纯函数，可单测）：固定盐 + SHA-256 后取 Base64Url 前 22 位。
    /// 单向不可反查，同一台机器跨启动稳定，用于统计活跃设备数。
    /// </summary>
    internal static string BuildDeviceId(string seed)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes("TubaWinUi3.Telemetry.v1|" + seed));
        return Convert.ToBase64String(hash)[..22].Replace('+', '-').Replace('/', '_');
    }

    /// <summary>设备种子：优先机器 GUID，取不到时退回机器名 + 用户名。</summary>
    internal static string GetDeviceSeed()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            if (key?.GetValue("MachineGuid") is string machineGuid && !string.IsNullOrWhiteSpace(machineGuid))
                return machineGuid;
        }
        catch { }

        return $"{Environment.MachineName}|{Environment.UserName}";
    }

    /// <summary>取（或首次生成并持久化）匿名设备标识。</summary>
    public static string GetOrCreateDeviceId()
    {
        if (_deviceId is not null) return _deviceId;

        lock (Gate)
        {
            if (_deviceId is not null) return _deviceId;

            var stored = AppSettings.Get(DeviceIdSettingKey);
            if (!string.IsNullOrWhiteSpace(stored))
            {
                _deviceId = stored;
                return stored;
            }

            _deviceId = BuildDeviceId(GetDeviceSeed());
            AppSettings.Set(DeviceIdSettingKey, _deviceId);
            return _deviceId;
        }
    }

    private static void Start()
    {
        lock (Gate)
        {
            if (_client is not null) return;

            try
            {
                if (string.IsNullOrWhiteSpace(AxiomTelemetryConfig.IngestToken))
                    throw new InvalidOperationException("未配置 Axiom 只写凭据，遥测未启动");
                var trackUri = new Uri(AxiomTelemetryConfig.Endpoint);
                var resolver = new TelemetryEndpointResolver(trackUri.Host, "", []);
                var transport = new TelemetryTransport(resolver, trackUri, ProxyService.GetWebProxy(),
                    AxiomTelemetryConfig.IngestToken);
                var channel = new TelemetryChannel(trackUri, transport, Path.Combine(ConfigManager.GetDataDir(), "Telemetry", "spool"));

                var configuration = new TelemetryConfiguration
                {
                    ConnectionString = SerializerConnectionString,
                };
                configuration.TelemetryChannel = channel;

                var client = new TelemetryClient(configuration);
                ApplyContext(client);

                _channel = channel;
                _configuration = configuration;
                _client = client;

                TrackEvent("app_start", BuildStartupProperties());
                _heartbeatTimer = new Timer(_ => TrackEvent("heartbeat"), null, HeartbeatInterval, HeartbeatInterval);
            }
            catch (Exception ex)
            {
                AgentDebugLog.Error("[Telemetry] 初始化失败（已停用遥测）", ex);
                _client = null;
                _channel = null;
                _configuration = null;
            }
        }
    }

    private static void Stop(bool discardPending)
    {
        TelemetryChannel? channel;
        TelemetryConfiguration? configuration;
        Timer? timer;

        lock (Gate)
        {
            channel = _channel;
            configuration = _configuration;
            timer = _heartbeatTimer;
            _channel = null;
            _configuration = null;
            _client = null;
            _heartbeatTimer = null;
        }

        try { timer?.Dispose(); } catch { }
        try { channel?.Stop(discardPending); } catch { }
        try { configuration?.Dispose(); } catch { }

        if (!discardPending) return; // 正常退出保留离线批次，下次启动继续上传

        // 用户关闭采集时才清空本地待传数据
        try
        {
            var spool = Path.Combine(ConfigManager.GetDataDir(), "Telemetry", "spool");
            if (Directory.Exists(spool))
            {
                foreach (var file in Directory.GetFiles(spool, "*.bin"))
                {
                    try { File.Delete(file); } catch { }
                }
            }
        }
        catch { }
    }

    private static void ApplyContext(TelemetryClient client)
    {
        try
        {
            var context = client.Context;
            context.Component.Version = GetAppVersion();
            context.Device.Type = "Desktop";
            context.Device.OperatingSystem = Environment.OSVersion.VersionString;
            context.User.Id = GetOrCreateDeviceId();
            context.Session.Id = _sessionId;

            context.GlobalProperties["arch"] = RuntimeInformation.ProcessArchitecture.ToString();
            context.GlobalProperties["msix"] = RuntimeHelper.IsMsixPackaged ? "1" : "0";
            context.GlobalProperties["lite"] = App.IsLiteMode ? "1" : "0";
            context.GlobalProperties["lang"] = CultureInfo.CurrentUICulture.Name;
            context.GlobalProperties["osBuild"] = Environment.OSVersion.Version.Build.ToString(CultureInfo.InvariantCulture);
        }
        catch (Exception ex)
        {
            AgentDebugLog.Error("[Telemetry] 上下文设置失败", ex);
        }
    }

    private static Dictionary<string, string> BuildStartupProperties() => new()
    {
        ["version"] = GetAppVersion(),
        ["arch"] = RuntimeInformation.ProcessArchitecture.ToString(),
        ["msix"] = RuntimeHelper.IsMsixPackaged ? "1" : "0",
        ["lite"] = App.IsLiteMode ? "1" : "0",
        ["os"] = Environment.OSVersion.Version.ToString(),
        ["lang"] = CultureInfo.CurrentUICulture.Name,
        ["admin"] = IsRunningAsAdmin() ? "1" : "0",
        ["session"] = _sessionId,
    };

    private static bool IsRunningAsAdmin()
    {
        try
        {
            using var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
            return new System.Security.Principal.WindowsPrincipal(identity)
                .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
        }
        catch { return false; }
    }

    private static string GetAppVersion()
    {
        try
        {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            return version is not null ? $"{version.Major}.{version.Minor}.{version.Build}" : "0.0.0";
        }
        catch
        {
            return "0.0.0";
        }
    }

}

using System.Diagnostics;
using System.Text.Json;

namespace TubaWinUi3.Services.AiPlayground;

/// <summary>
/// EP/模型兼容性探测结果（进程级缓存 + 落盘，避免每次重复起子进程）。
/// </summary>
public sealed class AiEpProbeResult
{
    public bool Ok { get; set; }
    public string? Error { get; set; }
    public DateTime CheckedAt { get; set; }
}

/// <summary>
/// 用「隔离子进程」探测某个模型能否在指定 EP 设备上编译出会话。
/// <para>
/// 必要性：Intel OpenVINO 的 NPU 原生 VPUX 编译器对**不支持的模型**（如动态形状未定死的
/// DETR）会直接 <c>abort()</c>（<c>0xC0000409</c> fail-fast，托管 try/catch 拦不住）或**无限等待**，
/// 在主进程里尝试会直接杀掉/卡死整个应用。因此先让子进程（<c>--ai-ep-probe</c>）试跑一次：
/// 崩了/超时/失败都只影响子进程，主进程据此回退 GPU/CPU。
/// </para>
/// <para>
/// 结果按 (模型路径, EP, 设备类型, 形状) 缓存到 &lt;DataDir&gt;/AiPlayground/ep-probe.json，
/// 同一模型只探测一次。
/// </para>
/// </summary>
public static class AiEpProbe
{
    public const string ProbeArg = "--ai-ep-probe";

    private static readonly object Gate = new();
    private static readonly JsonSerializerOptions JsonOpts = new() { WriteIndented = true };
    private static Dictionary<string, AiEpProbeResult>? _cache;

    internal static string? RootOverride;

    private static string CachePath => Path.Combine(
        RootOverride ?? Path.Combine(ConfigManager.GetDataDir(), "AiPlayground"), "ep-probe.json");

    private static string CacheKey(string modelPath, string epName, string deviceType, int height, int width) =>
        $"{modelPath}|{epName}|{deviceType}|{height}x{width}";

    private static Dictionary<string, AiEpProbeResult> Load()
    {
        if (_cache is not null) return _cache;
        try
        {
            _cache = File.Exists(CachePath)
                ? JsonSerializer.Deserialize<Dictionary<string, AiEpProbeResult>>(File.ReadAllText(CachePath)) ?? []
                : [];
        }
        catch
        {
            _cache = [];
        }
        return _cache;
    }

    private static void Save()
    {
        try
        {
            var dir = Path.GetDirectoryName(CachePath);
            if (dir is not null) Directory.CreateDirectory(dir);
            var temp = CachePath + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(_cache, JsonOpts));
            File.Move(temp, CachePath, overwrite: true);
        }
        catch { }
    }

    /// <summary>已缓存的结果（不触发探测）；无缓存返回 null。</summary>
    public static AiEpProbeResult? GetCached(string modelPath, string epName, string deviceType, int height, int width)
    {
        lock (Gate)
        {
            return Load().TryGetValue(CacheKey(modelPath, epName, deviceType, height, width), out var r) ? r : null;
        }
    }

    public static void Store(string modelPath, string epName, string deviceType, int height, int width, AiEpProbeResult result)
    {
        lock (Gate)
        {
            Load()[CacheKey(modelPath, epName, deviceType, height, width)] = result;
            Save();
        }
    }

    /// <summary>清除缓存（换设备/驱动后可重探）。</summary>
    public static void Clear()
    {
        lock (Gate)
        {
            _cache = [];
            Save();
        }
    }

    /// <summary>
    /// 在隔离子进程里尝试用 <paramref name="epName"/> / <paramref name="deviceType"/> 为
    /// <paramref name="modelPath"/> 编译会话（形状定死为 height×width）。任何崩溃/超时/失败都返回 false。
    /// </summary>
    public static async Task<AiEpProbeResult> ProbeAsync(
        string modelPath, string epName, string deviceType, int height, int width,
        TimeSpan timeout, CancellationToken ct)
    {
        var exe = Environment.ProcessPath;
        if (string.IsNullOrEmpty(exe))
            return new AiEpProbeResult { Ok = false, Error = "无法定位程序自身路径。", CheckedAt = DateTime.Now };

        var outFile = Path.Combine(Path.GetTempPath(), $"tuba_ep_probe_{Guid.NewGuid():N}.json");
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add(ProbeArg);
        psi.ArgumentList.Add(modelPath);
        psi.ArgumentList.Add(epName);
        psi.ArgumentList.Add(deviceType);
        psi.ArgumentList.Add(height.ToString());
        psi.ArgumentList.Add(width.ToString());
        psi.ArgumentList.Add(outFile);
        // 子进程只做探测，不继承主程序的诊断/遥测等环境
        psi.Environment["TUBA_EP_PROBE_CHILD"] = "1";

        var result = new AiEpProbeResult { Ok = false, CheckedAt = DateTime.Now };
        Process? process = null;
        try
        {
            process = Process.Start(psi);
            if (process is null)
            {
                result.Error = "无法启动探测子进程。";
                return result;
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeoutCts.CancelAfter(timeout);
            try
            {
                await process.WaitForExitAsync(timeoutCts.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                try { process.Kill(entireProcessTree: true); } catch { }
                result.Error = ct.IsCancellationRequested ? "已取消。" : $"探测超时（>{timeout.TotalSeconds:F0}s），判定该模型不适合此设备。";
                return result;
            }

            if (process.ExitCode != 0)
            {
                result.Error = $"探测子进程异常退出（0x{process.ExitCode:X8}），判定该模型在此设备上会崩溃。";
                return result;
            }

            if (File.Exists(outFile))
            {
                try
                {
                    var parsed = JsonSerializer.Deserialize<AiEpProbeResult>(File.ReadAllText(outFile));
                    if (parsed is not null)
                    {
                        parsed.CheckedAt = DateTime.Now;
                        return parsed;
                    }
                }
                catch { }
            }
            result.Error = "探测子进程未返回结果。";
            return result;
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
            return result;
        }
        finally
        {
            try { process?.Dispose(); } catch { }
            try { if (File.Exists(outFile)) File.Delete(outFile); } catch { }
        }
    }

    /// <summary>
    /// 子进程入口：为给定模型在指定设备上编译一次会话（形状定死），把结果写入 <paramref name="outFile"/>。
    /// 成功返回 0；探测到不兼容时写失败结果并返回非 0（父进程据此回退）。崩溃由 OS 直接暴露为异常退出码。
    /// </summary>
    internal static void RunChild(string modelPath, string epName, string deviceType, int height, int width, string outFile)
    {
        var result = new AiEpProbeResult { Ok = false, CheckedAt = DateTime.Now };
        try
        {
            // EP 注册是进程级的：子进程是全新进程，必须先把已安装加速包注册进本进程的 OrtEnv。
            AiRuntimeService.RegisterInstalledProvidersAsync().GetAwaiter().GetResult();

            // 目标设备必须真实出现，否则探测会静默降级到 CPU 而给出假阳性
            // （YOLO 事故：探测在 CPU 上"成功"，主进程挂 NPU 时 0xC0000005 闪退）。
            var devices = AiRuntimeService.GetEpDevices()
                .Where(d => string.Equals(AiRuntimeService.DescribeEpDeviceName(d), epName, StringComparison.OrdinalIgnoreCase)
                            && string.Equals(AiRuntimeService.DescribeEpDeviceType(d), deviceType, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (devices.Count == 0)
            {
                result.Error = $"目标设备 {epName}/{deviceType} 未在本进程注册成功，探测无效（避免 CPU 假阳性）。";
                WriteResult(outFile, result);
                Environment.Exit(2);
                return;
            }

            using var session = AiRuntimeService.ProbeSessionOnDevice(modelPath, epName, deviceType, height, width);
            // 必须真跑一次推理：只建会话会出现假阳性（会话建成功但输入形状覆盖
            // 没生效，主进程真编译时 0xC0000005 闪退）。推理跑通才证明该模型在此设备上可用。
            AiRuntimeService.ProbeRunDummyInference(session, height, width);
            result.Ok = true;
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
        }

        WriteResult(outFile, result);
        Environment.Exit(result.Ok ? 0 : 2);
    }

    private static void WriteResult(string outFile, AiEpProbeResult result)
    {
        try
        {
            File.WriteAllText(outFile, JsonSerializer.Serialize(result));
        }
        catch { }
    }
}

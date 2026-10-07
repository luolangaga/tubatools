using System.Management;
using System.Runtime.InteropServices;
using Microsoft.ML.OnnxRuntime;
using Microsoft.Windows.AI.MachineLearning;

namespace TubaWinUi3.Services.AiPlayground;

/// <summary>一条 EP 设备信息（引擎页设备表用）。</summary>
public sealed class AiEpDeviceInfo
{
    public required string EpName { get; init; }
    public required string Vendor { get; init; }
    /// <summary>CPU / GPU / NPU。</summary>
    public required string DeviceType { get; init; }
    public string Display => $"{EpName} · {DeviceType}";
}

/// <summary>一次会话创建的结果。</summary>
public sealed class AiSessionResult
{
    public required InferenceSession Session { get; init; }
    public string RuntimeNote { get; init; } = "";
}

/// <summary>
/// 本地 AI 试炼场的运行时层：Windows ML 执行提供程序（EP）的发现 / 下载 / 注册、
/// ONNX Runtime 环境单例、按引擎模式创建会话、NPU 编译缓存管理、设备信息采集。
/// 依赖 Microsoft.Windows.AI.MachineLearning（自包含 Windows ML 运行时）。
/// </summary>
public static class AiRuntimeService
{
    private static readonly object EnvGate = new();
    private static OrtEnv? _env;
    private static bool _envFailed;
    private static string? _envError;

    // ── 平台门槛 ─────────────────────────────────────────────────────

    public static bool IsArchSupported =>
        RuntimeInformation.ProcessArchitecture is Architecture.X64 or Architecture.Arm64;

    /// <summary>不满足条件时返回原因（用于页面降级横幅）。null = 可用。</summary>
    public static string? UnsupportedReason
    {
        get
        {
            if (!IsArchSupported)
                return "当前是 32 位（x86）版本：Windows ML 推理运行时仅提供 64 位原生库，本地推理不可用。请使用 x64 / ARM64 版本。";
            if (Environment.OSVersion.Version.Build < 18362)
                return "系统版本过低：本地推理需要 Windows 10 19H1（build 18362）及以上。";
            return null;
        }
    }

    /// <summary>厂商加速包（NPU/GPU EP）的在线安装需要 Windows 11 24H2（build 26100）及以上。</summary>
    public static bool CanInstallVendorEps => Environment.OSVersion.Version.Build >= 26100;

    public static int GetOsBuild() => Environment.OSVersion.Version.Build;

    // ── ONNX Runtime 环境（进程单例）─────────────────────────────────

    public static OrtEnv? GetEnvironment()
    {
        lock (EnvGate)
        {
            if (_env is not null) return _env;
            if (_envFailed) return null;
            try
            {
                EnvironmentCreationOptions options = new()
                {
                    logId = "TubaAiPlayground",
                    logLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR,
                };
                _env = OrtEnv.CreateInstanceWithOptions(ref options);
            }
            catch (Exception ex)
            {
                _envFailed = true;
                _envError = ex.Message;
                _env = null;
            }
            return _env;
        }
    }

    public static string? EnvironmentError => _envError;

    /// <summary>当前已注册到 ONNX Runtime 的 EP 设备（未装任何厂商加速包时 = CPU + DirectML）。</summary>
    public static IReadOnlyList<OrtEpDevice> GetEpDevices()
    {
        try
        {
            return GetEnvironment()?.GetEpDevices() ?? [];
        }
        catch
        {
            return [];
        }
    }

    public static IReadOnlyList<AiEpDeviceInfo> DescribeEpDevices()
    {
        var list = new List<AiEpDeviceInfo>();
        foreach (var device in GetEpDevices())
        {
            string type;
            string vendor;
            try
            {
                type = device.HardwareDevice.Type.ToString();
                vendor = device.EpVendor;
            }
            catch
            {
                continue;
            }
            list.Add(new AiEpDeviceInfo { EpName = device.EpName, Vendor = vendor, DeviceType = type });
        }
        return list;
    }

    public static bool HasNpuDevice() =>
        GetEpDevices().Any(d => IsNpuDevice(d));

    private static bool IsNpuDevice(OrtEpDevice device)
    {
        try
        {
            return device.HardwareDevice.Type == OrtHardwareDeviceType.NPU;
        }
        catch
        {
            return false;
        }
    }

    // ── EP 目录：发现 / 下载 / 注册 ───────────────────────────────────

    private static string? _catalogError;

    /// <summary>最近一次读取 EP 目录失败的原因（成功时为 null）。</summary>
    public static string? CatalogError => _catalogError;

    public static IReadOnlyList<ExecutionProvider> GetCatalogProviders()
    {
        try
        {
            var providers = ExecutionProviderCatalog.GetDefault().FindAllProviders();
            _catalogError = null;
            return providers;
        }
        catch (Exception ex)
        {
            _catalogError = ex.Message;
            return [];
        }
    }

    /// <summary>一键：下载并以认证方式注册全部适合本机的厂商加速包。</summary>
    public static async Task<(bool Ok, string Message)> EnsureAndRegisterCertifiedAsync()
    {
        if (!CanInstallVendorEps && !HasInstalledVendorEp())
            return (false, "安装厂商加速包需要 Windows 11 24H2（build 26100）及以上，当前系统版本不支持。");
        try
        {
            await ExecutionProviderCatalog.GetDefault().EnsureAndRegisterCertifiedAsync();
            foreach (var p in GetCatalogProviders())
            {
                if (p.ReadyState == ExecutionProviderReadyState.Ready)
                    RegisteredProviders.Add(p.Name);
            }
            return (true, "已完成：适合本机的加速包已安装并注册。");
        }
        catch (Exception ex)
        {
            return (false, $"安装失败：{ex.Message}");
        }
    }

    /// <summary>本进程已成功注册的 EP 名（EP 注册是进程级的，重启后需重新注册）。</summary>
    private static readonly HashSet<string> RegisteredProviders = new(StringComparer.OrdinalIgnoreCase);

    public static bool IsProviderRegistered(string name) => RegisteredProviders.Contains(name);

    /// <summary>
    /// 只注册「已安装」（ReadyState != NotPresent）的加速包，绝不触发下载 —— 供启动后台预热，
    /// 避免用户每次开程序都要手动点一次「启用并注册」。单个 EP 失败不影响其余，全程静默。
    /// </summary>
    public static async Task RegisterInstalledProvidersAsync()
    {
        if (UnsupportedReason is not null) return;
        try
        {
            foreach (var provider in GetCatalogProviders())
            {
                if (provider.ReadyState == ExecutionProviderReadyState.NotPresent) continue;
                if (RegisteredProviders.Contains(provider.Name)) continue;
                try
                {
                    await provider.EnsureReadyAsync();
                    if (provider.TryRegister())
                        RegisteredProviders.Add(provider.Name);
                }
                catch { }
            }
        }
        catch { }
    }

    /// <summary>安装（按需下载）并注册单个 EP。</summary>
    public static async Task<(bool Ok, string Message)> EnsureProviderAsync(ExecutionProvider provider)
    {
        try
        {
            if (provider.ReadyState == ExecutionProviderReadyState.NotPresent && !CanInstallVendorEps)
                return (false, "该加速包未安装，且当前系统低于 Windows 11 24H2，无法在线安装。");

            var result = await provider.EnsureReadyAsync();
            if (result.Status != ExecutionProviderReadyResultState.Success)
            {
                var detail = result.DiagnosticText;
                if (string.IsNullOrWhiteSpace(detail))
                    detail = result.ExtendedError?.Message ?? "未知错误";
                return (false, $"下载/安装失败：{detail}");
            }
            if (!provider.TryRegister())
                return (false, "加速包已安装，但注册到 ONNX Runtime 失败。");
            RegisteredProviders.Add(provider.Name);
            return (true, "已就绪并注册。");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    private static bool HasInstalledVendorEp()
    {
        try
        {
            return GetCatalogProviders().Any(p => p.ReadyState != ExecutionProviderReadyState.NotPresent);
        }
        catch
        {
            return false;
        }
    }

    // ── 引擎模式与会话创建 ───────────────────────────────────────────

    public static AiEngineMode GetMode()
    {
        var raw = AppSettings.Get("AiPlayground_EpMode");
        return raw?.ToLowerInvariant() switch
        {
            "npu" => AiEngineMode.Npu,
            "gpu" => AiEngineMode.Gpu,
            "cpu" => AiEngineMode.Cpu,
            _ => AiEngineMode.Auto,
        };
    }

    public static void SetMode(AiEngineMode mode) =>
        AppSettings.Set("AiPlayground_EpMode", mode.ToString().ToLowerInvariant());

    private static ExecutionProviderDevicePolicy GetPolicy() => GetPolicyFor(
        GetMode(),
        GetEpDevices().Select(SafeDeviceType).Where(t => t.Length > 0).ToList());

    private static bool HasType(IReadOnlyList<string> deviceTypes, string type) =>
        deviceTypes.Any(t => string.Equals(t, type, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// 引擎模式 → 生成 ORT EP 选择策略（纯逻辑，供单测）。
    /// <para>
    /// **策略路径永远不选 NPU**：NPU（尤其 Intel OpenVINO NPU）对不支持的模型会让原生编译器
    /// abort/访问违例（0xC0000409 / 0xC0000005，托管 try/catch 拦不住），而策略选择发生在主进程、
    /// 无法事先探测。因此 NPU 只允许走「页面解析出显式设备 → 子进程探测通过 → 显式 Append」这一条门；
    /// 策略只在显式解析失败（无任何加速设备）时兜底，且最多到 GPU / CPU。
    /// 另外绝不选用 OpenVINO 的 AUTO 设备 —— 它会在内部自行路由到 NPU。
    /// </para>
    /// </summary>
    internal static ExecutionProviderDevicePolicy GetPolicyFor(
        AiEngineMode mode, IReadOnlyList<string> deviceTypes)
    {
        var hasGpu = HasType(deviceTypes, "GPU");

        return mode switch
        {
            AiEngineMode.Cpu => ExecutionProviderDevicePolicy.PREFER_CPU,
            _ => hasGpu ? ExecutionProviderDevicePolicy.PREFER_GPU : ExecutionProviderDevicePolicy.PREFER_CPU,
        };
    }

    /// <summary>
    /// 为视觉会话解析要显式使用的 EP 设备（纯逻辑核心可单测）。
    /// 返回 (EpName, DeviceType, RequiresProbe)。显式指定时原样返回（NPU 需要探测）；
    /// 未指定时按引擎模式挑**单个**设备：GPU 优先 DirectML（不走 OpenVINO，杜绝 AUTO 内部路由 NPU），
    /// NPU 只认 QNN（免探测）或非 AUTO 的 OpenVINO NPU（必须探测），AUTO 设备一律排除。
    /// </summary>
    public static (string? EpName, string? DeviceType, bool RequiresProbe) ResolveVisionTarget(
        string? explicitEpName, string? explicitDeviceType)
    {
        if (!string.IsNullOrEmpty(explicitEpName))
            return (explicitEpName, explicitDeviceType, RequiresProbe(explicitEpName, explicitDeviceType));

        var devices = GetEpDevices()
            .Select(d => (Name: SafeEpName(d), Type: SafeDeviceType(d)))
            .Where(d => d.Name.Length > 0)
            .ToList();
        return ResolveVisionTargetCore(GetMode(), devices);
    }

    internal static (string? EpName, string? DeviceType, bool RequiresProbe) ResolveVisionTargetCore(
        AiEngineMode mode, IReadOnlyList<(string Name, string Type)> devices)
    {
        // AUTO 设备一律排除：OpenVINO AUTO 会在内部自行选择 NPU，绕过探测防线
        static bool IsAuto((string Name, string Type) d) => d.Name.Contains("AUTO", StringComparison.OrdinalIgnoreCase);
        static bool Has(IReadOnlyList<(string Name, string Type)> list, string type) =>
            list.Any(d => string.Equals(d.Type, type, StringComparison.OrdinalIgnoreCase));

        var pickType = mode switch
        {
            AiEngineMode.Npu => Has(devices, "NPU") ? "NPU" : "CPU",
            AiEngineMode.Gpu => Has(devices, "GPU") ? "GPU" : "CPU",
            AiEngineMode.Cpu => "CPU",
            // Auto：有 GPU 用 GPU，否则 NPU（显式 + 探测），否则 CPU
            _ => Has(devices, "GPU") ? "GPU" : Has(devices, "NPU") ? "NPU" : "CPU",
        };

        if (pickType == "GPU")
        {
            // GPU 优先 DirectML（内部不会路由 NPU）；其余非 AUTO 的 GPU（如 OpenVINO GPU）也可
            var dml = devices.FirstOrDefault(d => d.Type == "GPU" && !IsAuto(d) &&
                d.Name.Contains("Dml", StringComparison.OrdinalIgnoreCase));
            if (dml.Name is not null) return (dml.Name, "GPU", false);
            var gpu = devices.FirstOrDefault(d => d.Type == "GPU" && !IsAuto(d));
            if (gpu.Name is not null) return (gpu.Name, "GPU", false);
        }
        else if (pickType == "NPU")
        {
            var qnn = devices.FirstOrDefault(d => d.Type == "NPU" && !IsAuto(d) &&
                d.Name.Contains("QNN", StringComparison.OrdinalIgnoreCase));
            if (qnn.Name is not null) return (qnn.Name, "NPU", false);
            var npu = devices.FirstOrDefault(d => d.Type == "NPU" && !IsAuto(d));
            if (npu.Name is not null) return (npu.Name, "NPU", true);
        }
        else
        {
            var cpu = devices.FirstOrDefault(d => d.Type == "CPU" && !IsAuto(d));
            if (cpu.Name is not null) return (cpu.Name, "CPU", false);
        }

        return (null, null, false);
    }

    /// <summary>
    /// NPU 探测失败后的回退：显式挑一个安全设备（GPU 优先 DirectML，否则 CPU），
    /// 返回的永远是**显式**设备 —— 绝不返回 null，避免调用方落入策略路径（策略可能再选 NPU）。
    /// </summary>
    public static (string? EpName, string? DeviceType) ResolveVisionFallback()
    {
        var devices = GetEpDevices()
            .Select(d => (Name: SafeEpName(d), Type: SafeDeviceType(d)))
            .Where(d => d.Name.Length > 0 && !d.Name.Contains("AUTO", StringComparison.OrdinalIgnoreCase))
            .ToList();

        var dml = devices.FirstOrDefault(d => d.Type == "GPU" &&
            d.Name.Contains("Dml", StringComparison.OrdinalIgnoreCase));
        if (dml.Name is not null) return (dml.Name, "GPU");

        var gpu = devices.FirstOrDefault(d => d.Type == "GPU");
        if (gpu.Name is not null) return (gpu.Name, "GPU");

        var cpu = devices.FirstOrDefault(d => d.Type == "CPU");
        if (cpu.Name is not null) return (cpu.Name, "CPU");

        return (null, null);
    }

    /// <summary>策略将优先选中的设备类型（用于展示实际设备说明）。null = 由 ORT 决定。</summary>
    private static (string? Type, string Note) SelectPolicyDeviceType(AiEngineMode mode) =>
        GetPolicyFor(mode, GetEpDevices().Select(SafeDeviceType).Where(t => t.Length > 0).ToList()) switch
        {
            ExecutionProviderDevicePolicy.PREFER_NPU => ("NPU", "优先 NPU"),
            ExecutionProviderDevicePolicy.PREFER_GPU => ("GPU", "优先 GPU"),
            ExecutionProviderDevicePolicy.PREFER_CPU => ("CPU", "仅 CPU"),
            _ => (null, DescribeModeFor(mode)),
        };

    public static string DescribeMode() => GetMode() switch
    {
        AiEngineMode.Npu => "优先 NPU",
        AiEngineMode.Gpu => "优先 GPU",
        AiEngineMode.Cpu => "仅 CPU",
        _ => "自动（NPU → GPU → CPU）",
    };

    /// <summary>
    /// 为 ORT GenAI（LLM）解析要使用的执行提供程序：显式选定设备优先，
    /// 否则按引擎模式挑第一个对应硬件类型的已注册设备；自动模式或找不到时返回
    /// provider = null（交给 Windows ML 自动选择 NPU → GPU → CPU）。
    /// Note 为可直接展示的设备说明（如 "GPU（DmlExecutionProvider）"）。
    /// </summary>
    public static (string? Provider, string? DeviceType, string Note) ResolveLlmTarget(
        string? explicitEpName, string? explicitDeviceType)
    {
        // 显式设备仅在 GenAI 可用时才采用（否则交给下面的模式解析 → 回退到可用加速器/CPU）
        if (!string.IsNullOrEmpty(explicitEpName) && IsLlmUsableEp(explicitEpName, explicitDeviceType ?? ""))
            return (explicitEpName, explicitDeviceType, DescribeTarget(explicitDeviceType, explicitEpName));

        var available = GetEpDevices()
            .Select(d => (Name: SafeEpName(d), Type: SafeDeviceType(d)))
            .Where(d => d.Name.Length > 0)
            .ToList();
        return ResolveLlmTargetCore(GetMode(), available);
    }

    /// <summary>
    /// 纯逻辑：按引擎模式从候选设备里挑 LLM 要显式使用的提供程序。
    /// <para>
    /// 关键约束（本机实测 2026-10）：Phi-3.5-mini INT4 上，**CPU 是唯一跑得通的设备** ——
    /// DirectML 加载即报错（控制流节点）；OpenVINO NPU 无法编译动态序列长度（静默回退其 CPU）；
    /// OpenVINO GPU 虽能加载，但**生成首 token 时即崩**（<c>Failed to find allocator</c>）。
    /// 因此除非显式选中 QNN NPU，一律返回 null（GenAI 按 genai_config 默认走 CPU）并如实标注。
    /// 等新版 ORT GenAI / OpenVINO EP 修复后可在 <see cref="IsLlmUsableEp"/> 放行对应设备。
    /// </para>
    /// </summary>
    internal static (string? Provider, string? DeviceType, string Note) ResolveLlmTargetCore(
        AiEngineMode mode,
        IReadOnlyList<(string Name, string Type)> availableDevices)
    {
        var wanted = mode switch
        {
            AiEngineMode.Npu => "NPU",
            AiEngineMode.Gpu => "GPU",
            AiEngineMode.Cpu => "CPU",
            _ => null,
        };

        if (wanted is not null)
        {
            var match = availableDevices.FirstOrDefault(d =>
                string.Equals(d.Type, wanted, StringComparison.OrdinalIgnoreCase) &&
                IsLlmUsableEp(d.Name, d.Type));
            if (string.IsNullOrEmpty(match.Name))
                return (null, null, DescribeModeFor(mode) + " · LLM 暂无可用加速（已用 CPU）");
            return (match.Name, wanted, DescribeTarget(wanted, match.Name));
        }

        // Auto：只认 QNN NPU（其余实测均不可用/会崩）
        var accel = availableDevices.FirstOrDefault(d => IsLlmUsableEp(d.Name, d.Type));
        if (string.IsNullOrEmpty(accel.Name))
            return (null, null, "CPU（LLM 加速暂不可用：DML 不兼容控制流、OpenVINO NPU 编译不过动态序列、OpenVINO GPU 生成阶段崩溃）");
        return (accel.Name, accel.Type, DescribeTarget(accel.Type, accel.Name));
    }

    /// <summary>
    /// 该 EP/设备能否用于 LLM（ORT GenAI）。本机实测：DirectML/WebGPU 加载报错（控制流节点）；
    /// OpenVINO NPU 编译不过动态序列（静默回退其 CPU）；OpenVINO GPU 生成首 token 即崩（allocator）。
    /// 目前只有 **QNN NPU** 通过；其余等新版 ORT GenAI / OpenVINO EP 修复后再放行。
    /// </summary>
    private static bool IsLlmUsableEp(string epName, string deviceType)
    {
        if (string.IsNullOrEmpty(epName)) return false;
        if (string.Equals(deviceType, "NPU", StringComparison.OrdinalIgnoreCase))
            return epName.Contains("QNN", StringComparison.OrdinalIgnoreCase);
        return false;
    }

    private static string DescribeModeFor(AiEngineMode mode) => mode switch
    {
        AiEngineMode.Npu => "优先 NPU",
        AiEngineMode.Gpu => "优先 GPU",
        AiEngineMode.Cpu => "仅 CPU",
        _ => "自动（NPU → GPU → CPU）",
    };

    private static string SafeEpName(OrtEpDevice device)
    {
        try { return device.EpName ?? ""; } catch { return ""; }
    }

    private static string SafeDeviceType(OrtEpDevice device)
    {
        try { return device.HardwareDevice.Type.ToString(); } catch { return ""; }
    }

    /// <summary>探测子进程判断目标设备是否真实注册用：EP 名。</summary>
    public static string DescribeEpDeviceName(OrtEpDevice device) => SafeEpName(device);

    /// <summary>探测子进程判断目标设备是否真实注册用：设备类型。</summary>
    public static string DescribeEpDeviceType(OrtEpDevice device) => SafeDeviceType(device);

    private static string DescribeTarget(string? deviceType, string epName)
    {
        var type = string.IsNullOrEmpty(deviceType) ? "" : deviceType.ToUpperInvariant();
        if (type.Length == 0)
        {
            // 类型未知时从提供程序名猜一个可读标签（如 DmlExecutionProvider → GPU）
            type = epName.Contains("Dml", StringComparison.OrdinalIgnoreCase) ? "GPU"
                : epName.Contains("Cpu", StringComparison.OrdinalIgnoreCase) ? "CPU"
                : epName;
        }
        return $"{type}（{epName}）";
    }

    /// <summary>
    /// 创建会话选项：显式指定 EP 设备（epName 非空）时显式附加该设备（NPU 需调用方先探测）；
    /// 否则走策略兜底 —— 策略**永远不选 NPU**（见 <see cref="GetPolicyFor"/>），AUTO 设备在显式分支被排除。
    /// <paramref name="symbolicDims"/> 为模型输入的符号维度名（静态形状覆盖按它映射，见
    /// <see cref="ApplyStaticShapeOverrides"/>；YOLO 事故后不再猜名字）。
    /// </summary>
    public static SessionOptions CreateSessionOptions(
        string? explicitEpName = null,
        string? explicitDeviceType = null,
        (int Height, int Width)? staticShape = null,
        IReadOnlyList<string>? symbolicDims = null)
    {
        var options = new SessionOptions();
        if (!string.IsNullOrEmpty(explicitEpName))
        {
            var env = GetEnvironment();
            if (env is not null)
            {
                // 只取一个设备：同一 EP 可能有多个同类设备（如 3 个 DML GPU），
                // 而 ORT 的 AppendExecutionProvider 要求全部设备属于同一 EP 且部分 EP（DML）只接受单个设备。
                // AUTO 设备（OpenVINO AUTO 内部会自行路由 NPU）显式排除。
                var device = env.GetEpDevices()
                    .Where(d => string.Equals(d.EpName, explicitEpName, StringComparison.OrdinalIgnoreCase)
                                && !d.EpName.Contains("AUTO", StringComparison.OrdinalIgnoreCase))
                    .Where(d => string.IsNullOrEmpty(explicitDeviceType)
                        || string.Equals(SafeDeviceType(d), explicitDeviceType, StringComparison.OrdinalIgnoreCase))
                    .FirstOrDefault();
                if (device is not null)
                {
                    options.AppendExecutionProvider(env, [device], new Dictionary<string, string>());
                    ApplyStaticShapeOverrides(options, staticShape, symbolicDims);
                    return options;
                }
            }
            // 显式设备已不可用（如加速包被卸载）→ 回退策略模式
        }
        options.SetEpSelectionPolicy(GetPolicy());
        ApplyStaticShapeOverrides(options, staticShape, symbolicDims);
        return options;
    }

    /// <summary>
    /// 把模型的自由/符号维度定死成静态形状。**Intel NPU 只支持静态形状**：模型的
    /// <c>[-1,-1,-1,-1]</c> 动态输入会让 VPUX 编译器报 <c>Missing upper bound</c> 并 abort/卡死；
    /// 用 FreeDimensionOverride 定死后即可正常编译（实测 resnet50 NPU 比 CPU 快约 5 倍）。
    /// CPU/GPU 上这些覆盖同样安全（形状本就是该值）。
    /// <para>
    /// 关键（YOLO 闪退事故）：符号名**不能猜** —— YOLO 的是 <c>[batch, "", height, width]</c>
    /// （channels 是空串、batch 不叫 batch_size），按固定名字覆盖会漏维度，NPU 编译时
    /// 0xC0000005 闪退。因此先读模型 InputMetadata 的 SymbolicDimensions，按位置映射
    /// （[0]=batch→1、[1]=channels→3、[2]=height、[3]=width）逐个覆盖，常见的名字别名再补一轮。
    /// </para>
    /// </summary>
    internal static void ApplyStaticShapeOverrides(
        SessionOptions options, (int Height, int Width)? shape, IReadOnlyList<string>? symbolicDims = null)
    {
        if (shape is not { } s) return;

        // 按位置映射真实符号名（含空串）—— 覆盖以名字为键，必须用模型自己的符号名
        if (symbolicDims is { Count: 4 })
        {
            var values = new[] { 1, 3, s.Height, s.Width };
            for (var i = 0; i < 4; i++)
            {
                var name = symbolicDims[i];
                if (string.IsNullOrEmpty(name)) continue;
                TryOverride(options, name, values[i]);
            }
        }

        // 常见别名兜底（不同导出器的命名习惯）
        TryOverride(options, "batch_size", 1);
        TryOverride(options, "batch", 1);
        TryOverride(options, "num_channels", 3);
        TryOverride(options, "channels", 3);
        TryOverride(options, "height", s.Height);
        TryOverride(options, "width", s.Width);
    }

    /// <summary>读取模型第一个输入的符号维度名（供 <see cref="ApplyStaticShapeOverrides"/>）。</summary>
    public static IReadOnlyList<string>? GetSymbolicDims(string modelPath)
    {
        try
        {
            using var session = new InferenceSession(modelPath);
            var meta = session.InputMetadata.First().Value;
            return meta.SymbolicDimensions.ToArray();
        }
        catch
        {
            return null;
        }
    }

    private static void TryOverride(SessionOptions options, string dimName, int value)
    {
        try { options.AddFreeDimensionOverrideByName(dimName, value); } catch { }
    }

    /// <summary>
    /// 为子进程探测创建会话：显式指定 EP 设备并把形状定死（不落 NPU 编译缓存，避免污染）。
    /// 仅由 <see cref="AiEpProbe.RunChild"/> 在隔离子进程中调用。
    /// </summary>
    internal static InferenceSession ProbeSessionOnDevice(
        string modelPath, string epName, string deviceType, int height, int width)
    {
        var options = CreateSessionOptions(epName, deviceType, (height, width), GetSymbolicDims(modelPath));
        return new InferenceSession(modelPath, options);
    }

    /// <summary>
    /// 探测用哑推理：对会话喂一次全零输入并跑通。**必须做** —— 只建会话会出现假阳性
    /// （YOLO 事故：会话建成但形状覆盖没生效，主进程真编译时 0xC0000005 闪退）。
    /// 支持单输入 NCHW 浮点模型与多输入（如 DETR 的 pixel_mask）模型。
    /// </summary>
    internal static void ProbeRunDummyInference(InferenceSession session, int height, int width)
    {
        var inputs = new List<NamedOnnxValue>();
        foreach (var meta in session.InputMetadata)
        {
            var dims = meta.Value.Dimensions;
            if (dims.Length == 4)
            {
                var h = dims[2] > 0 ? dims[2] : height;
                var w = dims[3] > 0 ? dims[3] : width;
                var data = new float[1 * 3 * h * w];
                inputs.Add(NamedOnnxValue.CreateFromTensor(
                    meta.Key, new Microsoft.ML.OnnxRuntime.Tensors.DenseTensor<float>(data, new[] { 1, 3, h, w })));
            }
            else if (dims.Length == 3)
            {
                // 如 DETR pixel_mask [batch, maskH, maskW]：用 1x64x64 全 1
                var d1 = dims[1] > 0 ? dims[1] : 64;
                var d2 = dims[2] > 0 ? dims[2] : 64;
                var data = new float[1 * d1 * d2];
                Array.Fill(data, 1f);
                inputs.Add(NamedOnnxValue.CreateFromTensor(
                    meta.Key, new Microsoft.ML.OnnxRuntime.Tensors.DenseTensor<float>(data, new[] { 1, d1, d2 })));
            }
            else
            {
                throw new InvalidOperationException($"探测不支持的输入形状 [{string.Join(",", dims)}]（{meta.Key}）。");
            }
        }
        using var outputs = session.Run(inputs);
    }

    /// <summary>
    /// 显式选定的 NPU 设备是否**必须经子进程探测**才能使用。非 QNN 的 NPU（Intel OpenVINO NPU 等）
    /// 对不支持的模型会让原生编译器 abort/卡死，故用前必须先探测（见 <see cref="AiEpProbe"/>）。
    /// QNN 走自身的 EP 上下文编译，不需要探测。
    /// </summary>
    public static bool RequiresProbe(string? epName, string? deviceType) =>
        string.Equals(deviceType, "NPU", StringComparison.OrdinalIgnoreCase) &&
        !string.IsNullOrEmpty(epName) &&
        !epName.Contains("QNN", StringComparison.OrdinalIgnoreCase);

    /// <summary>已注册设备里第一个 NPU 的 EP 名（无则 null）。</summary>
    public static string? FindNpuEpName()
    {
        var dev = GetEpDevices().FirstOrDefault(d =>
            string.Equals(SafeDeviceType(d), "NPU", StringComparison.OrdinalIgnoreCase));
        return dev is null ? null : SafeEpName(dev);
    }

    /// <summary>
    /// 从模型输入元数据推断静态形状（height, width）：优先取已知静态维度，否则按任务默认
    /// （分类/特征 224、检测 640）。**Intel NPU 需要静态形状**，探测与建会话都用它。
    /// </summary>
    public static (int Height, int Width) ResolveStaticShape(string modelPath, AiTaskKind task)
    {
        try
        {
            using var session = new InferenceSession(modelPath);
            var dims = session.InputMetadata.First().Value.Dimensions;
            if (dims.Length == 4)
            {
                var h = dims[2] > 0 ? dims[2] : 0;
                var w = dims[3] > 0 ? dims[3] : 0;
                if (h > 0 && w > 0) return (h, w);
                if (h > 0) return (h, h);
            }
        }
        catch { }
        return task switch
        {
            AiTaskKind.ObjectDetection => (640, 640),
            _ => (224, 224),
        };
    }

    /// <summary>
    /// 探测（带缓存）某模型能否在指定 NPU 设备上安全编译。同 (模型,EP,设备,形状) 只探一次。
    /// </summary>
    public static async Task<AiEpProbeResult> EnsureProbedAsync(
        string modelPath, string epName, string deviceType, (int Height, int Width) shape, CancellationToken ct)
    {
        var cached = AiEpProbe.GetCached(modelPath, epName, deviceType, shape.Height, shape.Width);
        if (cached is not null) return cached;

        // 首次冷编译可能长达数分钟（实测 DETR 266s、CLIP 180s+，之后走 OpenVINO 驱动级缓存）。
        // 超时按「不兼容」处理，但必须给足，否则会把只是编译慢的模型误判为不支持。
        var result = await AiEpProbe.ProbeAsync(modelPath, epName, deviceType, shape.Height, shape.Width,
            TimeSpan.FromSeconds(300), ct).ConfigureAwait(false);
        AiEpProbe.Store(modelPath, epName, deviceType, shape.Height, shape.Width, result);
        return result;
    }

    private static bool IsNpuConfiguration(string? explicitEpName, string? explicitDeviceType)
    {
        if (!string.IsNullOrEmpty(explicitEpName))
            return string.Equals(explicitDeviceType, "NPU", StringComparison.OrdinalIgnoreCase);
        return GetMode() is AiEngineMode.Auto or AiEngineMode.Npu && HasNpuDevice();
    }

    /// <summary>
    /// 是否需要「EP 上下文编译并缓存」。这是 **Qualcomm QNN 专属**能力（把图预编译成 QNN 上下文）。
    /// Intel OpenVINO 等其它 EP 走各自的内部编译，对它们调用
    /// <c>OrtModelCompilationOptions.CompileModel()</c> 会在原生侧直接 abort（0xC0000409 fail-fast，
    /// 托管 try/catch 拦不住）。因此只在确有 QNN 设备时才启用。
    /// </summary>
    private static bool SupportsEpContextCompilation(string? explicitEpName, string? explicitDeviceType)
    {
        // 仅当显式目标（或最终策略兜底将选中的）确为 QNN 时才编译；
        // 任何非 QNN 的目标（OpenVINO NPU / DML / CPU）都绝不走 CompileModel（原生 abort，拦不住）。
        var targetEp = explicitEpName;
        if (string.IsNullOrEmpty(targetEp))
        {
            var (ep, _, _) = ResolveVisionTarget(null, null);
            targetEp = ep;
        }
        return !string.IsNullOrEmpty(targetEp) &&
               targetEp.Contains("QNN", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// 创建视觉模型推理会话。<paramref name="staticShape"/> 非空时把动态输入维度定死
    /// （**Intel NPU 必需**，见 <see cref="ApplyStaticShapeOverrides"/>）。
    /// QNN 配置下首次会编译 EP 上下文并缓存（编译失败自动回退）。
    /// </summary>
    public static AiSessionResult CreateVisionSession(
        string modelPath,
        string cacheKeyBase,
        string? explicitEpName,
        string? explicitDeviceType,
        (int Height, int Width)? staticShape,
        Action<string>? status)
    {
        var symbolicDims = GetSymbolicDims(modelPath);
        var options = CreateSessionOptions(explicitEpName, explicitDeviceType, staticShape, symbolicDims);

        // EP 上下文编译（CompileModel）是 QNN 专属：其它 EP（Intel OpenVINO / DirectML）上调用会在
        // 原生侧直接 abort（0xC0000409，托管拦不住），故仅对 QNN 走这条编译缓存分支。
        var compile = SupportsEpContextCompilation(explicitEpName, explicitDeviceType) &&
                      IsNpuConfiguration(explicitEpName, explicitDeviceType);
        if (compile)
        {
            try
            {
                var descriptor = SanitizeForFileName(
                    $"{(string.IsNullOrEmpty(explicitEpName) ? GetMode().ToString() : explicitEpName)}_{explicitDeviceType ?? "default"}");
                var compiledPath = Path.Combine(CompiledCacheDir, $"{SanitizeForFileName(cacheKeyBase)}__{descriptor}.onnx");
                if (File.Exists(compiledPath))
                {
                    try
                    {
                        return new AiSessionResult
                        {
                            Session = new InferenceSession(compiledPath, options),
                            RuntimeNote = "NPU 编译缓存",
                        };
                    }
                    catch
                    {
                        TryDelete(compiledPath);
                    }
                }

                status?.Invoke("首次为该模型编译 NPU 加速版本，可能需要几分钟…");
                Directory.CreateDirectory(CompiledCacheDir);
                using (var compileOptions = new OrtModelCompilationOptions(options))
                {
                    compileOptions.SetInputModelPath(modelPath);
                    compileOptions.SetOutputModelPath(compiledPath);
                    compileOptions.SetEpContextEmbedMode(true);
                    compileOptions.CompileModel();
                }
                if (File.Exists(compiledPath))
                {
                    return new AiSessionResult
                    {
                        Session = new InferenceSession(compiledPath, options),
                        RuntimeNote = "NPU（新编译并缓存）",
                    };
                }
            }
            catch (Exception ex)
            {
                status?.Invoke("NPU 编译未成功，回退常规推理：" + ex.Message);
            }
            // 编译失败 → 用新 options 常规加载（上面的 options 可能已被编译过程消费，重建一份）
            options = CreateSessionOptions(explicitEpName, explicitDeviceType, staticShape);
        }

        status?.Invoke("正在创建推理会话…");
        return new AiSessionResult
        {
            Session = new InferenceSession(modelPath, options),
            RuntimeNote = DescribeEpSelection(explicitEpName, explicitDeviceType),
        };
    }

    /// <summary>
    /// 视觉会话实际使用的设备说明：显式且安全的设备用其名称；不安全（非 QNN NPU）或未指定时，
    /// 按引擎模式给出策略将选中的 EP；自动模式下如实列出当前可用设备类型，
    /// 避免让用户误以为跑在会被拒绝的加速器上。
    /// </summary>
    public static string DescribeEpSelection(string? explicitEpName, string? explicitDeviceType)
    {
        // 走到这里时显式设备已通过探测（页面在调用前会先探测非 QNN NPU），故直接如实标注。
        if (!string.IsNullOrEmpty(explicitEpName))
            return DescribeTarget(explicitDeviceType, explicitEpName);

        var (targetType, note) = SelectPolicyDeviceType(GetMode());
        if (targetType is not null)
        {
            var ep = GetEpDevices().FirstOrDefault(d =>
                string.Equals(SafeDeviceType(d), targetType, StringComparison.OrdinalIgnoreCase));
            var epName = ep is null ? null : SafeEpName(ep);
            if (!string.IsNullOrEmpty(epName))
                return DescribeTarget(targetType, epName);
        }

        var types = GetEpDevices()
            .Select(SafeDeviceType)
            .Where(t => t.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        return types.Count == 0 ? note : $"{note} · 当前可用：{string.Join(" + ", types)}";
    }

    // ── NPU 编译缓存管理 ─────────────────────────────────────────────

    public static string CompiledCacheDir => Path.Combine(ConfigManager.GetDataDir(), "ai-models", ".compiled");

    public static (int Count, long Bytes) GetCompiledCacheInfo()
    {
        try
        {
            if (!Directory.Exists(CompiledCacheDir)) return (0, 0);
            var files = Directory.GetFiles(CompiledCacheDir);
            return (files.Length, files.Sum(f =>
            {
                try { return new FileInfo(f).Length; } catch { return 0L; }
            }));
        }
        catch
        {
            return (0, 0);
        }
    }

    public static void ClearCompiledCache()
    {
        try
        {
            if (Directory.Exists(CompiledCacheDir))
                Directory.Delete(CompiledCacheDir, recursive: true);
        }
        catch { }
    }

    private static string SanitizeForFileName(string value)
    {
        var chars = value.Select(c => char.IsLetterOrDigit(c) || c is '_' or '-' or '.' ? c : '_').ToArray();
        var result = new string(chars).Trim('_');
        return string.IsNullOrEmpty(result) ? "model" : result;
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { }
    }

    // ── 设备信息（引擎页 + 状态条）──────────────────────────────────

    public static string? DetectNpuName()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher(
                "SELECT Name FROM Win32_PnPEntity WHERE PNPClass = 'ComputeAccelerator'");
            foreach (var item in searcher.Get())
            {
                var name = item["Name"]?.ToString();
                if (!string.IsNullOrWhiteSpace(name)) return name;
            }
        }
        catch { }
        return null;
    }

    public static IReadOnlyList<string> DetectGpuNames()
    {
        var list = new List<string>();
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
            foreach (var item in searcher.Get())
            {
                var name = item["Name"]?.ToString();
                if (!string.IsNullOrWhiteSpace(name)) list.Add(name);
            }
        }
        catch { }
        return list;
    }

    public static string? DetectCpuName()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                @"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            return key?.GetValue("ProcessorNameString")?.ToString()?.Trim();
        }
        catch
        {
            return null;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MemoryStatusEx
    {
        public uint Length;
        public uint MemoryLoad;
        public ulong TotalPhys;
        public ulong AvailPhys;
        public ulong TotalPageFile;
        public ulong AvailPageFile;
        public ulong TotalVirtual;
        public ulong AvailVirtual;
        public ulong AvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GlobalMemoryStatusEx(ref MemoryStatusEx buffer);

    /// <summary>物理内存 (总量 MB, 可用 MB)。失败返回 (0, 0)。</summary>
    public static (long TotalMb, long AvailableMb) GetMemoryMb()
    {
        try
        {
            var status = new MemoryStatusEx { Length = (uint)Marshal.SizeOf<MemoryStatusEx>() };
            if (GlobalMemoryStatusEx(ref status))
                return ((long)(status.TotalPhys / (1024 * 1024)), (long)(status.AvailPhys / (1024 * 1024)));
        }
        catch { }
        return (0, 0);
    }
}

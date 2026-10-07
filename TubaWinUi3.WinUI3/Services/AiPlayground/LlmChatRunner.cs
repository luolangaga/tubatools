#if !AI_PLAYGROUND_X86
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Channels;
using Microsoft.ML.OnnxRuntimeGenAI;

namespace TubaWinUi3.Services.AiPlayground;

/// <summary>
/// 本地 LLM 对话（ONNX Runtime GenAI）：加载 genai 模型目录、流式生成（KV Cache 由 GenAI 管理）、
/// 停止与卸载。WinML 变体经 Windows ML 自动选择硬件（NPU → GPU → CPU）。
/// 提示词构造见 <see cref="LlmPromptBuilder"/>。
/// </summary>
public sealed class LlmChatRunner : IDisposable
{
    private static readonly object OgaGate = new();
    private static OgaHandle? _ogaHandle;

    private Model? _model;
    private Tokenizer? _tokenizer;
    private TokenizerStream? _stream;
    private readonly SemaphoreSlim _generateLock = new(1, 1);

    public bool IsLoaded => _model is not null;

    /// <summary>本次加载实际使用的设备说明（如 "GPU（DmlExecutionProvider）"）。</summary>
    public string RuntimeNote { get; private set; } = "Windows ML 自动选择（NPU → GPU → CPU）";

    /// <summary>
    /// 上下文预算（prompt + 生成的总 token 上限）。由模型 KV 尺寸与可用内存算出，
    /// 用于夹紧 <c>max_length</c>——ORT GenAI 按 max_length 一次性预分配 KV cache，
    /// 超量会直接分配失败（BFCArena / GroupQueryAttention 数十 GB）。
    /// </summary>
    public int ContextBudgetTokens { get; private set; } = LlmContextBudget.DefaultBudgetTokens;

    /// <summary>
    /// 用户在界面上自定义的上下文上限（token，0/负 = 自动）。设置后作为上限，
    /// 但永不突破内存自动值（防 OOM），最终值见 <see cref="ContextBudgetTokens"/>。
    /// </summary>
    public int ContextTokensOverride { get; set; }

    /// <summary>ORT GenAI 要求进程级持有 OgaHandle 直至退出。</summary>
    public static void EnsureOgaHandle()
    {
        lock (OgaGate)
        {
            _ogaHandle ??= new OgaHandle();
        }
    }

    /// <summary>
    /// 加载模型目录（含 genai_config.json）。耗时较长，调用方应在后台线程执行。
    /// provider 非空时显式指定执行提供程序（NPU/GPU），失败自动回退 GenAI 默认。
    /// <paramref name="autoNote"/> 为未指定设备（或指定失败回退）时如实展示的说明。
    /// </summary>
    public void Load(string modelDir, string? provider = null, string? deviceType = null, string? autoNote = null)
    {
        Unload();
        EnsureOgaHandle();

        ContextBudgetTokens = LlmContextBudget.ResolveEffectiveBudget(
            ResolveContextBudget(modelDir), ContextTokensOverride);

        Model model;
        if (!string.IsNullOrEmpty(provider))
        {
            try
            {
                using var config = new Config(modelDir);
                config.ClearProviders();
                config.AppendProvider(provider);
                if (!string.IsNullOrEmpty(deviceType))
                    config.SetDecoderProviderOptionsHardwareDeviceType(provider, deviceType);
                model = new Model(config);
                RuntimeNote = $"{deviceType ?? provider}（{provider}）";
            }
            catch
            {
                // 指定设备不可用（模型/EP 不兼容等）→ 回退 GenAI 默认，如实标注
                model = new Model(modelDir);
                RuntimeNote = (autoNote ?? "自动") + " · 指定设备失败已回退";
            }
        }
        else
        {
            model = new Model(modelDir);
            RuntimeNote = autoNote ?? "自动（NPU → GPU → CPU）";
        }

        try
        {
            _tokenizer = new Tokenizer(model);
            _stream = _tokenizer.CreateStream();
            _model = model;
        }
        catch
        {
            model.Dispose();
            throw;
        }
    }

    public void Unload()
    {
        try { _stream?.Dispose(); } catch { }
        try { _tokenizer?.Dispose(); } catch { }
        try { _model?.Dispose(); } catch { }
        _stream = null;
        _tokenizer = null;
        _model = null;
    }

    /// <summary>
    /// 计算该模型的上下文预算：从 genai_config.json 读解码器维度得每 token KV 尺寸，
    /// 按可用物理内存的固定比例折算 token 数（纯逻辑见 <see cref="LlmContextBudget"/>）。
    /// </summary>
    private static int ResolveContextBudget(string modelDir)
    {
        try
        {
            var configPath = Path.Combine(modelDir, "genai_config.json");
            if (!File.Exists(configPath)) return LlmContextBudget.DefaultBudgetTokens;

            if (!LlmContextBudget.TryParseDecoderDims(
                    File.ReadAllText(configPath), out var layers, out var kvHeads, out var headSize))
                return LlmContextBudget.DefaultBudgetTokens;

            var kvBytesPerToken = LlmContextBudget.KvBytesPerToken(layers, kvHeads, headSize);
            var available = GetAvailableMemoryBytes();
            return LlmContextBudget.ComputeBudgetTokens(kvBytesPerToken, available);
        }
        catch
        {
            return LlmContextBudget.DefaultBudgetTokens;
        }
    }

    /// <summary>可用物理内存（GlobalMemoryStatusEx；失败时退回 GC 上报值，再兜底 4GB）。</summary>
    private static long GetAvailableMemoryBytes()
    {
        try
        {
            var status = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (GlobalMemoryStatusEx(ref status))
            {
                var avail = (long)status.ullAvailPhys;
                if (avail > 0) return avail;
            }
        }
        catch { }

        try
        {
            var gc = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
            if (gc > 0) return gc;
        }
        catch { }

        return 4L << 30;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    /// <summary>
    /// 流式生成。temperature ≤ 0.01 视为贪心；maxNewTokens 为新增 token 上限。
    /// </summary>
    public async IAsyncEnumerable<string> GenerateAsync(
        string prompt,
        double temperature,
        double topP,
        int maxNewTokens,
        [EnumeratorCancellation] CancellationToken ct)
    {
        if (_model is null || _tokenizer is null || _stream is null)
            throw new InvalidOperationException("模型尚未加载。");

        await _generateLock.WaitAsync(ct).ConfigureAwait(false);
        var channel = Channel.CreateUnbounded<string>(
            new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });

        // 上下文预算：prompt + 生成都算在 max_length 内。
        // 两处硬约束：(1) ORT 按 max_length **一次性预分配** KV cache，超量直接分配失败（数十 GB）；
        // (2) ORT 校验 prompt 长度用的是**真实分词数**，不是字符/估算数——估算偏差会让
        //     真实 prompt 超过 max_length（"input_ids size … exceeds max length"）。
        // 因此这里一律用真实 tokenizer 计数。
        var tokenizer = _tokenizer;
        var budget = ContextBudgetTokens;

        int CountTokens(string text)
        {
            if (string.IsNullOrEmpty(text)) return 0;
            using var seq = tokenizer.Encode(text);
            return seq[0].Length;
        }

        prompt = LlmContextBudget.TruncateToTokens(
            prompt, Math.Max(256, budget - 32), CountTokens, out _);
        var promptTokens = Math.Max(1, CountTokens(prompt));
        var roomForNew = Math.Max(16, budget - promptTokens);
        var effectiveMaxNew = Math.Min(Math.Clamp(maxNewTokens, 16, 8192), roomForNew);

        var loop = Task.Run(() =>
        {
            try
            {
                using var generatorParams = new GeneratorParams(_model);
                // max_length 为总长度（真实 prompt 分词数 + 生成上限），两者都夹在预算内
                generatorParams.SetSearchOption(
                    "max_length", (double)(promptTokens + effectiveMaxNew));
                if (temperature <= 0.01)
                {
                    generatorParams.SetSearchOption("do_sample", false);
                }
                else
                {
                    generatorParams.SetSearchOption("temperature", temperature);
                    generatorParams.SetSearchOption("top_p", Math.Clamp(topP, 0.05, 1.0));
                }

                using var sequences = _tokenizer.Encode(prompt);
                using var generator = new Generator(_model, generatorParams);
                generator.AppendTokenSequences(sequences);

                var soFar = new StringBuilder();
                while (!generator.IsDone())
                {
                    ct.ThrowIfCancellationRequested();
                    generator.GenerateNextToken();
                    var sequence = generator.GetSequence(0);
                    var piece = _stream.Decode(sequence[^1]);
                    if (string.IsNullOrEmpty(piece)) continue;
                    soFar.Append(piece);
                    if (LlmPromptBuilder.FindStopMarker(soFar) >= 0) break;
                    channel.Writer.TryWrite(piece);
                }
                channel.Writer.TryComplete();
            }
            catch (OperationCanceledException)
            {
                channel.Writer.TryComplete();
            }
            catch (Exception ex)
            {
                channel.Writer.TryComplete(ex);
            }
            finally
            {
                _generateLock.Release();
            }
        }, CancellationToken.None);

        try
        {
            await foreach (var piece in channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
            {
                yield return piece;
            }
        }
        finally
        {
            try { await loop.ConfigureAwait(false); } catch { }
        }
    }

    public void Dispose()
    {
        Unload();
        _generateLock.Dispose();
    }
}
#else
using System.Runtime.CompilerServices;

namespace TubaWinUi3.Services.AiPlayground;

/// <summary>
/// x86 编译桩：Windows ML 运行时与 ORT GenAI 均无 32 位原生资产，
/// 页面已按平台门槛（<see cref="AiRuntimeService.UnsupportedReason"/>）禁用推理入口。
/// </summary>
public sealed class LlmChatRunner : IDisposable
{
    private const string Message = "当前是 32 位（x86）版本，Windows ML 本地推理不可用。";

    public bool IsLoaded => false;
    public string RuntimeNote => "x86 版本不支持本地推理";

    public static void EnsureOgaHandle() { }

    public void Load(string modelDir, string? provider = null, string? deviceType = null, string? autoNote = null)
        => throw new PlatformNotSupportedException(Message);

    public void Unload() { }

    public async IAsyncEnumerable<string> GenerateAsync(
        string prompt,
        double temperature,
        double topP,
        int maxNewTokens,
        [EnumeratorCancellation] CancellationToken ct)
    {
        await Task.Yield();
        yield return Throw();
    }

    private static string Throw() => throw new PlatformNotSupportedException(Message);

    public void Dispose() { }
}
#endif

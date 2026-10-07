using TubaWinUi3.Services.AiPlayground;

namespace TubaWinUi3.Services.Ai.Onnx;

/// <summary>
/// 进程级本地模型运行时：持有一个 <see cref="LlmChatRunner"/> 与当前已加载的模型 Id。
/// AI 助手选中「本地模型」提供商时经此加载/推理；换模型时自动卸载重载。
/// </summary>
public static class LocalModelRuntime
{
    private static readonly object Gate = new();
    private static LlmChatRunner? _runner;
    private static string? _loadedModelId;

    /// <summary>当前已加载的模型 Id（null = 未加载）。</summary>
    public static string? LoadedModelId
    {
        get { lock (Gate) return _loadedModelId; }
    }

    /// <summary>本次加载实际使用的设备说明（如 "CPU"）。</summary>
    public static string RuntimeNote
    {
        get { lock (Gate) return _runner?.RuntimeNote ?? ""; }
    }

    /// <summary>
    /// 确保指定模型已加载（同步，耗时较长——调用方应在后台线程执行）。
    /// 平台不支持或模型不可用时抛 <see cref="InvalidOperationException"/>（含友好文案）。
    /// </summary>
    public static void EnsureLoaded(string modelId)
    {
        lock (Gate)
        {
            if (_runner is not null && _loadedModelId == modelId && _runner.IsLoaded)
                return;

            if (AiRuntimeService.UnsupportedReason is { } reason)
                throw new InvalidOperationException(reason);

            var entry = AiModelLibrary.GetAllEntries().FirstOrDefault(e => e.Id == modelId)
                ?? throw new InvalidOperationException($"本地模型 '{modelId}' 不存在，请在「本地 AI 试炼场」重新选择。");

            var status = AiModelLibrary.GetStatus(entry);
            if (!status.AllReady)
                throw new InvalidOperationException($"本地模型尚未就绪（{status.Describe()}），请先在「本地 AI 试炼场」完成下载或导入。");

            var dir = AiModelLibrary.GetEntryDataDir(entry);
            var (provider, deviceType, note) = AiRuntimeService.ResolveLlmTarget(null, null);

            var runner = new LlmChatRunner();
            try
            {
                runner.Load(dir, provider, deviceType, note);
            }
            catch
            {
                runner.Dispose();
                throw;
            }

            _runner?.Dispose();
            _runner = runner;
            _loadedModelId = modelId;
        }
    }

    /// <summary>流式生成（要求已 <see cref="EnsureLoaded"/>）。</summary>
    public static IAsyncEnumerable<string> GenerateAsync(
        string prompt, double temperature, double topP, int maxNewTokens, CancellationToken ct)
    {
        LlmChatRunner runner;
        lock (Gate)
        {
            runner = _runner ?? throw new InvalidOperationException("本地模型尚未加载。");
        }
        return runner.GenerateAsync(prompt, temperature, topP, maxNewTokens, ct);
    }

    /// <summary>取指定对话模型的提示词格式（无则 Phi 默认）。</summary>
    public static AiPromptFormat GetPromptFormat(string modelId)
    {
        var entry = AiModelLibrary.GetAllEntries().FirstOrDefault(e => e.Id == modelId);
        return entry?.Custom?.PromptFormat ?? entry?.Preset?.PromptFormat ?? AiPromptFormat.Phi;
    }

    /// <summary>卸载并释放（数据目录切换 / 应用退出时调用）。</summary>
    public static void Unload()
    {
        lock (Gate)
        {
            _runner?.Dispose();
            _runner = null;
            _loadedModelId = null;
        }
    }
}

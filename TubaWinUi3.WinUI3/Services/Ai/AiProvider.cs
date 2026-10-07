using System.Text.Json.Serialization;

namespace TubaWinUi3.Services.Ai;

/// <summary>提供商种类：云端 OpenAI 兼容端点，或进程内本地 ONNX 模型。</summary>
public enum ProviderKind
{
    /// <summary>OpenAI 兼容 HTTP 端点（默认）。</summary>
    OpenAi,

    /// <summary>进程内本地 ONNX 模型（由「本地 AI 试炼场」的模型库提供）。</summary>
    Local,
}

/// <summary>
/// 一个可用的模型选项（提供商下的预设/自定义模型）。
/// </summary>
public sealed class AiModelOption
{
    /// <summary>模型标识符（请求体 model 字段，如 deepseek-v4-pro）。</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    /// <summary>可选显示名（为空时直接显示 Id）。</summary>
    [JsonPropertyName("label")]
    public string? Label { get; set; }

    /// <summary>UI 展示文本。</summary>
    [JsonIgnore]
    public string DisplayText => string.IsNullOrWhiteSpace(Label) ? Id : $"{Label} ({Id})";

    public AiModelOption() { }

    public AiModelOption(string id, string? label = null)
    {
        Id = id;
        Label = label;
    }
}

/// <summary>
/// 一个 AI 服务提供商：API 地址 + Key + 可预设多个模型。
/// 内置预设（DeepSeek / 小米 MiMo / OpenCode Zen）锁定地址与名称，只填 Key 即可用；
/// 自定义提供商可完全编辑。
/// </summary>
public sealed class AiProvider
{
    /// <summary>稳定标识（custom / deepseek / mimo / opencode / custom-N）。</summary>
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    /// <summary>显示名称。</summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>提供商种类（缺省 OpenAi，向后兼容旧配置）。</summary>
    [JsonPropertyName("kind")]
    public ProviderKind Kind { get; set; } = ProviderKind.OpenAi;

    /// <summary>OpenAI 兼容 Base URL（不含 /chat/completions）。</summary>
    [JsonPropertyName("baseUrl")]
    public string BaseUrl { get; set; } = "";

    /// <summary>API Key（OpenCode Zen 登录后自动写入登录 token）。</summary>
    [JsonPropertyName("apiKey")]
    public string ApiKey { get; set; } = "";

    /// <summary>是否内置预设（预设锁定名称与地址，仅可改 Key 与模型列表）。</summary>
    [JsonPropertyName("isPreset")]
    public bool IsPreset { get; set; }

    /// <summary>地址是否锁定（预设锁定，自定义可编辑）。</summary>
    [JsonPropertyName("endpointLocked")]
    public bool EndpointLocked { get; set; }

    /// <summary>「获取 API Key」跳转的平台地址（为空则不显示）。</summary>
    [JsonPropertyName("keyHintUrl")]
    public string KeyHintUrl { get; set; } = "";

    /// <summary>默认模型 Id（切换提供商时自动选中）。</summary>
    [JsonPropertyName("defaultModel")]
    public string DefaultModel { get; set; } = "";

    /// <summary>模型列表（可在聊天界面切换）。</summary>
    [JsonPropertyName("models")]
    public List<AiModelOption> Models { get; set; } = [];

    /// <summary>添加一个模型（去重）。</summary>
    public void AddModel(string id, string? label = null)
    {
        id = id.Trim();
        if (id.Length == 0) return;
        if (Models.Any(m => m.Id.Equals(id, StringComparison.OrdinalIgnoreCase))) return;
        Models.Add(new AiModelOption(id, label));
    }
}

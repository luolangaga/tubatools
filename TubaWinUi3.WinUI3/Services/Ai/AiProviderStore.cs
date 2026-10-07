using System.Text.Json;
using System.Text.Json.Serialization;
using TubaWinUi3.Services;
using TubaWinUi3.Services.AiPlayground;

namespace TubaWinUi3.Services.Ai;

/// <summary>
/// AI 提供商配置存储：管理 ai_providers.json（内置预设 + 用户自定义），
/// 以及当前选中的提供商/模型（聊天界面切换即持久化到这里）。
/// 兼容旧版扁平配置：首次加载时把 AiApiEndpoint / AiApiKey / AiModelName 迁移进 custom 提供商。
/// </summary>
public static class AiProviderStore
{
    public const string CustomProviderId = "custom";
    public const string DeepSeekProviderId = "deepseek";
    public const string MiMoProviderId = "mimo";
    public const string OpenCodeZenProviderId = "opencode";
    /// <summary>进程内本地 ONNX 模型提供商（模型列表由「本地 AI 试炼场」模型库派生）。</summary>
    public const string LocalProviderId = "local";

    /// <summary>内置预设提供商 Id（不可删除、名称由「恢复默认」复位）。</summary>
    public static bool IsPresetProviderId(string id) =>
        id is CustomProviderId or DeepSeekProviderId or MiMoProviderId or OpenCodeZenProviderId or LocalProviderId;

    /// <summary>OpenCode Zen 免费模型的兜底种子列表（首次使用、未登录刷新时也能选）。</summary>
    public static readonly string[] OpenCodeZenSeedFreeModels =
    [
        "deepseek-v4-flash-free",
        "mimo-v2.5-free",
        "hy3-free",
        "nemotron-3-ultra-free",
        "nemotron-3.5-lightning-free",
        "ling-3.0-tiny-free",
        "laguna-s-2.1-free",
        "big-pickle",
    ];

    private sealed class StoreFile
    {
        [JsonPropertyName("version")] public int Version { get; set; } = 1;
        [JsonPropertyName("selectedProviderId")] public string SelectedProviderId { get; set; } = CustomProviderId;
        [JsonPropertyName("selectedModelId")] public string SelectedModelId { get; set; } = "";
        /// <summary>默认模型是否已归位到小图吧自带模型（一次性标记，之后用户手动切换不再被强制覆盖）。</summary>
        [JsonPropertyName("defaultTubaMigrated")] public bool DefaultTubaMigrated { get; set; }
        [JsonPropertyName("providers")] public List<AiProvider> Providers { get; set; } = [];
    }

    private static readonly object _lock = new();
    private static StoreFile? _cache;

    /// <summary>测试用存储路径覆盖（null = 使用真实数据目录）。</summary>
    internal static string? StoragePathOverride { get; set; }

    /// <summary>测试用旧配置读取器覆盖（默认读 AppSettings）。</summary>
    internal static Func<string, string?> LegacyGet { get; set; } = AppSettings.Get;

    private static string FilePath =>
        StoragePathOverride ?? ConfigManager.GetAiProvidersPath();

    /// <summary>全部提供商（已加载的副本，修改后需调用 <see cref="Save"/>）。</summary>
    public static IReadOnlyList<AiProvider> GetProviders()
    {
        lock (_lock)
        {
            EnsureLoaded();
            return _cache!.Providers;
        }
    }

    public static AiProvider? GetProvider(string id)
        => GetProviders().FirstOrDefault(p => p.Id == id);

    public static string SelectedProviderId
    {
        get
        {
            lock (_lock)
            {
                EnsureLoaded();
                return _cache!.SelectedProviderId;
            }
        }
    }

    /// <summary>当前选中的提供商（不存在时回退 custom）。</summary>
    public static AiProvider SelectedProvider
    {
        get
        {
            var id = SelectedProviderId;
            return GetProvider(id) ?? GetProvider(CustomProviderId) ?? GetProviders()[0];
        }
    }

    /// <summary>当前选中的模型 Id（不存在于模型列表时回退提供商默认模型）。</summary>
    public static string SelectedModelId
    {
        get
        {
            lock (_lock)
            {
                EnsureLoaded();
                return ResolveSelectedModel(_cache!.SelectedProviderId, _cache.SelectedModelId);
            }
        }
    }

    /// <summary>切换选中的提供商与模型（null 模型 = 使用该提供商默认模型）。</summary>
    public static void SetSelected(string providerId, string? modelId = null)
    {
        lock (_lock)
        {
            EnsureLoaded();
            var provider = GetProvider(providerId);
            if (provider is null) return;
            _cache!.SelectedProviderId = providerId;
            _cache.SelectedModelId = modelId ?? provider.DefaultModel;
            Save();
        }
    }

    /// <summary>新建一个空的自定义提供商（地址/模型全部留空，由用户自行填写），并选中它。</summary>
    public static AiProvider AddCustomProvider(string? name = null)
    {
        lock (_lock)
        {
            EnsureLoaded();
            var id = CustomProviderId;
            var n = 1;
            while (_cache!.Providers.Any(p => p.Id == id))
                id = $"{CustomProviderId}-{++n}";

            var provider = new AiProvider
            {
                Id = id,
                Name = name ?? $"自定义 {n}",
                BaseUrl = "",
                IsPreset = false,
                EndpointLocked = false,
                DefaultModel = "",
                Models = [],
            };
            _cache.Providers.Add(provider);
            _cache.SelectedProviderId = id;
            _cache.SelectedModelId = "";
            Save();
            return provider;
        }
    }

    /// <summary>
    /// 删除一个自定义提供商（预设不可删）。若删除的是当前选中项则回退到「小图吧自带模型」，
    /// 且永远至少保留一个提供商。返回是否删除成功。
    /// </summary>
    public static bool DeleteProvider(string providerId)
    {
        lock (_lock)
        {
            EnsureLoaded();
            if (IsPresetProviderId(providerId)) return false;

            var provider = GetProvider(providerId);
            if (provider is null) return false;

            _cache!.Providers.Remove(provider);
            if (_cache.Providers.Count == 0)
            {
                if (CreatePreset(CustomProviderId) is { } fallback)
                    _cache.Providers.Add(fallback);
            }

            if (_cache.SelectedProviderId == providerId)
            {
                var target = GetProvider(CustomProviderId) ?? _cache.Providers[0];
                _cache.SelectedProviderId = target.Id;
                _cache.SelectedModelId = target.DefaultModel;
            }

            _cache.SelectedModelId = ResolveSelectedModel(_cache.SelectedProviderId, _cache.SelectedModelId);
            Save();
            return true;
        }
    }

    /// <summary>重命名一个自定义提供商（预设名称由「恢复默认」复位，不允许改名）。返回是否成功。</summary>
    public static bool RenameProvider(string providerId, string name)
    {
        lock (_lock)
        {
            EnsureLoaded();
            if (IsPresetProviderId(providerId)) return false;

            var provider = GetProvider(providerId);
            var trimmed = name.Trim();
            if (provider is null || trimmed.Length == 0) return false;

            provider.Name = trimmed;
            Save();
            return true;
        }
    }

    /// <summary>
    /// 同步「本地模型」提供商的模型列表（单一事实来源 = AiModelLibrary 的对话模型）。
    /// 无可用模型时列表为空。返回该提供商的可用模型数。
    /// </summary>
    public static int SyncLocalProviderModels()
    {
        lock (_lock)
        {
            EnsureLoaded();
            var local = GetProvider(LocalProviderId);
            if (local is null) return 0;

            var models = AiModelLibrary.GetAllEntries()
                .Where(e => e.Task == AiTaskKind.Chat && AiModelLibrary.GetStatus(e).AllReady)
                .Select(e => new AiModelOption(e.Id, e.DisplayName))
                .ToList();

            local.Kind = ProviderKind.Local;
            local.Models = models;

            if (!models.Any(m => m.Id.Equals(local.DefaultModel, StringComparison.OrdinalIgnoreCase)))
                local.DefaultModel = models.FirstOrDefault()?.Id ?? "";

            _cache!.SelectedModelId = ResolveSelectedModel(_cache.SelectedProviderId, _cache.SelectedModelId);
            Save();
            return models.Count;
        }
    }

    /// <summary>恢复提供商的预设默认（模型列表/默认模型/地址），保留 API Key。</summary>
    public static void ResetProviderDefaults(string providerId)
    {
        lock (_lock)
        {
            EnsureLoaded();
            var provider = GetProvider(providerId);
            if (provider is null) return;

            var preset = CreatePreset(providerId);
            if (preset is null)
            {
                // 自定义提供商：完全清空（地址、模型），由用户自行填写
                provider.BaseUrl = "";
                provider.Models = [];
                provider.DefaultModel = "";
            }
            else
            {
                var key = provider.ApiKey;
                provider.Name = preset.Name;
                provider.BaseUrl = preset.BaseUrl;
                provider.EndpointLocked = preset.EndpointLocked;
                provider.KeyHintUrl = preset.KeyHintUrl;
                provider.DefaultModel = preset.DefaultModel;
                provider.Models = preset.Models;
                provider.IsPreset = true;
                provider.ApiKey = key;
            }

            _cache!.SelectedModelId = ResolveSelectedModel(providerId, _cache.SelectedModelId);
            Save();
        }
    }

    /// <summary>OpenCode Zen 免费模型过滤规则：-free 后缀或 big-pickle。</summary>
    public static bool IsFreeModelId(string modelId)
    {
        var id = modelId.Trim();
        return id.Equals("big-pickle", StringComparison.OrdinalIgnoreCase)
            || id.EndsWith("-free", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>解析当前选中提供商的实际请求配置（endpoint/model/key，可能含空值，由 AiService 补默认）。</summary>
    public static (string Endpoint, string Model, string ApiKey) GetSelectedConfig()
    {
        var provider = SelectedProvider;
        var model = SelectedModelId;
        var endpoint = provider.BaseUrl?.Trim() ?? "";
        var key = provider.ApiKey?.Trim() ?? "";

        // OpenCode Zen 未登录/无 Key 时使用匿名 key "public"（官方约定：免费模型匿名可用）
        if (provider.Id == OpenCodeZenProviderId && key.Length == 0)
            key = "public";

        return (endpoint, model, key);
    }

    /// <summary>保存到磁盘（失败静默，与 AppSettings 一致）。</summary>
    public static void Save()
    {
        lock (_lock)
        {
            if (_cache is null) return;
            try
            {
                var dir = Path.GetDirectoryName(FilePath)!;
                Directory.CreateDirectory(dir);
                var json = JsonSerializer.Serialize(_cache, JsonOpts);
                File.WriteAllText(FilePath, json);
            }
            catch { }
        }
    }

    /// <summary>清空缓存（测试用 / 数据目录切换后）。</summary>
    public static void InvalidateCache()
    {
        lock (_lock) _cache = null;
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
    };

    private static void EnsureLoaded()
    {
        if (_cache is not null) return;

        StoreFile? file = null;
        try
        {
            if (File.Exists(FilePath))
                file = JsonSerializer.Deserialize<StoreFile>(File.ReadAllText(FilePath), JsonOpts);
        }
        catch { }

        if (file is not null && file.Providers.Count > 0)
        {
            _cache = file;

            // 老配置补入「本地模型」提供商（此前版本没有此项）
            if (_cache.Providers.All(p => p.Id != LocalProviderId))
            {
                if (CreatePreset(LocalProviderId) is { } localPreset)
                    _cache.Providers.Add(localPreset);
                Save();
            }

            var fileCustom = _cache.Providers.FirstOrDefault(p => p.Id == CustomProviderId);

            // 旧版本名「自定义 (OpenAI 兼容)」→「小图吧自带模型」
            if (fileCustom is not null && fileCustom.Name == "自定义 (OpenAI 兼容)")
            {
                fileCustom.Name = "小图吧自带模型";
                Save();
            }

            // 清理旧版登录器自动写入的 OAuth token（不可用作 zen API Key，会导致 Invalid API key）
            var staleToken = AppSettings.Get("OpenCodeZenToken");
            if (!string.IsNullOrWhiteSpace(staleToken))
            {
                var zenProvider = _cache.Providers.FirstOrDefault(p => p.Id == OpenCodeZenProviderId);
                if (zenProvider is not null && zenProvider.ApiKey == staleToken)
                    zenProvider.ApiKey = "";
                AppSettings.Remove("OpenCodeZenToken");
                AppSettings.Remove("OpenCodeZenRefreshToken");
                AppSettings.Remove("OpenCodeZenExpiresAt");
                AppSettings.Remove("OpenCodeZenEmail");
                Save();
            }
            // 一次性迁移：默认模型归位到「小图吧自带模型」。
            // 仍停留在自动默认状态（旧版自动迁移留下的 OpenCode Zen 默认免费模型 / 未配置的空白自定义）
            // 且从未手动填写过提供商配置的用户，切回小图吧内置端点；手动配置过的用户不受影响。
            // 只执行一次（defaultTubaMigrated 标记），之后用户手动切换不会再被强制覆盖。
            if (!_cache.DefaultTubaMigrated)
            {
                _cache.DefaultTubaMigrated = true;
                var tubaCustom = _cache.Providers.FirstOrDefault(p => p.Id == CustomProviderId);
                var selected = _cache.Providers.FirstOrDefault(p => p.Id == _cache.SelectedProviderId);
                if (tubaCustom is not null && selected is not null)
                {
                    var isZenDefault = selected.Id == OpenCodeZenProviderId
                        && _cache.SelectedModelId == selected.DefaultModel
                        && string.IsNullOrWhiteSpace(selected.ApiKey);
                    var isBlankCustom = selected.Id != OpenCodeZenProviderId
                        && string.IsNullOrWhiteSpace(selected.BaseUrl)
                        && string.IsNullOrWhiteSpace(selected.ApiKey);
                    if (isZenDefault || isBlankCustom)
                    {
                        _cache.SelectedProviderId = CustomProviderId;
                        _cache.SelectedModelId = tubaCustom.DefaultModel;
                    }
                }
                Save();
            }
            return;
        }

        _cache = new StoreFile();
        foreach (var id in new[] { CustomProviderId, DeepSeekProviderId, MiMoProviderId, OpenCodeZenProviderId, LocalProviderId })
        {
            if (CreatePreset(id) is { } preset)
                _cache.Providers.Add(preset);
        }

        // 旧版扁平配置迁移 → custom 提供商（AppSettings 键：AiApiEndpoint / AiModelName / AiApiKey）
        var custom = _cache.Providers.First(p => p.Id == CustomProviderId);
        var legacyEndpoint = LegacyGet("AiApiEndpoint")?.Trim() ?? "";
        var legacyModel = LegacyGet("AiModelName")?.Trim() ?? "";
        var legacyKey = LegacyGet("AiApiKey")?.Trim() ?? "";
        var legacyConfigured = legacyEndpoint.Length > 0 || legacyModel.Length > 0 || legacyKey.Length > 0;
        if (legacyEndpoint.Length > 0) custom.BaseUrl = legacyEndpoint;
        if (legacyKey.Length > 0) custom.ApiKey = legacyKey;
        if (legacyModel.Length > 0)
        {
            custom.DefaultModel = legacyModel;
            if (!custom.Models.Any(m => m.Id == legacyModel))
                custom.Models.Insert(0, new AiModelOption(legacyModel));
            _cache.SelectedModelId = legacyModel;
        }

        if (legacyConfigured)
        {
            // 旧版已配置 → 选中 custom 提供商（行为与旧版一致）
            _cache.SelectedProviderId = CustomProviderId;
        }
        else
        {
            // 全新安装/未配置 → 默认使用小图吧自带模型（内置端点，无需 Key）
            _cache.SelectedProviderId = CustomProviderId;
            _cache.SelectedModelId = custom.DefaultModel;
        }

        // 全新文件即默认小图吧自带模型，标记已迁移（避免后续被重复迁移打扰）
        _cache.DefaultTubaMigrated = true;
        Save();
    }

    private static AiProvider? CreatePreset(string id)
    {
        switch (id)
        {
            case CustomProviderId:
                return new AiProvider
                {
                    Id = id,
                    Name = "小图吧自带模型",
                    BaseUrl = "",
                    IsPreset = true,
                    EndpointLocked = false,
                    KeyHintUrl = "",
                    DefaultModel = AiService.DefaultModel,
                    Models = [new AiModelOption(AiService.DefaultModel, "自动")],
                };
            case DeepSeekProviderId:
                return new AiProvider
                {
                    Id = id,
                    Name = "DeepSeek",
                    BaseUrl = "https://api.deepseek.com",
                    IsPreset = true,
                    EndpointLocked = true,
                    KeyHintUrl = "https://platform.deepseek.com/api_keys",
                    DefaultModel = "deepseek-v4-pro",
                    Models =
                    [
                        new AiModelOption("deepseek-v4-pro", "DeepSeek V4 Pro"),
                        new AiModelOption("deepseek-v4-flash", "DeepSeek V4 Flash"),
                    ],
                };
            case MiMoProviderId:
                return new AiProvider
                {
                    Id = id,
                    Name = "小米 MiMo",
                    BaseUrl = "https://api.xiaomimimo.com/v1",
                    IsPreset = true,
                    EndpointLocked = true,
                    KeyHintUrl = "https://platform.xiaomimimo.com/#/console/api-keys",
                    DefaultModel = "mimo-v2.5-pro",
                    Models = [new AiModelOption("mimo-v2.5-pro", "MiMo V2.5 Pro")],
                };
            case OpenCodeZenProviderId:
                var seed = OpenCodeZenSeedFreeModels
                    .Select(m => new AiModelOption(m))
                    .ToList();
                return new AiProvider
                {
                    Id = id,
                    Name = "OpenCode Zen",
                    BaseUrl = "https://opencode.ai/zen/v1",
                    IsPreset = true,
                    EndpointLocked = true,
                    KeyHintUrl = "",
                    DefaultModel = OpenCodeZenSeedFreeModels[0],
                    Models = seed,
                };
            case LocalProviderId:
                return new AiProvider
                {
                    Id = id,
                    Name = "本地模型",
                    Kind = ProviderKind.Local,
                    IsPreset = true,
                    EndpointLocked = true,
                    KeyHintUrl = "",
                    DefaultModel = "",
                    Models = [],
                };
            default:
                return null;
        }
    }

    private static string ResolveSelectedModel(string providerId, string? selectedModelId)
    {
        var provider = GetProvider(providerId) ?? GetProvider(CustomProviderId);
        if (provider is null) return "";

        if (!string.IsNullOrWhiteSpace(selectedModelId) &&
            provider.Models.Any(m => m.Id.Equals(selectedModelId, StringComparison.OrdinalIgnoreCase)))
        {
            return selectedModelId;
        }

        if (!string.IsNullOrWhiteSpace(provider.DefaultModel) &&
            provider.Models.Any(m => m.Id.Equals(provider.DefaultModel, StringComparison.OrdinalIgnoreCase)))
        {
            return provider.DefaultModel;
        }

        return provider.Models.FirstOrDefault()?.Id ?? "";
    }
}

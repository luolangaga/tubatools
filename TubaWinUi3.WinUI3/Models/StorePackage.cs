using System.Text.Json.Serialization;

namespace TubaWinUi3.Models;

/// <summary>
/// 正版软件商店 — 分类
/// </summary>
public sealed class StoreCategory
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("glyph")]
    public string Glyph { get; init; } = "";

    [JsonPropertyName("packages")]
    public List<StorePackage> Packages { get; init; } = [];

    [JsonPropertyName("subCategories")]
    public List<StoreSubCategory>? SubCategories { get; init; }
}

/// <summary>
/// 正版软件商店 — 子分类
/// </summary>
public sealed class StoreSubCategory
{
    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("packages")]
    public List<StorePackage> Packages { get; init; } = [];
}

/// <summary>
/// 正版软件商店 — 软件包
/// </summary>
public sealed class StorePackage
{
    [JsonPropertyName("id")]
    public string Id { get; init; } = "";

    [JsonPropertyName("name")]
    public string Name { get; init; } = "";

    [JsonPropertyName("desc")]
    public string? Description { get; init; }

    [JsonPropertyName("recommended")]
    public bool IsRecommended { get; init; }

    [JsonPropertyName("installNotes")]
    public string? InstallNotes { get; init; }

    [JsonPropertyName("downloadUrl")]
    public string? DownloadUrl { get; init; }

    [JsonIgnore]
    public bool IsDirectDownload => Uri.TryCreate(DownloadUrl, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps;

    [JsonPropertyName("unavailableReason")]
    public string? UnavailableReason { get; init; }

    [JsonIgnore]
    public bool CanInstall => string.IsNullOrEmpty(UnavailableReason);

    [JsonIgnore]
    public string ActionLabel => CanInstall ? "安装" : "暂未收录";

    [JsonIgnore]
    public string SourceLabel => IsDirectDownload ? "官方直链 · x64" : CanInstall ? "WinGet 官方源" : "WinGet 暂未收录";

    [JsonIgnore]
    public string Monogram => Name.Length > 0 ? Name[..1].ToUpperInvariant() : "A";

    [JsonIgnore]
    public string AccessibleName => $"{Name}，{Description}，{SourceLabel}";

    // ---- 以下为运行时附加字段，不参与 JSON 反序列化 ----

    [JsonIgnore]
    public string Category { get; set; } = "";

    [JsonIgnore]
    public string Glyph { get; set; } = "";

    [JsonIgnore]
    public bool IsOnlineResult { get; set; }

    /// <summary>安装状态：idle / queued（直链下载） / installing / done / error</summary>
    [JsonIgnore]
    public string InstallState { get; set; } = "idle";

    [JsonIgnore]
    public string? StatusText { get; set; }
}

/// <summary>商店分组；直接作为 CollectionViewSource 的组集合。</summary>
public sealed class StoreSection(string title, string description, IEnumerable<StorePackage> packages)
    : List<StorePackage>(packages)
{
    public string Title { get; } = title;
    public string Description { get; } = description;
}

/// <summary>
/// pcsetup_catalog.json 顶层结构
/// </summary>
public sealed class PcSetupCatalog
{
    [JsonPropertyName("categories")]
    public List<StoreCategory> Categories { get; init; } = [];
}

/// <summary>
/// WinGet REST API 搜索结果项
/// </summary>
public sealed class WingetSearchResult
{
    public string PackageIdentifier { get; init; } = "";
    public string PackageName { get; init; } = "";
    public string? Publisher { get; init; }
    public string? Description { get; init; }
    public string? LatestVersion { get; init; }
}

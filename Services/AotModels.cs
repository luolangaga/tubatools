using System.Text.Json.Serialization;

namespace TubaWinUi3.Services;

public sealed class CpuRankingData
{
    public string LastUpdated { get; set; } = "";
    public string Source { get; set; } = "";
    public List<Models.CpuRankingEntry> Desktop { get; set; } = [];
    public List<Models.CpuRankingEntry> Laptop { get; set; } = [];
}

public sealed class GpuRankingData
{
    public string LastUpdated { get; set; } = "";
    public string Source { get; set; } = "";
    public List<Models.GpuRankingEntry> Desktop { get; set; } = [];
    public List<Models.GpuRankingEntry> Laptop { get; set; } = [];
}

public sealed class KmsRawItem
{
    public string? Host { get; set; }
    public int Port { get; set; }
    public string? Country { get; set; }
    public int ConnectCount { get; set; }
    public int ActivateCount { get; set; }
    public int FailedCount { get; set; }
    public double AverageTime { get; set; }
    public string? LastCheckDate { get; set; }
    public List<KmsRawResult>? Results { get; set; }
}

public sealed class KmsRawResult
{
    public string? Address { get; set; }
    public string? Country { get; set; }
    public double Time { get; set; }
    public bool Result { get; set; }
}

public sealed class CatalogDatabase
{
    [JsonPropertyName("categories")]
    public List<Models.CatalogCategory>? Categories { get; set; }
}

public sealed class JsonToolDatabase
{
    public List<JsonToolMetadata> Tools { get; set; } = [];
}

public sealed class JsonToolMetadata
{
    public string? Match { get; set; }
    public string? Description { get; set; }
    public string? Publisher { get; set; }
    public string? DownloadUrl { get; set; }
    public string? DownloadFilter { get; set; }
    public string? WingetId { get; set; }
    public string? LaunchTarget { get; set; }
    public List<string>? Tags { get; set; }
    public List<JsonArchVariantEntry>? ArchVariants { get; set; }
}

public sealed class JsonArchVariantEntry
{
    public string? File { get; set; }
    public string? Dir { get; set; }
    public string? Arch { get; set; }
}

public sealed class HardwareSpooferBackupItem
{
    public required string KeyPath { get; init; }
    public required string ValueName { get; init; }
    public required Microsoft.Win32.RegistryValueKind Kind { get; init; }
    public required string OriginalValue { get; init; }
}

public sealed class HardwareSpooferBackupData
{
    public List<HardwareSpooferBackupItem> Entries { get; set; } = [];
    public string? GpuBackup { get; set; }
}

public sealed class CommunityToolPluginDto
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
    [JsonPropertyName("version")]
    public string? Version { get; set; }
    [JsonPropertyName("description")]
    public string? Description { get; set; }
    [JsonPropertyName("category")]
    public string Category { get; set; } = "";
    [JsonPropertyName("publisher")]
    public string? Publisher { get; set; }
    [JsonPropertyName("tags")]
    public List<string>? Tags { get; set; }
    [JsonPropertyName("icon")]
    public string? Icon { get; set; }
    [JsonPropertyName("downloadUrl")]
    public string? DownloadUrl { get; set; }
    [JsonPropertyName("downloadFilter")]
    public string? DownloadFilter { get; set; }
    [JsonPropertyName("launchTarget")]
    public string? LaunchTarget { get; set; }
    [JsonPropertyName("archVariants")]
    public List<CommunityArchVariantDto>? ArchVariants { get; set; }
    [JsonPropertyName("author")]
    public string? Author { get; set; }
    [JsonPropertyName("submittedAt")]
    public DateTimeOffset? SubmittedAt { get; set; }
    [JsonPropertyName("homepage")]
    public string? Homepage { get; set; }
    [JsonPropertyName("repoPath")]
    public string? RepoPath { get; set; }
    [JsonPropertyName("file")]
    public string? File { get; set; }
    [JsonPropertyName("fileSha")]
    public string? FileSha { get; set; }
}

public sealed class CommunityArchVariantDto
{
    [JsonPropertyName("file")]
    public required string File { get; init; }
    [JsonPropertyName("arch")]
    public required string Arch { get; init; }
}

public sealed class GitHubForcePushBody
{
    public string Sha { get; set; } = "";
    public bool Force { get; set; } = true;
}

public sealed class GitHubUpdateRefBody
{
    [JsonPropertyName("ref")]
    public string RefName { get; set; } = "";
    public string Sha { get; set; } = "";
}

public sealed class GitHubCreateFileBody
{
    public string Message { get; set; } = "";
    public string Content { get; set; } = "";
    public string Branch { get; set; } = "";
}

public sealed class GitHubDeleteFileBody
{
    public string Message { get; set; } = "";
    public string Sha { get; set; } = "";
    public string Branch { get; set; } = "";
}

public sealed class GitHubCreatePrBody
{
    public string Title { get; set; } = "";
    public string Head { get; set; } = "";
    [JsonPropertyName("base")]
    public string BaseRef { get; set; } = "main";
    public string? Body { get; set; }
}

public sealed class BenchmarkUploadBody
{
    public string Message { get; set; } = "";
    public string Content { get; set; } = "";
    public string Branch { get; set; } = "";
}

public sealed class WebSearchBody
{
    public string Q { get; set; } = "";
}

public sealed class WebMarkdownBody
{
    public string Url { get; set; } = "";
}

public sealed class AiActionExportDto
{
    public string Kind { get; set; } = "";
    public string Description { get; set; } = "";
    public string? Detail { get; set; }
    public string Reason { get; set; } = "";
}

public sealed class AiJunkExportDto
{
    public string Path { get; set; } = "";
    public string Description { get; set; } = "";
    public string Reason { get; set; } = "";
    public string RiskLevel { get; set; } = "";
    public string Category { get; set; } = "";
}

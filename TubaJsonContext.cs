using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using TubaWinUi3.Models;
using TubaWinUi3.Services;

namespace TubaWinUi3;

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(CpuRankingData))]
[JsonSerializable(typeof(List<CpuRankingEntry>))]
[JsonSerializable(typeof(GpuRankingData))]
[JsonSerializable(typeof(List<GpuRankingEntry>))]
[JsonSerializable(typeof(KmsRawItem))]
[JsonSerializable(typeof(List<KmsRawItem>))]
[JsonSerializable(typeof(KmsRawResult))]
[JsonSerializable(typeof(CatalogDatabase))]
[JsonSerializable(typeof(JsonToolDatabase))]
[JsonSerializable(typeof(JsonToolMetadata))]
[JsonSerializable(typeof(JsonArchVariantEntry))]
[JsonSerializable(typeof(HardwareSpooferBackupItem))]
[JsonSerializable(typeof(List<HardwareSpooferBackupItem>))]
[JsonSerializable(typeof(HardwareSpooferBackupData))]
[JsonSerializable(typeof(PerformanceBenchmarkResult))]
[JsonSerializable(typeof(List<PerformanceBenchmarkResult>))]
[JsonSerializable(typeof(BenchmarkReportEntry))]
[JsonSerializable(typeof(ConversationMeta))]
[JsonSerializable(typeof(List<AiChatMessage>))]
[JsonSerializable(typeof(AiToolCallItem))]
[JsonSerializable(typeof(DownloadQueueEntry))]
[JsonSerializable(typeof(List<DownloadQueueEntry>))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(Dictionary<string, List<string>>))]
[JsonSerializable(typeof(Dictionary<string, Dictionary<string, string>>))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(List<JsonElement>))]
[JsonSerializable(typeof(CommunityToolPluginDto))]
[JsonSerializable(typeof(InterCoreLatencyMatrix))]
[JsonSerializable(typeof(AiActionExportDto))]
[JsonSerializable(typeof(List<AiActionExportDto>))]
internal sealed partial class TubaCamelCaseContext : JsonSerializerContext
{
    public static TubaCamelCaseContext Instance => Default;
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.Unspecified,
    PropertyNameCaseInsensitive = true,
    WriteIndented = false)]
[JsonSerializable(typeof(CpuRankingData))]
[JsonSerializable(typeof(GpuRankingData))]
[JsonSerializable(typeof(KmsRawItem))]
[JsonSerializable(typeof(List<KmsRawItem>))]
[JsonSerializable(typeof(CatalogDatabase))]
[JsonSerializable(typeof(JsonToolDatabase))]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(Dictionary<string, List<string>>))]
[JsonSerializable(typeof(Dictionary<string, Dictionary<string, string>>))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(List<DownloadQueueEntry>))]
[JsonSerializable(typeof(DownloadQueueEntry))]
[JsonSerializable(typeof(List<HardwareSpooferBackupItem>))]
[JsonSerializable(typeof(HardwareSpooferBackupData))]
[JsonSerializable(typeof(List<JsonElement>))]
[JsonSerializable(typeof(CommunityToolPluginDto))]
[JsonSerializable(typeof(AiJunkExportDto))]
[JsonSerializable(typeof(List<AiJunkExportDto>))]
[JsonSerializable(typeof(ConversationMeta))]
[JsonSerializable(typeof(List<AiChatMessage>))]
[JsonSerializable(typeof(PerformanceBenchmarkResult))]
[JsonSerializable(typeof(List<PerformanceBenchmarkResult>))]
[JsonSerializable(typeof(BenchmarkReportEntry))]
internal sealed partial class TubaDefaultContext : JsonSerializerContext
{
    public static TubaDefaultContext Instance => Default;
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    WriteIndented = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(CpuRankingData))]
[JsonSerializable(typeof(GpuRankingData))]
[JsonSerializable(typeof(BenchmarkReportEntry))]
[JsonSerializable(typeof(CommunityToolPluginDto))]
[JsonSerializable(typeof(PerformanceBenchmarkResult))]
[JsonSerializable(typeof(List<PerformanceBenchmarkResult>))]
internal sealed partial class TubaCamelCaseIndentedContext : JsonSerializerContext
{
    public static TubaCamelCaseIndentedContext Instance => Default;
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.Unspecified,
    PropertyNameCaseInsensitive = true,
    WriteIndented = true)]
[JsonSerializable(typeof(Dictionary<string, string>))]
[JsonSerializable(typeof(List<string>))]
[JsonSerializable(typeof(JsonToolDatabase))]
[JsonSerializable(typeof(HardwareSpooferBackupData))]
[JsonSerializable(typeof(List<HardwareSpooferBackupItem>))]
[JsonSerializable(typeof(List<DownloadQueueEntry>))]
[JsonSerializable(typeof(ConversationMeta))]
[JsonSerializable(typeof(List<AiChatMessage>))]
[JsonSerializable(typeof(List<JsonElement>))]
[JsonSerializable(typeof(CommunityToolPluginDto))]
[JsonSerializable(typeof(AiJunkExportDto))]
[JsonSerializable(typeof(List<AiJunkExportDto>))]
[JsonSerializable(typeof(PerformanceBenchmarkResult))]
[JsonSerializable(typeof(List<PerformanceBenchmarkResult>))]
[JsonSerializable(typeof(BenchmarkReportEntry))]
[JsonSerializable(typeof(InterCoreLatencyMatrix))]
[JsonSerializable(typeof(JsonNode))]
internal sealed partial class TubaDefaultIndentedContext : JsonSerializerContext
{
    public static TubaDefaultIndentedContext Instance => Default;
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.Unspecified,
    PropertyNameCaseInsensitive = true,
    WriteIndented = false,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(CommunityToolPluginDto))]
[JsonSerializable(typeof(GitHubForcePushBody))]
[JsonSerializable(typeof(GitHubUpdateRefBody))]
[JsonSerializable(typeof(GitHubCreateFileBody))]
[JsonSerializable(typeof(GitHubCreatePrBody))]
[JsonSerializable(typeof(GitHubDeleteFileBody))]
[JsonSerializable(typeof(BenchmarkUploadBody))]
[JsonSerializable(typeof(WebSearchBody))]
[JsonSerializable(typeof(WebMarkdownBody))]
internal sealed partial class TubaNullIgnoreContext : JsonSerializerContext
{
    public static TubaNullIgnoreContext Instance => Default;
}

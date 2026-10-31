using System.Reflection;

namespace TubaWinUi3.Services.Telemetry;

internal static class AxiomTelemetryConfig
{
    internal const string Endpoint = "https://us-east-1.aws.edge.axiom.co/v1/ingest/tuba-telemetry";

    // 只写凭据由本地 Telemetry.local.props / CI secret 注入，不把真实密钥提交到仓库。
    // 这是随客户端分发的受限写入凭据；查询/管理凭据绝不能放进软件。
    internal static string IngestToken => typeof(AxiomTelemetryConfig).Assembly
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(a => a.Key == "AxiomIngestToken")?.Value ?? string.Empty;
}

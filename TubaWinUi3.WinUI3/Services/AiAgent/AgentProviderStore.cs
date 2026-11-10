using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using TubaWinUi3.Services.Ai;

namespace TubaWinUi3.Services.AiAgent;

public sealed record AgentProviderDefinition(string Id, string Name, string Protocol, string BaseUrl,
    [property: JsonConverter(typeof(ProtectedApiKeyConverter))] string ApiKey, string[] Models, bool SupportsImages = false)
{
    public override string ToString() => Name;
    internal string EnvironmentKey => "TUBA_AI_KEY_" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Id)))[..24];
}

internal static class AgentProviderStore
{
    internal static readonly string[] Protocols = ["chat", "responses", "anthropic", "google"];
    private static string FilePath => Path.Combine(AiComponentService.Root, "providers.json");
    internal static IReadOnlyList<AgentProviderDefinition> Read() => File.Exists(FilePath)
        ? JsonSerializer.Deserialize<AgentProviderDefinition[]>(File.ReadAllText(FilePath)) ?? [] : [];
    internal static void Save(IReadOnlyList<AgentProviderDefinition> providers)
    {
        foreach (var provider in providers) Validate(provider);
        if (providers.Select(p => p.Id).Distinct(StringComparer.Ordinal).Count() != providers.Count) throw new ArgumentException(AgentText.L("ProviderDuplicate", "提供商 ID 重复。"));
        Directory.CreateDirectory(AiComponentService.Root);
        var temp = FilePath + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try { File.WriteAllText(temp, JsonSerializer.Serialize(providers, new JsonSerializerOptions { WriteIndented = true })); File.Move(temp, FilePath, true); }
        finally { if (File.Exists(temp)) File.Delete(temp); }
    }
    internal static void Validate(AgentProviderDefinition p)
    {
        if (!p.Id.StartsWith("tuba-custom-", StringComparison.Ordinal) || p.Id.Any(c => !char.IsAsciiLetterOrDigit(c) && c != '-')) throw new ArgumentException(AgentText.L("ProviderInvalidId", "提供商 ID 无效。"));
        if (string.IsNullOrWhiteSpace(p.Name) || p.Name.Length > 120 || !Protocols.Contains(p.Protocol)) throw new ArgumentException(AgentText.L("ProviderInvalidName", "请填写提供商名称并选择接口类型。"));
        ValidateEndpoint(p.BaseUrl);
        if (p.Models.Length == 0 || p.Models.Length > 1000 || p.Models.Any(m => string.IsNullOrWhiteSpace(m) || m.Length > 256 || m.Any(char.IsControl))) throw new ArgumentException(AgentText.L("ProviderInvalidModels", "请至少填写一个有效模型 ID，每行一个。"));
    }
    internal static Uri ValidateEndpoint(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https") || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0)
            throw new ArgumentException(AgentText.L("ProviderInvalidUrl", "请填写 HTTP/HTTPS API 根地址，不包含密钥、查询参数或片段。"));
        return uri;
    }
    internal static Dictionary<string, object> Configuration(IReadOnlyList<AgentProviderDefinition> providers) => providers.ToDictionary(p => p.Id, p => (object)new
    {
        name = p.Name, env = new[] { p.EnvironmentKey },
        package = "@opencode/ai/providers/" + (p.Protocol switch { "responses" => "openai-compatible/responses", "anthropic" => "anthropic", "google" => "google", _ => "openai-compatible" }),
        settings = new { baseURL = p.BaseUrl.TrimEnd('/'), apiKey = "{env:" + p.EnvironmentKey + "}" },
        models = p.Models.Distinct(StringComparer.Ordinal).ToDictionary(m => m, m => new { name = p.Name + " · " + m, capabilities = new { tools = true, input = p.SupportsImages ? new[] { "text", "image" } : new[] { "text" }, output = new[] { "text" } } })
    }, StringComparer.Ordinal);

    internal static async Task<IReadOnlyList<string>> DiscoverModelsAsync(string baseUrl, string protocol, string apiKey, CancellationToken ct)
    {
        using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct); budget.CancelAfter(TimeSpan.FromSeconds(30)); ct = budget.Token;
        var uri = ValidateEndpoint(baseUrl);
        using var handler = new HttpClientHandler { AllowAutoRedirect = false, Proxy = ProxyService.GetWebProxy() };
        using var http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        // Do not forward a user-supplied credential across redirects.
        using var request = new HttpRequestMessage(HttpMethod.Get, uri.AbsoluteUri.TrimEnd('/') + "/models");
        if (apiKey.Length > 0)
        {
            if (protocol == "anthropic") { request.Headers.Add("x-api-key", apiKey); request.Headers.Add("anthropic-version", "2023-06-01"); }
            else if (protocol == "google") request.Headers.Add("x-goog-api-key", apiKey);
            else request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException(string.Format(AgentText.L("ProviderDiscoverHttp", "无法读取模型列表（HTTP {0}）。可检查地址与密钥，或手动填写模型 ID。"), (int)response.StatusCode));
        if (response.Content.Headers.ContentLength > 4_000_000) throw new IOException(AgentText.L("ProviderListTooLarge", "模型列表过大。"));
        await using var stream = await response.Content.ReadAsStreamAsync(ct);
        using var buffer = new MemoryStream();
        var block = new byte[16384]; int count;
        while ((count = await stream.ReadAsync(block, ct)) > 0) { if (buffer.Length + count > 4_000_000) throw new IOException(AgentText.L("ProviderListTooLarge", "模型列表过大。")); buffer.Write(block, 0, count); }
        using var json = JsonDocument.Parse(buffer.ToArray());
        var root = json.RootElement;
        var models = root.TryGetProperty("data", out var data) ? data : root.TryGetProperty("models", out data) ? data : default;
        if (models.ValueKind != JsonValueKind.Array) throw new IOException(AgentText.L("ProviderInvalidList", "服务未返回模型列表，请手动填写模型 ID。"));
        return models.EnumerateArray().Select(m => AgentProtocol.Text(m, "id", AgentProtocol.Text(m, "name")).Replace("models/", "", StringComparison.Ordinal))
            .Where(m => m.Length > 0).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).Take(1000).ToArray();
    }
}

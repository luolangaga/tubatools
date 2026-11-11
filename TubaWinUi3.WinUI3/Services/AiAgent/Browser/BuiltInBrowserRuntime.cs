using System.Text.Json;
using System.Text.Json.Nodes;

namespace TubaWinUi3.Services.AiAgent;

internal sealed record BrowserPageStatus(string Url, string Title, string Ready);

/// <summary>Every CDP call is relayed to the app-hosted WebView2 page over a named pipe.</summary>
internal sealed class BuiltInBrowserRuntime : IAsyncDisposable
{
    private readonly AiBrowserHostClient _client = new();

    internal Task EnsureAsync(CancellationToken ct) => _client.RequestAsync("ensure", ct: ct);

    internal async Task<JsonElement> SendCdpAsync(string method, JsonNode? parameters, CancellationToken ct)
    {
        await EnsureAsync(ct);
        return await _client.RequestAsync("cdp", method, parameters as JsonObject, ct);
    }

    internal async Task<byte[]> CaptureAsync(CancellationToken ct)
    {
        var result = await _client.RequestAsync("capture", ct: ct);
        var data = result.ValueKind == JsonValueKind.Object && result.TryGetProperty("base64", out var value)
            ? value.GetString() : null;
        if (string.IsNullOrEmpty(data)) throw new BrowserToolException("SCREENSHOT_FAILED", "内置浏览器没有返回截图。");
        return Convert.FromBase64String(data);
    }

    public ValueTask DisposeAsync() => _client.DisposeAsync();
}

using System.Text.Json;
using Microsoft.AspNetCore.Http;

namespace TubaWinUi3.Services;

internal static class RequestLimits
{
    internal const int MaxJsonBytes = 64 * 1024;
    internal static async Task<JsonElement> ReadJsonAsync(HttpRequest request)
    {
        if (request.ContentLength > MaxJsonBytes) throw new BadHttpRequestException("请求过大", 413);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(request.HttpContext.RequestAborted);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        using var body = new MemoryStream();
        await CopyBoundedAsync(request.Body, body, MaxJsonBytes, deadline.Token);
        if (body.Length == 0) return default;
        try { using var json = JsonDocument.Parse(body.ToArray()); return json.RootElement.Clone(); }
        catch (JsonException) { throw new BadHttpRequestException("JSON 格式不合法", 400); }
    }

    internal static async Task<long> CopyBoundedAsync(Stream source, Stream destination, long limit, CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        long total = 0;
        int count;
        while ((count = await source.ReadAsync(buffer, ct)) != 0)
        {
            if (count > limit - total) throw new BadHttpRequestException("文件或请求超过大小限制", 413);
            await destination.WriteAsync(buffer.AsMemory(0, count), ct);
            total += count;
        }
        return total;
    }
}

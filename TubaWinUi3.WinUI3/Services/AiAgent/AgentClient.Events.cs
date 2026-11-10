using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace TubaWinUi3.Services.AiAgent;

public sealed partial class AgentClient
{
    private async Task WatchEventsAsync(string sessionId, AgentRunSignals signals, TaskCompletionSource connected, CancellationToken ct)
    {
        while (!ct.IsCancellationRequested && IsReady)
        {
            try
            {
                var http = _http; if (http is null) break;
                using var request = new HttpRequestMessage(HttpMethod.Get, "api/event");
                using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                response.EnsureSuccessStatusCode(); connected.TrySetResult();
                await using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                using var reader = new StreamReader(stream);
                var data = new StringBuilder(); var oversized = false;
                while (await reader.ReadLineAsync(ct).ConfigureAwait(false) is { } line)
                {
                    if (line.Length == 0)
                    {
                        if (!oversized && data.Length > 0)
                            try { using var json = JsonDocument.Parse(data.ToString()); signals.Observe(json.RootElement, sessionId); }
                            catch (JsonException) { /* A malformed event does not invalidate durable message history. */ }
                        data.Clear(); oversized = false;
                    }
                    else if (line.StartsWith("data:", StringComparison.Ordinal) && !oversized)
                    {
                        if (data.Length > 0) data.Append('\n');
                        data.Append(line.AsSpan(5).TrimStart());
                        if (data.Length > 2_000_000) { data.Clear(); oversized = true; }
                    }
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or OperationCanceledException or ObjectDisposedException) { }
            if (ct.IsCancellationRequested || !IsReady) break;
            try { await Task.Delay(1000, ct).ConfigureAwait(false); } catch (OperationCanceledException) { break; }
        }
    }
}

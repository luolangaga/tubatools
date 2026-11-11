using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TubaWinUi3.Services.AiAgent;

internal sealed record AiBrowserHostDescriptor(string Pipe, string Token, int Pid, long StartedAt);

/// <summary>
/// Client side of the app-hosted WebView2 bridge. Commands are raw CDP method calls so the
/// same browser automation runs against the built-in WebView2 and external Chromium.
/// </summary>
internal sealed class AiBrowserHostClient : IAsyncDisposable
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private NamedPipeClientStream? _pipe;
    private StreamReader? _reader;
    private StreamWriter? _writer;
    private string _token = "";
    private DateTime _descriptorStamp;

    internal async Task<JsonElement> RequestAsync(string op, string? cdp = null, JsonObject? parameters = null,
        CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            try
            {
                await EnsureConnectedAsync(ct);
                return await SendCoreAsync(op, cdp, parameters, ct);
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException or InvalidOperationException)
            {
                await ResetAsync();
                await EnsureConnectedAsync(ct);
                return await SendCoreAsync(op, cdp, parameters, ct);
            }
        }
        finally { _gate.Release(); }
    }

    private async Task<JsonElement> SendCoreAsync(string op, string? cdp, JsonObject? parameters, CancellationToken ct)
    {
        var id = Guid.NewGuid().ToString("N");
        var request = new JsonObject
        {
            ["token"] = _token,
            ["id"] = id,
            ["op"] = op
        };
        if (!string.IsNullOrEmpty(cdp)) request["cdp"] = cdp;
        if (parameters is not null) request["params"] = parameters;
        await _writer!.WriteLineAsync(request.ToJsonString());
        await _writer.FlushAsync();

        for (var attempt = 0; attempt < 4; attempt++)
        {
            var line = await _reader!.ReadLineAsync(ct);
            if (line is null) throw new IOException("AI browser host closed the pipe.");
            JsonDocument document;
            try { document = JsonDocument.Parse(line); }
            catch (JsonException) { continue; }
            using (document)
            {
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("id", out var responseId) ||
                    responseId.ValueKind != JsonValueKind.String || responseId.GetString() != id) continue;
                var ok = root.TryGetProperty("ok", out var okElement) && okElement.ValueKind == JsonValueKind.True;
                if (!ok)
                {
                    var code = root.TryGetProperty("code", out var codeElement) && codeElement.ValueKind == JsonValueKind.String
                        ? codeElement.GetString() ?? "BROWSER_HOST_ERROR" : "BROWSER_HOST_ERROR";
                    var message = root.TryGetProperty("message", out var messageElement) && messageElement.ValueKind == JsonValueKind.String
                        ? messageElement.GetString() ?? "AI 浏览器操作失败。" : "AI 浏览器操作失败。";
                    throw new BrowserToolException(code, message);
                }
                return root.TryGetProperty("result", out var result) ? result.Clone() : default;
            }
        }
        throw new IOException("AI browser host returned an unexpected response.");
    }

    private async Task EnsureConnectedAsync(CancellationToken ct)
    {
        var path = AiBrowserPaths.HostDescriptor;
        if (!File.Exists(path))
            throw new BrowserToolException("BROWSER_HOST_UNAVAILABLE",
                "内置 WebView2 服务尚未就绪：请先打开图吧工具箱的 AI 助手。若仍失败，请在 设置 → AI 浏览器 中点击「一键修复」。");
        var stamp = File.GetLastWriteTimeUtc(path);
        AiBrowserHostDescriptor? descriptor = null;
        try
        {
            descriptor = JsonSerializer.Deserialize<AiBrowserHostDescriptor>(File.ReadAllText(path),
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        if (descriptor is null || !ProcessAlive(descriptor.Pid))
            throw new BrowserToolException("BROWSER_HOST_UNAVAILABLE",
                "内置 WebView2 服务没有运行：请保持图吧工具箱打开，或重新打开 AI 助手后再试。");

        if (_pipe is { IsConnected: true } && stamp == _descriptorStamp && _token == descriptor.Token) return;
        await ResetAsync();
        _token = descriptor.Token;
        _descriptorStamp = stamp;

        NamedPipeClientStream? connected = null;
        for (var attempt = 0; attempt < 10 && connected is null; attempt++)
        {
            var pipe = new NamedPipeClientStream(".", descriptor.Pipe, PipeDirection.InOut, PipeOptions.Asynchronous);
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(2));
                await pipe.ConnectAsync(timeout.Token);
                connected = pipe;
            }
            catch (Exception ex) when (ex is TimeoutException or IOException or OperationCanceledException)
            {
                pipe.Dispose();
                if (attempt < 9)
                {
                    try { await Task.Delay(200, ct); }
                    catch (OperationCanceledException) { break; }
                }
            }
        }
        if (connected is null)
            throw new BrowserToolException("BROWSER_HOST_UNAVAILABLE",
                "无法连接内置 WebView2 服务：请保持图吧工具箱打开，或在 设置 → AI 浏览器 中点击「一键修复」。");
        _pipe = connected;
        _reader = new StreamReader(connected, new UTF8Encoding(false), false, 8192, leaveOpen: true);
        _writer = new StreamWriter(connected, new UTF8Encoding(false), 8192, leaveOpen: true) { AutoFlush = false };
    }

    private async Task ResetAsync()
    {
        if (_pipe is not null)
        {
            try { await _pipe.DisposeAsync(); }
            catch (IOException) { }
        }
        _pipe = null;
        _reader = null;
        _writer = null;
        _token = "";
        _descriptorStamp = default;
    }

    private static bool ProcessAlive(int pid)
    {
        try
        {
            using var process = Process.GetProcessById(pid);
            return !process.HasExited;
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or System.ComponentModel.Win32Exception) { return false; }
    }

    public async ValueTask DisposeAsync()
    {
        await ResetAsync();
        _gate.Dispose();
    }
}

using System.IO.Pipes;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using TubaWinUi3.Pages;

namespace TubaWinUi3.Services.AiAgent;

/// <summary>
/// Runs inside the app: owns the AI WebView2 page and serves raw CDP calls over a same-user
/// named pipe. Opening the page, the control badge and the stop gate are handled here.
/// </summary>
internal static class AiBrowserHostService
{
    private static readonly SemaphoreSlim PageGate = new(1, 1);
    private static readonly object Sync = new();
    private static DispatcherQueue? _dispatcher;
    private static volatile BrowserPage? _page;
    private static volatile TaskCompletionSource<BrowserPage>? _pendingPage;
    private static CancellationTokenSource? _lifetime;
    private static string _pipeName = "";
    private static string _token = "";
    private static bool _started;
    private static bool _recycling;
    private static bool _active;
    private static DateTime _lastActivity;
    private static DispatcherTimer? _activityTimer;

    internal static event Action<bool>? ActivityChanged;
    internal static bool IsActive => _active;

    internal static void Start(Window owner)
    {
        if (_started) return;
        _started = true;
        _dispatcher = owner.DispatcherQueue;
        AiBrowserControl.Resume();
        AiBrowserControl.StartWatcher();
        _pipeName = PipeName();
        _token = Convert.ToHexString(RandomNumberGenerator.GetBytes(18));
        _activityTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _activityTimer.Tick += (_, _) =>
        {
            if (_active && DateTime.UtcNow - _lastActivity > TimeSpan.FromSeconds(6)) SetActive(false);
        };
        WriteDescriptor();
        _lifetime = new CancellationTokenSource();
        _ = Task.Run(() => ServeAsync(_lifetime.Token));
    }

    internal static void Shutdown()
    {
        if (!_started) return;
        _started = false;
        _lifetime?.Cancel();
        _lifetime?.Dispose();
        _lifetime = null;
        try { File.Delete(AiBrowserPaths.HostDescriptor); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        _activityTimer?.Stop();
        _activityTimer = null;
        _page = null;
        _pendingPage = null;
    }

    internal static async Task<BrowserPage> EnsurePageAsync(CancellationToken ct)
    {
        await PageGate.WaitAsync(ct);
        try
        {
            if (_page is { } ready && ready.IsReady) return ready;
            if (_dispatcher is null)
                throw new BrowserToolException("BROWSER_HOST_UNAVAILABLE", "AI 浏览器服务尚未初始化，请重新打开图吧工具箱。");

            if (_page is { } stale)
            {
                CloseWindowFor(stale);
                var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
                while (_page == stale && DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
                    await Task.Delay(50, ct);
            }

            var pending = new TaskCompletionSource<BrowserPage>(TaskCreationOptions.RunContinuationsAsynchronously);
            _pendingPage = pending;
            var enqueued = _dispatcher.TryEnqueue(() =>
            {
                try
                {
                    BuiltinToolWindow.Show(typeof(BrowserPage),
                        new BrowserPageParam { Url = "about:blank", Title = "AI 浏览器", AiHosted = true }, "AI 浏览器");
                }
                catch (Exception ex) { pending.TrySetException(ex); }
            });
            if (!enqueued) throw new BrowserToolException("BROWSER_HOST_UNAVAILABLE", "无法打开 AI 浏览器窗口。");
            var page = await pending.Task.WaitAsync(TimeSpan.FromSeconds(30), ct);
            await page.ReadyTask.WaitAsync(TimeSpan.FromSeconds(40), ct);
            return page;
        }
        finally
        {
            _pendingPage = null;
            PageGate.Release();
        }
    }

    internal static async Task OpenWindowAsync(CancellationToken ct = default)
    {
        AiBrowserControl.Resume();
        await EnsurePageAsync(ct);
    }

    internal static async Task CloseWindowAsync(bool stopControl = true, CancellationToken ct = default)
    {
        await CloseWindowCoreAsync(stopControl, ct);
    }

    internal static async Task RecyclePageAsync(CancellationToken ct)
    {
        await CloseWindowCoreAsync(stopControl: false, ct);
        AiBrowserControl.Resume();
        await EnsurePageAsync(ct);
    }

    private static async Task CloseWindowCoreAsync(bool stopControl, CancellationToken ct)
    {
        var page = _page;
        if (page is not null)
        {
            if (!stopControl) _recycling = true;
            try
            {
                CloseWindowFor(page);
                var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
                while (_page == page && DateTime.UtcNow < deadline && !ct.IsCancellationRequested)
                    await Task.Delay(50, ct);
            }
            finally { if (!stopControl) _recycling = false; }
        }
        _page = null;
        SetActive(false);
        if (stopControl) AiBrowserControl.Stop("AI 浏览器窗口已关闭");
    }

    internal static void AttachPage(BrowserPage page)
    {
        _page = page;
        _pendingPage?.TrySetResult(page);
        SetActive(true);
    }

    internal static void PageHostClosed(BrowserPage page)
    {
        var wasCurrent = _page == page;
        if (wasCurrent) _page = null;
        SetActive(false);
        if (wasCurrent && !_recycling && _started) AiBrowserControl.Stop("AI 浏览器窗口已关闭");
    }

    internal static JsonObject LocalStatus()
    {
        var page = _page;
        return new JsonObject
        {
            ["open"] = page is not null,
            ["ready"] = page?.IsReady == true,
            ["url"] = page?.CurrentUrl ?? "",
            ["title"] = page?.CurrentTitle ?? "",
            ["active"] = _active,
            ["stopped"] = AiBrowserControl.IsStopped,
            ["engine"] = AiBrowserInfo.Name,
            ["profile"] = AiBrowserInfo.ProfilePath
        };
    }

    internal static void MarkActive()
    {
        _lastActivity = DateTime.UtcNow;
        if (!_active) SetActive(true);
    }

    internal static void MarkInactive() => SetActive(false);

    private static void SetActive(bool value)
    {
        if (_active == value) return;
        _active = value;
        void Raise()
        {
            if (value) _activityTimer?.Start(); else _activityTimer?.Stop();
            var handler = ActivityChanged;
            if (handler is null) return;
            foreach (var subscriber in handler.GetInvocationList())
            {
                try { ((Action<bool>)subscriber)(value); }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine("[AiBrowser] ActivityChanged: " + ex.Message); }
            }
        }
        // TryEnqueue is safe from both the UI thread and the pipe thread; HasThreadAccess is a
        // WinRT call and must not be made from the background reader.
        _dispatcher?.TryEnqueue(Raise);
    }

    private static void CloseWindowFor(BrowserPage page)
    {
        if (_dispatcher is null) return;
        _dispatcher.TryEnqueue(() =>
        {
            try
            {
                var window = BuiltinToolWindow.FindHostingWindow(page);
                if (window is not null) window.Close();
                else if (App.MainWindow?.Content?.XamlRoot is { } root && ReferenceEquals(root, page.XamlRoot))
                    App.MainWindow.NavigateBack();
            }
            catch (Exception ex) { LogService.Warn(LogService.App, "关闭 AI 浏览器窗口失败：" + ex.Message); }
        });
    }

    private static async Task ServeAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeServerStream? pipe = null;
            try
            {
                pipe = new NamedPipeServerStream(_pipeName, PipeDirection.InOut, 4,
                    PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(ct);
                _ = Task.Run(() => HandleConnectionAsync(pipe, ct), CancellationToken.None);
            }
            catch (OperationCanceledException) { pipe?.Dispose(); return; }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                pipe?.Dispose();
                LogService.Warn(LogService.App, "AI 浏览器管道服务异常：" + ex.Message);
                try { await Task.Delay(500, ct); } catch (OperationCanceledException) { return; }
            }
        }
    }

    private static async Task HandleConnectionAsync(NamedPipeServerStream pipe, CancellationToken ct)
    {
        using (pipe)
        using (var reader = new StreamReader(pipe, new UTF8Encoding(false), false, 8192, leaveOpen: true))
        using (var writer = new StreamWriter(pipe, new UTF8Encoding(false), 8192, leaveOpen: true) { AutoFlush = true })
        {
            while (!ct.IsCancellationRequested)
            {
                string? line;
                try { line = await reader.ReadLineAsync(ct); }
                catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException) { break; }
                if (line is null) break;
                if (line.Length > 8 * 1024 * 1024) continue;
                JsonObject? request;
                try { request = JsonNode.Parse(line) as JsonObject; }
                catch (JsonException) { continue; }
                if (request is null) continue;
                var id = request["id"]?.GetValue<string>() ?? "";
                JsonObject response;
                try
                {
                    if (request["token"]?.GetValue<string>() != _token)
                        throw new BrowserToolException("BROWSER_HOST_UNAUTHORIZED", "AI 浏览器控制通道校验失败。");
                    var op = request["op"]?.GetValue<string>() ?? "";
                    var result = await DispatchAsync(op, request["cdp"]?.GetValue<string>(), request["params"] as JsonObject, ct);
                    response = new JsonObject { ["id"] = id, ["ok"] = true, ["result"] = result };
                }
                catch (BrowserToolException ex)
                {
                    response = Error(id, ex.Code, ex.Message);
                }
                catch (OperationCanceledException)
                {
                    response = Error(id, "CANCELLED", "浏览器操作已取消。");
                }
                catch (Exception ex)
                {
                    LogService.Warn(LogService.App, "AI 浏览器操作失败：" + ex);
                    response = Error(id, "BROWSER_HOST_ERROR", ex.Message);
                }
                try { await writer.WriteLineAsync(response.ToJsonString()); }
                catch (Exception ex) when (ex is IOException or ObjectDisposedException) { break; }
            }
        }
    }

    private static async Task<JsonNode?> DispatchAsync(string op, string? cdp, JsonObject? parameters, CancellationToken ct)
    {
        switch (op.ToLowerInvariant())
        {
            case "ping":
                return new JsonObject();

            case "status":
                return LocalStatus();

            case "resume":
                AiBrowserControl.Resume();
                return new JsonObject { ["stopped"] = false };

            case "stop":
                AiBrowserControl.Stop("用户停止 AI 浏览器控制");
                MarkInactive();
                return new JsonObject { ["stopped"] = true };

            case "close":
                await CloseWindowAsync();
                return new JsonObject();

            case "ensure":
            {
                EnsureAllowed();
                try { return PageStatus(await EnsurePageAsync(ct)); }
                finally { MarkActive(); }
            }

            case "cdp":
            {
                EnsureAllowed();
                if (string.IsNullOrWhiteSpace(cdp))
                    throw new BrowserToolException("INVALID_ARGUMENT", "缺少 CDP 方法名。");
                try
                {
                    var page = await EnsurePageAsync(ct);
                    var raw = await page.CallCdpAsync(cdp, parameters?.ToJsonString() ?? "{}", ct);
                    return ParseResult(raw);
                }
                finally { MarkActive(); }
            }

            case "capture":
            {
                EnsureAllowed();
                try
                {
                    var page = await EnsurePageAsync(ct);
                    try
                    {
                        var raw = await page.CallCdpAsync("Page.captureScreenshot", "{\"format\":\"png\",\"fromSurface\":true}", ct);
                        var data = (ParseResult(raw) as JsonObject)?["data"]?.GetValue<string>();
                        if (!string.IsNullOrEmpty(data)) return new JsonObject { ["base64"] = data };
                    }
                    catch (BrowserToolException) { }
                    var bytes = await page.CaptureAsync(ct);
                    return new JsonObject { ["base64"] = Convert.ToBase64String(bytes) };
                }
                finally { MarkActive(); }
            }

            default:
                throw new BrowserToolException("INVALID_ARGUMENT", "未知的浏览器操作：" + op);
        }
    }

    private static void EnsureAllowed()
    {
        if (AiBrowserControl.IsStopped)
            throw new BrowserToolException("USER_STOPPED_BROWSER_CONTROL",
                "用户已停止浏览器控制。不要重新获取控制；只有用户明确要求继续后才能再次操作浏览器。");
    }

    private static JsonObject PageStatus(BrowserPage page) => new()
    {
        ["url"] = page.CurrentUrl,
        ["title"] = page.CurrentTitle,
        ["open"] = true,
        ["ready"] = page.IsReady,
        ["active"] = _active,
        ["stopped"] = AiBrowserControl.IsStopped
    };

    private static JsonNode? ParseResult(string raw)
    {
        try { return JsonNode.Parse(raw); }
        catch (JsonException) { return JsonValue.Create(raw); }
    }

    private static JsonObject Error(string id, string code, string message) => new()
    {
        ["id"] = id,
        ["ok"] = false,
        ["code"] = code,
        ["message"] = message
    };

    private static void WriteDescriptor()
    {
        try
        {
            Directory.CreateDirectory(AiBrowserPaths.Root);
            var descriptor = new JsonObject
            {
                ["pipe"] = _pipeName,
                ["token"] = _token,
                ["pid"] = Environment.ProcessId,
                ["startedAt"] = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
            };
            var path = AiBrowserPaths.HostDescriptor;
            var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllText(temporary, descriptor.ToJsonString(), new UTF8Encoding(false));
            File.Move(temporary, path, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            LogService.Warn(LogService.App, "写入 AI 浏览器服务描述失败：" + ex.Message);
        }
    }

    private static string PipeName()
    {
        var sid = "";
        try { using var identity = WindowsIdentity.GetCurrent(); sid = identity.User?.Value ?? ""; }
        catch (Exception ex) when (ex is UnauthorizedAccessException or InvalidOperationException) { }
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sid + "|" + AppContext.BaseDirectory.ToUpperInvariant())))[..24];
        return "Tuba.AiBrowser." + hash;
    }
}

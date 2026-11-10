using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TubaWinUi3.Services.AiAgent;

/// <summary>V2 私有服务适配。只绑定回环、随机密码，配置/数据隔离，不连接用户的共享服务。</summary>
public sealed partial class AgentClient : IDisposable
{
    private Process? _process;
    private HttpClient? _http;
    private bool _disposed;
    public bool IsReady => _process is { HasExited: false } && _http is not null;

    public async Task StartAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (IsReady) return;
        StopProcess();
        if (!AiComponentService.IsInstalled) throw new InvalidOperationException("AI component missing.");
        await HybridComputerUseService.EnsureInstalledAsync(ct);
        AgentWorkspace.Prepare();
        var port = FindPort();
        var password = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
        var info = AiComponentService.StartInfo(AiComponentService.Executable, AgentWorkspace.DirectoryPath);
        foreach (var argument in new[] { "serve", "--hostname", "127.0.0.1", "--port", port.ToString() }) info.ArgumentList.Add(argument);
        info.Environment["OPENCODE_PASSWORD"] = password;
        _process = Process.Start(info) ?? throw new IOException("AI component failed to start.");
        ChildProcessJob.Assign(_process);
        // 服务日志可能包含连接密码，不转发到界面或持久日志。
        _process.OutputDataReceived += (_, _) => { };
        _process.ErrorDataReceived += (_, _) => { };
        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();
        _http = new HttpClient(new SocketsHttpHandler { UseProxy = false })
        {
            BaseAddress = new Uri($"http://127.0.0.1:{port}"), Timeout = TimeSpan.FromSeconds(15)
        };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic",
            Convert.ToBase64String(Encoding.UTF8.GetBytes("opencode:" + password)));
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromSeconds(60));
        try
        {
            while (true)
            {
                deadline.Token.ThrowIfCancellationRequested();
                if (_process.HasExited) throw new IOException("AI component exited.");
                try
                {
                    using var response = await _http.GetAsync("api/info", deadline.Token);
                    if (response.IsSuccessStatusCode) return;
                }
                catch (HttpRequestException) { }
                await Task.Delay(250, deadline.Token);
            }
        }
        catch { StopProcess(); throw; }
    }

    private static int FindPort()
    {
        for (var i = 0; i < 30; i++)
        {
            var listener = new TcpListener(IPAddress.Loopback, Random.Shared.Next(20000, 40000));
            try { listener.Start(); return ((IPEndPoint)listener.LocalEndpoint).Port; }
            catch (SocketException) { }
            finally { listener.Stop(); }
        }
        throw new IOException("No available local port.");
    }

    internal async Task<JsonElement> RequestAsync(HttpMethod method, string path, object? body, CancellationToken ct)
    {
        if (!IsReady) throw new IOException("AI component not running.");
        using var request = new HttpRequestMessage(method, path);
        if (body is not null) request.Content = JsonContent.Create(body);
        using var response = await _http!.SendAsync(request, ct);
        if (!response.IsSuccessStatusCode) throw new HttpRequestException($"AI request failed ({(int)response.StatusCode}).", null, response.StatusCode);
        if (response.StatusCode == HttpStatusCode.NoContent) return default;
        var text = await response.Content.ReadAsStringAsync(ct);
        if (text.Length == 0) return default;
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    private static string Location => "location%5Bdirectory%5D=" + Uri.EscapeDataString(AgentWorkspace.DirectoryPath);
    private static string SessionPath(string id) => "api/session/" + Uri.EscapeDataString(id);
    public async Task<IReadOnlyList<AgentModel>> GetModelsAsync(CancellationToken ct) =>
        AgentProtocol.ParseModels(await RequestAsync(HttpMethod.Get, "api/model?" + Location, null, ct));

    public async Task<IReadOnlyList<AgentConversation>> GetConversationsAsync(CancellationToken ct, int pageSize = 100)
    {
        var result = new List<AgentConversation>();
        string? cursor = null;
        var seen = new HashSet<string>();
        do
        {
            var path = "api/session?limit=" + Math.Clamp(pageSize, 1, 1000) +
                (cursor is null ? "&order=desc&parentID=null&directory=" + Uri.EscapeDataString(AgentWorkspace.DirectoryPath) : "&cursor=" + Uri.EscapeDataString(cursor));
            var root = await RequestAsync(HttpMethod.Get, path, null, ct);
            result.AddRange(AgentProtocol.Data(root).EnumerateArray().Select(s => new AgentConversation(AgentProtocol.Text(s, "id"),
                AgentProtocol.Text(s, "title", AgentText.L("Untitled", "新对话")))));
            cursor = root.TryGetProperty("cursor", out var c) ? AgentProtocol.Text(c, "next") : null;
            if (string.IsNullOrEmpty(cursor)) break;
            if (!seen.Add(cursor)) throw new IOException("AI session cursor repeated.");
        } while (true);
        return result.DistinctBy(s => s.Id).ToArray();
    }

    public async Task<string> CreateConversationAsync(AgentModel model, string title, CancellationToken ct,
        AgentPermissionMode permissions = AgentPermissionMode.FullAccess)
    {
        var root = await RequestAsync(HttpMethod.Post, "api/session", new
        {
            title, agent = AgentPersona.Id, model = model.Reference, location = new { directory = AgentWorkspace.DirectoryPath }, permissions = AgentPermissionPolicy.Rules(permissions)
        }, ct);
        return AgentProtocol.Text(AgentProtocol.Data(root), "id");
    }

    public async Task<IReadOnlyList<AgentMessage>> GetMessagesAsync(string id, CancellationToken ct) =>
        AgentProtocol.ParseMessages(await RequestAsync(HttpMethod.Get, SessionPath(id) + "/message?limit=100&order=desc", null, ct))
            .Reverse().ToList();

    public async Task RenameConversationAsync(string id, string title, CancellationToken ct) =>
        await RequestAsync(HttpMethod.Patch, SessionPath(id), new { title }, ct);

    public async Task<IReadOnlyList<AgentMessage>> GetAllMessagesAsync(string id, CancellationToken ct, int pageSize = 100)
    {
        var result = new List<AgentMessage>();
        string? cursor = null;
        var seen = new HashSet<string>();
        do
        {
            var path = SessionPath(id) + "/message?limit=" + Math.Clamp(pageSize, 1, 1000) +
                (cursor is null ? "&order=asc" : "&cursor=" + Uri.EscapeDataString(cursor));
            var root = await RequestAsync(HttpMethod.Get, path, null, ct);
            result.AddRange(AgentProtocol.ParseMessages(root));
            cursor = root.TryGetProperty("cursor", out var c) ? AgentProtocol.Text(c, "next") : null;
            if (string.IsNullOrEmpty(cursor)) break;
            if (!seen.Add(cursor)) throw new IOException("AI history cursor repeated.");
        } while (true);
        return result.DistinctBy(m => m.Id).ToArray();
    }

    public async Task RunAsync(string id, AgentModel model, string prompt,
        Action<IReadOnlyList<AgentMessage>> update, Func<AgentPermission, Task<bool>> approve, CancellationToken ct,
        Func<AgentQuestion, CancellationToken, Task<IReadOnlyDictionary<string, object>?>>? ask = null,
        Action<AgentRunProgress>? progress = null, AgentPermissionMode permissions = AgentPermissionMode.FullAccess,
        IReadOnlyList<AgentAttachment>? attachments = null)
        => await RunInputAsync(id, model, prompt, update, approve, ct, false, ask, progress, permissions, attachments);

    internal async Task ResumeWithPluginFeedbackAsync(string id, AgentModel model, string feedback,
        Action<IReadOnlyList<AgentMessage>> update, Func<AgentPermission, Task<bool>> approve, CancellationToken ct,
        Func<AgentQuestion, CancellationToken, Task<IReadOnlyDictionary<string, object>?>>? ask = null,
        Action<AgentRunProgress>? progress = null, AgentPermissionMode permissions = AgentPermissionMode.FullAccess)
        => await RunInputAsync(id, model, feedback, update, approve, ct, true, ask, progress, permissions);

    internal async Task RecordPluginResultsAsync(string id, IReadOnlyList<AgentPluginLoadResult> results, CancellationToken ct)
    {
        if (results.Count == 0) return;
        await RequestAsync(HttpMethod.Post, SessionPath(id) + "/synthetic", new
        {
            text = PluginFeedback(results), description = "Plugin load result", resume = false,
            metadata = new { tubaHostPluginResult = true }
        }, ct);
    }
    internal static string PluginFeedback(IReadOnlyList<AgentPluginLoadResult> results) =>
        "宿主插件加载的实际结果（不是用户的新请求）：\n" + string.Join("\n", results.Select(r => r.Message)) +
        "\n如有加载失败，请检查清单、入口和依赖，递增版本重新 dotnet pack 到 artifacts/。不得要求用户点击加载按钮。";

    private async Task RunInputAsync(string id, AgentModel model, string prompt,
        Action<IReadOnlyList<AgentMessage>> update, Func<AgentPermission, Task<bool>> approve, CancellationToken ct, bool synthetic,
        Func<AgentQuestion, CancellationToken, Task<IReadOnlyDictionary<string, object>?>>? ask,
        Action<AgentRunProgress>? progress, AgentPermissionMode permissionMode, IReadOnlyList<AgentAttachment>? attachments = null)
    {
        using var eventLifetime = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var signals = new AgentRunSignals();
        var connected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var events = WatchEventsAsync(id, signals, connected, eventLifetime.Token);
        try
        {
        try { await connected.Task.WaitAsync(TimeSpan.FromSeconds(2), ct); } catch (TimeoutException) { }
        await RequestAsync(HttpMethod.Post, SessionPath(id) + "/model", new { model = model.Reference }, ct);
        // 使用组件时间做完成边界，避免主机时钟改变或上一轮 idle 导致提前结束。
        var before = AgentProtocol.Data(await RequestAsync(HttpMethod.Get, SessionPath(id), null, ct));
        // Apply the user's current policy to existing sessions as well as newly created sessions.
        await RequestAsync(HttpMethod.Patch, SessionPath(id), new { permissions = AgentPermissionPolicy.Rules(permissionMode) }, ct);
        var oldIdle = IdleTime(before);
        object input = synthetic ? new { text = prompt, description = "Plugin load repair", resume = true } : await AgentAttachment.PromptInputAsync(prompt, attachments, ct);
        await RequestAsync(HttpMethod.Post, SessionPath(id) + (synthetic ? "/synthetic" : "/prompt"), input, ct);
        var handledPermissions = new HashSet<string>();
        var handledForms = new HashSet<string>();
        var permissionReplies = new Dictionary<string, bool>();
        var formReplies = new Dictionary<string, IReadOnlyDictionary<string, object>?>();
        var watch = Stopwatch.StartNew();
        var monitor = new AgentRunMonitor();
        progress?.Invoke(new("waiting"));
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                var messages = await GetMessagesAsync(id, ct);
                update(messages);
                var state = monitor.Observe(messages, watch.Elapsed, signals.Read());
                progress?.Invoke(state);
                var permissions = AgentProtocol.Data(await RequestAsync(HttpMethod.Get, SessionPath(id) + "/permission", null, ct));
                foreach (var item in permissions.EnumerateArray())
                {
                    var requestId = AgentProtocol.Text(item, "id");
                    if (handledPermissions.Contains(requestId)) continue;
                    var permission = new AgentPermission(requestId, AgentProtocol.Text(item, "action"),
                        item.GetProperty("resources").EnumerateArray().Select(x => x.GetString() ?? "").ToArray(), AgentProtocol.Text(item, "message"));
                    progress?.Invoke(new("permission"));
                    if (!permissionReplies.TryGetValue(requestId, out var accepted)) { accepted = permissionMode == AgentPermissionMode.FullAccess || await approve(permission); permissionReplies.Add(requestId, accepted); }
                    monitor.Resume(watch.Elapsed);
                    ct.ThrowIfCancellationRequested();
                    await RequestAsync(HttpMethod.Post, SessionPath(id) + "/permission/" + Uri.EscapeDataString(requestId) + "/reply",
                        new { decision = accepted ? "once" : "reject" }, ct);
                    handledPermissions.Add(requestId);
                }
                var forms = AgentProtocol.Data(await RequestAsync(HttpMethod.Get, SessionPath(id) + "/form", null, ct));
                foreach (var form in forms.EnumerateArray())
                {
                    var question = AgentQuestions.Parse(form);
                    if (handledForms.Contains(question.Id)) continue;
                    progress?.Invoke(new("question"));
                    if (!formReplies.TryGetValue(question.Id, out var answer)) { answer = ask is null ? null : await ask(question, ct); formReplies.Add(question.Id, answer); }
                    monitor.Resume(watch.Elapsed);
                    ct.ThrowIfCancellationRequested();
                    var path = SessionPath(id) + "/form/" + Uri.EscapeDataString(question.Id);
                    if (answer is null) await RequestAsync(HttpMethod.Delete, path, null, ct);
                    else
                    {
                        var error = AgentQuestions.Validate(question, answer);
                        if (error is not null) throw new InvalidOperationException(error);
                        await RequestAsync(HttpMethod.Post, path + "/reply", new { answer }, ct);
                    }
                    handledForms.Add(question.Id);
                    progress?.Invoke(new("waiting"));
                }
                var session = AgentProtocol.Data(await RequestAsync(HttpMethod.Get, SessionPath(id), null, ct));
                if (IdleTime(session) > oldIdle)
                {
                    var completed = await GetMessagesAsync(id, ct);
                    update(completed);
                    if (completed.LastOrDefault(m => m.Role == "assistant")?.Error is { } error)
                        throw new AgentModelFailureException(error);
                    return;
                }
                await Task.Delay(500, ct);
                }
                catch (Exception ex) when (IsReady && !ct.IsCancellationRequested && IsTransientPollFailure(ex))
                { progress?.Invoke(new("reconnecting")); await Task.Delay(1500, ct); }
            }
        }
        catch (AgentModelFailureException) { throw; }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            await InterruptAsync(id);
            throw;
        }
        finally { eventLifetime.Cancel(); await events; }
    }

    private static bool IsTransientPollFailure(Exception ex) => ex is TaskCanceledException or IOException ||
        ex is HttpRequestException http && (http.StatusCode is null or HttpStatusCode.RequestTimeout or HttpStatusCode.TooManyRequests || (int)http.StatusCode >= 500);

    private static double IdleTime(JsonElement session) => session.TryGetProperty("time", out var t) &&
        t.TryGetProperty("idle", out var idle) && idle.TryGetDouble(out var value) ? value : 0;

    public async Task InterruptAsync(string id)
    {
        using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        try { await RequestAsync(HttpMethod.Post, SessionPath(id) + "/interrupt", null, budget.Token); }
        catch { /* One failed interrupt must not kill the component and every other session. */ }
    }
    public async Task DeleteConversationAsync(string id, CancellationToken ct) =>
        await RequestAsync(HttpMethod.Delete, SessionPath(id), null, ct);

    private void StopProcess()
    {
        _http?.Dispose();
        _http = null;
        if (_process is { } process)
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                process.WaitForExit(2000);
            }
            catch { }
            process.Dispose();
            _process = null;
        }
    }
    public void Dispose() { if (_disposed) return; _disposed = true; StopProcess(); }
}

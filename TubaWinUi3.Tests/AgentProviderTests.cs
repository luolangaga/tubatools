using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using TubaWinUi3.Services;
using TubaWinUi3.Services.AiAgent;

namespace TubaWinUi3.Tests;

[Collection("GlobalConfigTests")]
public sealed class AgentProviderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "tuba-agent-provider-" + Guid.NewGuid().ToString("N"));
    private readonly string? _original = ConfigManager.DataDirectoryOverride;
    public AgentProviderTests() { ConfigManager.DataDirectoryOverride = _root; Directory.CreateDirectory(_root); }
    public void Dispose() { ConfigManager.DataDirectoryOverride = _original; Directory.Delete(_root, true); }
    private static AgentProviderDefinition Provider(string url = "https://example.com/v1") => new("tuba-custom-test", "My API", "chat", url, "fixture-secret", ["fixture-model"]);

    [Fact]
    public void ProviderKey_IsEncryptedAtRest_AndOnlyReferencedInComponentConfig()
    {
        var provider = Provider(); AgentProviderStore.Save([provider]);
        var file = File.ReadAllText(Path.Combine(AiComponentService.Root, "providers.json"));
        Assert.DoesNotContain(provider.ApiKey, file); Assert.Contains("dpapi:v1:", file);
        Assert.Equal(provider.ApiKey, Assert.Single(AgentProviderStore.Read()).ApiKey);
        var config = JsonSerializer.Serialize(AgentProviderStore.Configuration([provider]));
        Assert.DoesNotContain(provider.ApiKey, config); Assert.Contains(provider.EnvironmentKey, config);
        var start = AiComponentService.StartInfo("fixture.exe", _root);
        Assert.Equal(provider.ApiKey, start.Environment[provider.EnvironmentKey]);
        AgentProviderStore.Save([]); Assert.Empty(AgentProviderStore.Read());
    }
    [Theory]
    [InlineData("file:///C:/secret")]
    [InlineData("https://user:password@example.com/v1")]
    [InlineData("https://example.com/v1?key=secret")]
    public void InvalidEndpoint_IsRejected(string url) => Assert.Throws<ArgumentException>(() => AgentProviderStore.Validate(Provider(url)));

    [Fact]
    public async Task ModelDiscovery_UsesConfiguredCredentialAndEndpoint()
    {
        await using var server = new ProviderFixture();
        var models = await AgentProviderStore.DiscoverModelsAsync(server.BaseUrl, "chat", "fixture-secret", CancellationToken.None);
        Assert.Equal(["fixture-model"], models); Assert.True(server.Authenticated);
    }

    [AiComponentFact]
    public async Task CustomProvider_RealComponent_ListsModelAndStreamsReply_WithoutExternalService()
    {
        await using var server = new ProviderFixture();
        var provider = Provider(server.BaseUrl); AgentProviderStore.Save([provider]);
        Directory.CreateDirectory(Path.GetDirectoryName(AiComponentService.Executable)!);
        File.Copy(Environment.GetEnvironmentVariable("TUBA_AI_SMOKE_EXE")!, AiComponentService.Executable);
        using var client = new AgentClient(); using var budget = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await client.StartAsync(budget.Token);
        var model = Assert.Single((await client.GetModelsAsync(budget.Token)).Where(m => m.ProviderId == provider.Id));
        Assert.Equal("fixture-model", model.Id);
        var session = await client.CreateConversationAsync(model, "Local provider test", budget.Token);
        var phases = new List<string>(); IReadOnlyList<AgentMessage> messages = [];
        await client.RunAsync(session, model, "Reply OK without tools.", m => messages = m, _ => Task.FromResult(false), budget.Token, progress: p => phases.Add(p.Phase));
        Assert.Contains(messages, m => m.Role == "assistant" && m.Text.Contains("LOCAL_OK"));
        Assert.Contains("responding", phases); Assert.True(server.Authenticated);
        Assert.DoesNotContain("fixture-secret", File.ReadAllText(Path.Combine(AgentWorkspace.DirectoryPath, "opencode.json")));
        await client.DeleteConversationAsync(session, budget.Token);
    }

    [AiComponentFact]
    public async Task ImageInput_RealComponent_SendsPixelsToProvider_AndPreservesHistory()
    {
        await using var server = new ProviderFixture();
        var provider = Provider(server.BaseUrl) with { SupportsImages = true }; AgentProviderStore.Save([provider]);
        Directory.CreateDirectory(Path.GetDirectoryName(AiComponentService.Executable)!);
        File.Copy(Environment.GetEnvironmentVariable("TUBA_AI_SMOKE_EXE")!, AiComponentService.Executable);
        var image = Path.Combine(_root, "剪贴板 图片.png");
        using (var bitmap = new System.Drawing.Bitmap(16, 16))
        {
            using var drawing = System.Drawing.Graphics.FromImage(bitmap);
            drawing.Clear(System.Drawing.Color.CornflowerBlue);
            bitmap.Save(image, System.Drawing.Imaging.ImageFormat.Png);
        }
        using var client = new AgentClient(); using var budget = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await client.StartAsync(budget.Token);
        var model = Assert.Single(await client.GetModelsAsync(budget.Token), m => m.ProviderId == provider.Id);
        Assert.True(model.SupportsImages);
        var session = await client.CreateConversationAsync(model, "Image attachment test", budget.Token);
        IReadOnlyList<AgentMessage> messages = [];
        var document = Path.Combine(_root, "报告.txt"); await File.WriteAllTextAsync(document, "LOCAL_FILE_REFERENCE", budget.Token);
        await client.RunAsync(session, model, "Describe the attached image without tools.", m => messages = m, _ => Task.FromResult(false), budget.Token,
            attachments: [new(image, "剪贴板 图片.png", "image/png"), new(document, "报告.txt", "")]);
        Assert.Contains("image_url", server.LastRequest);
        Assert.Contains("data:image/png;base64,", server.LastRequest);
        var user = Assert.Single(messages, m => m.Role == "user");
        Assert.Equal(2, user.Attachments!.Count);
        var attachment = Assert.Single(user.Attachments!, a => a.IsImage);
        Assert.Equal(image, attachment.Path); Assert.Equal("剪贴板 图片.png", attachment.Name);
        Assert.Equal(document, Assert.Single(user.Attachments!, a => !a.IsImage).Path);
        Assert.Contains(messages, m => m.Role == "assistant" && m.Text.Contains("LOCAL_OK"));
        await client.DeleteConversationAsync(session, budget.Token);
    }

    [AiComponentFact]
    public async Task CustomProvider_LongStreamBeyondOldThreeMinuteLimit_Completes()
    {
        await using var server = new ProviderFixture(longStream: true);
        var provider = Provider(server.BaseUrl); AgentProviderStore.Save([provider]);
        Directory.CreateDirectory(Path.GetDirectoryName(AiComponentService.Executable)!);
        File.Copy(Environment.GetEnvironmentVariable("TUBA_AI_SMOKE_EXE")!, AiComponentService.Executable);
        using var client = new AgentClient(); using var budget = new CancellationTokenSource(TimeSpan.FromMinutes(5));
        await client.StartAsync(budget.Token);
        var model = Assert.Single(await client.GetModelsAsync(budget.Token), m => m.ProviderId == provider.Id);
        var session = await client.CreateConversationAsync(model, "Long local stream", budget.Token);
        IReadOnlyList<AgentMessage> messages = []; var phases = new HashSet<string>(); var watch = System.Diagnostics.Stopwatch.StartNew();
        await client.RunAsync(session, model, "Reply OK without tools.", m => messages = m, _ => Task.FromResult(false), budget.Token, progress: p => phases.Add(p.Phase));
        Assert.True(watch.Elapsed > TimeSpan.FromMinutes(3));
        Assert.Contains(messages, m => m.Role == "assistant" && m.Text.Contains("LOCAL_OK"));
        Assert.DoesNotContain(messages, m => m.Error is not null || m.Text.Contains("PRIVATE_REASONING_FIXTURE"));
        Assert.True(client.IsReady);
        Console.WriteLine($"Long local stream completed in {watch.Elapsed.TotalSeconds:F0}s; phases: {string.Join(',', phases)}");
        await client.DeleteConversationAsync(session, budget.Token);
    }

    private sealed class ProviderFixture : IAsyncDisposable
    {
        private readonly HttpListener _listener = new(); private readonly CancellationTokenSource _stop = new(); private readonly Task _loop; private readonly bool _longStream;
        public string BaseUrl { get; }
        public bool Authenticated { get; private set; }
        public string LastRequest { get; private set; } = "";
        public ProviderFixture(bool longStream = false)
        {
            _longStream = longStream;
            var portProbe = new TcpListener(IPAddress.Loopback, 0); portProbe.Start(); var port = ((IPEndPoint)portProbe.LocalEndpoint).Port; portProbe.Stop();
            BaseUrl = $"http://127.0.0.1:{port}/v1"; _listener.Prefixes.Add($"http://127.0.0.1:{port}/"); _listener.Start(); _loop = ServeAsync();
        }
        private async Task ServeAsync()
        {
            try { while (!_stop.IsCancellationRequested) { var context = await _listener.GetContextAsync().WaitAsync(_stop.Token); await HandleAsync(context); } }
            catch (Exception ex) when (ex is OperationCanceledException or HttpListenerException or ObjectDisposedException) { }
        }
        private async Task HandleAsync(HttpListenerContext context)
        {
            try
            {
                Authenticated |= context.Request.Headers["Authorization"] == "Bearer fixture-secret";
                if (context.Request.Url!.AbsolutePath.EndsWith("/models"))
                {
                    var bytes = Encoding.UTF8.GetBytes("""{"object":"list","data":[{"id":"fixture-model","object":"model"}]}""");
                    context.Response.ContentType = "application/json"; await context.Response.OutputStream.WriteAsync(bytes); return;
                }
                if (!context.Request.Url.AbsolutePath.EndsWith("/chat/completions")) { context.Response.StatusCode = 404; return; }
                using (var body = new StreamReader(context.Request.InputStream)) LastRequest = await body.ReadToEndAsync();
                context.Response.ContentType = "text/event-stream"; context.Response.SendChunked = true;
                if (_longStream)
                    for (var i = 0; i < 28; i++)
                    {
                        var reasoning = JsonSerializer.Serialize(new { id = "chatcmpl-local", @object = "chat.completion.chunk", created = 1794000000, model = "fixture-model", choices = new[] { new { index = 0, delta = new { reasoning_content = "PRIVATE_REASONING_FIXTURE " }, finish_reason = (string?)null } } });
                        await context.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes("data: " + reasoning + "\n\n")); await context.Response.OutputStream.FlushAsync(); await Task.Delay(7000, _stop.Token);
                    }
                foreach (var text in new[] { "LOCAL_", "OK" })
                {
                    var chunk = JsonSerializer.Serialize(new { id = "chatcmpl-local", @object = "chat.completion.chunk", created = 1794000000, model = "fixture-model", choices = new[] { new { index = 0, delta = new { role = "assistant", content = text }, finish_reason = (string?)null } } });
                    await context.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes("data: " + chunk + "\n\n")); await context.Response.OutputStream.FlushAsync(); await Task.Delay(1500, _stop.Token);
                }
                var finish = """{"id":"chatcmpl-local","object":"chat.completion.chunk","created":1794000000,"model":"fixture-model","choices":[{"index":0,"delta":{},"finish_reason":"stop"}],"usage":{"prompt_tokens":1,"completion_tokens":2,"total_tokens":3}}""";
                await context.Response.OutputStream.WriteAsync(Encoding.UTF8.GetBytes("data: " + finish + "\n\ndata: [DONE]\n\n"));
            }
            finally { context.Response.Close(); }
        }
        public async ValueTask DisposeAsync() { _stop.Cancel(); _listener.Close(); await _loop; _stop.Dispose(); }
    }
}

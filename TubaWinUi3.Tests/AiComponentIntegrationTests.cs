using TubaWinUi3.Services;
using TubaWinUi3.Services.AiAgent;

namespace TubaWinUi3.Tests;

public sealed class AiComponentFactAttribute : FactAttribute
{
    public AiComponentFactAttribute()
    {
        if (!File.Exists(Environment.GetEnvironmentVariable("TUBA_AI_SMOKE_EXE")))
            Skip = "Set TUBA_AI_SMOKE_EXE to a trusted component executable to run the integration smoke test.";
    }
}

[Collection("GlobalConfigTests")]
public sealed class AiComponentIntegrationTests
{
    [AiComponentFact]
    public async Task PrivateComponent_ModelsSessionSkillsAndFreeReply()
    {
        var original = ConfigManager.DataDirectoryOverride;
        var root = Path.Combine(Path.GetTempPath(), "tuba-ai-smoke-" + Guid.NewGuid().ToString("N"));
        ConfigManager.DataDirectoryOverride = root;
        using var client = new AgentClient();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(AiComponentService.Executable)!);
            File.Copy(Environment.GetEnvironmentVariable("TUBA_AI_SMOKE_EXE")!, AiComponentService.Executable);
            using var budget = new CancellationTokenSource(TimeSpan.FromSeconds(180));
            await client.StartAsync(budget.Token);
            var models = await client.GetModelsAsync(budget.Token);
            Assert.NotEmpty(models);
            var model = AgentProtocol.SelectDefault(models, null);
            Assert.NotNull(model);
            Assert.True(model.IsFree);
            var skills = await client.RequestAsync(System.Net.Http.HttpMethod.Get, "api/skill?location%5Bdirectory%5D=" + Uri.EscapeDataString(AgentWorkspace.DirectoryPath), null, budget.Token);
            Assert.Contains(AgentProtocol.Data(skills).EnumerateArray(), s => AgentProtocol.Text(s, "id") == "tuba-plugin");
            Assert.Contains(AgentProtocol.Data(skills).EnumerateArray(), s => AgentProtocol.Text(s, "id") == "tuba-toolbox");
            Assert.True(File.Exists(Path.Combine(AgentWorkspace.DirectoryPath, ".opencode", "skills", "tuba-toolbox", "scripts", "hardware.ps1")));
            Assert.True(new FileInfo(Path.Combine(AgentWorkspace.DirectoryPath, ".opencode", "skills", "tuba-toolbox", "references", "gui-tools.md")).Length > 1000,
                "The imported skill must include the real GUI tool inventory.");
            var maintenance = await client.CreateConversationAsync(model, "Maintenance test", budget.Token);
            Assert.Contains(await client.GetConversationsAsync(budget.Token), s => s.Id == maintenance);
            // Real form API: submit one answer and cancel another, without a model dependency.
            var created = AgentProtocol.Data(await client.RequestAsync(System.Net.Http.HttpMethod.Post, "api/session/" + maintenance + "/form", new
            { title = "Plugin consent", fields = new[] { new { key = "plugin", type = "string", required = true, options = new[] { new { value = "no", label = "Only solve this problem" } } } } }, budget.Token));
            var formId = AgentProtocol.Text(created, "id");
            Assert.NotEmpty(formId);
            await client.RequestAsync(System.Net.Http.HttpMethod.Post, "api/session/" + maintenance + "/form/" + formId + "/reply", new { answer = new { plugin = "no" } }, budget.Token);
            Assert.Empty(AgentProtocol.Data(await client.RequestAsync(System.Net.Http.HttpMethod.Get, "api/session/" + maintenance + "/form", null, budget.Token)).EnumerateArray());
            var session = await client.CreateConversationAsync(model, "Integration smoke test", budget.Token);
            var sessionInfo = AgentProtocol.Data(await client.RequestAsync(System.Net.Http.HttpMethod.Get, "api/session/" + session, null, budget.Token));
            Assert.Equal("tuba", AgentProtocol.Text(sessionInfo, "agent"));
            var initialRule = Assert.Single(sessionInfo.GetProperty("permissions").EnumerateArray());
            Assert.Equal("*", AgentProtocol.Text(initialRule, "action"));
            Assert.Equal("allow", AgentProtocol.Text(initialRule, "effect"));
            Assert.Contains(await client.GetConversationsAsync(budget.Token), s => s.Id == session);
            Assert.Equal((await client.GetConversationsAsync(budget.Token)).Select(s => s.Id),
                (await client.GetConversationsAsync(budget.Token, pageSize: 1)).Select(s => s.Id));
            IReadOnlyList<AgentMessage> transcript = [];
            // 免费公共服务可能限流；只为无工具、无副作用的联网探针尝试少量其他免费模型。
            foreach (var candidate in new[] { model }.Concat(models.Where(m => m.IsFree && m.Key != model.Key).Take(2)))
            {
                Console.WriteLine("Checking free model: " + candidate.Name);
                try { await client.RunAsync(session, candidate, "Reply with exactly OK. Do not use tools or read files.", m => transcript = m, _ => Task.FromResult(false), budget.Token); }
                catch (AgentModelFailureException) { }
                if (transcript.LastOrDefault(m => m.Role == "assistant") is { Error: null } last && last.Text.Contains("OK", StringComparison.Ordinal)) break;
            }
            Assert.Contains(transcript, m => m.Role == "assistant" && m.Text.Contains("OK", StringComparison.Ordinal));
            // 覆盖用户实际使用的「加载技能 → 读取文档」工具轮次，不能只测无工具回复。
            var marker = "TOOL_STREAM_OK_" + Guid.NewGuid().ToString("N");
            var fixture = Path.Combine(AgentWorkspace.DirectoryPath, "streaming-probe.txt");
            await File.WriteAllTextAsync(fixture, marker, budget.Token);
            var toolModel = models.FirstOrDefault(m => m.IsFree && m.Id.Contains("glm", StringComparison.OrdinalIgnoreCase)) ?? model;
            await client.RunAsync(session, toolModel,
                "Use the tuba-plugin skill once. Then use the read tool to read streaming-probe.txt in the current workspace and reply with its exact contents. Do not edit files, run shell commands, or delegate tasks.",
                m => transcript = m, _ => Task.FromResult(false), budget.Token);
            Assert.Contains(transcript, m => m.Role == "assistant" && m.Error is null && m.Text.Contains(marker, StringComparison.Ordinal));
            var raw = await client.RequestAsync(System.Net.Http.HttpMethod.Get, "api/session/" + session + "/message?limit=100", null, budget.Token);
            var tools = AgentProtocol.Data(raw).EnumerateArray()
                .Where(m => AgentProtocol.Text(m, "type") == "assistant")
                .SelectMany(m => m.GetProperty("content").EnumerateArray())
                .Where(p => AgentProtocol.Text(p, "type") == "tool" && AgentProtocol.Text(p.GetProperty("state"), "status") == "completed")
                .Select(p => AgentProtocol.Text(p, "name")).ToList();
            Assert.Contains("skill", tools);
            Assert.Contains("read", tools);
            var approvals = 0;
            await client.RunAsync(session, toolModel,
                "Use the bash tool to execute this exact command without extra punctuation:\ndotnet --version\nDo not run any other command. Report the result.",
                m => transcript = m, permission => { approvals++; return Task.FromResult(false); }, budget.Token);
            Assert.Equal(0, approvals); // The default must really execute without a permission callback.
            var shell = transcript.SelectMany(m => m.Content ?? []).Select(p => p.Operation)
                .LastOrDefault(o => o?.Kind == "command" && o.Command.Trim() == "dotnet --version");
            Assert.NotNull(shell);
            Assert.Equal(0, shell.ExitCode);
            // 宿主执行结果可作为观察写回会话，不能伪装成用户发言。
            await client.RecordPluginResultsAsync(session, [new("test-only.nupkg", true, "Integration test observation only: no plugin was installed.")], budget.Token);
            await client.ResumeWithPluginFeedbackAsync(session, model, "This is an integration test observation. Reply with exactly OBSERVATION_OK. Do not use tools.", m => transcript = m, _ => Task.FromResult(false), budget.Token);
            Assert.Contains(transcript, m => m.Role == "assistant" && m.Error is null && m.Text.Contains("OBSERVATION_OK", StringComparison.Ordinal));
            var previousIds = (await client.GetMessagesAsync(session, budget.Token)).Select(m => m.Id).ToArray();
            Assert.Equal(previousIds, (await client.GetMessagesAsync(session, budget.Token)).Select(m => m.Id));
            Assert.Equal(previousIds, (await client.GetAllMessagesAsync(session, budget.Token, pageSize: 2)).Select(m => m.Id));
            await client.RenameConversationAsync(session, "Renamed native session", budget.Token);
            Assert.Contains(await client.GetConversationsAsync(budget.Token), s => s.Id == session && s.Title == "Renamed native session");
            var questions = 0;
            var phases = new List<string>();
            // Simulate a conversation saved by the previous client, which denied questions.
            await client.RequestAsync(System.Net.Http.HttpMethod.Patch, "api/session/" + maintenance, new
            { permissions = new[] { new { action = "*", resource = "*", effect = "ask" }, new { action = "question", resource = "*", effect = "deny" }, new { action = "skill", resource = "*", effect = "deny" }, new { action = "read", resource = "*", effect = "allow" } } }, budget.Token);
            await client.RunAsync(maintenance, toolModel,
                "This is a maintenance interaction test. First load the tuba-toolbox skill without executing commands. Then use the question tool to ask exactly one question: Should this solution become a plugin? Offer two choices: Create plugin / Only solve this problem. After the user answers, use the read tool to read streaming-probe.txt, then reply with its exact contents. Do not read the file before asking the question. Do not edit files, run commands, or create plugins.",
                m => transcript = m, _ => Task.FromResult(false), budget.Token, async (question, ct) =>
                {
                    questions++;
                    await Task.Delay(TimeSpan.FromSeconds(5), ct); // A human answer must not need an immediate callback.
                    var answers = question.Fields.Where(f => f.Kind != "external").ToDictionary(f => f.Key, f => (object)(f.Options.LastOrDefault()?.Value ?? "Only solve this problem"));
                    return answers;
                }, p => phases.Add(p.Phase));
            Assert.True(questions > 0, "The model must ask through the native form callback.");
            Assert.Contains(transcript.SelectMany(m => m.Content ?? []), p => p.Operation is { Name: "question", Status: "completed" });
            Assert.Contains(transcript.SelectMany(m => m.Content ?? []), p => p.Operation is { Name: "skill", Status: "completed", Target: "tuba-toolbox" });
            Assert.DoesNotContain(transcript.SelectMany(m => m.Content ?? []), p => p.Operation?.Kind is "edit" or "command");
            Assert.Contains("question", phases);
            var migrated = AgentProtocol.Data(await client.RequestAsync(System.Net.Http.HttpMethod.Get, "api/session/" + maintenance, null, budget.Token));
            Assert.Equal("allow", AgentProtocol.Text(Assert.Single(migrated.GetProperty("permissions").EnumerateArray()), "effect"));
            Assert.Contains(transcript, m => m.Role == "assistant" && m.Error is null && m.Text.Contains(marker, StringComparison.Ordinal));
            await client.DeleteConversationAsync(maintenance, budget.Token);
            await client.DeleteConversationAsync(session, budget.Token);
        }
        finally
        {
            client.Dispose();
            ConfigManager.DataDirectoryOverride = original;
            for (var attempt = 0; Directory.Exists(root) && attempt < 10; attempt++)
            {
                try { Directory.Delete(root, true); }
                catch (IOException) when (attempt < 9) { await Task.Delay(100); }
                catch (UnauthorizedAccessException) when (attempt < 9) { await Task.Delay(100); }
            }
        }
    }
}

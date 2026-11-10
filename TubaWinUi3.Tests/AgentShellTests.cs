using System.Text.Json;
using TubaWinUi3.Services.AiAgent;

namespace TubaWinUi3.Tests;

public sealed class AgentShellTests
{
    [Fact]
    public void DefaultPolicy_AllowsEveryActionAndResource_WithoutHiddenDenies()
    {
        var rules = AgentPermissionPolicy.Rules(AgentPermissionMode.FullAccess);
        var rule = Assert.Single(rules);
        Assert.Equal(("*", "*", "allow"), (rule.action, rule.resource, rule.effect));
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(rules));
        Assert.Equal("allow", json.RootElement[0].GetProperty("effect").GetString());
        Assert.DoesNotContain(rules, r => r.effect is "ask" or "deny");
    }
    [Fact]
    public void AskPolicy_LeavesCommandsAndEditsForTheNativeQuestionFlow()
    {
        var rules = AgentPermissionPolicy.Rules(AgentPermissionMode.Ask);
        Assert.Equal("ask", rules[0].effect);
        Assert.DoesNotContain(rules, r => r.effect == "deny");
        Assert.Contains(rules, r => r.action == "question" && r.effect == "allow");
    }
    [Fact]
    public void StreamingTail_MergesUpdatesWithoutDiscardingEarlierConversation()
    {
        AgentMessage[] original = [new("old-user", "user", "Earlier task", ""), new("stream", "assistant", "partial", "")];
        var result = AgentHistory.Merge(original, [new("stream", "assistant", "complete", ""), new("next", "assistant", "Next step", "")]);
        Assert.Equal(new[] { "old-user", "stream", "next" }, result.Select(m => m.Id));
        Assert.Equal("complete", result[1].Text);
        Assert.Equal("partial", original[1].Text);
    }
    [Fact]
    public void MarkdownExport_PreservesCommandsAndDiffs_WithoutBreakingNestedFences()
    {
        AgentMessage[] messages = [new("u", "user", "Build this", ""), new("a", "assistant", "", "", Content:
            [new("t", "", new("t", "edit", "edit", "completed", "Plugin.cs", "", "```text\noutput\n```", "- old\n+ new"))])];
        var output = AgentHistory.ToMarkdown("Session", messages);
        Assert.Contains("Build this", output);
        Assert.Contains("- old\n+ new", output);
        Assert.Contains("````\n```text\noutput\n```\n````", output);
    }
}

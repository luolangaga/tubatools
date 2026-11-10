using System.Text.Json;
using TubaWinUi3.Services.AiAgent;

namespace TubaWinUi3.Tests;

public sealed class AgentRunProgressTests
{
    [Fact]
    public void Reasoning_ExposesOnlyPhase_AndDetectsContinuedStreaming()
    {
        using var first = JsonDocument.Parse("""[{"id":"a","type":"assistant","content":[{"type":"reasoning","text":"private thought"}]}]""");
        using var second = JsonDocument.Parse("""[{"id":"a","type":"assistant","content":[{"type":"reasoning","text":"private thought updated"}]}]""");
        var messages = AgentProtocol.ParseMessages(first.RootElement);
        Assert.True(messages[0].Thinking);
        Assert.Empty(messages[0].Text);
        Assert.Empty(messages[0].Content!);
        var monitor = new AgentRunMonitor();
        Assert.Equal("thinking", monitor.Observe(messages, TimeSpan.Zero).Phase);
        Assert.Equal("thinking", monitor.Observe(AgentProtocol.ParseMessages(second.RootElement), TimeSpan.FromMinutes(2)).Phase);
        Assert.Equal("thinking", monitor.Observe(AgentProtocol.ParseMessages(second.RootElement), TimeSpan.FromMinutes(4)).Phase);
    }
    [Fact]
    public void SilentSnapshots_DoNotAbortModel_AndHumanWaitIsExcluded()
    {
        var monitor = new AgentRunMonitor();
        AgentMessage[] messages = [new("a", "assistant", "", "", Revision: 1)];
        monitor.Observe(messages, TimeSpan.Zero);
        monitor.Resume(TimeSpan.FromMinutes(10)); // Time spent in a native question card.
        monitor.Observe(messages, TimeSpan.FromMinutes(12));
        Assert.Equal("stalled", monitor.Observe(messages, TimeSpan.FromMinutes(13)).Phase);
        Assert.Equal("stalled", monitor.Observe(messages, TimeSpan.FromMinutes(45)).Phase);
        Assert.Equal("thinking", monitor.Observe(messages, TimeSpan.FromMinutes(46), new(1, "thinking")).Phase);
    }
    [Fact]
    public void RunningTool_IsVisible_AndUsesItsOwnTimeout()
    {
        var monitor = new AgentRunMonitor();
        AgentMessage[] messages = [new("a", "assistant", "", "", Content: [new("t", "", new("t", "shell", "command", "running", "Build", "dotnet pack", "", ""))])];
        Assert.Equal("tool", monitor.Observe(messages, TimeSpan.Zero).Phase);
        Assert.Equal("tool", monitor.Observe(messages, TimeSpan.FromMinutes(5)).Phase);
    }
}

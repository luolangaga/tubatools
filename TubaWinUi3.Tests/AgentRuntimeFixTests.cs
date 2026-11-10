using System.Text.Json;
using TubaWinUi3.Services.AiAgent;

namespace TubaWinUi3.Tests;

public sealed class AgentRuntimeFixTests
{
    [Fact]
    public void HiddenReasoningEvents_KeepLongRunsAlive_WithoutExposingText()
    {
        var signals = new AgentRunSignals(); var monitor = new AgentRunMonitor();
        AgentMessage[] frozen = [new("a", "assistant", "", "", Revision: 1)];
        monitor.Observe(frozen, TimeSpan.Zero);
        for (var minute = 1; minute <= 12; minute++)
        {
            using var json = JsonDocument.Parse("""{"type":"session.reasoning.delta","data":{"sessionID":"mine","delta":"private text must not be exposed"}}""");
            signals.Observe(json.RootElement, "mine");
            var progress = monitor.Observe(frozen, TimeSpan.FromMinutes(minute), signals.Read());
            Assert.Equal("thinking", progress.Phase); Assert.Empty(progress.Detail);
        }
        Assert.Equal("stalled", monitor.Observe(frozen, TimeSpan.FromMinutes(20), signals.Read()).Phase);
    }
    [Fact]
    public void RetryCompactionAndOtherSessionEvents_AreHandledSeparately()
    {
        var signals = new AgentRunSignals();
        void Event(string type, string id)
        { signals.Observe(JsonSerializer.SerializeToElement(new { type, data = new { sessionID = id, attempt = 3 } }), "mine"); }
        Event("session.reasoning.delta", "other"); Assert.Equal(0, signals.Read().Revision);
        Event("session.compaction.started", "mine"); Assert.Equal("compacting", signals.Read().Phase);
        Event("session.retry.scheduled", "mine"); Assert.Equal("retrying", signals.Read().Phase);
        Event("session.text.delta", "mine"); Assert.Equal("responding", signals.Read().Phase);
    }
    [Fact]
    public void EditMetadata_UsesActualPerFileCountsAndPatches()
    {
        using var json = JsonDocument.Parse("""{"id":"edit","name":"edit","state":{"status":"completed","input":{"path":"one.cs"},"metadata":{"files":[{"file":"one.cs","patch":"@@ -1 +1 @@\n-old\n+new","additions":27,"deletions":4},{"file":"two.cs","patch":"@@ -0,0 +1 @@\n+new","additions":3,"deletions":0}]}}}""");
        var operation = AgentOperations.Parse(json.RootElement, "fallback");
        Assert.Equal(30, operation.Additions); Assert.Equal(4, operation.Deletions);
        Assert.Equal(2, operation.Files!.Count); Assert.Contains("-old", operation.Files[0].Diff);
    }
    [Fact]
    public void DiffCounts_IgnoreHeaders_AndDoNotInventWriteChanges()
    {
        Assert.Equal((2, 1), AgentOperations.CountDiff("--- a\n+++ b\n@@ -1 +1,2 @@\n-a\n+b\n+c"));
        Assert.Equal(((int?)null, (int?)null), AgentOperations.CountDiff("+ a plain file that starts with plus"));
        using var json = JsonDocument.Parse("""{"name":"write","state":{"status":"completed","input":{"filePath":"x.txt","content":"new text"}}}""");
        var operation = AgentOperations.Parse(json.RootElement, "write"); Assert.Null(operation.Additions); Assert.Null(operation.Deletions);
    }
}

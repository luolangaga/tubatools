using System.Text.Json;

namespace TubaWinUi3.Services.AiAgent;

internal sealed record AgentSignal(long Revision, string Phase, string Detail = "");

/// <summary>Only phase metadata crosses into the UI; reasoning deltas are discarded.</summary>
internal sealed class AgentRunSignals
{
    private readonly object _gate = new();
    private AgentSignal _signal = new(0, "waiting");
    internal AgentSignal Read() { lock (_gate) return _signal; }
    internal void Observe(JsonElement root, string sessionId)
    {
        if (root.ValueKind != JsonValueKind.Object) return;
        if (root.TryGetProperty("payload", out var payload)) root = payload;
        if (root.ValueKind != JsonValueKind.Object || !root.TryGetProperty("data", out var data) || AgentProtocol.Text(data, "sessionID") != sessionId) return;
        var type = AgentProtocol.Text(root, "type");
        var phase = type switch
        {
            "session.reasoning.started" or "session.reasoning.delta" => "thinking",
            "session.text.started" or "session.text.delta" => "responding",
            "session.tool.input.started" or "session.tool.input.delta" => "preparing-tool",
            "session.tool.called" or "session.tool.progress" => "tool",
            "session.compaction.started" or "session.compaction.delta" => "compacting",
            "session.retry.scheduled" => "retrying",
            "session.step.started" or "session.step.streamed" or "session.compaction.ended" or "session.tool.success" or "session.tool.failed" => "waiting",
            _ => null
        };
        if (phase is null) return;
        var detail = phase == "retrying" && data.TryGetProperty("attempt", out var attempt) && attempt.TryGetInt32(out var n)
            ? string.Format(AgentText.L("RetryAttempt", "服务正在重试 · 第 {0} 次"), n) : "";
        lock (_gate) _signal = new(_signal.Revision + 1, phase, detail);
    }
}
